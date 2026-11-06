using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FishNet;

public sealed class Preferences
{
    public string Accent { get; set; } = "Aquarium Mint";
    public string Background { get; set; } = "Ocean";
    public bool Compact { get; set; }
    public string WindowBorder { get; set; } = "System";
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
    private static readonly Dictionary<string, Windows.UI.Color> Palette = new()
    {
        ["Ocean"] = ColorHelper.FromArgb(255, 89, 190, 255),
        ["Sunset"] = ColorHelper.FromArgb(255, 255, 158, 79),
        ["Amethyst"] = ColorHelper.FromArgb(255, 177, 122, 255),
        ["Rose"] = ColorHelper.FromArgb(255, 228, 100, 181),
        ["Lime"] = ColorHelper.FromArgb(255, 155, 213, 98),
        ["Gold"] = ColorHelper.FromArgb(255, 236, 178, 62),
        ["Ice"] = ColorHelper.FromArgb(255, 103, 198, 232),
        ["Cobalt"] = ColorHelper.FromArgb(255, 77, 127, 255),
        ["Indigo"] = ColorHelper.FromArgb(255, 129, 114, 237),
        ["Violet"] = ColorHelper.FromArgb(255, 193, 86, 237),
        ["Orchid"] = ColorHelper.FromArgb(255, 225, 143, 234),
        ["Magenta"] = ColorHelper.FromArgb(255, 240, 92, 203),
        ["Ruby"] = ColorHelper.FromArgb(255, 240, 91, 112),
        ["Coral"] = ColorHelper.FromArgb(255, 255, 130, 115),
        ["Tangerine"] = ColorHelper.FromArgb(255, 255, 171, 50),
        ["Amber"] = ColorHelper.FromArgb(255, 240, 196, 70),
        ["Lemon"] = ColorHelper.FromArgb(255, 230, 223, 103),
        ["Emerald"] = ColorHelper.FromArgb(255, 67, 200, 134),
        ["Mint"] = ColorHelper.FromArgb(255, 131, 226, 188),
        ["Teal"] = ColorHelper.FromArgb(255, 59, 197, 186),
        ["Turquoise"] = ColorHelper.FromArgb(255, 64, 216, 228),
        ["Sapphire"] = ColorHelper.FromArgb(255, 72, 158, 221),
        ["Lavender"] = ColorHelper.FromArgb(255, 193, 166, 235),
        ["Peach"] = ColorHelper.FromArgb(255, 241, 177, 154),
        ["Copper"] = ColorHelper.FromArgb(255, 207, 145, 101),
        ["Silver"] = ColorHelper.FromArgb(255, 195, 203, 216),
        ["Pearl"] = ColorHelper.FromArgb(255, 245, 239, 228),
        ["Graphite"] = ColorHelper.FromArgb(255, 135, 149, 166),
        ["Aquarium Mint"] = ColorHelper.FromArgb(255, 103, 232, 211),
        ["Aquarium Blue"] = ColorHelper.FromArgb(255, 113, 190, 255),
        ["Aquarium Coral"] = ColorHelper.FromArgb(255, 255, 170, 150),
    };

    public Windows.UI.Color Accent => Palette.GetValueOrDefault(AccentName, Palette["Aquarium Mint"]);
    private string AccentName => current.Accent == "Blue" ? "Aquarium Blue" : current.Accent ?? "Aquarium Mint";
    public Windows.UI.Color? BorderColor => current.WindowBorder switch
    {
        "Match accent" => Accent,
        string name when Palette.TryGetValue(name, out var color) => color,
        _ => null
    };
    public bool HideBorder => current.WindowBorder == "Hidden";

    private (Windows.UI.Color Page, Windows.UI.Color Surface) Background => current.Background switch
    {
        "Midnight" => (ColorHelper.FromArgb(255, 12, 14, 22), ColorHelper.FromArgb(255, 30, 32, 46)),
        "Slate" => (ColorHelper.FromArgb(255, 27, 35, 43), ColorHelper.FromArgb(255, 45, 56, 65)),
        "Kelp" => (ColorHelper.FromArgb(255, 12, 31, 26), ColorHelper.FromArgb(255, 23, 52, 43)),
        "Plum" => (ColorHelper.FromArgb(255, 33, 21, 43), ColorHelper.FromArgb(255, 54, 37, 66)),
        _ => (ColorHelper.FromArgb(255, 9, 21, 29), ColorHelper.FromArgb(255, 16, 36, 47))
    };

    public SolidColorBrush PageBrush => new(Background.Page);
    public SolidColorBrush SurfaceBrush => new(Background.Surface);

    public StackPanel Editor(Action changed, Action<string> error)
    {
        var panel = new StackPanel { Spacing = 12 };
        var accent = new ComboBox { Header = "Accent", ItemsSource = Palette.Keys.ToArray(), SelectedItem = AccentName };
        var background = new ComboBox { Header = "Background", ItemsSource = new[] { "Ocean", "Midnight", "Slate", "Kelp", "Plum" }, SelectedItem = current.Background };
        var border = new ComboBox { Header = "Window border", ItemsSource = new[] { "System", "Match accent", "Hidden" }.Concat(Palette.Keys).ToArray(), SelectedItem = current.WindowBorder };
        var compact = new ToggleSwitch { Header = "Compact collection spacing", IsOn = current.Compact };
        async Task Save()
        {
            var next = new Preferences { Accent = accent.SelectedItem?.ToString() ?? "Aquarium Mint", Background = background.SelectedItem?.ToString() ?? "Ocean", Compact = compact.IsOn, WindowBorder = border.SelectedItem?.ToString() ?? "System" };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(next));
                File.Move(path + ".tmp", path, true);
                current = next;
                changed();
            }
            catch (Exception ex) { error("Appearance was not saved: " + ex.Message); }
        }
        // Disable during a write so rapid events cannot collide on the temporary file.
        var save = new Button { Content = "Apply appearance", IsEnabled = writable };
        save.Click += async (_, _) => { save.IsEnabled = false; try { await Save(); } finally { save.IsEnabled = writable; } };
        panel.Children.Add(accent); panel.Children.Add(background); panel.Children.Add(border); panel.Children.Add(compact); panel.Children.Add(save);
        if (Warning is not null) panel.Children.Add(new TextBlock { Text = Warning, TextWrapping = TextWrapping.Wrap });
        return panel;
    }
}
