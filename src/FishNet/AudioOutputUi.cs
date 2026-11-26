using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace FishNet;

public sealed partial class MainWindow
{
    private DeviceWatcher? audioDevices;
    private string? selectedAudioId;
    private string playbackDiagnostic = "No playback failure reported in this session.";
    private readonly string audioSettings = Path.Combine(ProfilePaths.DataDirectory, "audio-output.json");

    private void AddAudioOutputControls()
    {
        var body = new StackPanel { Spacing = 8 };
        var choose = new ComboBox { Header = "Audio output", HorizontalAlignment = HorizontalAlignment.Stretch };
        var refresh = new Button { Content = "Refresh audio outputs" };
        var status = new TextBlock { Text = "Loading outputs…", TextWrapping = TextWrapping.Wrap };
        var available = new List<DeviceInformation>();
        bool loading = true;
        async Task Load()
        {
            refresh.IsEnabled = choose.IsEnabled = false; loading = true;
            try
            {
                available = (await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector())).ToList();
                if (closed || !body.IsLoaded) return;
                choose.ItemsSource = new[] { "Windows default" }.Concat(available.Select(d => d.Name)).ToArray();
                choose.SelectedIndex = selectedAudioId is null ? 0 : available.FindIndex(d => d.Id == selectedAudioId) + 1;
                status.Text = selectedAudioId is not null && !available.Any(d => d.Id == selectedAudioId)
                    ? "The preferred output is unavailable. Playback uses the Windows default. Choose an output to change the preference."
                    : "Output changes apply only to FishNet. Refresh after connecting a device.";
            }
            catch (Exception ex) { status.Text = "Could not enumerate outputs: " + ex.Message; }
            finally { loading = false; if (!closed) refresh.IsEnabled = choose.IsEnabled = true; }
        }
        choose.SelectionChanged += (_, _) =>
        {
            if (loading || choose.SelectedIndex < 0) return;
            try
            {
                var device = choose.SelectedIndex == 0 ? null : available[choose.SelectedIndex - 1];
                player.AudioDevice = device;
                selectedAudioId = device?.Id;
                Directory.CreateDirectory(Path.GetDirectoryName(audioSettings)!);
                File.WriteAllText(audioSettings + ".tmp", JsonSerializer.Serialize(selectedAudioId));
                File.Move(audioSettings + ".tmp", audioSettings, true);
                status.Text = "Audio output saved.";
            }
            catch (Exception ex) { status.Text = "Could not save or change output: " + ex.Message; }
        };
        refresh.Click += async (_, _) => await Load();
        body.Loaded += async (_, _) => await Load();
        body.Children.Add(choose); body.Children.Add(refresh); body.Children.Add(status);
        body.Children.Add(new TextBlock { Text = "Playback diagnostics: " + playbackDiagnostic, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new HyperlinkButton { Content = "Windows media playback information", NavigateUri = new Uri("https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/play-audio-and-video-with-mediaplayer") });
        MediaBody.Children.Add(body);
    }

    private async Task InitializeAudioOutput()
    {
        try
        {
            if (File.Exists(audioSettings))
            {
                if (new FileInfo(audioSettings).Length > 32768) throw new InvalidDataException("Audio output preference is too large.");
                selectedAudioId = JsonSerializer.Deserialize<string?>(await File.ReadAllTextAsync(audioSettings));
                var available = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
                if (closed) return;
                player.AudioDevice = available.FirstOrDefault(d => d.Id == selectedAudioId);
            }
            if (closed) return;
            audioDevices = DeviceInformation.CreateWatcher(MediaDevice.GetAudioRenderSelector());
            audioDevices.Removed += AudioDeviceRemoved;
            audioDevices.Start();
        }
        catch (Exception ex) { if (!closed) ShowError("Audio output preference could not be applied. Windows default is available. " + ex.Message); }
    }

    private void AudioDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate device) => DispatcherQueue.TryEnqueue(() =>
    {
        if (closed || device.Id != selectedAudioId) return;
        try { player.AudioDevice = null; ShowError("The selected audio output was disconnected. FishNet switched to the Windows default."); }
        catch (Exception ex) { ShowError("Audio output disconnected. Select an available output in Playback tools. " + ex.Message); }
    });
}
