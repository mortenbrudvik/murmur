using Mediator;
using Murmur.Common;
using Murmur.Core.Features.App;

namespace Murmur.Core.Setup;

/// <summary>
/// Opens the Settings page from outside the web UI ("Settings" in the tray menu). The page lives in the web UI, and a
/// message posted to a page that has not loaded yet is lost: the window may never have been shown when the menu item
/// is picked. So a request made before the UI has drawn is remembered and sent the moment it has. Create this before
/// the UI can become ready (App resolves it at start), or the moment is missed.
/// </summary>
public sealed class UiLauncher
{
    private readonly Lock _lock = new();
    private readonly UiReadiness _ui;
    private readonly IMediator _mediator;
    private bool _pending;

    public UiLauncher(UiReadiness ui, IMediator mediator)
    {
        _ui = ui;
        _mediator = mediator;
        _ui.BecameReady += OnUiReady;
    }

    /// <summary>Safe to call from any thread. Several requests before the UI is ready go to the page once.</summary>
    public void ShowSettings()
    {
        lock (_lock)
        {
            if (!_ui.IsReady)
            {
                _pending = true;
                return;
            }
        }

        Publish();
    }

    private void OnUiReady()
    {
        lock (_lock)
        {
            if (!_pending)
                return;

            _pending = false;
        }

        Publish();
    }

    private void Publish() => _mediator.Publish(new ShowSettingsNotification()).FireAndForget();
}
