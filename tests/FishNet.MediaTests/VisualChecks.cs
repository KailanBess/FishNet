using FishNet;
using System.Text.Json;

internal static class VisualChecks
{
    internal static async Task Run(string root)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        async Task Refuses<T>(Func<Task> action) where T : Exception
        { try { await action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
        var directory = Path.Combine(root, "visual"); Directory.CreateDirectory(directory);
        Check(ProfilePaths.Resolve(directory, root) == Path.GetFullPath(directory), "Explicit profile root changed");
        Check(ProfilePaths.Resolve(null, root) == Path.Combine(Path.GetFullPath(root), "FishNet"), "Default profile root changed");
        await Refuses<ArgumentException>(() => { ProfilePaths.Resolve("relative", root); return Task.CompletedTask; });
        var store = new VisualLibrary(directory);
        await Refuses<InvalidOperationException>(() => store.ImportAsync([Path.Combine(root, "one.png")]));
        await store.LoadAsync();
        var photo = Path.Combine(root, "Screenshot fixture.png"); var pdf = Path.Combine(root, "Fixture.pdf");
        await store.ImportAsync([photo, photo, pdf]);
        Check(store.State.Items.Count == 2 && store.State.Items[0].Screenshot && store.State.Items[1].Kind == "Documents", "Visual categorization/deduplication");
        await store.AddAlbumAsync("Family"); await store.SetAlbumAsync(photo, "Family");
        var reload = new VisualLibrary(directory); await reload.LoadAsync();
        Check(reload.State.Items.Single(i => i.Path == photo).Album == "Family", "Album persistence");
        await Refuses<InvalidDataException>(() => store.AddAlbumAsync("family"));
        await Refuses<InvalidDataException>(() => store.ImportAsync([Path.Combine(root, "not-visual.exe")]));
        await Refuses<InvalidDataException>(() => store.ImportAsync(Enumerable.Repeat(photo, 10001)));
        var replacement = Path.Combine(root, "Moved.png");
        await store.RelinkAsync(photo, replacement);
        Check(store.State.Items.Single(i => i.Path == replacement).Album == "Family", "Relink lost album");
        await store.RemoveAlbumAsync("Family");
        Check(store.State.Items.All(i => i.Album == ""), "Album removal orphaned references");
        var fresh = new VisualLibrary(directory); await fresh.LoadAsync();
        await store.ImportAsync([Path.Combine(root, "New.png")]);
        await Refuses<IOException>(() => fresh.AddAlbumAsync("Stale writer"));
        var path = Path.Combine(directory, "visual-library.json");
        var raw = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        raw["FutureSetting"] = true; raw["Items"]![0]!["FutureItem"] = "kept";
        await File.WriteAllTextAsync(path, raw.ToJsonString());
        await store.LoadAsync(); await store.AddAlbumAsync("Updated");
        using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
            Check(saved.RootElement.GetProperty("FutureSetting").GetBoolean() && saved.RootElement.GetProperty("Items")[0].GetProperty("FutureItem").GetString() == "kept", "Future visual fields discarded");
        await store.RemoveAsync(replacement);
        Check(store.State.Items.All(i => i.Path != replacement), "Record not removed");
        await File.WriteAllTextAsync(path, "{\"_schemaVersion\":99}");
        await store.LoadAsync(); Check(!store.Writable && store.Warning is not null, "Unsupported schema writable");
        await Refuses<InvalidOperationException>(() => store.AddAlbumAsync("Overwrite"));
        Check(File.ReadAllText(path) == "{\"_schemaVersion\":99}", "Unsupported records were destroyed");

        var pixels = Enumerable.Range(0, 6).SelectMany(i => new byte[] { (byte)i, (byte)(10 + i), (byte)(20 + i), 255 }).ToArray();
        var original = pixels.ToArray();
        var turned = RasterTransform.Apply(pixels, 2, 3, new PhotoEdit(1));
        Check(turned.Width == 3 && turned.Height == 2 && turned.Pixels.Where((_, i) => i % 4 == 0).SequenceEqual(new byte[] { 4, 2, 0, 5, 3, 1 }), "Clockwise rotation pixels");
        var crop = RasterTransform.Apply(pixels, 2, 3, new PhotoEdit(0, 1, 1, 1, 2));
        Check(crop.Pixels.Where((_, i) => i % 4 == 0).SequenceEqual(new byte[] { 3, 5 }), "Crop pixels");
        var brighter = RasterTransform.Apply(pixels, 2, 3, new PhotoEdit(Brightness: 100));
        Check(brighter.Pixels.Where((_, i) => i % 4 < 3).All(b => b == 255) && brighter.Pixels.Where((_, i) => i % 4 == 3).All(b => b == 255), "Brightness/alpha");
        Check(pixels.SequenceEqual(original), "Editing changed input pixels");
        foreach (var bad in new[] { new PhotoEdit(4), new PhotoEdit(X: -1), new PhotoEdit(X: 1, Width: 2), new PhotoEdit(Brightness: 101), new PhotoEdit(Height: int.MaxValue) })
            await Refuses<InvalidDataException>(() => { RasterTransform.Apply(pixels, 2, 3, bad); return Task.CompletedTask; });
        Console.WriteLine($"Visual library and photo transforms: {checks} checks passed.");
    }
}
