using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        // DTOs captured by the server are immutable publication baselines. A delta
        // carries complete scene metadata, changed items, and explicit tombstones.
        static HoloSnapshot BuildDelta(HoloSnapshot before, HoloSnapshot after, long revision)
        {
            var delta = new HoloSnapshot { Delta = true, BaseRevision = revision, ServerTick = after.ServerTick };
            if(!SameBytes(before.Ui,after.Ui)){delta.UiChanged=true;delta.Ui=after.Ui;}
            var oldScenes = SceneMap(before);
            var newScenes = SceneMap(after);
            foreach (var pair in oldScenes) if (!newScenes.ContainsKey(pair.Key)) delta.RemovedScenes.Add(pair.Key);
            foreach (var pair in newScenes)
            {
                HoloSceneData old;
                if (!oldScenes.TryGetValue(pair.Key, out old)) { delta.Scenes.Add(pair.Value); continue; }
                var changed = SceneHeader(pair.Value); changed.Partial = true;
                var oldItems = ItemMap(old.Items); var newItems = ItemMap(pair.Value.Items);
                foreach (var item in oldItems)
                    if (!newItems.ContainsKey(item.Key)) changed.RemovedItems.Add(new HoloItemKey { CallerId = item.Value.CallerId, Id = item.Value.Id });
                foreach (var item in newItems)
                {
                    HoloItemData previous;
                    if (!oldItems.TryGetValue(item.Key, out previous)) { changed.Items.Add(item.Value); continue; }
                    if (!SameGeometry(previous, item.Value)) { changed.Items.Add(item.Value); continue; }
                    if (!SameBytes(ItemMetadata(previous), ItemMetadata(item.Value)))
                    {
                        var metadata = ItemMetadata(item.Value); metadata.ReuseGeometry = true; changed.Items.Add(metadata);
                    }
                }
                if (changed.Items.Count != 0 || changed.RemovedItems.Count != 0 || !SameBytes(SceneHeader(old), SceneHeader(pair.Value)))
                    delta.Scenes.Add(changed);
            }
            return delta;
        }

        static HoloSnapshot MergeDelta(HoloSnapshot baseline, HoloSnapshot delta, long revision)
        {
            if (baseline == null || delta == null || !delta.Delta || delta.BaseRevision != revision
                || delta.Scenes == null || delta.Scenes.Count > MaxConsoles || delta.RemovedScenes == null
                || delta.RemovedScenes.Count > MaxConsoles) throw new ArgumentException("Invalid delta baseline.");
            var scenes = SceneMap(baseline); var touched = new HashSet<long>();
            foreach (long id in delta.RemovedScenes)
            {
                if (!touched.Add(id) || !scenes.Remove(id)) throw new ArgumentException("Invalid scene removal.");
            }
            foreach (var change in delta.Scenes)
            {
                if (change == null || !touched.Add(change.ConsoleId) || change.Items == null || change.Items.Count > MaxObjects
                    || change.RemovedItems == null || change.RemovedItems.Count > MaxObjects) throw new ArgumentException("Invalid scene delta.");
                if (!change.Partial)
                {
                    if (change.RemovedItems.Count != 0 || scenes.ContainsKey(change.ConsoleId)) throw new ArgumentException("Invalid scene creation.");
                    foreach (var item in change.Items) if (item == null || item.ReuseGeometry) throw new ArgumentException("Incomplete new item.");
                    scenes.Add(change.ConsoleId, change); continue;
                }
                HoloSceneData old;
                if (!scenes.TryGetValue(change.ConsoleId, out old)) throw new ArgumentException("Missing scene baseline.");
                var items = ItemMap(old.Items); var modified = new HashSet<string>();
                foreach (var key in change.RemovedItems)
                {
                    if (key == null) throw new ArgumentException("Invalid item removal.");
                    ValidateId(key.Id); var id = Key(key.CallerId, key.Id);
                    if (!modified.Add(id) || !items.Remove(id)) throw new ArgumentException("Invalid item removal.");
                }
                foreach (var item in change.Items)
                {
                    if (item == null) throw new ArgumentException("Invalid changed item.");
                    ValidateId(item.Id); var id = Key(item.CallerId, item.Id);
                    if (!modified.Add(id)) throw new ArgumentException("Duplicate changed item.");
                    if (item.ReuseGeometry)
                    {
                        HoloItemData previous;
                        if (!items.TryGetValue(id, out previous) || item.Points != null || item.Declaration != null || item.Material != null
                            || item.TriangleColors != null || item.EdgeColors != null || item.UV != null
                            || item.Edges == null || item.Edges.Length != 0 || item.Triangles == null || item.Triangles.Length != 0)
                            throw new ArgumentException("Invalid geometry reference.");
                        var complete = ItemMetadata(item);
                        complete.Points = previous.Points; complete.Edges = previous.Edges; complete.Triangles = previous.Triangles;
                        complete.TriangleColors = previous.TriangleColors; complete.EdgeColors = previous.EdgeColors;
                        complete.UV = previous.UV; complete.Material = previous.Material; complete.Declaration = previous.Declaration;
                        items[id] = complete;
                    }
                    else items[id] = item;
                }
                if (items.Count > MaxObjects) throw new ArgumentException("Item delta exceeds budget.");
                var scene = SceneHeader(change); foreach (var item in items.Values) scene.Items.Add(item);
                scenes[change.ConsoleId] = scene;
            }
            if (scenes.Count > MaxConsoles) throw new ArgumentException("Scene delta exceeds budget.");
            var result = new HoloSnapshot { ServerTick = delta.ServerTick, Ui=delta.UiChanged?delta.Ui:baseline.Ui };
            foreach (var scene in scenes.Values) result.Scenes.Add(scene);
            return result;
        }

        static Dictionary<long, HoloSceneData> SceneMap(HoloSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Scenes == null || snapshot.Scenes.Count > MaxConsoles) throw new ArgumentException("Invalid scene baseline.");
            var result = new Dictionary<long, HoloSceneData>();
            foreach (var scene in snapshot.Scenes)
            { if (scene == null || result.ContainsKey(scene.ConsoleId)) throw new ArgumentException("Duplicate scene."); result.Add(scene.ConsoleId, scene); }
            return result;
        }
        static Dictionary<string, HoloItemData> ItemMap(List<HoloItemData> items)
        {
            if (items == null || items.Count > MaxObjects) throw new ArgumentException("Invalid item baseline.");
            var result = new Dictionary<string, HoloItemData>();
            foreach (var item in items)
            {
                if (item == null) throw new ArgumentException("Invalid item baseline."); ValidateId(item.Id);
                var key = Key(item.CallerId, item.Id); if (result.ContainsKey(key)) throw new ArgumentException("Duplicate item."); result.Add(key, item);
            }
            return result;
        }
        static HoloSceneData SceneHeader(HoloSceneData s)
        {
            return new HoloSceneData { ConsoleId = s.ConsoleId, View = s.View, RootId = s.RootId,
                TrackingCallerId = s.TrackingCallerId, ShipView = s.ShipView, MetresPerUnit = s.MetresPerUnit,
                ShipOrigin = s.ShipOrigin, Labels = s.Labels, Layers = s.Layers, ConstructLayer = s.ConstructLayer, Animations = s.Animations,
                Volume = s.Volume, Lcd = s.Lcd, Screens = s.Screens, Budget = s.Budget };
        }
        static HoloItemData ItemMetadata(HoloItemData i)
        {
            return new HoloItemData { CallerId = i.CallerId, Id = i.Id, Style = i.Style, Transform = i.Transform,
                Visible = i.Visible, Shaded = i.Shaded, Layer = i.Layer, Opacity = i.Opacity, Emission = i.Emission,Effects=i.Effects };
        }
        static bool SameGeometry(HoloItemData a, HoloItemData b)
        {
            return SameArray(a.Points, b.Points) && SameArray(a.Edges, b.Edges) && SameArray(a.Triangles, b.Triangles)
                && SameArray(a.TriangleColors, b.TriangleColors) && SameArray(a.EdgeColors, b.EdgeColors)
                && SameArray(a.UV, b.UV) && a.Material == b.Material && SameBytes(a.Declaration, b.Declaration);
        }
        static bool SameArray<T>(T[] a, T[] b)
        {
            if (ReferenceEquals(a, b)) return true; if (a == null || b == null || a.Length != b.Length) return false;
            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < a.Length; i++) if (!comparer.Equals(a[i], b[i])) return false;
            return true;
        }
        static bool SameBytes<T>(T a, T b) where T : class
        {
            if (ReferenceEquals(a, b)) return true; if (a == null || b == null) return false;
            return SameArray(MyAPIGateway.Utilities.SerializeToBinary(a), MyAPIGateway.Utilities.SerializeToBinary(b));
        }
    }
}
