using System.Windows.Controls;

namespace BannerGuardSystem.Views
{
    public partial class RealTimeMonitoringView : UserControl
    {
        public RealTimeMonitoringView()
        {
            InitializeComponent();
            this.Loaded += RealTimeMonitoringView_Loaded;
            this.DataContextChanged += RealTimeMonitoringView_DataContextChanged;
        }

        private void RealTimeMonitoringView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ViewModels.MainViewModel oldVm)
            {
                oldVm.PropertyChanged -= Vm_PropertyChanged;
            }
            if (e.NewValue is ViewModels.MainViewModel newVm)
            {
                newVm.PropertyChanged += Vm_PropertyChanged;
                UpdateMap(newVm.MapQuery);
            }
        }

        private void Vm_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "MapQuery")
            {
                var vm = DataContext as ViewModels.MainViewModel;
                UpdateMap(vm?.MapQuery);
            }
        }

        private async void UpdateMap(string query)
        {
            if (string.IsNullOrEmpty(query)) return;
            try
            {
                await MapWebView.EnsureCoreWebView2Async(null);
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

        private async void RealTimeMonitoringView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                // Explicitly ensure WebView2 is initialized before navigating
                await MapWebView.EnsureCoreWebView2Async(null);
                
                var vm = DataContext as ViewModels.MainViewModel;
                if (vm != null && !string.IsNullOrEmpty(vm.MapQuery))
                {
                    UpdateMap(vm.MapQuery);
                }
            }
            catch { }
        }
    }
}
