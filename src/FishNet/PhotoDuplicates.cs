using Aquarium.Core;
using System.Security.Cryptography;

namespace FishNet;

public sealed record PhotoDuplicateLimits(int MaximumFiles = 10000, long MaximumFileBytes = 64L * 1024 * 1024,
    long MaximumTotalBytes = 2L * 1024 * 1024 * 1024);
public sealed record PhotoDuplicateGroup(long Bytes, string Sha256, IReadOnlyList<string> Paths);
public sealed record SkippedPhoto(string Path, string Reason);
public sealed record PhotoDuplicateProgress(int CompletedPaths, int TotalPaths, long BytesRead);
public sealed record PhotoDuplicateReport(int SchemaVersion, DateTimeOffset CompletedAtUtc, int TotalPaths, int HashedFiles,
    long BytesRead, IReadOnlyList<PhotoDuplicateGroup> Groups, IReadOnlyList<SkippedPhoto> Skipped)
{
    public bool Complete => Skipped.Count == 0;
}

public static class PhotoDuplicates
{
    public static async Task<PhotoDuplicateReport> CompareAsync(IEnumerable<string> paths, CancellationToken token = default,
        IProgress<PhotoDuplicateProgress>? progress = null, PhotoDuplicateLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        limits ??= new();
        if (limits.MaximumFiles is < 1 or > 10000 || limits.MaximumFileBytes is < 1 or > 64L * 1024 * 1024 ||
            limits.MaximumTotalBytes is < 1 or > 2L * 1024 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(limits));
        token.ThrowIfCancellationRequested();
        var selected = paths.Take(limits.MaximumFiles + 1).ToArray();
        if (selected.Length > limits.MaximumFiles || selected.Any(p => string.IsNullOrWhiteSpace(p) ||
            !Path.IsPathFullyQualified(p) || VisualLibrary.Kind(p) != "Photos"))
            throw new InvalidDataException("Choose up to 10,000 absolute indexed photo paths.");
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var unique = selected.Select(Path.GetFullPath).Distinct(comparer).ToArray();
        var matches = new Dictionary<(long Length, string Hash), List<string>>();
        var skipped = new List<SkippedPhoto>(); long bytesRead = 0; int hashed = 0, completed = 0;
        var buffer = new byte[64 * 1024];
        progress?.Report(new(0, unique.Length, 0));
        foreach (var path in unique)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                PathSafety.RefuseLinks(path);
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
                long length = input.Length;
                if (length > limits.MaximumFileBytes) skipped.Add(new(path, "file-size-limit"));
                else if (length > limits.MaximumTotalBytes - bytesRead) skipped.Add(new(path, "scan-byte-limit"));
                else
                {
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long fileBytes = 0;
                    while (fileBytes < length)
                    {
                        int count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - fileBytes)), token);
                        if (count == 0) throw new IOException("The file changed during the scan.");
                        fileBytes += count; bytesRead += count; hash.AppendData(buffer, 0, count);
                    }
                    if (input.Length != length) throw new IOException("The file changed during the scan.");
                    token.ThrowIfCancellationRequested();
                    var key = (length, Convert.ToHexString(hash.GetHashAndReset()));
                    if (!matches.TryGetValue(key, out var members)) matches[key] = members = [];
                    members.Add(path); hashed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { skipped.Add(new(path, "unavailable-or-linked")); }
            completed++; progress?.Report(new(completed, unique.Length, bytesRead));
        }
        token.ThrowIfCancellationRequested();
        var groups = matches.Where(pair => pair.Value.Count > 1).OrderByDescending(pair => pair.Key.Length)
            .ThenBy(pair => pair.Key.Hash, StringComparer.Ordinal)
            .Select(pair => new PhotoDuplicateGroup(pair.Key.Length, pair.Key.Hash, pair.Value.AsReadOnly())).ToArray();
        return new(1, DateTimeOffset.UtcNow, unique.Length, hashed, bytesRead, Array.AsReadOnly(groups), skipped.AsReadOnly());
    }
}
