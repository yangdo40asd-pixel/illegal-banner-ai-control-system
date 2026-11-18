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
            // 텍스트 입력 컨트롤에 포커스가 있을 때는 단축키를 가로채지 않음
            if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase)
                return;

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
            else if (e.Key is Key.D1 or Key.NumPad1)
            {
                vm.NavigateCommand.Execute("RealTimeMonitoring");
                e.Handled = true;
            }
            else if (e.Key is Key.D2 or Key.NumPad2)
            {
                vm.NavigateCommand.Execute("CctvMonitoring");
                e.Handled = true;
            }
            else if (e.Key is Key.D3 or Key.NumPad3)
            {
                vm.NavigateCommand.Execute("EnforcementList");
                e.Handled = true;
            }
            else if (e.Key is Key.D4 or Key.NumPad4)
            {
                vm.NavigateCommand.Execute("Statistics");
                e.Handled = true;
            }
            else if (e.Key is Key.D5 or Key.NumPad5)
            {
                vm.NavigateCommand.Execute("Settings");
                e.Handled = true;
            }
        }
    }
}