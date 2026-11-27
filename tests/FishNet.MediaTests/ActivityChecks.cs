using Aquarium.Core;
using FishNet;
using System.Text.Json.Nodes;

internal static class ActivityChecks
{
    internal static async Task Run(string root)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var profile = Path.Combine(root, "activity-profile"); var program = Path.Combine(root, "activity-program");
        Directory.CreateDirectory(program);
        var log = new LocalActivity(profile, program);
        Check(log.Read().Events.Count == 0 && !Directory.Exists(profile), "Reading absent activity mutated the profile");
        Check(await log.RecordAsync(LocalActivityKind.ApplicationStarted), "App start was not recorded");
        Check(await log.RecordAsync(LocalActivityKind.ErrorNotice), "Error code was not recorded");
        var snapshot = log.Read();
        Check(snapshot.ApplicationId == "fishnet" && snapshot.Events.Select(e => e.Code).SequenceEqual(new[] { "application.started", "notice.error" }), "FishNet event identity/codes changed");
        Check(snapshot.Events[1].Level == ActivityLevel.Error && snapshot.Events.All(e => e.RecordedAtUtc.Offset == TimeSpan.Zero), "Event level/UTC attribution changed");
        var path = Path.Combine(profile, "Activity", "history.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        document["FuturePrivateField"] = "Private media path/token fixture";
        await File.WriteAllTextAsync(path, document.ToJsonString());
        Check(await log.RecordAsync(LocalActivityKind.ApplicationStarted), "Known event could not preserve unknown fields");
        Check((await File.ReadAllTextAsync(path)).Contains("Private media path/token fixture"), "Unknown storage fields were lost");
        var output = Path.Combine(root, "activity.json");
        await log.ExportAsync(output, snapshot);
        var exported = JsonNode.Parse(await File.ReadAllTextAsync(output))!;
        Check(exported["Events"]!.AsArray().Count == 2 && !exported.ToJsonString().Contains("Private"), "Export did not preserve the reviewed snapshot or projected unknown fields");
        byte[] previous = await File.ReadAllBytesAsync(output);
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            try { await log.ExportAsync(output, snapshot, cancel.Token); throw new Exception("Canceled export completed"); }
            catch (OperationCanceledException) { checks++; }
            Check((await File.ReadAllBytesAsync(output)).SequenceEqual(previous), "Canceled export changed an existing destination");
            Check(!await log.RecordAsync(LocalActivityKind.ErrorNotice, cancel.Token) && log.HasMissedEvents, "Canceled recording inferred complete coverage");
        }
        foreach (var invalid in new[] { Path.Combine(profile, "report.json"), Path.Combine(program, "report.json"), Path.Combine(root, "wrong.txt") })
        {
            try { await log.ExportAsync(invalid, snapshot); throw new Exception("Invalid export destination accepted"); }
            catch (InvalidDataException) { Check(!File.Exists(invalid), "Invalid export created a file"); }
        }
        var future = JsonNode.Parse(await File.ReadAllTextAsync(path))!; future["SchemaVersion"] = 999;
        await File.WriteAllTextAsync(path, future.ToJsonString()); var preserved = await File.ReadAllBytesAsync(path);
        Check(!await log.RecordAsync(LocalActivityKind.ApplicationStarted) && log.HasMissedEvents, "Future schema inferred successful recording");
        Check((await File.ReadAllBytesAsync(path)).SequenceEqual(preserved), "Future activity was overwritten");
        await File.WriteAllTextAsync(path, "damaged activity fixture");
        Check(!await log.RecordAsync(LocalActivityKind.ErrorNotice), "Corrupt history inferred successful recording");
        Check(await File.ReadAllTextAsync(path) == "damaged activity fixture", "Corrupt activity was overwritten");
        try { await log.RecordAsync((LocalActivityKind)999); throw new Exception("Unknown event kind accepted"); }
        catch (ArgumentOutOfRangeException) { checks++; }
        Console.WriteLine($"PASS: {checks} FishNet activity integration/privacy/export/preservation checks.");
    }
}
