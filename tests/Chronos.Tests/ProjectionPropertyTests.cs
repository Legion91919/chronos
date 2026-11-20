using Agentic.Blocks.Core;
using Chronos.EventStore;
using Chronos.Ledger;
using Chronos.Statements;
using Xunit;

namespace Chronos.Statements.Tests;

/// <summary>
/// Property: for any sequence of commands, the balance projection equals (1) a fresh replay of the events,
/// (2) an independent day-by-day oracle, and (3) the point-in-time replay at the last business date.
/// Fixed seed so a failure is reproducible.
/// </summary>
public sealed class ProjectionPropertyTests
{
    private sealed class Oracle
    {
        public decimal Principal, Interest, Rate;
        public DateOnly AccruedThrough, LastDate;
        public readonly Dictionary<DateOnly, decimal> PrincipalOnDay = [];
        public readonly Dictionary<string, decimal> Pending = [];
    }

    [Fact]
    public async Task Balance_projection_always_equals_the_replay_of_events()
    {
        var random = new Random(20261120);
        for (var run = 0; run < 150; run++)
        {
            var (ledger, queries, store) = Harness.Create(new InMemoryEventStore());
            var loanId = $"P{run}";
            var opened = new DateOnly(2026, 1, 1).AddDays(random.Next(0, 60));
            var rate = random.Next(100, 1500) / 10_000m;
            var oracle = new Oracle { Rate = rate, AccruedThrough = opened.AddDays(-1), LastDate = opened };
            var open = await ledger.SendAsync(new OpenLoan(loanId, "Borrower", "GBP", rate, 5_000_000m, opened), new CommandContext($"open-{run}", "ops-1"));
            Assert.True(open.IsSuccess);
            var date = opened;
            var ids = 0;

            for (var step = 0; step < 40; step++)
            {
                date = date.AddDays(random.Next(0, 6));
                var context = new CommandContext($"k{run}-{step}", "ops-1");
                ILedgerCommand command;
                var kind = random.Next(4);
                string? drawdownToApprove = null;
                decimal amount = 0;
                switch (kind)
                {
                    case 0:
                        amount = random.Next(1, 400_000) + random.Next(0, 100) / 100m;
                        command = new RequestDrawdown(loanId, $"D{ids++}", Money.Of(amount, "GBP"), date);
                        break;
                    case 1 when oracle.Pending.Count > 0:
                        drawdownToApprove = oracle.Pending.Keys.ElementAt(random.Next(oracle.Pending.Count));
                        command = new ApproveDrawdown(loanId, drawdownToApprove, date);
                        context = context with { Actor = "fin-1" };
                        break;
                    case 2:
                        command = new AccrueInterest(loanId, date);
                        break;
                    default:
                        kind = 3;
                        var outstanding = oracle.Principal + oracle.Interest;
                        amount = outstanding <= 0 ? 1m : Math.Round(outstanding * random.Next(1, 110) / 100m, 2);
                        command = new RecordRepayment(loanId, $"R{ids++}", Money.Of(amount, "GBP"), date);
                        break;
                }

                var result = await ledger.SendAsync(command, context);
                if (result.IsSuccess && result.Value.EventsAppended > 0)
                {
                    oracle.LastDate = date;
                    switch (kind)
                    {
                        case 0: oracle.Pending[((RequestDrawdown)command).DrawdownId] = amount; break;
                        case 1:
                            oracle.Principal += oracle.Pending[drawdownToApprove!];
                            oracle.Pending.Remove(drawdownToApprove!);
                            oracle.PrincipalOnDay[date] = oracle.Principal;
                            break;
                        case 2:
                            // Oracle: day by day on each day's closing principal, one rounding per accrual.
                            decimal raw = 0, p = PrincipalAt(oracle, oracle.AccruedThrough);
                            for (var d = oracle.AccruedThrough.AddDays(1); d <= date; d = d.AddDays(1))
                            {
                                if (oracle.PrincipalOnDay.TryGetValue(d, out var changed)) p = changed;
                                raw += p * oracle.Rate;
                            }
                            oracle.Interest += Math.Round(raw / 365m, 2, MidpointRounding.AwayFromZero);
                            oracle.AccruedThrough = date;
                            break;
                        default:
                            var toInterest = Math.Min(amount, oracle.Interest);
                            oracle.Interest -= toInterest;
                            oracle.Principal -= amount - toInterest;
                            oracle.PrincipalOnDay[date] = oracle.Principal;
                            break;
                    }
                }

                if (random.Next(8) == 0)
                {
                    // A client retry: same command, same id. Never a second event.
                    var before = (await store.ReadStreamAsync(Streams.ForLoan(loanId))).Count;
                    var again = await ledger.SendAsync(command, context);
                    Assert.Equal(before, (await store.ReadStreamAsync(Streams.ForLoan(loanId))).Count);
                    if (result.IsSuccess) Assert.True(again.IsSuccess && again.Value.Duplicate);
                }
            }

            var projected = (await queries.GetBalanceAsync(loanId)).Value;
            Assert.Equal(oracle.Principal, projected.Principal);
            Assert.Equal(oracle.Interest, projected.AccruedInterest);
            Assert.Equal(oracle.Principal + oracle.Interest, projected.Outstanding);
            Assert.Equal(oracle.Pending.Values.Sum(), projected.PendingDrawdowns);

            var asOf = (await queries.GetBalanceAsOfAsync(loanId, oracle.LastDate)).Value;
            Assert.Equal(projected.Outstanding, asOf.Outstanding);
            Assert.Equal(projected.Version, asOf.Version);

            await queries.RebuildAsync();
            var rebuilt = (await queries.GetBalanceAsync(loanId)).Value;
            Assert.Equal(projected, rebuilt);
        }
    }

    private static decimal PrincipalAt(Oracle o, DateOnly day) =>
        o.PrincipalOnDay.Where(kv => kv.Key <= day).OrderBy(kv => kv.Key).Select(kv => kv.Value).LastOrDefault();
}
