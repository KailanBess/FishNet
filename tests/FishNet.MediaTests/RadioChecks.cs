using FishNet;
using System.Text.Json;

internal static class RadioChecks
{
    internal static async Task Run(string parent)
    {
        var root = Path.Combine(parent, "radio"); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "radio-stations.json"); int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        async Task Refuses<T>(Func<Task> action) where T : Exception
        { try { await action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
        var stations = new RadioStations(root); await stations.LoadAsync();
        Check(stations.Items.Count == 0 && stations.Warning is null, "Empty station list");
        await stations.AddAsync(" River Radio ", "https://radio.example/live");
        var saved = stations.Items.Single();
        Check(saved.Name == "River Radio", "Station name not trimmed");
        var reload = new RadioStations(root); await reload.LoadAsync();
        Check(reload.Items.Single() == saved, "Station not persisted");
        await Refuses<InvalidDataException>(() => stations.AddAsync("Duplicate", "https://RADIO.example/live"));
        Check(stations.Items.Count == 1, "Rejected station changed memory");
        foreach (var url in new[] { "http://radio.example/live", "file:///C:/test.mp3", "javascript:alert(1)",
            "https://user:secret@radio.example/live", "https://radio.example/live#fragment", "https://radio.example/\ntrack", "relative.mp3", "" })
            await Refuses<InvalidDataException>(() => stations.AddAsync("Invalid", url));
        await Refuses<InvalidDataException>(() => stations.AddAsync(" ", "https://other.example/live"));
        await Refuses<InvalidDataException>(() => stations.AddAsync("Bad\nName", "https://other.example/live"));
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => stations.AddAsync("Station " + i, "https://radio.example/" + i)));
        Check(stations.Items.Count == 11, "Concurrent saves lost stations");
        await stations.RemoveAsync(saved.Id);
        reload = new RadioStations(root); await reload.LoadAsync();
        Check(reload.Items.Count == 10 && reload.Items.All(s => s.Id != saved.Id), "Station removal not persisted");
        await File.WriteAllTextAsync(path, "damaged");
        var corrupt = new RadioStations(root); await corrupt.LoadAsync();
        Check(corrupt.Warning is not null, "Corrupt data not reported");
        await Refuses<InvalidOperationException>(() => corrupt.AddAsync("New", "https://radio.example/new"));
        Check(File.ReadAllText(path) == "damaged", "Corrupt records overwritten");
        foreach (var data in new[]
        {
            new[] { saved, saved }, new[] { saved with { Id = "not-an-id" } },
            new[] { saved with { Url = "file:///etc/passwd" } }, new RadioStation[] { null! },
            Enumerable.Range(0, 201).Select(i => new RadioStation(Guid.NewGuid().ToString("N"), "Station", "https://radio.example/" + i)).ToArray()
        })
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data));
            var invalid = new RadioStations(root); await invalid.LoadAsync();
            Check(invalid.Warning is not null && invalid.Items.Count == 0, "Invalid station records accepted");
        }
        await File.WriteAllTextAsync(path, new string('x', 512 * 1024 + 1));
        var oversized = new RadioStations(root); await oversized.LoadAsync();
        Check(oversized.Warning is not null, "Oversized station records accepted");
        Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Station save leaked temporary files");
        Console.WriteLine($"Internet radio: {checks} checks passed. No station was contacted.");
    }
}
