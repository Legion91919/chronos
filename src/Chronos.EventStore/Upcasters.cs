using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chronos.EventStore;

/// <summary>
/// Event versioning by upcasting: stored events are never rewritten; old schema versions are converted to the
/// current shape when read. Register one step per (type, fromVersion).
/// </summary>
public sealed class UpcasterChain
{
    private readonly Dictionary<(string Type, int From), Func<JsonObject, JsonObject>> _steps = [];

    /// <summary>Registers a step that turns <paramref name="type"/> v<paramref name="fromVersion"/> into v<paramref name="fromVersion"/>+1.</summary>
    public UpcasterChain Register(string type, int fromVersion, Func<JsonObject, JsonObject> step)
    {
        _steps[(type, fromVersion)] = step;
        return this;
    }

    /// <summary>Applies steps until no further step exists; returns the payload and the version reached.</summary>
    public (JsonElement Data, int Version) Upcast(string type, int version, JsonElement data)
    {
        if (!_steps.ContainsKey((type, version))) return (data, version);
        var node = JsonNode.Parse(data.GetRawText())!.AsObject();
        while (_steps.TryGetValue((type, version), out var step))
        {
            node = step(node);
            version++;
        }

        return (JsonSerializer.SerializeToElement(node), version);
    }
}
