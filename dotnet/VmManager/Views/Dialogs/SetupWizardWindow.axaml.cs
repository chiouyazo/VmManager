using Avalonia.Controls;
using VmManager.ViewModels;

namespace VmManager.Views.Dialogs;

public partial class SetupWizardWindow : Window
{
    private readonly SetupWizardViewModel _viewModel;

    public AppSettings Result => _viewModel.Result ?? new AppSettings();

    public SetupWizardWindow()
    {
        _viewModel = new SetupWizardViewModel();
        DataContext = _viewModel;

        _viewModel.RequestClose += () =>
        {
            Close(true);
        };

        InitializeComponent();
    }
}
