using System;

namespace BannerGuardSystem.Services
{
    /// <summary>
    /// Defines navigation operations for switching views within the application.
    /// </summary>
    public interface INavigationService
    {
        /// <summary>
        /// Fired when navigation to a new view succeeds.
        /// </summary>
        event Action<object> Navigated;

        /// <summary>
        /// Navigates to a view identified by the given key.
        /// </summary>
        /// <param name="viewKey">The identifier of the target view.</param>
        void Navigate(string viewKey);

        /// <summary>
        /// Navigates back to the previous view if possible.
        /// </summary>
        void GoBack();
    }
}
