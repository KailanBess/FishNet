using Aquarium.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishNet;

public sealed record VisualItem(string Path, string Name, string Kind, DateTimeOffset AddedAt, string Album, bool Screenshot)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}
public sealed class VisualState
{
    public List<VisualItem> Items { get; set; } = [];
    public List<string> Albums { get; set; } = [];
}

public sealed class VisualLibrary
{
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic"];
    private readonly JsonSettingsStore<VisualState> store;
    private readonly SemaphoreSlim writes = new(1, 1);
    private SettingsSnapshot<VisualState>? snapshot;
    public VisualState State { get; private set; } = new();
    public string? Warning { get; private set; }
    public bool Writable => snapshot is not null && Warning is null;
    public VisualLibrary(string? directory = null) => store = new(Path.Combine(directory ??
        ProfilePaths.DataDirectory, "visual-library.json"),
        1, validate: Valid, maximumBytes: 16 * 1024 * 1024);

    public async Task LoadAsync()
    {
        await writes.WaitAsync();
        try
        {
            Warning = null; snapshot = null; State = new();
            try { snapshot = store.Load(); State = snapshot.Value; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            { Warning = "The photo/document library could not be read and was preserved. " + ex.Message; }
        }
        finally { writes.Release(); }
    }

    public static string? Kind(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "Documents" :
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) ? "Photos" : null;
    public static bool IsScreenshot(string path) => Path.GetFileNameWithoutExtension(path).StartsWith("Screenshot", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileNameWithoutExtension(path).StartsWith("Screen Shot", StringComparison.OrdinalIgnoreCase) ||
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part.Equals("Screenshots", StringComparison.OrdinalIgnoreCase));

    public Task ImportAsync(IEnumerable<string> paths) => UpdateAsync(next =>
    {
        var known = next.Items.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incoming = paths.Take(10001).ToArray();
        if (incoming.Length > 10000) throw new InvalidDataException("Import at most 10,000 paths at a time.");
        foreach (var path in incoming)
        {
            if (!Path.IsPathFullyQualified(path) || Kind(path) is not { } kind) throw new InvalidDataException("Select supported local photos or PDF files.");
            var full = Path.GetFullPath(path);
            if (known.Add(full)) next.Items.Add(new(full, Path.GetFileNameWithoutExtension(full), kind, DateTimeOffset.UtcNow, "", kind == "Photos" && IsScreenshot(full)));
        }
    });

    public Task AddAlbumAsync(string name) => UpdateAsync(next =>
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl) || next.Albums.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a unique album name of up to 80 characters.");
        next.Albums.Add(name);
    });
    public Task SetAlbumAsync(string path, string album) => UpdateAsync(next =>
    {
        if (album.Length != 0 && !next.Albums.Contains(album)) throw new InvalidDataException("That album is unavailable.");
        if (!next.Items.Any(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("The library record is missing.");
        next.Items = next.Items.Select(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase) ? i with { Album = album } : i).ToList();
    });
    public Task RemoveAlbumAsync(string album) => UpdateAsync(next =>
    { next.Albums.Remove(album); next.Items = next.Items.Select(i => i.Album == album ? i with { Album = "" } : i).ToList(); });
    public Task RemoveAsync(string path) => UpdateAsync(next => next.Items.RemoveAll(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));
    public Task RelinkAsync(string previous, string replacement) => UpdateAsync(next =>
    {
        if (!Path.IsPathFullyQualified(replacement) || Kind(replacement) is null || next.Items.Any(i => i.Path.Equals(replacement, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Choose a supported local file not already in this library.");
        if (!next.Items.Any(i => i.Path.Equals(previous, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("The original record is missing.");
        next.Items = next.Items.Select(i => i.Path.Equals(previous, StringComparison.OrdinalIgnoreCase)
            ? i with { Path = Path.GetFullPath(replacement), Name = Path.GetFileNameWithoutExtension(replacement), Kind = Kind(replacement)!, Screenshot = IsScreenshot(replacement) } : i).ToList();
    });

    private async Task UpdateAsync(Action<VisualState> update)
    {
        await writes.WaitAsync();
        try
        {
            if (!Writable) throw new InvalidOperationException(Warning ?? "Load the library before editing.");
            var next = new VisualState { Items = State.Items.ToList(), Albums = State.Albums.ToList() };
            update(next);
            snapshot = await store.SaveAsync(snapshot!, next, keepPrevious: true);
            State = next;
        }
        finally { writes.Release(); }
    }
    private static bool Valid(VisualState value) => value.Items is not null && value.Albums is not null && value.Items.Count <= 10000 && value.Albums.Count <= 200 &&
        value.Albums.All(a => !string.IsNullOrWhiteSpace(a) && a.Length <= 80 && !a.Any(char.IsControl)) &&
        value.Albums.Distinct(StringComparer.OrdinalIgnoreCase).Count() == value.Albums.Count &&
        value.Items.All(i => i is not null && !string.IsNullOrWhiteSpace(i.Path) && Path.IsPathFullyQualified(i.Path) &&
            !string.IsNullOrWhiteSpace(i.Name) && i.Name.Length <= 1024 && i.Kind == Kind(i.Path) && i.Album is not null &&
            (i.Album.Length == 0 || value.Albums.Contains(i.Album))) &&
        value.Items.Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == value.Items.Count;
}
