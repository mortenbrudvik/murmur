using System.Windows;
using Murmur.Core.Services;

namespace Murmur.UI.About;

/// <summary>
/// "About" in the tray menu: the About window, on its own. The main window is not shown for it, and if it is open it
/// is left as it is. A second request brings the window that is already open to the front. Call on the UI thread.
/// </summary>
public sealed class AboutWindowPresenter(Func<AboutWindow> createWindow, AboutViewModel model, AppLifecycle lifecycle)
{
    /// <summary>The About window while one is open.</summary>
    private AboutWindow? _window;

    public void Show()
    {
        if (lifecycle.IsQuitting)
            return;

        // The history's totals and the engine's status as they are now, for a window opened again as for a new one.
        model.Refresh();

        if (_window is null)
        {
            var window = createWindow();
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                    _window = null;
            };
            _window = window;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
