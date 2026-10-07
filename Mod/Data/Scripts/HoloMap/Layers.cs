using System;
using VRage;
using PbBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        sealed class Layer
        {
            public long CallerId;
            public string Name;
            public bool Visible = true;
            public float Opacity = 1;
            public int Order;
        }
        Layer GetLayer(Scene scene, long callerId, string name)
        {
            name = LayerRules.Name(name);ValidateWriteLayer(scene,callerId,name);string key = Key(callerId, name); Layer layer;
            if (scene.Layers.TryGetValue(key, out layer)) return layer;
            if (scene.Layers.Count >= 32) throw new ArgumentException("Console layer limit reached.");
            layer = new Layer { CallerId = callerId, Name = name }; scene.Layers.Add(key, layer); return layer;
        }
        static float LayerAlpha(Scene scene, long callerId, string name)
        {
            if (string.IsNullOrEmpty(name)) return 1;
            Layer layer;
            return scene.Layers.TryGetValue(Key(callerId, name), out layer) ? LayerRules.Alpha(layer.Visible, layer.Opacity) : 1;
        }
        static int LayerOrder(Scene scene,long callerId,string name)
        {
            Layer layer;
            return !string.IsNullOrEmpty(name)&&scene.Layers.TryGetValue(Key(callerId,name),out layer)?layer.Order:0;
        }
        MyTuple<bool,string> SetLayerOrder(PbBlock caller,PbBlock target,string name,int order)
        {
            return Guard(()=>{var display=Authorize(caller,target);if(order < -1024 || order > 1024)throw new ArgumentException("Layer order must be -1024 through 1024.");GetLayer(GetScene(display.EntityId),caller.EntityId,name).Order=order;});
        }
        MyTuple<bool, string> SetObjectLayer(PbBlock caller, PbBlock console, string id, string name)
        {
            return Guard(() =>
            {
                var target = Authorize(caller, console); ValidateWriteId(caller,console,id); name = LayerRules.Name(name);
                Scene scene; Item item; Label label;
                if (!_scenes.TryGetValue(target.EntityId, out scene)) throw new ArgumentException("Object does not exist.");
                string key = Key(caller.EntityId, id);
                bool hasItem = scene.Items.TryGetValue(key, out item), hasLabel = scene.Labels.TryGetValue(key, out label);
                if (!hasItem && !hasLabel) throw new ArgumentException("Object does not exist for this PB.");
                GetLayer(scene, caller.EntityId, name);
                if (hasItem) item.Layer = name;
                if (hasLabel) label.Layer = name;
            });
        }
        MyTuple<bool, string> SetConstructLayer(PbBlock caller, PbBlock console, string name)
        {
            return Guard(() =>
            {
                var target = Authorize(caller, console); name = LayerRules.Name(name); Scene scene;
                if (!_scenes.TryGetValue(target.EntityId, out scene) || scene.TrackingCallerId != caller.EntityId)
                    throw new ArgumentException("This PB does not control the live construct layer.");
                GetLayer(scene, caller.EntityId, name); scene.ConstructLayer = name;
            });
        }
        MyTuple<bool, string> SetLayerVisible(PbBlock caller, PbBlock console, string name, bool visible)
        { return Guard(() => { var target = Authorize(caller, console); GetLayer(GetScene(target.EntityId), caller.EntityId, name).Visible = visible; }); }
        MyTuple<bool, string> SetLayerOpacity(PbBlock caller, PbBlock console, string name, float opacity)
        { return Guard(() => { var target = Authorize(caller, console); LayerRules.Opacity(opacity); GetLayer(GetScene(target.EntityId), caller.EntityId, name).Opacity = opacity; }); }
        MyTuple<bool, string, bool, float> GetLayerState(PbBlock caller, PbBlock console, string name)
        {
            try
            {
                var target = Authorize(caller, console); name = LayerRules.Name(name); Scene scene; Layer layer;
                if (_scenes.TryGetValue(target.EntityId, out scene) && scene.Layers.TryGetValue(Key(caller.EntityId, name), out layer))
                    return new MyTuple<bool, string, bool, float>(true, "", layer.Visible, layer.Opacity);
                return new MyTuple<bool, string, bool, float>(true, "", true, 1);
            }
            catch (ArgumentException ex) { return new MyTuple<bool, string, bool, float>(false, ex.Message, false, 0); }
        }
    }
    public static class LayerRules
    {
        public static string Name(string name)
        {
            if (name == null) throw new ArgumentException("Layer name is required.");
            name = name.Trim().ToLowerInvariant();
            if (name.Length == 0 || name.Length > 32) throw new ArgumentException("Layer name requires 1–32 characters.");
            foreach (char c in name)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '_')
                    throw new ArgumentException("Layer names use letters, digits, hyphens, and underscores.");
            return name;
        }
        public static void Opacity(float value)
        { if (!Geometry.Finite(value) || value < 0 || value > 1) throw new ArgumentException("Layer opacity must be in [0,1]."); }
        public static float Alpha(bool visible, float opacity) { return visible ? opacity : 0; }
        public static float PreviewTransparency(float opacity) { return 1 - opacity * 0.65f; }
    }
}
