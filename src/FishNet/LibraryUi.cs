using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private readonly LinkedLibrary library = new();
    private readonly Appearance appearance = new();
    private bool libraryBusy;
    private string selectedCollection = "All collections";

    private async void InitializeHub(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= InitializeHub;
        appearance.Load(); ApplyAppearance();
        AppearanceBody.Children.Add(appearance.Editor(() => { ApplyAppearance(); RenderLibrary(); RenderMedia(); }, ShowError));
        await library.LoadAsync();
        LoadQueue();
        await mediaCatalog.LoadAsync(); await playlists.LoadAsync(); mediaReady = true;
        StartFolderWatch();
        MediaNavigation.SelectedItem = MediaNavigation.MenuItems[0]; RenderMedia();
        if (mediaCatalog.Warning is not null) ShowError(mediaCatalog.Warning);
        RenderLibrary();
        if (library.Warning is not null) ShowError(library.Warning);
    }
    private void RenderLibrary()
    {
        LibraryBody.Children.Clear();
        LibraryBody.Spacing = appearance.Compact ? 8 : 18;
        LibraryBody.Children.Add(new TextBlock { Text = "Services, apps & browsers", FontSize = 28, TextWrapping = TextWrapping.Wrap });
        LibraryBody.Children.Add(new TextBlock { Text = "Choose a ready-made service or add your own. Services open in your added browser; accounts and streaming stay with the provider.", TextWrapping = TextWrapping.Wrap });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        void AddButton(string label, Func<Task> action)
        {
            var button = new Button { Content = label, IsEnabled = library.Writable && !libraryBusy };
            button.Click += async (_, _) =>
            {
                if (libraryBusy) return;
                libraryBusy = true; RenderLibrary();
                try { await action(); } catch (Exception ex) { ShowError(ex.Message); }
                finally { libraryBusy = false; RenderLibrary(); }
            };
            toolbar.Children.Add(button);
        }
        AddButton("Link app", () => AddLink("App"));
        AddButton("Add browser", () => AddLink("Browser"));
        AddButton("Add custom service", () => AddLink("Website"));
        AddButton("New collection", CreateCollection);
        LibraryBody.Children.Add(toolbar);
        LibraryBody.Children.Add(new TextBlock { Text = "Available services", FontSize = 22 });
        foreach (var service in ServiceCatalog.BuiltIn)
        {
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(new TextBlock { Text = service.Name, FontSize = 20 });
            row.Children.Add(new TextBlock { Text = service.Category + " · " + service.Url, TextWrapping = TextWrapping.Wrap, Opacity = .7 });
            var add = new Button { Content = "Add to my library", IsEnabled = library.Writable && !libraryBusy };
            add.Click += async (_, _) =>
            {
                if (libraryBusy) return;
                libraryBusy = true; RenderLibrary();
                try { await AddLink("Website", service); }
                catch (Exception ex) { ShowError(ex.Message); }
                finally { libraryBusy = false; RenderLibrary(); }
            };
            row.Children.Add(add);
            LibraryBody.Children.Add(new Border { Child = row, Padding = new Thickness(16), CornerRadius = new CornerRadius(12), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255,43,43,48)) });
        }
        LibraryBody.Children.Add(new TextBlock { Text = "My library", FontSize = 22 });
        var filter = new ComboBox { Header = "Collection", ItemsSource = new[] { "All collections" }.Concat(library.State.Collections).ToList(), SelectedItem = selectedCollection };
        filter.SelectionChanged += (_, _) => { selectedCollection = filter.SelectedItem?.ToString() ?? "All collections"; RenderLibrary(); };
        LibraryBody.Children.Add(filter);
        var apps = library.State.Apps.Where(a => selectedCollection == "All collections" || a.Collection == selectedCollection).ToList();
        if (apps.Count == 0) LibraryBody.Children.Add(new TextBlock { Text = "No links here yet. Add an app or browser to begin.", TextWrapping = TextWrapping.Wrap });
        foreach (var app in apps)
        {
            var card = new StackPanel { Spacing = 8 };
            card.Children.Add(new TextBlock { Text = app.Name, FontSize = 22 });
            card.Children.Add(new TextBlock { Text = $"{app.Kind} · {app.Collection}\n{app.Target}", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var launch = new Button { Content = "Open externally" };
            launch.Click += (_, _) => { try { library.Launch(app); } catch (Exception ex) { ShowError(ex.Message); } };
            var remove = new Button { Content = "Remove link", IsEnabled = library.Writable && !libraryBusy };
            var edit = new Button { Content = "Edit / move", IsEnabled = library.Writable && !libraryBusy };
            edit.Click += async (_, _) =>
            {
                if (libraryBusy) return;
                libraryBusy = true; RenderLibrary();
                try
                {
                    var name = new TextBox { Header = "Display name", Text = app.Name, MaxLength = 100 };
                    var collection = new ComboBox { Header = "Collection", ItemsSource = library.State.Collections, SelectedItem = app.Collection };
                    var body = new StackPanel { Spacing = 12 }; body.Children.Add(name); body.Children.Add(collection);
                    if (await Dialog("Edit link", body).ShowAsync() == ContentDialogResult.Primary)
                    {
                        if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter a display name.");
                        var next = library.Copy();
                        var index = next.Apps.FindIndex(a => a.Id == app.Id);
                        next.Apps[index] = app with { Name = name.Text.Trim(), Collection = collection.SelectedItem?.ToString() ?? app.Collection };
                        await library.SaveAsync(next);
                    }
                }
                catch (Exception ex) { ShowError(ex.Message); }
                finally { libraryBusy = false; RenderLibrary(); }
            };
            remove.Click += async (_, _) =>
            {
                if (libraryBusy) return;
                libraryBusy = true;
                try
                {
                    if (library.State.Apps.Any(a => a.BrowserId == app.Id)) throw new InvalidOperationException("Remove this browser's website links first.");
                    var next = library.Copy(); next.Apps.RemoveAll(a => a.Id == app.Id); await library.SaveAsync(next);
                }
                catch (Exception ex) { ShowError(ex.Message); }
                finally { libraryBusy = false; RenderLibrary(); }
            };
            actions.Children.Add(launch); actions.Children.Add(edit); actions.Children.Add(remove); card.Children.Add(actions);
            LibraryBody.Children.Add(new Border { Child = card, Padding = new Thickness(20), CornerRadius = new CornerRadius(16), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255,43,43,48)) });
        }
    }
    private async Task CreateCollection()
    {
        var name = new TextBox { Header = "Collection name", MaxLength = 80 };
        var dialog = Dialog("New collection", name);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var value = name.Text.Trim();
        if (value.Length == 0 || value == "All collections" || library.State.Collections.Contains(value, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a unique collection name.");
        var next = library.Copy(); next.Collections.Add(value); await library.SaveAsync(next);
        selectedCollection = value;
    }
    private ContentDialog Dialog(string title, UIElement body) => new()
    { Title = title, Content = body, XamlRoot = Root.XamlRoot, RequestedTheme = ElementTheme.Dark, PrimaryButtonText = "Save", CloseButtonText = "Cancel" };

    private async Task AddLink(string kind, ServicePreset? service = null)
    {
        string target = "";
        if (kind != "Website")
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null || closed) return;
            target = file.Path;
        }
        var body = new StackPanel { Spacing = 12 };
        var name = new TextBox { Header = "Display name", Text = service?.Name ?? Path.GetFileNameWithoutExtension(target), MaxLength = 100 };
        var collection = new ComboBox { Header = "Collection", ItemsSource = library.State.Collections, SelectedIndex = 0 };
        var url = new TextBox { Header = "HTTPS service address", PlaceholderText = "https://…", Text = service?.Url ?? "" };
        var browsers = library.State.Apps.Where(a => a.Kind == "Browser").ToList();
        var browser = new ComboBox { Header = "Open with your added browser", ItemsSource = browsers, DisplayMemberPath = "Name", SelectedIndex = browsers.Count > 0 ? 0 : -1 };
        body.Children.Add(name); body.Children.Add(collection);
        if (kind == "Website")
        {
            if (browsers.Count == 0) throw new InvalidOperationException("Use Add browser first. FishNet has no built-in browser.");
            body.Children.Add(url); body.Children.Add(browser);
        }
        else body.Children.Add(new TextBlock { Text = target, TextWrapping = TextWrapping.Wrap });
        if (await Dialog("Add " + kind.ToLowerInvariant(), body).ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter a display name.");
        if (kind == "Website")
        {
            if (!Uri.TryCreate(url.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Enter a valid HTTPS address.");
            target = uri.AbsoluteUri;
        }
        var next = library.Copy();
        next.Apps.Add(new LinkedApp(Guid.NewGuid().ToString("N"), name.Text.Trim(), kind, target, collection.SelectedItem?.ToString() ?? "Favorites", kind == "Website" ? (browser.SelectedItem as LinkedApp)?.Id : null));
        await library.SaveAsync(next);
    }
}
