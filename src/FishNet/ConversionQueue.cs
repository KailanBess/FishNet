using System.Text.Json;

namespace FishNet;

internal sealed record ConversionJob(string Input, string? Output = null, string? Error = null);

internal sealed record ConversionQueue(string Destination, string Format, uint Mp3Bitrate, bool ToPlaylist, List<ConversionJob> Jobs)
{
    private static readonly string QueuePath = Path.Combine(
        ProfilePaths.DataDirectory, "conversion-queue.json");

    internal static FileStream AcquireWriter()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(QueuePath)!);
        try { return File.Open(QueuePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("Another FishNet window is using the conversion queue. Try again when it finishes.", ex); }
    }

    internal bool Matches(ConversionQueue? other) =>
        other is not null && Destination == other.Destination && Format == other.Format &&
        Mp3Bitrate == other.Mp3Bitrate && ToPlaylist == other.ToPlaylist && Jobs.SequenceEqual(other.Jobs);

    internal static ConversionQueue? Load()
    {
        if (!File.Exists(QueuePath)) return null;
        using var stream = File.Open(QueuePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("The conversion queue is too large.");
        var queue = JsonSerializer.Deserialize<ConversionQueue>(stream);
        if (queue is null || queue.Jobs is not { Count: > 0 and <= 1000 } ||
            queue.Format is not ("MP3" or "M4A" or "WAV" or "FLAC" or "MP4" or "WMV") ||
            queue.Mp3Bitrate is not (128000u or 192000u or 256000u) ||
            !Path.IsPathFullyQualified(queue.Destination) ||
            queue.Jobs.Any(job => job is null || !Path.IsPathFullyQualified(job.Input)))
            throw new InvalidDataException("The conversion queue has an unsupported format.");
        return queue;
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(QueuePath)!);
        var temporary = QueuePath + "." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = File.Open(temporary, FileMode.CreateNew))
                JsonSerializer.Serialize(stream, this);
            File.Move(temporary, QueuePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static void Clear() => File.Delete(QueuePath);
}
