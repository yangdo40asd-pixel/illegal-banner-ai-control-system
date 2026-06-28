using System;
using System.ComponentModel;
using System.Windows.Controls;
using BannerGuardSystem.Helpers;
using BannerGuardSystem.Services;
using BannerGuardSystem.ViewModels;

namespace BannerGuardSystem.Views;

public partial class RealTimeMonitoringView : UserControl
{
    private RealTimeMonitoringViewModel? _vm;

    public RealTimeMonitoringView(RealTimeMonitoringViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += Vm_PropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_vm != null)
            _vm.PropertyChanged -= Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;
        if (e.PropertyName is nameof(RealTimeMonitoringViewModel.MapQuery)
            or nameof(RealTimeMonitoringViewModel.ActiveAlert))
        {
            var query = _vm.ActiveAlert != null
                ? LocationMapHelper.ResolveMapQuery(_vm.ActiveAlert)
                : _vm.MapQuery;
            UpdateMap(query);
        }
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            await MapWebView.EnsureCoreWebView2Async(null);
            if (_vm != null)
            {
                var query = _vm.ActiveAlert != null
                    ? LocationMapHelper.ResolveMapQuery(_vm.ActiveAlert)
                    : _vm.MapQuery;
                UpdateMap(query);
            }
        }
        catch (Exception ex)
        {
            BackendManager.Log($"[VLM-Map] OnLoaded failed: {ex.Message}");
        }
    }

    private async void UpdateMap(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            await MapWebView.EnsureCoreWebView2Async(null);
            var encoded = Uri.EscapeDataString(query);
            // Google Maps embed는 iframe 안에서만 허용됨 (직접 Navigate 시 오류)
            var cacheBuster = DateTime.UtcNow.Ticks;
            var html =
                "<!DOCTYPE html><html><head><meta charset='utf-8'>" +
                "<style>body,html{margin:0;padding:0;height:100%;overflow:hidden}" +
                "iframe{width:100%;height:100%;border:none}</style></head><body>" +
                $"<iframe src='https://maps.google.com/maps?q={encoded}&amp;t=&amp;z=15&amp;ie=UTF8&amp;iwloc=&amp;output=embed&amp;_={cacheBuster}' allowfullscreen loading='lazy'></iframe>" +
                "</body></html>";
            MapWebView.NavigateToString(html);
            BackendManager.Log($"[VLM-Map] iframe embed: {query}");
        }
        catch (Exception ex)
        {
            BackendManager.Log($"[VLM-Map] UpdateMap failed: {ex.Message}");
        }
    }
}