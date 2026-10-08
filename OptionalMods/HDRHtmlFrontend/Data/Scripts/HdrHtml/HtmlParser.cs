using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hdr.Html
{
    public static class HtmlParser
    {
        static readonly HashSet<string> Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "html", "head", "style", "title", "body", "div", "section", "main", "header", "footer",
            "p", "span", "h1", "h2", "h3", "br", "button", "label", "input"
        };
        const int MaxAttributesPerNode = 16, MaxAttributeCharacters = 4096;

        public static HtmlDocument Parse(string markup)
        {
            return Parse(markup, "", new HtmlLimits());
        }

        public static HtmlDocument Parse(string markup, string stylesheet)
        {
            return Parse(markup, stylesheet, new HtmlLimits());
        }

        public static HtmlDocument Parse(string markup, string stylesheet, HtmlLimits limits)
        {
            if (markup == null) throw new ArgumentNullException("markup");
            if (limits == null) throw new ArgumentNullException("limits");
            ValidateLimits(limits);
            stylesheet = stylesheet ?? "";
            if ((long)markup.Length + stylesheet.Length > limits.MaxCharacters)
                throw Error("limit-characters", "Combined HTML and CSS character limit exceeded", 0);
            ValidateCharacters(markup, 0);
            ValidateCharacters(stylesheet, 0);
            return new Reader(markup, stylesheet, limits).Read();
        }

        static void ValidateLimits(HtmlLimits limits)
        {
            if (limits.MaxCharacters < 1 || limits.MaxCharacters > 1048576 || limits.MaxNodes < 1 || limits.MaxNodes > 8192 ||
                limits.MaxDepth < 1 || limits.MaxDepth > 128 || limits.MaxRules < 1 || limits.MaxRules > 4096 ||
                limits.MaxDeclarations < 1 || limits.MaxDeclarations > 32768 || limits.MaxTextCharacters < 1 || limits.MaxTextCharacters > 262144)
                throw Error("limits", "Parse limits must be positive and within the profile's absolute ceilings", 0);
        }

        sealed class Reader
        {
            readonly string source, stylesheet;
            readonly HtmlLimits limits;
            readonly HtmlDocument document = new HtmlDocument();
            readonly List<HtmlNode> stack = new List<HtmlNode>();
            int position, nodes, textCharacters, declarations;

            public Reader(string source, string stylesheet, HtmlLimits limits)
            {
                this.source = source;
                this.stylesheet = stylesheet;
                this.limits = limits;
            }

            public HtmlDocument Read()
            {
                document.Root = new HtmlNode { Tag = "#document", Ordinal = -1 };
                stack.Add(document.Root);
                while (position < source.Length)
                {
                    if (source[position] != '<') ReadText();
                    else if (At("<!--")) ReadComment();
                    else if (AtIgnoreCase("<!doctype")) ReadDoctype();
                    else if (At("</")) ReadClose();
                    else ReadOpen();
                }
                if (stack.Count != 1) throw Error("html-nesting", "Unclosed element '" + stack[stack.Count - 1].Tag + "'", position);
                HtmlCss.AddRules(document, stylesheet, limits, ref declarations);
                return document;
            }

            void ReadText()
            {
                int start = position;
                int next = source.IndexOf('<', position);
                position = next < 0 ? source.Length : next;
                string text = Decode(source.Substring(start, position - start), start);
                if (text.Length == 0) return;
                AddTextCharacters(text.Length, start);
                Add(new HtmlNode { Tag = "#text", Text = text }, start);
            }

            void ReadComment()
            {
                int close = source.IndexOf("-->", position + 4, StringComparison.Ordinal);
                if (close < 0) throw Error("html-comment", "Unclosed HTML comment", position);
                position = close + 3;
            }

            void ReadDoctype()
            {
                int close = source.IndexOf('>', position + 2);
                if (close < 0 || !source.Substring(position, close - position + 1).Equals("<!doctype html>", StringComparison.OrdinalIgnoreCase))
                    throw Error("html-doctype", "Only <!DOCTYPE html> is supported", position);
                position = close + 1;
            }

            void ReadClose()
            {
                int start = position;
                position += 2;
                string tag = ReadName().ToLowerInvariant();
                SkipSpace();
                if (position == source.Length || source[position++] != '>') throw Error("html-syntax", "Invalid closing element", start);
                if (tag == "input" || tag == "br") throw Error("html-nesting", "Void elements cannot have closing tags", start);
                if (stack.Count == 1 || stack[stack.Count - 1].Tag != tag)
                    throw Error("html-nesting", "Closing element '" + tag + "' does not match the current element", start);
                stack.RemoveAt(stack.Count - 1);
            }

            void ReadOpen()
            {
                int start = position++;
                string tag = ReadName().ToLowerInvariant();
                if (!Tags.Contains(tag)) throw Error("html-tag", "Unsupported element '" + tag + "'", start);
                var node = new HtmlNode { Tag = tag };
                bool selfClosing = false;
                while (true)
                {
                    int beforeSpace = position;
                    SkipSpace();
                    if (position == source.Length) throw Error("html-syntax", "Unclosed element start", start);
                    if (source[position] == '>') { position++; break; }
                    if (source[position] == '/' && position + 1 < source.Length && source[position + 1] == '>')
                    { position += 2; selfClosing = true; break; }
                    if (position == beforeSpace) throw Error("html-syntax", "Attributes require whitespace separation", position);
                    int attributeOffset = position;
                    string attribute = ReadName().ToLowerInvariant();
                    if (node.Attributes.Count >= MaxAttributesPerNode) throw Error("limit-attributes", "Element attribute limit exceeded", attributeOffset);
                    if (node.Attributes.ContainsKey(attribute)) throw Error("html-attribute", "Duplicate attribute '" + attribute + "'", attributeOffset);
                    int afterName = position;
                    SkipSpace();
                    string value = "";
                    bool hasValue = position < source.Length && source[position] == '=';
                    if (hasValue)
                    {
                        position++;
                        SkipSpace();
                        value = ReadAttributeValue(attributeOffset);
                    }
                    else
                    {
                        if (attribute != "disabled") throw Error("html-attribute", "Attribute '" + attribute + "' requires a value", attributeOffset);
                        position = afterName;
                    }
                    ValidateAttribute(tag, attribute, value, attributeOffset);
                    node.Attributes.Add(attribute, value);
                }
                if (tag == "input")
                {
                    string type;
                    if (!node.Attributes.TryGetValue("type", out type) || !type.Equals("range", StringComparison.OrdinalIgnoreCase))
                        throw Error("html-input", "Only input type='range' is supported", start);
                }
                if (tag != "input" && tag != "br" && selfClosing)
                    throw Error("html-nesting", "Only input and br are void/self-closing elements", start);
                string id, classes, inline;
                if (node.Attributes.TryGetValue("id", out id))
                {
                    if (id.Length == 0 || ContainsSpace(id) || id.Length > 128) throw Error("html-id", "IDs need 1 to 128 characters without whitespace", start);
                    if (document.ById.ContainsKey(id)) throw Error("html-id", "Duplicate ID '" + id + "'", start);
                    node.Id = id;
                    document.ById.Add(id, node);
                }
                if (node.Attributes.TryGetValue("class", out classes))
                    foreach (string name in classes.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (name.Length > 128 || node.Classes.Count >= 32) throw Error("limit-classes", "Class names or class count exceed the profile limit", start);
                        if (!node.Classes.Contains(name)) node.Classes.Add(name);
                    }
                if (node.Attributes.TryGetValue("style", out inline))
                    foreach (var declaration in HtmlCss.ParseDeclarations(inline, limits, ref declarations)) node.InlineStyle.Add(declaration.Key, declaration.Value);
                Add(node, start);
                if (tag == "style")
                {
                    ReadStyle(node, start);
                    return;
                }
                if (tag != "input" && tag != "br")
                {
                    if (stack.Count > limits.MaxDepth) throw Error("limit-depth", "HTML nesting depth exceeded", start);
                    stack.Add(node);
                }
            }

            void ReadStyle(HtmlNode node, int start)
            {
                int close = source.IndexOf("</style", position, StringComparison.OrdinalIgnoreCase);
                if (close < 0) throw Error("html-nesting", "Unclosed style element", start);
                node.Text = source.Substring(position, close - position);
                HtmlCss.AddRules(document, node.Text, limits, ref declarations);
                position = close + 7;
                SkipSpace();
                if (position == source.Length || source[position++] != '>') throw Error("html-syntax", "Invalid closing style element", close);
            }

            void Add(HtmlNode node, int offset)
            {
                if (nodes >= limits.MaxNodes) throw Error("limit-nodes", "HTML node limit exceeded", offset);
                if (stack.Count > limits.MaxDepth) throw Error("limit-depth", "HTML nesting depth exceeded", offset);
                node.Ordinal = nodes++;
                node.Parent = stack[stack.Count - 1];
                node.Parent.Children.Add(node);
            }

            void AddTextCharacters(int count, int offset)
            {
                if ((long)textCharacters + count > limits.MaxTextCharacters) throw Error("limit-text", "Decoded HTML text limit exceeded", offset);
                textCharacters += count;
            }

            string ReadName()
            {
                int start = position;
                while (position < source.Length && NameChar(source[position])) position++;
                if (start == position) throw Error("html-syntax", "Expected an element or attribute name", start);
                return source.Substring(start, position - start);
            }

            string ReadAttributeValue(int offset)
            {
                if (position == source.Length) throw Error("html-syntax", "Missing attribute value", offset);
                int start;
                string value;
                char quote = source[position];
                if (quote == '\'' || quote == '"')
                {
                    start = ++position;
                    int close = source.IndexOf(quote, position);
                    if (close < 0) throw Error("html-syntax", "Unclosed attribute string", offset);
                    value = source.Substring(start, close - start);
                    position = close + 1;
                }
                else
                {
                    start = position;
                    while (position < source.Length && !Char.IsWhiteSpace(source[position]) && source[position] != '>')
                    {
                        char c = source[position];
                        if (c == '\'' || c == '"' || c == '<' || c == '=' || c == '`') throw Error("html-syntax", "Invalid unquoted attribute", position);
                        if (c == '/' && position + 1 < source.Length && source[position + 1] == '>') break;
                        position++;
                    }
                    value = source.Substring(start, position - start);
                    if (value.Length == 0) throw Error("html-syntax", "Missing attribute value", offset);
                }
                if (value.Length > MaxAttributeCharacters) throw Error("limit-attributes", "Attribute value is too long", offset);
                return Decode(value, start);
            }

            void SkipSpace() { while (position < source.Length && Char.IsWhiteSpace(source[position])) position++; }
            bool At(string value) { return source.Length - position >= value.Length && String.CompareOrdinal(source, position, value, 0, value.Length) == 0; }
            bool AtIgnoreCase(string value) { return source.Length - position >= value.Length && String.Compare(source, position, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0; }
        }

        static void ValidateAttribute(string tag, string name, string value, int offset)
        {
            bool global = name == "id" || name == "class" || name == "style" || name == "data-bind" || name == "data-action";
            bool supported = global || tag == "label" && name == "for" || tag == "button" && (name == "type" || name == "disabled") ||
                tag == "input" && (name == "type" || name == "min" || name == "max" || name == "step" || name == "value" || name == "disabled") ||
                tag == "style" && name == "type";
            if (!supported) throw Error("html-attribute", "Unsupported attribute '" + name + "' on '" + tag + "' (no scripts, URLs or event handlers)", offset);
            if (name == "type" && tag == "button" && !value.Equals("button", StringComparison.OrdinalIgnoreCase) ||
                name == "type" && tag == "style" && !value.Equals("text/css", StringComparison.OrdinalIgnoreCase))
                throw Error("html-attribute", "Unsupported element type", offset);
            if ((name == "data-bind" || name == "data-action" || name == "for") && (value.Length == 0 || value.Length > 128 || ContainsSpace(value)))
                throw Error("html-attribute", "Bindings, actions and label targets require 1 to 128 characters without whitespace", offset);
        }

        static string Decode(string value, int offset)
        {
            var result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '&') { result.Append(value[i]); continue; }
                int close = value.IndexOf(';', i + 1);
                if (close < 0 || close - i > 32) throw Error("html-entity", "Entities must be supported names or Unicode numbers with a semicolon", offset + i);
                string entity = value.Substring(i + 1, close - i - 1);
                string decoded;
                if (entity == "amp") decoded = "&";
                else if (entity == "lt") decoded = "<";
                else if (entity == "gt") decoded = ">";
                else if (entity == "quot") decoded = "\"";
                else if (entity == "apos") decoded = "'";
                else if (entity == "nbsp") decoded = "\u00a0";
                else if (entity.Length > 1 && entity[0] == '#')
                {
                    int code;
                    bool hex = entity.Length > 2 && (entity[1] == 'x' || entity[1] == 'X');
                    string digits = entity.Substring(hex ? 2 : 1);
                    if (digits.Length == 0 || !ValidDigits(digits, hex) || !Int32.TryParse(digits, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out code) || !ValidScalar(code))
                        throw Error("html-entity", "Invalid Unicode entity", offset + i);
                    decoded = code <= 65535 ? new string((char)code, 1) : Char.ConvertFromUtf32(code);
                }
                else throw Error("html-entity", "Unsupported entity '&" + entity + ";'", offset + i);
                result.Append(decoded);
                i = close;
            }
            string text = result.ToString();
            ValidateCharacters(text, offset);
            return text;
        }

        static bool ValidDigits(string value, bool hex)
        {
            foreach (char c in value)
                if (!(c >= '0' && c <= '9') && !(hex && (c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F'))) return false;
            return true;
        }
        static bool ValidScalar(int value) { return value > 0 && value <= 0x10ffff && (value < 0xd800 || value > 0xdfff) && !(value < 32 && value != 9 && value != 10 && value != 13) && !(value >= 127 && value <= 159); }
        static void ValidateCharacters(string value, int offset)
        {
            for (int i = 0; i < value.Length; i++)
            {
                int code = value[i];
                if (Char.IsHighSurrogate(value[i]))
                {
                    if (i + 1 == value.Length || !Char.IsLowSurrogate(value[i + 1])) throw Error("html-character", "Unpaired Unicode surrogate", offset + i);
                    code = Char.ConvertToUtf32(value[i], value[++i]);
                }
                else if (Char.IsLowSurrogate(value[i])) throw Error("html-character", "Unpaired Unicode surrogate", offset + i);
                if (!ValidScalar(code)) throw Error("html-character", "Unsupported control character", offset + i);
            }
        }
        static bool ContainsSpace(string value) { foreach (char c in value) if (Char.IsWhiteSpace(c)) return true; return false; }
        static bool NameChar(char c) { return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_'; }
        static HtmlParseException Error(string code, string message, int offset) { return new HtmlParseException(code, message, offset); }
    }
}
