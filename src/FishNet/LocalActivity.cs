using Aquarium.Core;
using System.Text.Json;

namespace FishNet;

internal enum LocalActivityKind { ApplicationStarted, ErrorNotice }

// Only fixed symbolic events enter this optional store. Never pass notice text or media metadata.
internal sealed class LocalActivity
{
    private readonly ActivityHistory history;
    private readonly string profile, program;
    private int pending, missed;
    internal bool HasMissedEvents => Volatile.Read(ref missed) != 0;
    internal LocalActivity(string profile, string program)
    {
        this.profile = Path.GetFullPath(profile); this.program = Path.GetFullPath(program);
        history = new ActivityHistory(Path.Combine(this.profile, "Activity", "history.json"), "fishnet",
            ["application.started", "notice.error"]);
    }
    internal async Task<bool> RecordAsync(LocalActivityKind kind, CancellationToken token = default)
    {
        string code = kind switch { LocalActivityKind.ApplicationStarted => "application.started", LocalActivityKind.ErrorNotice => "notice.error", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        if (Interlocked.Increment(ref pending) > 16)
        { Interlocked.Decrement(ref pending); Volatile.Write(ref missed, 1); return false; }
        try
        {
            await Task.Run(() => history.AppendAsync(code, kind == LocalActivityKind.ErrorNotice ? ActivityLevel.Error : ActivityLevel.Information,
                typeof(LocalActivity).Assembly.GetName().Version!.ToString(), token), token);
            return true;
        }
        catch (Exception) { Volatile.Write(ref missed, 1); return false; }
        finally { Interlocked.Decrement(ref pending); }
    }
    internal ActivitySnapshot Read() => history.Read();
    internal async Task ExportAsync(string path, ActivitySnapshot reviewed, CancellationToken token = default)
    {
        var full = Path.GetFullPath(path);
        static bool Within(string candidate, string root) => candidate.Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!Path.GetExtension(full).Equals(".json", StringComparison.OrdinalIgnoreCase) || Within(full, profile) || Within(full, program))
            throw new InvalidDataException("Choose a JSON filename outside FishNet's profile and program folders.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(reviewed, new JsonSerializerOptions { WriteIndented = true });
        await AtomicFile.WriteAsync(full, (output, cancellation) => output.WriteAsync(bytes, cancellation).AsTask(), token);
    }
}
