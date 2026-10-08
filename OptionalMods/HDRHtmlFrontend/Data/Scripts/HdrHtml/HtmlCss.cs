using System;
using System.Collections.Generic;
using System.Text;

namespace Hdr.Html
{
    // A deliberately bounded cascade, not an implementation of the browser CSS grammar.
    public static class HtmlCss
    {
        static readonly HashSet<string> Properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "display", "width", "height", "min-width", "max-width", "min-height", "max-height",
            "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
            "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
            "gap", "row-gap", "column-gap", "flex-direction", "flex-grow", "justify-content", "align-items", "align-self",
            "background", "background-color", "color", "border", "border-width", "border-color", "border-radius",
            "font-size", "font-family", "line-height", "text-align", "white-space", "overflow", "opacity"
        };
        static readonly string[] Inherited = { "color", "font-size", "font-family", "line-height", "text-align", "white-space" };
        static readonly string[] Sides = { "top", "right", "bottom", "left" };

        public static Dictionary<string, string> Resolve(HtmlDocument document, HtmlNode node, IDictionary<string, string> inherited)
        {
            if (document == null || node == null) throw new ArgumentNullException(document == null ? "document" : "node");
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (inherited != null)
                foreach (var declaration in inherited)
                    foreach (string property in Inherited)
                        if (property.Equals(declaration.Key, StringComparison.OrdinalIgnoreCase)) result[property] = declaration.Value;
            if (node.Tag == "#text" || node.Tag == "#document") return result;
            var strengths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var orders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (HtmlCssRule rule in document.Rules)
            {
                if (!Matches(rule.Selector, node)) continue;
                foreach (var declaration in rule.Declarations)
                {
                    int strength, order;
                    if (!strengths.TryGetValue(declaration.Key, out strength) || rule.Specificity > strength ||
                        (rule.Specificity == strength && (!orders.TryGetValue(declaration.Key, out order) || rule.Order >= order)))
                    {
                        result[declaration.Key] = declaration.Value;
                        strengths[declaration.Key] = rule.Specificity;
                        orders[declaration.Key] = rule.Order;
                    }
                }
            }
            foreach (var declaration in node.InlineStyle) result[declaration.Key] = declaration.Value;
            return result;
        }

        internal static void AddRules(HtmlDocument document, string source, HtmlLimits limits, ref int declarationCount)
        {
            string css = StripComments(source ?? "");
            int position = 0;
            while (true)
            {
                SkipSpace(css, ref position);
                if (position == css.Length) break;
                int start = position;
                int open = css.IndexOf('{', position);
                if (open < 0) throw Error("css-syntax", "A CSS rule needs an opening brace", start);
                string selectors = css.Substring(position, open - position).Trim();
                int close = css.IndexOf('}', open + 1);
                if (close < 0 || css.IndexOf('{', open + 1, close < 0 ? 0 : close - open - 1) >= 0)
                    throw Error("css-syntax", "CSS nested or unclosed blocks are unsupported", open);
                var declarations = ParseDeclarations(css.Substring(open + 1, close - open - 1), limits, ref declarationCount);
                string[] selectorList = selectors.Split(',');
                bool first = true;
                foreach (string rawSelector in selectorList)
                {
                    string selector = rawSelector.Trim();
                    int specificity = ValidateSelector(selector, start);
                    if (document.Rules.Count >= limits.MaxRules) throw Error("limit-rules", "CSS rule limit exceeded", start);
                    var rule = new HtmlCssRule { Selector = selector, Specificity = specificity, Order = document.Rules.Count };
                    foreach (var declaration in declarations)
                    {
                        if (!first && ++declarationCount > limits.MaxDeclarations) throw Error("limit-declarations", "CSS declaration limit exceeded", start);
                        rule.Declarations.Add(declaration.Key, declaration.Value);
                    }
                    document.Rules.Add(rule);
                    first = false;
                }
                position = close + 1;
            }
        }

        internal static Dictionary<string, string> ParseDeclarations(string source, HtmlLimits limits, ref int declarationCount)
        {
            string css = StripComments(source ?? "");
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string declaration in SplitOutside(css, ';'))
            {
                if (declaration.Trim().Length == 0) continue;
                int colon = declaration.IndexOf(':');
                if (colon <= 0) throw Error("css-syntax", "A CSS declaration needs a property and value", 0);
                string property = declaration.Substring(0, colon).Trim().ToLowerInvariant();
                string value = declaration.Substring(colon + 1).Trim();
                if (!Properties.Contains(property)) throw Error("css-property", "Unsupported CSS property '" + property + "'", 0);
                ValidateValue(value);
                var expanded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (property == "margin" || property == "padding") ExpandSides(expanded, property, value);
                else if (property == "border") ExpandBorder(expanded, value);
                else expanded[property == "background" ? "background-color" : property] = value;
                foreach (var entry in expanded)
                {
                    if (++declarationCount > limits.MaxDeclarations) throw Error("limit-declarations", "CSS declaration limit exceeded", 0);
                    result[entry.Key] = entry.Value;
                }
            }
            return result;
        }

        static void ExpandSides(Dictionary<string, string> result, string property, string value)
        {
            List<string> values = SplitWhitespaceOutside(value);
            if (values.Count < 1 || values.Count > 4) throw Error("css-value", property + " accepts one to four side values", 0);
            string top = values[0], right = values.Count > 1 ? values[1] : top;
            string bottom = values.Count > 2 ? values[2] : top, left = values.Count > 3 ? values[3] : right;
            string[] all = { top, right, bottom, left };
            for (int i = 0; i < 4; i++) result[property + "-" + Sides[i]] = all[i];
        }

        static void ExpandBorder(Dictionary<string, string> result, string value)
        {
            List<string> parts = SplitWhitespaceOutside(value);
            if (parts.Count == 1 && parts[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                result["border-width"] = "0";
                return;
            }
            if (parts.Count != 3 || !parts[1].Equals("solid", StringComparison.OrdinalIgnoreCase))
                throw Error("css-value", "Border supports 'none' or 'width solid color'", 0);
            result["border-width"] = parts[0];
            result["border-color"] = parts[2];
        }

        static void ValidateValue(string value)
        {
            if (value.Length == 0 || value.Length > 4096) throw Error("css-value", "A CSS value must contain 1 to 4096 characters", 0);
            string lower = value.ToLowerInvariant();
            if (lower.IndexOf("url", StringComparison.Ordinal) >= 0 || lower.IndexOf("!important", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("expression", StringComparison.Ordinal) >= 0 || lower.IndexOf("var(", StringComparison.Ordinal) >= 0 ||
                value.IndexOf('@') >= 0 || value.IndexOf('!') >= 0 || value.IndexOf('\\') >= 0 || value.IndexOf('{') >= 0 || value.IndexOf('}') >= 0)
                throw Error("css-value", "CSS URLs, escapes, variables, directives and important declarations are unsupported", 0);
            // Split validates string and function delimiters even for properties validated by layout.
            SplitOutside(value, ';');
        }

        static int ValidateSelector(string selector, int offset)
        {
            if (selector.Length == 0 || selector.Length > 256) throw Error("css-selector", "CSS selector is empty or too long", offset);
            int position = 0, score = 0, ids = 0;
            if (selector[position] == '*') position++;
            else if (selector[position] != '.' && selector[position] != '#')
            {
                ReadIdentifier(selector, ref position, offset);
                score++;
            }
            while (position < selector.Length)
            {
                char prefix = selector[position++];
                if (prefix != '.' && prefix != '#') throw Error("css-selector", "Only compound element, class and ID selectors are supported", offset);
                ReadIdentifier(selector, ref position, offset);
                if (prefix == '#')
                {
                    if (++ids > 1) throw Error("css-selector", "Only one ID is supported in a compound selector", offset);
                    score += 1000000;
                }
                else score += 100;
            }
            return score;
        }

        static bool Matches(string selector, HtmlNode node)
        {
            int position = 0;
            if (selector[0] == '*') position++;
            else if (selector[0] != '.' && selector[0] != '#')
            {
                int start = position;
                while (position < selector.Length && IdentifierChar(selector[position])) position++;
                if (!selector.Substring(start, position - start).Equals(node.Tag, StringComparison.OrdinalIgnoreCase)) return false;
            }
            while (position < selector.Length)
            {
                char prefix = selector[position++];
                int start = position;
                while (position < selector.Length && IdentifierChar(selector[position])) position++;
                string name = selector.Substring(start, position - start);
                if (prefix == '#' ? !String.Equals(name, node.Id, StringComparison.Ordinal) : !node.Classes.Contains(name)) return false;
            }
            return true;
        }

        static void ReadIdentifier(string selector, ref int position, int offset)
        {
            int start = position;
            while (position < selector.Length && IdentifierChar(selector[position])) position++;
            if (position == start) throw Error("css-selector", "Selector names require ASCII letters, digits, hyphens or underscores", offset);
        }
        static bool IdentifierChar(char c) { return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_'; }

        static string StripComments(string source)
        {
            var result = new StringBuilder(source.Length);
            char quote = '\0';
            for (int i = 0; i < source.Length; i++)
            {
                if (quote != '\0')
                {
                    result.Append(source[i]);
                    if (source[i] == quote) quote = '\0';
                    continue;
                }
                if (source[i] == '\'' || source[i] == '"')
                {
                    quote = source[i];
                    result.Append(source[i]);
                    continue;
                }
                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    int close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0) throw Error("css-syntax", "Unclosed CSS comment", i);
                    result.Append(' ');
                    i = close + 1;
                }
                else result.Append(source[i]);
            }
            return result.ToString();
        }

        static List<string> SplitOutside(string source, char separator)
        {
            var result = new List<string>();
            int start = 0, depth = 0;
            char quote = '\0';
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"') { quote = c; continue; }
                if (c == '(') { if (++depth > 1) throw Error("css-syntax", "Nested CSS functions are unsupported", i); }
                else if (c == ')') { if (--depth < 0) throw Error("css-syntax", "Unbalanced CSS function", i); }
                else if (c == separator && depth == 0) { result.Add(source.Substring(start, i - start)); start = i + 1; }
            }
            if (quote != '\0' || depth != 0) throw Error("css-syntax", "Unclosed CSS string or function", source.Length);
            result.Add(source.Substring(start));
            return result;
        }

        static List<string> SplitWhitespaceOutside(string source)
        {
            var result = new List<string>();
            int start = -1, depth = 0;
            char quote = '\0';
            for (int i = 0; i <= source.Length; i++)
            {
                char c = i < source.Length ? source[i] : ' ';
                if (start < 0 && !Char.IsWhiteSpace(c)) start = i;
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"') { quote = c; continue; }
                if (c == '(') depth++;
                else if (c == ')') depth--;
                if (Char.IsWhiteSpace(c) && depth == 0 && start >= 0)
                {
                    result.Add(source.Substring(start, i - start));
                    start = -1;
                }
            }
            return result;
        }
        static void SkipSpace(string source, ref int position) { while (position < source.Length && Char.IsWhiteSpace(source[position])) position++; }
        static HtmlParseException Error(string code, string message, int offset) { return new HtmlParseException(code, message, offset); }
    }
}
