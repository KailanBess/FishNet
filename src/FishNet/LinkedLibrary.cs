using System.Diagnostics;
using System.Text.Json;

namespace FishNet;

public sealed record LinkedApp(string Id, string Name, string Kind, string Target, string Collection, string? BrowserId);
public sealed class LinkedLibraryState
{
    public int SchemaVersion { get; set; } = 1;
    public List<string> Collections { get; set; } = ["Favorites"];
    public List<LinkedApp> Apps { get; set; } = [];
}

public sealed class LinkedLibrary
{
    public string PathOnDisk { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet", "linked-library.json");
    public LinkedLibraryState State { get; private set; } = new();
    public bool Writable { get; private set; } = true;
    public string? Warning { get; private set; }
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(PathOnDisk)) return;
            var loaded = JsonSerializer.Deserialize<LinkedLibraryState>(await File.ReadAllTextAsync(PathOnDisk)) ?? throw new InvalidDataException("Empty library.");
            if (loaded.SchemaVersion != 1 || loaded.Collections is null || loaded.Apps is null || loaded.Apps.Any(a => a is null || string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Name)) || loaded.Apps.Select(a => a.Id).Distinct().Count() != loaded.Apps.Count)
                throw new InvalidDataException("Invalid or unsupported library.");
            State = loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { Writable = false; Warning = "The library could not be read. It was preserved; edits are disabled. " + ex.Message; }
    }
    public async Task SaveAsync(LinkedLibraryState next)
    {
        if (!Writable) throw new InvalidOperationException("Library is read-only after a load failure.");
        Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
        await File.WriteAllTextAsync(PathOnDisk + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(PathOnDisk + ".tmp", PathOnDisk, true);
        State = next;
    }
    public LinkedLibraryState Copy() => new() { Collections = State.Collections.ToList(), Apps = State.Apps.ToList() };
    public void Launch(LinkedApp app)
    {
        string executable;
        Uri? website = null;
        if (app.Kind == "Website")
        {
            if (!Uri.TryCreate(app.Target, UriKind.Absolute, out website) || website.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Only HTTPS website links are supported.");
            executable = State.Apps.FirstOrDefault(a => a.Id == app.BrowserId && a.Kind == "Browser")?.Target
                ?? throw new InvalidOperationException("Add or select an external browser first.");
        }
        else if (app.Kind is "App" or "Browser") executable = app.Target;
        else throw new InvalidOperationException("Unknown link type.");
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The linked executable is missing. Remove the link and add the app again.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        if (website is not null) start.ArgumentList.Add(website.AbsoluteUri);
        Process.Start(start)?.Dispose();
    }
}
