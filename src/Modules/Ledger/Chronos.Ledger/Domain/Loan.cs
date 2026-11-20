using Agentic.Blocks.Core;
using Chronos.Ledger.Contracts;

namespace Chronos.Ledger.Domain;

/// <summary>
/// The loan aggregate. State exists only as a fold over its events (<see cref="Apply"/>); <c>Decide</c> methods
/// check the rules and return new events without changing state. Rules:
/// business dates in a stream never go backwards; money cannot move on or before the last accrued day;
/// a drawdown counts only once a different person approves it; repayments pay interest first.
/// </summary>
internal sealed class Loan
{
    private readonly Dictionary<string, Drawdown> _drawdowns = new(StringComparer.Ordinal);
    private readonly HashSet<string> _repayments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _processedCommands = new(StringComparer.Ordinal);
    private readonly List<(DateOnly Day, decimal PrincipalAfter)> _changesSinceAccrual = [];
    private decimal _principalAtLastAccrual;

    private sealed record Drawdown(string Id, decimal Amount, DateOnly RequestedOn, string RequestedBy, bool Approved);

    public bool Exists { get; private set; }
    public string LoanId { get; private set; } = "";
    public string Currency { get; private set; } = "";
    public decimal AnnualRate { get; private set; }
    public decimal FacilityLimit { get; private set; }
    public decimal Principal { get; private set; }
    public decimal AccruedInterest { get; private set; }
    public decimal PendingTotal => _drawdowns.Values.Where(d => !d.Approved).Sum(d => d.Amount);
    public DateOnly AccruedThrough { get; private set; }
    public DateOnly LastEffectiveDate { get; private set; }
    public long Version { get; private set; }

    public bool HasProcessed(string commandId, string commandType) =>
        _processedCommands.TryGetValue(commandId, out var t) && t == commandType;

    public string? ProcessedCommandType(string commandId) => _processedCommands.GetValueOrDefault(commandId);

    public Result<IReadOnlyList<ILedgerEvent>> Decide(ILedgerCommand command, string actor) => command switch
    {
        OpenLoan c => Open(c),
        RequestDrawdown c => Request(c, actor),
        ApproveDrawdown c => Approve(c, actor),
        AccrueInterest c => Accrue(c),
        RecordRepayment c => Repay(c),
        _ => Error.Validation("command.unknown", $"Unknown command {command?.GetType().Name}."),
    };

    private Result<IReadOnlyList<ILedgerEvent>> Open(OpenLoan c)
    {
        if (Exists) return Error.Conflict("loan.exists", $"Loan {c.LoanId} already exists.");
        if (string.IsNullOrWhiteSpace(c.LoanId) || c.LoanId.Length > 40) return Error.Validation("loan.id", "loanId", "Loan id is required (max 40).");
        if (string.IsNullOrWhiteSpace(c.Borrower)) return Error.Validation("loan.borrower", "borrower", "Borrower is required.");
        var limit = Money.TryOf(c.FacilityLimit, c.Currency);
        if (limit.IsFailure) return limit.Error;
        if (c.FacilityLimit <= 0) return Error.Validation("loan.limit", "facilityLimit", "Facility limit must be positive.");
        if (c.AnnualRate is < 0 or > 1) return Error.Validation("loan.rate", "annualRate", "Annual rate is a fraction between 0 and 1 (0.0725 = 7.25%).");
        return Events(new LoanOpened(c.LoanId, c.Borrower.Trim(), limit.Value.Currency, c.AnnualRate, limit.Value.Amount, c.OpenedOn));
    }

    private Result<IReadOnlyList<ILedgerEvent>> Request(RequestDrawdown c, string actor)
    {
        var check = MoneyMove(c.Amount, c.RequestedOn, "amount");
        if (check is not null) return check;
        if (string.IsNullOrWhiteSpace(c.DrawdownId)) return Error.Validation("drawdown.id", "drawdownId", "Drawdown id is required.");
        if (_drawdowns.ContainsKey(c.DrawdownId)) return Error.Conflict("drawdown.id_reused", $"Drawdown {c.DrawdownId} already exists.");
        if (Principal + PendingTotal + c.Amount.Amount > FacilityLimit)
            return Error.Validation("drawdown.over_limit", "amount", $"Drawn plus pending would exceed the facility limit of {FacilityLimit} {Currency}.");
        return Events(new DrawdownRequested(LoanId, c.DrawdownId, c.Amount.Amount, Currency, c.RequestedOn, actor));
    }

    private Result<IReadOnlyList<ILedgerEvent>> Approve(ApproveDrawdown c, string actor)
    {
        var check = Dated(c.ApprovedOn, moneyMoves: true);
        if (check is not null) return check;
        if (!_drawdowns.TryGetValue(c.DrawdownId ?? "", out var d)) return Error.NotFound("drawdown.not_found", $"Drawdown {c.DrawdownId} not found.");
        if (d.Approved) return Error.Conflict("drawdown.not_pending", $"Drawdown {d.Id} is already approved.");
        if (string.Equals(d.RequestedBy, actor, StringComparison.Ordinal))
            return Error.Conflict("drawdown.four_eyes", "The person who requested a drawdown cannot approve it.");
        return Events(new DrawdownApproved(LoanId, d.Id, d.Amount, Currency, c.ApprovedOn, actor));
    }

    private Result<IReadOnlyList<ILedgerEvent>> Accrue(AccrueInterest c)
    {
        if (!Exists) return NotFound();
        if (c.Through <= AccruedThrough) return Error.Validation("interest.nothing_to_accrue", "through", $"Interest is already accrued through {AccruedThrough:yyyy-MM-dd}.");
        var check = Dated(c.Through, moneyMoves: false);
        if (check is not null) return check;

        // Segments of constant closing principal between changes; one rounding for the whole accrual.
        decimal principalDays = 0;
        var principal = _principalAtLastAccrual;
        var cursor = AccruedThrough.AddDays(1);
        foreach (var (day, after) in _changesSinceAccrual)
        {
            principalDays += principal * DayCount.ActualDays(cursor.AddDays(-1), day.AddDays(-1));
            principal = after;
            cursor = day;
        }

        principalDays += principal * DayCount.ActualDays(cursor.AddDays(-1), c.Through);
        var interest = Money.Of(principalDays * AnnualRate / 365m, Currency, MidpointRounding.AwayFromZero);
        return Events(new InterestAccrued(LoanId, AccruedThrough, c.Through, DayCount.ActualDays(AccruedThrough, c.Through), AnnualRate,
            DayCount.Actual365, interest.Amount, Currency));
    }

    private Result<IReadOnlyList<ILedgerEvent>> Repay(RecordRepayment c)
    {
        var check = MoneyMove(c.Amount, c.ReceivedOn, "amount");
        if (check is not null) return check;
        if (string.IsNullOrWhiteSpace(c.RepaymentId)) return Error.Validation("repayment.id", "repaymentId", "Repayment id is required.");
        if (_repayments.Contains(c.RepaymentId)) return Error.Conflict("repayment.id_reused", $"Repayment {c.RepaymentId} already exists.");
        var amount = c.Amount.Amount;
        if (amount > Principal + AccruedInterest)
            return Error.Validation("repayment.exceeds_outstanding", "amount", $"Repayment exceeds the outstanding {Principal + AccruedInterest} {Currency}.");
        var toInterest = Math.Min(amount, AccruedInterest);
        return Events(new RepaymentRecorded(LoanId, c.RepaymentId, amount, toInterest, amount - toInterest, Currency, c.ReceivedOn));
    }

    private Error? MoneyMove(Money amount, DateOnly date, string field)
    {
        var dated = Dated(date, moneyMoves: true);
        if (dated is not null) return dated;
        if (amount is null || amount.Amount <= 0) return Error.Validation("money.not_positive", field, "Amount must be positive.");
        if (!string.Equals(amount.Currency, Currency, StringComparison.Ordinal))
            return Error.Validation("money.currency_mismatch", field, $"Loan {LoanId} is in {Currency}, not {amount.Currency}.");
        return null;
    }

    private Error? Dated(DateOnly date, bool moneyMoves)
    {
        if (!Exists) return NotFound();
        if (date < LastEffectiveDate)
            return Error.Validation("ledger.backdated", "date", $"Dates on a loan never go backwards; the last entry is dated {LastEffectiveDate:yyyy-MM-dd}.");
        if (moneyMoves && date <= AccruedThrough)
            return Error.Validation("ledger.period_closed", "date", $"Interest is accrued through {AccruedThrough:yyyy-MM-dd}; money can only move after that day.");
        return null;
    }

    private static Error NotFound() => Error.NotFound("loan.not_found", "Loan not found.");

    private static Result<IReadOnlyList<ILedgerEvent>> Events(params ILedgerEvent[] events) => events;

    public void Apply(ILedgerEvent @event, string? commandId, string? commandType)
    {
        switch (@event)
        {
            case LoanOpened e:
                Exists = true; LoanId = e.LoanId; Currency = e.Currency; AnnualRate = e.AnnualRate; FacilityLimit = e.FacilityLimit;
                AccruedThrough = e.OpenedOn.AddDays(-1);
                break;
            case DrawdownRequested e:
                _drawdowns[e.DrawdownId] = new Drawdown(e.DrawdownId, e.Amount, e.RequestedOn, e.RequestedBy, false);
                break;
            case DrawdownApproved e:
                _drawdowns[e.DrawdownId] = _drawdowns[e.DrawdownId] with { Approved = true };
                Principal += e.Amount;
                _changesSinceAccrual.Add((e.ApprovedOn, Principal));
                break;
            case InterestAccrued e:
                AccruedInterest += e.Amount;
                AccruedThrough = e.ThroughDate;
                _principalAtLastAccrual = Principal;
                _changesSinceAccrual.Clear();
                break;
            case RepaymentRecorded e:
                _repayments.Add(e.RepaymentId);
                AccruedInterest -= e.InterestPortion;
                Principal -= e.PrincipalPortion;
                _changesSinceAccrual.Add((e.ReceivedOn, Principal));
                break;
        }

        LastEffectiveDate = @event.EffectiveDate;
        Version++;
        if (commandId is not null) _processedCommands[commandId] = commandType ?? "";
    }
}
