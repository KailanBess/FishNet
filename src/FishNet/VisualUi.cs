using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private readonly VisualLibrary visualLibrary = new();
    private VisualItem? visualCurrent;
    private VisualFiles.OpenedPdf? visualPdf;
    private uint visualPageIndex;
    private string? visualAlbum;
    private bool visualScreenshots, visualBusy;
    private CancellationTokenSource? visualCancellation;
    private Image? visualImage;
    private double visualZoom = 100;
    private readonly SemaphoreSlim visualRenderGate = new(1, 1);
    private readonly DispatcherTimer slideshow = new() { Interval = TimeSpan.FromSeconds(5) };

    private void StopVisualWork()
    { slideshow.Stop(); visualCancellation?.Cancel(); visualCancellation?.Dispose(); visualCancellation = null; visualPdf?.Dispose(); visualPdf = null; }

    private void RenderVisualLibrary()
    {
        MediaPane.Visibility = Visibility.Visible;
        MediaBody.Children.Clear();
        if (visualCurrent is not null) { RenderVisualViewer(); return; }
        if (visualLibrary.Warning is not null) MediaBody.Children.Add(new TextBlock { Text = visualLibrary.Warning, TextWrapping = TextWrapping.Wrap });
        var import = new Button { Content = "Import " + (mediaPage == "Photos" ? "photo" : "PDF") + " folder", IsEnabled = visualLibrary.Writable && !visualBusy };
        import.Click += async (_, _) => await RunVisualEdit(async () =>
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitPicker(picker);
            var folder = await picker.PickSingleFolderAsync(); if (folder is null || closed) return;
            var kind = mediaPage;
            var paths = await Task.Run(() => Directory.EnumerateFiles(folder.Path, "*", new EnumerationOptions
            { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = System.IO.FileAttributes.ReparsePoint }).Where(p => VisualLibrary.Kind(p) == kind).Take(10001).ToArray());
            if (paths.Length > 10000) throw new InvalidDataException("Choose a smaller folder; this library supports 10,000 records.");
            await visualLibrary.ImportAsync(paths);
        });
        MediaBody.Children.Add(import);
        var albums = new ComboBox { Header = "Album", ItemsSource = new[] { "All albums" }.Concat(visualLibrary.State.Albums).ToArray(),
            SelectedIndex = visualAlbum is null ? 0 : visualLibrary.State.Albums.IndexOf(visualAlbum) + 1 };
        albums.SelectionChanged += (_, _) => { visualAlbum = albums.SelectedIndex <= 0 ? null : visualLibrary.State.Albums[albums.SelectedIndex - 1]; RenderVisualLibrary(); };
        MediaBody.Children.Add(albums);
        var createAlbum = new Button { Content = "Create album", IsEnabled = visualLibrary.Writable && !visualBusy };
        createAlbum.Click += async (_, _) => await RunVisualEdit(async () =>
        {
            var name = new TextBox { Header = "Album name", MaxLength = 80 };
            var dialog = Dialog("New album", name);
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await visualLibrary.AddAlbumAsync(name.Text);
        });
        MediaBody.Children.Add(createAlbum);
        if (visualAlbum is not null)
        {
            var remove = new Button { Content = "Remove album (keep its files and records)", IsEnabled = visualLibrary.Writable && !visualBusy };
            remove.Click += async (_, _) => await RunVisualEdit(async () => { await visualLibrary.RemoveAlbumAsync(visualAlbum); visualAlbum = null; });
            MediaBody.Children.Add(remove);
        }
        if (mediaPage == "Photos")
        {
            var screenshots = new CheckBox { Content = "Imported screenshots only", IsChecked = visualScreenshots };
            screenshots.Checked += (_, _) => { visualScreenshots = true; RenderVisualLibrary(); };
            screenshots.Unchecked += (_, _) => { visualScreenshots = false; RenderVisualLibrary(); };
            MediaBody.Children.Add(screenshots);
        }
        var entries = VisualEntries();
        if (entries.Count == 0) MediaBody.Children.Add(new TextBlock { Text = "No matching files. Open a photo/PDF or import a folder to begin.", TextWrapping = TextWrapping.Wrap });
        if (entries.Count > 150) MediaBody.Children.Add(new TextBlock { Text = "Showing 150 results. Search or select an album to narrow the list." });
        foreach (var entry in entries.Take(150))
        {
            var row = new StackPanel { Spacing = 6 };
            var thumbnail = new Image { Width = 160, Height = 120, Stretch = Stretch.Uniform };
            var open = new Button { Content = entry.Name, IsEnabled = !visualBusy, HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += async (_, _) => await OpenVisualAsync(entry);
            row.Children.Add(thumbnail); row.Children.Add(open);
            row.Children.Add(new TextBlock { Text = entry.Album.Length == 0 ? entry.Kind : entry.Album, Opacity = .65 });
            if (!File.Exists(entry.Path))
            {
                var relink = new Button { Content = "Missing file · Relink", IsEnabled = visualLibrary.Writable && !visualBusy };
                relink.Click += async (_, _) => await RunVisualEdit(async () =>
                {
                    var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(Path.GetExtension(entry.Path)); InitPicker(picker);
                    var replacement = await picker.PickSingleFileAsync(); if (replacement is not null) await visualLibrary.RelinkAsync(entry.Path, replacement.Path);
                }); row.Children.Add(relink);
            }
            MediaBody.Children.Add(row);
            _ = LoadVisualThumbnail(entry, thumbnail, artworkCancellation!.Token);
        }
    }

    private List<VisualItem> VisualEntries() => visualLibrary.State.Items.Where(i => i.Kind == mediaPage &&
        (visualAlbum is null || i.Album == visualAlbum) && (!visualScreenshots || mediaPage != "Photos" || i.Screenshot) &&
        i.Name.Contains(MediaSearch.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Name).ToList();
    private void InitPicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
    private async Task RunVisualEdit(Func<Task> edit)
    {
        if (visualBusy) return; visualBusy = true;
        try { await edit(); }
        catch (Exception ex) { if (!closed) ShowError("Photo/document operation stopped: " + ex.Message); }
        finally { visualBusy = false; if (!closed && mediaPage is "Photos" or "Documents") RenderVisualLibrary(); }
    }
    private static async Task LoadVisualThumbnail(VisualItem item, Image image, CancellationToken token)
    {
        bool held = false;
        try
        {
            await artworkLoads.WaitAsync(token); held = true;
            if (item.Kind != "Photos") return;
            _ = await VisualFiles.ReadPhotoAsync(item.Path, token);
            using var file = File.OpenRead(item.Path);
            using var input = file.AsRandomAccessStream();
            var bitmap = new BitmapImage { DecodePixelWidth = 192 };
            await bitmap.SetSourceAsync(input);
            if (!token.IsCancellationRequested) image.Source = bitmap;
        }
        catch { /* Missing/unsupported thumbnails keep the named file button. */ }
        finally { if (held) artworkLoads.Release(); }
    }
    private async Task OpenVisualFileAsync(StorageFile file)
    {
        await visualLibrary.ImportAsync([file.Path]);
        var item = visualLibrary.State.Items.Single(i => i.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
        mediaPage = item.Kind;
        MediaNavigation.SelectedItem = MediaNavigation.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == mediaPage);
        await OpenVisualAsync(item);
    }
    private async Task OpenVisualAsync(VisualItem item)
    {
        if (visualBusy || closed) return;
        StopVisualWork(); visualPdf = null; visualCurrent = item; visualPageIndex = 0; visualZoom = 100;
        visualCancellation = new CancellationTokenSource();
        var cancellation = visualCancellation;
        var token = cancellation.Token;
        visualBusy = true;
        try
        {
            if (item.Kind == "Documents")
            {
                VisualFiles.OpenedPdf? opened = null;
                try { opened = await VisualFiles.LoadPdfAsync(item.Path, "", token); }
                catch (Exception ex) when (ex.HResult == unchecked((int)0x8007052B))
                {
                    if (closed || token.IsCancellationRequested) return;
                    var password = new PasswordBox { Header = "Password, if this PDF requires one" };
                    var body = new StackPanel { Spacing = 10 }; body.Children.Add(new TextBlock { Text = "Windows could not open this PDF. It may be damaged, unsupported or password-protected.", TextWrapping = TextWrapping.Wrap }); body.Children.Add(password);
                    var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "PDF could not be opened", Content = body, PrimaryButtonText = "Try password", CloseButtonText = "Cancel" };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) { visualCurrent = null; return; }
                    try { opened = await VisualFiles.LoadPdfAsync(item.Path, password.Password, token); }
                    finally { password.Password = ""; }
                }
                if (closed || token.IsCancellationRequested) { opened?.Dispose(); return; }
                visualPdf = opened;
            }
            else _ = await VisualFiles.ReadPhotoAsync(item.Path, token);
            if (!closed) RenderMedia();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) { visualCurrent = null; if (!closed) ShowError("Could not open this file: " + ex.Message); } }
        finally { visualBusy = false; if (!closed && mediaPage is "Photos" or "Documents") RenderVisualLibrary(); }
    }
    private void RenderVisualViewer()
    {
        if (visualCurrent is not { } item) return;
        var back = new Button { Content = "Back to library" };
        back.Click += (_, _) => { StopVisualWork(); visualCurrent = null; visualPdf = null; RenderVisualLibrary(); };
        MediaBody.Children.Add(back);
        MediaBody.Children.Add(new TextBlock { Text = item.Name, FontSize = 24, TextWrapping = TextWrapping.Wrap });
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var previous = new Button { Content = "Previous", IsEnabled = !visualBusy && (visualPdf is null || visualPageIndex > 0) };
        var next = new Button { Content = "Next", IsEnabled = !visualBusy && (visualPdf is null || visualPageIndex + 1 < visualPdf.PageCount) };
        previous.Click += async (_, _) => await AdvanceVisual(-1); next.Click += async (_, _) => await AdvanceVisual(1);
        navigation.Children.Add(previous); navigation.Children.Add(next); MediaBody.Children.Add(navigation);
        if (visualPdf is not null) MediaBody.Children.Add(new TextBlock { Text = $"Page {visualPageIndex + 1} of {visualPdf.PageCount}" });
        var zoom = new Slider { Header = "Zoom", Minimum = 25, Maximum = 200, Value = visualZoom, StepFrequency = 25, Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
        zoom.ValueChanged += (_, args) => { visualZoom = args.NewValue; if (visualImage is not null) visualImage.Width = 800 * visualZoom / 100; };
        MediaBody.Children.Add(zoom);
        visualImage = new Image { Width = 800 * visualZoom / 100, Stretch = Stretch.Uniform };
        MediaBody.Children.Add(new ScrollViewer { Content = visualImage, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 650 });
        _ = LoadVisualDisplay(item, visualImage);
        if (item.Kind == "Photos")
        {
            var slide = new Button { Content = slideshow.IsEnabled ? "Stop slideshow" : "Start slideshow (5 seconds)", IsEnabled = !visualBusy };
            slide.Click += (_, _) => { if (slideshow.IsEnabled) slideshow.Stop(); else slideshow.Start(); RenderVisualLibrary(); };
            MediaBody.Children.Add(slide);
            var edit = new Button { Content = "Crop / rotate / brightness · export a copy", IsEnabled = !visualBusy };
            edit.Click += async (_, _) => await ExportVisualPhoto(item); MediaBody.Children.Add(edit);
            var metadata = new Button { Content = "Photo information", IsEnabled = !visualBusy };
            metadata.Click += async (_, _) =>
            {
                try { var info = await VisualFiles.ReadPhotoAsync(item.Path, CancellationToken.None); await new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Photo information", Content = $"{info.Width} × {info.Height} pixels\n{info.Frames} frame(s)\n{info.Bytes:N0} bytes\nDate taken: {info.DateTaken}\nTitle: {info.Title}", CloseButtonText = "Close" }.ShowAsync(); }
                catch (Exception ex) { if (!closed) ShowError(ex.Message); }
            }; MediaBody.Children.Add(metadata);
        }
        var album = new ComboBox { Header = "Album", ItemsSource = new[] { "No album" }.Concat(visualLibrary.State.Albums).ToArray(), SelectedIndex = item.Album.Length == 0 ? 0 : visualLibrary.State.Albums.IndexOf(item.Album) + 1, IsEnabled = visualLibrary.Writable && !visualBusy };
        album.SelectionChanged += async (_, _) => await RunVisualEdit(async () =>
        { var target = album.SelectedIndex <= 0 ? "" : visualLibrary.State.Albums[album.SelectedIndex - 1]; await visualLibrary.SetAlbumAsync(item.Path, target); visualCurrent = visualLibrary.State.Items.Single(i => i.Path == item.Path); });
        MediaBody.Children.Add(album);
        var remove = new Button { Content = "Remove from library (keep the file)", IsEnabled = visualLibrary.Writable && !visualBusy };
        remove.Click += async (_, _) => await RunVisualEdit(async () => { StopVisualWork(); await visualLibrary.RemoveAsync(item.Path); visualCurrent = null; visualPdf = null; }); MediaBody.Children.Add(remove);
    }
    private async Task LoadVisualDisplay(VisualItem item, Image target)
    {
        var token = visualCancellation?.Token ?? CancellationToken.None;
        var generationPage = visualPageIndex;
        bool acquired = false;
        try
        {
            await visualRenderGate.WaitAsync(token); acquired = true;
            if (closed || !ReferenceEquals(target, visualImage)) return;
            var bitmap = new BitmapImage { DecodePixelWidth = 2048 };
            if (visualPdf is not null)
            { using var rendered = await VisualFiles.RenderPdfAsync(visualPdf, generationPage, token); await bitmap.SetSourceAsync(rendered); }
            else { using var file = File.OpenRead(item.Path); using var input = file.AsRandomAccessStream(); await bitmap.SetSourceAsync(input); }
            if (!closed && !token.IsCancellationRequested && ReferenceEquals(target, visualImage)) target.Source = bitmap;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed && !token.IsCancellationRequested && ReferenceEquals(target, visualImage)) ShowError("The preview could not be rendered: " + ex.Message); }
        finally { if (acquired) visualRenderGate.Release(); }
    }
    private async Task AdvanceVisual(int direction)
    {
        if (visualBusy || visualCurrent is null) return;
        if (visualPdf is not null)
        {
            int page = (int)visualPageIndex + direction;
            if (page >= 0 && page < visualPdf.PageCount) { visualPageIndex = (uint)page; RenderVisualLibrary(); }
        }
        else
        {
            var entries = VisualEntries(); int index = entries.FindIndex(i => i.Path == visualCurrent.Path);
            if (entries.Count == 0) return;
            var target = entries[(index + direction + entries.Count) % entries.Count];
            bool continuing = slideshow.IsEnabled;
            await OpenVisualAsync(target);
            if (continuing && visualCurrent is not null && !closed && mediaPage == "Photos") slideshow.Start();
        }
    }
    private async Task ExportVisualPhoto(VisualItem item)
    {
        if (visualBusy) return; slideshow.Stop(); visualBusy = true;
        try
        {
            var info = await VisualFiles.ReadPhotoAsync(item.Path, CancellationToken.None);
            var turns = new ComboBox { Header = "Clockwise rotation", ItemsSource = new[] { "0°", "90°", "180°", "270°" }, SelectedIndex = 0 };
            NumberBox Number(string title, double value, double max) => new() { Header = title, Value = value, Minimum = 0, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            var x = Number("Crop left (pixels)", 0, info.Width - 1); var y = Number("Crop top (pixels)", 0, info.Height - 1);
            var width = Number("Crop width", info.Width, info.Width); var height = Number("Crop height", info.Height, info.Height);
            width.Minimum = height.Minimum = 1;
            var brightness = new NumberBox { Header = "Brightness adjustment (-100 to 100)", Minimum = -100, Maximum = 100, Value = 0 };
            var body = new StackPanel { Spacing = 8 }; foreach (var control in new UIElement[] { turns, x, y, width, height, brightness }) body.Children.Add(control);
            body.Children.Add(new TextBlock { Text = "Writes a new PNG/JPEG with pixels only; source metadata including EXIF/GPS is not copied. The original is unchanged. Animated files export their first frame. Editing limit: 16 megapixels.", TextWrapping = TextWrapping.Wrap });
            if (await Dialog("Export an edited photo copy", new ScrollViewer { Content = body, MaxHeight = 500 }).ShowAsync() != ContentDialogResult.Primary) return;
            int Value(NumberBox box) => double.IsFinite(box.Value) && box.Value == Math.Truncate(box.Value) ? checked((int)box.Value) : throw new InvalidDataException("Enter whole pixel/brightness values.");
            var edit = new PhotoEdit(turns.SelectedIndex, Value(x), Value(y), Value(width), Value(height), Value(brightness));
            // The App SDK picker returns a path without creating/truncating the file.
            // Validate source/destination and encode successfully before AtomicFile publishes it.
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id)
                { SuggestedFileName = Path.GetFileNameWithoutExtension(item.Name) + "-edited", DefaultFileExtension = ".png" };
            picker.FileTypeChoices.Add("PNG image", new List<string> { ".png" }); picker.FileTypeChoices.Add("JPEG image", new List<string> { ".jpg" });
            var output = await picker.PickSaveFileAsync(); if (output is null || closed) return;
            await VisualFiles.ExportPhotoAsync(item.Path, output.Path, edit, CancellationToken.None);
            Notice.Severity = InfoBarSeverity.Success; Notice.Message = "Edited copy saved. The original was not changed; source metadata was not copied."; Notice.IsOpen = true;
        }
        catch (Exception ex) { if (!closed) ShowError("Photo export stopped: " + ex.Message); }
        finally { visualBusy = false; if (!closed && mediaPage is "Photos" or "Documents") RenderVisualLibrary(); }
    }
}
