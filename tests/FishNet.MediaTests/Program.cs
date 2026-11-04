using FishNet;
var root = Path.Combine(Path.GetTempPath(), "FishNet-media-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var playlists = new Playlists(root); await playlists.LoadAsync();
    var first = Path.Combine(root, "first.mp3"); var second = Path.Combine(root, "second.MP3");
    await playlists.SaveAsync([new Playlist("fixture", "My MP3s", [first, second])]);
    var reloadLists = new Playlists(root); await reloadLists.LoadAsync();
    if (!reloadLists.Items.Single().Tracks.SequenceEqual(new[] { first, second })) throw new Exception("Playlist order not persisted");
    await reloadLists.SaveAsync([reloadLists.Items[0] with { Name = "Renamed", Tracks = [second] }]);
    var edited = new Playlists(root); await edited.LoadAsync();
    if (edited.Items[0].Name != "Renamed" || edited.Items[0].Tracks.Single() != second) throw new Exception("Playlist edits not saved");
    try { await edited.SaveAsync([new Playlist("bad", "Bad", [Path.Combine(root, "not-mp3.wav")])]); throw new Exception("Non-MP3 track accepted"); }
    catch (InvalidDataException) { }
    await File.WriteAllTextAsync(Path.Combine(root, "playlists.json"), "damaged");
    var damagedLists = new Playlists(root); await damagedLists.LoadAsync();
    try { await damagedLists.SaveAsync([]); throw new Exception("Damaged playlists overwritten"); }
    catch (InvalidOperationException) { }
    if (await File.ReadAllTextAsync(Path.Combine(root, "playlists.json")) != "damaged") throw new Exception("Playlist preservation failed");
    Console.WriteLine("PASS: playlist creation, ordering, rename/removal persistence, MP3 validation and damaged-data preservation.");
    if (ServiceCatalog.BuiltIn.Select(s => s.Id).Distinct().Count() != ServiceCatalog.BuiltIn.Count || ServiceCatalog.BuiltIn.Any(s => !Uri.TryCreate(s.Url, UriKind.Absolute, out var u) || u.Scheme != "https")) throw new Exception("Service catalog contains duplicate IDs or invalid HTTPS targets");
    var catalog = new MediaCatalog(root); await catalog.LoadAsync();
    if (catalog.Items.Count != 0) throw new Exception("Empty library fabricated entries");
    await catalog.RememberAsync(Path.Combine(root, "Fixture Song.MP3"));
    await catalog.RememberAsync(Path.Combine(root, "Fixture Video.mp4"));
    await catalog.RememberAsync(Path.Combine(root, "Fixture Song.MP3"));
    if (catalog.Items.Count != 2 || catalog.Items[0].Kind != "Music") throw new Exception("Recent ordering, deduplication or media filtering incorrect");
    var reload = new MediaCatalog(root); await reload.LoadAsync();
    if (reload.Items.Count != 2 || reload.Items[1].Kind != "Videos") throw new Exception("Media collection did not persist");
    var path = Path.Combine(root, "media-library.json"); await File.WriteAllTextAsync(path, "invalid JSON");
    var damaged = new MediaCatalog(root); await damaged.LoadAsync(); await damaged.RememberAsync(Path.Combine(root, "Fixture.mp3"));
    if (damaged.Warning is null || await File.ReadAllTextAsync(path) != "invalid JSON") throw new Exception("Invalid saved library was overwritten");
    Console.WriteLine("PASS: empty state, recent ordering, duplicate handling, music/video categories, persistence and invalid-file preservation. No media files were opened.");
}
finally
{
    if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("FishNet-media-tests-")) throw new Exception("Unsafe cleanup path");
    Directory.Delete(root, true);
}

