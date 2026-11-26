using System.Text.Json;

namespace FishNet;

public sealed record RadioStation(string Id, string Name, string Url);

// Loading and editing never connect to a station.
public sealed class RadioStations
{
    private readonly string path;
    private readonly SemaphoreSlim writes = new(1, 1);
    public IReadOnlyList<RadioStation> Items { get; private set; } = [];
    public string? Warning { get; private set; }
    public RadioStations(string? directory = null) => path = Path.Combine(directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet"), "radio-stations.json");

    public async Task LoadAsync()
    {
        Warning = null; Items = [];
        try
        {
            if (!File.Exists(path)) return;
            using var input = File.OpenRead(path);
            if (input.Length > 512 * 1024) throw new InvalidDataException("The radio station list is too large.");
            var items = await JsonSerializer.DeserializeAsync<List<RadioStation>>(input)
                ?? throw new InvalidDataException("The radio station list is empty.");
            Validate(items); Items = items.AsReadOnly();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { Warning = "Radio stations could not be read and were preserved. " + ex.Message; }
    }

    public Task AddAsync(string name, string url) => UpdateAsync(items =>
    { items.Add(new RadioStation(Guid.NewGuid().ToString("N"), name.Trim(), url.Trim())); return items; });
    public Task RemoveAsync(string id) => UpdateAsync(items => items.Where(s => s.Id != id).ToList());

    private async Task UpdateAsync(Func<List<RadioStation>, List<RadioStation>> change)
    {
        await writes.WaitAsync();
        try
        {
            if (Warning is not null) throw new InvalidOperationException(Warning);
            var next = change(Items.ToList()); Validate(next);
            var json = JsonSerializer.SerializeToUtf8Bytes(next, new JsonSerializerOptions { WriteIndented = true });
            if (json.Length > 512 * 1024) throw new InvalidDataException("The radio station list is too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { await output.WriteAsync(json); output.Flush(flushToDisk: true); }
                File.Move(temporary, path, overwrite: true); Items = next.AsReadOnly();
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { writes.Release(); }
    }

    public static Uri StreamUri(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 || url.Any(char.IsControl) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Enter a direct HTTPS audio stream without embedded credentials or a fragment.");
        return uri;
    }

    private static void Validate(List<RadioStation> items)
    {
        if (items.Count > 200 || items.Any(s => s is null || !Guid.TryParseExact(s.Id, "N", out _) ||
            string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 120 || s.Name.Any(char.IsControl)) ||
            items.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new InvalidDataException("Invalid radio station list. Up to 200 named stations are supported.");
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var station in items)
            if (!addresses.Add(StreamUri(station.Url).AbsoluteUri)) throw new InvalidDataException("That stream is already saved.");
    }
}
