using System.Windows.Controls;

namespace BannerGuardSystem.Views;

public partial class CctvMonitoringView : UserControl
{
    public CctvMonitoringView(BannerGuardSystem.ViewModels.CctvMonitoringViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            viewModel.SyncChannelMetadata();
            viewModel.RequestCctvAnalysisRefresh();
        };
    }
}