using Murmur.Common;
using Serilog;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace Murmur.Core.Services;

/// <summary>
/// Start with Windows for the packaged (Microsoft Store) build, over the package's <c>windows.startupTask</c>
/// extension.
/// </summary>
/// <remarks>
/// The HKCU Run key cannot be used here and, worse, does not fail: MSIX copy-on-writes the value into a private
/// per-package hive that Explorer never reads, then merges that hive on read, so the write succeeds,
/// <see cref="StartupRegistration.IsRegistered"/> reads it straight back, and nothing runs at sign-in. This class is
/// the supported mechanism. It gives up one thing in exchange: once the user turns Murmur off in Task Manager or in
/// Settings &gt; Apps &gt; Startup, the state is <see cref="StartupTaskState.DisabledByUser"/> and
/// <see cref="StartupTask.RequestEnableAsync"/> will not override them. That is logged; the user has the final say.
/// <para>
/// <see cref="Apply"/> is synchronous because the interface is, but every <see cref="StartupTask"/> call goes to an
/// out-of-process broker. So Apply starts the work and returns at once: its callers are on the UI thread (start-up and
/// a settings save) and must neither block nor see an exception. Calls are applied one at a time and in order, so a
/// quick on-then-off cannot end up on.
/// </para>
/// </remarks>
public sealed class PackagedStartupRegistration : IStartupRegistration
{
    /// <summary>Must equal the TaskId in the manifest's uap5:StartupTask element, or GetAsync answers null.</summary>
    public const string TaskId = "MurmurStartup";

    private readonly ILogger _logger = Log.ForContext<PackagedStartupRegistration>();

    // Async waiters are released first in, first out, which is what keeps the calls in order.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _isRegistered;

    /// <summary>
    /// Whether the task was enabled (by Murmur or by policy) when the last <see cref="Apply"/> finished; false until
    /// one has. Not a live query: reading the task takes an asynchronous fetch, so a change made in Task Manager while
    /// Murmur runs is only seen by the next Apply. Nothing in the app reads it today.
    /// </summary>
    public bool IsRegistered => _isRegistered;

    /// <summary>Starts enabling or disabling the startup task and returns at once. Never throws.</summary>
    public void Apply(bool enabled) => ApplyAsync(enabled).FireAndForget();

    /// <summary>
    /// True when Windows started this process through the startup task, at sign-in. The Run key says so on the command
    /// line; a startup task cannot pass arguments, so a packaged start asks Windows how it was activated instead. Only
    /// meaningful with package identity. Never throws.
    /// </summary>
    public static bool IsStartupActivation()
    {
        var logger = Log.ForContext<PackagedStartupRegistration>();
        try
        {
            var kind = AppInstance.GetActivatedEventArgs()?.Kind;
            logger.Information("Started by Windows as {Activation}", kind?.ToString() ?? "an unknown activation");
            return kind == ActivationKind.StartupTask;
        }
        catch (Exception ex)
        {
            // Treated as an ordinary launch: the worst case is the main window opening at sign-in.
            logger.Warning(ex, "Could not read how Windows started Murmur");
            return false;
        }
    }

    /// <summary>The work behind <see cref="Apply"/>. Never faults: every failure is logged here.</summary>
    internal async Task ApplyAsync(bool enabled)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var task = await StartupTask.GetAsync(TaskId).AsTask().ConfigureAwait(false);
            if (task is null)
            {
                _logger.Error("The package declares no startup task {TaskId}; start with Windows cannot work", TaskId);
                return;
            }

            var state = task.State;
            switch (Plan(enabled, state))
            {
                case StartupTaskChange.None:
                    _logger.Debug("The start-with-Windows task is already {State}", state);
                    break;

                case StartupTaskChange.Enable:
                    // Answers the state it ended in, which is not Enabled when Windows or the user overruled it.
                    // Called from a thread-pool thread: for a packaged desktop app it shows no prompt and needs no UI
                    // thread (measured 2026-10-30, task read back Enabled).
                    var result = await task.RequestEnableAsync().AsTask().ConfigureAwait(false);
                    if (IsEnabled(result))
                        _logger.Information("Registered to start with Windows (startup task {TaskId})", TaskId);
                    else
                        _logger.Warning("Asked Windows to start Murmur at sign-in, but the startup task is {State}", result);
                    break;

                case StartupTaskChange.Disable:
                    task.Disable();
                    _logger.Information("Removed the start-with-Windows task");
                    break;

                case StartupTaskChange.RefusedByUser:
                    _logger.Warning(
                        "Start with Windows is on in Murmur's settings, but it was turned off in Task Manager or in " +
                        "Settings > Apps > Startup. Windows does not let an app override that; it stays off until it " +
                        "is turned back on there");
                    break;

                case StartupTaskChange.RefusedByPolicy:
                    _logger.Warning(
                        "The start-with-Windows task is {State} by group policy; Murmur's setting ({Enabled}) cannot change it",
                        state, enabled);
                    break;
            }

            _isRegistered = IsEnabled(task.State);
        }
        catch (Exception ex)
        {
            // Unlike StartupRegistration, this catches Exception broadly rather than a known set: the WinRT/COM
            // surface behind StartupTask is open-ended (broker RPC failures surface as any HRESULT), and nothing above
            // a fire-and-forget call could handle it anyway.
            _logger.Warning(ex, "Could not update the start-with-Windows task");
        }
        finally
        {
            _gate.Release();
        }
    }

#pragma warning disable CS8524
    /// <summary>What <see cref="Apply"/> does for the setting and the task's current state.</summary>
    /// <remarks>
    /// No discard arm on purpose: a <see cref="StartupTaskState"/> member added by a future Windows SDK fails the
    /// build here (CS8509) rather than quietly doing nothing. CS8524, which asks for an arm for unnamed enum values as
    /// well, is suppressed for that reason; such a value throws, and <see cref="ApplyAsync"/> logs it.
    /// </remarks>
    internal static StartupTaskChange Plan(bool enabled, StartupTaskState state) => (enabled, state) switch
    {
        (true, StartupTaskState.Disabled) => StartupTaskChange.Enable,
        (true, StartupTaskState.Enabled) => StartupTaskChange.None,
        (true, StartupTaskState.EnabledByPolicy) => StartupTaskChange.None,
        (true, StartupTaskState.DisabledByUser) => StartupTaskChange.RefusedByUser,
        (true, StartupTaskState.DisabledByPolicy) => StartupTaskChange.RefusedByPolicy,
        (false, StartupTaskState.Enabled) => StartupTaskChange.Disable,
        (false, StartupTaskState.Disabled) => StartupTaskChange.None,
        (false, StartupTaskState.DisabledByUser) => StartupTaskChange.None,
        (false, StartupTaskState.DisabledByPolicy) => StartupTaskChange.None,
        (false, StartupTaskState.EnabledByPolicy) => StartupTaskChange.RefusedByPolicy,
    };
#pragma warning restore CS8524

    /// <summary>Whether Murmur will start at sign-in in this state, whoever enabled it.</summary>
    internal static bool IsEnabled(StartupTaskState state) =>
        state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
}

/// <summary>What <see cref="PackagedStartupRegistration"/> does with the startup task.</summary>
public enum StartupTaskChange
{
    /// <summary>The task is already where the setting wants it.</summary>
    None,

    /// <summary>Ask Windows to enable it (<see cref="StartupTask.RequestEnableAsync"/>).</summary>
    Enable,

    /// <summary>Disable it (<see cref="StartupTask.Disable"/>).</summary>
    Disable,

    /// <summary>The user turned it off in Task Manager or Settings; only they can turn it back on.</summary>
    RefusedByUser,

    /// <summary>A group policy decides; the app can change it neither way.</summary>
    RefusedByPolicy,
}
