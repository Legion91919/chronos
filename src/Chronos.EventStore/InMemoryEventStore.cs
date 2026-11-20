namespace Chronos.EventStore;

/// <summary>Thread-safe in-memory event store. Loses everything on restart; for tests and demos.</summary>
public class InMemoryEventStore : IEventStore
{
    private readonly object _gate = new();
    private readonly List<StoredEvent> _all = [];
    private readonly Dictionary<string, List<StoredEvent>> _streams = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<long> AppendAsync(string streamId, long expectedVersion, IReadOnlyList<EventData> events, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(events);
        lock (_gate)
        {
            var stored = Prepare(streamId, expectedVersion, events);
            BeforeCommit(stored);
            Commit(stored);
            return Task.FromResult(expectedVersion + events.Count);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(string streamId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<StoredEvent> result = _streams.TryGetValue(streamId, out var s) ? s.ToArray() : [];
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEvent>> ReadAllAsync(long afterPosition, int max, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // Positions are 1..n with no gaps, so the index of position p is p - 1.
            var start = (int)Math.Clamp(afterPosition, 0, _all.Count);
            IReadOnlyList<StoredEvent> result = _all.Skip(start).Take(Math.Max(0, max)).ToArray();
            return Task.FromResult(result);
        }
    }

    /// <summary>Called inside the write lock, after the version check and before the events become visible.</summary>
    protected virtual void BeforeCommit(IReadOnlyList<StoredEvent> events) { }

    /// <summary>Loads already persisted events at start-up (no version checks).</summary>
    protected void Load(IEnumerable<StoredEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        lock (_gate) Commit(events.ToArray());
    }

    private StoredEvent[] Prepare(string streamId, long expectedVersion, IReadOnlyList<EventData> events)
    {
        var current = _streams.TryGetValue(streamId, out var s) ? s.Count : 0;
        if (current != expectedVersion) throw new WrongExpectedVersionException(streamId, expectedVersion, current);
        var position = _all.Count;
        return events.Select((e, i) => new StoredEvent(position + i + 1, streamId, expectedVersion + i + 1, e.Type, e.SchemaVersion, e.Data.Clone(), e.Metadata)).ToArray();
    }

    private void Commit(IReadOnlyList<StoredEvent> events)
    {
        foreach (var e in events)
        {
            _all.Add(e);
            if (!_streams.TryGetValue(e.StreamId, out var s)) _streams[e.StreamId] = s = [];
            s.Add(e);
        }
    }
}
