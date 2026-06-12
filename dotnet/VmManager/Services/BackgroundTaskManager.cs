using System.Collections.ObjectModel;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VmManager.ViewModels;

namespace VmManager.Services;

public class BackgroundTaskManager : IBackgroundTaskManager
{
    private readonly ObservableCollection<IBackgroundTask> _tasks =
        new ObservableCollection<IBackgroundTask>();
    private readonly CancellationToken _appShutdownToken;
    private readonly ILogger<BackgroundTaskManager> _logger;

    public ReadOnlyObservableCollection<IBackgroundTask> Tasks { get; }
    public int ActiveCount => _tasks.Count(t => !t.IsComplete && !t.IsFailed && !t.IsCancelled);

    public event Action? TasksChanged;

    public BackgroundTaskManager(ILogger<BackgroundTaskManager> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _appShutdownToken = App.ShutdownToken;
        Tasks = new ReadOnlyObservableCollection<IBackgroundTask>(_tasks);
    }

    public IBackgroundTask StartTask(
        string title,
        Func<BackgroundTaskContext, Task> work,
        bool isCancellable = true
    )
    {
        return StartTask(title, "", work, isCancellable);
    }

    public IBackgroundTask StartTask(
        string title,
        string username,
        Func<BackgroundTaskContext, Task> work,
        bool isCancellable = true
    )
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(work);

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            _appShutdownToken
        );
        BackgroundTaskViewModel task = new BackgroundTaskViewModel(
            title,
            cts,
            isCancellable,
            username
        );

        DispatchToUi(() => _tasks.Add(task));
        RaiseTasksChanged();

        BackgroundTaskContext ctx = new BackgroundTaskContext(
            cts.Token,
            (pct, status) => DispatchToUi(() => task.SetProgress(pct, status)),
            msg => DispatchToUi(() => task.AddLog(msg)),
            msg => DispatchToUi(() => task.AddLog($"ERROR: {msg}"))
        );

        _ = RunTaskAsync(task, ctx, work, cts);
        return task;
    }

    private async Task RunTaskAsync(
        BackgroundTaskViewModel task,
        BackgroundTaskContext ctx,
        Func<BackgroundTaskContext, Task> work,
        CancellationTokenSource cts
    )
    {
        try
        {
            await work(ctx);
            DispatchToUi(() => task.SetComplete());
        }
        catch (OperationCanceledException)
        {
            DispatchToUi(() => task.SetCancelled());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background task {Title} failed", task.Title);
            DispatchToUi(() => task.SetFailed(ex.Message));
        }
        finally
        {
            cts.Dispose();
            RaiseTasksChanged();
        }
    }

    private void RaiseTasksChanged()
    {
        TasksChanged?.Invoke();
    }

    public IEnumerable<IBackgroundTask> GetTasksForUser(string username)
    {
        return _tasks.Where(t =>
            string.Equals(t.Username, username, StringComparison.OrdinalIgnoreCase)
        );
    }

    public void RemoveTask(IBackgroundTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        DispatchToUi(() => _tasks.Remove(task));
        RaiseTasksChanged();
    }

    public void RemoveTask(string taskId)
    {
        IBackgroundTask? task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task != null)
            RemoveTask(task);
    }

    private static void DispatchToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
