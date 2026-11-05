using Microsoft.UI.Xaml;

namespace FishNet;

public sealed partial class MainWindow
{
    private string? resumePath;
    private double resumeSeconds;
    private bool playbackReady;
    private readonly DispatcherTimer resumeTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private void InitializePlaybackResume()
    {
        resumeTimer.Tick += async (_, _) => await SavePlaybackPositionAsync();
        resumeTimer.Start();
    }
    private async Task SavePlaybackPositionAsync()
    {
        if (resumePath is null || !playbackReady || mediaCatalog.Warning is not null) return;
        var file = resumePath;
        var seconds = Math.Max(0, player.PlaybackSession.Position.TotalSeconds);
        try { await mediaCatalog.SavePositionAsync(file, seconds); }
        catch (Exception ex) { resumeTimer.Stop(); if (!closed) ShowError("Playback position could not be saved: " + ex.Message); }
    }
}
