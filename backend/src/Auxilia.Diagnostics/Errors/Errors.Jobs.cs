using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Jobs
    {
        public static Error JobNotFound(string code) =>
            Error.NotFound(EventCodes.Jobs.JobNotFound, $"No recurring job {code}");

        public static Error JobRunFailed(string code) =>
            Error.Failure(EventCodes.Jobs.JobRunFailed, $"The job {code} failed");

        public static Error JobAlreadyRunning(string code) =>
            Error.Conflict(EventCodes.Jobs.JobAlreadyRunning, $"The job {code} is already running");
    }
}
