namespace Auxilia.Diagnostics;

/// <summary>
/// Marks an exception whose outcome log was already written (by <c>IOperationRunner</c>), so the global handler and
/// Worker error handling do not log it a second time (ADR 0012: exactly one log per failure).
/// </summary>
public static class ExceptionLogging
{
    private const string LoggedKey = "Auxilia.Logged";

    public static void MarkLogged(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.Data[LoggedKey] = true;
    }

    public static bool IsLogged(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[LoggedKey] is true;
    }
}
