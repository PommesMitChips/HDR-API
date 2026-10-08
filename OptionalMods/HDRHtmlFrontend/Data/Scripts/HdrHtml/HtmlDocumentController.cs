using System;
using System.Collections.Generic;

namespace Hdr.Html
{
    // Coalesces static-document edits; every parse/layout failure preserves the last valid frame.
    public sealed class HtmlDocumentController
    {
        private readonly IHtmlTextMetrics _metrics;
        private readonly HtmlLimits _limits;
        private Dictionary<string, string> _data = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, string> _pendingData;
        private readonly Dictionary<string, string> _pendingText = new Dictionary<string, string>(StringComparer.Ordinal);
        private double _width, _height, _pendingWidth, _pendingHeight;
        public HtmlDocument Document { get; private set; }
        public HtmlPaintFrame Frame { get; private set; }
        public string LastError { get; private set; }
        public long Revision { get; private set; }
        public long LayoutBuildCount { get; private set; }
        public bool IsDirty { get; private set; }
        public HtmlDocumentController(IHtmlTextMetrics metrics, HtmlLimits limits)
        {
            if (metrics == null || limits == null) throw new ArgumentException("Metrics and limits are required.");
            _metrics = metrics;
            // Limits belong to the controller; callers cannot mutate admission after load.
            _limits = new HtmlLimits { MaxCharacters = limits.MaxCharacters, MaxNodes = limits.MaxNodes, MaxDepth = limits.MaxDepth, MaxRules = limits.MaxRules, MaxDeclarations = limits.MaxDeclarations, MaxTextCharacters = limits.MaxTextCharacters, MaxPaintOperations = limits.MaxPaintOperations, MaxHitRegions = limits.MaxHitRegions };
        }
        public bool TryLoad(string markup, string stylesheet, double width, double height)
        {
            try
            {
                HtmlPaintValidation.Dimensions(width, height);
                HtmlDocument candidate = HtmlParser.Parse(markup, stylesheet, _limits);
                LayoutBuildCount++;
                HtmlPaintFrame frame = HtmlLayout.Build(candidate, width, height, _metrics, _data, _limits);
                Document = candidate; Frame = frame; _width = width; _height = height; Revision++; Frame.Revision = Revision;
                ResetPending(); LastError = null; return true;
            }
            catch (ArgumentException error) { LastError = error.Message; ResetPending(); return false; }
            catch (InvalidOperationException error) { LastError = error.Message; ResetPending(); return false; }
        }
        public bool SetData(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 128) { LastError = "Binding keys must contain 1..128 characters."; return false; }
            value = value ?? "";
            if (value.Length > _limits.MaxTextCharacters) { LastError = "Binding text exceeds the limit."; return false; }
            try { HtmlPaintValidation.Text(key, 128); HtmlPaintValidation.Text(value, _limits.MaxTextCharacters); }
            catch (ArgumentException error) { LastError = error.Message; return false; }
            Dictionary<string, string> current = _pendingData ?? _data; string previous;
            if (current.TryGetValue(key, out previous) && previous == value) return false;
            if (!current.ContainsKey(key) && current.Count >= 512) { LastError = "Binding dictionary exceeds the 512-key limit."; return false; }
            long characters = value.Length;
            foreach (KeyValuePair<string, string> entry in current) if (entry.Key != key) characters += entry.Value.Length;
            if (characters > 131072) { LastError = "Binding dictionary exceeds 131072 text characters."; return false; }
            if (_pendingData == null) _pendingData = new Dictionary<string, string>(_data, StringComparer.Ordinal);
            _pendingData[key] = value; RefreshDirty(); return true;
        }
        public bool SetText(string nodeId, string text)
        {
            if (Document == null || string.IsNullOrEmpty(nodeId) || !Document.ById.ContainsKey(nodeId)) { LastError = "Unknown text node id: " + nodeId; return false; }
            text = text ?? "";
            if (text.Length > _limits.MaxTextCharacters) { LastError = "Text exceeds the limit."; return false; }
            try { HtmlPaintValidation.Text(text, _limits.MaxTextCharacters); }
            catch (ArgumentException error) { LastError = error.Message; return false; }
            HtmlNode node = Document.ById[nodeId];
            if (node.Tag == "input" || node.Tag == "img" || node.Tag == "svg" || node.Tag == "head" || node.Tag == "style" || node.Tag == "title") { LastError = "This node does not accept plain text content."; return false; }
            string previous;
            if (_pendingText.TryGetValue(nodeId, out previous) && previous == text) return false;
            if (!_pendingText.ContainsKey(nodeId) && node.Children.Count == 1 && node.Children[0].Tag == "#text" && node.Children[0].Text == text) return false;
            if (node.Children.Count == 1 && node.Children[0].Tag == "#text" && node.Children[0].Text == text) _pendingText.Remove(nodeId);
            else _pendingText[nodeId] = text;
            RefreshDirty(); return true;
        }
        public bool Resize(double width, double height)
        {
            try { HtmlPaintValidation.Dimensions(width, height); }
            catch (ArgumentException error) { LastError = error.Message; return false; }
            double currentWidth = IsDirty ? _pendingWidth : _width, currentHeight = IsDirty ? _pendingHeight : _height;
            if (currentWidth == width && currentHeight == height) return false;
            _pendingWidth = width; _pendingHeight = height; RefreshDirty(); return true;
        }
        public Dictionary<string, string> GetData() { return new Dictionary<string, string>(_data, StringComparer.Ordinal); }
        // Candidate edits remain queued and text writes clone the DOM in Update.
        // Unchanged document nodes retain identity across source/data-only batches.
        internal HtmlDocumentController Fork()
        {
            var candidate=new HtmlDocumentController(_metrics,_limits);
            candidate.Document=Document;candidate.Frame=HtmlPaintDiff.Snapshot(Frame);candidate.Revision=Revision;candidate.LayoutBuildCount=LayoutBuildCount;
            candidate._data=new Dictionary<string,string>(_data,StringComparer.Ordinal);
            candidate._pendingData=_pendingData==null?null:new Dictionary<string,string>(_pendingData,StringComparer.Ordinal);
            foreach(var pair in _pendingText)candidate._pendingText.Add(pair.Key,pair.Value);
            candidate._width=_width;candidate._height=_height;candidate._pendingWidth=_pendingWidth;candidate._pendingHeight=_pendingHeight;candidate.IsDirty=IsDirty;
            return candidate;
        }
        internal void Commit(HtmlDocumentController candidate)
        {
            if(candidate==null||!ReferenceEquals(candidate._metrics,_metrics)||candidate.IsDirty)throw new ArgumentException("Mutation candidate must be fully validated by this controller's metrics.");
            bool retainFrame=Revision==candidate.Revision&&HtmlPaintDiff.Equal(Frame,candidate.Frame);
            Document=candidate.Document;if(!retainFrame)Frame=candidate.Frame;Revision=candidate.Revision;LayoutBuildCount=candidate.LayoutBuildCount;
            _data=new Dictionary<string,string>(candidate._data,StringComparer.Ordinal);_width=candidate._width;_height=candidate._height;ResetPending();LastError=null;
        }
        public bool Update()
        {
            if (!IsDirty) return false;
            if (Document == null) { LastError = "Load a document before updating."; ResetPending(); return false; }
            try
            {
                HtmlDocument candidate = _pendingText.Count == 0 ? Document : CloneDocument(Document, _pendingText);
                Dictionary<string, string> data = _pendingData ?? _data;
                LayoutBuildCount++;
                HtmlPaintFrame frame = HtmlLayout.Build(candidate, _pendingWidth, _pendingHeight, _metrics, data, _limits);
                Document = candidate; Frame = frame; _data = new Dictionary<string, string>(data, StringComparer.Ordinal); _width = _pendingWidth; _height = _pendingHeight;
                Revision++; Frame.Revision = Revision; ResetPending(); LastError = null; return true;
            }
            catch (ArgumentException error) { LastError = error.Message; ResetPending(); return false; }
            catch (InvalidOperationException error) { LastError = error.Message; ResetPending(); return false; }
        }
        private void ResetPending()
        {
            _pendingData = null; _pendingText.Clear(); _pendingWidth = _width; _pendingHeight = _height; IsDirty = false;
        }
        private void RefreshDirty()
        {
            bool dataChanged = false;
            if (_pendingData != null)
            {
                dataChanged = _pendingData.Count != _data.Count;
                if (!dataChanged) foreach (KeyValuePair<string, string> item in _pendingData)
                {
                    string prior; if (!_data.TryGetValue(item.Key, out prior) || prior != item.Value) { dataChanged = true; break; }
                }
                if (!dataChanged) _pendingData = null;
            }
            IsDirty = dataChanged || _pendingText.Count != 0 || _pendingWidth != _width || _pendingHeight != _height;
        }
        private static HtmlDocument CloneDocument(HtmlDocument source, Dictionary<string, string> text)
        {
            var clone = new HtmlDocument(); int next = MaximumOrdinal(source.Root) + 1;
            clone.Root = CloneNode(source.Root, null, clone, text, ref next);
            for (int i = 0; i < source.Rules.Count; i++)
            {
                HtmlCssRule rule = source.Rules[i]; var copy = new HtmlCssRule { Selector = rule.Selector, Specificity = rule.Specificity, Order = rule.Order };
                foreach (KeyValuePair<string, string> declaration in rule.Declarations) copy.Declarations.Add(declaration.Key, declaration.Value);
                clone.Rules.Add(copy);
            }
            return clone;
        }
        private static int MaximumOrdinal(HtmlNode node) { int max = node.Ordinal; for (int i = 0; i < node.Children.Count; i++) max = Math.Max(max, MaximumOrdinal(node.Children[i])); return max; }
        private static HtmlNode CloneNode(HtmlNode node, HtmlNode parent, HtmlDocument clone, Dictionary<string, string> text, ref int next)
        {
            var copy = new HtmlNode { Ordinal = node.Ordinal, Tag = node.Tag, Id = node.Id, Text = node.Text, Parent = parent };
            foreach (KeyValuePair<string, string> attribute in node.Attributes) copy.Attributes.Add(attribute.Key, attribute.Value);
            foreach (KeyValuePair<string, string> property in node.InlineStyle) copy.InlineStyle.Add(property.Key, property.Value);
            copy.Classes.AddRange(node.Classes);
            if (!string.IsNullOrEmpty(copy.Id)) clone.ById.Add(copy.Id, copy);
            string replacement;
            if (!string.IsNullOrEmpty(copy.Id) && text.TryGetValue(copy.Id, out replacement))
            {
                // Preserve an existing text ordinal, so pure text edits preserve retained paint identities.
                int ordinal = node.Children.Count > 0 && node.Children[0].Tag == "#text" ? node.Children[0].Ordinal : next++;
                copy.Children.Add(new HtmlNode { Ordinal = ordinal, Tag = "#text", Text = replacement, Parent = copy });
            }
            else for (int i = 0; i < node.Children.Count; i++) copy.Children.Add(CloneNode(node.Children[i], copy, clone, text, ref next));
            return copy;
        }
    }
}
