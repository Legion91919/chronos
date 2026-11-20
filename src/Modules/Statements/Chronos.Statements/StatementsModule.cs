using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chronos.Statements;

/// <summary>Registers the read side.</summary>
public static class StatementsModule
{
    /// <summary>Adds <see cref="ILoanQueries"/> (projections over the shared event store).</summary>
    public static IServiceCollection AddStatementsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ILoanQueries, LoanQueries>();
        return services;
    }
}
