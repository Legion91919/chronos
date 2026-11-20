using System.Text.Json;
using Chronos.EventStore;
using Chronos.Ledger;
using Chronos.Ledger.Contracts;
using Xunit;

namespace Chronos.EventStore.Tests;

public sealed class EventStoreTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private static EventData Evt(string type, int n) =>
        new(type, 1, JsonSerializer.SerializeToElement(new { n }), new EventMetadata($"cmd-{n}", "ops-1", At, new DateOnly(2026, 1, n)));

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"ll-{Guid.NewGuid():N}.jsonl");

    public static IEnumerable<Func<IEventStore>> Stores() =>
    [
        () => new InMemoryEventStore(),
        () => new JsonLinesEventStore(TempFile()),
    ];

    [Fact]
    public async Task Both_stores_append_and_read_in_order_with_versions_and_global_positions()
    {
        foreach (var make in Stores())
        {
            var store = make();
            Assert.Equal(2, await store.AppendAsync("loan-a", 0, [Evt("A", 1), Evt("A", 2)]));
            Assert.Equal(1, await store.AppendAsync("loan-b", 0, [Evt("B", 3)]));
            var a = await store.ReadStreamAsync("loan-a");
            Assert.Equal([1L, 2L], a.Select(e => e.Version).ToArray());
            var all = await store.ReadAllAsync(0, 100);
            Assert.Equal([1L, 2L, 3L], all.Select(e => e.Position).ToArray());
            Assert.Equal("cmd-3", all[2].Metadata.CommandId);
            Assert.Single(await store.ReadAllAsync(2, 100));
        }
    }

    [Fact]
    public async Task Both_stores_reject_a_stale_expected_version()
    {
        foreach (var make in Stores())
        {
            var store = make();
            await store.AppendAsync("loan-a", 0, [Evt("A", 1)]);
            var ex = await AssertThrowsAsync<WrongExpectedVersionException>(() => store.AppendAsync("loan-a", 0, [Evt("A", 2)]));
            Assert.Equal(1, ex.ActualVersion);
            Assert.Single(await store.ReadStreamAsync("loan-a"));
        }
    }

    [Fact]
    public async Task The_file_store_survives_a_restart()
    {
        var path = TempFile();
        var first = new JsonLinesEventStore(path);
        await first.AppendAsync("loan-a", 0, [Evt("A", 1), Evt("A", 2)]);
        var reopened = new JsonLinesEventStore(path);
        var events = await reopened.ReadStreamAsync("loan-a");
        Assert.Equal(2, events.Count);
        Assert.Equal(1, await reopened.AppendAsync("loan-b", 0, [Evt("B", 3)]));
        Assert.Equal(3, (await reopened.ReadAllAsync(0, 10))[^1].Position);
        Assert.Equal(3, File.ReadAllLines(path).Length);
    }

    [Fact]
    public async Task The_file_store_drops_a_torn_last_line_from_a_crash_mid_write()
    {
        var path = TempFile();
        await new JsonLinesEventStore(path).AppendAsync("loan-a", 0, [Evt("A", 1)]);
        await File.AppendAllTextAsync(path, "{\"position\":2,\"streamId\":\"loan-a\",\"ver");
        var reopened = new JsonLinesEventStore(path);
        Assert.Single(await reopened.ReadStreamAsync("loan-a"));
        Assert.Equal(2, await reopened.AppendAsync("loan-a", 1, [Evt("A", 2)]));
        Assert.Equal(2, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void An_old_schema_version_is_upcast_on_read()
    {
        // v1 of InterestAccrued had no dayCount field; v2 made the convention explicit.
        var v1 = JsonSerializer.SerializeToElement(new
        {
            loanId = "L1", fromDate = "2026-01-04", throughDate = "2026-01-31", days = 27, annualRate = 0.073m, amount = 420.00m, currency = "GBP",
        });
        var stored = new StoredEvent(7, "loan-L1", 3, "InterestAccrued", 1, v1, new EventMetadata("c", "fin-1", At, new DateOnly(2026, 1, 31)));
        var envelope = LedgerEventSerializer.Deserialize(stored);
        var accrued = envelope.Event as InterestAccrued;
        Assert.NotNull(accrued);
        Assert.Equal("Actual/365", accrued!.DayCount);
        Assert.Equal(420.00m, accrued.Amount);
    }

    private static async Task<T> AssertThrowsAsync<T>(Func<Task> act) where T : Exception
    {
        try { await act(); }
        catch (T ex) { return ex; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
}
