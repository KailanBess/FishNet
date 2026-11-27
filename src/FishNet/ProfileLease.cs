namespace FishNet;

internal static class ProfileLease
{
    internal static FileStream Acquire()
    {
        var directory = ProfilePaths.DataDirectory;
        for (var current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked profiles are not supported.");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "profile.lock");
        if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked profile locks are not supported.");
        return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
