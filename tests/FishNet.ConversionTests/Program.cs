using FishNet;
using Windows.Storage;

var root = Path.Combine(Path.GetTempPath(), "FishNet-conversion-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var inputPath = Path.Combine(root, "tone.wav");
    // Generated one-second PCM tone; no user media is read.
    using (var writer = new BinaryWriter(File.Create(inputPath)))
    {
        var samples = 44100; var bytes = samples * 2;
        writer.Write("RIFF"u8.ToArray()); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8.ToArray()); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(samples); writer.Write(samples * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8.ToArray()); writer.Write(bytes);
        for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / samples) * 4000));
    }
    var input = await StorageFile.GetFileFromPathAsync(inputPath);
    foreach (var format in new[] { "MP3", "M4A", "WAV", "FLAC" })
    {
        var path = Path.Combine(root, "converted." + format.ToLowerInvariant()); File.WriteAllBytes(path, []);
        var output = await StorageFile.GetFileFromPathAsync(path);
        await MediaConverter.TranscodeAsync(input, output, format);
        if (new FileInfo(path).Length < 100) throw new Exception(format + " output is empty");
        var decodedPath = Path.Combine(root, "decoded-" + format + ".wav"); File.WriteAllBytes(decodedPath, []);
        var decoded = await StorageFile.GetFileFromPathAsync(decodedPath);
        await MediaConverter.TranscodeAsync(output, decoded, "WAV");
        var properties = await decoded.Properties.GetMusicPropertiesAsync();
        if (properties.Duration.TotalSeconds < .8) throw new Exception(format + " output duration invalid");
        Console.WriteLine("PASS: generated WAV → " + format + " with real duration");
        if (format == "MP3")
        {
            var tagPath = Path.Combine(root, "tags-fixture.mp3"); File.Copy(path, tagPath);
            var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tagPath));
            var coverPath = Path.Combine(root, "cover.png");
            File.WriteAllBytes(coverPath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII="));
            var backup = MediaTags.Save(tagPath, new AudioTags("Fixture title", "Fixture artist", "Fixture album", 2026, 2), coverPath);
            var tags = MediaTags.Read(tagPath);
            if (tags.Title != "Fixture title" || tags.Artist != "Fixture artist" || tags.Year != 2026 || tags.Track != 2) throw new Exception("MP3 tag edits not persisted");
            if (!System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(backup)).SequenceEqual(before)) throw new Exception("Tag backup changed original bytes");
            using var tagged = TagLib.File.Create(tagPath);
            if (tagged.Tag.Pictures.Length != 1 || tagged.Tag.Pictures[0].MimeType != "image/png") throw new Exception("Cover art not saved");
            Console.WriteLine("PASS: MP3 tags/cover art and exact original backup");
        }
    }
    var presetSizes = new List<long>();
    foreach (var bitrate in new uint[] { 128000, 256000 })
    {
        var path = Path.Combine(root, "preset-" + bitrate + ".mp3");
        await File.WriteAllBytesAsync(path, []);
        await MediaConverter.TranscodeAsync(input, await StorageFile.GetFileFromPathAsync(path), "MP3", mp3Bitrate: bitrate);
        presetSizes.Add(new FileInfo(path).Length);
    }
    if (presetSizes[0] < 100 || presetSizes[0] >= presetSizes[1])
        throw new Exception("The compact preset did not produce a smaller encoded file.");
    Console.WriteLine("PASS: compact and high quality MP3 presets produce different file sizes");
    var cancelledPath = Path.Combine(root, "cancelled.mp3"); File.WriteAllBytes(cancelledPath, []);
    var composition = new Windows.Media.Editing.MediaComposition();
    composition.Clips.Add(Windows.Media.Editing.MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 20, 40, 60), TimeSpan.FromSeconds(1)));
    composition.BackgroundAudioTracks.Add(await Windows.Media.Editing.BackgroundAudioTrack.CreateFromFileAsync(input));
    var videoPath = Path.Combine(root, "fixture.mp4"); File.WriteAllBytes(videoPath, []);
    var video = await StorageFile.GetFileFromPathAsync(videoPath);
    var render = await composition.RenderToFileAsync(video);
    if (render != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new Exception("Could not generate video fixture: " + render);
    var subtitlePath = Path.Combine(root, "fixture.srt"); await File.WriteAllTextAsync(subtitlePath, "1\n00:00:00,100 --> 00:00:00,900\nFixture caption\n");
    using (var mediaSource = Windows.Media.Core.MediaSource.CreateFromStorageFile(video))
    using (var mediaPlayer = new Windows.Media.Playback.MediaPlayer { IsMuted = true })
    {
        var resolved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subtitles = Windows.Media.Core.TimedTextSource.CreateFromUri(new Uri(subtitlePath));
        subtitles.Resolved += (_, result) => { if (result.Error is not null) resolved.TrySetException(new Exception("Subtitle resolve failed: " + result.Error.ErrorCode)); else resolved.TrySetResult(result.Tracks.Count > 0); };
        mediaSource.ExternalTimedTextSources.Add(subtitles);
        mediaPlayer.Source = new Windows.Media.Playback.MediaPlaybackItem(mediaSource);
        mediaPlayer.Play();
        if (!await resolved.Task.WaitAsync(TimeSpan.FromSeconds(15))) throw new Exception("Subtitle track missing");
        mediaPlayer.Pause(); mediaPlayer.Source = null;
        Console.WriteLine("PASS: local SRT resolves as a real timed-text track");
    }
    foreach (var format in new[] { "MP4", "WMV", "MP3" })
    {
        var path = Path.Combine(root, "video-output." + format.ToLowerInvariant()); File.WriteAllBytes(path, []);
        var output = await StorageFile.GetFileFromPathAsync(path);
        await MediaConverter.TranscodeAsync(video, output, format);
        if (new FileInfo(path).Length < 100) throw new Exception("Video conversion empty: " + format);
        Console.WriteLine("PASS: generated video → " + format);
    }
    using var cts = new CancellationTokenSource(); cts.Cancel();
    try { await MediaConverter.TranscodeAsync(input, await StorageFile.GetFileFromPathAsync(cancelledPath), "MP3", cts.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: cancellation"); }
    try { await MediaConverter.TranscodeAsync(input, input, "MP3"); throw new Exception("Source overwrite permitted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: source overwrite refused"); }
    var badPath = Path.Combine(root, "invalid.wav"); await File.WriteAllTextAsync(badPath, "not media");
    try { await MediaConverter.TranscodeAsync(await StorageFile.GetFileFromPathAsync(badPath), await StorageFile.GetFileFromPathAsync(cancelledPath), "MP3"); throw new Exception("Invalid media accepted"); }
    catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { Console.WriteLine("PASS: invalid media rejected"); }
}
finally
{
    if (!Path.GetFileName(root).StartsWith("FishNet-conversion-tests-") || Path.GetDirectoryName(root) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)) throw new Exception("Unsafe cleanup path");
    for (var attempt = 0; ; attempt++)
    {
        try { Directory.Delete(root, true); break; }
        catch (IOException) when (attempt < 5) { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(250); }
    }
}

