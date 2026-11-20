using Chronos.EventStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chronos.Ledger;

/// <summary>Registers the write side. Needs an <see cref="IEventStore"/> and a <see cref="TimeProvider"/>.</summary>
public static class LedgerModule
{
    /// <summary>Adds <see cref="ILedgerCommands"/>.</summary>
    public static IServiceCollection AddLedgerModule(this IServiceCollection services, Action<LedgerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<LedgerOptions>().Validate(o => o.MaxAttempts >= 1, "MaxAttempts must be at least 1.");
        if (configure is not null) options.Configure(configure);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEventStore, InMemoryEventStore>();
        services.TryAddSingleton<ILedgerCommands, LedgerCommandHandler>();
        return services;
    }
}
