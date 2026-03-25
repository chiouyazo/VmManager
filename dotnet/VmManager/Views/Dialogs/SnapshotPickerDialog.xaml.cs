using System.Windows;
using System.Windows.Input;

namespace VmManager.Views.Dialogs;

public partial class SnapshotPickerDialog : Window
{
    public int SelectedIndex => SnapshotList.SelectedIndex;

    public SnapshotPickerDialog(List<string> items)
    {
        InitializeComponent();
        foreach (var item in items)
            SnapshotList.Items.Add(item);
        if (items.Count > 0)
            SnapshotList.SelectedIndex = 0;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (SnapshotList.SelectedIndex < 0)
            return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SnapshotList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SnapshotList.SelectedIndex >= 0)
            DialogResult = true;
    }
}
