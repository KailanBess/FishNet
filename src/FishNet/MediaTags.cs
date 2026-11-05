namespace FishNet;

public sealed record AudioTags(string Title, string Artist, string Album, uint Year, uint Track);
public static class MediaTags
{
    public static AudioTags Read(string path)
    {
        using var file = TagLib.File.Create(path);
        return new(file.Tag.Title ?? "", file.Tag.FirstPerformer ?? "", file.Tag.Album ?? "", file.Tag.Year, file.Tag.Track);
    }
    // Edits a copy; File.Replace retains the untouched original in a unique backup.
    public static string Save(string path, AudioTags tags, string? cover)
    {
        if (!Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Tag editing currently supports MP3 files.");
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".fishnet-tags-" + Guid.NewGuid().ToString("N") + ".mp3");
        var backup = path + ".fishnet-backup-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(path, temporary, false);
            using (var file = TagLib.File.Create(temporary))
            {
                file.Tag.Title = tags.Title; file.Tag.Performers = [tags.Artist]; file.Tag.Album = tags.Album; file.Tag.Year = tags.Year; file.Tag.Track = tags.Track;
                if (cover is not null)
                {
                    if (new FileInfo(cover).Length > 8 * 1024 * 1024) throw new InvalidDataException("Cover art must be smaller than 8 MB.");
                    var picture = new TagLib.Picture(cover) { Type = TagLib.PictureType.FrontCover };
                    if (picture.MimeType is not ("image/png" or "image/jpeg")) throw new InvalidDataException("Choose PNG or JPEG cover art.");
                    file.Tag.Pictures = [picture];
                }
                file.Save();
            }
            File.Replace(temporary, path, backup); return backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
