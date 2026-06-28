using System.Windows;
using System.Windows.Input;
using BannerGuardSystem.ViewModels;

namespace BannerGuardSystem;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is ShellViewModel vm)
        {
            if (e.Key == Key.Space)
            {
                if (vm.ApproveCommand.CanExecute(null))
                {
                    vm.ApproveCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (vm.DismissCommand.CanExecute(null))
                {
                    vm.DismissCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}