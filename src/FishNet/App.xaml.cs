using Microsoft.UI.Xaml;

namespace FishNet;

public partial class App : Application
{
    private Window? window;
    private FileStream? profileLease;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { profileLease = ProfileLease.Acquire(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            window = new Window { Title = "FishNet profile unavailable", Content = new Microsoft.UI.Xaml.Controls.TextBlock
                { Text = "FishNet is already open, or its profile is unavailable. Close the other copy before trying again.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) } };
            window.Activate(); return;
        }
        window = new MainWindow();
        window.Closed += (_, _) => { profileLease?.Dispose(); profileLease = null; };
        window.Activate();
    }
}

