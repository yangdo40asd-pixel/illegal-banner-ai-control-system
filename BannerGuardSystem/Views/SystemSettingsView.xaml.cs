using System.Windows.Controls;

namespace BannerGuardSystem.Views;

public partial class SystemSettingsView : UserControl
{
    public SystemSettingsView(BannerGuardSystem.ViewModels.SystemSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}