namespace VmManager.Contracts.Models;

public sealed class BackgroundTaskContext
{
    private readonly Action<double, string> _reportProgress;
    private readonly Action<string> _log;
    private readonly Action<string> _logError;

    public CancellationToken Token { get; }

    public BackgroundTaskContext(
        CancellationToken token,
        Action<double, string> reportProgress,
        Action<string> log,
        Action<string> logError
    )
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(logError);
        Token = token;
        _reportProgress = reportProgress;
        _log = log;
        _logError = logError;
    }

    public void ReportProgress(double percent, string status) => _reportProgress(percent, status);

    public void Log(string message) => _log(message);

    public void LogError(string message) => _logError(message);
}
