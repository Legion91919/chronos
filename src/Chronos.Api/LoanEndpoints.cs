using System.Text.RegularExpressions;
using Agentic.Blocks.Audit;
using Agentic.Blocks.Core;
using Agentic.Blocks.Idempotency;
using Agentic.Blocks.Outbox;
using Agentic.Blocks.Web;
using Chronos.Ledger;
using Chronos.Statements;

namespace Chronos.Api;

internal sealed record OpenLoanBody(string? LoanId, string? Borrower, string? Currency, decimal AnnualRate, decimal FacilityLimit, DateOnly OpenedOn);
internal sealed record DrawdownBody(string? DrawdownId, decimal Amount, string? Currency, DateOnly RequestedOn);
internal sealed record ApproveBody(DateOnly ApprovedOn);
internal sealed record AccrualBody(DateOnly Through);
internal sealed record RepaymentBody(string? RepaymentId, decimal Amount, string? Currency, DateOnly ReceivedOn);

/// <summary>What a command returns: the stream version, whether it was a replay, and the balance after it.</summary>
internal sealed record CommandResponse(string LoanId, long Version, int EventsAppended, bool Duplicate, int Attempts, LoanBalance Balance);

internal static partial class LoanEndpoints
{
    private const string ActorHeader = "X-Actor";

    public static void MapLoanEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireRateLimiting(BlocksRateLimitPolicies.Api).WithTags("Loans");

        // Queries (read side)
        api.MapGet("/loans", async (ILoanQueries q, CancellationToken ct) => Results.Ok(await q.ListAsync(ct).ConfigureAwait(false)));
        api.MapGet("/loans/{loanId}", async (string loanId, ILoanQueries q, CancellationToken ct) =>
            (await q.GetBalanceAsync(loanId, ct).ConfigureAwait(false)).ToHttpResult());
        api.MapGet("/loans/{loanId}/balance", async (string loanId, DateOnly? asOf, ILoanQueries q, CancellationToken ct) =>
            (asOf is { } date ? await q.GetBalanceAsOfAsync(loanId, date, ct).ConfigureAwait(false) : await q.GetBalanceAsync(loanId, ct).ConfigureAwait(false)).ToHttpResult());
        api.MapGet("/loans/{loanId}/statement", async (string loanId, DateOnly? from, DateOnly? to, ILoanQueries q, CancellationToken ct) =>
            (await q.GetStatementAsync(loanId, from, to, ct).ConfigureAwait(false)).ToHttpResult());
        api.MapGet("/loans/{loanId}/pending", async (string loanId, ILoanQueries q, CancellationToken ct) =>
            (await q.GetPendingAsync(loanId, ct).ConfigureAwait(false)).ToHttpResult());
        api.MapGet("/loans/{loanId}/events", async (string loanId, ILoanQueries q, CancellationToken ct) =>
            (await q.GetEventsAsync(loanId, ct).ConfigureAwait(false)).ToHttpResult());
        api.MapGet("/cash-desk/feed", (CashDeskFeed feed) => Results.Ok(feed.Snapshot()));

        // Commands (write side); every one needs an Idempotency-Key
        api.MapPost("/loans", (OpenLoanBody b, HttpContext http) =>
            Send(http, new OpenLoan(b.LoanId ?? "", b.Borrower ?? "", b.Currency ?? "", b.AnnualRate, b.FacilityLimit, b.OpenedOn), "LoanOpened",
                r => Results.Created($"/api/loans/{r.LoanId}", r))).RequireIdempotencyKey();

        api.MapPost("/loans/{loanId}/drawdowns", (string loanId, DrawdownBody b, HttpContext http) =>
            WithMoney(b.Amount, b.Currency, m => Send(http, new RequestDrawdown(loanId, b.DrawdownId ?? "", m, b.RequestedOn), "DrawdownRequested",
                r => Results.Created($"/api/loans/{loanId}/drawdowns/{Uri.EscapeDataString(b.DrawdownId ?? "")}", r)))).RequireIdempotencyKey();

        api.MapPost("/loans/{loanId}/drawdowns/{drawdownId}/approve", (string loanId, string drawdownId, ApproveBody b, HttpContext http) =>
            Send(http, new ApproveDrawdown(loanId, drawdownId, b.ApprovedOn), "DrawdownApproved", Results.Ok)).RequireIdempotencyKey();

        api.MapPost("/loans/{loanId}/interest-accruals", (string loanId, AccrualBody b, HttpContext http) =>
            Send(http, new AccrueInterest(loanId, b.Through), "InterestAccrued", Results.Ok)).RequireIdempotencyKey();

        api.MapPost("/loans/{loanId}/repayments", (string loanId, RepaymentBody b, HttpContext http) =>
            WithMoney(b.Amount, b.Currency, m => Send(http, new RecordRepayment(loanId, b.RepaymentId ?? "", m, b.ReceivedOn), "RepaymentRecorded",
                r => Results.Created($"/api/loans/{loanId}/repayments/{Uri.EscapeDataString(b.RepaymentId ?? "")}", r)))).RequireIdempotencyKey();

        // Throw the projections away and replay every event (Development only).
        if (app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            api.MapPost("/projections/rebuild", async (ILoanQueries q, CancellationToken ct) =>
                Results.Ok(new { eventsReplayed = await q.RebuildAsync(ct).ConfigureAwait(false) }));
        }
    }

    private static Task<IResult> WithMoney(decimal amount, string? currency, Func<Money, Task<IResult>> next)
    {
        var money = Money.TryOf(amount, currency);
        return money.IsSuccess ? next(money.Value) : Task.FromResult(money.Error.ToProblem());
    }

    private static async Task<IResult> Send(HttpContext http, ILedgerCommand command, string eventType, Func<CommandResponse, IResult> onSuccess)
    {
        var services = http.RequestServices;
        var ct = http.RequestAborted;
        var actor = http.Request.Headers[ActorHeader].FirstOrDefault() ?? "ops-demo";
        if (!ActorPattern().IsMatch(actor)) return Error.Validation("actor.invalid", ActorHeader, "X-Actor must be an opaque id (a-z, 0-9, '.', '-', max 40).").ToProblem();
        var key = http.Request.Headers["Idempotency-Key"].ToString();

        var outcome = await services.GetRequiredService<ILedgerCommands>().SendAsync(command, new CommandContext(key, actor), ct).ConfigureAwait(false);
        if (outcome.IsFailure) return outcome.Error.ToProblem();

        var o = outcome.Value;
        if (!o.Duplicate)
        {
            await services.GetRequiredService<IAuditLog>().AppendAsync(new AuditRecord(actor, "ledger." + Kebab(eventType), "loan", o.LoanId), ct).ConfigureAwait(false);
            services.GetRequiredService<IOutbox>().Enqueue(new LedgerEventNotice(o.LoanId, eventType, o.Version));
        }

        var balance = await services.GetRequiredService<ILoanQueries>().GetBalanceAsync(o.LoanId, ct).ConfigureAwait(false);
        return onSuccess(new CommandResponse(o.LoanId, o.Version, o.EventsAppended, o.Duplicate, o.Attempts, balance.Value));
    }

    private static string Kebab(string pascal) => KebabPattern().Replace(pascal, "-$1").ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9.-]{1,40}$")]
    private static partial Regex ActorPattern();

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex KebabPattern();
}
