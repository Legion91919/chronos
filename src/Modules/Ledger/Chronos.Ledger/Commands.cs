using Agentic.Blocks.Core;

namespace Chronos.Ledger;

/// <summary>A request to change one loan. Commands may be refused; events may not.</summary>
public interface ILedgerCommand
{
    /// <summary>The target loan.</summary>
    string LoanId { get; }
}

/// <summary>Open a facility.</summary>
public sealed record OpenLoan(string LoanId, string Borrower, string Currency, decimal AnnualRate, decimal FacilityLimit, DateOnly OpenedOn) : ILedgerCommand;

/// <summary>Ask to draw funds; it waits for a second person's approval.</summary>
public sealed record RequestDrawdown(string LoanId, string DrawdownId, Money Amount, DateOnly RequestedOn) : ILedgerCommand;

/// <summary>Sign off a pending drawdown.</summary>
public sealed record ApproveDrawdown(string LoanId, string DrawdownId, DateOnly ApprovedOn) : ILedgerCommand;

/// <summary>Accrue interest (Actual/365) up to and including <paramref name="Through"/>.</summary>
public sealed record AccrueInterest(string LoanId, DateOnly Through) : ILedgerCommand;

/// <summary>Record money received from the borrower.</summary>
public sealed record RecordRepayment(string LoanId, string RepaymentId, Money Amount, DateOnly ReceivedOn) : ILedgerCommand;

/// <summary>Who sends the command and its idempotency key.</summary>
public sealed record CommandContext(string CommandId, string Actor);

/// <summary>What happened: the stream version after the command, how many events it appended, and how many tries it took.</summary>
public sealed record CommandOutcome(string LoanId, long Version, int EventsAppended, bool Duplicate, int Attempts);

/// <summary>The write side. Every command is idempotent on <see cref="CommandContext.CommandId"/>.</summary>
public interface ILedgerCommands
{
    /// <summary>Loads the loan from its events, decides, and appends with optimistic concurrency (retrying conflicts).</summary>
    Task<Result<CommandOutcome>> SendAsync(ILedgerCommand command, CommandContext context, CancellationToken cancellationToken = default);
}

/// <summary>Options for the write side.</summary>
public sealed class LedgerOptions
{
    /// <summary>How many times a command is tried when other writers keep winning the race. Default 10.</summary>
    public int MaxAttempts { get; set; } = 10;
}

/// <summary>Stream naming.</summary>
public static class Streams
{
    /// <summary>Stream prefix for loans.</summary>
    public const string LoanPrefix = "loan-";

    /// <summary>The stream holding one loan's events.</summary>
    public static string ForLoan(string loanId) => LoanPrefix + loanId;
}
