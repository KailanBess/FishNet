using Aquarium.Core;
using System.Text.Json;

namespace FishNet;

internal static class ProfilePaths
{
    private sealed class ProfileLocation { public string? DataDirectory { get; set; } }
    private static readonly Lazy<string> Selected = new(() =>
    {
        var configured = Environment.GetEnvironmentVariable("FISHNET_DATA_ROOT");
        var selector = Path.Combine(AppContext.BaseDirectory, "FishNet.profile.json");
        if (string.IsNullOrWhiteSpace(configured) && File.Exists(selector))
        {
            PathSafety.RefuseLinks(selector);
            using var input = File.OpenRead(selector);
            if (input.Length > 4096) throw new InvalidDataException("The profile selector is too large.");
            try { configured = JsonSerializer.Deserialize<ProfileLocation>(input)?.DataDirectory; }
            catch (JsonException) { throw new InvalidDataException("The profile selector is invalid."); }
            if (string.IsNullOrWhiteSpace(configured)) throw new InvalidDataException("The profile selector must name a data directory.");
        }
        return Resolve(configured, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    });
    internal static string DataDirectory => Selected.Value;
    internal static string Resolve(string? configured, string defaultLocalData)
    {
        if (!string.IsNullOrWhiteSpace(configured) && !Path.IsPathFullyQualified(configured))
            throw new ArgumentException("FISHNET_DATA_ROOT must be an absolute directory path.");
        if (configured?.StartsWith(@"\\", StringComparison.Ordinal) == true)
            throw new ArgumentException("Use a profile directory on a local drive.");
        if (string.IsNullOrWhiteSpace(configured) && string.IsNullOrWhiteSpace(defaultLocalData))
            throw new InvalidOperationException("The local application data directory is unavailable.");
        var path = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(defaultLocalData, "FishNet") : configured);
        PathSafety.RefuseLinks(path);
        return path;
    }
}
