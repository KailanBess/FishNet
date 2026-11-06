using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private readonly List<string> batchResults = [];
    private async Task ConvertBatch(bool toPlaylist = false, bool fromFolder = false)
    {
        if (converting) return;
        converting = true; cancelConversion = false; batchResults.Clear();
        var selected = toPlaylist ? "MP3" : conversionFormat;
        var completed = new List<string>();
        uint mp3Bitrate = 192000;
        try
        {
            IReadOnlyList<StorageFile> files;
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (fromFolder)
            {
                var sourceFolder = new FolderPicker();
                sourceFolder.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(sourceFolder, windowHandle);
                var album = await sourceFolder.PickSingleFolderAsync();
                if (album is null || closed) return;
                var contents = await album.GetFilesAsync(Windows.Storage.Search.CommonFileQuery.DefaultQuery, 0, 1001);
                if (contents.Count > 1000) throw new InvalidOperationException("Choose a folder with at most 1,000 files.");
                files = contents.Where(file => MediaCatalog.IsAudio(file.Path) || IsLocalVideo(file.Path))
                    .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (files.Count == 0) throw new InvalidOperationException("The folder contains no supported media files.");
            }
            else
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
                files = await picker.PickMultipleFilesAsync();
            }
            if (files.Count == 0 || closed) return;
            if (files.Count > 1000) throw new InvalidOperationException("Choose at most 1,000 files per batch.");
            var destination = new FolderPicker(); destination.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(destination, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await destination.PickSingleFolderAsync(); if (folder is null || closed) return;
            if (toPlaylist)
            {
                var preview = new TextBlock
                {
                    Text = string.Join(Environment.NewLine, files.Take(12).Select(file => Path.GetFileNameWithoutExtension(file.Name) + "-converted.mp3")) +
                        (files.Count > 12 ? $"{Environment.NewLine}… and {files.Count - 12} more" : "") +
                        $"{Environment.NewLine}Existing filenames receive a unique suffix.",
                    TextWrapping = TextWrapping.Wrap
                };
                var presets = new ComboBox
                {
                    Header = "MP3 preset", ItemsSource = new[] { "Compact · 128 kbps", "Balanced · 192 kbps", "High quality · 256 kbps" },
                    SelectedIndex = 1
                };
                var review = new StackPanel { Spacing = 8 };
                review.Children.Add(presets);
                review.Children.Add(preview);
                if (await Dialog("Review MP3 outputs", review).ShowAsync() != ContentDialogResult.Primary || closed || cancelConversion) return;
                mp3Bitrate = presets.SelectedIndex switch { 0 => 128000u, 2 => 256000u, _ => 192000u };
            }
            conversionCancellation = new CancellationTokenSource(); RenderConverter();
            foreach (var input in files)
            {
                if (cancelConversion || closed) break;
                StorageFile? temporary = null;
                try
                {
                    var extension = "." + selected.ToLowerInvariant();
                    temporary = await folder.CreateFileAsync(".fishnet-batch-" + Guid.NewGuid().ToString("N") + extension, CreationCollisionOption.FailIfExists);
                    await MediaConverter.TranscodeAsync(input, temporary, selected, conversionCancellation.Token, value => DispatcherQueue.TryEnqueue(() =>
                    {
                        if (closed) return;
                        conversionStatus = input.Name + $" · {value:0}%";
                        if (conversionStatusLabel is not null) conversionStatusLabel.Text = conversionStatus;
                        if (conversionProgress is not null) { conversionProgress.IsIndeterminate = false; conversionProgress.Value = value; }
                    }), mp3Bitrate: mp3Bitrate);
                    conversionCancellation.Token.ThrowIfCancellationRequested();
                    await temporary.RenameAsync(Path.GetFileNameWithoutExtension(input.Name) + "-converted" + extension, NameCollisionOption.GenerateUniqueName);
                    completed.Add(temporary.Path);
                    batchResults.Add("Saved · " + temporary.Path); temporary = null;
                }
                catch (OperationCanceledException) { batchResults.Add("Cancelled · " + input.Name); break; }
                catch (Exception ex) { batchResults.Add("Failed · " + input.Name + " · " + ex.Message); }
                finally { if (temporary is not null) { try { await temporary.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch { batchResults.Add("Temporary file remains: " + temporary.Path); } } }
            }
            conversionStatus = cancelConversion ? "Batch cancelled. Completed copies were kept." : "Batch finished. Review each file's result below.";
            if (toPlaylist && !closed && completed.Count > 0)
            {
                try { await SaveConvertedPlaylist(completed); }
                catch (Exception ex) { conversionStatus = "Converted copies saved. Playlist could not be saved: " + ex.Message; }
            }
        }
        catch (Exception ex) { conversionStatus = "Batch failed: " + ex.Message; }
        finally { converting = false; conversionCancellation?.Dispose(); conversionCancellation = null; if (!closed && mediaPage == "Converter") RenderConverter(); else if (!closed && mediaPage == "Playlists") RenderPlaylists(); }
    }

    private async Task SaveConvertedPlaylist(List<string> completed)
    {
        if (playlistBusy || playlists.Warning is not null) throw new InvalidOperationException("The playlist library is unavailable.");
        playlistBusy = true;
        try
        {
            var existing = playlists.Items.ToList();
            var target = new ComboBox { Header = "Destination", SelectedIndex = -1 };
            target.Items.Add("Create new playlist");
            foreach (var list in existing) target.Items.Add(list.Name);
            target.SelectedIndex = 0;
            var name = new TextBox { Header = "New playlist name", Text = "Converted MP3s", MaxLength = 100 };
            target.SelectionChanged += (_, _) => name.Visibility = target.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new TextBlock { Text = $"Add {completed.Count} converted MP3 files." });
            body.Children.Add(target);
            body.Children.Add(name);
            if (await Dialog("Save converted tracks to a playlist", body).ShowAsync() != ContentDialogResult.Primary || closed) return;
            if (target.SelectedIndex > 0)
            {
                var id = existing[target.SelectedIndex - 1].Id;
                await playlists.SaveAsync(existing.Select(list => list.Id == id
                    ? list with { Tracks = list.Tracks.Concat(completed).Distinct(StringComparer.OrdinalIgnoreCase).ToList() }
                    : list).ToList());
            }
            else
            {
                if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter a playlist name.");
                await playlists.SaveAsync([.. existing, new Playlist(Guid.NewGuid().ToString("N"), name.Text.Trim(), completed)]);
            }
            conversionStatus = $"Saved {completed.Count} converted MP3 files to the playlist.";
        }
        finally { playlistBusy = false; }
    }
}




