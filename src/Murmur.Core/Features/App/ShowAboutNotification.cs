using MediatR;

namespace Murmur.Core.Features.App;

/// <summary>Published when the About dialog should open in the main window ("About Murmur" in the tray menu).</summary>
public sealed class ShowAboutNotification : INotification;
