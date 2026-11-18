using System.Windows.Controls;

namespace BannerGuardSystem.Views;

public partial class StatisticsReportView : UserControl
{
    public StatisticsReportView(BannerGuardSystem.ViewModels.StatisticsReportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}