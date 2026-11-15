using System.Windows;
using System.Windows.Input;

namespace Murmur.UI.About;

/// <summary>
/// The About window: a small window of its own, opened from the tray menu, that never involves the main window. What
/// it says and does is <see cref="AboutViewModel"/>'s; the window only closes when asked and lets itself be dragged.
/// </summary>
public partial class AboutWindow : Window
{
    private readonly AboutViewModel _viewModel;

    public AboutWindow(AboutViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.CloseRequested += Close;
    }

    protected override void OnClosed(EventArgs e)
    {
        // The view model serves the next About window too, and must not keep this one alive or close it again.
        _viewModel.CloseRequested -= Close;
        base.OnClosed(e);
    }

    /// <summary>The window has no title bar: its header is where it is dragged by.</summary>
    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
