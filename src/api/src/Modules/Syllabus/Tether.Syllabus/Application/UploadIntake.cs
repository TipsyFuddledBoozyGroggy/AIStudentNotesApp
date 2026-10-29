using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Tether.Syllabus.Application;

internal enum SniffKind { Pdf, Docx, Text, Unsupported }

internal sealed record IntakeResult(IResult? Error, CreateSyllabusRequest? Metadata, string? MetadataCanonical, string? FileSha256, SniffKind Kind, string? DecodedText);

/// <summary>
/// Multipart intake validation only (P2-B1). It decides the file type by magic bytes, enforces the streaming caps while reading, and hashes the
/// bytes in the same pass. It never writes bytes anywhere: there is no parser in this increment (real DOCX/PDF parsing and the sandboxed
/// parser worker are P2-B3), so PDF and DOCX bytes are counted, hashed and discarded; plain text is decoded in memory (it needs no parser)
/// and follows the same path as pasted text. The client filename is read once to compare its extension and is then dropped: never stored,
/// logged, echoed or used as a path (API_CONTRACTS_P2 5.1 item 1).
/// </summary>
internal static class UploadIntake
{
    private const int HeadBytes = 64;
    private const int TextBufferCap = 1024 * 1024; // 50,000 characters never need more than 200 KB; anything bigger is too_large

    public static SniffKind Sniff(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("%PDF-"u8)) return SniffKind.Pdf;
        if (head.StartsWith("PK\x03\x04"u8))
            // OOXML packages written by Word start with [Content_Types].xml; the full check (word/document.xml) belongs to the sandboxed parser.
            return head.Length >= 49 && head.Slice(30, 19).SequenceEqual("[Content_Types].xml"u8) ? SniffKind.Docx : SniffKind.Unsupported;
        return SniffKind.Text;
    }

    /// <summary>UTF-8 or UTF-16 with a BOM only; strict decoding; NUL after decoding is rejected (a UTF-16 file legitimately contains 0x00 before decoding).</summary>
    public static bool TryDecodeText(byte[] bytes, out string text)
    {
        text = "";
        try
        {
            string s;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) s = new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) s = new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) s = new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2);
            else s = new UTF8Encoding(false, true).GetString(bytes);
            if (s.Contains('\0') || s.Length == 0) return false;
            text = s;
            return true;
        }
        catch (Exception ex) when (ex is DecoderFallbackException or ArgumentException) { return false; }
    }

    public static async Task<IntakeResult> ReadAsync(HttpContext http, SyllabusOptions opt, CancellationToken ct)
    {
        static IntakeResult Fail(IResult e) => new(e, null, null, null, SniffKind.Unsupported, null);

        if (!MediaTypeHeaderValue.TryParse(http.Request.ContentType, out var mt) || mt.Boundary.Value is not { Length: > 0 } rawBoundary)
            return Fail(SyllabusProblem.InvalidBare());
        var boundary = HeaderUtilities.RemoveQuotes(rawBoundary).Value ?? "";
        if (boundary.Length is 0 or > 100) return Fail(SyllabusProblem.InvalidBare());

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(opt.UploadReadSeconds));
        var token = timeout.Token;

        byte[]? metadataBytes = null;
        string? fileSha = null; var kind = SniffKind.Unsupported; byte[]? textBytes = null;
        var sawFile = false; var sections = 0;
        try
        {
            var reader = new MultipartReader(boundary, http.Request.Body) { HeadersCountLimit = 8, HeadersLengthLimit = 4096 };
            while (await reader.ReadNextSectionAsync(token) is { } section)
            {
                if (++sections > 2) return Fail(SyllabusProblem.InvalidBare());               // exactly one file part and one metadata part
                var cd = section.GetContentDispositionHeader();
                if (cd is null || !cd.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)) return Fail(SyllabusProblem.InvalidBare());
                var name = HeaderUtilities.RemoveQuotes(cd.Name).Value;
                if (name == "metadata")
                {
                    if (metadataBytes is not null) return Fail(SyllabusProblem.InvalidBare());
                    using var ms = new MemoryStream();
                    var buf = new byte[1024]; int n;
                    while ((n = await section.Body.ReadAsync(buf, token)) > 0)
                    {
                        if (ms.Length + n > opt.MetadataBytes) return Fail(SyllabusProblem.InvalidBare());
                        ms.Write(buf, 0, n);
                    }
                    metadataBytes = ms.ToArray();
                }
                else if (name == "file")
                {
                    if (sawFile) return Fail(SyllabusProblem.InvalidBare());
                    sawFile = true;
                    var ext = cd.FileName.HasValue || cd.FileNameStar.HasValue
                        ? Path.GetExtension(HeaderUtilities.RemoveQuotes(cd.FileName.HasValue ? cd.FileName : cd.FileNameStar).Value ?? "").ToLowerInvariant() : "";
                    var partType = section.ContentType is { Length: > 0 } c ? c.Split(';')[0].Trim().ToLowerInvariant() : "";
                    // the filename is not kept beyond this expression
                    var (k, sha, bytes, error) = await ReadFileAsync(section.Body, opt, token);
                    if (error is not null) return Fail(error);
                    kind = k; fileSha = sha; textBytes = bytes;
                    if (!TypeAgrees(kind, ext, partType)) return Fail(SyllabusProblem.Unsupported());
                }
                else return Fail(SyllabusProblem.InvalidBare());
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Fail(SyllabusProblem.InvalidBare()); }   // slow upload
        catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException or ArgumentException)
        {
            return Fail(ex is BadHttpRequestException { StatusCode: 413 } ? SyllabusProblem.TooLarge() : SyllabusProblem.InvalidBare());
        }
        if (!sawFile || metadataBytes is null || fileSha is null) return Fail(SyllabusProblem.InvalidBare());
        if (kind == SniffKind.Unsupported) return Fail(SyllabusProblem.Unsupported());

        if (!JsonBody.TryBind<CreateSyllabusRequest>(metadataBytes, out var meta) || meta is null || meta.Text is not null)
            return Fail(SyllabusProblem.InvalidBare());      // the metadata part has no `text`; unknown members are rejected by the binder
        string? decoded = null;
        if (kind == SniffKind.Text)
        {
            if (textBytes is null || !TryDecodeText(textBytes, out var t)) return Fail(SyllabusProblem.Unsupported());   // binary, NUL, bad encoding
            decoded = t;
        }
        // Canonical JSON of the metadata part (sorted members) for the fingerprint.
        var canonical = string.Join("|", new[]
        {
            "t=" + (meta.Title ?? ""), "ts=" + (meta.TermStart ?? ""), "te=" + (meta.TermEnd ?? ""), "tz=" + meta.DefaultTimezone,
            "ad=" + (meta.AllowDuplicate ? "1" : "0"), "nv=" + meta.NoticeVersion, "aa=" + meta.Attestation.AcceptedAt,
        });
        return new IntakeResult(null, meta, canonical, fileSha, kind, decoded);
    }

    private static async Task<(SniffKind Kind, string? Sha, byte[]? TextBytes, IResult? Error)> ReadFileAsync(Stream body, SyllabusOptions opt, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[64 * 1024];
        var head = new byte[HeadBytes]; var headLen = 0;
        var total = 0L; var kind = SniffKind.Unsupported; var sniffed = false; var keep = true;
        var text = new MemoryStream();
        int n;
        while ((n = await body.ReadAsync(buf, ct)) > 0)
        {
            total += n;
            if (total > opt.UploadMaxBytes) return (kind, null, null, SyllabusProblem.TooLarge());      // counted while reading, never Content-Length
            hash.AppendData(buf, 0, n);
            if (headLen < HeadBytes) { var take = Math.Min(HeadBytes - headLen, n); Array.Copy(buf, 0, head, headLen, take); headLen += take; }
            if (!sniffed && headLen >= HeadBytes)
            {
                kind = Sniff(head.AsSpan(0, headLen)); sniffed = true;
                if (kind == SniffKind.Unsupported) return (kind, null, null, SyllabusProblem.Unsupported());
                if (kind != SniffKind.Text) { keep = false; text = new MemoryStream(); }
            }
            if (!keep) continue;                                   // PDF/DOCX bytes are hashed and dropped, never kept
            if (text.Length + n > TextBufferCap) return (kind, null, null, SyllabusProblem.TooLarge());
            text.Write(buf, 0, n);
        }
        if (!sniffed)
        {
            kind = headLen == 0 ? SniffKind.Unsupported : Sniff(head.AsSpan(0, headLen));
            if (kind == SniffKind.Unsupported) return (kind, null, null, SyllabusProblem.Unsupported());
            keep = kind == SniffKind.Text;
        }
        return (kind, Convert.ToHexStringLower(hash.GetHashAndReset()), keep ? text.ToArray() : null, null);
    }

    /// <summary>Extension, part Content-Type and signature must agree (a disagreement is 415, never a guess).</summary>
    private static bool TypeAgrees(SniffKind kind, string ext, string partType)
    {
        var expectedExt = kind switch { SniffKind.Pdf => ".pdf", SniffKind.Docx => ".docx", _ => ".txt" };
        var expectedType = kind switch
        {
            SniffKind.Pdf => "application/pdf", SniffKind.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", _ => "text/plain",
        };
        if (ext.Length > 0 && ext != expectedExt) return false;
        if (partType.Length > 0 && partType != "application/octet-stream" && partType != expectedType) return false;
        return true;
    }
}
