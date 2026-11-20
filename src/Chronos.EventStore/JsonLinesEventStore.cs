using System.Text;
using System.Text.Json;

namespace Chronos.EventStore;

/// <summary>
/// File-backed event store: one JSON object per line, appended and flushed to disk before the events become
/// visible. On open it loads the file; a torn last line (crash mid-write) is cut off. Single process only:
/// several instances would need a database with a unique (stream, version) constraint.
/// </summary>
public sealed class JsonLinesEventStore : InMemoryEventStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _path;

    /// <summary>Opens (or creates) the file at <paramref name="path"/>.</summary>
    public JsonLinesEventStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path)) File.WriteAllBytes(_path, []);
        Load(ReadAndRepair());
    }

    /// <inheritdoc />
    protected override void BeforeCommit(IReadOnlyList<StoredEvent> events)
    {
        var text = new StringBuilder();
        foreach (var e in events) text.Append(JsonSerializer.Serialize(e, Json)).Append('\n');
        using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private List<StoredEvent> ReadAndRepair()
    {
        var bytes = File.ReadAllBytes(_path);
        var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        var complete = lastNewline + 1;
        if (complete < bytes.Length)
        {
            // Torn write: keep only whole lines.
            using var f = new FileStream(_path, FileMode.Open, FileAccess.Write);
            f.SetLength(complete);
        }

        var events = new List<StoredEvent>();
        foreach (var line in Encoding.UTF8.GetString(bytes, 0, complete).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var e = JsonSerializer.Deserialize<StoredEvent>(line, Json)
                ?? throw new InvalidDataException($"Empty event line in {_path}.");
            if (e.Position != events.Count + 1) throw new InvalidDataException($"Event file {_path} has a gap at position {events.Count + 1}.");
            events.Add(e);
        }

        return events;
    }
}
