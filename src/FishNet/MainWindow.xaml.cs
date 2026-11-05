using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow : Window
{
    private readonly MediaPlayer player = new() { AutoPlay = false };
    private MediaSource? source;
    private bool closed;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "FishNet.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 780));
        AquariumWorkspace.Mark(this);
        Root.Loaded += InitializeHub;
        PlayerView.SetMediaPlayer(player);
        player.MediaOpened += MediaOpened;
        player.MediaFailed += MediaFailed;
        player.MediaEnded += MediaEnded;
        Closed += (_, _) =>
        {
            SaveWatchPosition();
            closed = true; cancelConversion = true; conversionCancellation?.Cancel(); podcastCancellation?.Cancel();
            player.MediaOpened -= MediaOpened;
            player.MediaFailed -= MediaFailed;
            player.MediaEnded -= MediaEnded;
            player.Pause();
            player.Source = null;
            PlayerView.SetMediaPlayer(null);
            source?.Dispose();
            player.Dispose();
        };
    }

    private async void OpenFile(object sender, RoutedEventArgs e)
    {
        OpenButton.IsEnabled = false;
        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            foreach (var extension in new[] { ".mp4", ".m4v", ".mov", ".wmv", ".mkv", ".mp3", ".m4a", ".wav", ".flac", ".wma" })
                picker.FileTypeFilter.Add(extension);
            var file = await picker.PickSingleFileAsync();
            if (file is null || closed) return;
            await PlayLocalAsync(file);
        }
        catch (Exception ex)
        {
            if (!closed) ShowError("Could not open media: " + ex.Message);
        }
        finally { if (!closed) OpenButton.IsEnabled = true; }
    }

    private void MediaOpened(MediaPlayer sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!closed && source is not null && pendingWatchPosition is TimeSpan position)
        {
            pendingWatchPosition = null;
            if (player.PlaybackSession.CanSeek && position < player.PlaybackSession.NaturalDuration)
                player.PlaybackSession.Position = position;
            player.Play();
        }
        if (!closed && source is not null) PlaybackStatus.Text = "Use the player controls for playback, seeking, and volume.";
    });

    private void MediaEnded(MediaPlayer sender, object args) => DispatcherQueue.TryEnqueue(async () =>
    {
        if (closed) return;
        ForgetFinishedPosition();
        if (playbackQueue.Count > 0) { await NextPlaylistTrack(); return; }
        if (!closed && source is not null) PlaybackStatus.Text = "Playback ended. Replay with the player controls or open another file.";
    });

    private void MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!closed && source is not null)
        {
            player.Pause();
            playbackQueue = []; queueIndex = -1;
            ShowError("Playback failed. This file may be damaged or require a codec not installed in Windows. " + args.ErrorMessage);
            PlaybackStatus.Text = "Unable to play this file. Open another file to continue.";
        }
    });

    private void CloseMedia(object sender, RoutedEventArgs e)
    {
        SaveWatchPosition();
        watchedPath = null;
        pendingWatchPosition = null;
        playbackQueue = []; queueIndex = -1;
        player.Pause();
        player.Source = null;
        playbackItem = null;
        source?.Dispose();
        source = null;
        PlayerView.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        CloseButton.IsEnabled = false;
        NowPlaying.Text = "Nothing playing";
        PlaybackStatus.Text = "LOCAL PLAYBACK";
        Notice.IsOpen = false;
    }

    private void ShowError(string message)
    {
        Notice.Severity = InfoBarSeverity.Error;
        Notice.Message = message;
        Notice.IsOpen = true;
    }
}








