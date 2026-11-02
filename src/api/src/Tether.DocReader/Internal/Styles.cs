using System.Globalization;
using System.Xml;

namespace Tether.DocReader.Internal;

/// <summary>The run properties that decide whether text is hidden: <c>w:vanish</c>/<c>w:specVanish</c>, <c>w:sz</c> and <c>w:rStyle</c>.</summary>
internal readonly record struct RunProps(bool? Vanish, int? HalfPoints, string? CharStyle)
{
    /// <summary>At an <c>rPr</c> start: reads its direct children only (so the old formatting inside <c>w:rPrChange</c> is ignored).</summary>
    public static RunProps Read(XmlCursor x, char[] scratch)
    {
        var r = x.R;
        if (r.IsEmptyElement) return default;
        var depth = r.Depth;
        bool? vanish = null; int? sz = null; string? style = null;
        while (x.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) break;
            if (r.NodeType != XmlNodeType.Element) continue;
            if (r.Depth != depth + 1 || !Ns.IsW(r.NamespaceURI)) { x.Skip(scratch); continue; }
            switch (r.LocalName)
            {
                case "vanish":
                case "specVanish":
                    var on = OnOff(x.Attr("val"));
                    vanish = vanish == true || on;          // either element set to true hides the run
                    break;
                case "sz":
                    if (int.TryParse(x.Attr("val"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) sz = v;
                    break;
                case "rStyle":
                    style = x.Attr("val");
                    break;
            }
            x.Skip(scratch);
        }
        return new RunProps(vanish, sz, style);
    }

    /// <summary>ST_OnOff: absent means on; "0", "false" and "off" are off; anything else is on.</summary>
    public static bool OnOff(string? value) => value is null || !(value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Just enough of <c>styles.xml</c> to resolve hidden and tiny text set through a style: document defaults, the default paragraph style,
/// and each style's <c>basedOn</c> chain (cycle-safe, at most 32 links). Direct run formatting wins over the character style, which wins
/// over the paragraph style, which wins over the defaults. Simplification: <c>vanish</c> is a toggle property in ECMA-376 (two hidden
/// styles in one chain cancel out); here the nearest setting wins, which can only over-drop, and every drop is counted.
/// </summary>
internal sealed class StyleTable
{
    private const int MaxChain = 32;
    private readonly Dictionary<string, (string? BasedOn, RunProps Props)> _styles = new(StringComparer.Ordinal);
    private RunProps _defaults;
    private string? _defaultParagraphStyle;

    public static StyleTable Empty { get; } = new();

    public static StyleTable Parse(XmlCursor x, char[] scratch)
    {
        var table = new StyleTable();
        var r = x.R;
        while (x.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            if (r.Depth == 0)
            {
                if (r.LocalName != "styles" || !Ns.IsW(r.NamespaceURI)) { x.Skip(scratch); return table; }
                continue;
            }
            if (r.Depth != 1 || !Ns.IsW(r.NamespaceURI)) { x.Skip(scratch); continue; }
            if (r.LocalName == "docDefaults") table.ReadDefaults(x, scratch);
            else if (r.LocalName == "style") table.ReadStyle(x, scratch);
            else x.Skip(scratch);
        }
        return table;
    }

    private void ReadDefaults(XmlCursor x, char[] scratch)
    {
        var r = x.R;
        if (r.IsEmptyElement) return;
        var depth = r.Depth;
        while (x.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) return;
            if (r.NodeType != XmlNodeType.Element) continue;
            if (r.Depth == depth + 1 && r.LocalName == "rPrDefault" && Ns.IsW(r.NamespaceURI)) continue;   // descend
            if (r.Depth == depth + 2 && r.LocalName == "rPr" && Ns.IsW(r.NamespaceURI)) { _defaults = RunProps.Read(x, scratch); continue; }
            x.Skip(scratch);
        }
    }

    private void ReadStyle(XmlCursor x, char[] scratch)
    {
        var r = x.R;
        var id = x.Attr("styleId");
        var type = x.Attr("type");
        var isDefault = x.Attr("default") is { } d && RunProps.OnOff(d);
        string? basedOn = null; RunProps props = default;
        if (!r.IsEmptyElement)
        {
            var depth = r.Depth;
            while (x.Read())
            {
                if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) break;
                if (r.NodeType != XmlNodeType.Element) continue;
                if (r.Depth == depth + 1 && Ns.IsW(r.NamespaceURI) && r.LocalName == "basedOn") basedOn = x.Attr("val");
                else if (r.Depth == depth + 1 && Ns.IsW(r.NamespaceURI) && r.LocalName == "rPr") { props = RunProps.Read(x, scratch); continue; }
                x.Skip(scratch);
            }
        }
        if (id is null) return;
        _styles.TryAdd(id, (basedOn, props));                 // first definition wins, as Word does
        if (isDefault && type == "paragraph") _defaultParagraphStyle ??= id;
    }

    private T? FromStyle<T>(string? id, Func<RunProps, T?> pick) where T : struct
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (id is not null && seen.Count < MaxChain && seen.Add(id) && _styles.TryGetValue(id, out var s))
        {
            if (pick(s.Props) is { } v) return v;
            id = s.BasedOn;
        }
        return null;
    }

    public bool IsHidden(RunProps run, string? paragraphStyle) =>
        (run.Vanish ?? FromStyle(run.CharStyle, p => p.Vanish) ?? FromStyle(paragraphStyle ?? _defaultParagraphStyle, p => p.Vanish) ?? _defaults.Vanish) == true;

    /// <summary>Under 1 pt: <c>w:sz</c> is in half-points, so below 2.</summary>
    public bool IsTiny(RunProps run, string? paragraphStyle) =>
        (run.HalfPoints ?? FromStyle(run.CharStyle, p => p.HalfPoints) ?? FromStyle(paragraphStyle ?? _defaultParagraphStyle, p => p.HalfPoints) ?? _defaults.HalfPoints) is < 2;
}
