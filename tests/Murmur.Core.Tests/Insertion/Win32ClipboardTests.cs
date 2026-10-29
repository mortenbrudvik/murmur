using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Murmur.Core.Insertion;
using Murmur.Core.Interop;
using Shouldly;

namespace Murmur.Core.Tests.Insertion;

[CollectionDefinition(nameof(Win32ClipboardCollection), DisableParallelization = true)]
public sealed class Win32ClipboardCollection;

/// <summary>
/// Uses the real Windows clipboard, one test at a time, each on its own STA thread. Whatever was on the clipboard is
/// saved before each test and put back afterwards, and every write the tests make is excluded from Win+V history.
/// The tests skip in a session that has no clipboard access at all (a locked workstation or a sandboxed shell).
/// </summary>
[Collection(nameof(Win32ClipboardCollection))]
public sealed class Win32ClipboardTests
{
    private const uint CF_TEXT = 1;
    private const uint CF_METAFILEPICT = 3;
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_ENHMETAFILE = 14;
    private const uint CF_PRIVATEFIRST = 0x200;

    private static readonly uint ExcludeFromMonitoring = RegisterFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint CanIncludeInHistory = RegisterFormat("CanIncludeInClipboardHistory");
    private static readonly uint CanUploadToCloud = RegisterFormat("CanUploadToCloudClipboard");
    private static readonly Lazy<string?> NoClipboardAccessReason = new(ProbeClipboardAccess);

    private readonly Win32Clipboard _clipboard = new();

    [Fact]
    public void SetText_ThenCapture_ContainsUnicodeText()
    {
        var snapshot = OnClipboardThread(() =>
        {
            _clipboard.SetText("héllo wörld ✓");
            return _clipboard.Capture();
        });

        var text = snapshot.Formats.Single(f => f.Format == CF_UNICODETEXT);
        DecodeText(text.Data).ShouldBe("héllo wörld ✓");
    }

    [Fact]
    public void Capture_LeavesOutExclusionFormats()
    {
        var snapshot = OnClipboardThread(() =>
        {
            _clipboard.SetText("x");
            return _clipboard.Capture();
        });

        snapshot.Formats.Select(f => f.Format).ShouldNotContain(ExcludeFromMonitoring);
        snapshot.Formats.Select(f => f.Format).ShouldNotContain(CanIncludeInHistory);
        snapshot.Formats.Select(f => f.Format).ShouldNotContain(CanUploadToCloud);
    }

    [Fact]
    public void SnapshotRestore_RoundTripsCustomFormatAndText()
    {
        OnClipboardThread(() =>
        {
            var custom = RegisterFormat("Murmur.Test.Format");
            RawClipboard.Write((custom, [1, 2, 3]), (CF_UNICODETEXT, EncodeText("orig")));

            var snapshot = _clipboard.Capture();
            _clipboard.SetText("x");
            RawClipboard.ReadText().ShouldBe("x");
            NativeMethods.IsClipboardFormatAvailable(custom).ShouldBeFalse();

            _clipboard.Restore(snapshot);

            RawClipboard.Read(custom).ShouldNotBeNull()[..3].ShouldBe(new byte[] { 1, 2, 3 });
            RawClipboard.ReadText().ShouldBe("orig");
        });
    }

    [Fact]
    public void SnapshotRestore_RoundTripsEnhancedMetafile()
    {
        OnClipboardThread(() =>
        {
            var originalSize = RawClipboard.WriteEnhMetaFile();

            var snapshot = _clipboard.Capture();
            snapshot.Formats.Select(f => f.Format).ShouldContain(CF_ENHMETAFILE);
            snapshot.Formats.Select(f => f.Format).ShouldNotContain(CF_METAFILEPICT); // Windows synthesizes it
            _clipboard.SetText("x");
            NativeMethods.IsClipboardFormatAvailable(CF_ENHMETAFILE).ShouldBeFalse();

            _clipboard.Restore(snapshot);

            NativeMethods.IsClipboardFormatAvailable(CF_ENHMETAFILE).ShouldBeTrue();
            RawClipboard.EnhMetaFileSize().ShouldBe(originalSize);
        });
    }

    [Fact]
    public void Capture_SkipsPrivateFormats()
    {
        OnClipboardThread(() =>
        {
            // Windows never frees private-format handles; this two-byte one is left to leak rather than guess when
            // it would be safe to free.
            RawClipboard.Write((CF_PRIVATEFIRST, [9, 9]), (CF_UNICODETEXT, EncodeText("orig")));

            var formats = _clipboard.Capture().Formats.Select(f => f.Format).ToList();

            formats.ShouldNotContain(CF_PRIVATEFIRST);
            formats.ShouldContain(CF_UNICODETEXT);
        });
    }

    [Theory]
    [InlineData(2u)]     // CF_BITMAP
    [InlineData(3u)]     // CF_METAFILEPICT
    [InlineData(9u)]     // CF_PALETTE
    [InlineData(0x80u)]  // CF_OWNERDISPLAY
    [InlineData(0x82u)]  // CF_DSPBITMAP
    [InlineData(0x83u)]  // CF_DSPMETAFILEPICT
    [InlineData(0x8Eu)]  // CF_DSPENHMETAFILE
    [InlineData(0x200u)] // CF_PRIVATEFIRST
    [InlineData(0x2FFu)] // CF_PRIVATELAST
    [InlineData(0x300u)] // CF_GDIOBJFIRST
    [InlineData(0x3FFu)] // CF_GDIOBJLAST
    public void IsSkippedFormat_HandleBasedOrPrivateFormat_Skipped(uint format)
    {
        Win32Clipboard.IsSkippedFormat(format).ShouldBeTrue();
    }

    [Theory]
    [InlineData(1u)]  // CF_TEXT
    [InlineData(8u)]  // CF_DIB
    [InlineData(13u)] // CF_UNICODETEXT
    [InlineData(14u)] // CF_ENHMETAFILE
    [InlineData(15u)] // CF_HDROP
    [InlineData(17u)] // CF_DIBV5
    public void IsSkippedFormat_DataFormat_Kept(uint format)
    {
        Win32Clipboard.IsSkippedFormat(format).ShouldBeFalse();
    }

    [Fact]
    public void IsSkippedFormat_RegisteredFormats_KeptExceptExclusionMarkers()
    {
        var custom = RegisterFormat("Murmur.Test.Format");

        custom.ShouldBeGreaterThanOrEqualTo(0xC000u);
        Win32Clipboard.IsSkippedFormat(custom).ShouldBeFalse();
        Win32Clipboard.IsSkippedFormat(ExcludeFromMonitoring).ShouldBeTrue();
        Win32Clipboard.IsSkippedFormat(CanIncludeInHistory).ShouldBeTrue();
        Win32Clipboard.IsSkippedFormat(CanUploadToCloud).ShouldBeTrue();
    }

    [Fact]
    public void SetText_MarksExcludedFromHistory()
    {
        OnClipboardThread(() =>
        {
            _clipboard.SetText("x");

            AssertMarkedExcluded();
        });
    }

    [Fact]
    public void Restore_MarksExcludedFromHistory()
    {
        OnClipboardThread(() =>
        {
            _clipboard.Restore(new ClipboardSnapshot([new ClipboardFormatData(CF_UNICODETEXT, EncodeText("orig"))]));

            RawClipboard.ReadText().ShouldBe("orig");
            AssertMarkedExcluded();
        });
    }

    [Fact]
    public void Capture_EmptyClipboard_ReturnsEmpty()
    {
        var snapshot = OnClipboardThread(() =>
        {
            RawClipboard.Write();
            return _clipboard.Capture();
        });

        snapshot.ShouldBeSameAs(ClipboardSnapshot.Empty);
    }

    [Fact]
    public void Restore_Empty_LeavesClipboardEmpty()
    {
        var formatCount = OnClipboardThread(() =>
        {
            _clipboard.SetText("x");
            _clipboard.Restore(ClipboardSnapshot.Empty);
            return NativeMethods.CountClipboardFormats();
        });

        formatCount.ShouldBe(0);
    }

    [Fact]
    public void SequenceNumber_ChangesOnEveryWrite()
    {
        OnClipboardThread(() =>
        {
            var before = _clipboard.SequenceNumber;
            _clipboard.SetText("x");
            var afterSetText = _clipboard.SequenceNumber;
            _clipboard.Restore(new ClipboardSnapshot([new ClipboardFormatData(CF_UNICODETEXT, EncodeText("y"))]));

            afterSetText.ShouldNotBe(before);
            _clipboard.SequenceNumber.ShouldNotBe(afterSetText);
        });
    }

    [Fact]
    public void SequenceNumber_UnchangedWhenAnAppPastesSynthesizedText()
    {
        // The target app may paste CF_TEXT, which Windows synthesizes from Murmur's CF_UNICODETEXT. That must not
        // look like a new copy, or TextInserter would skip the restore.
        OnClipboardThread(() =>
        {
            _clipboard.SetText("x");
            var sequence = _clipboard.SequenceNumber;

            RawClipboard.Read(CF_TEXT).ShouldNotBeNull();

            _clipboard.SequenceNumber.ShouldBe(sequence);
        });
    }

    private static void AssertMarkedExcluded()
    {
        NativeMethods.IsClipboardFormatAvailable(ExcludeFromMonitoring).ShouldBeTrue();
        BitConverter.ToUInt32(RawClipboard.Read(CanIncludeInHistory).ShouldNotBeNull()).ShouldBe(0u);
        BitConverter.ToUInt32(RawClipboard.Read(CanUploadToCloud).ShouldNotBeNull()).ShouldBe(0u);
    }

    private void OnClipboardThread(Action body) => OnClipboardThread<object?>(() =>
    {
        body();
        return null;
    });

    /// <summary>
    /// Runs <paramref name="body"/> on a new STA thread, with the owner's clipboard saved before it and put back
    /// after it, whether it passes or fails.
    /// </summary>
    private T OnClipboardThread<T>(Func<T> body)
    {
        Assert.SkipWhen(NoClipboardAccessReason.Value is not null, NoClipboardAccessReason.Value ?? "");

        return OnStaThread(() =>
        {
            var ownersClipboard = _clipboard.Capture();
            try
            {
                return body();
            }
            finally
            {
                _clipboard.Restore(ownersClipboard);
            }
        });
    }

    /// <summary>Runs <paramref name="body"/> on a new STA thread and rethrows whatever it threw.</summary>
    private static T OnStaThread<T>(Func<T> body)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }

    /// <returns>Null if the clipboard can be opened; otherwise why not.</returns>
    private static string? ProbeClipboardAccess() => OnStaThread(() =>
    {
        // Give an app that is briefly holding the clipboard half a second to let go.
        for (var attempt = 0; attempt < 25; attempt++)
        {
            if (NativeMethods.OpenClipboard(0))
            {
                NativeMethods.CloseClipboard();
                return null;
            }

            Thread.Sleep(20);
        }

        return $"This session cannot open the clipboard (Win32 error {Marshal.GetLastPInvokeError()}), "
            + "for example because the workstation is locked or the process is sandboxed.";
    });

    private static uint RegisterFormat(string name)
    {
        var format = NativeMethods.RegisterClipboardFormat(name);
        if (format == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return format;
    }

    private static byte[] EncodeText(string text) => Encoding.Unicode.GetBytes(text + '\0');

    private static string DecodeText(byte[] data)
    {
        var text = Encoding.Unicode.GetString(data);
        var end = text.IndexOf('\0', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    /// <summary>Direct Win32 clipboard access, independent of <see cref="Win32Clipboard"/>, to set up and check state.</summary>
    private static class RawClipboard
    {
        /// <summary>
        /// Replaces the clipboard contents (none empties it). A non-empty write is marked excluded so it stays out
        /// of Win+V history.
        /// </summary>
        public static void Write(params (uint Format, byte[] Data)[] formats)
        {
            using var session = Open();
            if (!NativeMethods.EmptyClipboard())
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            if (formats.Length == 0)
                return;

            SetData(ExcludeFromMonitoring, [0, 0, 0, 0]);
            foreach (var (format, data) in formats)
                SetData(format, data);
        }

        public static byte[]? Read(uint format)
        {
            using var session = Open();
            var handle = NativeMethods.GetClipboardData(format);
            if (handle == 0)
                return null;

            var size = (int)NativeMethods.GlobalSize(handle);
            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == 0)
                return null;

            try
            {
                var data = new byte[size];
                Marshal.Copy(pointer, data, 0, size);
                return data;
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }

        public static string? ReadText() => Read(CF_UNICODETEXT) is { } data ? DecodeText(data) : null;

        /// <summary>Draws a small enhanced metafile and makes it the clipboard's only content (marked excluded).</summary>
        /// <returns>The size of its metafile bits.</returns>
        public static uint WriteEnhMetaFile()
        {
            var dc = Gdi.CreateEnhMetaFile(0, null, 0, null);
            if (dc == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            Gdi.Rectangle(dc, 0, 0, 100, 50).ShouldBeTrue();
            var metafile = Gdi.CloseEnhMetaFile(dc);
            if (metafile == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            var size = Gdi.GetEnhMetaFileBits(metafile, 0, 0);
            size.ShouldBeGreaterThan(0u);

            using var session = Open();
            if (!NativeMethods.EmptyClipboard())
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            SetData(ExcludeFromMonitoring, [0, 0, 0, 0]);
            if (NativeMethods.SetClipboardData(CF_ENHMETAFILE, metafile) == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                NativeMethods.DeleteEnhMetaFile(metafile);
                throw new Win32Exception(error);
            }

            return size;
        }

        /// <returns>The size of the clipboard's CF_ENHMETAFILE bits, or 0 if there is none.</returns>
        public static uint EnhMetaFileSize()
        {
            using var session = Open();
            var metafile = NativeMethods.GetClipboardData(CF_ENHMETAFILE);
            return metafile == 0 ? 0 : Gdi.GetEnhMetaFileBits(metafile, 0, 0);
        }

        private static void SetData(uint format, byte[] data)
        {
            var handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)data.Length);
            if (handle == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            var pointer = NativeMethods.GlobalLock(handle);
            Marshal.Copy(data, 0, pointer, data.Length);
            NativeMethods.GlobalUnlock(handle);

            if (NativeMethods.SetClipboardData(format, handle) == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                NativeMethods.GlobalFree(handle);
                throw new Win32Exception(error);
            }
        }

        private static Session Open()
        {
            for (var attempt = 1; ; attempt++)
            {
                if (NativeMethods.OpenClipboard(0))
                    return new Session();
                if (attempt == 10)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                Thread.Sleep(20);
            }
        }

        private readonly struct Session : IDisposable
        {
            public void Dispose() => NativeMethods.CloseClipboard();
        }
    }

    /// <summary>GDI calls only the tests need (to draw a metafile), declared without pointers so no unsafe code is needed.</summary>
    private static class Gdi
    {
        [DllImport("gdi32.dll", EntryPoint = "CreateEnhMetaFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint CreateEnhMetaFile(nint hdcRef, string? fileName, nint bounds, string? description);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Rectangle(nint hdc, int left, int top, int right, int bottom);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern nint CloseEnhMetaFile(nint hdc);

        [DllImport("gdi32.dll")]
        public static extern uint GetEnhMetaFileBits(nint hEMF, uint nSize, nint lpData);
    }
}
