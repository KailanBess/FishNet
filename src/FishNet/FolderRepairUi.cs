using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private bool repairPublishing;
    private async Task RepairMediaFolder()
    {
        if (mediaToolsBusy || scanningFolders || source is not null || podcastBusy || converting || playlistBusy)
        { ShowError("Close playback and let current imports, podcast downloads or conversions finish before repairing folders."); return; }
        if (mediaCatalog.Warning is not null || playlists.Warning is not null || queueWarning is not null || folderWarning is not null)
        { ShowError("Resolve the unreadable media records before repairing folders."); return; }
        mediaToolsBusy = true; folderTimer?.Stop(); OpenButton.IsEnabled = false;
        try
        {
            var oldRoot = new TextBox { Header = "Previous folder path", PlaceholderText = @"D:\Media" };
            string? replacementRoot = null;
            var browse = new Button { Content = "Choose replacement folder" };
            var location = new TextBlock { TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
            browse.Click += async (_, _) =>
            {
                try
                {
                    var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    var folder = await picker.PickSingleFolderAsync();
                    if (folder is not null) { replacementRoot = folder.Path; location.Text = folder.Path; }
                }
                catch (Exception ex) { location.Text = ex.Message; }
            };
            var body = new StackPanel { Spacing = 10, Children = { oldRoot, browse, location, new TextBlock
                { Text = "Matches missing files by their relative folder paths. Files are never moved or renamed. A backup of the records is kept before applying.", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap } } };
            if (await Dialog("Repair a moved media folder", body).ShowAsync() != ContentDialogResult.Primary || replacementRoot is null || closed) return;
            var allPaths = mediaCatalog.Items.Select(i => i.Path).Concat(playlists.Items.SelectMany(p => p.Tracks)).Concat(playbackQueue);
            var changes = FolderRepair.Preview(allPaths, oldRoot.Text, replacementRoot);
            var folderChanges = FolderRepair.FolderReferences(watchedFolders.Concat(playlists.Items.Where(p => p.Rule is not null).Select(p => p.Rule!.Folder)), oldRoot.Text, replacementRoot);
            if (changes.Count == 0) { ShowError("No missing files matched the same relative paths in that folder."); return; }
            var review = new ContentDialog { XamlRoot = Root.XamlRoot, Title = $"Repair {changes.Count} media paths?",
                Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = string.Join("\n\n", changes.Take(100).Select(c => c.Previous + "\n→ " + c.Replacement)) + (changes.Count > 100 ? "\nMore matches are included in the saved repair record." : ""), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap } },
                PrimaryButtonText = "Apply reviewed repair", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await review.ShowAsync() != ContentDialogResult.Primary || closed) return;
            MediaNavigation.IsEnabled = false;
            repairPublishing = true;
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet");
            var writes = new List<RepairWrite>();
            var before = new Dictionary<string, byte[]>();
            void Add(string file, IReadOnlyList<MediaPathChange> replacements)
            {
                var path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) return;
                if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("A media record is too large.");
                var data = File.ReadAllBytes(path); before[file] = data;
                writes.Add(new(file, FolderRepair.Rewrite(data, replacements)));
            }
            foreach (var file in new[] { "media-library.json", "play-queue.json" }) Add(file, changes);
            Add("playlists.json", changes.Concat(folderChanges).DistinctBy(c => c.Previous, StringComparer.OrdinalIgnoreCase).ToArray());
            Add("watched-folders.json", folderChanges);
            foreach (var change in changes)
            {
                var previous = Path.Combine(root, FolderRepair.PositionFile(change.Previous).Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(previous))
                {
                    var next = FolderRepair.PositionFile(change.Replacement);
                    if (File.Exists(Path.Combine(root, next.Replace('/', Path.DirectorySeparatorChar)))) continue;
                    writes.Add(new(next, FolderRepair.Rewrite(File.ReadAllBytes(previous), [change])));
                }
            }
            var backup = Path.Combine(root, "repair-backups", Guid.NewGuid().ToString("N") + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.WriteAllText(backup, JsonSerializer.Serialize(new { Records = before, Changes = changes }));
            await Task.Run(() => FolderRepair.Apply(root, writes));
            await mediaCatalog.LoadAsync(); await playlists.LoadAsync(); LoadQueue();
            watchedFolders = watchedFolders.Select(p => folderChanges.FirstOrDefault(c => c.Previous.Equals(p, StringComparison.OrdinalIgnoreCase))?.Replacement ?? p).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Notice.Severity = InfoBarSeverity.Success; Notice.Message = $"Repaired {changes.Count} paths in the library, playlists, queue and saved playback positions. Previous records: {backup}"; Notice.IsOpen = true;
        }
        catch (Exception ex) { if (!closed) ShowError("Folder repair stopped. " + ex.Message); }
        finally { repairPublishing = false; mediaToolsBusy = false; if (!closed) { folderTimer?.Start(); MediaNavigation.IsEnabled = OpenButton.IsEnabled = true; RenderMedia(); } }
    }
}
