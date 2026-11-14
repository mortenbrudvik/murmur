using CommunityToolkit.Mvvm.ComponentModel;
using Murmur.Core.Models;
using Murmur.Core.Speech;

namespace Murmur.UI.Splash;

/// <summary>
/// What the splash says besides its animation: the version, and the speech engine's status in the wording the main
/// window uses. Until the engine has reported, the model is on its way.
/// </summary>
public sealed partial class SplashViewModel(string version) : ObservableObject
{
    public string Version { get; } = version;

    [ObservableProperty]
    private string _statusText = "Loading model…";

    [ObservableProperty]
    private EngineStatusTone _statusTone = EngineStatusTone.Info;

    /// <summary>Shows a new engine status. Call on the UI thread.</summary>
    public void Apply(EngineStatus status)
    {
        var line = EngineStatusText.Describe(status);
        StatusText = line.Text;
        StatusTone = line.Tone;
    }
}
