using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FishNet;

public sealed record MediaPathChange(string Previous, string Replacement);
public sealed record RepairWrite(string File, byte[] Data);

public static class FolderRepair
{
    public static List<MediaPathChange> Preview(IEnumerable<string> paths, string previousRoot, string replacementRoot)
    {
        if (!Path.IsPathFullyQualified(previousRoot) || !Path.IsPathFullyQualified(replacementRoot))
            throw new InvalidDataException("Choose absolute old and new folder paths.");
        var oldPrefix = Path.GetFullPath(previousRoot);
        var newPrefix = Path.GetFullPath(replacementRoot);
        if (!Path.EndsInDirectorySeparator(oldPrefix)) oldPrefix += Path.DirectorySeparatorChar;
        if (!Path.EndsInDirectorySeparator(newPrefix)) newPrefix += Path.DirectorySeparatorChar;
        if (oldPrefix.Equals(newPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a different replacement folder.");
        var known = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var changes = new List<MediaPathChange>();
        foreach (var path in known)
        {
            if (!Path.IsPathFullyQualified(path) || !Path.GetFullPath(path).StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase) || File.Exists(path)) continue;
            var target = Path.GetFullPath(Path.Combine(newPrefix, Path.GetRelativePath(oldPrefix, Path.GetFullPath(path))));
            if (!target.StartsWith(newPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(target)) continue;
            if (known.Any(p => p.Equals(target, StringComparison.OrdinalIgnoreCase)) || changes.Any(c => c.Replacement.Equals(target, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("A replacement already belongs to another media record. Repair those files individually.");
            changes.Add(new(path, target));
        }
        return changes;
    }

    public static List<MediaPathChange> FolderReferences(IEnumerable<string> folders, string previousRoot, string replacementRoot)
    {
        var oldRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(previousRoot));
        var newRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(replacementRoot));
        var prefix = Path.EndsInDirectorySeparator(oldRoot) ? oldRoot : oldRoot + Path.DirectorySeparatorChar;
        return folders.Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase).Where(folder =>
            Path.GetFullPath(folder).Equals(oldRoot, StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(folder).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(folder => new MediaPathChange(folder, Path.GetFullPath(Path.Combine(newRoot, Path.GetRelativePath(oldRoot, folder))))).ToList();
    }

    public static byte[] Rewrite(byte[] json, IReadOnlyList<MediaPathChange> changes)
    {
        var mapping = changes.ToDictionary(c => c.Previous, c => c.Replacement, StringComparer.OrdinalIgnoreCase);
        JsonNode? Visit(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var text) && mapping.TryGetValue(text, out var replacement)) return JsonValue.Create(replacement);
            if (node is JsonObject obj) foreach (var key in obj.Select(p => p.Key).ToArray())
            { var next = Visit(obj[key]); if (!ReferenceEquals(next, obj[key])) obj[key] = next; }
            else if (node is JsonArray array) for (int i = 0; i < array.Count; i++)
            { var next = Visit(array[i]); if (!ReferenceEquals(next, array[i])) array[i] = next; }
            return node;
        }
        return JsonSerializer.SerializeToUtf8Bytes(Visit(JsonNode.Parse(json)), new JsonSerializerOptions { WriteIndented = true });
    }

    public static string PositionFile(string path) => "watch-positions/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))) + ".json";

    private static bool Allowed(string file) => file is "media-library.json" or "playlists.json" or "play-queue.json" or "watched-folders.json" ||
        file.StartsWith("watch-positions/", StringComparison.Ordinal) && file.EndsWith(".json", StringComparison.Ordinal) &&
        file.Length == 85 && file[16..^5].All(Uri.IsHexDigit);

    private static string Target(string root, string file)
    {
        if (!Allowed(file)) throw new InvalidDataException("Unsupported repair record.");
        var target = Path.GetFullPath(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe repair path.");
        for (var current = target; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked repair paths are not supported.");
        return target;
    }

    // All writes are staged first. A sharing violation rolls back already published records.
    public static void Apply(string root, IReadOnlyList<RepairWrite> writes, Action? beforePublish = null)
    {
        if (writes.Select(w => w.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != writes.Count || writes.Sum(w => (long)w.Data.Length) > 64 * 1024 * 1024)
            throw new InvalidDataException("Repair records are duplicated or too large.");
        var staged = new List<(string Target, string Temp, byte[]? Previous)>();
        var published = new List<(string Target, byte[]? Previous)>();
        long previousSize = 0;
        try
        {
            foreach (var write in writes)
            {
                using var document = JsonDocument.Parse(write.Data);
                var target = Target(root, write.File);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                byte[]? previous = null;
                if (File.Exists(target))
                {
                    previousSize += new FileInfo(target).Length;
                    if (previousSize > 64 * 1024 * 1024) throw new InvalidDataException("Existing repair records are too large.");
                    previous = File.ReadAllBytes(target);
                }
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((target, temporary, previous));
                File.WriteAllBytes(temporary, write.Data);
            }
            beforePublish?.Invoke();
            foreach (var item in staged)
            {
                _ = Target(root, Path.GetRelativePath(root, item.Target).Replace('\\', '/'));
                File.Move(item.Temp, item.Target, true); published.Add((item.Target, item.Previous));
            }
        }
        catch
        {
            foreach (var item in published.AsEnumerable().Reverse())
                if (item.Previous is null) File.Delete(item.Target); else File.WriteAllBytes(item.Target, item.Previous);
            throw;
        }
        finally { foreach (var item in staged) if (File.Exists(item.Temp)) File.Delete(item.Temp); }
    }
}
