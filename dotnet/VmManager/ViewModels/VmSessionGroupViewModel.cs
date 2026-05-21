using CommunityToolkit.Mvvm.ComponentModel;
using VmManager.Contracts.Models;

namespace VmManager.ViewModels;

public partial class VmSessionGroupViewModel : ObservableObject
{
    public string VmName { get; }
    public string VmState { get; }
    public List<ActiveSession> Sessions { get; }

    public VmSessionGroupViewModel(VmSessionGroup group)
    {
        VmName = group.VmName;
        VmState = group.VmState;
        Sessions = group.Sessions;
    }
}
