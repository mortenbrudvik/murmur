using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Murmur.Core.Interop;

namespace Murmur.Core.Insertion;

/// <summary>
/// <see cref="IClipboard"/> over the Win32 clipboard API. Each call opens and closes the clipboard within the call,
/// so it can be used from any thread.
/// </summary>
public sealed class Win32Clipboard : IClipboard
{
    private const uint CF_BITMAP = 2;
    private const uint CF_METAFILEPICT = 3;
    private const uint CF_PALETTE = 9;
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_ENHMETAFILE = 14;
    private const uint CF_OWNERDISPLAY = 0x80;
    private const uint CF_GDIOBJFIRST = 0x300;
    private const uint CF_GDIOBJLAST = 0x3FF;

    private const int OpenAttempts = 10;
    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(20);

    // Clipboard history (Win+V) and cloud clipboard skip content that carries these formats. The first one takes
    // any data; the other two take a DWORD, where 0 means "exclude".
    private static readonly uint ExcludeFromMonitoringFormat = RegisterFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint CanIncludeInHistoryFormat = RegisterFormat("CanIncludeInClipboardHistory");
    private static readonly uint CanUploadToCloudFormat = RegisterFormat("CanUploadToCloudClipboard");
    private static readonly byte[] DwordZero = [0, 0, 0, 0];

    public uint SequenceNumber => NativeMethods.GetClipboardSequenceNumber();

    /// <summary>
    /// Copies every HGLOBAL-backed format. Formats whose data is a GDI or other handle (bitmaps, metafiles, palettes,
    /// owner-display, GDI objects) are skipped: Windows synthesizes CF_BITMAP from CF_DIB, which is copied. Murmur's
    /// own exclusion markers are skipped too, since every write adds them anyway.
    /// </summary>
    public ClipboardSnapshot Capture()
    {
        using var session = OpenClipboard();

        var formats = new List<ClipboardFormatData>();
        uint format = 0;
        while ((format = NativeMethods.EnumClipboardFormats(format)) != 0)
        {
            if (IsSkipped(format))
                continue;

            var handle = NativeMethods.GetClipboardData(format);
            if (handle != 0 && ReadGlobal(handle) is { } data)
                formats.Add(new ClipboardFormatData(format, data));
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != 0)
            throw new Win32Exception(error);

        return formats.Count == 0 ? ClipboardSnapshot.Empty : new ClipboardSnapshot(formats);
    }

    public void Restore(ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        using var session = OpenClipboard();
        EmptyClipboard();
        if (snapshot.Formats.Count == 0)
            return;

        MarkExcludedFromHistory();
        foreach (var format in snapshot.Formats)
            SetData(format.Format, format.Data);
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var data = new byte[Encoding.Unicode.GetByteCount(text) + sizeof(char)]; // UTF-16 plus the terminating null
        Encoding.Unicode.GetBytes(text, data);

        using var session = OpenClipboard();
        EmptyClipboard();
        MarkExcludedFromHistory();
        SetData(CF_UNICODETEXT, data);
    }

    private static bool IsSkipped(uint format) =>
        format is CF_BITMAP or CF_METAFILEPICT or CF_PALETTE or CF_ENHMETAFILE or CF_OWNERDISPLAY
            or (>= CF_GDIOBJFIRST and <= CF_GDIOBJLAST)
        || format == ExcludeFromMonitoringFormat
        || format == CanIncludeInHistoryFormat
        || format == CanUploadToCloudFormat;

    /// <summary>
    /// Written first, so that even a write that fails halfway stays out of history. The markers only need to be on
    /// the clipboard when it is closed, which is when history and cloud clipboard look at it.
    /// </summary>
    private static void MarkExcludedFromHistory()
    {
        SetData(ExcludeFromMonitoringFormat, DwordZero);
        SetData(CanIncludeInHistoryFormat, DwordZero);
        SetData(CanUploadToCloudFormat, DwordZero);
    }

    /// <returns>A copy of the memory block, or null if <paramref name="handle"/> is not a readable, non-empty HGLOBAL.</returns>
    private static unsafe byte[]? ReadGlobal(nint handle)
    {
        var size = NativeMethods.GlobalSize(handle);
        if (size == 0 || size > (nuint)Array.MaxLength)
            return null;

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == 0)
            return null;

        try
        {
            return new ReadOnlySpan<byte>((void*)pointer, (int)size).ToArray();
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    /// <remarks>
    /// Never allocates zero bytes: a zero-byte movable block is created "discarded", cannot be locked, and so cannot
    /// be handed to the clipboard.
    /// </remarks>
    private static unsafe void SetData(uint format, ReadOnlySpan<byte> data)
    {
        var size = Math.Max(data.Length, 1);
        var handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE | NativeMethods.GMEM_ZEROINIT, (nuint)size);
        if (handle == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        try
        {
            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            data.CopyTo(new Span<byte>((void*)pointer, size));
            NativeMethods.GlobalUnlock(handle);

            if (NativeMethods.SetClipboardData(format, handle) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            handle = 0; // the system owns it now
        }
        finally
        {
            if (handle != 0)
                NativeMethods.GlobalFree(handle);
        }
    }

    private static void EmptyClipboard()
    {
        if (!NativeMethods.EmptyClipboard())
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    /// <summary>Opens the clipboard, retrying while another app holds it.</summary>
    private static ClipboardSession OpenClipboard()
    {
        for (var attempt = 1; ; attempt++)
        {
            if (NativeMethods.OpenClipboard(0))
                return new ClipboardSession();

            if (attempt == OpenAttempts)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The clipboard is in use by another app.");

            Thread.Sleep(OpenRetryDelay);
        }
    }

    private static uint RegisterFormat(string name)
    {
        var format = NativeMethods.RegisterClipboardFormat(name);
        if (format == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return format;
    }

    private readonly ref struct ClipboardSession : IDisposable
    {
        public void Dispose() => NativeMethods.CloseClipboard();
    }
}
