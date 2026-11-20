using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agentic.Blocks.Testing;
using Chronos.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chronos.Api.Tests;

/// <summary>Slice 1 acceptance criteria, end to end over real HTTP.</summary>
public sealed class ApiAcceptanceTests
{
    private static Task<KestrelTestHost> StartAsync() => KestrelTestHost.StartAsync(
        b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["EventStore:Kind"] = "InMemory", ["Seed:Enabled"] = "false" });
            ChronosApp.ConfigureServices(b);
        },
        ChronosApp.Configure);

    private static Dictionary<string, string> Key(string key, string actor = "ops-1") => new() { ["Idempotency-Key"] = key, ["X-Actor"] = actor };

    private static async Task OpenAsync(KestrelTestHost host, string id = "HL-9001")
    {
        var r = await host.SendAsync(HttpMethod.Post, "/api/loans",
            new { loanId = id, borrower = "Example Borrower Ltd", currency = "GBP", annualRate = 0.073m, facilityLimit = 1_000_000m, openedOn = "2026-01-01" }, headers: Key($"open-{id}"));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Posting_the_same_drawdown_twice_with_one_idempotency_key_creates_one_event()
    {
        await using var host = await StartAsync();
        await OpenAsync(host);
        var body = new { drawdownId = "D-1", amount = 250_000m, currency = "GBP", requestedOn = "2026-01-05" };
        var first = await host.SendAsync(HttpMethod.Post, "/api/loans/HL-9001/drawdowns", body, headers: Key("dd-1"));
        var second = await host.SendAsync(HttpMethod.Post, "/api/loans/HL-9001/drawdowns", body, headers: Key("dd-1"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Contains("true", string.Join(",", second.Headers.TryGetValues("Idempotency-Replayed", out var v) ? v : []));
        var events = await JsonAsync(await host.Client.GetAsync("/api/loans/HL-9001/events"));
        Assert.Equal(1, events.EnumerateArray().Count(e => e.GetProperty("type").GetString() == "DrawdownRequested"));
    }

    [Fact]
    public async Task A_missing_idempotency_key_is_rejected()
    {
        await using var host = await StartAsync();
        var r = await host.SendAsync(HttpMethod.Post, "/api/loans", new { loanId = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task A_drawdown_awaiting_sign_off_is_not_in_the_balance_until_approved()
    {
        await using var host = await StartAsync();
        await OpenAsync(host);
        await host.SendAsync(HttpMethod.Post, "/api/loans/HL-9001/drawdowns", new { drawdownId = "D-1", amount = 250_000m, currency = "GBP", requestedOn = "2026-01-05" }, headers: Key("dd-1"));
        var pending = await JsonAsync(await host.Client.GetAsync("/api/loans/HL-9001"));
        Assert.Equal(0m, pending.GetProperty("outstanding").GetDecimal());
        Assert.Equal(250_000m, pending.GetProperty("pendingDrawdowns").GetDecimal());

        var selfApproval = await host.SendAsync(HttpMethod.Post, "/api/loans/HL-9001/drawdowns/D-1/approve", new { approvedOn = "2026-01-06" }, headers: Key("ap-0", "ops-1"));
        Assert.Equal(HttpStatusCode.Conflict, selfApproval.StatusCode);

        var approve = await host.SendAsync(HttpMethod.Post, "/api/loans/HL-9001/drawdowns/D-1/approve", new { approvedOn = "2026-01-06" }, headers: Key("ap-1", "fin-1"));
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var approved = await JsonAsync(await host.Client.GetAsync("/api/loans/HL-9001"));
        Assert.Equal(250_000m, approved.GetProperty("outstanding").GetDecimal());
        Assert.Equal(0m, approved.GetProperty("pendingDrawdowns").GetDecimal());
    }

    [Fact]
    public async Task The_balance_is_always_the_sum_of_the_ledger_events()
    {
        await using var host = await StartAsync();
        await OpenAsync(host);
        await host.SendAsync(HttpMethod.Post, "/api/loans/HL-9001/drawdowns", new { drawdownId = "D-1", amount = 100_000m, currency = "GBP", requestedOn = "2026-01-01" }, headers: Key("k1"));
        await host.SendAsync(HttpMethod.Post, "/api/loans/HL-9001/drawdowns/D-1/approve", new { approvedOn = "2026-01-02" }, headers: Key("k2", "fin-1"));
        var accrue = await host.SendAsync(HttpMethod.Post, "/api/loans/HL-9001/interest-accruals", new { through = "2026-01-31" }, headers: Key("k3", "fin-1"));
        Assert.Equal(HttpStatusCode.OK, accrue.StatusCode);
        var repay = await host.SendAsync(HttpMethod.Post, "/api/loans/HL-9001/repayments", new { repaymentId = "R-1", amount = 10_600m, currency = "GBP", receivedOn = "2026-02-03" }, headers: Key("k4"));
        Assert.Equal(HttpStatusCode.Created, repay.StatusCode);

        var statement = await JsonAsync(await host.Client.GetAsync("/api/loans/HL-9001/statement"));
        var sum = statement.EnumerateArray().Sum(l => l.GetProperty("balanceEffect").GetDecimal());
        var balance = await JsonAsync(await host.Client.GetAsync("/api/loans/HL-9001"));
        Assert.Equal(90_000m, balance.GetProperty("outstanding").GetDecimal());
        Assert.Equal(sum, balance.GetProperty("outstanding").GetDecimal());

        var asOf = await JsonAsync(await host.Client.GetAsync("/api/loans/HL-9001/balance?asOf=2026-01-31"));
        Assert.Equal(100_600m, asOf.GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task Domain_errors_are_problem_details_and_health_endpoints_answer()
    {
        await using var host = await StartAsync();
        var missing = await host.Client.GetAsync("/api/loans/NOPE");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("loan.not_found", (await JsonAsync(missing)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Accepted_commands_are_audited_and_announced_through_the_outbox()
    {
        await using var host = await StartAsync();
        await OpenAsync(host);
        var audit = host.App.Services.GetRequiredService<Agentic.Blocks.Audit.IAuditLog>();
        var entries = await audit.GetByEntityAsync("loan", "HL-9001", new Agentic.Blocks.Core.PageRequest(null, 10));
        var entry = Assert.Single(entries.Value.Items);
        Assert.Equal("ledger.loan-opened", entry.Action);
        Assert.Equal("ops-1", entry.Actor);
        Assert.True((await audit.VerifyIntegrityAsync()).IsSuccess);

        var feed = host.App.Services.GetRequiredService<CashDeskFeed>();
        for (var i = 0; i < 100 && feed.Snapshot().Count == 0; i++) await Task.Delay(50);
        var notice = Assert.Single(feed.Snapshot());
        Assert.Equal("LoanOpened", notice.EventType);
        Assert.Equal("HL-9001", notice.LoanId);
    }
}
