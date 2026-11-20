using Agentic.Blocks.Core;
using Chronos.Ledger;
using Chronos.Ledger.Contracts;
using Chronos.Ledger.Domain;
using Xunit;

namespace Chronos.Ledger.Tests;

public class LoanAggregateTests
{
    private static readonly DateOnly Opened = new(2026, 1, 5);

    private static Loan OpenLoan(decimal limit = 1_000_000m, decimal rate = 0.073m)
    {
        var loan = new Loan();
        loan.Apply(new LoanOpened("L1", "Borrower One", "GBP", rate, limit, Opened), "c0", "OpenLoan");
        return loan;
    }

    private static void Commit(Loan loan, Result<IReadOnlyList<ILedgerEvent>> decided, string commandId, string commandType)
    {
        Assert.True(decided.IsSuccess, decided.IsFailure ? decided.Error.Code : null);
        foreach (var e in decided.Value) loan.Apply(e, commandId, commandType);
    }

    [Fact]
    public void A_requested_drawdown_is_pending_and_not_in_the_principal()
    {
        var loan = OpenLoan();
        Commit(loan, loan.Decide(new RequestDrawdown("L1", "D1", Money.Of(250_000m, "GBP"), Opened.AddDays(1)), "ops-1"), "c1", "RequestDrawdown");
        Assert.Equal(0m, loan.Principal);
        Assert.Equal(250_000m, loan.PendingTotal);
    }

    [Fact]
    public void Approval_moves_the_drawdown_into_the_principal()
    {
        var loan = OpenLoan();
        Commit(loan, loan.Decide(new RequestDrawdown("L1", "D1", Money.Of(250_000m, "GBP"), Opened.AddDays(1)), "ops-1"), "c1", "RequestDrawdown");
        Commit(loan, loan.Decide(new ApproveDrawdown("L1", "D1", Opened.AddDays(2)), "fin-1"), "c2", "ApproveDrawdown");
        Assert.Equal(250_000m, loan.Principal);
        Assert.Equal(0m, loan.PendingTotal);
    }

    [Fact]
    public void The_requester_cannot_approve_their_own_drawdown()
    {
        var loan = OpenLoan();
        Commit(loan, loan.Decide(new RequestDrawdown("L1", "D1", Money.Of(10m, "GBP"), Opened), "ops-1"), "c1", "RequestDrawdown");
        var result = loan.Decide(new ApproveDrawdown("L1", "D1", Opened), "ops-1");
        Assert.Equal("drawdown.four_eyes", result.Error.Code);
    }

    [Fact]
    public void A_drawdown_over_the_facility_limit_is_rejected()
    {
        var loan = OpenLoan(limit: 100m);
        var result = loan.Decide(new RequestDrawdown("L1", "D1", Money.Of(100.01m, "GBP"), Opened), "ops-1");
        Assert.Equal("drawdown.over_limit", result.Error.Code);
    }

    [Fact]
    public void A_drawdown_in_another_currency_is_rejected()
    {
        var loan = OpenLoan();
        var result = loan.Decide(new RequestDrawdown("L1", "D1", Money.Of(10m, "EUR"), Opened), "ops-1");
        Assert.Equal("money.currency_mismatch", result.Error.Code);
    }

    [Fact]
    public void Interest_accrues_actual_365_on_each_days_closing_principal()
    {
        var loan = OpenLoan();
        // 100,000 drawn on 11 Jan; accrue through 31 Jan = 21 days (11..31 inclusive)
        Commit(loan, loan.Decide(new RequestDrawdown("L1", "D1", Money.Of(100_000m, "GBP"), new DateOnly(2026, 1, 10)), "ops-1"), "c1", "RequestDrawdown");
        Commit(loan, loan.Decide(new ApproveDrawdown("L1", "D1", new DateOnly(2026, 1, 11)), "fin-1"), "c2", "ApproveDrawdown");
        var decided = loan.Decide(new AccrueInterest("L1", new DateOnly(2026, 1, 31)), "fin-1");
        var accrued = Assert.Single(decided.Value.OfType<InterestAccrued>());
        Assert.Equal(27, accrued.Days);                 // 5..31 Jan: interest runs from the opening day
        Assert.Equal(420.00m, accrued.Amount);          // 100,000 * 7.3% * 21 / 365, nothing on the 6 undrawn days
        Assert.Equal("Actual/365", accrued.DayCount);
    }

    [Fact]
    public void A_repayment_pays_accrued_interest_first_then_principal()
    {
        var loan = OpenLoan();
        Commit(loan, loan.Decide(new RequestDrawdown("L1", "D1", Money.Of(100_000m, "GBP"), new DateOnly(2026, 1, 10)), "ops-1"), "c1", "RequestDrawdown");
        Commit(loan, loan.Decide(new ApproveDrawdown("L1", "D1", new DateOnly(2026, 1, 11)), "fin-1"), "c2", "ApproveDrawdown");
        Commit(loan, loan.Decide(new AccrueInterest("L1", new DateOnly(2026, 1, 31)), "fin-1"), "c3", "AccrueInterest");
        var decided = loan.Decide(new RecordRepayment("L1", "R1", Money.Of(1_000m, "GBP"), new DateOnly(2026, 2, 1)), "ops-1");
        var repayment = Assert.Single(decided.Value.OfType<RepaymentRecorded>());
        Assert.Equal(420.00m, repayment.InterestPortion);
        Assert.Equal(580.00m, repayment.PrincipalPortion);
    }

    [Fact]
    public void A_repayment_larger_than_the_outstanding_balance_is_rejected()
    {
        var loan = OpenLoan();
        var result = loan.Decide(new RecordRepayment("L1", "R1", Money.Of(1m, "GBP"), Opened), "ops-1");
        Assert.Equal("repayment.exceeds_outstanding", result.Error.Code);
    }

    [Fact]
    public void Money_cannot_move_inside_an_accrued_period()
    {
        var loan = OpenLoan();
        Commit(loan, loan.Decide(new AccrueInterest("L1", new DateOnly(2026, 1, 31)), "fin-1"), "c1", "AccrueInterest");
        var result = loan.Decide(new RequestDrawdown("L1", "D1", Money.Of(10m, "GBP"), new DateOnly(2026, 1, 31)), "ops-1");
        Assert.Equal("ledger.period_closed", result.Error.Code);
    }

    [Fact]
    public void Business_dates_in_a_stream_never_go_backwards()
    {
        var loan = OpenLoan();
        Commit(loan, loan.Decide(new RequestDrawdown("L1", "D1", Money.Of(10m, "GBP"), new DateOnly(2026, 2, 1)), "ops-1"), "c1", "RequestDrawdown");
        var result = loan.Decide(new RequestDrawdown("L1", "D2", Money.Of(10m, "GBP"), new DateOnly(2026, 1, 20)), "ops-1");
        Assert.Equal("ledger.backdated", result.Error.Code);
    }

    [Fact]
    public void Replaying_a_command_id_is_recognised_as_a_duplicate()
    {
        var loan = OpenLoan();
        Commit(loan, loan.Decide(new RequestDrawdown("L1", "D1", Money.Of(10m, "GBP"), Opened), "ops-1"), "key-1", "RequestDrawdown");
        Assert.True(loan.HasProcessed("key-1", "RequestDrawdown"));
        Assert.False(loan.HasProcessed("key-2", "RequestDrawdown"));
    }
}
