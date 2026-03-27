using System.Collections.ObjectModel;

namespace VmManager.ViewModels;

public class VmGroup
{
    public string Name { get; set; } = "";
    public bool IsExpanded { get; set; } = true;
    public ObservableCollection<VmInstanceViewModel> Items { get; set; } =
        new ObservableCollection<VmInstanceViewModel>();
}
