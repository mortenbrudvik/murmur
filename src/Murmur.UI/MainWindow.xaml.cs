using System.ComponentModel;
using System.Windows;
using Wpf.Ui.Controls;

namespace Murmur.UI;

/// <summary>Hosts the web UI. Window events are forwarded to <see cref="MainViewModel"/>, which decides what they mean.</summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Deactivated += (_, _) => _viewModel.OnWindowDeactivated();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
                _viewModel.OnWindowHidden();
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                _viewModel.OnWindowMinimized();
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.OnWindowClosing())
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
