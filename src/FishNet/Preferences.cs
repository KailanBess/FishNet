using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FishNet;

public sealed class Preferences
{
    public string Accent { get; set; } = "Mint";
    public string Background { get; set; } = "Charcoal";
    public bool Compact { get; set; }
}

// Separate from the catalog and library so appearance never rewrites app records.
public sealed class Appearance
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet", "appearance.json");
    private Preferences current = new();
    private bool writable = true;
    public string? Warning { get; private set; }
    public bool Compact => current.Compact;
    public void Load()
    {
        try
        {
            if (File.Exists(path)) current = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty preferences.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { writable = false; Warning = "Appearance settings could not be read and were preserved. " + ex.Message; }
    }
    public void Apply(FrameworkElement root)
    {
        var color = current.Accent switch
        {
            "Blue" => ColorHelper.FromArgb(255, 113, 190, 255),
            "Coral" => ColorHelper.FromArgb(255, 255, 170, 150),
            _ => ColorHelper.FromArgb(255, 103, 232, 211)
        };
        if (Application.Current.Resources["AquariumAccent"] is SolidColorBrush brush) brush.Color = color;
        root.Resources["SystemAccentColor"] = color;
        root.Resources["SystemControlHighlightAccentBrush"] = new SolidColorBrush(color);
        if (root is Panel panel) panel.Background = new SolidColorBrush(current.Background switch
        {
            "Charcoal" => ColorHelper.FromArgb(255, 26, 26, 31),
            "Midnight" => ColorHelper.FromArgb(255, 12, 14, 22),
            "Slate" => ColorHelper.FromArgb(255, 27, 35, 43),
            _ => ColorHelper.FromArgb(255, 9, 21, 29)
        });
    }
    public StackPanel Editor(FrameworkElement root, Action changed, Action<string> error)
    {
        var panel = new StackPanel { Spacing = 12 };
        var accent = new ComboBox { Header = "Accent", ItemsSource = new[] { "Mint", "Blue", "Coral" }, SelectedItem = current.Accent };
        var background = new ComboBox { Header = "Background", ItemsSource = new[] { "Charcoal", "Ocean", "Midnight", "Slate" }, SelectedItem = current.Background };
        var compact = new ToggleSwitch { Header = "Compact collection spacing", IsOn = current.Compact };
        async Task Save()
        {
            var next = new Preferences { Accent = accent.SelectedItem?.ToString() ?? "Mint", Background = background.SelectedItem?.ToString() ?? "Ocean", Compact = compact.IsOn };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(next));
                File.Move(path + ".tmp", path, true);
                current = next;
                Apply(root);
                changed();
            }
            catch (Exception ex) { error("Appearance was not saved: " + ex.Message); }
        }
        // Disable during a write so rapid events cannot collide on the temporary file.
        var save = new Button { Content = "Apply appearance", IsEnabled = writable };
        save.Click += async (_, _) => { save.IsEnabled = false; try { await Save(); } finally { save.IsEnabled = writable; } };
        panel.Children.Add(accent); panel.Children.Add(background); panel.Children.Add(compact); panel.Children.Add(save);
        if (Warning is not null) panel.Children.Add(new TextBlock { Text = Warning, TextWrapping = TextWrapping.Wrap });
        return panel;
    }
}

