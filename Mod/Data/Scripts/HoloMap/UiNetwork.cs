using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace HoloMap
{
    [ProtoContract]
    public sealed class UiPress
    {
        [ProtoMember(1)] public long CallerId;
        [ProtoMember(2)] public long TargetId;
        [ProtoMember(3)] public string WidgetId;
        [ProtoMember(4)] public long Revision;
        [ProtoMember(5)] public long Sequence;
        [ProtoMember(6)] public int Action;
    }
    [ProtoContract]
    public sealed class UiAck
    {
        [ProtoMember(1)] public long CallerId;
        [ProtoMember(2)] public long TargetId;
        [ProtoMember(3)] public string WidgetId;
        [ProtoMember(4)] public long Revision;
        [ProtoMember(5)] public long Sequence;
        [ProtoMember(6)] public bool Accepted;
        [ProtoMember(7)] public string ActionKind;
        [ProtoMember(8)] public string Argument;
        [ProtoMember(9)] public long SourceTileId;
    }
    public sealed partial class HoloMapSession
    {
        const ushort UiNetworkChannel = 49784;
        const int UiPacketBytes = 1024;
        sealed class UiPeer
        {
            public long Sequence, OpenCaller, OpenTarget, OpenTile, OpenRevision, OpenCharacter;
            public string OpenBundle, FocusSourceWidget;
            public bool IsFocus;
            public Vector3D FocusLocal;
            public MatrixD FocusView;
            public double[] FocusLayout;
            public long FocusTileSourceId;
            public int OpenExpires, Started, Count, LastSeen;
        }
        sealed class UiQueuedPress { public ulong Sender; public UiPress Press; }
        readonly Dictionary<ulong, UiPeer> _uiPeers = new Dictionary<ulong, UiPeer>();
        readonly Queue<UiQueuedPress> _uiRequests = new Queue<UiQueuedPress>();
        UiPress _uiPending;
        int _uiPendingTick, _uiLastSendTick = -12, _uiGlobalStart, _uiGlobalCount;
        long _uiSequence = DateTime.UtcNow.Ticks;

        void RegisterUiNetwork() { MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(UiNetworkChannel, ReceiveUiMessage); }
        void UnregisterUiNetwork()
        {
            try{if(MyAPIGateway.Multiplayer!=null)MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(UiNetworkChannel, ReceiveUiMessage);}
            catch(Exception error){if(VRage.Utils.MyLog.Default!=null)VRage.Utils.MyLog.Default.WriteLineAndConsole("HDR UI network cleanup: "+error.Message);}
            _uiPeers.Clear(); _uiRequests.Clear(); _uiPending = null;
        }

        // No arguments, world points, camera matrices or executable data are supplied by a peer.
        void SendUiPress(UiDisplay display, UiWidget widget)
        { SendUiWidgetPress(display, widget, 0); }
        void SendUiFocusPress(UiDisplay display, UiWidget widget)
        { SendUiWidgetPress(display, widget, 2); }
        void SendUiWidgetPress(UiDisplay display, UiWidget widget, int action)
        {
            if (display == null || widget == null || _uiPending != null || _ticks - _uiLastSendTick < 12) return;
            var press = new UiPress { CallerId = display.CallerId, TargetId = display.TargetId,
                WidgetId = widget.Id, Revision = display.Revision, Sequence = ++_uiSequence, Action = action };
            _uiPending = press; _uiPendingTick = _ticks; _uiLastSendTick = _ticks;
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                var player = MyAPIGateway.Session.LocalHumanPlayer;
                if (player != null && ReserveUiRequest(player.SteamUserId)) _uiRequests.Enqueue(new UiQueuedPress { Sender = player.SteamUserId, Press = press });
                else _uiPending = null;
            }
            else MyAPIGateway.Multiplayer.SendMessageToServer(UiNetworkChannel, MyAPIGateway.Utilities.SerializeToBinary(press), true);
        }
        void SendUiClose()
        {
            _uiPending = null;
            if(MyAPIGateway.Multiplayer==null||MyAPIGateway.Utilities==null||MyAPIGateway.Session==null)return;
            var close = new UiPress { Action = 1, Sequence = ++_uiSequence, WidgetId = "" };
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                var player = MyAPIGateway.Session.LocalHumanPlayer;
                if (player != null) { UiPeer peer; if (_uiPeers.TryGetValue(player.SteamUserId, out peer)) { peer.OpenBundle = null; peer.Sequence = close.Sequence; } }
            }
            else MyAPIGateway.Multiplayer.SendMessageToServer(UiNetworkChannel, MyAPIGateway.Utilities.SerializeToBinary(close), true);
        }

        bool ReserveUiRequest(ulong sender)
        {
            if (sender == 0 || _uiRequests.Count >= 20) return false;
            if (_ticks - _uiGlobalStart >= 60) { _uiGlobalStart = _ticks; _uiGlobalCount = 0; }
            if (_uiGlobalCount >= 20) return false;
            UiPeer peer;
            if (!_uiPeers.TryGetValue(sender, out peer))
            {
                if (_uiPeers.Count >= 128) return false;
                peer = new UiPeer { Started = _ticks }; _uiPeers.Add(sender, peer);
            }
            if (_ticks - peer.Started >= 60) { peer.Started = _ticks; peer.Count = 0; }
            if (peer.Count >= 5) return false;
            peer.Count++; peer.LastSeen = _ticks; _uiGlobalCount++; return true;
        }

        void ReceiveUiMessage(ushort channel, byte[] bytes, ulong sender, bool fromServer)
        {
            if (channel != UiNetworkChannel || bytes == null || bytes.Length == 0 || bytes.Length > UiPacketBytes) return;
            try
            {
                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    if (fromServer || !ReserveUiRequest(sender)) return;
                    var press = MyAPIGateway.Utilities.SerializeFromBinary<UiPress>(bytes);
                    if (ValidUiPress(press)) _uiRequests.Enqueue(new UiQueuedPress { Sender = sender, Press = press });
                }
                else
                {
                    if (!fromServer || sender != 0 && sender != MyAPIGateway.Multiplayer.ServerId) return;
                    ReceiveUiAck(MyAPIGateway.Utilities.SerializeFromBinary<UiAck>(bytes));
                }
            }
            catch (Exception) { /* Malformed packets are bounded and ignored without per-packet log spam. */ }
        }
        static bool ValidUiPress(UiPress p)
        { return p != null && p.Sequence > 0 && (p.Action == 1 && p.CallerId == 0 && p.TargetId == 0 && p.Revision == 0 && p.WidgetId == ""
            || (p.Action == 0 || p.Action == 2) && p.CallerId != 0 && p.TargetId != 0 && p.Revision > 0 && !string.IsNullOrWhiteSpace(p.WidgetId) && p.WidgetId.Length <= 24); }

        void ReceiveUiAck(UiAck ack)
        {
            var pending = _uiPending;
            if (pending == null || ack == null || ack.Sequence != pending.Sequence || ack.CallerId != pending.CallerId
                || ack.TargetId != pending.TargetId || ack.WidgetId != pending.WidgetId || ack.Revision != pending.Revision) return;
            _uiPending = null;
            if (!ack.Accepted) { if (pending.Action == 2) ClearUiClient(); return; }
            if (!UiActionAllowed(ack.ActionKind) || ack.Argument == null || ack.Argument.Length > 256) return;
            ApplyUiAcknowledgement(ack);
        }
        static bool UiActionAllowed(string kind) { return kind == "pb" || kind == "menu" || kind == "focus" || kind == "toggle"; }

        void TickUiNetwork()
        {
            if (_uiPending != null && _ticks - _uiPendingTick > 180)
            { bool focus = _uiPending.Action == 2; _uiPending = null; if (focus) ClearUiClient(); }
            if (!MyAPIGateway.Multiplayer.IsServer) return;
            for (int i = 0; i < 4 && _uiRequests.Count != 0; i++)
            {
                var request = _uiRequests.Dequeue();
                var ack = ProcessUiPress(request.Sender, request.Press);
                if (MyAPIGateway.Session.LocalHumanPlayer != null && request.Sender == MyAPIGateway.Session.LocalHumanPlayer.SteamUserId) ReceiveUiAck(ack);
                else MyAPIGateway.Multiplayer.SendMessageTo(UiNetworkChannel, MyAPIGateway.Utilities.SerializeToBinary(ack), request.Sender, true);
            }
            if (_ticks % 60 != 0) return;
            var players = new List<IMyPlayer>(); MyAPIGateway.Players.GetPlayers(players);
            var active = new Dictionary<ulong, IMyPlayer>(); foreach (var player in players) active[player.SteamUserId] = player;
            var remove = new List<ulong>(); foreach (var entry in _uiPeers)
            {
                IMyPlayer player;
                if (!active.TryGetValue(entry.Key, out player)) { remove.Add(entry.Key); continue; }
                var peer = entry.Value;
                if (peer.OpenBundle == null) continue;
                var character = player.Character;
                var anchor = MyAPIGateway.Entities.GetEntityById(peer.OpenTile) as IMyTerminalBlock;
                var display = GetUiDisplay(peer.OpenCaller, peer.OpenTarget);
                if (_ticks > peer.OpenExpires || character == null || character.Closed || character.IsDead || character.EntityId != peer.OpenCharacter
                    || player.Controller == null || !ReferenceEquals(player.Controller.ControlledEntity, character)
                    || anchor == null || anchor.Closed || Vector3D.DistanceSquared(character.GetPosition(), anchor.GetPosition()) > 25
                    || display == null || display.Revision != peer.OpenRevision) peer.OpenBundle = null;
                else if (peer.IsFocus && !UiFocusGrantContext(peer, player, display)) peer.OpenBundle = null;
            }
            foreach (var sender in remove) _uiPeers.Remove(sender);
        }

        UiAck ProcessUiPress(ulong sender, UiPress press)
        {
            var ack = new UiAck { CallerId = press.CallerId, TargetId = press.TargetId, WidgetId = press.WidgetId, Revision = press.Revision, Sequence = press.Sequence };
            try
            {
                if (!ValidUiPress(press)) return ack;
                UiPeer peer;
                if (!_uiPeers.TryGetValue(sender, out peer) || press.Sequence <= peer.Sequence) return ack;
                // Consume a sequence even on rejection; retries cannot later become valid accidentally.
                peer.Sequence = press.Sequence;
                if (press.Action == 1) { peer.OpenBundle = null; return ack; }
                var players = new List<IMyPlayer>(); MyAPIGateway.Players.GetPlayers(players, p => p.SteamUserId == sender);
                if (players.Count != 1) return ack;
                var player = players[0]; var character = player.Character;
                if (character == null || character.Closed || character.IsDead || player.IdentityId == 0
                    || !ReferenceEquals(MyAPIGateway.Entities.GetEntityById(character.EntityId), character)) return ack;
                if (player.Controller == null || !ReferenceEquals(player.Controller.ControlledEntity, character)) return ack;
                var caller = MyAPIGateway.Entities.GetEntityById(press.CallerId) as IMyProgrammableBlock;
                var anchor = MyAPIGateway.Entities.GetEntityById(press.TargetId) as IMyTerminalBlock;
                if (caller == null || anchor == null) return ack;
                Authorize(caller, anchor);
                if (!caller.HasPlayerAccess(player.IdentityId) || !anchor.HasPlayerAccess(player.IdentityId)) return ack;
                var display = GetUiDisplay(press.CallerId, press.TargetId);
                var widget = GetUiWidget(press.CallerId, press.TargetId, press.WidgetId, false);
                bool opened = widget != null && peer.OpenCaller == press.CallerId && peer.OpenTarget == press.TargetId
                    && peer.OpenRevision == press.Revision && peer.OpenBundle == widget.Bundle
                    && peer.OpenCharacter == character.EntityId && _ticks <= peer.OpenExpires;
                if (display == null || display.Revision != press.Revision || widget == null || !HasVisibleUiWidget(display, widget, opened)
                    || !UiActionAllowed(widget.ActionKind) || widget.Argument == null || widget.Argument.Length > 256) return ack;
                var head = character.GetHeadMatrix(true, true, true, true); Vector3D hit; long tileId;
                if (press.Action == 2)
                {
                    if (!opened || !peer.IsFocus || !UiFocusGrantContext(peer, player, display)) return ack;
                    tileId = peer.OpenTile;
                    var focusTile = MyAPIGateway.Entities.GetEntityById(tileId) as IMyTerminalBlock;
                    hit = Vector3D.Transform(peer.FocusLocal, focusTile.WorldMatrix);
                }
                else if (!TryUiWidgetHit(display, widget, head.Translation, head.Forward, out hit, out tileId)) return ack;
                var tile = MyAPIGateway.Entities.GetEntityById(tileId) as IMyTerminalBlock;
                if (tile == null || tile.Closed) return ack;
                Authorize(caller, tile);
                if (!tile.HasPlayerAccess(player.IdentityId) || Vector3D.DistanceSquared(character.GetPosition(), tile.GetPosition()) > 25
                    || Vector3D.DistanceSquared(head.Translation, hit) > 36 || !UiUnoccluded(character.EntityId, tile, head.Translation, hit)) return ack;
                // Fixed PB-registered arguments only. A client cannot invoke any arbitrary command.
                if (widget.ActionKind == "pb" && !caller.TryRun(widget.Argument)) return ack;
                if (widget.ActionKind == "toggle")
                {
                    var state = GetLayerState(caller, anchor, widget.Argument);
                    if (!state.Item1 || !SetLayerVisible(caller, anchor, widget.Argument, !state.Item3).Item1) return ack;
                }
                if (widget.ActionKind == "menu" || widget.ActionKind == "focus")
                {
                    // Only a server-approved visible widget can establish a private submenu grant.
                    bool exists = false; foreach (var bundle in display.Bundles) if (bundle.Id == widget.Argument) exists = true;
                    if (!exists) return ack;
                    bool continueFocus = press.Action == 2 && peer.IsFocus;
                    if (widget.ActionKind == "focus" && !continueFocus)
                    {
                        // Focus begins at an actually visible physical widget, never a client-claimed point.
                        if (!HasVisibleUiWidget(display, widget, false)) return ack;
                        Scene focusScene; if (!_scenes.TryGetValue(display.TargetId, out focusScene)) return ack;
                        peer.FocusSourceWidget = widget.Id;
                        peer.FocusLocal = Vector3D.Transform(hit, MatrixD.Invert(tile.WorldMatrix));
                        peer.FocusView = LocalView(focusScene);
                        Scene focusTileScene; if (!_scenes.TryGetValue(tileId, out focusTileScene)) return ack;
                        peer.FocusLayout = CaptureUiFocusLayout(focusScene, focusTileScene); peer.FocusTileSourceId = focusTileScene.LcdSourceId;
                    }
                    peer.IsFocus = widget.ActionKind == "focus" || continueFocus;
                    peer.OpenCaller = display.CallerId; peer.OpenTarget = display.TargetId;
                    peer.OpenRevision = display.Revision; peer.OpenBundle = widget.Argument;
                    peer.OpenCharacter = character.EntityId; peer.OpenTile = tileId; peer.OpenExpires = _ticks + 1800;
                }
                else if (opened) peer.OpenExpires = _ticks + 1800;
                ack.Accepted = true; ack.ActionKind = widget.ActionKind; ack.Argument = widget.Argument; ack.SourceTileId = tileId;
            }
            catch (Exception) { /* Fail closed after permission, entity or physics changes. */ }
            return ack;
        }

        bool UiFocusGrantContext(UiPeer peer, IMyPlayer player, UiDisplay display)
        {
            try
            {
                if (!peer.IsFocus || peer.OpenBundle == null || _ticks > peer.OpenExpires || display == null
                    || display.CallerId != peer.OpenCaller || display.TargetId != peer.OpenTarget || display.Revision != peer.OpenRevision) return false;
                var character = player.Character;
                if (character == null || character.Closed || character.IsDead || character.EntityId != peer.OpenCharacter
                    || player.IdentityId == 0 || player.Controller == null || !ReferenceEquals(player.Controller.ControlledEntity, character)
                    || !ReferenceEquals(MyAPIGateway.Entities.GetEntityById(character.EntityId), character)) return false;
                var caller = MyAPIGateway.Entities.GetEntityById(peer.OpenCaller) as IMyProgrammableBlock;
                var anchor = MyAPIGateway.Entities.GetEntityById(peer.OpenTarget) as IMyTerminalBlock;
                var tile = MyAPIGateway.Entities.GetEntityById(peer.OpenTile) as IMyTerminalBlock;
                if (caller == null || anchor == null || tile == null || !anchor.IsWorking || !tile.IsWorking) return false;
                Authorize(caller, anchor); Authorize(caller, tile);
                if (!caller.HasPlayerAccess(player.IdentityId) || !anchor.HasPlayerAccess(player.IdentityId) || !tile.HasPlayerAccess(player.IdentityId)
                    || Vector3D.DistanceSquared(character.GetPosition(), tile.GetPosition()) > 25) return false;
                Scene scene, tileScene;
                if (!_scenes.TryGetValue(display.TargetId, out scene) || !_scenes.TryGetValue(peer.OpenTile, out tileScene)
                    || tileScene.ConsoleId != display.TargetId && tileScene.LcdSourceId != display.TargetId || LocalView(scene) != peer.FocusView) return false;
                var layout = CaptureUiFocusLayout(scene, tileScene);
                if (peer.FocusLayout == null || peer.FocusTileSourceId != tileScene.LcdSourceId) return false;
                for (int i = 0; i < layout.Length; i++) if (layout[i] != peer.FocusLayout[i]) return false;
                var source = GetUiWidget(peer.OpenCaller, peer.OpenTarget, peer.FocusSourceWidget, false);
                if (source == null || source.ActionKind != "focus" || !HasVisibleUiWidget(display, source, false)) return false;
                var head = character.GetHeadMatrix(true, true, true, true);
                var point = Vector3D.Transform(peer.FocusLocal, tile.WorldMatrix);
                var toward = point - head.Translation;
                if (toward.LengthSquared() > 36 || toward.LengthSquared() < 1e-8 || Vector3D.Dot(Vector3D.Normalize(toward), head.Forward) < .25) return false;
                return UiUnoccluded(character.EntityId, tile, head.Translation, point);
            }
            catch (Exception) { return false; }
        }
        static double[] CaptureUiFocusLayout(Scene source, Scene tile)
        { return new double[] { source.LcdWidth, source.LcdHeight, source.LcdColumns, source.LcdRows,
            tile.LcdWidth, tile.LcdHeight, tile.LcdColumns, tile.LcdRows, tile.LcdColumn, tile.LcdRow }; }

        bool UiUnoccluded(long characterId, IMyTerminalBlock anchor, Vector3D origin, Vector3D hit)
        {
            if (MyAPIGateway.Physics == null) return false;
            var hits = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(origin, hit, hits);
            double end = Vector3D.Distance(origin, hit);
            foreach (var obstruction in hits)
            {
                if (obstruction.HitEntity == null) continue;
                if (obstruction.HitEntity.EntityId == characterId) continue;
                // The screen surface itself sits slightly inside its owning block's collision shape.
                if (anchor is IMyTextPanel && end - Vector3D.Distance(origin, obstruction.Position) <= 0.15)
                {
                    if (obstruction.HitEntity.EntityId == anchor.EntityId) continue;
                    if (obstruction.HitEntity.EntityId == anchor.CubeGrid.EntityId)
                    {
                        var inward = obstruction.Position + Vector3D.Normalize(hit - origin) * .01;
                        var slim = anchor.CubeGrid.GetCubeBlock(anchor.CubeGrid.WorldToGridInteger(inward));
                        if (slim != null && slim.FatBlock != null && slim.FatBlock.EntityId == anchor.EntityId) continue;
                    }
                }
                if (end - Vector3D.Distance(origin, obstruction.Position) > 0.03) return false;
            }
            return true;
        }
    }
}

