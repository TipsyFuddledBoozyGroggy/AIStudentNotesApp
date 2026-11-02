using System.Text;
using System.Xml;

namespace Tether.DocReader.Internal;

internal sealed class Counters
{
    public int HiddenRuns, TinyRuns, TrackedDeletions, ExternalRelationships, FieldCodes, AltText, Fallbacks, NoteReferences, HeaderFooterParts, DirectionOverrides;
    public readonly HashSet<string> CommentIds = new(StringComparer.Ordinal);

    public DocxDropCounts ToCounts() => new()
    {
        HiddenRuns = HiddenRuns, TinyRuns = TinyRuns, TrackedDeletions = TrackedDeletions, Comments = CommentIds.Count,
        ExternalRelationshipsIgnored = ExternalRelationships, FieldCodesDropped = FieldCodes, AltTextDropped = AltText,
        AlternateContentFallbacksSkipped = Fallbacks, NoteReferencesSkipped = NoteReferences, HeaderFooterPartsSkipped = HeaderFooterParts,
        DirectionOverrides = DirectionOverrides,
    };
}

/// <summary>
/// Streams <c>word/document.xml</c> once, iteratively (no recursion), and builds paragraphs in reading order (design rules 5 and 6).
/// Runs are buffered until their end so formatting is judged whatever order a hostile file puts <c>rPr</c> and <c>w:t</c> in. Text is
/// read in chunks and counted against <see cref="DocxLimits.MaxTextCharacters"/> before it is buffered, hidden text included, so
/// memory stays bounded and the cap trips during extraction.
/// </summary>
internal sealed class BodyExtractor(XmlCursor x, StyleTable styles, Counters counters, DocxLimits limits)
{
    private readonly XmlReader _r = x.R;
    private readonly List<string> _out = [];
    private readonly List<Frame> _stack = [];
    private readonly char[] _chunk = new char[4096];
    private long _chars;
    private int _anonymousComments;

    private abstract class Frame(int depth) { public int Depth { get; } = depth; }
    private sealed class ParaFrame(int depth) : Frame(depth) { public readonly StringBuilder Text = new(); public string? Style; }
    private sealed class RunFrame(int depth) : Frame(depth) { public readonly StringBuilder Text = new(); public RunProps Props; }
    private sealed class CellFrame(int depth) : Frame(depth) { public readonly List<string> Paragraphs = []; }
    private sealed class RowFrame(int depth) : Frame(depth) { public readonly List<string> Cells = []; }
    private sealed class AltFrame(int depth) : Frame(depth) { public bool BranchTaken; }

    public List<string> Run()
    {
        var sawRoot = false;
        while (x.Read())
        {
            if (_r.NodeType == XmlNodeType.Element)
            {
                if (!sawRoot)
                {
                    sawRoot = true;
                    if (_r.LocalName != "document" || !Ns.IsW(_r.NamespaceURI)) throw new DocxRejectedException(DocxFailure.NotWordprocessingDocument);
                    continue;
                }
                OnStart();
            }
            else if (_r.NodeType == XmlNodeType.EndElement && _stack.Count > 0 && _stack[^1].Depth == _r.Depth)
            {
                Pop();
            }
        }
        return _out;
    }

    private void OnStart()
    {
        var ns = _r.NamespaceURI;
        var name = _r.LocalName;
        var depth = _r.Depth;
        var empty = _r.IsEmptyElement;

        if (Ns.IsW(ns))
        {
            switch (name)
            {
                case "p": Push(new ParaFrame(depth), empty); return;
                case "pPr": ReadParagraphProps(); return;
                case "r": Push(new RunFrame(depth), empty); return;
                case "rPr":
                    if (Top is RunFrame run && run.Depth == depth - 1) run.Props = RunProps.Read(x, _chunk);
                    else x.Skip(_chunk);
                    return;
                case "t":
                    if (Top is RunFrame tr) ReadText(tr.Text); else x.Skip(_chunk);
                    return;
                case "tab": AppendToRun('\t'); return;
                case "br":
                case "cr": AppendToRun('\n'); return;
                case "noBreakHyphen": AppendToRun('-'); return;
                case "instrText": counters.FieldCodes++; x.Skip(_chunk); return;
                case "delInstrText": x.Skip(_chunk); return;
                case "fldSimple": counters.FieldCodes++; return;               // the children are the displayed result: read them
                case "del":
                case "moveFrom":
                    if (x.Skip(_chunk)) counters.TrackedDeletions++;            // deleted text never reaches a buffer
                    return;
                case "commentRangeStart":
                case "commentReference":
                    counters.CommentIds.Add(x.Attr("id") ?? "#" + _anonymousComments++);
                    x.Skip(_chunk);
                    return;
                case "footnoteReference":
                case "endnoteReference": counters.NoteReferences++; return;
                case "bdo":
                case "dir": counters.DirectionOverrides++; return;
                case "tr": Push(new RowFrame(depth), empty); return;
                case "tc": Push(new CellFrame(depth), empty); return;
                case "altChunk": throw new DocxRejectedException(DocxFailure.AltChunk);
                default: return;                                              // containers (body, tbl, sdt, hyperlink, ins, smartTag, ...) are walked
            }
        }
        if (ns == Ns.Mc)
        {
            switch (name)
            {
                case "AlternateContent": Push(new AltFrame(depth), empty); return;
                case "Choice":
                    if (Top is AltFrame { BranchTaken: false } alt) alt.BranchTaken = true;   // read the first Choice
                    else x.Skip(_chunk);
                    return;
                case "Fallback":
                    if (Top is AltFrame { BranchTaken: false } alt2) { alt2.BranchTaken = true; return; }   // no Choice: Word shows the Fallback
                    counters.Fallbacks++;
                    x.Skip(_chunk);
                    return;
                default: return;
            }
        }
        if (Ns.IsWp(ns) && name == "docPr")
        {
            if (!string.IsNullOrWhiteSpace(_r.GetAttribute("descr")) || !string.IsNullOrWhiteSpace(_r.GetAttribute("title"))) counters.AltText++;
            return;
        }
        if (ns == Ns.Vml && !string.IsNullOrWhiteSpace(_r.GetAttribute("alt"))) counters.AltText++;
    }

    private Frame? Top => _stack.Count > 0 ? _stack[^1] : null;

    private T? Nearest<T>() where T : Frame
    {
        for (var i = _stack.Count - 1; i >= 0; i--)
            if (_stack[i] is T t) return t;
        return null;
    }

    private void Push(Frame frame, bool empty)
    {
        _stack.Add(frame);
        if (empty) Pop();
    }

    private void Pop()
    {
        var frame = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        switch (frame)
        {
            case RunFrame run: FinishRun(run); break;
            case ParaFrame p:
                if (p.Text.Length > 0) Emit(p.Text.ToString());
                break;
            case CellFrame c:
                var cell = string.Join(' ', c.Paragraphs);
                if (Nearest<RowFrame>() is { } row) row.Cells.Add(cell);
                else if (cell.Length > 0) Emit(cell);                         // a cell outside any row: keep its text
                break;
            case RowFrame rw:
                var line = string.Join('\t', rw.Cells);
                if (line.AsSpan().ContainsAnyExcept('\t')) Emit(line);
                break;
        }
    }

    /// <summary>A finished paragraph or row goes to the enclosing table cell if there is one (nested tables, text boxes in cells), else out.</summary>
    private void Emit(string text)
    {
        if (Nearest<CellFrame>() is { } cell) cell.Paragraphs.Add(text);
        else _out.Add(text);
    }

    private void FinishRun(RunFrame run)
    {
        if (run.Text.Length == 0) return;
        var para = Nearest<ParaFrame>();
        var hidden = styles.IsHidden(run.Props, para?.Style);
        var tiny = !hidden && styles.IsTiny(run.Props, para?.Style);
        if (hidden || tiny)
        {
            if (HasVisibleCharacters(run.Text))
            {
                if (hidden) counters.HiddenRuns++; else counters.TinyRuns++;
            }
            return;
        }
        para?.Text.Append(run.Text);                                        // a run outside any paragraph is not shown by Word
    }

    private static bool HasVisibleCharacters(StringBuilder sb)
    {
        foreach (var chunk in sb.GetChunks())
            if (chunk.Span.ContainsAnyExcept(" \t\r\n")) return true;
        return false;
    }

    private void ReadParagraphProps()
    {
        var para = Top as ParaFrame;
        if (_r.IsEmptyElement) return;
        var depth = _r.Depth;
        while (x.Read())
        {
            if (_r.NodeType == XmlNodeType.EndElement && _r.Depth == depth) return;
            if (_r.NodeType != XmlNodeType.Element) continue;
            // Only pStyle matters. pPr/rPr formats the paragraph mark (a hidden mark does not hide the text); pPrChange is old formatting.
            if (_r.Depth == depth + 1 && Ns.IsW(_r.NamespaceURI) && _r.LocalName == "pStyle" && para is not null) para.Style = x.Attr("val");
            x.Skip(_chunk);
        }
    }

    private void AppendToRun(char c)
    {
        if (Top is not RunFrame run) return;
        Count(1);
        run.Text.Append(c);
    }

    private void ReadText(StringBuilder target)
    {
        if (_r.IsEmptyElement) return;
        var depth = _r.Depth;
        while (x.Read())
        {
            var type = _r.NodeType;
            if (type == XmlNodeType.EndElement && _r.Depth == depth) return;
            if (type is not (XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)) continue;
            if (_r.CanReadValueChunk)
            {
                int n;
                while ((n = _r.ReadValueChunk(_chunk, 0, _chunk.Length)) > 0)
                {
                    Count(n);
                    target.Append(_chunk, 0, n);
                }
            }
            else
            {
                var v = _r.Value;
                Count(v.Length);
                target.Append(v);
            }
        }
    }

    private void Count(int n)
    {
        _chars += n;
        if (_chars > limits.MaxTextCharacters) throw new DocxRejectedException(DocxFailure.TextTooLong);
    }
}
