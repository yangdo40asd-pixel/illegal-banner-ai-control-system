using System.Windows.Controls;
using BannerGuardSystem.ViewModels;

namespace BannerGuardSystem.Views
{
    public partial class RealTimeMonitoringView : UserControl
    {
        public RealTimeMonitoringView(RealTimeMonitoringViewModel viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
            this.Loaded += RealTimeMonitoringView_Loaded;
        }



        private async void RealTimeMonitoringView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                await MapWebView.EnsureCoreWebView2Async(null);
                // Assuming viewModel has a MapQuery property; otherwise, map initialization can be added later.
                if (DataContext is RealTimeMonitoringViewModel vm && !string.IsNullOrEmpty(vm.MapQuery))
                {
                    UpdateMap(vm.MapQuery);
                }
            }
            catch { }
        }

        private async void UpdateMap(string query)
        {
            if (string.IsNullOrEmpty(query)) return;
            try
            {
                string html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body, html {{ margin: 0; padding: 0; height: 100%; overflow: hidden; }}
        iframe {{ width: 100%; height: 100%; border: none; }}
    </style>
</head>
<body>
    <iframe src='https://maps.google.com/maps?q={System.Uri.EscapeDataString(query)}&amp;t=&amp;z=15&amp;ie=UTF8&amp;iwloc=&amp;output=embed' allowfullscreen></iframe>
</body>
</html>";
                MapWebView.NavigateToString(html);
            }
            catch { }
        }
    }
}
