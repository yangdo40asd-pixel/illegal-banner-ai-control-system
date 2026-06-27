using System.Windows.Controls;
using BannerGuardSystem.ViewModels;

namespace BannerGuardSystem.Views;

public partial class SystemSettingsView : UserControl
{
    public SystemSettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SystemSettingsViewModel oldVm)
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        if (e.NewValue is SystemSettingsViewModel newVm)
        {
            newVm.PropertyChanged += Vm_PropertyChanged;
            UpdateMap(newVm.MapQuery);
        }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemSettingsViewModel.MapQuery)
            && DataContext is SystemSettingsViewModel vm)
            UpdateMap(vm.MapQuery);
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            await MapWebView.EnsureCoreWebView2Async(null);
            if (DataContext is SystemSettingsViewModel vm)
                UpdateMap(vm.MapQuery);
        }
        catch { }
    }

    private async void UpdateMap(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            await MapWebView.EnsureCoreWebView2Async(null);
            var encoded = Uri.EscapeDataString(query);
            var html = "<!DOCTYPE html><html><head><meta charset='utf-8'>" +
                "<style>body,html{margin:0;padding:0;height:100%}iframe{width:100%;height:100%;border:none}</style></head><body>" +
                $"<iframe src='https://maps.google.com/maps?q={encoded}&amp;z=13&amp;output=embed'></iframe></body></html>";
            MapWebView.NavigateToString(html);
        }
        catch { }
    }
}