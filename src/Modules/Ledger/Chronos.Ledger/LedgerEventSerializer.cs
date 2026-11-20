using System.Text.Json;
using System.Text.Json.Nodes;
using Chronos.EventStore;
using Chronos.Ledger.Contracts;

namespace Chronos.Ledger;

/// <summary>A deserialized ledger event with its position, version and metadata.</summary>
public sealed record LedgerEventEnvelope(long Position, string StreamId, long Version, ILedgerEvent Event, EventMetadata Metadata);

/// <summary>
/// Maps ledger events to and from stored JSON. Type names and schema versions are part of the contract;
/// old versions are upcast on read (see <see cref="UpcasterChain"/>).
/// </summary>
public static class LedgerEventSerializer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, (Type Type, int Version)> ByName = new(StringComparer.Ordinal)
    {
        [nameof(LoanOpened)] = (typeof(LoanOpened), 1),
        [nameof(DrawdownRequested)] = (typeof(DrawdownRequested), 1),
        [nameof(DrawdownApproved)] = (typeof(DrawdownApproved), 1),
        [nameof(InterestAccrued)] = (typeof(InterestAccrued), 2),
        [nameof(RepaymentRecorded)] = (typeof(RepaymentRecorded), 1),
    };

    private static readonly UpcasterChain Upcasters = new UpcasterChain()
        // v1 -> v2: the day-count convention became explicit. Every v1 event was Actual/365.
        .Register(nameof(InterestAccrued), 1, o => { o["dayCount"] = "Actual/365"; return o; });

    /// <summary>Serializes an event at its current schema version.</summary>
    public static EventData Serialize(ILedgerEvent @event, EventMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var name = @event.GetType().Name;
        var (type, version) = ByName[name];
        var node = JsonSerializer.SerializeToNode(@event, type, Json)!.AsObject();
        node.Remove("effectiveDate"); // derived, not stored
        return new EventData(name, version, JsonSerializer.SerializeToElement<JsonNode>(node, Json), metadata);
    }

    /// <summary>Deserializes a stored event, upcasting old schema versions.</summary>
    public static LedgerEventEnvelope Deserialize(StoredEvent stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (!ByName.TryGetValue(stored.Type, out var target)) throw new InvalidDataException($"Unknown ledger event type '{stored.Type}'.");
        var (data, version) = Upcasters.Upcast(stored.Type, stored.SchemaVersion, stored.Data);
        if (version != target.Version) throw new InvalidDataException($"No upcaster from {stored.Type} v{version} to v{target.Version}.");
        var @event = (ILedgerEvent)(data.Deserialize(target.Type, Json) ?? throw new InvalidDataException("Empty event payload."));
        return new LedgerEventEnvelope(stored.Position, stored.StreamId, stored.Version, @event, stored.Metadata);
    }
}
