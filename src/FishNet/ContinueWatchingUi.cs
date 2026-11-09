using System.Security.Cryptography;
using System.Text;
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

    private string WatchFolder => Path.Combine(Path.GetDirectoryName(watchPositionFile)!, "watch-positions");

    private string VideoPositionFile(string path)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        return Path.Combine(WatchFolder, key + ".json");
    }

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
        ClearWatchPosition(activeWatchPositionFile, watchedPath);
    }

    private void ClearWatchPosition(string target, string path)
    {
        foreach (var file in new[] { target, watchPositionFile }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(ReadWatchPosition(file)?.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { if (!closed) ShowError("Could not clear the saved position: " + ex.Message); }
        }
    }

    private void AddContinueWatching()
    {
        List<string> files;
        try
        {
            files = Directory.Exists(WatchFolder)
                ? Directory.EnumerateFiles(WatchFolder, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { files = []; }
        files.Add(watchPositionFile);
        var entries = files.Select(ReadWatchPosition)
            .OfType<WatchPosition>()
            .Where(entry => entry.Ticks > 0 && !string.IsNullOrWhiteSpace(entry.Path) && IsLocalVideo(entry.Path) && File.Exists(entry.Path))
            .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Take(12).ToArray();
        if (entries.Length == 0) return;

        MediaBody.Children.Add(new TextBlock { Text = "Continue watching", FontSize = 22 });
        foreach (var entry in entries)
        {
            var position = TimeSpan.FromTicks(entry.Ticks);
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(new TextBlock { Text = $"{Path.GetFileName(entry.Path)} · {position:g}", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var resume = new Button { Content = "Resume" };
            var restart = new Button { Content = "Start over" };
            var watched = new Button { Content = "Mark watched" };
            async Task OpenAsync(bool fromStart)
            {
                resume.IsEnabled = restart.IsEnabled = watched.IsEnabled = false;
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(entry.Path);
                    if (!closed) await PlayLocalAsync(file, resumeAt: fromStart ? null : position, resetWatchPosition: fromStart);
                }
                catch (Exception ex) { if (!closed) ShowError("Could not open video: " + ex.Message); }
                finally { if (!closed) resume.IsEnabled = restart.IsEnabled = watched.IsEnabled = true; }
            }
            resume.Click += async (_, _) => await OpenAsync(false);
            restart.Click += async (_, _) => await OpenAsync(true);
            watched.Click += (_, _) =>
            {
                if (string.Equals(watchedPath, entry.Path, StringComparison.OrdinalIgnoreCase))
                {
                    watchedPath = null;
                    activeWatchPositionFile = null;
                }
                ClearWatchPosition(VideoPositionFile(entry.Path), entry.Path);
                RenderMedia();
            };
            actions.Children.Add(resume);
            actions.Children.Add(restart);
            actions.Children.Add(watched);
            row.Children.Add(actions);
            MediaBody.Children.Add(row);
        }
    }
}