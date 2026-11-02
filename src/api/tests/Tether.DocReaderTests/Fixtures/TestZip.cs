using System.IO.Compression;
using System.Text;

namespace Tether.DocReaderTests.Fixtures;

/// <summary>One entry for <see cref="TestZip.Write"/>. Header fields can be overridden so a fixture can lie about sizes, flags or names.</summary>
internal sealed class ZipEntrySpec
{
    public required string Name { get; set; }
    /// <summary>Raw name bytes, when the name must hold bytes a string cannot express; otherwise UTF-8 of <see cref="Name"/>.</summary>
    public byte[]? RawName { get; set; }
    public byte[] Data { get; set; } = [];
    public bool Deflate { get; set; } = true;
    /// <summary>General-purpose flags (bit 11 = UTF-8 names; bit 0 = encrypted).</summary>
    public ushort Flags { get; set; } = 0x0800;
    public uint? DeclaredUncompressedSize { get; set; }
    public uint? DeclaredCompressedSize { get; set; }
    /// <summary>Already-deflated data with its CRC and inflated size (for bombs generated without materialising them).</summary>
    public (byte[] Compressed, uint Crc, long Size)? Precompressed { get; set; }
}

/// <summary>
/// A minimal ZIP writer for fixtures (local headers, central directory, end record; no ZIP64, no data descriptors). It writes exactly
/// what the spec says, including lies, which System.IO.Compression's writer would refuse to produce. Timestamps are fixed (1980-01-01),
/// so the same spec always gives the same bytes.
/// </summary>
internal static class TestZip
{
    public static byte[] Write(IEnumerable<ZipEntrySpec> entries)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        var central = new List<(ZipEntrySpec Spec, byte[] Name, ushort Method, uint Crc, uint CSize, uint USize, uint Offset)>();
        foreach (var e in entries)
        {
            var name = e.RawName ?? Encoding.UTF8.GetBytes(e.Name);
            byte[] payload; uint crc; long size; ushort method;
            if (e.Precompressed is { } pre) { payload = pre.Compressed; crc = pre.Crc; size = pre.Size; method = 8; }
            else if (e.Deflate) { payload = DeflateBytes(e.Data); crc = Crc32(e.Data); size = e.Data.Length; method = 8; }
            else { payload = e.Data; crc = Crc32(e.Data); size = e.Data.Length; method = 0; }
            var csize = e.DeclaredCompressedSize ?? (uint)payload.Length;
            var usize = e.DeclaredUncompressedSize ?? checked((uint)size);
            var offset = (uint)ms.Position;
            w.Write(0x04034b50u); w.Write((ushort)20); w.Write(e.Flags); w.Write(method); w.Write((ushort)0); w.Write((ushort)0x0021);
            w.Write(crc); w.Write(csize); w.Write(usize); w.Write((ushort)name.Length); w.Write((ushort)0); w.Write(name); w.Write(payload);
            central.Add((e, name, method, crc, csize, usize, offset));
        }
        var cdStart = (uint)ms.Position;
        foreach (var c in central)
        {
            w.Write(0x02014b50u); w.Write((ushort)20); w.Write((ushort)20); w.Write(c.Spec.Flags); w.Write(c.Method); w.Write((ushort)0); w.Write((ushort)0x0021);
            w.Write(c.Crc); w.Write(c.CSize); w.Write(c.USize); w.Write((ushort)c.Name.Length); w.Write((ushort)0); w.Write((ushort)0);
            w.Write((ushort)0); w.Write((ushort)0); w.Write(0u); w.Write(c.Offset); w.Write(c.Name);
        }
        var cdSize = (uint)ms.Position - cdStart;
        w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)central.Count); w.Write((ushort)central.Count);
        w.Write(cdSize); w.Write(cdStart); w.Write((ushort)0);
        w.Flush();
        return ms.ToArray();
    }

    public static byte[] DeflateBytes(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var d = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) d.Write(data);
        return ms.ToArray();
    }

    /// <summary>Deflates whatever <paramref name="write"/> produces, without holding the inflated bytes, and returns the CRC and size too.</summary>
    public static (byte[] Compressed, uint Crc, long Size) DeflateGenerated(Action<Stream> write)
    {
        using var ms = new MemoryStream();
        var tap = new CrcTap(new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true));
        write(tap);
        tap.Dispose();
        return (ms.ToArray(), tap.Crc, tap.Size);
    }

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Crc32(ReadOnlySpan<byte> data) => Crc32Update(0xFFFFFFFFu, data) ^ 0xFFFFFFFFu;

    internal static uint Crc32Update(uint state, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) state = Table[(state ^ b) & 0xFF] ^ (state >> 8);
        return state;
    }

    private sealed class CrcTap(Stream inner) : Stream
    {
        private uint _state = 0xFFFFFFFFu;
        public uint Crc => _state ^ 0xFFFFFFFFu;
        public long Size { get; private set; }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { _state = Crc32Update(_state, buffer); Size += buffer.Length; inner.Write(buffer); }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Size; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
