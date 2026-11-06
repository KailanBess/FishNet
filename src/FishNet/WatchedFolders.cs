using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private readonly string foldersFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet", "watched-folders.json");
    private List<string> watchedFolders = [];
    private DispatcherQueueTimer? folderTimer;
    private bool scanningFolders;
    private string? folderWarning;

    private void StartFolderWatch()
    {
        try
        {
            if (File.Exists(foldersFile))
            {
                var saved = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(foldersFile));
                if (saved is null || saved.Count > 20 || saved.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
                    throw new InvalidDataException("Invalid watched folder list.");
                watchedFolders = saved.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { folderWarning = "Watched folder settings were preserved. " + ex.Message; return; }

        folderTimer = DispatcherQueue.CreateTimer();
        folderTimer.Interval = TimeSpan.FromSeconds(90);
        folderTimer.Tick += async (_, _) => await ScanWatchedFoldersAsync();
        folderTimer.Start();
        _ = ScanWatchedFoldersAsync();
    }

    private void SaveWatchedFolders(List<string> next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(foldersFile)!);
        File.WriteAllText(foldersFile + ".tmp", JsonSerializer.Serialize(next));
        File.Move(foldersFile + ".tmp", foldersFile, overwrite: true);
        watchedFolders = next;
    }

    private void AddFolderControls()
    {
        var watch = new Button { Content = "Watch media folder", IsEnabled = folderWarning is null && watchedFolders.Count < 20 };
        watch.Click += async (_, _) =>
        {
            watch.IsEnabled = false;
            try
            {
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null || closed || watchedFolders.Contains(folder.Path, StringComparer.OrdinalIgnoreCase)) return;
                SaveWatchedFolders(watchedFolders.Append(folder.Path).ToList());
                await ScanWatchedFoldersAsync();
                if (!closed && mediaPage == "Home") RenderMedia();
            }
            catch (Exception ex) { if (!closed) ShowError("Could not watch the folder. " + ex.Message); }
            finally { if (!closed) watch.IsEnabled = watchedFolders.Count < 20; }
        };
        MediaBody.Children.Add(watch);
        if (folderWarning is not null)
            MediaBody.Children.Add(new TextBlock { Text = folderWarning, TextWrapping = TextWrapping.Wrap });
        foreach (var path in watchedFolders)
        {
            var remove = new Button { Content = "Stop watching " + path };
            remove.Click += (_, _) =>
            {
                try { SaveWatchedFolders(watchedFolders.Where(folder => folder != path).ToList()); RenderMedia(); }
                catch (Exception ex) { ShowError("Could not update watched folders. " + ex.Message); }
            };
            MediaBody.Children.Add(remove);
        }
    }

    private async Task ScanWatchedFoldersAsync()
    {
        if (scanningFolders || closed || watchedFolders.Count == 0 || mediaCatalog.Warning is not null) return;
        scanningFolders = true;
        try
        {
            var roots = watchedFolders.ToArray();
            var files = await Task.Run(() => roots.Where(Directory.Exists).SelectMany(root =>
                Directory.EnumerateFiles(root, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                })).Where(path => MediaCatalog.IsAudio(path) || VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(10001).ToArray());
            if (closed || !roots.SequenceEqual(watchedFolders)) return;
            var previous = mediaCatalog.Items;
            await mediaCatalog.ImportAsync(files);
            if (!closed && !ReferenceEquals(previous, mediaCatalog.Items) && mediaPage is "Home" or "Music" or "Videos") RenderMedia();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { if (!closed && mediaPage == "Home") ShowError("Watched folder scan stopped. " + ex.Message); }
        finally { scanningFolders = false; }
    }

    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".mov", ".wmv", ".m4v", ".avi", ".webm"];
}
