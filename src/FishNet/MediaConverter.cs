using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace FishNet;

public static class MediaConverter
{
    public static async Task TranscodeAsync(StorageFile input, StorageFile output, string format, CancellationToken cancellation = default, Action<double>? progress = null, uint mp3Bitrate = 192000)
    {
        if (input.Path.Equals(output.Path, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Source and output must differ.");
        cancellation.ThrowIfCancellationRequested();
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
        var prepared = await new MediaTranscoder().PrepareFileTranscodeAsync(input, output, profile);
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

