namespace Chronos.Ledger.Contracts;

/// <summary>
/// A fact recorded on a loan's stream. These records are the module's published contract: the read side and
/// auditors depend on them, so a change is a new schema version with an upcaster, never an edit in place.
/// Amounts are decimals in the loan's currency (minor-unit precision).
/// </summary>
public interface ILedgerEvent
{
    /// <summary>The loan the event belongs to.</summary>
    string LoanId { get; }

    /// <summary>The business date the event takes effect.</summary>
    DateOnly EffectiveDate { get; }
}

/// <summary>A facility was opened. Interest runs from <paramref name="OpenedOn"/>.</summary>
public sealed record LoanOpened(string LoanId, string Borrower, string Currency, decimal AnnualRate, decimal FacilityLimit, DateOnly OpenedOn) : ILedgerEvent
{
    /// <inheritdoc />
    public DateOnly EffectiveDate => OpenedOn;
}

/// <summary>Operations asked to draw funds. Not in the balance until approved.</summary>
public sealed record DrawdownRequested(string LoanId, string DrawdownId, decimal Amount, string Currency, DateOnly RequestedOn, string RequestedBy) : ILedgerEvent
{
    /// <inheritdoc />
    public DateOnly EffectiveDate => RequestedOn;
}

/// <summary>A second person signed off; the amount is now principal from <paramref name="ApprovedOn"/>.</summary>
public sealed record DrawdownApproved(string LoanId, string DrawdownId, decimal Amount, string Currency, DateOnly ApprovedOn, string ApprovedBy) : ILedgerEvent
{
    /// <inheritdoc />
    public DateOnly EffectiveDate => ApprovedOn;
}

/// <summary>
/// Interest for the days after <paramref name="FromDate"/> up to and including <paramref name="ThroughDate"/>,
/// on each day's closing principal, using <paramref name="DayCount"/>. Schema v2 (v1 had no DayCount).
/// </summary>
public sealed record InterestAccrued(string LoanId, DateOnly FromDate, DateOnly ThroughDate, int Days, decimal AnnualRate, string DayCount, decimal Amount, string Currency) : ILedgerEvent
{
    /// <inheritdoc />
    public DateOnly EffectiveDate => ThroughDate;
}

/// <summary>Money received, split interest first, then principal.</summary>
public sealed record RepaymentRecorded(string LoanId, string RepaymentId, decimal Amount, decimal InterestPortion, decimal PrincipalPortion, string Currency, DateOnly ReceivedOn) : ILedgerEvent
{
    /// <inheritdoc />
    public DateOnly EffectiveDate => ReceivedOn;
}
