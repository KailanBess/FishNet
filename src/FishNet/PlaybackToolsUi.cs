using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private MediaPlaybackItem? playbackItem;
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
    private void RenderPlaybackTools()
    {
        MediaBody.Children.Clear();
        var subtitles = new Button { Content = "Add subtitle file (SRT / VTT)" }; subtitles.Click += async (_, _) => await AddSubtitles(); MediaBody.Children.Add(subtitles);
        var off = new Button { Content = "Disable subtitles" }; off.Click += (_, _) => { if (playbackItem is not null) for (uint i = 0; i < playbackItem.TimedMetadataTracks.Count; i++) playbackItem.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.Disabled); }; MediaBody.Children.Add(off);
        var speed = new ComboBox { Header = "Playback speed", ItemsSource = new[] { .5, .75, 1, 1.25, 1.5, 2 }, SelectedItem = player.PlaybackSession.PlaybackRate };
        speed.SelectionChanged += (_, _) => { if (speed.SelectedItem is double value) player.PlaybackSession.PlaybackRate = value; }; MediaBody.Children.Add(speed);
        var next = new Button { Content = "Next playlist track" }; next.Click += async (_, _) => { if (playbackQueue.Count > 0) await NextPlaylistTrack(); }; MediaBody.Children.Add(next);
        MediaBody.Children.Add(new TextBlock { Text = "Embedded subtitles can also be selected with the player's caption controls. Subtitle availability depends on Windows and the file.", TextWrapping = TextWrapping.Wrap });
    }
}
