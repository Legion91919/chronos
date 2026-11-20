using Agentic.Blocks.Core;
using Chronos.Ledger;

namespace Chronos.Api;

/// <summary>Seeds invented demo loans in Development when the store is empty. Idempotent by command id.</summary>
internal sealed partial class DemoSeeder(ILedgerCommands ledger, ILogger<DemoSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var ops = "ops-17";
        var fin = "fin-04";
        var n = 0;
        async Task Do(ILedgerCommand c, string actor)
        {
            var r = await ledger.SendAsync(c, new CommandContext($"seed-{++n}", actor), cancellationToken).ConfigureAwait(false);
            if (r.IsFailure) LogSeedFailed(logger, n, r.Error.Code);
        }

        static Money Gbp(decimal a) => Money.Of(a, "GBP");

        await Do(new OpenLoan("HL-1001", "Tidewater Court Developments", "GBP", 0.0725m, 2_500_000m, new DateOnly(2026, 1, 5)), ops).ConfigureAwait(false);
        await Do(new RequestDrawdown("HL-1001", "DD-1001-01", Gbp(800_000m), new DateOnly(2026, 1, 10)), ops).ConfigureAwait(false);
        await Do(new ApproveDrawdown("HL-1001", "DD-1001-01", new DateOnly(2026, 1, 12)), fin).ConfigureAwait(false);
        await Do(new AccrueInterest("HL-1001", new DateOnly(2026, 1, 31)), fin).ConfigureAwait(false);
        await Do(new RecordRepayment("HL-1001", "RP-1001-01", Gbp(20_000m), new DateOnly(2026, 2, 2)), ops).ConfigureAwait(false);
        await Do(new RequestDrawdown("HL-1001", "DD-1001-02", Gbp(350_000m), new DateOnly(2026, 2, 10)), ops).ConfigureAwait(false);

        await Do(new OpenLoan("HL-1002", "Saltmarsh Lofts", "GBP", 0.068m, 1_200_000m, new DateOnly(2026, 2, 1)), ops).ConfigureAwait(false);
        await Do(new RequestDrawdown("HL-1002", "DD-1002-01", Gbp(450_000m), new DateOnly(2026, 2, 3)), ops).ConfigureAwait(false);
        await Do(new ApproveDrawdown("HL-1002", "DD-1002-01", new DateOnly(2026, 2, 4)), fin).ConfigureAwait(false);
        await Do(new AccrueInterest("HL-1002", new DateOnly(2026, 2, 28)), fin).ConfigureAwait(false);

        await Do(new OpenLoan("HL-1003", "Copperbeam Terraces", "GBP", 0.0795m, 900_000m, new DateOnly(2026, 3, 2)), ops).ConfigureAwait(false);
        await Do(new RequestDrawdown("HL-1003", "DD-1003-01", Gbp(125_000m), new DateOnly(2026, 3, 9)), ops).ConfigureAwait(false);
        LogSeeded(logger);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo seed step {Step} refused: {Code}")]
    private static partial void LogSeedFailed(ILogger logger, int step, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo loans seeded (re-runs are no-ops: commands are idempotent)")]
    private static partial void LogSeeded(ILogger logger);
}
