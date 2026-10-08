using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Hdr.Html
{
    // Explicitly selected alternative. The caller owns the surface and its content mode.
    // Native Debug measurements are a line-height proxy, not Inter outline/cap metrics.
    public sealed class HtmlLcdTextMetrics : IHtmlTextMetrics
    {
        private readonly IMyTextSurface _surface;
        public string Profile { get { return "Debug:Lcd"; } }

        public HtmlLcdTextMetrics(IMyTextSurface surface)
        {
            if (surface == null) throw new ArgumentNullException("surface");
            _surface = surface;
        }

        public HtmlTextMeasurement Measure(string text, double size, double lineHeight)
        {
            ValidateText(text);
            if (!Finite(size) || size <= 0 || size > 4096 || !Finite(lineHeight) || lineHeight < 1 || lineHeight > 16)
                throw new ArgumentException("LCD text size/line height is outside the measured profile.");
            float scale = Scale(size);
            Vector2 measured = _surface.MeasureStringInPixels(new StringBuilder(text), "Debug", scale);
            if (!Finite(measured.X) || !Finite(measured.Y) || measured.X < 0 || measured.Y < 0)
                throw new ArgumentException("LCD returned invalid Debug text measurements.");
            // The API reports advance and line extent; it does not expose true ink bounds.
            // This box is a layout proxy. Native glyph/shadow bearings can extend
            // outside it; the painter uses the engine's scissor sprites for clipping.
            return new HtmlTextMeasurement
            {
                Advance = measured.X, CapHeight = size, LineAdvance = size * lineHeight,
                // Padding avoids premature layout culling of Debug glyph shadows.
                // It is conservative for the audited shipped font, not exact ink metadata.
                Ink = new HtmlRect(-size, -size, measured.X + size * 2, Math.Max(size, measured.Y) + size * 2),
                HasInk = text.Length != 0 && measured.Y > 0, Generation = 1
            };
        }

        internal float Scale(double size)
        {
            Vector2 basis = _surface.MeasureStringInPixels(new StringBuilder("M"), "Debug", 1f);
            if (!Finite(basis.Y) || basis.Y <= 0) throw new ArgumentException("LCD returned an invalid Debug line height.");
            double scale = size / basis.Y;
            if (!Finite(scale) || scale <= 0 || scale > 4096) throw new ArgumentException("LCD Debug text scale is invalid.");
            return (float)scale;
        }

        internal static void ValidateText(string text)
        {
            if (text == null || text.Length > 64) throw new ArgumentException("LCD text operations require at most 64 UTF-16 units.");
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsControl(text[i])) throw new ArgumentException("LCD text operations must be a single printable line.");
                if (char.IsHighSurrogate(text[i]))
                {
                    if (++i >= text.Length || !char.IsLowSurrogate(text[i])) throw new ArgumentException("LCD text contains an unpaired surrogate.");
                }
                else if (char.IsLowSurrogate(text[i])) throw new ArgumentException("LCD text contains an unpaired surrogate.");
            }
        }

        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed class HtmlNativeLcdPainter : IHtmlPainter
    {
        private IMyTextSurface _surface;
        private HtmlLcdTextMetrics _metrics;
        private readonly int _maxOperations, _maxPrimitives;
        private List<MySprite> _last;
        private HtmlPaintFrame _lastFrame;
        private Vector2 _lastSize, _lastTexture;
        private int _lastTriangles;
        private Func<bool> _ownershipGuard;
        private bool _ownsPublishedFrame;
        private bool _busy, _disposePending;
        private long _cleanupAttemptSerial;

        public bool LastCleanupAttempted { get; private set; }
        public bool LastCleanupSucceeded { get; private set; }
        public string LastCleanupError { get; private set; }

        public HtmlNativeLcdPainter(IMyTextSurface surface, bool callerOwnsSurface, HtmlPainterLimits limits = null, Func<bool> ownershipGuard = null)
        {
            if (!callerOwnsSurface) throw new ArgumentException("The caller must explicitly own the native LCD surface.");
            if (surface == null) throw new ArgumentNullException("surface");
            limits = limits ?? new HtmlPainterLimits();
            limits.Validate();
            _surface = surface; _metrics = new HtmlLcdTextMetrics(surface); _ownershipGuard = ownershipGuard;
            _maxOperations = limits.MaxOperations; _maxPrimitives = limits.MaxPrimitives;
        }

        public bool TryPaint(HtmlPaintFrame frame, out HtmlPaintReport report)
        {
            report = new HtmlPaintReport { Backend = "native-lcd", Revision = frame == null ? 0 : frame.Revision };
            if (_busy) { report.Error = "The native LCD painter is already processing an operation."; return false; }
            _busy = true;
            try
            {
                if (_surface == null) throw new InvalidOperationException("The native LCD painter has been disposed.");
                if (_ownershipGuard != null)
                {
                    bool owned;
                    try { owned = _ownershipGuard(); }
                    catch { LosePublication(); throw; }
                    if (!owned) { LosePublication(); throw new ArgumentException("The caller no longer owns the native LCD surface."); }
                }
                if (frame == null) throw new ArgumentNullException("frame");
                if (frame.FontProfile != _metrics.Profile) throw new ArgumentException("Native LCD requires the Debug:Lcd measured font profile.");
                if (_surface.ContentType != ContentType.SCRIPT || !string.IsNullOrEmpty(_surface.Script))
                {
                    LosePublication();
                    throw new ArgumentException("Native LCD requires caller-owned SCRIPT content with no selected text-surface script.");
                }
                Vector2 size = _surface.SurfaceSize, texture = _surface.TextureSize;
                ValidateSize(size); ValidateSize(texture);
                if (texture.X < size.X || texture.Y < size.Y || !Same(frame.Width, size.X) || !Same(frame.Height, size.Y))
                    throw new ArgumentException("Logical viewport must match SurfaceSize inside TextureSize.");
                if (frame.Operations.Count > _maxOperations) throw new ArgumentException("Native LCD paint-operation limit exceeded.");
                ThrowIfDisposePending();
                if (_lastFrame != null && size == _lastSize && texture == _lastTexture && HtmlPaintDiff.Equal(_lastFrame, frame))
                {
                    report.Success = true; report.PreparedOperations = _last.Count; report.EstimatedTriangles = _lastTriangles; report.EstimatedPoints = _lastTriangles * 2; return true;
                }
                Vector2 offset = (texture - size) * .5f;
                var prepared = new List<MySprite>(frame.Operations.Count);
                HtmlRect viewport = new HtmlRect(0, 0, size.X, size.Y);
                int triangles = 0;
                for (int i = 0; i < frame.Operations.Count; i++)
                {
                    HtmlPaintOperation operation = frame.Operations[i];
                    if (operation == null) throw new ArgumentException("A paint operation is null.");
                    ValidateRect(operation.Bounds); ValidateColor(operation.Color);
                    if (operation.HasClip) ValidateRect(operation.Clip);
                    if (!HtmlLcdTextMetrics.Finite(operation.Radius) || operation.Radius != 0 ||
                        !HtmlLcdTextMetrics.Finite(operation.StrokeWidth) || operation.StrokeWidth != 0)
                        throw new ArgumentException("Native LCD does not support rounded corners or stroked paint operations.");
                    if (operation.Kind != "rect" && operation.Kind != "text")
                        throw new ArgumentException("Native LCD supports only solid rectangles and Debug text; SVG/image assets require another painter.");
                    HtmlRect clip = operation.HasClip ? Intersect(viewport, operation.Clip) : viewport;
                    if (operation.Kind == "rect")
                    {
                        HtmlRect visible = Intersect(operation.Bounds, clip);
                        if (visible.Empty || operation.Color.A == 0) continue;
                        prepared.Add(MySprite.CreateSprite("SquareSimple",
                            new Vector2((float)(visible.X + visible.Width * .5), (float)(visible.Y + visible.Height * .5)) + offset,
                            new Vector2((float)visible.Width, (float)visible.Height)));
                        var sprite = prepared[prepared.Count - 1]; sprite.Color = NativeColor(operation.Color); prepared[prepared.Count - 1] = sprite;
                        triangles += 2;
                    }
                    else
                    {
                        HtmlTextMeasurement measurement = _metrics.Measure(operation.Text, operation.FontSize, operation.LineHeight);
                        if (!Same(operation.Bounds.Width, measurement.Advance) || !Same(operation.Bounds.Height, measurement.CapHeight))
                            throw new ArgumentException("Text bounds do not match the native Debug measurements; rebuild layout with HtmlLcdTextMetrics.");
                        Rectangle scissor = NativeScissor(clip, offset);
                        if (!measurement.HasInk || scissor.Width <= 0 || scissor.Height <= 0 || operation.Color.A == 0) continue;
                        // MeasureStringInPixels reports advances, not native Debug
                        // shadow bearings. Do not cull an individual text run by
                        // that approximate box or infer containment from it.
                        prepared.Add(MySprite.CreateClipRect(scissor));
                        var sprite = MySprite.CreateText(operation.Text, "Debug", NativeColor(operation.Color), _metrics.Scale(operation.FontSize), TextAlignment.LEFT);
                        sprite.Position = new Vector2((float)operation.Bounds.X, (float)operation.Bounds.Y) + offset;
                        prepared.Add(sprite); prepared.Add(MySprite.CreateClearClipRect()); triangles += operation.Text.Length * 2;
                    }
                    if (_maxPrimitives > 0 && prepared.Count > _maxPrimitives) throw new ArgumentException("Native LCD sprite limit exceeded.");
                }
                report.PreparedOperations = prepared.Count; report.EstimatedTriangles = triangles; report.EstimatedPoints = triangles * 2;
                HtmlPaintFrame admittedFrame = SnapshotPaint(frame);
                ThrowIfDisposePending();
                if (_last != null && size == _lastSize && texture == _lastTexture && Equal(_last, prepared))
                {
                    _lastFrame = admittedFrame; report.Success = true; return true;
                }
                report.MutatingCalls = 1;
                // Admission above is pure. DrawFrame/Dispose is one native commit, not a
                // cross-document transaction; exceptions may leave the engine frame uncertain.
                try
                {
                    using (MySpriteDrawFrame native = _surface.DrawFrame())
                        for (int i = 0; i < prepared.Count; i++) native.Add(prepared[i]);
                }
                catch { _last = null; _lastFrame = null; throw; }
                _last = prepared; _lastFrame = admittedFrame; _lastSize = size; _lastTexture = texture; _lastTriangles = triangles; _ownsPublishedFrame = true;
                ThrowIfDisposePending();
                report.Success = true; report.Changed = true; return true;
            }
            catch (Exception error)
            {
                report.Error = error.Message; return false;
            }
            finally
            {
                _busy = false;
                if (_disposePending)
                {
                    _disposePending = false;
                    long cleanupBefore = _cleanupAttemptSerial;
                    Dispose();
                    report.MutatingCalls += (int)(_cleanupAttemptSerial - cleanupBefore);
                }
            }
        }

        public void Reset()
        {
            if (_busy) { LastCleanupError = "Reset was skipped because the native LCD painter is busy."; return; }
            _busy = true;
            try { _last = null; _lastFrame = null; CleanupOwnedFrame(); }
            finally
            {
                _busy = false;
                if (_disposePending) { _disposePending = false; Dispose(); }
            }
        }
        public void Dispose()
        {
            if (_busy) { _disposePending = true; return; }
            _busy = true;
            try { _last = null; _lastFrame = null; CleanupOwnedFrame(); }
            finally
            {
                _last = null; _lastFrame = null; _ownsPublishedFrame = false;
                _surface = null; _metrics = null; _ownershipGuard = null;
                _disposePending = false; _busy = false;
            }
        }

        private void LosePublication() { _last = null; _lastFrame = null; _ownsPublishedFrame = false; }
        private void ThrowIfDisposePending() { if (_disposePending) throw new InvalidOperationException("Disposal was requested during the native LCD operation."); }

        private void CleanupOwnedFrame()
        {
            if (!_ownsPublishedFrame) return;
            // One cleanup opportunity per successful publication. Consume before
            // invoking caller/native code: failures must not retry or reenter and
            // accidentally erase another writer's later content.
            _ownsPublishedFrame = false;
            LastCleanupAttempted = false; LastCleanupSucceeded = false; LastCleanupError = null;
            try
            {
                // Without a guard, the constructor assertion is the caller's
                // promise of exclusive ownership until Reset/Dispose completes.
                // Content mode is evidence only, not third-party writer detection.
                if (_surface == null || _ownershipGuard != null && !_ownershipGuard()) return;
                if (_surface.ContentType != ContentType.SCRIPT || !string.IsNullOrEmpty(_surface.Script)) return;
                LastCleanupAttempted = true;
                _cleanupAttemptSerial++;
                using (MySpriteDrawFrame empty = _surface.DrawFrame()) { }
                LastCleanupSucceeded = true; LastCleanupError = null;
            }
            catch (Exception error)
            {
                // The engine's frame state is uncertain after a native exception.
                // Preserve this diagnostic until another eligible cleanup occurs.
                LastCleanupError = error.Message;
            }
        }

        // Hit regions belong to the controller. Do not copy unbounded, unrelated
        // caller metadata into a native renderer cache.
        private static HtmlPaintFrame SnapshotPaint(HtmlPaintFrame frame)
        {
            var copy = new HtmlPaintFrame { Revision = frame.Revision, Width = frame.Width, Height = frame.Height, FontProfile = frame.FontProfile };
            for (int i = 0; i < frame.Operations.Count; i++)
            {
                HtmlPaintOperation p = frame.Operations[i];
                copy.Operations.Add(new HtmlPaintOperation
                {
                    Key = p.Key, NodeId = p.NodeId, Kind = p.Kind, Text = p.Text, Asset = p.Asset, Order = p.Order,
                    Bounds = p.Bounds, Clip = p.Clip, HasClip = p.HasClip, Color = p.Color, Stroke = p.Stroke,
                    StrokeWidth = p.StrokeWidth, Radius = p.Radius, FontSize = p.FontSize, LineHeight = p.LineHeight
                });
            }
            foreach (var source in frame.SourceRegions)
                copy.SourceRegions.Add(new HtmlSourceRegion
                {
                    NodeId = source.NodeId, Bounds = source.Bounds, Clip = source.Clip,
                    Opacity = source.Opacity, Order = source.Order, Visible = source.Visible
                });
            return copy;
        }

        private static bool Equal(List<MySprite> left, List<MySprite> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                MySprite a = left[i], b = right[i];
                if (a.Type != b.Type || a.Data != b.Data || a.Position != b.Position || a.Size != b.Size || a.Color != b.Color ||
                    a.FontId != b.FontId || a.Alignment != b.Alignment || a.RotationOrScale != b.RotationOrScale) return false;
            }
            return true;
        }
        private static Color NativeColor(HtmlColor color) { return new Color((float)color.R, (float)color.G, (float)color.B, (float)color.A); }
        private static bool Same(double a, double b) { return HtmlLcdTextMetrics.Finite(a) && HtmlLcdTextMetrics.Finite(b) && Math.Abs(a - b) <= .001; }
        private static void ValidateSize(Vector2 size)
        {
            if (!HtmlLcdTextMetrics.Finite(size.X) || !HtmlLcdTextMetrics.Finite(size.Y) || size.X <= 0 || size.Y <= 0 || size.X > 16384 || size.Y > 16384)
                throw new ArgumentException("Native LCD surface/texture dimensions are invalid.");
        }
        private static void ValidateRect(HtmlRect rect)
        {
            if (!HtmlLcdTextMetrics.Finite(rect.X) || !HtmlLcdTextMetrics.Finite(rect.Y) || !HtmlLcdTextMetrics.Finite(rect.Width) || !HtmlLcdTextMetrics.Finite(rect.Height) ||
                Math.Abs(rect.X) > 1000000 || Math.Abs(rect.Y) > 1000000 || rect.Width < 0 || rect.Height < 0 || rect.Width > 1000000 || rect.Height > 1000000)
                throw new ArgumentException("A native LCD paint rectangle is invalid.");
        }
        private static void ValidateColor(HtmlColor color)
        {
            if (!HtmlLcdTextMetrics.Finite(color.R) || !HtmlLcdTextMetrics.Finite(color.G) || !HtmlLcdTextMetrics.Finite(color.B) || !HtmlLcdTextMetrics.Finite(color.A) ||
                color.R < 0 || color.R > 1 || color.G < 0 || color.G > 1 || color.B < 0 || color.B > 1 || color.A < 0 || color.A > 1)
                throw new ArgumentException("A native LCD paint color is invalid.");
        }
        private static HtmlRect Intersect(HtmlRect a, HtmlRect b)
        {
            double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
            return new HtmlRect(x, y, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x), Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y));
        }
        private static Rectangle NativeScissor(HtmlRect clip, Vector2 offset)
        {
            // Scissors use whole texture pixels. Round inward so no pixel outside
            // an authored fractional clip can leak through the native glyph shadow.
            double left = Math.Ceiling(clip.X + offset.X), top = Math.Ceiling(clip.Y + offset.Y);
            double right = Math.Floor(clip.X + clip.Width + offset.X), bottom = Math.Floor(clip.Y + clip.Height + offset.Y);
            if (!HtmlLcdTextMetrics.Finite(left) || !HtmlLcdTextMetrics.Finite(top) || !HtmlLcdTextMetrics.Finite(right) || !HtmlLcdTextMetrics.Finite(bottom) ||
                left < int.MinValue || left > int.MaxValue || top < int.MinValue || top > int.MaxValue || right < int.MinValue || right > int.MaxValue || bottom < int.MinValue || bottom > int.MaxValue)
                throw new ArgumentException("Native LCD clip exceeds the integer texture-pixel range.");
            return new Rectangle((int)left, (int)top, (int)Math.Max(0, right - left), (int)Math.Max(0, bottom - top));
        }
    }
}
