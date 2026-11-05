using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace FishNet;

public sealed partial class MainWindow
{
    private readonly MediaCatalog mediaCatalog = new();
    private bool mediaReady;
    private string mediaPage = "Home";
    private void NavigateMedia(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        mediaPage = args.IsSettingsSelected ? "Customize" : (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "Home";
        if (mediaReady) RenderMedia();
    }
    private void SearchMedia(object sender, TextChangedEventArgs args) { if (mediaReady) RenderMedia(); }
    private void RenderMedia()
    {
        MediaPageTitle.Text = mediaPage == "Apps" ? "Services & apps" : mediaPage == "Player" ? "Now playing" : mediaPage == "PlaybackTools" ? "Playback tools" : mediaPage;
        AppPane.Visibility = mediaPage == "Apps" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePane.Visibility = mediaPage == "Customize" ? Visibility.Visible : Visibility.Collapsed;
        MediaPane.Visibility = mediaPage is "Home" or "Music" or "Videos" or "Playlists" or "Converter" or "PlaybackTools" ? Visibility.Visible : Visibility.Collapsed;
        PageHost.Visibility = mediaPage == "Player" ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(PlayerContainer, mediaPage == "Player" ? 1 : 2);
        PlayerContainer.Height = mediaPage == "Player" ? double.NaN : 172;
        if (mediaPage == "Playlists") { RenderPlaylists(); return; }
        if (mediaPage == "PlaybackTools") { RenderPlaybackTools(); return; }
        if (mediaPage == "Converter") { RenderConverter(); return; }
        MediaBody.Children.Clear();
        var import = new Button { Content = "Import media folder", IsEnabled = !mediaToolsBusy && mediaCatalog.Warning is null };
        import.Click += async (_, _) => await ImportMediaFolder(); MediaBody.Children.Add(import);
        MediaBody.Children.Add(new TextBlock { Text = mediaPage == "Home" ? "Recent" : mediaPage, FontSize = 25, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var entries = mediaCatalog.Items.Where(i => (mediaPage == "Home" || i.Kind == mediaPage) && i.Name.Contains(MediaSearch.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count == 0)
        {
            MediaBody.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(MediaSearch.Text) ? "Your media collection starts here. Open a local file to add it to Recent." : "No matching media.", TextWrapping = TextWrapping.Wrap, Opacity = .65 });
            return;
        }
        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 24 };
        void Arrange()
        {
            var columns = Math.Max(1, Math.Min(4, (int)(Math.Max(200, grid.ActualWidth) / 205)));
            if (grid.ColumnDefinitions.Count == columns) return;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < Math.Ceiling(grid.Children.Count / (double)columns); i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < grid.Children.Count; i++) { Grid.SetColumn((FrameworkElement)grid.Children[i], i % columns); Grid.SetRow((FrameworkElement)grid.Children[i], i / columns); }
        }
        if (entries.Count > 250) MediaBody.Children.Add(new TextBlock { Text = "Showing the first 250 matches. Search to narrow your library." });
        foreach (var entry in entries.Take(250))
        {
            var card = new StackPanel { Spacing = 8 };
            var artwork = new Grid { Height = 174, Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 43, 43, 48)), CornerRadius = new CornerRadius(8) };
            artwork.Children.Add(new FontIcon { Glyph = entry.Kind == "Music" ? "\uE8D6" : "\uE714", FontSize = 40, Opacity = .45 });
            var image = new Image { Stretch = Stretch.UniformToFill }; artwork.Children.Add(image);
            var open = new Button { Content = artwork, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, "Play " + entry.Name);
            open.Click += async (_, _) => { try { await PlayLocalAsync(await StorageFile.GetFileFromPathAsync(entry.Path)); } catch (Exception ex) { ShowError("Could not open media: " + ex.Message); } };
            card.Children.Add(open); card.Children.Add(new TextBlock { Text = entry.Name, FontSize = 16, TextWrapping = TextWrapping.Wrap });
            card.Children.Add(new TextBlock { Text = entry.Kind, FontSize = 13, Opacity = .6 });
            if (Path.GetExtension(entry.Path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                var tags = new Button { Content = "Tags & cover art" }; tags.Click += async (_, _) => await EditTags(entry.Path); card.Children.Add(tags);
            }
            if (!File.Exists(entry.Path))
            {
                var repair = new Button { Content = "Missing · Relink file" }; repair.Click += async (_, _) => await RelinkMedia(entry); card.Children.Add(repair);
            }
            grid.Children.Add(card); _ = LoadArtworkAsync(entry, image);
        }
        grid.SizeChanged += (_, _) => Arrange(); MediaBody.Children.Add(grid); Arrange();
    }
    private static async Task LoadArtworkAsync(RecentMedia entry, Image image)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(entry.Path);
            using var thumbnail = await file.GetThumbnailAsync(entry.Kind == "Music" ? ThumbnailMode.MusicView : ThumbnailMode.VideosView, 256);
            if (thumbnail is null) return;
            var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(thumbnail); image.Source = bitmap;
        }
        catch { /* Missing metadata keeps the honest media-type placeholder. */ }
    }
    private async Task PlayLocalAsync(StorageFile file, bool fromPlaylist = false)
    {
        await SavePlaybackPositionAsync();
        if (closed) return;
        playbackReady = false;
        resumePath = file.Path;
        resumeSeconds = fromPlaylist ? 0 : mediaCatalog.Items.FirstOrDefault(i => i.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase))?.PositionSeconds ?? 0;
        if (!fromPlaylist) { playbackQueue = []; queueIndex = -1; }
        var next = Windows.Media.Core.MediaSource.CreateFromStorageFile(file);
        player.Pause(); player.Source = null; source?.Dispose(); source = next;
        Notice.IsOpen = false; NowPlaying.Text = file.Name; PlaybackStatus.Text = "Opening media…";
        EmptyState.Visibility = Visibility.Collapsed; PlayerView.Visibility = Visibility.Visible; CloseButton.IsEnabled = true;
        playbackItem = new Windows.Media.Playback.MediaPlaybackItem(source);
        player.Source = playbackItem; player.Play();
        try { await mediaCatalog.RememberAsync(file.Path); if (mediaReady) RenderMedia(); }
        catch (Exception ex) { ShowError("Playback started, but the media library was not saved: " + ex.Message); }
    }
}
