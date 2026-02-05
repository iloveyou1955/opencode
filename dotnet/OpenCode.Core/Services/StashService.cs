using System.Text.Json;

namespace OpenCode.Core.Services;

public record StashEntry(
    string Content,
    long Timestamp,
    string? Type = "prompt", // "prompt" or "code"
    string? Title = null,
    Dictionary<string, string>? Metadata = null
);

public class StashService
{
    private readonly string _stashFile;
    private readonly List<StashEntry> _entries = new();
    private const int MaxEntries = 50;

    public StashService(string projectRoot)
    {
        _stashFile = Path.Combine(projectRoot, ".opencode", "prompt-stash.jsonl");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_stashFile)) return;

        try
        {
            var lines = File.ReadAllLines(_stashFile);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<StashEntry>(line);
                    if (entry != null)
                    {
                        _entries.Add(entry);
                    }
                }
                catch { }
            }
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }
        }
        catch { }
    }

    public void Push(string content, string type = "prompt", string? title = null, Dictionary<string, string>? metadata = null)
    {
        var entry = new StashEntry(content, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), type, title, metadata);
        _entries.Add(entry);
        
        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveAt(0);
            SaveAll();
        }
        else
        {
            try
            {
                File.AppendAllLines(_stashFile, new[] { JsonSerializer.Serialize(entry) });
            }
            catch { }
        }
    }

    public void Remove(int index)
    {
        if (index >= 0 && index < _entries.Count)
        {
            _entries.RemoveAt(index);
            SaveAll();
        }
    }

    public StashEntry? Pop()
    {
        if (_entries.Count == 0) return null;
        var entry = _entries[^1];
        _entries.RemoveAt(_entries.Count - 1);
        SaveAll();
        return entry;
    }

    public IEnumerable<StashEntry> List() => _entries;

    private void SaveAll()
    {
        try
        {
            var lines = _entries.Select(e => JsonSerializer.Serialize(e));
            File.WriteAllLines(_stashFile, lines);
        }
        catch { }
    }
}
