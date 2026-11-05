using System.Text.Json;

namespace FishNet;

public sealed record RecentMedia(string Path, string Name, string Kind, DateTimeOffset OpenedAt, double PositionSeconds = 0);
public sealed class MediaCatalog
{
    private readonly string path;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    public MediaCatalog(string? directory = null) => path = System.IO.Path.Combine(directory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet"), "media-library.json");
    public List<RecentMedia> Items { get; private set; } = [];
    public string? Warning { get; private set; }
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(path)) return;
            Items = JsonSerializer.Deserialize<List<RecentMedia>>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Invalid media library.");
            if (Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Path) || string.IsNullOrWhiteSpace(i.Name) || !double.IsFinite(i.PositionSeconds) || i.PositionSeconds < 0)) throw new InvalidDataException("Invalid media library entry.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { Items = []; Warning = "The media library could not be read and was preserved. " + ex.Message; }
    }
    public async Task RememberAsync(string file)
    {
        if (Warning is not null) return;
        await writeGate.WaitAsync();
        try
        {
        var audio = IsAudio(file);
        var position = Items.FirstOrDefault(i => i.Path.Equals(file, StringComparison.OrdinalIgnoreCase))?.PositionSeconds ?? 0;
        var next = Items.Where(i => !i.Path.Equals(file, StringComparison.OrdinalIgnoreCase)).ToList();
        next.Insert(0, new RecentMedia(file, System.IO.Path.GetFileNameWithoutExtension(file), audio ? "Music" : "Videos", DateTimeOffset.Now, position));
        await SaveAsync(next);
        }
        finally { writeGate.Release(); }
    }
    public async Task SavePositionAsync(string file, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Warning is not null) throw new InvalidOperationException(Warning);
        await writeGate.WaitAsync();
        try
        {
            if (!Items.Any(i => i.Path.Equals(file, StringComparison.OrdinalIgnoreCase))) return;
            await SaveAsync(Items.Select(i => i.Path.Equals(file, StringComparison.OrdinalIgnoreCase) ? i with { PositionSeconds = seconds } : i).ToList());
        }
        finally { writeGate.Release(); }
    }
    public async Task ImportAsync(IEnumerable<string> paths)
    {
        if (Warning is not null) throw new InvalidOperationException(Warning);
        await writeGate.WaitAsync();
        try
        {
        var next = Items.ToList(); var known = next.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in paths)
            if (known.Add(file)) next.Add(new(file, System.IO.Path.GetFileNameWithoutExtension(file), IsAudio(file) ? "Music" : "Videos", DateTimeOffset.MinValue));
        if (next.Count > 10000) throw new InvalidOperationException("The library limit is 10,000 files.");
        await SaveAsync(next);
        }
        finally { writeGate.Release(); }
    }
    public async Task RelinkAsync(string previous, string replacement)
    {
        if (Warning is not null) throw new InvalidOperationException(Warning);
        await writeGate.WaitAsync();
        try
        {
        await SaveAsync(Items.Where(i => !i.Path.Equals(replacement, StringComparison.OrdinalIgnoreCase)).Select(i => i.Path.Equals(previous, StringComparison.OrdinalIgnoreCase) ? i with { Path = replacement, Name = System.IO.Path.GetFileNameWithoutExtension(replacement), Kind = IsAudio(replacement) ? "Music" : "Videos" } : i).ToList());
        }
        finally { writeGate.Release(); }
    }
    public static bool IsAudio(string file) => new[] { ".mp3", ".m4a", ".wav", ".flac", ".wma", ".aac", ".ogg" }.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
    private async Task SaveAsync(List<RecentMedia> next)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true); Items = next;
    }
}
