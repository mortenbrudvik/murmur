using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Murmur.UI.Pill;

public enum PillMode
{
    Hidden,
    Listening,
    Transcribing,
    Notice,
}

/// <summary>State behind the pill: which face it shows, the notice text and the waveform bars. UI-thread only.</summary>
public sealed partial class PillViewModel : ObservableObject
{
    public const int BarCount = 8;
    public const double MinBar = 0.2;
    public const double MaxBar = 1.0;

    // Levels at or below this read as the minimum bar, levels at or above MaxDbfs as the full bar.
    private const float MinDbfs = -60f;
    private const float MaxDbfs = -20f;

    // A bar rises to its level at once but falls by no more than this from the one before, so a loud word dies away
    // over a few bars instead of dropping to the minimum.
    private const double MaxFall = 0.15;

    [ObservableProperty]
    private PillMode _mode = PillMode.Hidden;

    [ObservableProperty]
    private string _noticeText = string.Empty;

    /// <summary>Bar heights as a fraction (<see cref="MinBar"/> to <see cref="MaxBar"/>) of the full height, oldest first.</summary>
    public ObservableCollection<double> Bars { get; } = new(Enumerable.Repeat(MinBar, BarCount));

    /// <summary>
    /// Adds a microphone level (RMS in dBFS) as the newest bar and drops the oldest. The bars shift one place in the
    /// collection itself (it only raises Replace, never Remove and Add, so the view reuses its bar containers), and the
    /// new bar is at most <see cref="MaxFall"/> lower than the one before.
    /// </summary>
    public void PushLevel(float rmsDbfs)
    {
        var newest = Math.Max(ToBar(rmsDbfs), Bars[^1] - MaxFall);
        for (var i = 0; i < Bars.Count - 1; i++)
        {
            Bars[i] = Bars[i + 1];
        }

        Bars[^1] = newest;
    }

    private static double ToBar(float rmsDbfs)
    {
        if (float.IsNaN(rmsDbfs))
        {
            return MinBar;
        }

        var fraction = (rmsDbfs - MinDbfs) / (MaxDbfs - MinDbfs);
        return Math.Clamp(MinBar + fraction * (MaxBar - MinBar), MinBar, MaxBar);
    }

    // A new recording starts from a flat waveform, not the previous recording's last levels.
    partial void OnModeChanged(PillMode oldValue, PillMode newValue)
    {
        if (newValue == PillMode.Listening && oldValue != PillMode.Listening)
        {
            for (var i = 0; i < Bars.Count; i++)
            {
                Bars[i] = MinBar;
            }
        }
    }
}
