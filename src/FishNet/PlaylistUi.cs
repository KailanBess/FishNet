using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private readonly Playlists playlists = new();
    private bool playlistBusy;
    private List<string> playbackQueue = [];
    private int queueIndex = -1;
    private bool shufflePlaylist;
    private bool repeatPlaylist;

    private void RenderPlaylists()
    {
        MediaBody.Children.Clear();
        var create = new Button { Content = "New MP3 playlist", IsEnabled = playlists.Warning is null && !playlistBusy };
        create.Click += async (_, _) => await PlaylistAction(async () =>
        {
            var name = new TextBox { Header = "Playlist name", MaxLength = 100 };
            if (await Dialog("New playlist", name).ShowAsync() != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter a playlist name.");
            await playlists.SaveAsync([.. playlists.Items, new Playlist(Guid.NewGuid().ToString("N"), name.Text.Trim(), [])]);
        });
        MediaBody.Children.Add(create);
        var shuffle = new CheckBox { Content = "Shuffle", IsChecked = shufflePlaylist };
        shuffle.Checked += (_, _) => shufflePlaylist = true; shuffle.Unchecked += (_, _) => shufflePlaylist = false;
        var repeat = new CheckBox { Content = "Repeat playlist", IsChecked = repeatPlaylist };
        repeat.Checked += (_, _) => repeatPlaylist = true; repeat.Unchecked += (_, _) => repeatPlaylist = false;
        MediaBody.Children.Add(shuffle); MediaBody.Children.Add(repeat);
        var import = new Button { Content = "Import M3U playlist", IsEnabled = !playlistBusy && playlists.Warning is null };
        import.Click += async (_, _) => await PlaylistAction(async () =>
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".m3u"); picker.FileTypeFilter.Add(".m3u8");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync(); if (file is null || closed) return;
            if ((await file.GetBasicPropertiesAsync()).Size > 4_000_000) throw new InvalidDataException("Playlist is too large.");
            var tracks = PlaylistExchange.Read(await File.ReadAllTextAsync(file.Path), file.Path);
            await playlists.SaveAsync([.. playlists.Items, new Playlist(Guid.NewGuid().ToString("N"), Path.GetFileNameWithoutExtension(file.Name), tracks)]);
        }); MediaBody.Children.Add(import);
        MediaBody.Children.Add(new TextBlock { Text = playlists.Warning ?? "Playlists reference your MP3 files. Removing a track or playlist does not delete audio files.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        foreach (var list in playlists.Items)
        {
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(new TextBlock { Text = list.Name + " · " + list.Tracks.Count + " tracks", FontSize = 22 });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            void Action(string title, Func<Task> run, bool enabled = true)
            {
                var b = new Button { Content = title, IsEnabled = enabled && !playlistBusy };
                b.Click += async (_, _) => await PlaylistAction(run); actions.Children.Add(b);
            }
            Action("Play", async () => { playbackQueue = shufflePlaylist ? list.Tracks.OrderBy(_ => Random.Shared.Next()).ToList() : list.Tracks.ToList(); queueIndex = -1; await NextPlaylistTrack(); }, list.Tracks.Count > 0);
            Action("Export M3U", async () =>
            {
                var picker = new FileSavePicker { SuggestedFileName = list.Name }; picker.FileTypeChoices.Add("M3U8 playlist", new List<string> { ".m3u8" });
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSaveFileAsync(); if (file is not null) await File.WriteAllTextAsync(file.Path, PlaylistExchange.Write(list.Tracks));
            });
            Action("Add MP3s", async () =>
            {
                var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".mp3");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var files = await picker.PickMultipleFilesAsync(); if (closed || files.Count == 0) return;
                var tracks = list.Tracks.Concat(files.Select(f => f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                await playlists.SaveAsync(playlists.Items.Select(p => p.Id == list.Id ? p with { Tracks = tracks } : p).ToList());
            }, playlists.Warning is null);
            Action("Rename", async () =>
            {
                var name = new TextBox { Header = "Name", Text = list.Name, MaxLength = 100 };
                if (await Dialog("Rename playlist", name).ShowAsync() != ContentDialogResult.Primary) return;
                await playlists.SaveAsync(playlists.Items.Select(p => p.Id == list.Id ? p with { Name = name.Text.Trim() } : p).ToList());
            }, playlists.Warning is null);
            Action("Delete playlist", async () =>
            {
                if (await Dialog("Delete playlist?", new TextBlock { Text = "Only the playlist is removed. Your MP3 files are kept." }).ShowAsync() == ContentDialogResult.Primary)
                    await playlists.SaveAsync(playlists.Items.Where(p => p.Id != list.Id).ToList());
            }, playlists.Warning is null);
            body.Children.Add(actions);
            for (var i = 0; i < list.Tracks.Count; i++)
            {
                var index = i;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new TextBlock { Width = 280, TextTrimming = TextTrimming.CharacterEllipsis, Text = $"{i + 1}. {Path.GetFileName(list.Tracks[i])}" + (File.Exists(list.Tracks[i]) ? "" : " (missing)"), VerticalAlignment = VerticalAlignment.Center });
                void TrackAction(string label, Func<List<string>, List<string>> change, bool enabled)
                {
                    var b = new Button { Content = label, IsEnabled = enabled && playlists.Warning is null && !playlistBusy };
                    b.Click += async (_, _) => await PlaylistAction(() => playlists.SaveAsync(playlists.Items.Select(p => p.Id == list.Id ? p with { Tracks = change(list.Tracks.ToList()) } : p).ToList())); row.Children.Add(b);
                }
                TrackAction("↑", t => { (t[index - 1], t[index]) = (t[index], t[index - 1]); return t; }, i > 0);
                TrackAction("↓", t => { (t[index + 1], t[index]) = (t[index], t[index + 1]); return t; }, i < list.Tracks.Count - 1);
                TrackAction("Remove", t => { t.RemoveAt(index); return t; }, true);
                body.Children.Add(row);
            }
            MediaBody.Children.Add(new Border { Child = body, Padding = new Thickness(18), CornerRadius = new CornerRadius(12), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255,43,43,48)) });
        }
    }
    private async Task PlaylistAction(Func<Task> action)
    {
        if (playlistBusy) return;
        playlistBusy = true; RenderPlaylists();
        try { await action(); } catch (Exception ex) { if (!closed) ShowError(ex.Message); }
        finally { playlistBusy = false; if (!closed && mediaPage == "Playlists") RenderPlaylists(); }
    }
    private async Task NextPlaylistTrack()
    {
        if (closed) return;
        if (repeatPlaylist && queueIndex + 1 >= playbackQueue.Count && playbackQueue.Any(File.Exists)) queueIndex = -1;
        while (++queueIndex < playbackQueue.Count)
        {
            var path = playbackQueue[queueIndex];
            if (!File.Exists(path)) continue;
            try { await PlayLocalAsync(await StorageFile.GetFileFromPathAsync(path), true); return; }
            catch (Exception ex) { ShowError("Could not play playlist track: " + ex.Message); playbackQueue = []; queueIndex = -1; return; }
        }
        playbackQueue = []; queueIndex = -1;
        PlaybackStatus.Text = "Playlist ended. Missing files were skipped.";
    }
}

