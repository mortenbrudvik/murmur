using System.Diagnostics;
using System.Runtime.InteropServices;
using Murmur.Core.Interop;

namespace Murmur.Core.Insertion;

/// <summary><see cref="IForegroundInfo"/> over the foreground window's owning process. Never throws.</summary>
public sealed class Win32ForegroundInfo : IForegroundInfo
{
    internal const string UnknownApp = "Unknown app";

    public ForegroundTarget Capture()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == 0)
            return new ForegroundTarget(0, UnknownApp, false);

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
            return new ForegroundTarget(window, UnknownApp, false);

        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
        {
            // Even an elevated process grants limited query access to a normal one, so a refusal means something
            // more privileged still (a protected or system process): Murmur's input would be blocked there too.
            var denied = Marshal.GetLastPInvokeError() == NativeMethods.ERROR_ACCESS_DENIED;
            return new ForegroundTarget(window, UnknownApp, denied);
        }

        try
        {
            return new ForegroundTarget(window, GetAppName(process), IsElevated(process));
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static bool IsElevated(nint process)
    {
        if (!NativeMethods.OpenProcessToken(process, NativeMethods.TOKEN_QUERY, out var token))
            return Marshal.GetLastPInvokeError() == NativeMethods.ERROR_ACCESS_DENIED;

        try
        {
            return NativeMethods.GetTokenInformation(token, NativeMethods.TokenElevation, out var isElevated, sizeof(uint), out _)
                && isElevated != 0;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    /// <summary>The executable's FileDescription ("Notepad", "Google Chrome"), else its file name without extension.</summary>
    private static string GetAppName(nint process)
    {
        var path = GetImagePath(process);
        if (path is null)
            return UnknownApp;

        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description))
                return description;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall back to the file name.
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    private static unsafe string? GetImagePath(nint process)
    {
        const int capacity = 1024; // room for long paths; a longer one fails the call and the name falls back
        var buffer = stackalloc char[capacity];
        var length = (uint)capacity;
        return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref length)
            ? new string(buffer, 0, (int)length)
            : null;
    }
}
