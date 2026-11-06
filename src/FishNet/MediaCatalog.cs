using System.Text.Json;

namespace FishNet;

public sealed record RecentMedia(string Path, string Name, string Kind, DateTimeOffset OpenedAt);
public sealed class MediaCatalog
{
    private readonly string path;
    private readonly SemaphoreSlim writes = new(1, 1);
    public MediaCatalog(string? directory = null) => path = System.IO.Path.Combine(directory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet"), "media-library.json");
    public List<RecentMedia> Items { get; private set; } = [];
    public string? Warning { get; private set; }
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(path)) return;
            Items = JsonSerializer.Deserialize<List<RecentMedia>>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Invalid media library.");
            if (Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Path) || string.IsNullOrWhiteSpace(i.Name))) throw new InvalidDataException("Invalid media library entry.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { Items = []; Warning = "The media library could not be read and was preserved. " + ex.Message; }
    }
    public Task RememberAsync(string file) => UpdateAsync(() =>
    {
        var next = Items.Where(item => !item.Path.Equals(file, StringComparison.OrdinalIgnoreCase)).ToList();
        next.Insert(0, new RecentMedia(file, System.IO.Path.GetFileNameWithoutExtension(file),
            IsAudio(file) ? "Music" : "Videos", DateTimeOffset.Now));
        return next;
    });

    public Task ImportAsync(IEnumerable<string> paths) => UpdateAsync(() =>
    {
        var next = Items.ToList();
        var known = next.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in paths)
            if (known.Add(file))
                next.Add(new(file, System.IO.Path.GetFileNameWithoutExtension(file), IsAudio(file) ? "Music" : "Videos", DateTimeOffset.MinValue));
        return next;
    });

    public Task RelinkAsync(string previous, string replacement) => UpdateAsync(() =>
        Items.Where(item => !item.Path.Equals(replacement, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Path.Equals(previous, StringComparison.OrdinalIgnoreCase)
                ? item with { Path = replacement, Name = System.IO.Path.GetFileNameWithoutExtension(replacement), Kind = IsAudio(replacement) ? "Music" : "Videos" }
                : item).ToList());

    private async Task UpdateAsync(Func<List<RecentMedia>> update)
    {
        await writes.WaitAsync();
        try
        {
            if (Warning is not null) throw new InvalidOperationException(Warning);
            var next = update();
            if (next.Count > 10000) throw new InvalidOperationException("The library limit is 10,000 files.");
            if (!next.SequenceEqual(Items)) await SaveAsync(next);
        }
        finally { writes.Release(); }
    }

    public static bool IsAudio(string file) => new[] { ".mp3", ".m4a", ".wav", ".flac", ".wma", ".aac", ".ogg" }.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
    private async Task SaveAsync(List<RecentMedia> next)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true); Items = next;
    }
}

