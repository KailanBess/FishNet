namespace FishNet;

public static class PlaylistExchange
{
    public static List<string> Read(string text, string playlistPath)
    {
        if (text.Length > 4_000_000) throw new InvalidDataException("Playlist is too large.");
        var tracks = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.Contains("://")) throw new InvalidDataException("Only local MP3 paths are supported.");
            var path = Path.GetFullPath(line, Path.GetDirectoryName(Path.GetFullPath(playlistPath))!);
            if (path.StartsWith(@"\\") || !Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Only local MP3 paths are supported.");
            tracks.Add(path);
            if (tracks.Count > 10000) throw new InvalidDataException("Playlist exceeds 10,000 tracks.");
        }
        return tracks;
    }
    public static string Write(IEnumerable<string> tracks) => "#EXTM3U\n" + string.Join("\n", tracks.Select(t => t.Contains('\n') || t.Contains('\r') ? throw new InvalidDataException("Invalid track path.") : t)) + "\n";
}
