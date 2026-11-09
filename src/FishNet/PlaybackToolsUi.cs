using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private MediaPlaybackItem? playbackItem;
    private DispatcherTimer? sleepTimer;
    private DateTimeOffset? sleepDeadline;
    private async Task AddSubtitles()
    {
        try
        {
            if (source is null || playbackItem is null) throw new InvalidOperationException("Open a video first.");
            var current = source; var item = playbackItem;
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".srt"); picker.FileTypeFilter.Add(".vtt");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync(); if (file is null || closed || current != source) return;
            var text = TimedTextSource.CreateFromUri(new Uri(file.Path));
            text.Resolved += (_, result) => DispatcherQueue.TryEnqueue(() =>
            {
                if (closed || current != source) return;
                if (result.Error is not null) { ShowError("Windows could not read these subtitles: " + result.Error.ErrorCode); return; }
                foreach (var track in result.Tracks) track.Label = file.Name;
                EnableSubtitles(item);
            });
            item.TimedMetadataTracksChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!closed && item == playbackItem) EnableSubtitles(item); });
            current.ExternalTimedTextSources.Add(text);
        }
        catch (Exception ex) { ShowError("Could not add subtitles: " + ex.Message); }
    }
    private static void EnableSubtitles(MediaPlaybackItem item)
    {
        for (uint i = 0; i < item.TimedMetadataTracks.Count; i++)
            if (item.TimedMetadataTracks[(int)i].TimedMetadataKind is TimedMetadataKind.Subtitle or TimedMetadataKind.Caption)
                item.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.PlatformPresented);
    }
    private void SetSleepTimer(int? minutes)
    {
        sleepTimer?.Stop();
        sleepDeadline = minutes is int count ? DateTimeOffset.UtcNow.AddMinutes(count) : null;
        if (sleepDeadline is null) return;
        if (sleepTimer is null)
        {
            sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            sleepTimer.Tick += (_, _) =>
            {
                if (closed || sleepDeadline is not DateTimeOffset deadline || DateTimeOffset.UtcNow < deadline) return;
                SetSleepTimer(null);
                player.Pause();
                SaveWatchPosition();
                PlaybackStatus.Text = "Playback paused by the sleep timer.";
                if (mediaPage == "PlaybackTools") RenderPlaybackTools();
            };
        }
        sleepTimer.Start();
    }

    private void RenderPlaybackTools()
    {
        MediaBody.Children.Clear();
        var subtitles = new Button { Content = "Add subtitle file (SRT / VTT)" }; subtitles.Click += async (_, _) => await AddSubtitles(); MediaBody.Children.Add(subtitles);
        var off = new Button { Content = "Disable subtitles" }; off.Click += (_, _) => { if (playbackItem is not null) for (uint i = 0; i < playbackItem.TimedMetadataTracks.Count; i++) playbackItem.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.Disabled); }; MediaBody.Children.Add(off);
        var speed = new ComboBox { Header = "Playback speed", ItemsSource = new[] { .5, .75, 1, 1.25, 1.5, 2 }, SelectedItem = player.PlaybackSession.PlaybackRate };
        speed.SelectionChanged += (_, _) => { if (speed.SelectedItem is double value) player.PlaybackSession.PlaybackRate = value; }; MediaBody.Children.Add(speed);
        var sleep = new ComboBox { Header = "Sleep timer duration (minutes)", ItemsSource = new[] { 15, 30, 45, 60 }, SelectedIndex = 1 };
        var startSleep = new Button { Content = "Set sleep timer", IsEnabled = source is not null };
        startSleep.Click += (_, _) => { if (sleep.SelectedItem is int minutes && source is not null) { SetSleepTimer(minutes); RenderPlaybackTools(); } };
        var cancelSleep = new Button { Content = "Cancel sleep timer", IsEnabled = sleepDeadline is not null };
        cancelSleep.Click += (_, _) => { SetSleepTimer(null); RenderPlaybackTools(); };
        MediaBody.Children.Add(sleep);
        MediaBody.Children.Add(startSleep);
        MediaBody.Children.Add(cancelSleep);
        if (sleepDeadline is DateTimeOffset deadline)
            MediaBody.Children.Add(new TextBlock { Text = $"Playback pauses at {deadline.ToLocalTime():t}." });
        var next = new Button { Content = "Next playlist track" }; next.Click += async (_, _) => { if (playbackQueue.Count > 0) await NextPlaylistTrack(); }; MediaBody.Children.Add(next);
        MediaBody.Children.Add(new TextBlock { Text = "Embedded subtitles can also be selected with the player's caption controls. Subtitle availability depends on Windows and the file.", TextWrapping = TextWrapping.Wrap });
    }
}
