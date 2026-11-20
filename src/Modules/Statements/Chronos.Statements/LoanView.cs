using System.Globalization;
using Chronos.Ledger;
using Chronos.Ledger.Contracts;

namespace Chronos.Statements;

/// <summary>Projection state for one loan: a pure fold over its events. Used both live and for replay.</summary>
internal sealed class LoanView
{
    private readonly Dictionary<string, PendingDrawdown> _pending = new(StringComparer.Ordinal);
    private readonly List<StatementLine> _lines = [];

    public string LoanId { get; private set; } = "";
    public string Borrower { get; private set; } = "";
    public string Currency { get; private set; } = "";
    public decimal AnnualRate { get; private set; }
    public decimal FacilityLimit { get; private set; }
    public decimal Principal { get; private set; }
    public decimal Interest { get; private set; }
    public DateOnly? AccruedThrough { get; private set; }
    public DateOnly LastDate { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyList<StatementLine> Lines => _lines;
    public IReadOnlyCollection<PendingDrawdown> Pending => _pending.Values;

    public void Apply(LedgerEventEnvelope envelope)
    {
        var actor = envelope.Metadata.Actor;
        switch (envelope.Event)
        {
            case LoanOpened e:
                LoanId = e.LoanId; Borrower = e.Borrower; Currency = e.Currency; AnnualRate = e.AnnualRate; FacilityLimit = e.FacilityLimit;
                Line(envelope, "LoanOpened", e.LoanId, F($"Facility opened: limit {e.FacilityLimit:N2} {e.Currency} at {e.AnnualRate:P2}"), e.FacilityLimit, 0m, actor);
                break;
            case DrawdownRequested e:
                _pending[e.DrawdownId] = new PendingDrawdown(e.DrawdownId, e.Amount, e.Currency, e.RequestedOn, e.RequestedBy);
                Line(envelope, "DrawdownRequested", e.DrawdownId, F($"Drawdown {e.DrawdownId} requested, awaiting approval"), e.Amount, 0m, actor);
                break;
            case DrawdownApproved e:
                _pending.Remove(e.DrawdownId);
                Principal += e.Amount;
                Line(envelope, "DrawdownApproved", e.DrawdownId, F($"Drawdown {e.DrawdownId} approved"), e.Amount, e.Amount, actor);
                break;
            case InterestAccrued e:
                Interest += e.Amount;
                AccruedThrough = e.ThroughDate;
                Line(envelope, "InterestAccrued", F($"{e.FromDate:yyyy-MM-dd}..{e.ThroughDate:yyyy-MM-dd}"),
                    F($"Interest {e.Days} days {e.DayCount} at {e.AnnualRate:P2}"), e.Amount, e.Amount, actor);
                break;
            case RepaymentRecorded e:
                Interest -= e.InterestPortion;
                Principal -= e.PrincipalPortion;
                Line(envelope, "RepaymentRecorded", e.RepaymentId,
                    F($"Repayment {e.RepaymentId}: {e.InterestPortion:N2} interest, {e.PrincipalPortion:N2} principal"), e.Amount, -e.Amount, actor);
                break;
        }

        LastDate = envelope.Event.EffectiveDate;
        Version = envelope.Version;
    }

    public LoanBalance ToBalance(DateOnly? asOf = null)
    {
        var pending = _pending.Values.Sum(p => p.Amount);
        return new LoanBalance(LoanId, Borrower, Currency, AnnualRate, FacilityLimit, Principal, Interest, Principal + Interest,
            pending, _pending.Count, FacilityLimit - Principal - pending, AccruedThrough, asOf ?? LastDate, Version);
    }

    private void Line(LedgerEventEnvelope envelope, string kind, string reference, string description, decimal amount, decimal effect, string actor) =>
        _lines.Add(new StatementLine(envelope.Version, envelope.Event.EffectiveDate, kind, reference, description, amount, effect,
            Principal, Interest, Principal + Interest, actor));

    private static string F(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
