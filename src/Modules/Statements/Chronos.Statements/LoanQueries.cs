using Agentic.Blocks.Core;
using Chronos.EventStore;
using Chronos.Ledger;

namespace Chronos.Statements;

/// <summary>
/// Balance and statement projections. They keep a checkpoint (the last global position applied) and catch up
/// from the event store before answering, so a reader always sees its own writes. Rebuild = reset the checkpoint
/// to 0 and replay. Point-in-time answers never touch the projection: they replay the loan's stream.
/// </summary>
internal sealed class LoanQueries(IEventStore store) : ILoanQueries, IDisposable
{
    private const int BatchSize = 500;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, LoanView> _views = new(StringComparer.Ordinal);
    private long _checkpoint;

    public async Task<IReadOnlyList<LoanBalance>> ListAsync(CancellationToken cancellationToken = default) =>
        await ReadAsync(views => views.Values.Select(v => v.ToBalance()).OrderBy(b => b.LoanId, StringComparer.Ordinal).ToArray(), cancellationToken).ConfigureAwait(false);

    public Task<Result<LoanBalance>> GetBalanceAsync(string loanId, CancellationToken cancellationToken = default) =>
        ReadLoanAsync(loanId, v => v.ToBalance(), cancellationToken);

    public Task<Result<IReadOnlyList<StatementLine>>> GetStatementAsync(string loanId, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default) =>
        ReadLoanAsync<IReadOnlyList<StatementLine>>(loanId,
            v => v.Lines.Where(l => (fromDate is null || l.Date >= fromDate) && (toDate is null || l.Date <= toDate)).ToArray(), cancellationToken);

    public Task<Result<IReadOnlyList<PendingDrawdown>>> GetPendingAsync(string loanId, CancellationToken cancellationToken = default) =>
        ReadLoanAsync<IReadOnlyList<PendingDrawdown>>(loanId, v => v.Pending.OrderBy(p => p.RequestedOn).ToArray(), cancellationToken);

    public async Task<Result<LoanBalance>> GetBalanceAsOfAsync(string loanId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        // Business dates in a stream never go backwards, so "dated on or before asOf" is a prefix of the stream.
        var view = new LoanView();
        var any = false;
        foreach (var stored in await store.ReadStreamAsync(Streams.ForLoan(loanId), cancellationToken).ConfigureAwait(false))
        {
            if (stored.Metadata.EffectiveDate > asOf) break;
            view.Apply(LedgerEventSerializer.Deserialize(stored));
            any = true;
        }

        return any ? view.ToBalance(asOf) : NotFound(loanId);
    }

    public async Task<Result<IReadOnlyList<LoanEventView>>> GetEventsAsync(string loanId, CancellationToken cancellationToken = default)
    {
        var events = await store.ReadStreamAsync(Streams.ForLoan(loanId), cancellationToken).ConfigureAwait(false);
        if (events.Count == 0) return NotFound(loanId);
        return events.Select(e => new LoanEventView(e.Position, e.Version, e.Type, e.SchemaVersion, e.Metadata.EffectiveDate, e.Metadata.RecordedAt,
            e.Metadata.Actor, e.Metadata.CommandId, e.Data)).ToArray();
    }

    public async Task<long> RebuildAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _views = new Dictionary<string, LoanView>(StringComparer.Ordinal);
            _checkpoint = 0;
            await CatchUpLockedAsync(cancellationToken).ConfigureAwait(false);
            return _checkpoint;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<Result<T>> ReadLoanAsync<T>(string loanId, Func<LoanView, T> select, CancellationToken cancellationToken) =>
        await ReadAsync<Result<T>>(views => views.TryGetValue(loanId, out var v) ? select(v) : NotFound(loanId), cancellationToken).ConfigureAwait(false);

    private async Task<T> ReadAsync<T>(Func<Dictionary<string, LoanView>, T> read, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CatchUpLockedAsync(cancellationToken).ConfigureAwait(false);
            return read(_views);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CatchUpLockedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var batch = await store.ReadAllAsync(_checkpoint, BatchSize, cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0) return;
            foreach (var stored in batch)
            {
                if (stored.StreamId.StartsWith(Streams.LoanPrefix, StringComparison.Ordinal))
                {
                    var envelope = LedgerEventSerializer.Deserialize(stored);
                    if (!_views.TryGetValue(envelope.Event.LoanId, out var view)) _views[envelope.Event.LoanId] = view = new LoanView();
                    view.Apply(envelope);
                }

                _checkpoint = stored.Position;
            }
        }
    }

    private static Error NotFound(string loanId) => Error.NotFound("loan.not_found", $"Loan {loanId} not found.");
}
