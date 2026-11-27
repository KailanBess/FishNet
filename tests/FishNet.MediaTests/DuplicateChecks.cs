using FishNet;
using System.Security.Cryptography;

internal static class DuplicateChecks
{
    private sealed class Progress(Action<PhotoDuplicateProgress> action) : IProgress<PhotoDuplicateProgress>
    { public void Report(PhotoDuplicateProgress value) => action(value); }
    internal static async Task Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        var folder = Path.Combine(root, "duplicates"); Directory.CreateDirectory(folder);
        var a = Path.Combine(folder, "one.png"); var b = Path.Combine(folder, "copy.jpg"); var c = Path.Combine(folder, "different.png");
        var missing = Path.Combine(folder, "missing.png"); var large = Path.Combine(folder, "large.png");
        byte[] bytes = [1, 2, 3, 4]; File.WriteAllBytes(a, bytes); File.WriteAllBytes(b, bytes);
        File.WriteAllBytes(c, [4, 3, 2, 1]); File.WriteAllBytes(large, new byte[9]);
        var result = await PhotoDuplicates.CompareAsync([a, b, c, missing, large, a], limits: new(MaximumFileBytes: 8, MaximumTotalBytes: 32));
        Check(result.TotalPaths == 5 && result.HashedFiles == 3 && result.BytesRead == 12, "Comparison coverage/counts incorrect");
        Check(result.Groups.Count == 1 && result.Groups[0].Paths.SequenceEqual(new[] { a, b }) && result.Groups[0].Sha256 == Convert.ToHexString(SHA256.HashData(bytes)), "Same-size content/hash matching incorrect");
        Check(!result.Complete && result.Skipped.Count == 2 && result.Skipped.Any(s => s.Reason == "file-size-limit") && result.Skipped.Any(s => s.Reason == "unavailable-or-linked"), "Partial scan was mislabeled complete");
        Check(File.ReadAllBytes(a).SequenceEqual(bytes) && File.ReadAllBytes(b).SequenceEqual(bytes) && !File.Exists(missing), "Comparison changed source files");
        var complete = await PhotoDuplicates.CompareAsync([a, b, c]);
        Check(complete.Complete && complete.Groups.Count == 1 && complete.Skipped.Count == 0, "Complete scan coverage incorrect");
        var limited = await PhotoDuplicates.CompareAsync([a, b, c], limits: new(MaximumTotalBytes: 4));
        Check(!limited.Complete && limited.BytesRead == 4 && limited.HashedFiles == 1 && limited.Skipped.All(s => s.Reason == "scan-byte-limit"), "Byte budget was exceeded or hidden");
        using (var locked = File.Open(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var unavailable = await PhotoDuplicates.CompareAsync([a, b]);
            Check(!unavailable.Complete && unavailable.HashedFiles == 1 && unavailable.Groups.Count == 0, "Locked file produced a duplicate or healthy coverage claim");
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await PhotoDuplicates.CompareAsync([a, b], canceled.Token); throw new Exception("Canceled comparison ran"); } catch (OperationCanceledException) { checks++; }
        using var interrupted = new CancellationTokenSource();
        try
        {
            await PhotoDuplicates.CompareAsync([a, b], interrupted.Token, new Progress(value => { if (value.CompletedPaths == 1) interrupted.Cancel(); }));
            throw new Exception("Active comparison cancellation was ignored");
        }
        catch (OperationCanceledException) { checks++; }
        try { await PhotoDuplicates.CompareAsync([a, b], limits: new(MaximumFiles: 1)); throw new Exception("Path limit ignored"); } catch (InvalidDataException) { checks++; }
        try { await PhotoDuplicates.CompareAsync([a], limits: new(MaximumTotalBytes: 0)); throw new Exception("Invalid budget accepted"); } catch (ArgumentOutOfRangeException) { checks++; }
        try { await PhotoDuplicates.CompareAsync(["relative.png"]); throw new Exception("Relative path accepted"); } catch (InvalidDataException) { checks++; }
        Console.WriteLine($"Photo duplicate review: {checks} hash, coverage, budget, locking, cancellation and source-preservation checks passed.");
    }
}
