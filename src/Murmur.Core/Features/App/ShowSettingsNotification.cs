using MediatR;

namespace Murmur.Core.Features.App;

/// <summary>Published when the main window should go to its Settings page ("Settings" in the tray menu).</summary>
public sealed class ShowSettingsNotification : INotification;
