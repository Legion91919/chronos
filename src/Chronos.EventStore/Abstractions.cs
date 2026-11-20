using System.Text.Json;

namespace Chronos.EventStore;

/// <summary>Who caused an event, when it was recorded, and the business date it takes effect.</summary>
/// <param name="CommandId">The idempotency key of the command that produced the event.</param>
/// <param name="Actor">Opaque user id (never a name).</param>
/// <param name="RecordedAt">System time the event was written (UTC).</param>
/// <param name="EffectiveDate">Business date the event takes effect; drives point-in-time replay.</param>
public sealed record EventMetadata(string? CommandId, string Actor, DateTimeOffset RecordedAt, DateOnly EffectiveDate, string? CommandType = null);

/// <summary>An event to append: a type name, its schema version and a JSON payload.</summary>
public sealed record EventData(string Type, int SchemaVersion, JsonElement Data, EventMetadata Metadata);

/// <summary>An event as stored: with its global position and its version within the stream.</summary>
public sealed record StoredEvent(long Position, string StreamId, long Version, string Type, int SchemaVersion, JsonElement Data, EventMetadata Metadata);

/// <summary>
/// Append-only event store. There is no update or delete. A stream's version is the number of events in it;
/// an empty stream has version 0. Appends use optimistic concurrency on that version.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Appends events atomically if the stream is still at <paramref name="expectedVersion"/>; returns the new version.
    /// Throws <see cref="WrongExpectedVersionException"/> when another writer got there first.
    /// </summary>
    Task<long> AppendAsync(string streamId, long expectedVersion, IReadOnlyList<EventData> events, CancellationToken cancellationToken = default);

    /// <summary>All events of one stream, oldest first.</summary>
    Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="max"/> events from all streams with a position greater than <paramref name="afterPosition"/>.</summary>
    Task<IReadOnlyList<StoredEvent>> ReadAllAsync(long afterPosition, int max, CancellationToken cancellationToken = default);
}

/// <summary>Another writer appended to the stream since it was read.</summary>
public sealed class WrongExpectedVersionException : Exception
{
    /// <summary>Creates the exception.</summary>
    public WrongExpectedVersionException(string streamId, long expectedVersion, long actualVersion)
        : base($"Stream '{streamId}' is at version {actualVersion}, expected {expectedVersion}.")
    {
        StreamId = streamId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    /// <summary>Creates the exception.</summary>
    public WrongExpectedVersionException() : this("?", -1, -1) { }

    /// <summary>Creates the exception.</summary>
    public WrongExpectedVersionException(string message) : base(message) => StreamId = "?";

    /// <summary>Creates the exception.</summary>
    public WrongExpectedVersionException(string message, Exception innerException) : base(message, innerException) => StreamId = "?";

    /// <summary>The stream.</summary>
    public string StreamId { get; }

    /// <summary>The version the writer expected.</summary>
    public long ExpectedVersion { get; }

    /// <summary>The version found.</summary>
    public long ActualVersion { get; }
}
