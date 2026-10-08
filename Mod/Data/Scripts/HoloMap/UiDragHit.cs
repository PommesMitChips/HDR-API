using System;
using Sandbox.ModAPI;
using VRageMath;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        // A control has an explicitly authored rectangular hotzone in its owned
        // artwork's local XY plane. This tests that zone, not triangles or pixels.
        // The same item pose and display view used by rendering attach it to the
        // retained artwork; changing either takes effect without cached geometry.
        bool TryUiControlHit(UiDisplay display, UiWidget widget, Vector3D origin,
            Vector3D direction, out Vector3D hit, out long tileId)
        {return TryUiControlHit(display,widget,origin,direction,out hit,out tileId,false);}
        bool TryUiControlHit(UiDisplay display, UiWidget widget, Vector3D origin,
            Vector3D direction, out Vector3D hit, out long tileId,bool trustedHiddenBundle)
        {
            hit = Vector3D.Zero;
            tileId = 0;
            if (display == null || widget == null || widget.Control == null
                || !widget.Visible || !UiControlSourceValid(display, widget,trustedHiddenBundle)) return false;
            if(widget.ScreenId!=null)return TryUiProjectedHit(display,widget,origin,direction,out hit,out tileId);

            bool found = false;
            double nearest = UiInteractionRange * UiInteractionRange;
            foreach (var tile in _scenes.Values)
            {
                if (tile.ConsoleId != display.TargetId && tile.LcdSourceId != display.TargetId) continue;
                Vector3D center, right, up, candidate;
                if (!TryUiControlQuad(display, widget, tile, out center, out right, out up,trustedHiddenBundle)
                    || !UiHitGeometry.Quad(origin, direction, center, right, up,
                        UiInteractionRange, out candidate)) continue;

                var block = MyAPIGateway.Entities.GetEntityById(tile.ConsoleId) as IMyTerminalBlock;
                if (block == null || block.Closed || !block.IsWorking) continue;
                var panel = block as IMyTextPanel;
                if (panel != null)
                {
                    // Keep the renderer's front-face rule for calibrated vector
                    // LCDs. Native LCDs use their normal game screen behavior.
                    LcdScreenBasis basis;
                    if (tile.LcdRenderer == 1 && LcdScreenCalibration.TryGet(panel, out basis)
                        && !LcdPinnedProjection.FrontFacing(basis, block.WorldMatrix, origin)) continue;
                    Vector3D panelCenter, panelRight, panelUp, panelHit;
                    // The hotzone can cross a tiled canvas. Requiring the actual
                    // tile's full screen plane rejects its off-panel portions.
                    if (!UiLcdPlane(panel, out panelCenter, out panelRight, out panelUp)
                        || !UiHitGeometry.Quad(origin, direction, panelCenter, panelRight,
                            panelUp, UiInteractionRange, out panelHit)) continue;
                }
                var volume = GetDisplayVolume(tile, block);
                if (volume != null && !volume.Contains(candidate)) continue;
                double distance = Vector3D.DistanceSquared(origin, candidate);
                if (!Geometry.Finite(distance) || distance > nearest
                    || found && distance == nearest && tile.ConsoleId >= tileId) continue;
                nearest = distance;
                hit = candidate;
                tileId = tile.ConsoleId;
                found = true;
            }
            return found;
        }

        bool TryUiControlQuad(UiDisplay display, UiWidget widget, Scene tile,
            out Vector3D center, out Vector3D halfRight, out Vector3D halfUp)
        {return TryUiControlQuad(display,widget,tile,out center,out halfRight,out halfUp,false);}
        bool TryUiControlQuad(UiDisplay display, UiWidget widget, Scene tile,
            out Vector3D center, out Vector3D halfRight, out Vector3D halfUp,bool trustedHiddenBundle)
        {
            center = halfRight = halfUp = Vector3D.Zero;
            if(widget!=null&&widget.ScreenId!=null)return false;
            Scene source;
            Item item;
            MatrixD mapping;
            if (display == null || widget == null || widget.Control == null || tile == null
                || !Geometry.Finite(widget.Width) || !Geometry.Finite(widget.Height)
                || widget.Width <= 0 || widget.Height <= 0
                || !UiControlSourceValid(display, widget,trustedHiddenBundle)
                || !_scenes.TryGetValue(display.TargetId, out source)
                || !source.Items.TryGetValue(Key(display.CallerId, widget.Control.Artwork), out item)
                || item.CallerId != display.CallerId
                || !UiDragWorldMapping(display, tile.ConsoleId, out mapping)) return false;

            mapping = item.Transform * mapping;
            if (!UiDragMappingFinite(mapping)) return false;
            center = Vector3D.Transform(new Vector3D(widget.X, widget.Y, 0), mapping);
            halfRight = Vector3D.TransformNormal(new Vector3D(widget.Width * .5, 0, 0), mapping);
            halfUp = Vector3D.TransformNormal(new Vector3D(0, widget.Height * .5, 0), mapping);
            return FiniteDisplayPoint(center) && FiniteDisplayPoint(halfRight) && FiniteDisplayPoint(halfUp);
        }

        // Maps the display's artwork coordinates to its selected physical tile;
        // Item.Transform belongs before this matrix. An LCD discards canvas Z as
        // rendering does, so this matrix is intentionally singular. Drag callers
        // must solve the projected plane separately instead of inverting it.
        bool UiDragWorldMapping(UiDisplay display, long tileId, out MatrixD mapping)
        {
            mapping = MatrixD.Identity;
            Scene source, tile;
            if (display == null || MyAPIGateway.Entities == null
                || !_scenes.TryGetValue(display.TargetId, out source)
                || !_scenes.TryGetValue(tileId, out tile)
                || tile.ConsoleId != display.TargetId && tile.LcdSourceId != display.TargetId) return false;
            var block = MyAPIGateway.Entities.GetEntityById(tileId) as IMyTerminalBlock;
            if (block == null || block.Closed || !block.IsWorking) return false;
            var panel = block as IMyTextPanel;
            var view = LocalView(source);
            if (panel == null)
            {
                if (!(block is IMyProjector)) return false;
                mapping = view * block.WorldMatrix;
                return UiDragMappingFinite(mapping);
            }
            var surface = (Sandbox.ModAPI.Ingame.IMyTextSurface)panel;
            if (surface.Script != "HDRAPI"
                || surface.ContentType != VRage.Game.GUI.TextPanel.ContentType.SCRIPT) return false;
            try { LcdProjection.Validate(tile.LcdWidth, tile.LcdHeight,
                tile.LcdColumns, tile.LcdRows, tile.LcdColumn, tile.LcdRow); }
            catch (ArgumentException) { return false; }
            Vector3D center, right, up;
            if (!UiLcdPlane(panel, out center, out right, out up)) return false;
            var canvas = MatrixD.Identity;
            canvas.Right = right * (2 * tile.LcdColumns / tile.LcdWidth);
            canvas.Up = up * (2 * tile.LcdRows / tile.LcdHeight);
            canvas.Backward = Vector3D.Zero;
            canvas.Translation = center + right * (tile.LcdColumns - 2 * tile.LcdColumn - 1)
                + up * (1 - tile.LcdRows + 2 * tile.LcdRow);
            mapping = view * canvas;
            return UiDragMappingFinite(mapping);
        }

        static bool UiDragMappingFinite(MatrixD mapping)
        {
            foreach (double value in MatrixValues(mapping)) if (!Geometry.Finite(value)) return false;
            return Math.Abs(mapping.M14) <= 1e-12 && Math.Abs(mapping.M24) <= 1e-12
                && Math.Abs(mapping.M34) <= 1e-12 && Math.Abs(mapping.M44 - 1) <= 1e-12;
        }
    }
}
