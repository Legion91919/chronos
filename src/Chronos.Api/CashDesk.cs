using System.Collections.Concurrent;
using Agentic.Blocks.Outbox;

namespace Chronos.Api;

/// <summary>Outbox message: a ledger event the cash desk should know about. Ids only, no personal data.</summary>
internal sealed record LedgerEventNotice(string LoanId, string EventType, long Version);

/// <summary>A notice as the cash desk received it.</summary>
internal sealed record CashDeskNotice(Guid MessageId, string LoanId, string EventType, long Version, DateTimeOffset ReceivedAt);

/// <summary>Stand-in for the treasury/cash-desk integration: an in-memory feed de-duplicated on the outbox message id.</summary>
internal sealed class CashDeskFeed(TimeProvider time)
{
    private readonly ConcurrentDictionary<Guid, CashDeskNotice> _notices = new();

    public void Receive(Guid messageId, LedgerEventNotice notice) =>
        _notices.TryAdd(messageId, new CashDeskNotice(messageId, notice.LoanId, notice.EventType, notice.Version, time.GetUtcNow()));

    public IReadOnlyList<CashDeskNotice> Snapshot() => _notices.Values.OrderBy(n => n.ReceivedAt).ThenBy(n => n.Version).ToArray();
}

/// <summary>Delivers outbox notices to the feed. Idempotent: a redelivery with the same message id is ignored.</summary>
internal sealed class CashDeskNotifier(CashDeskFeed feed) : IOutboxHandler<LedgerEventNotice>
{
    public Task HandleAsync(LedgerEventNotice message, OutboxMessageContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        feed.Receive(context.MessageId, message);
        return Task.CompletedTask;
    }
}
