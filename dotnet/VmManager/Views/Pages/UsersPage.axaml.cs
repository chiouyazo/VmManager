using Avalonia.Controls;
using Avalonia.Interactivity;
using VmManager.Contracts.Models;
using VmManager.Services;
using VmManager.ViewModels;

namespace VmManager.Views.Pages;

public partial class UsersPage : UserControl
{
    public UsersPage(UsersViewModel viewModel, NotificationService notificationService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        viewModel.Notifications = notificationService;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void UserItem_Click(object? sender, RoutedEventArgs e)
    {
        if (
            sender is Button button
            && button.Tag is AuthenticatedUser user
            && DataContext is UsersViewModel vm
        )
        {
            vm.SelectUser(user);
        }
    }

    private void DeleteUser_Click(object? sender, RoutedEventArgs e)
    {
        if (
            sender is Button button
            && button.Tag is AuthenticatedUser user
            && DataContext is UsersViewModel vm
        )
        {
            vm.SelectedUser = user;
            vm.DeleteUserCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void ResetPassword_Click(object? sender, RoutedEventArgs e)
    {
        if (
            sender is Button button
            && button.Tag is AuthenticatedUser user
            && DataContext is UsersViewModel vm
        )
        {
            vm.SelectUser(user);
            vm.ToggleResetPasswordCommand.Execute(null);
        }
        e.Handled = true;
    }
}
