using System.Diagnostics.Metrics;
using Agentic.Blocks.Core;
using Chronos.EventStore;
using Chronos.Ledger.Domain;
using Microsoft.Extensions.Options;

namespace Chronos.Ledger;

/// <summary>
/// Load, decide, append. The loan is rebuilt from its stream for every command; the append states the version
/// it decided on, so a concurrent writer causes <see cref="WrongExpectedVersionException"/> and the whole
/// load-decide-append runs again on fresh state. A command id already in the stream is a duplicate: no new event.
/// </summary>
internal sealed class LedgerCommandHandler(IEventStore store, TimeProvider time, IOptions<LedgerOptions> options) : ILedgerCommands
{
    private static readonly Meter Meter = new("Chronos.Ledger");
    private static readonly Counter<long> Conflicts = Meter.CreateCounter<long>("chronos.ledger.concurrency_conflicts");

    public async Task<Result<CommandOutcome>> SendAsync(ILedgerCommand command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(command.LoanId)) return Error.Validation("loan.id", "loanId", "Loan id is required.");
        var commandType = command.GetType().Name;
        var stream = Streams.ForLoan(command.LoanId);
        var maxAttempts = Math.Max(1, options.Value.MaxAttempts);

        for (var attempt = 1; ; attempt++)
        {
            var loan = new Loan();
            foreach (var stored in await store.ReadStreamAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                var envelope = LedgerEventSerializer.Deserialize(stored);
                loan.Apply(envelope.Event, envelope.Metadata.CommandId, envelope.Metadata.CommandType);
            }

            var seen = loan.ProcessedCommandType(context.CommandId);
            if (seen is not null)
            {
                return seen == commandType
                    ? new CommandOutcome(command.LoanId, loan.Version, 0, true, attempt)
                    : Error.Conflict("command.id_reused", "This idempotency key was already used for a different command.");
            }

            var decided = loan.Decide(command, context.Actor);
            if (decided.IsFailure) return decided.Error;

            var now = time.GetUtcNow();
            var data = decided.Value
                .Select(e => LedgerEventSerializer.Serialize(e, new EventMetadata(context.CommandId, context.Actor, now, e.EffectiveDate, commandType)))
                .ToArray();
            try
            {
                var version = await store.AppendAsync(stream, loan.Version, data, cancellationToken).ConfigureAwait(false);
                return new CommandOutcome(command.LoanId, version, data.Length, false, attempt);
            }
            catch (WrongExpectedVersionException)
            {
                Conflicts.Add(1);
                if (attempt >= maxAttempts)
                    return Error.Conflict("ledger.concurrency_conflict", "The loan kept changing while the command ran. Retry with the same Idempotency-Key.");
                // Back off a little, with jitter, so racing writers spread out.
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1, 4 * attempt)), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
