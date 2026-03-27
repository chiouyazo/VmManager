using Avalonia.Controls;
using Avalonia.Interactivity;
using VmManager.Services;

namespace VmManager.Views.Controls;

public partial class TaskPanel : UserControl
{
    private readonly BackgroundTaskManager _taskManager;

    public TaskPanel(BackgroundTaskManager taskManager)
    {
        ArgumentNullException.ThrowIfNull(taskManager);
        _taskManager = taskManager;
        InitializeComponent();
        TaskList.ItemsSource = _taskManager.Tasks;
    }

    private void ClearCompleted_Click(object? sender, RoutedEventArgs e)
    {
        List<IBackgroundTask> toRemove = new List<IBackgroundTask>();
        foreach (IBackgroundTask task in _taskManager.Tasks)
        {
            if (task.IsComplete || task.IsFailed || task.IsCancelled)
                toRemove.Add(task);
        }

        foreach (IBackgroundTask task in toRemove)
        {
            _taskManager.RemoveTask(task);
        }
    }
}
