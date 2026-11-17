using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private ConversionQueue? batchQueue;
    private string? conversionQueueWarning;
    private bool queueLoaded;
    private async Task ConvertBatch(bool toPlaylist = false, bool fromFolder = false)
    {
        EnsureQueueLoaded();
        if (converting || conversionQueueWarning is not null || batchQueue?.Jobs.Any(job => job.Output is null) == true) return;
        converting = true; cancelConversion = false;
        var selected = toPlaylist ? "MP3" : conversionFormat;
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
            var extension = "." + selected.ToLowerInvariant();
            string OutputName(StorageFile input) => Path.GetFileNameWithoutExtension(input.Name) + "-converted" + extension;
            var preview = new TextBlock
            {
                Text = string.Join(Environment.NewLine, files.Take(12).Select(OutputName)) +
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
            if (toPlaylist) review.Children.Add(presets);
            review.Children.Add(preview);
            if (await Dialog("Review batch outputs", review).ShowAsync() != ContentDialogResult.Primary || closed || cancelConversion) return;
            if (toPlaylist)
                mp3Bitrate = presets.SelectedIndex switch { 0 => 128000u, 2 => 256000u, _ => 192000u };
            var queue = new ConversionQueue(folder.Path, selected, mp3Bitrate, toPlaylist,
                files.Select(file => new ConversionJob(file.Path)).ToList());
            using var writer = ConversionQueue.AcquireWriter();
            batchQueue = ConversionQueue.Load();
            if (batchQueue?.Jobs.Any(job => job.Output is null) == true)
                throw new InvalidOperationException("Another FishNet window saved an unfinished batch. Review that queue before starting a new batch.");
            queue.Save();
            batchQueue = queue;
            await RunQueuedJobs(queue, Enumerable.Range(0, queue.Jobs.Count));
        }
        catch (Exception ex) { conversionStatus = "Batch stopped: " + ex.Message; }
        finally { FinishQueueRun(); }
    }

    private void EnsureQueueLoaded()
    {
        if (queueLoaded) return;
        queueLoaded = true;
        try { batchQueue = ConversionQueue.Load(); }
        catch (Exception ex) { conversionQueueWarning = "The saved batch queue could not be read. " + ex.Message; }
    }

    private async Task ResumeQueue(int? index = null)
    {
        if (converting || batchQueue is not { } queue) return;
        converting = true;
        cancelConversion = false;
        try
        {
            using var writer = ConversionQueue.AcquireWriter();
            CheckCurrentQueue(queue);
            var positions = index is int jobIndex ? new[] { jobIndex } : Enumerable.Range(0, queue.Jobs.Count);
            await RunQueuedJobs(queue, positions);
        }
        catch (Exception ex) { conversionStatus = "Batch stopped: " + ex.Message; }
        finally { FinishQueueRun(); }
    }

    private void CheckCurrentQueue(ConversionQueue shown)
    {
        var saved = ConversionQueue.Load();
        if (shown.Matches(saved)) return;
        batchQueue = saved;
        throw new InvalidOperationException("The queue changed in another FishNet window. Review the refreshed queue before trying again.");
    }

    private void FinishQueueRun()
    {
        converting = false;
        conversionCancellation?.Dispose();
        conversionCancellation = null;
        if (!closed && mediaPage == "Converter") RenderConverter();
    }

    private async Task RunQueuedJobs(ConversionQueue queue, IEnumerable<int> positions)
    {
        var folder = await StorageFolder.GetFolderFromPathAsync(queue.Destination);
        var extension = "." + queue.Format.ToLowerInvariant();
        var cancellation = new CancellationTokenSource();
        conversionCancellation = cancellation;
        if (mediaPage == "Converter") RenderConverter();
        var completed = new List<string>();
        foreach (var position in positions)
        {
            if (cancelConversion || closed) break;
            var job = queue.Jobs[position];
            if (job.Output is not null) continue;
            StorageFile? temporary = null;
            ConversionJob result;
            try
            {
                var input = await StorageFile.GetFileFromPathAsync(job.Input);
                temporary = await folder.CreateFileAsync(".fishnet-batch-" + Guid.NewGuid().ToString("N") + extension, CreationCollisionOption.FailIfExists);
                await MediaConverter.TranscodeAsync(input, temporary, queue.Format, cancellation.Token, value => DispatcherQueue.TryEnqueue(() =>
                {
                    if (closed || conversionCancellation != cancellation) return;
                    conversionStatus = input.Name + $" · {value:0}%";
                    if (conversionStatusLabel is not null) conversionStatusLabel.Text = conversionStatus;
                    if (conversionProgress is not null) { conversionProgress.IsIndeterminate = false; conversionProgress.Value = value; }
                }), mp3Bitrate: queue.Mp3Bitrate);
                cancellation.Token.ThrowIfCancellationRequested();
                await temporary.RenameAsync(Path.GetFileNameWithoutExtension(input.Name) + "-converted" + extension, NameCollisionOption.GenerateUniqueName);
                result = job with { Output = temporary.Path, Error = null };
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                if (temporary is not null) await CleanupQueueCopy(temporary);
                break;
            }
            catch (Exception ex)
            {
                if (temporary is not null) await CleanupQueueCopy(temporary);
                result = job with { Error = ex.Message };
            }

            queue.Jobs[position] = result;
            try { queue.Save(); }
            catch
            {
                queue.Jobs[position] = job;
                if (result.Output is not null && temporary is not null) await CleanupQueueCopy(temporary);
                throw;
            }
            if (result.Output is not null) completed.Add(result.Output);
        }

        conversionStatus = cancelConversion || closed ? "Paused. Completed copies and remaining jobs were kept."
            : "Batch finished. Review each file's result below.";
        if (queue.ToPlaylist && !closed && mediaPage == "Converter" && completed.Count > 0)
        {
            try { await SaveConvertedPlaylist(completed); }
            catch (Exception ex) { conversionStatus = "Converted copies saved. Playlist could not be saved: " + ex.Message; }
        }
    }

    private static async Task CleanupQueueCopy(StorageFile file)
    {
        try { await file.DeleteAsync(StorageDeleteOption.PermanentDelete); }
        catch (Exception ex) { throw new IOException("Could not remove the unfinished output " + file.Path, ex); }
    }

    private async Task SaveConvertedPlaylist(List<string> completed)
    {
        if (playlistBusy || playlists.Warning is not null) throw new InvalidOperationException("The playlist library is unavailable.");
        playlistBusy = true;
        try
        {
            var existing = playlists.Items.ToList();
            var manual = existing.Where(list => list.Rule is null).ToArray();
            var target = new ComboBox { Header = "Destination", SelectedIndex = -1 };
            target.Items.Add("Create new playlist");
            foreach (var list in manual) target.Items.Add(list.Name);
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
                var id = manual[target.SelectedIndex - 1].Id;
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




