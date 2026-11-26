using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace FishNet;

public sealed partial class MainWindow
{
    private readonly RadioStations radioStations = new();
    private bool radioBusy;
    private void RenderRadio()
    {
        MediaBody.Children.Clear();
        MediaBody.Children.Add(new TextBlock { Text = "Save a direct HTTPS audio stream supplied by a station. Play connects to its provider. Availability, codecs and regional access depend on the provider. Radio streams stay separate from your local media library.", TextWrapping = TextWrapping.Wrap });
        if (radioStations.Warning is not null) MediaBody.Children.Add(new TextBlock { Text = radioStations.Warning, TextWrapping = TextWrapping.Wrap });
        var add = new Button { Content = "Add radio station", IsEnabled = !radioBusy && radioStations.Warning is null };
        add.Click += async (_, _) => await AddRadioStation(); MediaBody.Children.Add(add);
        var matches = radioStations.Items.Where(s => s.Name.Contains(MediaSearch.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) MediaBody.Children.Add(new TextBlock { Text = "No matching radio stations. Add a stream to begin.", TextWrapping = TextWrapping.Wrap });
        foreach (var station in matches)
        {
            var row = new StackPanel { Spacing = 8 };
            row.Children.Add(new TextBlock { Text = station.Name, FontSize = 22, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new TextBlock { Text = RadioStations.StreamUri(station.Url).Host, Opacity = .65 });
            var play = new Button { Content = "Play " + station.Name };
            play.Click += (_, _) => { try { PlayRadio(station); } catch (Exception ex) { if (!closed) ShowError("Radio could not start: " + ex.Message); } };
            var remove = new Button { Content = "Remove saved station", IsEnabled = !radioBusy && radioStations.Warning is null };
            remove.Click += async (_, _) =>
            {
                if (radioBusy) return; radioBusy = true; RenderRadio();
                try { await radioStations.RemoveAsync(station.Id); }
                catch (Exception ex) { if (!closed) ShowError("The station was not removed: " + ex.Message); }
                finally { radioBusy = false; if (!closed && mediaPage == "Radio") RenderRadio(); }
            };
            row.Children.Add(play); row.Children.Add(remove); MediaBody.Children.Add(row);
        }
    }

    private async Task AddRadioStation()
    {
        if (radioBusy || radioStations.Warning is not null) return; radioBusy = true;
        try
        {
            var name = new TextBox { Header = "Station name", MaxLength = 120 };
            var url = new TextBox { Header = "Direct HTTPS audio stream", PlaceholderText = "https://station.example/stream", MaxLength = 2048 };
            var body = new StackPanel { Spacing = 12 }; body.Children.Add(name); body.Children.Add(url);
            body.Children.Add(new TextBlock { Text = "Use a public stream address without credentials. Adding a station saves it locally and does not connect.", TextWrapping = TextWrapping.Wrap });
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Add radio station", Content = body,
                PrimaryButtonText = "Save station", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !closed) await radioStations.AddAsync(name.Text, url.Text);
        }
        catch (Exception ex) { if (!closed) ShowError("The station was not saved: " + ex.Message); }
        finally { radioBusy = false; if (!closed && mediaPage == "Radio") RenderRadio(); }
    }

    private void PlayRadio(RadioStation station)
    {
        var next = MediaSource.CreateFromUri(RadioStations.StreamUri(station.Url));
        SaveWatchPosition(); SetSleepTimer(null);
        watchedPath = null; activeWatchPositionFile = null; pendingWatchPosition = null; playingQueue = false;
        player.Pause(); player.Source = null; source?.Dispose(); source = next;
        playbackItem = new MediaPlaybackItem(source); player.Source = playbackItem;
        Notice.IsOpen = false; NowPlaying.Text = station.Name; PlaybackStatus.Text = "Connecting to radio stream…";
        EmptyState.Visibility = Visibility.Collapsed; PlayerView.Visibility = Visibility.Visible; CloseButton.IsEnabled = true;
        UpdatePlayerLayout(); player.Play();
    }
}
