using System;
using System.Collections.Generic;
using Hdr.Html;

// Independent behavioral fixtures. Does not read renderer/layout sources or run a game.
internal static class HtmlParserChecks
{
    private static int _assertions, _groups;
    private static readonly List<string> Failures = new List<string>();

    public static int Main()
    {
        Run("tree and identity", TreeAndIdentity);
        Run("entities", Entities);
        Run("strict markup rejection", StrictMarkupRejection);
        Run("safe attributes", SafeAttributes);
        Run("resource limits", ResourceLimits);
        Run("cascade", Cascade);
        Run("inheritance", Inheritance);
        Run("shorthand", Shorthand);
        Run("unsupported CSS", UnsupportedCss);
        Console.WriteLine("HTML parser/cascade: " + _assertions + " assertions, " + _groups + " groups, " + Failures.Count + " failures.");
        foreach (string failure in Failures) Console.WriteLine("FAIL: " + failure);
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        _groups++;
        try { test(); }
        catch (Exception ex) { Failures.Add(name + ": " + ex.GetType().Name + ": " + ex.Message); }
    }

    private static void Assert(bool condition, string reason)
    {
        _assertions++;
        if (!condition) Failures.Add(reason);
    }

    private static void Equal<T>(T expected, T actual, string reason)
    {
        Assert(EqualityComparer<T>.Default.Equals(expected, actual), reason + ": expected [" + expected + "], actual [" + actual + "]");
    }

    private static HtmlDocument Parse(string markup, string css = "", HtmlLimits limits = null)
    {
        return HtmlParser.Parse(markup, css, limits ?? new HtmlLimits());
    }

    private static void Reject(string markup, string css, string reason, HtmlLimits limits = null)
    {
        _assertions++;
        try { Parse(markup, css, limits); }
        catch (HtmlParseException) { return; }
        catch (Exception ex) { Failures.Add("Forbidden input threw " + ex.GetType().Name + " rather than HtmlParseException: " + reason); return; }
        Failures.Add("Accepted forbidden input: " + reason);
    }

    private static IDictionary<string, string> Style(HtmlDocument doc, string id, IDictionary<string, string> inherited = null)
    {
        return HtmlCss.Resolve(doc, doc.ById[id], inherited);
    }

    private static void Property(IDictionary<string, string> style, string name, string expected, string reason)
    {
        string actual;
        Assert(style.TryGetValue(name, out actual), reason + " contains " + name);
        Equal(expected, actual, reason + " " + name);
    }

    private static void TreeAndIdentity()
    {
        HtmlDocument doc = Parse("<div id='panel' class='panel active'><span id='label'>A</span><br><span id='second'>B</span></div>");
        Equal("#document", doc.Root.Tag, "synthetic root");
        Equal(1, doc.Root.Children.Count, "root child count");
        HtmlNode panel = doc.ById["panel"];
        Assert(object.ReferenceEquals(doc.Root, panel.Parent), "parent link points to document");
        Equal(3, panel.Children.Count, "void child does not swallow next span");
        Equal("br", panel.Children[1].Tag, "void tag retained");
        Equal("#text", doc.ById["label"].Children[0].Tag, "text is explicit node");
        Equal("A", doc.ById["label"].Children[0].Text, "text payload");
        Equal(2, panel.Classes.Count, "class token count");
        Equal("panel", panel.Classes[0], "first class");
        Equal("active", panel.Classes[1], "second class");
        HashSet<int> ordinals = new HashSet<int>();
        CollectOrdinals(doc.Root, ordinals);
        Equal(7, ordinals.Count, "all nodes get unique ordinals including synthetic and text nodes");
        HtmlDocument selfClosing = Parse("<div><br/><input type='range' min='0' max='10' value='4'/><span>after</span></div>");
        Equal(3, selfClosing.Root.Children[0].Children.Count, "self-closing void syntax");
        HtmlDocument caseIds = Parse("<div id='A'></div><div id='a'></div>");
        Equal(2, caseIds.ById.Count, "IDs are case-sensitive");
    }

    private static void CollectOrdinals(HtmlNode node, HashSet<int> ordinals)
    {
        Assert(ordinals.Add(node.Ordinal), "unique ordinal " + node.Ordinal);
        foreach (HtmlNode child in node.Children)
        {
            Assert(object.ReferenceEquals(node, child.Parent), "child parent reference");
            CollectOrdinals(child, ordinals);
        }
    }

    private static void Entities()
    {
        HtmlDocument doc = Parse("<div id='a' class='A &amp; B'>&lt;&gt;&amp;&quot;&apos;&#65;&#x41;&#x1F642;&#128578;</div>");
        Equal("<>&\"'AA\U0001F642\U0001F642", doc.ById["a"].Children[0].Text, "named/decimal/hex astral entities");
        Equal("A & B", doc.ById["a"].Attributes["class"], "attributes decode entities once");
        Equal("&lt;", Parse("<div>&amp;lt;</div>").Root.Children[0].Children[0].Text, "decoded entities are not reparsed");
        Equal("A\u00a0B", Parse("<div>A&nbsp;B</div>").Root.Children[0].Children[0].Text, "nonbreaking space entity");
        Equal("\U0001F642", Parse("<div>\U0001F642</div>").Root.Children[0].Children[0].Text, "literal astral Unicode");
        string[] invalid = { "&unknown;", "&#;", "&#x;", "&#xD800;", "&#x110000;", "&#0;", "&#-1;", "&#999999999999999999999;", "&#xZZ;", "&amp" };
        foreach (string value in invalid) Reject("<div>" + value + "</div>", "", "invalid entity " + value);
        Reject("<div>" + new string((char)0xD800, 1) + "</div>", "", "unpaired literal high surrogate");
        Reject("<div>" + new string((char)0xDC00, 1) + "</div>", "", "unpaired literal low surrogate");
        Reject("<div>" + new string((char)0, 1) + "</div>", "", "literal null control");
        Reject("<div></div>", "div{color:" + new string((char)0xD800, 1) + "}", "unpaired stylesheet surrogate");
    }

    private static void StrictMarkupRejection()
    {
        string[] invalid =
        {
            "<div><span></div></span>", "<div>", "</div>", "<div><span></span>",
            "<div id='same'></div><span id='same'></span>", "<br></br>",
            "<div id='a' id='b'></div>", "<div class='x' CLASS='y'></div>",
            "<script>alert(1)</script>", "<iframe></iframe>", "<video></video>", "<canvas></canvas>",
            "<div onclick='act()'></div>", "<div ONMOUSEMOVE='act()'></div>",
            "<div style='background-image: url(https://example.test/image.png)'></div>",
            "<img src='https://example.test/image.png'>", "<a href='https://example.test'>A</a>",
            "<div src='x'></div>", "<div title='unsupported tooltip'></div>", "<div unknown='x'></div>", "<div id='unterminated></div>"
        };
        foreach (string markup in invalid) Reject(markup, "", markup);
    }

    private static void SafeAttributes()
    {
        HtmlDocument doc = Parse("<div id='box' class='one two'><label id='label'>Gain</label><input id='gain' type='range' min='-2.5' max='9.5' step='0.25' value='2' data-bind='gain' data-action='gain-change' disabled></div>");
        HtmlNode input = doc.ById["gain"];
        Equal("input", input.Tag, "input tag");
        foreach (string name in new[] { "type", "min", "max", "step", "value", "data-bind", "data-action", "disabled" })
            Assert(input.Attributes.ContainsKey(name), "safe attribute " + name);
        Equal("-2.5", input.Attributes["min"], "negative finite range text");
        Equal("0.25", input.Attributes["step"], "fractional step text");
        Equal("gain", input.Attributes["data-bind"], "safe binding token");
        Equal("gain-change", input.Attributes["data-action"], "safe action token");
        HtmlDocument escaped = Parse("<button id='button' data-action='&quot;quoted&quot;'>Apply</button>");
        Equal("\"quoted\"", escaped.ById["button"].Attributes["data-action"], "quoted attribute decoding");
        Reject("<input type='password'>", "", "unsupported input type");
        Reject("<input>", "", "missing input type");
        // Numeric semantics belong to layout admission, not the markup parser.
        HtmlDocument deferred = Parse("<input id='deferred' type='range' min='NaN' max='Infinity' step='-1'>");
        Equal("NaN", deferred.ById["deferred"].Attributes["min"], "numeric semantics deferred to layout");
        HtmlDocument booleanBefore = Parse("<button disabled id='b' data-action='apply'>Apply</button>");
        Assert(booleanBefore.ById["b"].Attributes.ContainsKey("disabled"), "boolean attribute retains following attribute boundary");
        Equal("apply", booleanBefore.ById["b"].Attributes["data-action"], "attribute following boolean flag");
    }

    private static void ResourceLimits()
    {
        HtmlLimits chars = new HtmlLimits(); chars.MaxCharacters = 20;
        Parse("<div>A</div>", "", chars);
        Reject("<div>A</div>", "div { color: red; }", "combined markup and stylesheet source limit", chars);
        HtmlLimits nodes = new HtmlLimits(); nodes.MaxNodes = 2;
        Parse("<div></div>", "", nodes);
        Reject("<div><span>A</span></div>", "", "element and text node admission", nodes);
        HtmlLimits depth = new HtmlLimits(); depth.MaxDepth = 2;
        Parse("<div></div>", "", depth);
        Reject("<div><span><div></div></span></div>", "", "nested depth admission", depth);
        HtmlLimits text = new HtmlLimits(); text.MaxTextCharacters = 4;
        Parse("<div>ABCD</div>", "", text);
        Reject("<div>ABC</div><span>DE</span>", "", "global decoded text budget", text);
        HtmlLimits rules = new HtmlLimits(); rules.MaxRules = 1;
        Parse("<div></div>", "div { color: red; }", rules);
        Reject("<div></div>", "div { color: red; } span { color: blue; }", "stylesheet rule count", rules);
        HtmlLimits declarations = new HtmlLimits(); declarations.MaxDeclarations = 1;
        Parse("<div></div>", "div { color: red; }", declarations);
        Reject("<div></div>", "div { color: red; background-color: blue; }", "stylesheet declarations", declarations);
        Reject("<div style='color:red;background-color:blue'></div>", "", "inline declarations", declarations);
        Reject("<div style='color:red'></div>", "div{background-color:blue}", "combined inline and stylesheet declarations", declarations);
        HtmlLimits expanded = new HtmlLimits(); expanded.MaxDeclarations = 3;
        Reject("<div></div>", "div { margin: 1px; }", "expanded shorthand charged by canonical property count", expanded);
        HtmlLimits amplified = new HtmlLimits(); amplified.MaxDeclarations = 1;
        Reject("<div></div>", "div, span { color: red; }", "comma selectors charge materialized declaration copies", amplified);
        HtmlLimits noNodes = new HtmlLimits(); noNodes.MaxNodes = 0;
        Reject("<div></div>", "", "invalid zero node grant", noNodes);
        HtmlLimits tooBig = new HtmlLimits(); tooBig.MaxCharacters = 1048577;
        Reject("<div></div>", "", "absolute source grant ceiling", tooBig);
        Reject("<div class='" + new string('A', 4097) + "'></div>", "", "attribute value absolute ceiling");
        Reject("<div id='" + new string('A', 129) + "'></div>", "", "identifier absolute ceiling");
    }

    private static void Cascade()
    {
        string markup = "<div id='target' class='panel active'></div><span id='other' class='active'></span><div id='inactive' class='panel'></div>";
        HtmlDocument compound = Parse(markup, "div.panel.active { color: red; } .panel { background-color: black; } span.active, #inactive { color: blue; }");
        Property(Style(compound, "target"), "color", "red", "all compound tokens match");
        Property(Style(compound, "other"), "color", "blue", "comma selector first branch");
        Property(Style(compound, "inactive"), "color", "blue", "comma selector second branch");
        HtmlDocument specificity = Parse(markup, "#target { color: red; } div.panel.active { color: blue; } .active { color: green; } div { color: black; }");
        Property(Style(specificity, "target"), "color", "red", "ID specificity defeats later class/tag rules");
        Property(Style(specificity, "other"), "color", "green", "class specificity defeats tag");
        HtmlDocument sourceOrder = Parse(markup, ".active { color: red; } .panel { color: green; } .active { color: blue; }");
        Property(Style(sourceOrder, "target"), "color", "blue", "equal specificity uses last source order");
        HtmlDocument inline = Parse("<div id='target' class='panel' style='color: green'></div>", "#target { color: red; } .panel { color: blue; }");
        Property(Style(inline, "target"), "color", "green", "inline defeats selector rules");
        HtmlDocument duplicates = Parse("<div id='target' style='color: red; color: blue'></div>");
        Property(Style(duplicates, "target"), "color", "blue", "last declaration within inline style");
        HtmlDocument idCompound = Parse(markup, "div#target.panel.active { color: purple; } #target { color: red; }");
        Property(Style(idCompound, "target"), "color", "purple", "ID compound specificity includes tag/classes");
        HtmlDocument comments = Parse("<!-- explanation --><div id='target'></div>", "/* explanation */ div { color: /* inner */ red; }");
        Property(Style(comments, "target"), "color", "red", "markup and stylesheet comments");
        HtmlDocument embedded = Parse("<!DOCTYPE html><html><head><title>Safe title</title><style type='text/css'>#target { color:red; }</style></head><body><div id='target'></div></body></html>", "#target { color:blue; }");
        Property(Style(embedded, "target"), "color", "blue", "external stylesheet follows embedded style source order");
        IDictionary<string, string> detached = Style(embedded, "target");
        detached["color"] = "green";
        Property(Style(embedded, "target"), "color", "blue", "resolved style is independently owned");
        string[] repeatedClasses = new string[120];
        for (int i = 0; i < repeatedClasses.Length; i++) repeatedClasses[i] = ".a";
        HtmlDocument manyClasses = Parse("<div id='target' class='a'></div>", "#target{color:red}" + String.Concat(repeatedClasses) + "{color:blue}");
        Property(Style(manyClasses, "target"), "color", "red", "one ID outranks many duplicate class terms");
    }

    private static void Inheritance()
    {
        HtmlDocument doc = Parse("<div id='parent'><span id='child'></span><span id='override' style='color: blue'></span></div>", "#parent { color: red; font-size: 20px; line-height: 1.5; background-color: black; margin-left: 12px; opacity: 0.5; }");
        IDictionary<string, string> parent = Style(doc, "parent");
        IDictionary<string, string> child = Style(doc, "child", parent);
        Property(child, "color", "red", "inherited foreground");
        Property(child, "font-size", "20px", "inherited font size");
        Property(child, "line-height", "1.5", "inherited line height");
        Assert(!child.ContainsKey("background-color") || child["background-color"] != "black", "background does not inherit");
        Assert(!child.ContainsKey("margin-left") || child["margin-left"] != "12px", "margin does not inherit");
        Assert(!child.ContainsKey("opacity") || child["opacity"] != "0.5", "opacity not a directly inherited CSS property");
        Property(Style(doc, "override", parent), "color", "blue", "child declared property wins over inheritance");
        // Caller-provided inherited dictionaries must not inject noninherited geometry.
        Dictionary<string, string> supplied = new Dictionary<string, string>();
        supplied["color"] = "purple"; supplied["width"] = "900px"; supplied["position"] = "absolute";
        IDictionary<string, string> filtered = Style(doc, "child", supplied);
        Property(filtered, "color", "purple", "inherited dictionary whitelist");
        Assert(!filtered.ContainsKey("width") || filtered["width"] != "900px", "supplied width is filtered");
        Assert(!filtered.ContainsKey("position") || filtered["position"] != "absolute", "supplied positioning is filtered");
        Equal(3, supplied.Count, "Resolve leaves supplied inheritance dictionary intact");
        Dictionary<string, string> caseSensitiveCaller = new Dictionary<string, string>();
        caseSensitiveCaller["COLOR"] = "purple"; caseSensitiveCaller["WIDTH"] = "700px";
        IDictionary<string, string> upper = Style(doc, "child", caseSensitiveCaller);
        Property(upper, "color", "purple", "case insensitive inherited CSS names from case sensitive caller");
        Assert(!upper.ContainsKey("width") || upper["width"] != "700px", "case insensitive inheritance still filters geometry");
    }

    private static void Shorthand()
    {
        string[] shorthands = { "1px", "1px 2px", "1px 2px 3px", "1px 2px 3px 4px" };
        string[][] sides =
        {
            new[] { "1px", "1px", "1px", "1px" },
            new[] { "1px", "2px", "1px", "2px" },
            new[] { "1px", "2px", "3px", "2px" },
            new[] { "1px", "2px", "3px", "4px" }
        };
        string[] sideNames = { "top", "right", "bottom", "left" };
        for (int i = 0; i < shorthands.Length; i++)
        {
            HtmlDocument doc = Parse("<div id='x' style='margin:" + shorthands[i] + ";padding:" + shorthands[i] + "'></div>");
            IDictionary<string, string> style = Style(doc, "x");
            for (int j = 0; j < 4; j++)
            {
                Property(style, "margin-" + sideNames[j], sides[i][j], "margin expansion " + i);
                Property(style, "padding-" + sideNames[j], sides[i][j], "padding expansion " + i);
            }
        }
        HtmlDocument order = Parse("<div id='x' style='margin-left:9px;margin:1px 2px;margin-top:7px'></div>");
        IDictionary<string, string> ordered = Style(order, "x");
        Property(ordered, "margin-left", "2px", "later shorthand overrides earlier longhand");
        Property(ordered, "margin-top", "7px", "later longhand overrides earlier shorthand");
        HtmlDocument border = Parse("<div id='x' style='border:2px solid red'></div>");
        Property(Style(border, "x"), "border-width", "2px", "border width normalization");
        Property(Style(border, "x"), "border-color", "red", "border color normalization");
        Reject("<div style='margin:1px 2px 3px 4px 5px'></div>", "", "too many shorthand sides");
        Reject("<div style='border:2px dotted red'></div>", "", "unsupported border style");
    }

    private static void UnsupportedCss()
    {
        string[] sheets =
        {
            "div span {color:red}", "div > span {color:red}", "div + span {color:red}", "div ~ span {color:red}",
            "div:hover {color:red}", "div::before {color:red}", "[id=x] {color:red}",
            "@import 'https://example.test/x.css';", "@media screen {div{color:red}}", "@font-face {font-family:x}",
            "div { color: red !important; }", "div { background-image: url('asset'); }", "div { transform: rotate(2deg); }",
            "div { animation: flash 1s; }", "div { unknown-prop: 1; }", "div { color red; }", "div { color: red;", "div color:red; }"
        };
        foreach (string sheet in sheets) Reject("<div id='x'></div>", sheet, sheet);
        Reject("<div style='color:red !important'></div>", "", "inline important");
        Reject("<div style='color:var(--theme)'></div>", "", "unsupported custom-variable expression");
    }
}
