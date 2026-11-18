using FishNet;
using System.Text;
using System.Text.Json;

internal static class RepairChecks
{
    internal static void Run(string root)
    {
        var previous = Path.Combine(root, "old"); var replacement = Path.Combine(root, "new");
        var oldFile = Path.Combine(previous, "album", "fixture.mp3");
        var newFile = Path.Combine(replacement, "album", "fixture.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(newFile)!); File.WriteAllText(newFile, "inert fixture, never decoded");
        var paths = FolderRepair.Preview([oldFile, Path.Combine(previous + "-other", "fixture.mp3")], previous, replacement);
        if (paths.Count != 1 || paths[0].Replacement != newFile) throw new Exception("Folder repair crossed a folder boundary or lost relative paths.");
        var json = JsonSerializer.SerializeToUtf8Bytes(new[] { new { Path = oldFile, Title = "Fixture name", Tracks = new[] { oldFile } } });
        var folders = FolderRepair.FolderReferences([Path.Combine(previous, "album"), previous + "-other"], previous, replacement);
        if (folders.Count != 1 || folders[0].Replacement != Path.Combine(replacement, "album")) throw new Exception("Smart playlist/watch folder repair crossed a boundary or lost a nested folder.");
        using var result = JsonDocument.Parse(FolderRepair.Rewrite(json, paths));
        if (result.RootElement[0].GetProperty("Path").GetString() != newFile || result.RootElement[0].GetProperty("Title").GetString() != "Fixture name")
            throw new Exception("Repair did not preserve unrelated metadata.");
        if (FolderRepair.PositionFile(oldFile) == FolderRepair.PositionFile(newFile)) throw new Exception("Moved playback position reused the old identity.");
        try { FolderRepair.Preview([oldFile, newFile], previous, replacement); throw new Exception("Duplicate target was accepted."); } catch (InvalidDataException) { }
        var records = Path.Combine(root, "repair-records"); Directory.CreateDirectory(records);
        var first = Path.Combine(records, "media-library.json"); var second = Path.Combine(records, "playlists.json");
        File.WriteAllText(first, "[1]"); File.WriteAllText(second, "[2]");
        FileStream? locked = null;
        try
        {
            FolderRepair.Apply(records, [new("media-library.json", Encoding.UTF8.GetBytes("[]")), new("playlists.json", Encoding.UTF8.GetBytes("[]"))],
                () => locked = File.Open(second, FileMode.Open, FileAccess.Read, FileShare.None));
            throw new Exception("Locked repair destination was accepted.");
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        finally { locked?.Dispose(); }
        if (File.ReadAllText(first) != "[1]" || File.ReadAllText(second) != "[2]" || Directory.GetFiles(records, "*.tmp").Length != 0)
            throw new Exception("Failed folder repair did not restore prior records.");
        try { FolderRepair.Apply(records, [new("../outside.json", Encoding.UTF8.GetBytes("[]"))]); throw new Exception("Unsafe record path was accepted."); } catch (InvalidDataException) { }
        Console.WriteLine("Folder repair boundary, identity, collision, metadata, rollback and unsafe-path checks passed.");
    }
}
