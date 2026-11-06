using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace FishNet;

public sealed partial class MainWindow
{
    private StorageFile? conversionInput;
    private bool converting;
    private bool cancelConversion;
    private CancellationTokenSource? conversionCancellation;
    private string conversionStatus = "Choose a local audio or video file. Audio formats extract the audio track from video.";
    private string conversionFormat = "MP3";
    private TextBlock? conversionStatusLabel;
    private ProgressBar? conversionProgress;

    private void RenderConverter()
    {
        MediaBody.Children.Clear();
        MediaBody.Children.Add(new TextBlock { Text = "Convert media", FontSize = 26 });
        MediaBody.Children.Add(new TextBlock { Text = "Local conversion using Windows codecs. Availability depends on your file and installed codecs; protected streams and service links are not inputs. Original files are kept.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        var choose = new Button { Content = conversionInput?.Name ?? "Choose source file", IsEnabled = !converting };
        choose.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker();
                foreach (var ext in new[] { ".mp3", ".wav", ".flac", ".m4a", ".wma", ".aac", ".ogg", ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".wmv", ".webm" }) picker.FileTypeFilter.Add(ext);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSingleFileAsync(); if (file is null || closed) return;
                conversionInput = file; RenderConverter();
            }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        MediaBody.Children.Add(choose);
        var format = new ComboBox { Header = "Output format", ItemsSource = new[] { "MP3", "M4A", "WAV", "FLAC", "MP4", "WMV" }, SelectedItem = conversionFormat, IsEnabled = !converting };
        format.SelectionChanged += (_, _) => conversionFormat = format.SelectedItem?.ToString() ?? "MP3";
        MediaBody.Children.Add(format);
        MediaBody.Children.Add(new TextBlock { Text = "Audio: high quality preset. MP3: 192 kbps. Video: 720p preset. A conversion cannot restore quality lost in the source.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        var start = new Button { Content = "Convert and save as…", IsEnabled = conversionInput is not null && !converting };
        start.Click += async (_, _) => await ConvertMedia(); MediaBody.Children.Add(start);
        var batch = new Button { Content = "Batch convert files…", IsEnabled = !converting };
        batch.Click += async (_, _) => await ConvertBatch(); MediaBody.Children.Add(batch);
        var playlistBatch = new Button { Content = "Convert to MP3 playlist…", IsEnabled = !converting && !playlistBusy && playlists.Warning is null };
        playlistBatch.Click += async (_, _) => await ConvertBatch(toPlaylist: true);
        MediaBody.Children.Add(playlistBatch);
        var album = new Button { Content = "Convert folder to MP3 playlist…", IsEnabled = playlistBatch.IsEnabled };
        album.Click += async (_, _) => await ConvertBatch(toPlaylist: true, fromFolder: true);
        MediaBody.Children.Add(album);
        var cancel = new Button { Content = "Cancel conversion", IsEnabled = converting };
        cancel.Click += (_, _) => { cancelConversion = true; conversionCancellation?.Cancel(); conversionStatus = "Cancelling…"; if (conversionStatusLabel is not null) conversionStatusLabel.Text = conversionStatus; };
        MediaBody.Children.Add(cancel);
        conversionProgress = new ProgressBar { Minimum = 0, Maximum = 100, IsIndeterminate = converting };
        conversionStatusLabel = new TextBlock { Text = conversionStatus, TextWrapping = TextWrapping.Wrap };
        MediaBody.Children.Add(conversionProgress); MediaBody.Children.Add(conversionStatusLabel);
        foreach (var result in batchResults) MediaBody.Children.Add(new TextBlock { Text = result, TextWrapping = TextWrapping.Wrap });
    }
    private async Task ConvertMedia()
    {
        if (converting || conversionInput is null) return;
        converting = true; cancelConversion = false;
        var input = conversionInput; var selected = conversionFormat;
        conversionStatus = "Choose where to save the converted copy."; RenderConverter();
        StorageFile? temporary = null;
        try
        {
            var extension = "." + selected.ToLowerInvariant();
            var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(input.Name) + "-converted" };
            picker.FileTypeChoices.Add(selected, new List<string> { extension });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var output = await picker.PickSaveFileAsync();
            if (output is null || closed || cancelConversion) { conversionStatus = "Conversion cancelled."; return; }
            if (output.Path.Equals(input.Path, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a different destination from the source file.");
            var folder = await output.GetParentAsync();
            temporary = await folder.CreateFileAsync(".fishnet-" + Guid.NewGuid().ToString("N") + extension, CreationCollisionOption.FailIfExists);
            conversionCancellation = new CancellationTokenSource();
            if (cancelConversion || closed) conversionCancellation.Cancel();
            await MediaConverter.TranscodeAsync(input, temporary, selected, conversionCancellation.Token, value => DispatcherQueue.TryEnqueue(() =>
            {
                if (closed) return;
                conversionStatus = $"Converting… {Math.Clamp(value, 0, 100):0}%";
                if (conversionStatusLabel is not null) conversionStatusLabel.Text = conversionStatus;
                if (conversionProgress is not null) { conversionProgress.IsIndeterminate = false; conversionProgress.Value = value; }
            }));
            if (cancelConversion || closed) { conversionStatus = "Conversion cancelled."; return; }
            await temporary.MoveAndReplaceAsync(output); temporary = null;
            conversionStatus = "Saved: " + output.Path;
        }
        catch (OperationCanceledException) { conversionStatus = "Conversion cancelled. No converted media was saved."; }
        catch (Exception ex) { conversionStatus = cancelConversion ? "Conversion cancelled." : "Conversion failed: " + ex.Message; }
        finally
        {
            conversionCancellation?.Dispose(); conversionCancellation = null;
            if (temporary is not null)
            {
                try { await temporary.DeleteAsync(StorageDeleteOption.PermanentDelete); }
                catch { conversionStatus += " Temporary output could not be removed: " + temporary.Path; }
            }
            converting = false;
            if (!closed && mediaPage == "Converter") RenderConverter();
        }
    }
}



