using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FishNet;

public sealed partial class MainWindow
{
    private readonly LocalActivity localActivity = new(ProfilePaths.DataDirectory, AppContext.BaseDirectory);
    private readonly CancellationTokenSource activityCancellation = new();
    private bool activityReviewRunning;
    private void AddActivityControls()
    {
        AppearanceBody.Children.Add(new TextBlock { Text = "Local activity history", FontSize = 22, TextWrapping = TextWrapping.Wrap });
        AppearanceBody.Children.Add(new TextBlock { Text = "Keeps up to 500 app-start and error-notice events. Only UTC times, fixed codes, levels, IDs and app versions are stored. Notice text, media names/paths, URLs, accounts and exception details are omitted. This is not playback history, crash collection or notification history. Nothing uploads automatically; these records are separate from Aquarium record backups.", TextWrapping = TextWrapping.Wrap });
        var review = new Button { Content = "Review local activity…" };
        review.Click += async (_, _) => await ReviewActivityAsync();
        AppearanceBody.Children.Add(review);
    }
    private async Task ReviewActivityAsync()
    {
        if (activityReviewRunning || closed) return;
        activityReviewRunning = true;
        try
        {
            var snapshot = await Task.Run(localActivity.Read);
            if (closed) return;
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(new TextBlock { Text = $"{snapshot.Events.Count} retained events · {snapshot.DroppedEntries} older events dropped. Latest first. " +
                (localActivity.HasMissedEvents ? "Some events were not recorded this session; storage failure, cancellation or the pending-write limit occurred." : "Only declared event kinds are collected; this is not a complete activity audit."), TextWrapping = TextWrapping.Wrap });
            var events = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 340,
                Text = snapshot.Events.Count == 0 ? "No retained events." : string.Join("\n", snapshot.Events.Reverse().Select(e => $"{e.RecordedAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'} · {e.Level} · {e.Code} · v{e.ApplicationVersion}")) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(events, "Retained local activity events"); body.Children.Add(events);
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Review local activity", Content = new ScrollViewer { Content = body, MaxHeight = 480 },
                PrimaryButtonText = "Export reviewed JSON…", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id) { SuggestedFileName = "FishNet-activity", DefaultFileExtension = ".json" };
            picker.FileTypeChoices.Add("Activity history", new List<string> { ".json" });
            var file = await picker.PickSaveFileAsync(); if (file is null || closed) return;
            await localActivity.ExportAsync(file.Path, snapshot, activityCancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) ShowError("Activity history is unavailable or could not be exported. Existing history was preserved. Choose a JSON file outside FishNet's profile and program folders."); }
        finally { activityReviewRunning = false; }
    }
}
