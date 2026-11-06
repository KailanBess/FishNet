using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private bool mediaToolsBusy;
    private async Task ImportMediaFolder()
    {
        if (mediaToolsBusy) return;
        mediaToolsBusy = true;
        try
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync(); if (folder is null || closed) return;
            var files = await Task.Run(() => Directory.EnumerateFiles(folder.Path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = System.IO.FileAttributes.ReparsePoint }).Where(p => MediaCatalog.IsAudio(p) || VideoExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).Take(10001).ToArray());
            await mediaCatalog.ImportAsync(files); RenderMedia();
        }
        catch (Exception ex) { if (!closed) ShowError("Folder import failed: " + ex.Message); }
        finally { mediaToolsBusy = false; }
    }
    private async Task EditTags(string path)
    {
        if (mediaToolsBusy) return;
        mediaToolsBusy = true;
        try
        {
            var tags = await Task.Run(() => MediaTags.Read(path));
            var title = new TextBox { Header = "Title", Text = tags.Title };
            var artist = new TextBox { Header = "Artist", Text = tags.Artist };
            var album = new TextBox { Header = "Album", Text = tags.Album };
            var year = new NumberBox { Header = "Year", Value = tags.Year, Minimum = 0, Maximum = 9999 };
            var track = new NumberBox { Header = "Track", Value = tags.Track, Minimum = 0, Maximum = 9999 };
            string? cover = null;
            var art = new Image { Height = 100, Visibility = Visibility.Collapsed };
            var selectArt = new Button { Content = "Choose cover art" };
            selectArt.Click += async (_, _) =>
            {
                try
                {
                    var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".png");
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    var file = await picker.PickSingleFileAsync(); if (file is null) return;
                    if ((await file.GetBasicPropertiesAsync()).Size > 8 * 1024 * 1024) throw new InvalidDataException("Cover art must be smaller than 8 MB.");
                    cover = file.Path; using var stream = await file.OpenReadAsync(); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); art.Source = bitmap; art.Visibility = Visibility.Visible;
                }
                catch (Exception ex) { ShowError(ex.Message); }
            };
            var body = new StackPanel { Spacing = 8 }; foreach (var item in new UIElement[] { title, artist, album, year, track, selectArt, art, new TextBlock { Text = "Saving changes the MP3 tags and retains an original backup beside the file. Stop this file's playback before saving.", TextWrapping = TextWrapping.Wrap } }) body.Children.Add(item);
            if (await Dialog("MP3 tags & cover art", new ScrollViewer { Content = body, MaxHeight = 480 }).ShowAsync() != ContentDialogResult.Primary) return;
            if (NowPlaying.Text == Path.GetFileName(path)) CloseMedia(this, new RoutedEventArgs());
            var updated = new AudioTags(title.Text, artist.Text, album.Text, (uint)(double.IsNaN(year.Value) ? 0 : year.Value), (uint)(double.IsNaN(track.Value) ? 0 : track.Value));
            var backup = await Task.Run(() => MediaTags.Save(path, updated, cover));
            Notice.Severity = InfoBarSeverity.Success; Notice.Message = "Tags saved. Original backup: " + backup; Notice.IsOpen = true; RenderMedia();
        }
        catch (Exception ex) { if (!closed) ShowError("Tag editing failed: " + ex.Message); }
        finally { mediaToolsBusy = false; }
    }
    private async Task RelinkMedia(RecentMedia entry)
    {
        try
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(Path.GetExtension(entry.Path));
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync(); if (file is null || closed) return;
            // Save playlists first; if the catalog write fails it can be retried without losing playlist repairs.
            if (playlists.Warning is not null) throw new InvalidOperationException(playlists.Warning);
            await playlists.SaveAsync(playlists.Items.Select(p => p with { Tracks = p.Tracks.Select(t => t.Equals(entry.Path, StringComparison.OrdinalIgnoreCase) ? file.Path : t).ToList() }).ToList());
            await mediaCatalog.RelinkAsync(entry.Path, file.Path); RenderMedia();
        }
        catch (Exception ex) { ShowError("Relink failed: " + ex.Message); }
    }
}

