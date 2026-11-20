using Agentic.Blocks.Audit;
using Agentic.Blocks.Idempotency;
using Agentic.Blocks.Outbox;
using Agentic.Blocks.Web;
using Chronos.EventStore;
using Chronos.Ledger;
using Chronos.Statements;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chronos.Api;

/// <summary>Composition root, shared by Program and the acceptance tests.</summary>
internal static class ChronosApp
{
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services.AddBlocksWeb();
        services.AddBlocksIdempotency();
        services.AddBlocksAudit();
        services.AddBlocksOutbox(o => o.BaseDelay = TimeSpan.FromSeconds(1))
            .AddOutboxHandler<LedgerEventNotice, CashDeskNotifier>("ledger.event-notice.v1");
        services.AddSingleton<CashDeskFeed>();

        services.AddSingleton<IEventStore>(_ => config["EventStore:Kind"] switch
        {
            "InMemory" => new InMemoryEventStore(),
            _ => new JsonLinesEventStore(Path.Combine(builder.Environment.ContentRootPath, config["EventStore:Path"] ?? "App_Data/events.jsonl")),
        });
        services.AddLedgerModule(o => o.MaxAttempts = config.GetValue("Ledger:MaxAttempts", 10));
        services.AddStatementsModule();

        services.AddHealthChecks().AddCheck<EventStoreHealthCheck>("event-store", tags: [BlocksHealthTags.Ready]);
        if (builder.Environment.IsDevelopment() && config.GetValue("Seed:Enabled", true)) services.AddHostedService<DemoSeeder>();
    }

    public static void Configure(WebApplication app)
    {
        app.UseBlocksWeb();
        app.UseBlocksOutbox();
        app.MapLoanEndpoints();
    }
}

/// <summary>Ready when the event store can be read.</summary>
internal sealed class EventStoreHealthCheck(IEventStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await store.ReadAllAsync(0, 1, cancellationToken).ConfigureAwait(false);
        return HealthCheckResult.Healthy();
    }
}
