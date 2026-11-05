using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace FishNet;

public sealed partial class MainWindow
{
    private sealed record WatchPosition(string Path, long Ticks);
    private readonly string watchPositionFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet", "continue-watching.json");
    private string? watchedPath;
    private TimeSpan? pendingWatchPosition;
    private string? activeWatchPositionFile;

    private static bool IsLocalVideo(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".m4v" or ".mov" or ".wmv" or ".mkv" or ".avi" or ".webm";

    private void SaveWatchPosition()
    {
        if (watchedPath is null || activeWatchPositionFile is null || pendingWatchPosition is not null || !player.PlaybackSession.CanSeek) return;
        var position = player.PlaybackSession.Position;
        var duration = player.PlaybackSession.NaturalDuration;
        if (position < TimeSpan.FromSeconds(5) || duration <= position + TimeSpan.FromSeconds(5)) return;
        var target = activeWatchPositionFile;
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new WatchPosition(watchedPath, position.Ticks)));
            File.Move(temporary, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!closed) ShowError("Could not save the video position: " + ex.Message);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static WatchPosition? ReadWatchPosition(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 32768) return null;
            return JsonSerializer.Deserialize<WatchPosition>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private void ForgetFinishedPosition()
    {
        if (activeWatchPositionFile is null || watchedPath is null) return;
        if (!string.Equals(ReadWatchPosition(activeWatchPositionFile)?.Path, watchedPath, StringComparison.OrdinalIgnoreCase)) return;
        try { File.Delete(activeWatchPositionFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void AddContinueWatching()
    {
        var entry = ReadWatchPosition(watchPositionFile);
        if (entry is null || entry.Ticks <= 0 || string.IsNullOrEmpty(entry.Path) || !IsLocalVideo(entry.Path) || !File.Exists(entry.Path)) return;
        var position = TimeSpan.FromTicks(entry.Ticks);
        var button = new Button { Content = $"Continue watching {Path.GetFileName(entry.Path)} at {position:g}" };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(entry.Path);
                if (!closed) await PlayLocalAsync(file, resumeAt: position);
            }
            catch (Exception ex) { if (!closed) ShowError("Could not resume video: " + ex.Message); }
            finally { if (!closed) button.IsEnabled = true; }
        };
        MediaBody.Children.Add(button);
    }
}

