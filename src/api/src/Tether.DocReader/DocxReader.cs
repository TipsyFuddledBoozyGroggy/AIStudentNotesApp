using Tether.DocReader.Internal;

namespace Tether.DocReader;

/// <summary>
/// Pure DOCX text reader (P2-B3 stage A; docs/architecture/document-reader.md section 1.3, docs/API_CONTRACTS_P2.md 5.1 items 3, 4, 7).
/// Reads the main body only (PO default DR-SCOPE): no headers, footers, footnotes, endnotes or comments. Never reads document properties
/// (PO default DR-CONTRACT-META), so no author or account name can be returned. In memory only: no filesystem, no network, nothing outside
/// the archive is ever resolved. Not wired into any endpoint or job; it is meant to run inside the per-job parser sandbox (design section 2).
/// </summary>
public static class DocxReader
{
    /// <summary>
    /// Reads at most <see cref="DocxLimits.MaxInputBytes"/> from <paramref name="input"/> and returns the body text, or a typed failure.
    /// Hostile input never throws: every failure, including parser and decompression errors, is a <see cref="DocxResult"/> whose reason
    /// carries no exception text. Only programming errors throw (a null argument, a non-positive limit). A stack overflow cannot be caught
    /// by .NET; the reader is iterative to avoid one, and the sandbox process boundary is the backstop.
    /// </summary>
    public static DocxResult Read(Stream input, DocxLimits limits)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        try
        {
            return new DocxSession(limits).Run(input);
        }
        catch (DocxRejectedException rejected)
        {
            return DocxResult.Fail(rejected.Reason);
        }
        catch (Exception e) when (e is OutOfMemoryException or InsufficientExecutionStackException)
        {
            return DocxResult.Fail(DocxFailure.ResourceExhausted);
        }
        catch (Exception)
        {
            return DocxResult.Fail(DocxFailure.Unexpected);   // never the message: parser text can hold document content
        }
    }
}
