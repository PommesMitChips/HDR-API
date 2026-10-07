using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace HoloMap
{
    // Shared by the viewer and authoritative server. No client coordinates are trusted.
    public static class UiHitGeometry
    {
        public static bool Quad(Vector3D origin, Vector3D direction, Vector3D center,
            Vector3D halfRight, Vector3D halfUp, double range, out Vector3D hit)
        {
            hit = Vector3D.Zero;
            if (!Geometry.Finite(range) || range <= 0 || !Finite(origin) || !Finite(direction)
                || !Finite(center) || !Finite(halfRight) || !Finite(halfUp)) return false;
            double length = direction.Length();
            if (!Geometry.Finite(length) || length < 1e-8) return false;
            direction /= length;
            var normal = Vector3D.Cross(halfRight, halfUp);
            double denominator = Vector3D.Dot(normal, direction);
            if (!Geometry.Finite(denominator) || normal.LengthSquared() < 1e-16 || Math.Abs(denominator) < 1e-10) return false;
            double distance = Vector3D.Dot(normal, center - origin) / denominator;
            if (!Geometry.Finite(distance) || distance < 0 || distance > range) return false;
            var delta = origin + direction * distance - center;
            double xx = halfRight.LengthSquared(), yy = halfUp.LengthSquared(), xy = Vector3D.Dot(halfRight, halfUp);
            double determinant = xx * yy - xy * xy;
            if (!Geometry.Finite(determinant) || determinant < 1e-16) return false;
            double dx = Vector3D.Dot(delta, halfRight), dy = Vector3D.Dot(delta, halfUp);
            double u = (dx * yy - dy * xy) / determinant, v = (dy * xx - dx * xy) / determinant;
            if (!Geometry.Finite(u) || !Geometry.Finite(v) || Math.Abs(u) > 1 + 1e-8 || Math.Abs(v) > 1 + 1e-8) return false;
            hit = origin + direction * distance;
            return true;
        }
        static bool Finite(Vector3D p) { return Geometry.Finite(p.X) && Geometry.Finite(p.Y) && Geometry.Finite(p.Z); }
    }

    public sealed partial class HoloMapSession
    {
        const double UiInteractionRange = 3;
        sealed class UiScreenPlane { public int Tick; public Vector3D Center, Right, Up; }
        readonly Dictionary<long, UiScreenPlane> _uiScreenPlanes = new Dictionary<long, UiScreenPlane>();
        // Calibration uses the same model interaction detector as vanilla LCD use.
        // Modded panels without a detector use their model front plane; it is not an exact texture UV query.
        bool UiLcdPlane(IMyTextPanel panel, out Vector3D center, out Vector3D right, out Vector3D up)
        {
            var world = panel.WorldMatrix;
            LcdScreenBasis basis;
            if(LcdScreenCalibration.TryGet(panel,out basis))
            {center=Vector3D.Transform(basis.Center,world);right=Vector3D.TransformNormal(basis.HalfRight,world);up=Vector3D.TransformNormal(basis.HalfUp,world);return true;}
            UiScreenPlane cached;
            if (_uiScreenPlanes.TryGetValue(panel.EntityId, out cached) && _ticks - cached.Tick < 60)
            {
                center = Vector3D.Transform(cached.Center, world); right = Vector3D.TransformNormal(cached.Right, world);
                up = Vector3D.TransformNormal(cached.Up, world); return true;
            }
            var box = panel.LocalAABB;
            center = Vector3D.Transform(new Vector3D(box.Center.X, box.Center.Y, box.Min.Z - .002), world);
            right = world.Right * (box.Max.X - box.Min.X) * .5;
            up = world.Up * (box.Max.Y - box.Min.Y) * .5;
            if (panel.Model == null) return right.LengthSquared() > 1e-8 && up.LengthSquared() > 1e-8;
            var dummies = new Dictionary<string, IMyModelDummy>();
            panel.Model.GetDummies(dummies);
            IMyModelDummy chosen = null; string chosenName = null;
            foreach (var entry in dummies)
                if (entry.Key.IndexOf("textpanel", StringComparison.OrdinalIgnoreCase) >= 0 || entry.Key.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0)
                    if (chosenName == null || string.CompareOrdinal(entry.Key, chosenName) < 0) { chosen = entry.Value; chosenName = entry.Key; }
            if (chosen != null)
            {
                var dummy = chosen.Matrix * world;
                center = dummy.Translation;
                // Detector boxes are interaction volumes, not certified display texture dimensions.
                if (dummy.Right.LengthSquared() > 1e-8 && dummy.Up.LengthSquared() > 1e-8)
                { right = dummy.Right * .5; up = dummy.Up * .5; }
            }
            bool valid = right.LengthSquared() > 1e-8 && up.LengthSquared() > 1e-8;
            if (valid)
            {
                if (_uiScreenPlanes.Count >= 16 && !_uiScreenPlanes.ContainsKey(panel.EntityId)) _uiScreenPlanes.Clear();
                var inverse = MatrixD.Invert(world);
                _uiScreenPlanes[panel.EntityId] = new UiScreenPlane { Tick = _ticks, Center = Vector3D.Transform(center, inverse),
                    Right = Vector3D.TransformNormal(right, inverse), Up = Vector3D.TransformNormal(up, inverse) };
            }
            return valid;
        }

        bool TryUiWidgetQuad(UiDisplay display, UiWidget widget, Scene tile,
            out Vector3D center, out Vector3D halfRight, out Vector3D halfUp)
        {
            center = halfRight = halfUp = Vector3D.Zero;
            Scene source;
            if (display == null || widget == null || !_scenes.TryGetValue(display.TargetId, out source)) return false;
            var block = MyAPIGateway.Entities.GetEntityById(tile.ConsoleId) as IMyTerminalBlock;
            if (block == null || block.Closed || !block.IsWorking) return false;
            var view = LocalView(source);
            var c = Vector3D.Transform(new Vector3D(widget.X, widget.Y, 0), view);
            var x = Vector3D.TransformNormal(new Vector3D(widget.Width * .5, 0, 0), view);
            var y = Vector3D.TransformNormal(new Vector3D(0, widget.Height * .5, 0), view);
            var lcd = block as IMyTextPanel;
            if (lcd == null)
            {
                center = Vector3D.Transform(c, block.WorldMatrix);
                halfRight = Vector3D.TransformNormal(x, block.WorldMatrix);
                halfUp = Vector3D.TransformNormal(y, block.WorldMatrix);
                return true;
            }
            var surface = (Sandbox.ModAPI.Ingame.IMyTextSurface)lcd;
            if (surface.Script != "HDRAPI" || surface.ContentType != VRage.Game.GUI.TextPanel.ContentType.SCRIPT) return false;
            Vector3D panelCenter, panelRight, panelUp;
            if (!UiLcdPlane(lcd, out panelCenter, out panelRight, out panelUp)) return false;
            double cx = (c.X / tile.LcdWidth + .5) * tile.LcdColumns - tile.LcdColumn;
            double cy = (.5 - c.Y / tile.LcdHeight) * tile.LcdRows - tile.LcdRow;
            center = panelCenter + panelRight * (2 * cx - 1) + panelUp * (1 - 2 * cy);
            halfRight = panelRight * (2 * x.X / tile.LcdWidth * tile.LcdColumns) + panelUp * (2 * x.Y / tile.LcdHeight * tile.LcdRows);
            halfUp = panelRight * (2 * y.X / tile.LcdWidth * tile.LcdColumns) + panelUp * (2 * y.Y / tile.LcdHeight * tile.LcdRows);
            return true;
        }

        bool TryUiWidgetHit(UiDisplay display, UiWidget widget, Vector3D rayOrigin, Vector3D rayDirection, out Vector3D worldHit)
        {
            long tileId;
            return TryUiWidgetHit(display, widget, rayOrigin, rayDirection, out worldHit, out tileId);
        }
        bool TryUiWidgetHit(UiDisplay display, UiWidget widget, Vector3D rayOrigin, Vector3D rayDirection, out Vector3D worldHit, out long hitTileId)
        {
            worldHit = Vector3D.Zero; hitTileId = 0; bool found = false; double nearest = UiInteractionRange * UiInteractionRange;
            foreach (var tile in _scenes.Values)
            {
                if (tile.ConsoleId != display.TargetId && tile.LcdSourceId != display.TargetId) continue;
                Vector3D center, right, up, hit;
                if (!TryUiWidgetQuad(display, widget, tile, out center, out right, out up)
                    || !UiHitGeometry.Quad(rayOrigin, rayDirection, center, right, up, UiInteractionRange, out hit)) continue;
                var block = MyAPIGateway.Entities.GetEntityById(tile.ConsoleId) as IMyTerminalBlock;
                if (block is IMyTextPanel)
                {
                    LcdScreenBasis basis;
                    if(tile.LcdRenderer==1&&LcdScreenCalibration.TryGet((IMyTextPanel)block,out basis)
                       &&!LcdPinnedProjection.FrontFacing(basis,block.WorldMatrix,rayOrigin))continue;
                    Vector3D pc, pr, pu, panelHit;
                    if (!UiLcdPlane((IMyTextPanel)block, out pc, out pr, out pu)
                        || !UiHitGeometry.Quad(rayOrigin, rayDirection, pc, pr, pu, UiInteractionRange, out panelHit)) continue;
                }
                var volume = GetDisplayVolume(tile, block);
                if (volume != null && !volume.Contains(hit)) continue;
                double distance = Vector3D.DistanceSquared(rayOrigin, hit);
                if (distance <= nearest) { nearest = distance; worldHit = hit; hitTileId = tile.ConsoleId; found = true; }
            }
            return found;
        }
        bool UiWidgetWorldCenter(UiDisplay display, UiWidget widget, out Vector3D center)
        {
            center = Vector3D.Zero; Scene tile;
            if (!_scenes.TryGetValue(display.TargetId, out tile)) return false;
            Vector3D right, up;
            return TryUiWidgetQuad(display, widget, tile, out center, out right, out up);
        }
    }
}
