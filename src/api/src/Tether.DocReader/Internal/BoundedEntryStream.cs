namespace Tether.DocReader.Internal;

/// <summary>Inflated bytes across every entry opened in one read (design rule 1: total at most 50 MB, counted while reading).</summary>
internal sealed class InflateBudget(long maxTotal)
{
    private long _total;

    public void Add(int n)
    {
        _total += n;
        if (_total > maxTotal) throw new DocxRejectedException(DocxFailure.TotalTooLarge);
    }
}

/// <summary>
/// Wraps one entry's inflating stream and enforces, on bytes actually produced (never on header values): the per-entry cap, the ratio
/// cap once the entry passes the threshold, and the shared total. It also computes the CRC-32 of what it passed on, so a part read to its
/// end can be checked against the header whatever the runtime does, and keeps the first bytes so a DOCTYPE can be named as the reason.
/// </summary>
internal sealed class BoundedEntryStream : Stream
{
    private const int HeadCapacity = 4096;
    private readonly Stream _inner;
    private readonly DocxLimits _limits;
    private readonly InflateBudget _budget;
    private readonly long _compressed;
    private readonly byte[] _head = new byte[HeadCapacity];
    private int _headLength;
    private uint _crcState = 0xFFFFFFFFu;

    public BoundedEntryStream(Stream inner, long compressedLength, DocxLimits limits, InflateBudget budget)
    {
        _inner = inner;
        _limits = limits;
        _budget = budget;
        _compressed = Math.Max(1, compressedLength);
    }

    public long BytesRead { get; private set; }

    public uint Crc32 => _crcState ^ 0xFFFFFFFFu;

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var n = _inner.Read(buffer);
        if (n <= 0) return n;
        BytesRead += n;
        if (BytesRead > _limits.MaxEntryUncompressedBytes) throw new DocxRejectedException(DocxFailure.EntryTooLarge);
        if (BytesRead > _limits.CompressionRatioCheckAfterBytes && BytesRead > _compressed * _limits.MaxCompressionRatio)
            throw new DocxRejectedException(DocxFailure.CompressionRatioExceeded);
        _budget.Add(n);
        var chunk = buffer[..n];
        _crcState = Crc.Update(_crcState, chunk);
        if (_headLength < HeadCapacity)
        {
            var take = Math.Min(HeadCapacity - _headLength, n);
            chunk[..take].CopyTo(_head.AsSpan(_headLength));
            _headLength += take;
        }
        return n;
    }

    /// <summary>True when the first bytes hold <c>&lt;!DOCTYPE</c> in UTF-8, UTF-16LE or UTF-16BE (only used to name the failure).</summary>
    public bool HeadHasDoctype()
    {
        var head = _head.AsSpan(0, _headLength);
        if (head.IndexOf("<!DOCTYPE"u8) >= 0) return true;
        Span<byte> le = stackalloc byte[18];
        Span<byte> be = stackalloc byte[18];
        var word = "<!DOCTYPE"u8;
        for (var i = 0; i < word.Length; i++) { le[2 * i] = word[i]; le[2 * i + 1] = 0; be[2 * i] = 0; be[2 * i + 1] = word[i]; }
        return head.IndexOf(le) >= 0 || head.IndexOf(be) >= 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>CRC-32 (IEEE 802.3, as ZIP uses). System.IO.Hashing is a package, so the reader carries its own 20 lines.</summary>
internal static class Crc
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
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

    public static uint Update(uint state, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) state = Table[(state ^ b) & 0xFF] ^ (state >> 8);
        return state;
    }
}
