namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(26000, "Worker / recurring jobs (manual runs)")]
    public static class Jobs
    {
        /// <summary>A recurring job was run by hand and succeeded (D-15).</summary>
        public const int JobRunSucceeded = 26001;

        /// <summary>No recurring job is registered with this code (404).</summary>
        public const int JobNotFound = 26002;

        /// <summary>The job ran and reported a failure.</summary>
        public const int JobRunFailed = 26003;
    }
}
