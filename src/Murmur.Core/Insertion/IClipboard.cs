namespace Murmur.Core.Insertion;

/// <summary>One clipboard format and a copy of its bytes.</summary>
public sealed record ClipboardFormatData(uint Format, byte[] Data);

/// <summary>A copy of the clipboard's contents, in the order the formats were enumerated.</summary>
public sealed record ClipboardSnapshot(IReadOnlyList<ClipboardFormatData> Formats)
{
    /// <summary>The snapshot of an empty clipboard.</summary>
    public static ClipboardSnapshot Empty { get; } = new([]);
}

/// <summary>
/// The system clipboard. Every write Murmur makes is marked so it stays out of Win+V clipboard history and the
/// cloud clipboard. Members throw <see cref="System.ComponentModel.Win32Exception"/> when the clipboard cannot be
/// opened (another app holds it), read or written.
/// </summary>
public interface IClipboard
{
    /// <summary>Copies every format that can be copied byte for byte; returns <see cref="ClipboardSnapshot.Empty"/> for an empty clipboard.</summary>
    ClipboardSnapshot Capture();

    /// <summary>Replaces the clipboard contents with <paramref name="snapshot"/> (write marked as excluded).</summary>
    void Restore(ClipboardSnapshot snapshot);

    /// <summary>Replaces the clipboard contents with <paramref name="text"/> as CF_UNICODETEXT (write marked as excluded).</summary>
    void SetText(string text);

    /// <summary>The clipboard sequence number; it changes whenever any app (or Murmur) writes the clipboard.</summary>
    uint SequenceNumber { get; }
}
