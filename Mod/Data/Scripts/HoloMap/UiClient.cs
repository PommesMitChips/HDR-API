using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        sealed class UiLocalMenu { public long Revision; public string Bundle; public int LastActionTick; }
        readonly Dictionary<string, UiLocalMenu> _uiLocalMenus = new Dictionary<string, UiLocalMenu>();
        UiDisplay _uiHoveredDisplay;
        UiWidget _uiHoveredWidget;
        IMyHudNotification _uiPrompt;
        long _uiControlledCharacter;

        bool UiClientCanInteract()
        {
            if (!ClientRenderingEnabled || MyAPIGateway.Utilities.IsDedicated || MyAPIGateway.Session == null
                || MyAPIGateway.Input == null || MyAPIGateway.Gui == null || MyAPIGateway.Session.Player == null) return false;
            var character = MyAPIGateway.Session.Player.Character;
            return character != null && !character.Closed && !character.IsDead
                && MyAPIGateway.Session.Player.Controller.ControlledEntity != null
                && ReferenceEquals(MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity, character)
                && MyAPIGateway.Session.CameraController != null && MyAPIGateway.Session.CameraController.IsInFirstPersonView
                && !MyAPIGateway.Gui.ChatEntryVisible && (!MyAPIGateway.Gui.IsCursorVisible || UiDragOwnsNativeCursor)
                && MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.None;
        }

        void ClearUiClient()
        {
            try{if (_uiLocalMenus.Count != 0 || _uiPending != null || UiViewerRequested) SendUiClose();}catch{}
            ClearUiClientLocal();
        }
        void ClearUiClientLocal()
        {
            CancelUiDragLocal();
            _uiLocalMenus.Clear(); _uiHoveredDisplay = null; _uiHoveredWidget = null; _uiControlledCharacter = 0;
            ClearUiFocus();
            try{if (_uiPrompt != null) _uiPrompt.Hide();}catch{}
        }
        bool UiWidgetLocallyVisible(UiDisplay display, UiWidget widget)
        {
            UiLocalMenu menu;
            bool opened = _uiLocalMenus.TryGetValue(UiKey(display.CallerId, display.TargetId), out menu)
                && menu.Revision == display.Revision && menu.Bundle == widget.Bundle;
            return HasVisibleUiWidget(display, widget, opened);
        }
        float ClientLayerAlpha(Scene scene, long caller, string name)
        {
            if (name != null && name.StartsWith("ui-", StringComparison.Ordinal))
            {
                var display = GetUiDisplay(caller, scene.ConsoleId); UiLocalMenu menu;
                if (display != null && _uiLocalMenus.TryGetValue(UiKey(caller, scene.ConsoleId), out menu)
                    && menu.Revision == display.Revision && name == "ui-" + menu.Bundle)
                {
                    Layer layer;
                    return scene.Layers.TryGetValue(Key(caller, name), out layer) ? layer.Opacity : 1;
                }
            }
            return LayerAlpha(scene, caller, name);
        }
        void TickUiClient()
        {
            if (!UiClientCanInteract()) { ClearUiClient(); return; }
            var character = MyAPIGateway.Session.Player.Character;
            if (_uiControlledCharacter != 0 && _uiControlledCharacter != character.EntityId) ClearUiClient();
            _uiControlledCharacter = character.EntityId;
            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Escape)) { ClearUiClient(); return; }
            if(TickUiDragClient()){_uiHoveredDisplay=null;_uiHoveredWidget=null;if(_uiPrompt!=null)_uiPrompt.Hide();return;}
            if(UiFocusRequested)
            {
                _uiHoveredDisplay=null;_uiHoveredWidget=null;
                if(_uiPrompt!=null)_uiPrompt.Hide();TickUiFocus();return;
            }
            var stale = new List<string>();
            foreach (var entry in _uiLocalMenus)
            { UiDisplay display; if (!_uiDisplays.TryGetValue(entry.Key, out display) || display.Revision != entry.Value.Revision || _ticks - entry.Value.LastActionTick > 1800) stale.Add(entry.Key); }
            foreach (string key in stale) _uiLocalMenus.Remove(key);
            _uiHoveredDisplay = null; _uiHoveredWidget = null;
            var head = character.GetHeadMatrix(true, true, true, true);
            var hoveredHit = Vector3D.Zero;
            long hoveredTile = 0;
            double nearest = UiInteractionRange * UiInteractionRange;
            foreach (var display in _uiDisplays.Values)
            {
                var target = MyAPIGateway.Entities.GetEntityById(display.TargetId) as IMyTerminalBlock;
                var caller = MyAPIGateway.Entities.GetEntityById(display.CallerId) as IMyProgrammableBlock;
                if (target == null || target.Closed || caller == null || caller.Closed
                    || !target.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)
                    || !caller.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)) continue;
                // Reverse declaration order matches the painter order for overlapping widgets.
                for (int i = display.Widgets.Count - 1; i >= 0; i--)
                {
                    var widget = display.Widgets[i]; Vector3D hit; long hitTile;
                    if (!UiWidgetLocallyVisible(display, widget) || !TryUiCurrentWidgetHit(display,widget,head.Translation,head.Forward,out hit,out hitTile))continue;
                    var physical = MyAPIGateway.Entities.GetEntityById(hitTile) as IMyTerminalBlock;
                    if (physical == null || !physical.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)) continue;
                    double distance = Vector3D.DistanceSquared(head.Translation, hit);
                    if (distance >= nearest && _uiHoveredWidget != null) continue;
                    nearest = distance; _uiHoveredDisplay = display; _uiHoveredWidget = widget; hoveredHit = hit; hoveredTile = hitTile;
                }
            }
            if (_uiHoveredWidget != null)
            {
                var anchor = MyAPIGateway.Entities.GetEntityById(hoveredTile) as IMyTerminalBlock;
                if (anchor == null || !UiUnoccluded(character.EntityId, anchor, head.Translation, hoveredHit))
                { _uiHoveredDisplay = null; _uiHoveredWidget = null; }
            }
            if (_uiHoveredWidget == null) { if (_uiPrompt != null) _uiPrompt.Hide(); return; }
            if (_uiPrompt == null) _uiPrompt = MyAPIGateway.Utilities.CreateNotification("", 1000, "White");
            var hoveredAnchor = MyAPIGateway.Entities.GetEntityById(_uiHoveredDisplay.TargetId);
            _uiPrompt.Text = (hoveredAnchor is IMyTextPanel ? "Use: " : "UI region: ")
                + (_uiHoveredWidget.Label ?? _uiHoveredWidget.Id) + " (HDR UI)";
            _uiPrompt.Show();
            // Use goes through native use-object dispatch. Polling F cannot consume vanilla actions.
        }
        bool TryConsumeUiUse(long targetId)
        {
            if(UiFocusRequested&&targetId==_uiFocusTile&&UiClientCanInteract()){ClearUiClient();return true;}
            if (!UiClientCanInteract() || _uiHoveredDisplay == null || _uiHoveredWidget == null) return false;
            Scene tile;
            if (targetId != _uiHoveredDisplay.TargetId
                && (!_scenes.TryGetValue(targetId, out tile) || tile.LcdSourceId != _uiHoveredDisplay.TargetId)) return false;
            var registered = GetUiDisplay(_uiHoveredDisplay.CallerId, _uiHoveredDisplay.TargetId);
            if (registered == null) return false;
            var anchor = MyAPIGateway.Entities.GetEntityById(targetId) as IMyTerminalBlock;
            var caller = MyAPIGateway.Entities.GetEntityById(registered.CallerId) as IMyProgrammableBlock;
            var character = MyAPIGateway.Session.Player.Character;
            var head = character.GetHeadMatrix(true, true, true, true); Vector3D hit; long hitTile;
            if (anchor == null || anchor.Closed || !anchor.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)
                || caller == null || caller.Closed || !caller.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)) return false;
            if(registered.Revision!=_uiHoveredDisplay.Revision)
            {
                // A menu update can land between hover and native use dispatch. Consume a current
                // button hit, but do not run the changed action or fall through to the LCD editor.
                foreach(var widget in registered.Widgets)
                    if(UiWidgetLocallyVisible(registered,widget)&&TryUiCurrentWidgetHit(registered,widget,head.Translation,head.Forward,out hit,out hitTile)
                        &&hitTile==targetId&&UiUnoccluded(character.EntityId,anchor,head.Translation,hit))return true;
                return false;
            }
            if(!UiWidgetLocallyVisible(registered,_uiHoveredWidget)
                || !TryUiCurrentWidgetHit(registered, _uiHoveredWidget, head.Translation, head.Forward, out hit, out hitTile) || hitTile != targetId
                || !UiUnoccluded(character.EntityId, anchor, head.Translation, hit)) return false;
            if(_uiHoveredWidget.Control!=null)return TryBeginUiControlUse(registered,_uiHoveredWidget,targetId);
            SendUiPress(_uiHoveredDisplay, _uiHoveredWidget);
            return true;
        }
        void ApplyUiAcknowledgement(UiAck ack)
        {
            if (ack == null || !ack.Accepted || !UiClientCanInteract()) return;
            var display = GetUiDisplay(ack.CallerId, ack.TargetId);
            if (display == null || display.Revision != ack.Revision) return;
            if(UiFocusRequested&&_uiFocusDisplay.CallerId==display.CallerId&&_uiFocusDisplay.TargetId==display.TargetId)
                _uiFocusLastActionTick=_ticks;
            UiLocalMenu existing;
            if (_uiLocalMenus.TryGetValue(UiKey(display.CallerId, display.TargetId), out existing)) existing.LastActionTick = _ticks;
            if (ack.ActionKind == "menu" || ack.ActionKind == "focus")
            {
                if (!display.Bundles.Exists(bundle => bundle.Id == ack.Argument)) return;
                _uiLocalMenus.Clear();
                _uiLocalMenus[UiKey(display.CallerId, display.TargetId)] = new UiLocalMenu { Revision = display.Revision, Bundle = ack.Argument, LastActionTick = _ticks };
                if (ack.ActionKind == "focus")
                {
                    if(!BeginUiFocus(display,ack.Argument,ack.SourceTileId))
                    {
                        ClearUiFocus();
                        MyAPIGateway.Utilities.ShowMessage("HDR UI", "Bundle opened for look-and-use interaction. Mouse focus requires a compatible acknowledged input backend; Escape closes.");
                    }
                }
                else if(UiFocusRequested&&!BeginUiFocus(display,ack.Argument,ack.SourceTileId))ClearUiClient();
            }
        }
        void DrawUiHover(ref int budget)
        {
            if (_uiHoveredDisplay == null || _uiHoveredWidget == null || !UiClientCanInteract()) return;
            Scene scene;
            if (!_scenes.TryGetValue(_uiHoveredDisplay.TargetId, out scene)) return;
            var anchor = MyAPIGateway.Entities.GetEntityById(scene.ConsoleId) as IMyTerminalBlock;
            if (anchor == null || anchor is IMyTextPanel || anchor.Render == null) return;
            Vector3D center, right, up;
            if(_uiHoveredWidget.Control!=null){if(!TryUiControlQuad(_uiHoveredDisplay,_uiHoveredWidget,scene,out center,out right,out up))return;}
            else if (!TryUiWidgetQuad(_uiHoveredDisplay, _uiHoveredWidget, scene, out center, out right, out up)) return;
            var corners = new[] { center - right - up, center + right - up, center + right + up, center - right + up };
            var inverse = MatrixD.Invert(anchor.WorldMatrix); uint renderId = anchor.Render.GetRenderObjectID();
            var volume = GetDisplayVolume(scene, anchor);
            for (int i = 0; i < 4 && budget > 0; i++)
            {
                var a = corners[i]; var b = corners[(i + 1) % 4];
                if (volume != null && !volume.ClipLine(ref a, ref b)) continue;
                var delta = b - a; double length = delta.Length(); if (length < 1e-8) continue;
                MyTransparentGeometry.AddLineBillboard(LineMaterial, new Vector4(1, .8f, .15f, 1), a,
                    renderId, ref inverse, (Vector3)(delta / length), (float)length, .008f, MyBillboard.BlendTypeEnum.Standard);
                budget--;
            }
        }
        void DrawUiLcdHover(Scene scene, Scene tile, VRage.Game.GUI.TextPanel.MySpriteDrawFrame frame,
            Vector2 origin, Vector2 size, ref int budget)
        {
            if (_uiHoveredDisplay == null || _uiHoveredWidget == null || _uiHoveredDisplay.TargetId != scene.ConsoleId) return;
            var w = _uiHoveredWidget; var view = LocalView(scene);
            var corners = new[] { new Vector3D(w.X-w.Width*.5,w.Y-w.Height*.5,0),new Vector3D(w.X+w.Width*.5,w.Y-w.Height*.5,0),
                new Vector3D(w.X+w.Width*.5,w.Y+w.Height*.5,0),new Vector3D(w.X-w.Width*.5,w.Y+w.Height*.5,0) };
            if(w.Control!=null){Item item;if(!scene.Items.TryGetValue(Key(_uiHoveredDisplay.CallerId,w.Control.Artwork),out item))return;view=item.Transform*view;}
            var points = new Vector2[4];
            for(int i=0;i<4;i++)points[i]=origin+LcdProjection.Point(Vector3D.Transform(corners[i],view),tile.LcdWidth,tile.LcdHeight,size,tile.LcdColumns,tile.LcdRows,tile.LcdColumn,tile.LcdRow);
            for(int i=0;i<4;i++)LcdLine(frame,points[i],points[(i+1)%4],2,VRageMath.Color.Gold,origin,size,ref budget);
        }
    }
}
