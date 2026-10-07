using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;
using VRageMath;

namespace HoloMap
{
    [ProtoContract]
    public sealed class HoloFrame
    {
        [ProtoMember(1)] public bool Request;
        [ProtoMember(2)] public long Revision;
        [ProtoMember(3)] public int Index;
        [ProtoMember(4)] public int Count;
        [ProtoMember(5)] public int Length;
        [ProtoMember(6)] public byte[] Payload;
        [ProtoMember(7)] public long BaseRevision;
        [ProtoMember(8)] public bool Delta;
        [ProtoMember(9)] public int Protocol;
    }
    [ProtoContract]
    public sealed class HoloSnapshot
    {
        [ProtoMember(1)] public List<HoloSceneData> Scenes = new List<HoloSceneData>();
        [ProtoMember(2)] public bool Delta;
        [ProtoMember(3)] public long BaseRevision;
        [ProtoMember(4)] public List<long> RemovedScenes = new List<long>();
        [ProtoMember(5)] public long ServerTick;
        [ProtoMember(6)] public List<UiDisplay> Ui;
        [ProtoMember(7)] public bool UiChanged;
    }
    [ProtoContract]
    public sealed class HoloSceneData
    {
        [ProtoMember(1)] public long ConsoleId;
        [ProtoMember(2)] public double[] View;
        [ProtoMember(3)] public List<HoloItemData> Items = new List<HoloItemData>();
        [ProtoMember(4)] public long RootId;
        [ProtoMember(5)] public long TrackingCallerId;
        [ProtoMember(6)] public double[] ShipView;
        [ProtoMember(7)] public double MetresPerUnit;
        [ProtoMember(8)] public double[] ShipOrigin;
        [ProtoMember(9)] public List<HoloLabelData> Labels = new List<HoloLabelData>();
        [ProtoMember(10)] public List<HoloLayerData> Layers = new List<HoloLayerData>();
        [ProtoMember(11)] public string ConstructLayer;
        [ProtoMember(12)] public List<HoloAnimationData> Animations = new List<HoloAnimationData>();
        [ProtoMember(13)] public bool Partial;
        [ProtoMember(14)] public List<HoloItemKey> RemovedItems = new List<HoloItemKey>();
        [ProtoMember(15)] public double[] Volume;
        [ProtoMember(16)] public HoloLcdData Lcd;
        [ProtoMember(17)] public List<HoloProjectedScreenData> Screens;
        [ProtoMember(18)] public HoloRenderBudgetData Budget;
    }
    [ProtoContract]
    public sealed class HoloLcdData
    {
        [ProtoMember(1)] public int Columns=1;
        [ProtoMember(2)] public int Rows=1;
        [ProtoMember(3)] public int Column;
        [ProtoMember(4)] public int Row;
        [ProtoMember(5)] public double Width=2;
        [ProtoMember(6)] public double Height=2;
        [ProtoMember(7)] public long SourceId;
        [ProtoMember(8)] public long CallerId;
        [ProtoMember(9)] public int Renderer;
        [ProtoMember(10)] public double RefreshHz=6;
    }
    [ProtoContract]
    public sealed class HoloItemKey
    {
        [ProtoMember(1)] public long CallerId;
        [ProtoMember(2)] public string Id;
    }
    [ProtoContract]
    public sealed class HoloLayerData
    {
        [ProtoMember(1)] public long CallerId;
        [ProtoMember(2)] public string Name;
        [ProtoMember(3)] public bool Visible;
        [ProtoMember(4)] public float Opacity;
        [ProtoMember(5)] public int Order;
    }
    [ProtoContract]
    public sealed class HoloLabelData
    {
        [ProtoMember(1)] public long CallerId;
        [ProtoMember(2)] public string Id;
        [ProtoMember(3)] public string Text;
        [ProtoMember(4)] public double[] Position;
        [ProtoMember(5)] public float[] Style;
        [ProtoMember(6)] public bool Visible;
        [ProtoMember(7)] public string Layer;
        [ProtoMember(8)] public float? Opacity;
        [ProtoMember(9)] public float Emission;
    }
    [ProtoContract]
    public sealed class HoloItemData
    {
        [ProtoMember(1)] public long CallerId;
        [ProtoMember(2)] public string Id;
        [ProtoMember(3)] public double[] Points;
        [ProtoMember(4)] public int[] Edges = new int[0];
        [ProtoMember(5)] public int[] Triangles = new int[0];
        [ProtoMember(6)] public float[] Style;
        [ProtoMember(7)] public double[] Transform;
        [ProtoMember(8)] public bool Visible;
        [ProtoMember(9)] public bool Shaded;
        [ProtoMember(10)] public string Layer;
        [ProtoMember(11)] public float[] TriangleColors;
        [ProtoMember(12)] public float[] UV;
        [ProtoMember(13)] public string Material;
        [ProtoMember(14)] public float? Opacity;
        [ProtoMember(15)] public float[] EdgeColors;
        [ProtoMember(16)] public float Emission;
        [ProtoMember(17)] public bool ReuseGeometry;
        [ProtoMember(18)] public HoloGeometryDeclaration Declaration;
        [ProtoMember(19)] public HoloEffectDeclaration Effects;
    }

    public sealed partial class HoloMapSession
    {
        const ushort NetworkChannel = 49783;
        const int NetworkProtocol = 18;
        const int ChunkBytes = 2800, MaxSnapshotBytes = 2 * 1024 * 1024;
        long _revision, _receivedRevision;
        HoloSnapshot _sentSnapshot, _networkSnapshot;
        int _lastRequestTick = -600, _lastServerRequestTick = -600;
        bool _needsSnapshot = true;
        ulong _pendingRecovery;
        bool _recoveryReady;
        readonly Dictionary<long, AssemblyState> _assemblies = new Dictionary<long, AssemblyState>();
        readonly Dictionary<ulong, int> _requestTicks = new Dictionary<ulong, int>();
        readonly Queue<Outgoing> _outgoing = new Queue<Outgoing>();
        sealed class AssemblyState
        {
            public int Count, Length, Started, Received;
            public long BaseRevision; public bool Delta;
            public byte[][] Chunks;
        }
        sealed class Outgoing { public byte[] Data; public ulong Recipient; }

        void ResetNetworkState()
        {
            _sentSnapshot = null; _networkSnapshot = null; _revision = 0; _receivedRevision = 0;
            _lastRequestTick = -600; _lastServerRequestTick = -600; _needsSnapshot = true;
            _pendingRecovery = 0; _recoveryReady = false;
            _assemblies.Clear(); _requestTicks.Clear(); _outgoing.Clear();
        }

        void RequestSnapshot()
        {
            if (_ticks - _lastRequestTick < 120) return;
            _lastRequestTick = _ticks;
            MyAPIGateway.Multiplayer.SendMessageToServer(NetworkChannel,
                MyAPIGateway.Utilities.SerializeToBinary(new HoloFrame { Request = true, Protocol = NetworkProtocol }), true);
        }
        void TickNetwork()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                if (_dirty && _ticks % 30 == 0 && _outgoing.Count == 0)
                {
                    SendSnapshot(0); _dirty = false; if (_pendingRecovery != 0) _recoveryReady = true;
                }
                for (int i = 0; i < 8 && _outgoing.Count > 0; i++)
                {
                    var packet = _outgoing.Dequeue();
                    if (packet.Recipient == 0) MyAPIGateway.Multiplayer.SendMessageToOthers(NetworkChannel, packet.Data, true);
                    else MyAPIGateway.Multiplayer.SendMessageTo(NetworkChannel, packet.Data, packet.Recipient, true);
                }
                // One bounded pending recipient gives broadcast changes priority, then
                // guarantees the recovery response a turn even under continuous writes.
                if (_pendingRecovery != 0 && _outgoing.Count == 0 && (_recoveryReady || !_dirty))
                {
                    ulong recipient = _pendingRecovery; _pendingRecovery = 0; _recoveryReady = false;
                    SendSnapshot(recipient);
                }
            }
            else if (_needsSnapshot && _ticks - _lastRequestTick >= 120) RequestSnapshot();
            if (MyAPIGateway.Multiplayer.IsServer && _ticks % 600 == 0) PruneNetworkRequests();
            var expired = new List<long>();
            foreach (var entry in _assemblies) if (_ticks - entry.Value.Started > 1200) expired.Add(entry.Key);
            foreach (long id in expired) { _assemblies.Remove(id); _needsSnapshot = true; }
        }

        List<HoloProjectedScreenData> FreezeProjectedScreens(Scene scene)
        {
            var result = new List<HoloProjectedScreenData>();
            foreach (var data in ExportProjectedScreens(scene))
                result.Add(CloneScreen(data, true));
            return result;
        }

        HoloSnapshot CaptureSnapshot()
        {
            var snapshot = new HoloSnapshot { ServerTick = _ticks, Ui = CaptureUiDisplays() };
            foreach (var scene in _scenes.Values)
            {
                var data = new HoloSceneData { ConsoleId = scene.ConsoleId,
                    View = new[] { scene.Offset.X, scene.Offset.Y, scene.Offset.Z, scene.Rotation.X, scene.Rotation.Y, scene.Rotation.Z, scene.Scale },
                    RootId = scene.TrackedRootId, TrackingCallerId = scene.TrackingCallerId,
                    ConstructLayer = scene.ConstructLayer, Animations = ExportDrawAnimations(scene.ConsoleId),
                    Volume = ExportDisplayVolume(scene), Lcd = ExportLcdLayout(scene), Screens = FreezeProjectedScreens(scene), Budget = ExportRenderBudget(scene),
                    MetresPerUnit = scene.ShipMetresPerUnit,
                    ShipOrigin = new[] { scene.ShipOrigin.X, scene.ShipOrigin.Y, scene.ShipOrigin.Z },
                    ShipView = new[] { scene.ShipRadius, scene.ShipOffset.X, scene.ShipOffset.Y, scene.ShipOffset.Z } };
                foreach (var item in scene.Items.Values)
                {
                    var points = new double[item.Geometry.Points.Length * 3];
                    for (int i = 0; i < item.Geometry.Points.Length; i++)
                    { var p = item.Geometry.Points[i]; points[i * 3] = p.X; points[i * 3 + 1] = p.Y; points[i * 3 + 2] = p.Z; }
                    data.Items.Add(new HoloItemData { CallerId = item.CallerId, Id = item.Id, Points = points,
                        Edges = item.Geometry.Edges, Triangles = item.Geometry.Triangles,
                        Style = new[] { item.LineColor.X, item.LineColor.Y, item.LineColor.Z, item.LineColor.W,
                            item.FillColor.X, item.FillColor.Y, item.FillColor.Z, item.FillColor.W, item.Thickness },
                        Transform = MatrixValues(item.Transform), Visible = item.Visible, Shaded = item.Shaded, Layer = item.Layer,
                        TriangleColors = ColorValues(item.TriangleColors,item.FillColor), UV = UvValues(item.UV), Material = item.Material, Opacity = item.Opacity,
                        EdgeColors=ColorValues(item.EdgeColors,item.LineColor),Emission=item.Emission, Declaration = ExportGeometryDeclaration(item),Effects=ExportHologramEffects(item) });
                }
                foreach (var label in scene.Labels.Values)
                    data.Labels.Add(new HoloLabelData { CallerId = label.CallerId, Id = label.Id, Text = label.Text,
                        Position = new[] { label.Position.X, label.Position.Y, label.Position.Z },
                        Style = new[] { label.Color.X, label.Color.Y, label.Color.Z, label.Color.W, label.Height }, Visible = label.Visible, Layer = label.Layer,Opacity=label.Opacity,Emission=label.Emission });
                foreach (var layer in scene.Layers.Values)
                    data.Layers.Add(new HoloLayerData { CallerId = layer.CallerId, Name = layer.Name, Visible = layer.Visible, Opacity = layer.Opacity, Order = layer.Order });
                snapshot.Scenes.Add(data);
            }
            return snapshot;
        }

        void SendSnapshot(ulong recipient)
        {
            if (_outgoing.Count > 0) return;
            HoloSnapshot snapshot; long revision;
            if (recipient != 0 && _sentSnapshot != null)
            {
                // Targeted recovery cannot advance the published broadcast baseline.
                snapshot = new HoloSnapshot { Scenes = _sentSnapshot.Scenes, ServerTick = _ticks }; revision = _revision;
            }
            else
            {
                var current = CaptureSnapshot();
                snapshot = recipient == 0 && _sentSnapshot != null ? BuildDelta(_sentSnapshot, current, _revision) : current;
                revision = _revision + 1;
                QueueSnapshot(snapshot, recipient, revision);
                _sentSnapshot = current; _revision = revision;
                return;
            }
            QueueSnapshot(snapshot, recipient, revision);
        }

        void QueueSnapshot(HoloSnapshot snapshot, ulong recipient, long revision)
        {
            if (_outgoing.Count != 0) throw new InvalidOperationException("Network publication still pending.");
            byte[] bytes = MyAPIGateway.Utilities.SerializeToBinary(snapshot);
            if (bytes.Length > MaxSnapshotBytes) throw new InvalidOperationException("Holo Map snapshot exceeds network budget.");
            int count = Math.Max(1, (bytes.Length + ChunkBytes - 1) / ChunkBytes);
            var publication = new List<Outgoing>(count); int totalBytes = 0;
            for (int i = 0; i < count; i++)
            {
                int length = Math.Min(ChunkBytes, bytes.Length - i * ChunkBytes);
                var chunk = new byte[length]; Array.Copy(bytes, i * ChunkBytes, chunk, 0, length);
                var frame = new HoloFrame { Protocol = NetworkProtocol, Revision = revision, BaseRevision = snapshot.BaseRevision, Delta = snapshot.Delta,
                    Index = i, Count = count, Length = bytes.Length, Payload = chunk };
                var data = MyAPIGateway.Utilities.SerializeToBinary(frame); totalBytes += data.Length;
                if (data.Length > 4096 || totalBytes > MaxSnapshotBytes + 65536) throw new InvalidOperationException("Outgoing queue budget exceeded.");
                publication.Add(new Outgoing { Recipient = recipient, Data = data });
            }
            foreach (var packet in publication) _outgoing.Enqueue(packet);
        }

        void PruneNetworkRequests()
        {
            var players = new List<VRage.Game.ModAPI.IMyPlayer>(); MyAPIGateway.Players.GetPlayers(players);
            var active = new HashSet<ulong>(); foreach (var player in players) active.Add(player.SteamUserId);
            var removed = new List<ulong>(); foreach (var sender in _requestTicks.Keys) if (!active.Contains(sender)) removed.Add(sender);
            foreach (ulong sender in removed) _requestTicks.Remove(sender);
        }

        void Receive(ushort channel, byte[] bytes, ulong sender, bool fromServer)
        {
            if (channel != NetworkChannel || bytes == null || bytes.Length > 4096) return;
            // Authentication precedes envelope and expanded-content deserialization.
            // SE identifies dedicated-server secure messages with sender zero.
            if (!MyAPIGateway.Multiplayer.IsServer && (!fromServer || (sender != 0 && sender != MyAPIGateway.Multiplayer.ServerId))) return;
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                // Rate-limit even malformed envelopes before deserialization. A connected
                // peer cannot make exception logging/parsing run every simulation tick.
                if (fromServer || sender == 0 || _outgoing.Count != 0 || _ticks - _lastServerRequestTick < 30) return;
                int last;
                if (_requestTicks.TryGetValue(sender, out last) && _ticks - last < 120) return;
                var players = new List<VRage.Game.ModAPI.IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players, player => player.SteamUserId == sender);
                if (players.Count == 0 || (!_requestTicks.ContainsKey(sender) && _requestTicks.Count >= 128)) return;
                _requestTicks[sender] = _ticks;
            }
            try
            {
                var frame = MyAPIGateway.Utilities.SerializeFromBinary<HoloFrame>(bytes);
                if (frame == null || frame.Protocol != NetworkProtocol) return;
                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    if (!frame.Request || fromServer || _outgoing.Count != 0 || sender == 0
                        || frame.Payload != null || frame.Revision != 0 || frame.Count != 0 || frame.Length != 0
                        || frame.Index != 0 || frame.BaseRevision != 0 || frame.Delta || _ticks - _lastServerRequestTick < 30) return;
                    _lastServerRequestTick = _ticks;
                    if (_dirty && _sentSnapshot != null)
                    {
                        if (_pendingRecovery == 0) _pendingRecovery = sender;
                        return;
                    }
                    SendSnapshot(sender);
                    return;
                }
                // Clients never accept geometry supplied by another client.
                if (!fromServer || frame.Request || frame.Revision <= _receivedRevision || frame.Revision <= 0
                    || frame.Length < 0 || frame.Length > MaxSnapshotBytes
                    || frame.Count != Math.Max(1, (frame.Length + ChunkBytes - 1) / ChunkBytes)
                    || frame.Index < 0 || frame.Index >= frame.Count || frame.Payload == null
                    || frame.Payload.Length != Math.Min(ChunkBytes, frame.Length - frame.Index * ChunkBytes)) return;
                if (frame.Delta && (frame.BaseRevision != _receivedRevision || _networkSnapshot == null))
                { _needsSnapshot = true; RequestSnapshot(); return; }
                if ((!frame.Delta && frame.BaseRevision != 0) || (frame.Delta && (frame.BaseRevision <= 0 || frame.Revision != frame.BaseRevision + 1))) return;
                AssemblyState state;
                if (!_assemblies.TryGetValue(frame.Revision, out state))
                {
                    if (_assemblies.Count >= 2) return;
                    state = new AssemblyState { Count = frame.Count, Length = frame.Length, Started = _ticks, BaseRevision = frame.BaseRevision, Delta = frame.Delta, Chunks = new byte[frame.Count][] };
                    _assemblies.Add(frame.Revision, state);
                }
                if (state.Count != frame.Count || state.Length != frame.Length || state.BaseRevision != frame.BaseRevision || state.Delta != frame.Delta || state.Chunks[frame.Index] != null) return;
                state.Chunks[frame.Index] = frame.Payload; state.Received++;
                if (state.Received != state.Count) return;
                var assembled = new byte[state.Length];
                for (int i = 0; i < state.Count; i++) Array.Copy(state.Chunks[i], 0, assembled, i * ChunkBytes, state.Chunks[i].Length);
                var snapshot = MyAPIGateway.Utilities.SerializeFromBinary<HoloSnapshot>(assembled);
                if (snapshot == null || snapshot.Delta != frame.Delta || snapshot.BaseRevision != frame.BaseRevision)
                    throw new ArgumentException("Snapshot envelope mismatch.");
                var complete = snapshot.Delta ? MergeDelta(_networkSnapshot, snapshot, _receivedRevision) : snapshot;
                ApplySnapshot(complete); _networkSnapshot = complete;
                SetAnimationClock(complete.ServerTick);
                foreach (var data in complete.Scenes) ImportDrawAnimations(data.Animations, data.ConsoleId);
                var present = new HashSet<long>(); foreach (var data in complete.Scenes) present.Add(data.ConsoleId);
                SynchronizeDrawAnimationScenes(present);
                _receivedRevision = frame.Revision; _needsSnapshot = false;
                _assemblies.Clear();
            }
            catch (Exception ex) { _assemblies.Clear(); _needsSnapshot = true; if (VRage.Utils.MyLog.Default != null) VRage.Utils.MyLog.Default.WriteLineAndConsole("HDR API network: " + ex.Message); }
        }

        void ApplySnapshot(HoloSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Scenes == null || snapshot.Scenes.Count > MaxConsoles || snapshot.Delta || snapshot.BaseRevision != 0
                || snapshot.ServerTick < 0 || snapshot.ServerTick > int.MaxValue || snapshot.RemovedScenes == null || snapshot.RemovedScenes.Count != 0) throw new ArgumentException("Invalid snapshot.");
            var replacement = new Dictionary<long, Scene>();
            int animationCount = 0, packedCount = 0, projectedScreenCount = 0; long sourceCharacters = 0, artworkCharacters = 0;
            foreach (var data in snapshot.Scenes)
            {
                if (data == null || data.Partial || data.RemovedItems == null || data.RemovedItems.Count != 0) throw new ArgumentException("Incomplete scene.");
                ValidateDrawAnimations(data.Animations, data.ConsoleId);
                if (data.Animations != null) foreach (var animation in data.Animations)
                {
                    animationCount++; if (animation.Packed != null) { packedCount++; sourceCharacters += animation.Packed.Length; }
                    if (animation.Frames != null) foreach (var frame in animation.Frames) sourceCharacters += frame.Length;
                }
                if (animationCount > 128 || packedCount > 8 || sourceCharacters > 480000) throw new ArgumentException("Global animation budget exceeded.");
                if (data.View == null || data.View.Length != 7 || data.Items == null || data.Items.Count > MaxObjects) throw new ArgumentException("Invalid scene.");
                foreach (double value in data.View) if (!Geometry.Finite(value)) throw new ArgumentException("Nonfinite view.");
                var scene = new Scene { ConsoleId = data.ConsoleId, Offset = new Vector3D(data.View[0], data.View[1], data.View[2]),
                    Rotation = new Vector3D(data.View[3], data.View[4], data.View[5]), Scale = data.View[6] };
                if (scene.Scale < 1e-6 || scene.Scale > 1000 || scene.Offset.LengthSquared() > 100) throw new ArgumentException("Invalid map view.");
                ImportDisplayConfiguration(scene,data);ImportRenderBudget(scene,data.Budget);
                scene.ConstructLayer = string.IsNullOrEmpty(data.ConstructLayer) ? "ship" : LayerRules.Name(data.ConstructLayer);
                if (data.Layers != null)
                {
                    if (data.Layers.Count > 32) throw new ArgumentException("Layer budget exceeded.");
                    foreach (var layer in data.Layers)
                    {
                        string name = LayerRules.Name(layer.Name); LayerRules.Opacity(layer.Opacity);
                        if(layer.Order < -1024 || layer.Order > 1024)throw new ArgumentException("Invalid layer order.");
                        scene.Layers.Add(Key(layer.CallerId, name), new Layer { CallerId = layer.CallerId, Name = name, Visible = layer.Visible, Opacity = layer.Opacity, Order = layer.Order });
                    }
                }
                if (data.RootId != 0)
                {
                    if (data.ShipView == null || data.ShipView.Length != 4) throw new ArgumentException("Invalid live construct view.");
                    foreach (double value in data.ShipView) if (!Geometry.Finite(value)) throw new ArgumentException("Invalid live construct view.");
                    scene.ShipRadius = data.ShipView[0];
                    scene.ShipOffset = new Vector3D(data.ShipView[1], data.ShipView[2], data.ShipView[3]);
                    if (scene.ShipRadius < 0.05 || scene.ShipRadius > 5 || scene.ShipOffset.LengthSquared() > 100) throw new ArgumentException("Invalid live construct view.");
                    scene.TrackedRootId = data.RootId; scene.TrackingCallerId = data.TrackingCallerId;
                    if (!Geometry.Finite(data.MetresPerUnit) || data.MetresPerUnit < 0 || data.MetresPerUnit > 1000000
                        || (data.MetresPerUnit > 0 && data.MetresPerUnit < 0.001)) throw new ArgumentException("Invalid map scale.");
                    scene.ShipMetresPerUnit = data.MetresPerUnit;
                    if (data.MetresPerUnit > 0)
                    {
                        if (data.ShipOrigin == null || data.ShipOrigin.Length != 3) throw new ArgumentException("Invalid ship origin.");
                        foreach (double value in data.ShipOrigin) if (!Geometry.Finite(value)) throw new ArgumentException("Invalid ship origin.");
                        scene.ShipOrigin = new Vector3D(data.ShipOrigin[0], data.ShipOrigin[1], data.ShipOrigin[2]);
                        if (scene.ShipOrigin.LengthSquared() > 1e12) throw new ArgumentException("Invalid ship origin.");
                    }
                }
                int totalPoints = 0, totalPrimitives = 0;
                foreach (var item in data.Items)
                {
                    if (item == null || item.ReuseGeometry) throw new ArgumentException("Incomplete item.");
                    ValidateId(item.Id); ValidateGeometryDeclaration(item.Declaration);
                    if (item.Declaration != null)
                    {
                        if (item.Declaration.SvgSource != null) artworkCharacters += item.Declaration.SvgSource.Length;
                        if (item.Declaration.Text != null) artworkCharacters += item.Declaration.Text.Length;
                        if (artworkCharacters > 262144) throw new ArgumentException("Global artwork source budget exceeded.");
                    }
                    if (item.Points == null || item.Points.Length % 3 != 0 || item.Points.Length > Geometry.MaxPoints * 3
                        || item.Edges == null || item.Edges.Length % 2 != 0 || item.Edges.Length > Geometry.MaxPrimitives * 2
                        || item.Triangles == null || item.Triangles.Length % 3 != 0 || item.Triangles.Length > Geometry.MaxPrimitives * 3
                        || item.Style == null || item.Style.Length != 9) throw new ArgumentException("Invalid mesh packet.");
                    var points = new Vector3D[item.Points.Length / 3];
                    for (int i = 0; i < points.Length; i++) points[i] = new Vector3D(item.Points[i * 3], item.Points[i * 3 + 1], item.Points[i * 3 + 2]);
                    Geometry.ValidatePoints(points);
                    foreach (int index in item.Edges) if (index < 0 || index >= points.Length) throw new ArgumentException("Invalid edge index.");
                    foreach (int index in item.Triangles) if (index < 0 || index >= points.Length) throw new ArgumentException("Invalid triangle index.");
                    totalPoints += points.Length; totalPrimitives += item.Edges.Length / 2 + item.Triangles.Length / 3;
                    if (totalPoints > scene.PointBudget || totalPrimitives > scene.PrimitiveBudget) throw new ArgumentException("Scene budget exceeded.");
                    var line = new Vector4(item.Style[0], item.Style[1], item.Style[2], item.Style[3]);
                    var fill = new Vector4(item.Style[4], item.Style[5], item.Style[6], item.Style[7]);
                    Color(line); Color(fill); Width(item.Style[8]);
                    var transform = ReadMatrix(item.Transform); ValidateTransform(transform);
                    // Wire data already contains validated triangulation and original boundary edges.
                    var geometry = new Geometry(points, (int[])item.Edges.Clone(), (int[])item.Triangles.Clone());
                    FitsDisplay(geometry, transform, scene);
                    var colors = ReadColors(item.TriangleColors, geometry.Triangles.Length / 3);
                    var edgeColors = ReadColors(item.EdgeColors, geometry.Edges.Length / 2);EmissionValue(item.Emission);
                    var uv = ReadUv(item.UV, points.Length);
                    if ((item.Material == null) != (uv == null)) throw new ArgumentException("Invalid textured mesh.");
                    if (item.Material != null)
                    {
                        const string prefix = "HoloMap_Image_";
                        if (!item.Material.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("Invalid image material.");
                        ImageMaterial(item.Material.Substring(prefix.Length));
                    }
                    float opacity = item.Opacity ?? 1; LayerRules.Opacity(opacity);
                    var receivedItem = new Item { CallerId = item.CallerId, Id = item.Id,
                        Geometry = geometry, LineColor = line, FillColor = fill, Thickness = item.Style[8], Transform = transform,
                        TriangleColors = colors,EdgeColors=edgeColors,Emission=item.Emission, UV = uv, Material = item.Material, Opacity = opacity,
                        Shaded = item.Shaded, Visible = item.Visible, Layer = string.IsNullOrEmpty(item.Layer) ? "" : LayerRules.Name(item.Layer) };
                    receivedItem.ContentIdentity = item.Points;
                    ImportHologramEffects(receivedItem,item.Effects);
                    ImportGeometryDeclaration(receivedItem, item.Declaration);
                    scene.Items.Add(Key(item.CallerId, item.Id), receivedItem);
                }
                if (data.Labels != null)
                {
                    if (data.Labels.Count > 16) throw new ArgumentException("Invalid label count.");
                    int characters = 0;
                    foreach (var label in data.Labels)
                    {
                        ValidateId(label.Id);
                        if (label.Position == null || label.Position.Length != 3 || label.Style == null || label.Style.Length != 5)
                            throw new ArgumentException("Invalid label packet.");
                        var position = new Vector3D(label.Position[0], label.Position[1], label.Position[2]);
                        var color = new Vector4(label.Style[0], label.Style[1], label.Style[2], label.Style[3]);
                        LabelStyle(position, label.Text, color, label.Style[4]);
                        LayerRules.Opacity(label.Opacity??1);EmissionValue(label.Emission);
                        characters += label.Text.Length;
                        if (characters > 256 || (Vector3D.Transform(position, LocalView(scene)) - scene.Offset).LengthSquared() > MaxDisplayRadius * MaxDisplayRadius)
                            throw new ArgumentException("Label budget exceeded.");
                        scene.Labels.Add(Key(label.CallerId, label.Id), new Label { CallerId = label.CallerId, Id = label.Id,
                            Text = label.Text, Position = position, Color = color, Height = label.Style[4], Visible = label.Visible,
                            Opacity=label.Opacity??1,Emission=label.Emission,
                            Layer = string.IsNullOrEmpty(label.Layer) ? "" : LayerRules.Name(label.Layer) });
                    }
                }
                ImportProjectedScreens(scene, data.Screens);
                foreach(var screen in scene.Screens.Values)foreach(var item in scene.Items.Values)if(item.CallerId==screen.Data.CallerId&&item.Id.StartsWith(ScreenPrefix(screen.Data.Id),StringComparison.Ordinal))ValidateProjectedHologramEffects(item.Effects,screen.Data);
                projectedScreenCount += scene.Screens.Count;
                int projectedPoints, projectedPrimitives; ProjectedSourceCounts(scene, out projectedPoints, out projectedPrimitives);
                if (projectedScreenCount > MaxProjectedScreens || totalPoints + projectedPoints > scene.PointBudget
                    || totalPrimitives + projectedPrimitives > scene.PrimitiveBudget) throw new ArgumentException("Projected screen scene budget exceeded.");
                replacement.Add(scene.ConsoleId, scene);
            }
            long replicationBytes = 32768 + UiRules.ReplicationReserve;
            foreach (var scene in replacement.Values)
            {
                replicationBytes += ProjectedSourceBytes(scene);
                foreach (var item in scene.Items.Values) replicationBytes += ReplicationItemBytes(item);
            }
            foreach (var data in snapshot.Scenes) if (data.Animations != null)
                foreach (var animation in data.Animations) replicationBytes += AnimationBytes(animation);
            if (replicationBytes > 1572864) throw new ArgumentException("Global display replication budget exceeded.");
            ValidateLcdGroups(replacement);
            var validatedUi = ValidateUiDisplays(snapshot.Ui,replacement);
            foreach(var pair in replacement){Scene prior;if(_scenes.TryGetValue(pair.Key,out prior)&&(prior.PointBudget!=pair.Value.PointBudget||prior.PrimitiveBudget!=pair.Value.PrimitiveBudget||prior.DrawWorkBudget!=pair.Value.DrawWorkBudget))InvalidateSceneRenderBudget(pair.Key);}
            _scenes.Clear();
            foreach (var pair in replacement) _scenes.Add(pair.Key, pair.Value);
            ApplyValidatedUiDisplays(validatedUi);
        }

        static float[] ColorValues(Vector4[] colors,Vector4? uniform=null)
        {
            if (colors == null) return null;
            if(uniform.HasValue){bool same=true;foreach(var color in colors)if(color!=uniform.Value){same=false;break;}if(same)return null;}
            var values = new float[colors.Length * 4];
            for (int i=0;i<colors.Length;i++) { var c=colors[i]; values[i*4]=c.X; values[i*4+1]=c.Y; values[i*4+2]=c.Z; values[i*4+3]=c.W; }
            return values;
        }
        static Vector4[] ReadColors(float[] values,int count)
        {
            if (values == null) return null; if(values.Length!=count*4)throw new ArgumentException("Invalid SVG color data.");
            var colors=new Vector4[count];for(int i=0;i<count;i++){var c=new Vector4(values[i*4],values[i*4+1],values[i*4+2],values[i*4+3]);Color(c);colors[i]=c;}return colors;
        }
        static float[] UvValues(Vector2[] uv)
        { if(uv==null)return null;var values=new float[uv.Length*2];for(int i=0;i<uv.Length;i++){values[i*2]=uv[i].X;values[i*2+1]=uv[i].Y;}return values; }
        static Vector2[] ReadUv(float[] values,int count)
        {
            if(values==null)return null;if(values.Length!=count*2)throw new ArgumentException("Invalid image UV data.");
            var uv=new Vector2[count];for(int i=0;i<count;i++){float x=values[i*2],y=values[i*2+1];if(!Geometry.Finite(x)||!Geometry.Finite(y)||x<0||x>1||y<0||y>1)throw new ArgumentException("Invalid image UV coordinates.");uv[i]=new Vector2(x,y);}return uv;
        }
        static double[] MatrixValues(MatrixD m)
        { return new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 }; }
        static MatrixD ReadMatrix(double[] a)
        {
            if (a == null || a.Length != 16) throw new ArgumentException("Invalid transform.");
            return new MatrixD(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8], a[9], a[10], a[11], a[12], a[13], a[14], a[15]);
        }
    }
}
