using Aquarium.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Text.Json;

namespace FishNet;

public sealed partial class MainWindow
{
    private CancellationTokenSource? photoDuplicateCancellation;
    private sealed class DuplicateProgress(Action<PhotoDuplicateProgress> update) : IProgress<PhotoDuplicateProgress>
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long last = -100;
        public void Report(PhotoDuplicateProgress value)
        {
            if (clock.ElapsedMilliseconds - last < 100 && value.CompletedPaths != value.TotalPaths) return;
            last = clock.ElapsedMilliseconds; update(value);
        }
    }
    private async Task ComparePhotosAsync()
    {
        if (visualBusy || closed) return;
        var paths = VisualEntries().Select(item => item.Path).ToArray();
        if (paths.Length < 2) return;
        using var cancellation = new CancellationTokenSource();
        photoDuplicateCancellation = cancellation; visualBusy = true; slideshow.Stop();
        try
        {
            var status = new TextBlock { Text = "Reading matching photo files…", TextWrapping = TextWrapping.Wrap };
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Compare matching photo files",
                Content = status, CloseButtonText = "Cancel" };
            dialog.CloseButtonClick += (_, _) => cancellation.Cancel();
            var display = dialog.ShowAsync().AsTask();
            var progress = new DuplicateProgress(value => DispatcherQueue.TryEnqueue(() =>
            {
                if (!closed) status.Text = $"Compared {value.CompletedPaths:N0} of {value.TotalPaths:N0} paths · {value.BytesRead / 1048576.0:F1} MiB read. Files are never changed.";
            }));
            var scan = Task.Run(() => PhotoDuplicates.CompareAsync(paths, cancellation.Token, progress));
            PhotoDuplicateReport report;
            try
            {
                if (await Task.WhenAny(scan, display) == display) cancellation.Cancel();
                report = await scan;
            }
            finally { if (!closed) dialog.Hide(); await display; }
            if (closed || cancellation.IsCancellationRequested || mediaPage != "Photos") return;
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(new TextBlock { Text = $"{report.Groups.Count:N0} matching-content groups · {report.HashedFiles:N0} files hashed · {report.Skipped.Count:N0} skipped. " +
                (report.Complete ? "All selected paths were compared." : "This scan is partial; skipped files remain unknown."), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = "Matches use file length and SHA-256, not visual similarity. Linked paths are refused; separate hard links may share storage. Files can change after scanning. No files or catalog records are removed. The JSON report includes local file paths.", TextWrapping = TextWrapping.Wrap });
            foreach (var group in report.Groups.Take(100)) body.Children.Add(new TextBlock
            {
                Text = $"{group.Bytes:N0} bytes each\n" + string.Join("\n", group.Paths.Take(20)) +
                    (group.Paths.Count > 20 ? $"\n{group.Paths.Count - 20:N0} more paths in the saved report." : ""),
                TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
            });
            if (report.Groups.Count > 100) body.Children.Add(new TextBlock { Text = "Showing the first 100 groups. Save the full report or refine the album/search filters." });
            var review = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Photo duplicate review", Content = new ScrollViewer { Content = body, MaxHeight = 480 },
                PrimaryButtonText = "Save JSON report…", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close };
            if (await review.ShowAsync() != ContentDialogResult.Primary || closed) return;
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id)
                { SuggestedFileName = "FishNet-photo-duplicates", DefaultFileExtension = ".json" };
            picker.FileTypeChoices.Add("JSON report", new List<string> { ".json" });
            var file = await picker.PickSaveFileAsync(); if (file is null || closed) return;
            if (!Path.GetExtension(file.Path).Equals(".json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Save a JSON report.");
            var profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProfilePaths.DataDirectory)) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(file.Path).StartsWith(profile, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Save reports outside the active FishNet profile folder.");
            var data = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true });
            await AtomicFile.WriteAsync(file.Path, (output, token) => output.WriteAsync(data, token).AsTask(), cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed) ShowError("Photo comparison stopped: " + ex.Message); }
        finally
        {
            photoDuplicateCancellation = null; visualBusy = false;
            if (!closed && mediaPage is "Photos" or "Documents") RenderVisualLibrary();
        }
    }
}
