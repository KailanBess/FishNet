using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FishNet;

public sealed partial class MainWindow
{
    private sealed record SavedQueue(List<string> Tracks, int Index, bool Repeat);
    private readonly string queueFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet", "play-queue.json");
    private string? queueWarning;

    private void LoadQueue()
    {
        if (!File.Exists(queueFile)) return;
        try
        {
            var state = JsonSerializer.Deserialize<SavedQueue>(File.ReadAllText(queueFile));
            if (state?.Tracks is null || state.Tracks.Count > 10000 || state.Index < -1 ||
                state.Index >= state.Tracks.Count || state.Tracks.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
                throw new InvalidDataException("The saved queue is invalid.");
            playbackQueue = state.Tracks;
            queueIndex = state.Index;
            repeatPlaylist = state.Repeat;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { queueWarning = "The saved queue could not be read and was preserved. " + ex.Message; }
    }

    private void SaveQueue()
    {
        if (queueWarning is not null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(queueFile)!);
            File.WriteAllText(queueFile + ".tmp", JsonSerializer.Serialize(new SavedQueue(playbackQueue, queueIndex, repeatPlaylist)));
            File.Move(queueFile + ".tmp", queueFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (!closed) ShowError("The queue could not be saved. " + ex.Message); }
    }

    private void AddToQueue(string path)
    {
        if (playbackQueue.Count >= 10000) { ShowError("The queue already contains 10,000 tracks."); return; }
        playbackQueue.Add(path);
        SaveQueue();
    }

    private void RenderQueue()
    {
        MediaBody.Children.Clear();
        MediaBody.Children.Add(new TextBlock { Text = queueWarning ?? "Your queue is saved locally. Resume playback when you are ready.", TextWrapping = TextWrapping.Wrap });
        var resume = new Button { Content = "Play or resume queue", IsEnabled = playbackQueue.Count > 0 };
        resume.Click += async (_, _) =>
        {
            resume.IsEnabled = false;
            queueIndex = Math.Max(-1, queueIndex - 1);
            await NextPlaylistTrack();
            if (!closed && mediaPage == "Queue") RenderQueue();
        };
        MediaBody.Children.Add(resume);
        if (playbackQueue.Count > 250) MediaBody.Children.Add(new TextBlock { Text = "Showing the first 250 queued tracks." });
        for (int i = 0; i < Math.Min(250, playbackQueue.Count); i++)
        {
            int index = i;
            var row = new StackPanel { Spacing = 4 };
            var file = playbackQueue[index];
            row.Children.Add(new TextBlock { Text = (index == queueIndex ? "▶ " : "") + Path.GetFileName(file) +
                (File.Exists(file) ? "" : " · Missing file"), TextWrapping = TextWrapping.Wrap });
            var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            void Command(string label, Action action, bool enabled)
            {
                var button = new Button { Content = label, IsEnabled = enabled };
                button.Click += (_, _) => { action(); SaveQueue(); RenderQueue(); };
                commands.Children.Add(button);
            }
            Command("Move up", () => MoveQueueItem(index, index - 1), index > 0);
            Command("Move down", () => MoveQueueItem(index, index + 1), index + 1 < playbackQueue.Count);
            Command("Remove", () => { playbackQueue.RemoveAt(index); if (index <= queueIndex) queueIndex--; }, true);
            row.Children.Add(commands);
            MediaBody.Children.Add(row);
        }
    }

    private void MoveQueueItem(int from, int to)
    {
        (playbackQueue[from], playbackQueue[to]) = (playbackQueue[to], playbackQueue[from]);
        if (queueIndex == from) queueIndex = to;
        else if (queueIndex == to) queueIndex = from;
    }
}
