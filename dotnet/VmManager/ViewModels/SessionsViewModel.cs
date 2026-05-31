using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VmManager.Contracts.Models;
using VmManager.Services;

namespace VmManager.ViewModels;

public partial class SessionsViewModel : ViewModelBase
{
    private readonly ILogger<SessionsViewModel> _logger;
    private readonly DispatcherTimer _refreshTimer;

    private AgentClient _agentClient => App.AgentClient!;

    public SessionsViewModel(ILogger<SessionsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<VmSessionGroupViewModel> Groups { get; } =
        new ObservableCollection<VmSessionGroupViewModel>();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _sessionCount;

    [ObservableProperty]
    private int _vmCount;

    [ObservableProperty]
    private string _summaryText = "";

    public void StartPolling()
    {
        _refreshTimer.Start();
    }

    public void StopPolling()
    {
        _refreshTimer.Stop();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (App.AgentClient == null)
            return;

        try
        {
            IsLoading = Groups.Count == 0;

            List<VmSessionGroup> groups = await _agentClient.GetActiveSessionsAsync();

            Groups.Clear();
            int totalSessions = 0;
            foreach (VmSessionGroup group in groups)
            {
                VmSessionGroupViewModel groupVm = new VmSessionGroupViewModel(group);
                Groups.Add(groupVm);
                totalSessions += group.Sessions.Count;
            }

            SessionCount = totalSessions;
            VmCount = groups.Count;
            SummaryText =
                totalSessions == 0
                    ? "No active sessions"
                    : $"{totalSessions} session{(totalSessions == 1 ? "" : "s")} on {groups.Count} VM{(groups.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh sessions");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync(ActiveSession session)
    {
        try
        {
            await _agentClient.DisconnectSessionAsync(session.VmName, session.Token);
            ShowSuccess("Session disconnected");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError("Failed to disconnect: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task ShadowAsync(ActiveSession session)
    {
        await LaunchShadowForVmAsync(session.VmName, false);
    }

    [RelayCommand]
    public async Task ShadowForceAsync(ActiveSession session)
    {
        await LaunchShadowForVmAsync(session.VmName, true);
    }

    private async Task LaunchShadowForVmAsync(string vmName, bool noConsent)
    {
        try
        {
            RdpShadowSessionsResponse response = await _agentClient.GetShadowSessionsAsync(vmName);
            if (response.Sessions.Count == 0)
            {
                ShowError("No active Windows sessions found on " + vmName);
                return;
            }

            RdpShadowSession target = response.Sessions[0];
            _agentClient.LaunchShadowSession(vmName, target.SessionId, noConsent);
        }
        catch (Exception ex)
        {
            ShowError("Failed to shadow: " + ex.Message);
        }
    }
}
