using System.Buffers.Binary;
using System.Text;

namespace Tether.Syllabus.Application;

/// <summary>
/// DR-04: the intake's DOCX check (API_CONTRACTS_P2 5.1 item 1: a ZIP with <c>[Content_Types].xml</c> and <c>word/document.xml</c>). It reads
/// only the ZIP central directory, from the last <see cref="TailBytes"/> of the upload kept in memory while streaming (never written anywhere),
/// so the order in which a tool writes its entries does not matter. It is deliberately shallow: it never inflates, never opens an entry and
/// never follows a local header. Zip bombs, unsafe paths (<c>..</c>, absolute, backslash), duplicates, macros, OLE and embedded packages are
/// still decided by the sandboxed reader (Tether.DocReader, which the API never references). Anything it cannot read plainly is "not a DOCX"
/// (415), never an exception: multi-disk, ZIP64, a directory that does not end exactly at the end record, more than <see cref="MaxEntries"/>
/// entries, a name over <see cref="MaxNameBytes"/> bytes, or a directory that does not fit the window. The two limits are the reader's
/// (<c>DocxLimits.MaxEntries</c>, <c>DocxLimits.MaxPathLength</c>), so a file refused here would be refused there too.
/// </summary>
internal static class ZipDirectory
{
    /// <summary>The end record (22 bytes plus a comment of at most 65,535) and a directory of 1,000 entries with 260-byte names fit easily.</summary>
    public const int TailBytes = 512 * 1024;
    public const int MaxEntries = 1_000;
    public const int MaxNameBytes = 260;

    private const int EndRecordBytes = 22;
    private const int CentralHeaderBytes = 46;
    private const uint EndSignature = 0x06054b50;
    private const uint CentralSignature = 0x02014b50;

    private static ReadOnlySpan<byte> ContentTypesName => "[Content_Types].xml"u8;
    private static ReadOnlySpan<byte> MainDocumentName => "word/document.xml"u8;

    /// <param name="tail">The last bytes of the file (all of it, when it is smaller than the window).</param>
    /// <param name="fileLength">The total number of bytes read, so directory offsets can be placed inside the window.</param>
    public static bool HasDocxParts(ReadOnlySpan<byte> tail, long fileLength)
    {
        if (tail.Length < EndRecordBytes || fileLength < tail.Length) return false;
        var tailStart = fileLength - tail.Length;

        // The end record is the last thing in the file: its comment length must account for every byte after it.
        var end = -1;
        for (var i = tail.Length - EndRecordBytes; i >= 0 && i >= tail.Length - EndRecordBytes - ushort.MaxValue; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail[i..]) == EndSignature
                && BinaryPrimitives.ReadUInt16LittleEndian(tail[(i + 20)..]) == tail.Length - i - EndRecordBytes) { end = i; break; }
        }
        if (end < 0) return false;

        var record = tail[end..];
        var disk = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        var directoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        var entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        if (disk != 0 || directoryDisk != 0 || entriesOnDisk != entries) return false;          // multi-disk
        if (entries == 0 || entries > MaxEntries) return false;                                  // includes the ZIP64 marker 0xFFFF
        if ((long)directoryOffset + directorySize != tailStart + end) return false;               // includes 0xFFFFFFFF and a ZIP64 locator
        if (directoryOffset < tailStart) return false;                                           // the directory does not fit the window

        var p = (int)(directoryOffset - tailStart);
        bool contentTypes = false, mainDocument = false;
        for (var k = 0; k < entries; k++)
        {
            if (p > end - CentralHeaderBytes) return false;
            var header = tail[p..end];
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != CentralSignature) return false;
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            var localOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[42..]);
            if (nameLength is 0 or > MaxNameBytes || localOffset >= directoryOffset) return false;
            var size = CentralHeaderBytes + nameLength + extraLength + commentLength;
            if (size > header.Length) return false;
            var name = header.Slice(CentralHeaderBytes, nameLength);
            // OPC part names compare case-insensitively (the reader looks parts up with OrdinalIgnoreCase); exact names only, no paths.
            contentTypes |= Ascii.EqualsIgnoreCase(name, ContentTypesName);
            mainDocument |= Ascii.EqualsIgnoreCase(name, MainDocumentName);
            p += size;
        }
        return p == end && contentTypes && mainDocument;
    }

    /// <summary>A fixed-size ring holding the last <see cref="TailBytes"/> bytes of a stream.</summary>
    internal sealed class Tail
    {
        private readonly byte[] _buffer = new byte[TailBytes];
        private int _position;
        private long _written;

        public void Append(ReadOnlySpan<byte> data)
        {
            _written += data.Length;
            if (data.Length >= _buffer.Length) { data[^_buffer.Length..].CopyTo(_buffer); _position = 0; return; }
            var first = Math.Min(_buffer.Length - _position, data.Length);
            data[..first].CopyTo(_buffer.AsSpan(_position));
            data[first..].CopyTo(_buffer);
            _position = (_position + data.Length) % _buffer.Length;
        }

        public byte[] ToArray()
        {
            if (_written <= _buffer.Length) return _buffer.AsSpan(0, (int)_written).ToArray();
            var result = new byte[_buffer.Length];
            _buffer.AsSpan(_position).CopyTo(result);
            _buffer.AsSpan(0, _position).CopyTo(result.AsSpan(_buffer.Length - _position));
            return result;
        }
    }
}
