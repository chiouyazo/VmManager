using Microsoft.AspNetCore.SignalR;

namespace VmManager.Agent.Hubs;

public class ProgressHub : Hub
{
    public async Task JoinTask(string taskId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, taskId);
    }

    public async Task LeaveTask(string taskId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, taskId);
    }
}
