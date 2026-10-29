using System.Windows;
using System.Windows.Interop;

namespace Murmur.UI.Pill;

/// <summary>
/// The recording pill. A topmost, transparent, click-through tool window that is never activated, so showing it
/// cannot steal focus from the app being dictated into. Create it once, <c>EnsureHandle</c> it, and drive it
/// through <see cref="PillPresenter"/>.
/// </summary>
public partial class PillWindow : Window
{
    /// <summary>
    /// Transparent space around the capsule that the drop shadow is drawn into (the shadow is lowered and blurred,
    /// so there is more room below than above).
    /// </summary>
    public static readonly Thickness ShadowMargin = new(16, 12, 16, 20);

    public PillWindow(PillViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    /// <summary>
    /// The capsule's size in DIPs (without the shadow margin) for the current mode. Measured directly, so it is
    /// right even before the window has ever been shown (WPF does not lay out a window until its first Show).
    /// </summary>
    public Size MeasureCapsule()
    {
        Capsule.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = Capsule.DesiredSize;
        return new Size(
            desired.Width - ShadowMargin.Left - ShadowMargin.Right,
            desired.Height - ShadowMargin.Top - ShadowMargin.Bottom);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PillNative.ApplyPillExStyles(new WindowInteropHelper(this).Handle);
    }
}
