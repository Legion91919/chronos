using System.Text.Json;
using Agentic.Blocks.Core;

namespace Chronos.Statements;

/// <summary>A loan's position. Outstanding = principal + accrued interest; pending drawdowns are shown, never counted.</summary>
public sealed record LoanBalance(
    string LoanId, string Borrower, string Currency, decimal AnnualRate, decimal FacilityLimit,
    decimal Principal, decimal AccruedInterest, decimal Outstanding, decimal PendingDrawdowns, int PendingCount, decimal Available,
    DateOnly? AccruedThrough, DateOnly AsOf, long Version);

/// <summary>One statement line per event. <see cref="BalanceEffect"/> summed over all lines equals the outstanding balance.</summary>
public sealed record StatementLine(
    long Version, DateOnly Date, string Kind, string Reference, string Description, decimal Amount, decimal BalanceEffect,
    decimal PrincipalAfter, decimal InterestAfter, decimal OutstandingAfter, string Actor);

/// <summary>A drawdown waiting for sign-off.</summary>
public sealed record PendingDrawdown(string DrawdownId, decimal Amount, string Currency, DateOnly RequestedOn, string RequestedBy);

/// <summary>A raw event, as an auditor sees it.</summary>
public sealed record LoanEventView(
    long Position, long Version, string Type, int SchemaVersion, DateOnly EffectiveDate, DateTimeOffset RecordedAt,
    string Actor, string? CommandId, JsonElement Data);

/// <summary>The read side: projections kept current from the event store, plus replay for point-in-time questions.</summary>
public interface ILoanQueries
{
    /// <summary>All loans, by id.</summary>
    Task<IReadOnlyList<LoanBalance>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Current balance from the balance projection.</summary>
    Task<Result<LoanBalance>> GetBalanceAsync(string loanId, CancellationToken cancellationToken = default);

    /// <summary>Balance at the end of <paramref name="asOf"/>, by replaying the loan's events dated on or before it.</summary>
    Task<Result<LoanBalance>> GetBalanceAsOfAsync(string loanId, DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>Statement lines, optionally limited to business dates from/to (inclusive).</summary>
    Task<Result<IReadOnlyList<StatementLine>>> GetStatementAsync(string loanId, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default);

    /// <summary>Drawdowns awaiting approval.</summary>
    Task<Result<IReadOnlyList<PendingDrawdown>>> GetPendingAsync(string loanId, CancellationToken cancellationToken = default);

    /// <summary>The loan's raw event history.</summary>
    Task<Result<IReadOnlyList<LoanEventView>>> GetEventsAsync(string loanId, CancellationToken cancellationToken = default);

    /// <summary>Throws the projections away and replays every event from position 0. Returns the number of events replayed.</summary>
    Task<long> RebuildAsync(CancellationToken cancellationToken = default);
}
