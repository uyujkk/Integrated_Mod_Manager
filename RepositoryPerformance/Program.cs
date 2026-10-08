using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using IntegratedModManager.Core;
using ModFolderCopier.WinUI;

// Developer-only harness: uses existing synthetic fixtures read-only and writes
// fresh isolated SQLite indexes/results. Never deployed with the application.
if (args.Length != 2) throw new ArgumentException("Usage: RepositoryPerformance <fixture-root> <new-results-directory>");
string fixtures = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new results directory; existing results will not be replaced.");
Directory.CreateDirectory(output);
var rows = new List<object>();
foreach (int count in new[] { 100, 500, 1000 })
{
    string fixture = Path.Combine(fixtures, $"synthetic-{count}-mods");
    if (!File.ReadAllText(Path.Combine(fixture, "FIXTURE.txt")).StartsWith("Synthetic scanner fixture:", StringComparison.Ordinal))
        throw new InvalidDataException("Only generated scanner fixtures are accepted.");
    string source = Path.Combine(fixture, "Source"), target = Path.Combine(fixture, "Target");
    var samples = new List<object>();
    for (int iteration = 0; iteration < 5; iteration++)
    {
        var errors = new List<string>();
        var store = new AppDataStore(Path.Combine(output, $"index-{count}-{iteration}.db"), (op, _) => errors.Add(op));
        store.Initialize();
        var scanner = new RepositoryScanService(new RepositoryIndexStoreAdapter(store));
        var watch = Stopwatch.StartNew();
        var cold = scanner.Scan("synthetic", source); double coldMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart(); var warm = scanner.Scan("synthetic", source); double warmMs = watch.Elapsed.TotalMilliseconds;
        if (errors.Count != 0 || cold.Warnings.Count != 0 || warm.Warnings.Count != 0 || !cold.IsComplete || !warm.IsComplete
            || cold.ModCount != count || warm.ModCount != count || warm.CacheHits != count) throw new InvalidDataException("Fixture scan/index validation failed.");
        // Measures the same filesystem deployment-status operations, not WinUI creation/rendering.
        watch.Restart();
        var projected = warm.Categories.SelectMany(c => c.Mods).Select(m => new ProjectedMod(m.ModPath,
            Directory.Exists(Path.Combine(target, Path.GetFileName(m.ModPath))))).ToArray();
        double projectionMs = watch.Elapsed.TotalMilliseconds;
        var legacy = new ObservableCollection<ProjectedMod>(); int legacyEvents = 0;
        legacy.CollectionChanged += (_, _) => legacyEvents++;
        watch.Restart(); legacy.Clear(); foreach (var mod in projected) legacy.Add(mod);
        double legacyMs = watch.Elapsed.TotalMilliseconds;
        var batch = new BatchObservableCollection<ProjectedMod>(); int batchEvents = 0;
        batch.CollectionChanged += (_, _) => batchEvents++;
        watch.Restart(); batch.ReplaceAll(projected); double batchMs = watch.Elapsed.TotalMilliseconds;
        batch.ReplaceAll(projected);
        if (legacyEvents != count + 1 || batchEvents != 1 || !batch.SequenceEqual(legacy)) throw new InvalidDataException("Collection event/identity contract failed.");
        samples.Add(new { Iteration = iteration, ColdScanMs = coldMs, WarmScanMs = warmMs, WarmHits = warm.CacheHits,
            ProjectionMs = projectionMs, LegacyCollectionMs = legacyMs, BatchCollectionMs = batchMs, LegacyEvents = legacyEvents, BatchEvents = batchEvents });
    }
    rows.Add(new { ModCount = count, Samples = samples });
}
string result = Path.Combine(output, "repository-benchmark.json");
File.WriteAllText(result, JsonSerializer.Serialize(new { TimestampUtc = DateTimeOffset.UtcNow,
    Scope = "Synthetic service + SQLite + collection notifications only; not native rendering, frame rate or game performance.", Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(result);
internal sealed record ProjectedMod(string Path, bool IsDeployed);
