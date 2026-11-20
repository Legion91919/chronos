using Chronos.EventStore;
using Chronos.Ledger;
using Chronos.Statements;
using Microsoft.Extensions.DependencyInjection;

namespace Chronos.Statements.Tests;

internal static class Harness
{
    public static (ILedgerCommands Ledger, ILoanQueries Queries, IEventStore Store) Create(IEventStore store, TimeProvider? time = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(time ?? TimeProvider.System)
            .AddSingleton(store)
            .AddLedgerModule()
            .AddStatementsModule()
            .BuildServiceProvider();
        return (services.GetRequiredService<ILedgerCommands>(), services.GetRequiredService<ILoanQueries>(), store);
    }
}
