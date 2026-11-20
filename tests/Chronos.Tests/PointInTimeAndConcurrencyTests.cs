using System.Text.Json;
using Agentic.Blocks.Core;
using Chronos.EventStore;
using Chronos.Ledger;
using Xunit;

namespace Chronos.Statements.Tests;

public sealed class PointInTimeAndConcurrencyTests
{
    private static readonly CommandContext Ops = new("unused", "ops-1");

    private static async Task<(ILedgerCommands, ILoanQueries, IEventStore)> SeededAsync(IEventStore? store = null)
    {
        var h = Harness.Create(store ?? new InMemoryEventStore());
        var (l, _, _) = h;
        await l.SendAsync(new OpenLoan("L1", "Borrower One", "GBP", 0.073m, 1_000_000m, new DateOnly(2026, 1, 1)), Ops with { CommandId = "c1" });
        await l.SendAsync(new RequestDrawdown("L1", "D1", Money.Of(100_000m, "GBP"), new DateOnly(2026, 1, 1)), Ops with { CommandId = "c2" });
        await l.SendAsync(new ApproveDrawdown("L1", "D1", new DateOnly(2026, 1, 2)), new CommandContext("c3", "fin-1"));
        await l.SendAsync(new AccrueInterest("L1", new DateOnly(2026, 1, 31)), new CommandContext("c4", "fin-1"));     // 30 days -> 600.00
        await l.SendAsync(new RecordRepayment("L1", "R1", Money.Of(10_600m, "GBP"), new DateOnly(2026, 2, 3)), Ops with { CommandId = "c5" });
        return h;
    }

    [Theory]
    [InlineData("2025-12-31", 0, 0)]          // before the loan existed: not found
    [InlineData("2026-01-01", 0, 0)]          // drawdown requested but awaiting approval
    [InlineData("2026-01-02", 100000, 0)]
    [InlineData("2026-01-31", 100000, 600)]
    [InlineData("2026-02-02", 100000, 600)]
    [InlineData("2026-02-03", 90000, 0)]
    public async Task Point_in_time_balance_replays_events_up_to_the_date(string asOf, double principal, double interest)
    {
        var (_, queries, _) = await SeededAsync();
        var result = await queries.GetBalanceAsOfAsync("L1", DateOnly.Parse(asOf));
        if (asOf == "2025-12-31") { Assert.Equal("loan.not_found", result.Error.Code); return; }
        Assert.Equal((decimal)principal, result.Value.Principal);
        Assert.Equal((decimal)interest, result.Value.AccruedInterest);
        Assert.Equal(DateOnly.Parse(asOf), result.Value.AsOf);
    }

    [Fact]
    public async Task A_pending_drawdown_shows_as_pending_not_as_balance()
    {
        var (ledger, queries, _) = await SeededAsync();
        await ledger.SendAsync(new RequestDrawdown("L1", "D2", Money.Of(50_000m, "GBP"), new DateOnly(2026, 2, 4)), Ops with { CommandId = "c6" });
        var balance = (await queries.GetBalanceAsync("L1")).Value;
        Assert.Equal(90_000m, balance.Outstanding);
        Assert.Equal(50_000m, balance.PendingDrawdowns);
        var pending = Assert.Single((await queries.GetPendingAsync("L1")).Value);
        Assert.Equal("D2", pending.DrawdownId);
    }

    [Fact]
    public async Task The_statement_lists_every_event_with_a_running_balance()
    {
        var (_, queries, _) = await SeededAsync();
        var lines = (await queries.GetStatementAsync("L1", null, null)).Value;
        Assert.Equal(["LoanOpened", "DrawdownRequested", "DrawdownApproved", "InterestAccrued", "RepaymentRecorded"], lines.Select(l => l.Kind).ToArray());
        Assert.Equal([0m, 0m, 100_000m, 100_600m, 90_000m], lines.Select(l => l.OutstandingAfter).ToArray());
        var february = (await queries.GetStatementAsync("L1", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28))).Value;
        Assert.Equal("RepaymentRecorded", Assert.Single(february).Kind);
    }

    [Fact]
    public async Task A_version_conflict_is_retried_on_fresh_state_and_both_commands_land()
    {
        var racing = new RacingStore(new InMemoryEventStore());
        var (ledger, queries, store) = await SeededAsync(racing);
        // While our repayment decides on version 5, another writer appends a drawdown request first.
        racing.BeforeNextAppend = () => store.AppendAsync(Streams.ForLoan("L1"), 5,
            [LedgerEventSerializer.Serialize(new Chronos.Ledger.Contracts.DrawdownRequested("L1", "D9", 1_000m, "GBP", new DateOnly(2026, 2, 5), "ops-2"),
                new EventMetadata("other", "ops-2", DateTimeOffset.UtcNow, new DateOnly(2026, 2, 5)))]);
        var result = await ledger.SendAsync(new RecordRepayment("L1", "R2", Money.Of(1_000m, "GBP"), new DateOnly(2026, 2, 6)), Ops with { CommandId = "c7" });
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Attempts);
        Assert.Equal(7, result.Value.Version);
        Assert.Equal(89_000m, (await queries.GetBalanceAsync("L1")).Value.Outstanding);
    }

    [Fact]
    public async Task Many_concurrent_commands_on_one_loan_all_land_exactly_once()
    {
        var (ledger, queries, store) = await SeededAsync();
        var tasks = Enumerable.Range(0, 25).Select(i => Task.Run(() =>
            ledger.SendAsync(new RecordRepayment("L1", $"PAR-{i}", Money.Of(100m, "GBP"), new DateOnly(2026, 2, 10)), Ops with { CommandId = $"par-{i}" })));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r.IsSuccess, r.IsFailure ? r.Error.Code : null));
        Assert.Equal(30, (await store.ReadStreamAsync(Streams.ForLoan("L1"))).Count);
        Assert.Equal(87_500m, (await queries.GetBalanceAsync("L1")).Value.Outstanding);
    }

    [Fact]
    public async Task When_conflicts_never_stop_the_command_fails_with_a_conflict_error()
    {
        var racing = new RacingStore(new InMemoryEventStore());
        var (ledger, _, store) = await SeededAsync(racing);
        racing.AlwaysConflict = true;
        var result = await ledger.SendAsync(new RecordRepayment("L1", "R2", Money.Of(1m, "GBP"), new DateOnly(2026, 2, 6)), Ops with { CommandId = "c8" });
        Assert.Equal("ledger.concurrency_conflict", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(5, (await store.ReadStreamAsync(Streams.ForLoan("L1"))).Count);
    }

    [Fact]
    public async Task Projections_rebuilt_from_the_file_store_after_a_restart_match()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ll-{Guid.NewGuid():N}.jsonl");
        var (_, before, _) = await SeededAsync(new JsonLinesEventStore(path));
        var expected = (await before.GetBalanceAsync("L1")).Value;
        var (_, after, _) = Harness.Create(new JsonLinesEventStore(path));
        Assert.Equal(expected, (await after.GetBalanceAsync("L1")).Value);
    }

    /// <summary>Lets a test slip a competing write in just before the next append, or refuse every append.</summary>
    private sealed class RacingStore(IEventStore inner) : IEventStore
    {
        public Func<Task<long>>? BeforeNextAppend { get; set; }
        public bool AlwaysConflict { get; set; }

        public async Task<long> AppendAsync(string streamId, long expectedVersion, IReadOnlyList<EventData> events, CancellationToken cancellationToken = default)
        {
            if (AlwaysConflict) throw new WrongExpectedVersionException(streamId, expectedVersion, expectedVersion + 1);
            var race = BeforeNextAppend;
            BeforeNextAppend = null;
            if (race is not null) await race();
            return await inner.AppendAsync(streamId, expectedVersion, events, cancellationToken);
        }

        public Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(string streamId, CancellationToken cancellationToken = default) => inner.ReadStreamAsync(streamId, cancellationToken);
        public Task<IReadOnlyList<StoredEvent>> ReadAllAsync(long afterPosition, int max, CancellationToken cancellationToken = default) => inner.ReadAllAsync(afterPosition, max, cancellationToken);
    }
}
