using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace FishNet;

public sealed partial class MainWindow
{
    private sealed record PodcastEpisode(string Title, string Url, string Extension);
    private sealed record PodcastFeed(string Title, string Url, List<PodcastEpisode> Episodes);
    private static readonly HttpClient podcastHttp = new(new HttpClientHandler { UseCookies = false });
    private readonly string podcastFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FishNet", "Podcasts");
    private bool podcastBusy;
    private CancellationTokenSource? podcastCancellation;

    private string PodcastIndex => Path.Combine(podcastFolder, "subscriptions.json");

    private List<PodcastFeed> ReadPodcastFeeds()
    {
        if (!File.Exists(PodcastIndex)) return [];
        if (new FileInfo(PodcastIndex).Length > 8_000_000) throw new InvalidDataException("Podcast library is too large.");
        var feeds = JsonSerializer.Deserialize<List<PodcastFeed>>(File.ReadAllText(PodcastIndex))
            ?? throw new InvalidDataException("Podcast library is unreadable.");
        if (feeds.Count > 50 || feeds.Any(feed => feed is null || string.IsNullOrWhiteSpace(feed.Title) ||
            string.IsNullOrWhiteSpace(feed.Url) || feed.Episodes is null || feed.Episodes.Count > 100 ||
            feed.Episodes.Any(episode => episode is null || string.IsNullOrWhiteSpace(episode.Title) ||
                string.IsNullOrWhiteSpace(episode.Url) || episode.Extension is not (".mp3" or ".m4a" or ".ogg"))))
            throw new InvalidDataException("Podcast library entries are invalid.");
        foreach (var feed in feeds)
        {
            PodcastUri(feed.Url);
            foreach (var episode in feed.Episodes) PodcastUri(episode.Url);
        }
        return feeds;
    }

    private void SavePodcastFeeds(List<PodcastFeed> feeds)
    {
        Directory.CreateDirectory(podcastFolder);
        var temporary = Path.Combine(podcastFolder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var json = JsonSerializer.Serialize(feeds);
            if (Encoding.UTF8.GetByteCount(json) > 8_000_000) throw new InvalidDataException("Podcast library is too large.");
            File.WriteAllText(temporary, json);
            File.Move(temporary, PodcastIndex, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static Uri PodcastUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.IsLoopback || uri.UserInfo.Length != 0 || value.Length > 4096)
            throw new InvalidDataException("Use an HTTPS podcast address without sign-in credentials.");
        return uri;
    }

    private static async Task CopyPodcastAsync(Uri uri, Stream destination, long limit, CancellationToken token)
    {
        using var response = await podcastHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        PodcastUri(response.RequestMessage!.RequestUri!.AbsoluteUri);
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("The podcast file exceeds the download limit.");
        using var content = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[65536];
        long written = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, token)) > 0)
        {
            written += read;
            if (written > limit) throw new InvalidDataException("The podcast file exceeds the download limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }

    private static async Task<PodcastFeed> FetchPodcastAsync(string url, CancellationToken token)
    {
        using var data = new MemoryStream();
        var uri = PodcastUri(url);
        await CopyPodcastAsync(uri, data, 4_000_000, token);
        data.Position = 0;
        using var reader = XmlReader.Create(data, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000
        });
        var channel = XDocument.Load(reader).Root?.Element("channel")
            ?? throw new InvalidDataException("This address did not return an RSS channel.");
        var episodes = new List<PodcastEpisode>();
        foreach (var item in channel.Elements("item"))
        {
            var enclosure = item.Element("enclosure");
            var address = (string?)enclosure?.Attribute("url");
            var extension = ((string?)enclosure?.Attribute("type"))?.ToLowerInvariant() switch
            {
                "audio/mpeg" or "audio/mp3" => ".mp3",
                "audio/mp4" or "audio/x-m4a" => ".m4a",
                "audio/ogg" => ".ogg",
                _ => null
            };
            if (extension is null || address is null) continue;
            try { address = PodcastUri(address).AbsoluteUri; }
            catch (InvalidDataException) { continue; }
            if (episodes.Any(episode => episode.Url == address)) continue;
            episodes.Add(new PodcastEpisode(PodcastTitle((string?)item.Element("title"), "Episode"), address, extension));
            if (episodes.Count == 100) break;
        }
        if (episodes.Count == 0) throw new InvalidDataException("No supported HTTPS audio episodes were found.");
        return new PodcastFeed(PodcastTitle((string?)channel.Element("title"), uri.Host), uri.AbsoluteUri, episodes);
    }

    private static string PodcastTitle(string? title, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
        return value.Length > 200 ? value[..200] : value;
    }

    private string PodcastPath(PodcastEpisode episode)
    {
        if (episode.Extension is not (".mp3" or ".m4a" or ".ogg")) throw new InvalidDataException("Unsupported episode type.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PodcastUri(episode.Url).AbsoluteUri)));
        return Path.Combine(podcastFolder, hash + episode.Extension);
    }

    private async Task PodcastAction(Func<CancellationToken, Task> action)
    {
        if (podcastBusy) return;
        podcastBusy = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        podcastCancellation = timeout;
        if (mediaPage == "Podcasts") RenderPodcasts();
        try { await action(timeout.Token); }
        catch (OperationCanceledException) { if (!closed) ShowError("The podcast request timed out."); }
        catch (Exception ex) { if (!closed) ShowError("Podcast: " + ex.Message); }
        finally { podcastCancellation = null; podcastBusy = false; if (!closed && mediaPage == "Podcasts") RenderPodcasts(); }
    }

    private void RenderPodcasts()
    {
        MediaBody.Children.Clear();
        List<PodcastFeed> feeds;
        try { feeds = ReadPodcastFeeds(); }
        catch (Exception ex) { MediaBody.Children.Add(new TextBlock { Text = ex.Message }); return; }
        if (podcastBusy)
        {
            var cancel = new Button { Content = "Cancel podcast request" };
            cancel.Click += (_, _) => podcastCancellation?.Cancel();
            MediaBody.Children.Add(cancel);
            return;
        }
        var subscribe = new Button { Content = "Subscribe to RSS feed…" };
        subscribe.Click += async (_, _) => await PodcastAction(async token =>
        {
            var address = new TextBox { Header = "HTTPS RSS feed", MaxLength = 4096 };
            if (await Dialog("Subscribe to podcast", address).ShowAsync() != ContentDialogResult.Primary || closed) return;
            var url = PodcastUri(address.Text.Trim()).AbsoluteUri;
            if (feeds.Count >= 50 && !feeds.Any(feed => feed.Url == url)) throw new InvalidOperationException("The library supports up to 50 subscriptions.");
            var feed = await FetchPodcastAsync(url, token);
            if (!closed) SavePodcastFeeds([.. feeds.Where(item => item.Url != feed.Url), feed]);
        });
        MediaBody.Children.Add(subscribe);
        if (feeds.Count == 0) MediaBody.Children.Add(new TextBlock { Text = "Add a podcast RSS feed to browse episodes and save them for offline playback." });
        foreach (var feed in feeds)
        {
            var content = new StackPanel { Spacing = 8 };
            var refresh = new Button { Content = "Refresh episodes" };
            refresh.Click += async (_, _) => await PodcastAction(async token =>
            {
                var updated = await FetchPodcastAsync(feed.Url, token);
                if (!closed) SavePodcastFeeds(feeds.Select(item => item.Url == feed.Url ? updated : item).ToList());
            });
            content.Children.Add(refresh);
            foreach (var episode in feed.Episodes)
            {
                var path = PodcastPath(episode);
                var button = new Button { Content = (File.Exists(path) ? "Play offline · " : "Download and play · ") + episode.Title };
                button.Click += async (_, _) => await PodcastAction(async token =>
                {
                    if (!File.Exists(path))
                    {
                        Directory.CreateDirectory(podcastFolder);
                        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            await using (var output = File.Create(temporary))
                                await CopyPodcastAsync(PodcastUri(episode.Url), output, 256 * 1024 * 1024, token);
                            File.Move(temporary, path, overwrite: false);
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    if (closed) return;
                    var progress = path + ".progress.json";
                    var saved = ReadWatchPosition(progress);
                    TimeSpan? resume = saved is { Ticks: > 0 } && string.Equals(saved.Path, path, StringComparison.OrdinalIgnoreCase)
                        ? TimeSpan.FromTicks(saved.Ticks) : null;
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    if (!closed) await PlayLocalAsync(file, resumeAt: resume, displayName: episode.Title, progressFile: progress);
                });
                content.Children.Add(button);
            }
            MediaBody.Children.Add(new Expander { Header = feed.Title, Content = content });
        }
    }
}

