using System.Text.Json;

namespace OpenCode.Core.Services;

public record FrecencyEntry(
    string Path,
    int Frequency,
    long LastOpen
);

public class FrecencyService
{
    private readonly string _frecencyFile;
    private readonly Dictionary<string, FrecencyEntry> _data = new();
    private const int MaxEntries = 1000;

    public FrecencyService(string projectRoot)
    {
        _frecencyFile = Path.Combine(projectRoot, ".opencode", "frecency.jsonl");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_frecencyFile)) return;

        try
        {
            var lines = File.ReadAllLines(_frecencyFile);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<FrecencyEntry>(line);
                    if (entry != null)
                    {
                        _data[entry.Path] = entry;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    public void Update(string path)
    {
        var absolutePath = Path.GetFullPath(path);
        var current = _data.TryGetValue(absolutePath, out var existing) ? existing : null;
        
        var newEntry = new FrecencyEntry(
            absolutePath,
            (current?.Frequency ?? 0) + 1,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        _data[absolutePath] = newEntry;
        
        try
        {
            File.AppendAllLines(_frecencyFile, new[] { JsonSerializer.Serialize(newEntry) });
        }
        catch { }

        if (_data.Count > MaxEntries)
        {
            Prune();
        }
    }

    private void Prune()
    {
        var sorted = _data.Values.OrderByDescending(e => e.LastOpen).Take(MaxEntries).ToList();
        _data.Clear();
        foreach (var e in sorted) _data[e.Path] = e;

        try
        {
            var lines = sorted.Select(e => JsonSerializer.Serialize(e));
            File.WriteAllLines(_frecencyFile, lines);
        }
        catch { }
    }

    public double GetScore(string path)
    {
        var absolutePath = Path.GetFullPath(path);
        if (!_data.TryGetValue(absolutePath, out var entry)) return 0;

        var daysSince = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - entry.LastOpen) / 86400000.0;
        var weight = 1.0 / (1.0 + daysSince);
        return entry.Frequency * weight;
    }
}
