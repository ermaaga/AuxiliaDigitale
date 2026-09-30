using Auxilia.Application.Bus;
using Auxilia.Application.Jobs;
using Auxilia.Contracts.Messages.V1.Platform;
using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Messaging;
using Auxilia.SharedKernel.Results;

using Rebus.Pipeline;

namespace Auxilia.Worker.Handlers.Platform;

/// <summary>
/// Manual job run requested by the System (D-15): runs the job for the tenant of the message. A job made of many
/// operations is not one transaction; jobs are idempotent. A run refused because another one holds the lock is not
/// an error (the job is running).
/// </summary>
internal sealed class RunRecurringJobHandler(IIncomingMessageProcessor processor, IMessageContext context, IJobRunner jobs)
    : MessageHandler<RunRecurringJobCommand>(processor, context)
{
    protected override bool Transactional => false;

    protected override async Task<Result> HandleAsync(RunRecurringJobCommand message, CancellationToken cancellationToken)
    {
        var result = await jobs.RunAsync(message.JobCode, cancellationToken);
        return result.IsSuccess || result.Error!.Code == EventCodes.Jobs.JobAlreadyRunning
            ? Result.Success()
            : result.Error.Code == EventCodes.Jobs.JobRunFailed
                ? throw new InvalidOperationException($"Job {message.JobCode} failed ({result.Error.DisplayCode}); retried.")
                : Result.Failure(result.Error);
    }
}
