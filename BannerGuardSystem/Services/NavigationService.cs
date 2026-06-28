using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using BannerGuardSystem.ViewModels;
using BannerGuardSystem.Views;

namespace BannerGuardSystem.Services
{
    /// <summary>
    /// Implements view navigation for the MVVM architecture.
    /// </summary>
    public class NavigationService : INavigationService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, Type> _viewMap;
        private readonly Dictionary<string, object> _viewCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<string> _navigationStack = new();

        public event Action<object>? Navigated;

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            _viewMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
            {
                { "RealTimeMonitoring", typeof(RealTimeMonitoringView) },
                { "CctvMonitoring", typeof(CctvMonitoringView) },
                { "EnforcementList", typeof(EnforcementStatusView) },
                { "Settings", typeof(SystemSettingsView) },
                { "Statistics", typeof(StatisticsReportView) }
            };
        }

        public void Navigate(string viewKey)
        {
            if (string.IsNullOrWhiteSpace(viewKey))
                throw new ArgumentException("View key cannot be null or empty.", nameof(viewKey));

            if (!_viewMap.TryGetValue(viewKey, out var viewType))
                throw new InvalidOperationException($"No view registered for key '{viewKey}'.");

            if (!_viewCache.TryGetValue(viewKey, out var viewInstance))
            {
                // Resolve the view instance via DI (activator utilities will also resolve its DataContext).
                viewInstance = ActivatorUtilities.CreateInstance(_serviceProvider, viewType);
                if (viewInstance == null)
                    throw new InvalidOperationException($"Failed to create view instance for '{viewKey}'.");
                
                _viewCache[viewKey] = viewInstance;
            }

            // Add the new view key to history.
            _navigationStack.Push(viewKey);

            // Notify listeners that navigation occurred.
            Navigated?.Invoke(viewInstance);
        }

        public void GoBack()
        {
            if (_navigationStack.Count == 0)
                return; // No history.

            var previousKey = _navigationStack.Pop();
            // Attempt to navigate using the previous view's type name as the key.
            // If the naming convention differs, callers can extend this logic.
            foreach (var kvp in _viewMap)
            {
                if (kvp.Value.Name.Equals(previousKey, StringComparison.OrdinalIgnoreCase))
                {
                    Navigate(kvp.Key);
                    break;
                }
            }
        }
    }
}
