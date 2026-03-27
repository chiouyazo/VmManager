namespace VmManager.Contracts.Interfaces;

public interface IBackgroundTask
{
    string Id { get; }
    string Title { get; }
    string Status { get; }
    double Progress { get; } // 0.0 - 1.0, -1 for indeterminate
    bool IsComplete { get; }
    bool IsFailed { get; }
    bool IsCancelled { get; }
    bool IsCancellable { get; }
    string? ErrorMessage { get; }
    IReadOnlyList<string> LogLines { get; }
    void Cancel();
}
