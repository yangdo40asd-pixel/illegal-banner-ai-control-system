using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BannerGuardSystem.ViewModels;

namespace BannerGuardSystem.Views;

public partial class EnforcementStatusView : UserControl
{
    public EnforcementStatusView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is EnforcementStatusViewModel oldVm)
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        if (e.NewValue is EnforcementStatusViewModel newVm)
        {
            newVm.PropertyChanged += Vm_PropertyChanged;
            UpdateMap(newVm.MapQuery);
        }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EnforcementStatusViewModel.MapQuery)
            && DataContext is EnforcementStatusViewModel vm)
            UpdateMap(vm.MapQuery);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await MapWebView.EnsureCoreWebView2Async(null);
            if (DataContext is EnforcementStatusViewModel vm)
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
                "<style>body,html{margin:0;padding:0;height:100%;overflow:hidden}" +
                "iframe{width:100%;height:100%;border:none}</style></head><body>" +
                $"<iframe src='https://maps.google.com/maps?q={encoded}&amp;t=&amp;z=15&amp;ie=UTF8&amp;iwloc=&amp;output=embed' allowfullscreen></iframe>" +
                "</body></html>";
            MapWebView.NavigateToString(html);
        }
        catch { }
    }

    private void Overlay_BackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is EnforcementStatusViewModel vm)
            vm.IsDialogOpen = false;
    }
}