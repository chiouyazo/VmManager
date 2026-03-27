using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.AspNetCore.SignalR;
using VmManager.Agent.Hubs;

namespace VmManager.Agent.Services;

public class BackgroundTaskManager : IBackgroundTaskManager
{
    private readonly ConcurrentDictionary<string, AgentBackgroundTask> _tasks =
        new ConcurrentDictionary<string, AgentBackgroundTask>();
    private readonly ObservableCollection<IBackgroundTask> _observableTasks =
        new ObservableCollection<IBackgroundTask>();
    private readonly ILogger<BackgroundTaskManager> _logger;
    private readonly IHubContext<ProgressHub>? _hubContext;

    public ReadOnlyObservableCollection<IBackgroundTask> Tasks { get; }
    public int ActiveCount =>
        _tasks.Values.Count(t => !t.IsComplete && !t.IsFailed && !t.IsCancelled);
    public event Action? TasksChanged;

    public BackgroundTaskManager(
        ILogger<BackgroundTaskManager> logger,
        IHubContext<ProgressHub>? hubContext = null
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _hubContext = hubContext;
        Tasks = new ReadOnlyObservableCollection<IBackgroundTask>(_observableTasks);
    }

    public IBackgroundTask StartTask(
        string title,
        Func<BackgroundTaskContext, Task> work,
        bool isCancellable = true
    )
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(work);

        CancellationTokenSource cts = new CancellationTokenSource();
        AgentBackgroundTask task = new AgentBackgroundTask(title, cts, isCancellable);

        _tasks[task.Id] = task;
        _observableTasks.Add(task);
        TasksChanged?.Invoke();

        BackgroundTaskContext ctx = new BackgroundTaskContext(
            cts.Token,
            (pct, status) =>
            {
                task.Progress = pct;
                task.Status = status;
                BroadcastProgress(task.Id, pct, status);
            },
            msg => task.AddLog(msg),
            msg => task.AddLog("ERROR: " + msg)
        );

        _ = RunTaskAsync(task, ctx, work, cts);
        return task;
    }

    public IEnumerable<IBackgroundTask> GetAllTasks() => _tasks.Values;

    private async Task RunTaskAsync(
        AgentBackgroundTask task,
        BackgroundTaskContext ctx,
        Func<BackgroundTaskContext, Task> work,
        CancellationTokenSource cts
    )
    {
        try
        {
            await work(ctx);
            task.IsComplete = true;
            task.Progress = 1.0;
            task.Status = "Complete";
            BroadcastCompleted(task.Id, true, null);
        }
        catch (OperationCanceledException)
        {
            task.IsCancelled = true;
            task.Status = "Cancelled";
            BroadcastCompleted(task.Id, false, "Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background task {Title} failed", task.Title);
            task.IsFailed = true;
            task.ErrorMessage = ex.Message;
            task.Status = "Failed";
            BroadcastCompleted(task.Id, false, ex.Message);
        }
        finally
        {
            cts.Dispose();
            TasksChanged?.Invoke();
        }
    }

    private void BroadcastProgress(string taskId, double progress, string status)
    {
        _hubContext?.Clients.All.SendAsync("TaskProgress", taskId, progress, status);
    }

    private void BroadcastCompleted(string taskId, bool success, string? error)
    {
        _hubContext?.Clients.All.SendAsync("TaskCompleted", taskId, success, error);
    }

    private sealed class AgentBackgroundTask : IBackgroundTask
    {
        private readonly CancellationTokenSource _cts;
        private readonly List<string> _logLines = new List<string>();

        public string Id { get; }
        public string Title { get; set; }
        public string Status { get; set; } = "";
        public double Progress { get; set; } = -1;
        public bool IsComplete { get; set; }
        public bool IsFailed { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsCancellable { get; set; }
        public string? ErrorMessage { get; set; }
        public IReadOnlyList<string> LogLines => _logLines;

        public AgentBackgroundTask(string title, CancellationTokenSource cts, bool isCancellable)
        {
            Id = Guid.NewGuid().ToString("N");
            Title = title;
            _cts = cts;
            IsCancellable = isCancellable;
        }

        public void Cancel()
        {
            if (IsCancellable && !IsComplete && !IsFailed && !IsCancelled)
                _cts.Cancel();
        }

        public void AddLog(string message)
        {
            _logLines.Add(message);
        }
    }
}
