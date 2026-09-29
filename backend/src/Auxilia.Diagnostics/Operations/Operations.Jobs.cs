namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Jobs
    {
        public static readonly OperationDescriptor RunJob = new("Jobs.RunJob", EventCodes.Jobs.JobRunSucceeded);
    }
}
