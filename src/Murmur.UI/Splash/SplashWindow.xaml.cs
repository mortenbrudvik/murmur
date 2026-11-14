using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Murmur.UI.Splash;

/// <summary>
/// The splash: a small borderless window shown while Murmur starts, before WebView2 and the main window exist. It
/// only draws and reports a wish to be skipped; when it closes is decided elsewhere (SplashGate, in App).
/// </summary>
public partial class SplashWindow : Window
{
    private static readonly Duration FadeOut = new(TimeSpan.FromMilliseconds(150));

    public SplashWindow(SplashViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    /// <summary>
    /// Starts the animation: the canvas's clock runs from 0 to the timeline's end in real time, and holds there. Until
    /// this is called the splash shows its first frame. With animations turned off in Windows the canvas shows its
    /// final frame whatever the clock says.
    /// </summary>
    public void Play()
    {
        Canvas.BeginAnimation(
            SplashCanvas.TimeProperty,
            new DoubleAnimation(0, SplashTimeline.End, new Duration(TimeSpan.FromSeconds(SplashTimeline.End))));
    }

    /// <summary>The user clicked the splash or pressed a key: show the main window now.</summary>
    public event Action? SkipRequested;

    /// <summary>Fades the splash out and closes it. Call on the UI thread; calling it again does nothing.</summary>
    public Task FadeOutAndCloseAsync()
    {
        if (_closing is not null)
            return _closing.Task;

        _closing = new TaskCompletionSource();
        var fade = new DoubleAnimation(0, FadeOut);
        fade.Completed += (_, _) =>
        {
            Close();
            _closing.TrySetResult();
        };
        BeginAnimation(OpacityProperty, fade);
        return _closing.Task;
    }

    private TaskCompletionSource? _closing;

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        SkipRequested?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        SkipRequested?.Invoke();
    }
}
