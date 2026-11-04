using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private readonly List<string> batchResults = [];
    private async Task ConvertBatch()
    {
        if (converting) return;
        converting = true; cancelConversion = false; batchResults.Clear();
        var selected = conversionFormat;
        try
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var files = await picker.PickMultipleFilesAsync(); if (files.Count == 0 || closed) return;
            if (files.Count > 1000) throw new InvalidOperationException("Choose at most 1,000 files per batch.");
            var destination = new FolderPicker(); destination.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(destination, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await destination.PickSingleFolderAsync(); if (folder is null || closed) return;
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
                    }));
                    conversionCancellation.Token.ThrowIfCancellationRequested();
                    await temporary.RenameAsync(Path.GetFileNameWithoutExtension(input.Name) + "-converted" + extension, NameCollisionOption.GenerateUniqueName);
                    batchResults.Add("Saved · " + temporary.Path); temporary = null;
                }
                catch (OperationCanceledException) { batchResults.Add("Cancelled · " + input.Name); break; }
                catch (Exception ex) { batchResults.Add("Failed · " + input.Name + " · " + ex.Message); }
                finally { if (temporary is not null) { try { await temporary.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch { batchResults.Add("Temporary file remains: " + temporary.Path); } } }
            }
            conversionStatus = cancelConversion ? "Batch cancelled. Completed copies were kept." : "Batch finished. Review each file's result below.";
        }
        catch (Exception ex) { conversionStatus = "Batch failed: " + ex.Message; }
        finally { converting = false; conversionCancellation?.Dispose(); conversionCancellation = null; if (!closed && mediaPage == "Converter") RenderConverter(); }
    }
}
