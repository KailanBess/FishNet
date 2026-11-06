namespace FishNet;

public sealed record ServicePreset(string Id, string Name, string Category, string Url);

// Provider shortcuts, not account/API or embedded playback integrations.
public static class ServiceCatalog
{
    public static IReadOnlyList<ServicePreset> BuiltIn { get; } = Array.AsReadOnly(new[]
    {
        new ServicePreset("spotify", "Spotify", "Music & podcasts", "https://open.spotify.com/"),
        new ServicePreset("youtube", "YouTube", "Video & music", "https://www.youtube.com/"),
        new ServicePreset("soundcloud", "SoundCloud", "Music & audio", "https://soundcloud.com/")
    });
}
