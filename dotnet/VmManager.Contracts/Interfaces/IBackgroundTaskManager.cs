using System.Collections.ObjectModel;
using VmManager.Contracts.Models;

namespace VmManager.Contracts.Interfaces;

public interface IBackgroundTaskManager
{
    ReadOnlyObservableCollection<IBackgroundTask> Tasks { get; }
    int ActiveCount { get; }
    IBackgroundTask StartTask(
        string title,
        Func<BackgroundTaskContext, Task> work,
        bool isCancellable = true
    );
    IBackgroundTask StartTask(
        string title,
        string username,
        Func<BackgroundTaskContext, Task> work,
        bool isCancellable = true
    );
    IEnumerable<IBackgroundTask> GetTasksForUser(string username);
    void RemoveTask(string taskId);
    event Action? TasksChanged;
}
