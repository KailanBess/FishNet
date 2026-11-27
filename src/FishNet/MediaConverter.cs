using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Aquarium.Core;

namespace FishNet;

public static class MediaConverter
{
    public static async Task ConvertCopyAsync(string source, string destination, string format, CancellationToken cancellation = default, Action<double>? progress = null, uint mp3Bitrate = 192000)
    {
        source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Source and output must differ.");
        cancellation.ThrowIfCancellationRequested();
        var profile = Profile(format, mp3Bitrate);
        PathSafety.RefuseLinks(destination);
        // Retain a read-only source handle through publication, including alias paths.
        using var sourceFile = File.OpenRead(source);
        using var input = sourceFile.AsRandomAccessStream();
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var outputFile = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var output = outputFile.AsRandomAccessStream())
            {
                var prepared = await new MediaTranscoder().PrepareStreamTranscodeAsync(input, output, profile);
                await ExecuteAsync(prepared, format, cancellation, progress);
                await output.FlushAsync();
                outputFile.Flush(flushToDisk: true);
            }
            cancellation.ThrowIfCancellationRequested();
            PathSafety.RefuseLinks(destination);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task TranscodeAsync(StorageFile input, StorageFile output, string format, CancellationToken cancellation = default, Action<double>? progress = null, uint mp3Bitrate = 192000)
    {
        if (input.Path.Equals(output.Path, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Source and output must differ.");
        cancellation.ThrowIfCancellationRequested();
        var prepared = await new MediaTranscoder().PrepareFileTranscodeAsync(input, output, Profile(format, mp3Bitrate));
        await ExecuteAsync(prepared, format, cancellation, progress);
    }

    private static MediaEncodingProfile Profile(string format, uint mp3Bitrate)
    {
        var profile = format switch
        {
            "MP3" => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High),
            "M4A" => MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High),
            "WAV" => MediaEncodingProfile.CreateWav(AudioEncodingQuality.High),
            "FLAC" => MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High),
            "MP4" => MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p),
            "WMV" => MediaEncodingProfile.CreateWmv(VideoEncodingQuality.HD720p),
            _ => throw new InvalidOperationException("Unsupported output format.")
        };
        if (format == "MP3")
        {
            if (mp3Bitrate is not (128000 or 192000 or 256000)) throw new ArgumentOutOfRangeException(nameof(mp3Bitrate));
            profile.Audio.Bitrate = mp3Bitrate;
        }
        return profile;
    }
    private static async Task ExecuteAsync(PrepareTranscodeResult prepared, string format, CancellationToken cancellation, Action<double>? progress)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!prepared.CanTranscode) throw new InvalidOperationException("Windows cannot convert this file to " + format + ": " + prepared.FailureReason + ". Try another format or source file.");
        var operation = prepared.TranscodeAsync();
        operation.Progress = (_, value) => progress?.Invoke(Math.Clamp(value, 0, 100));
        try
        {
            using var registration = cancellation.Register(operation.Cancel);
            await operation;
        }
        finally { operation.Close(); }
        cancellation.ThrowIfCancellationRequested();
    }
}

