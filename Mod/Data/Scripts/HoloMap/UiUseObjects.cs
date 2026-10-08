using System;
using System.Collections.Generic;
using Sandbox.Game.Components;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        sealed class NativeUiBinding
        {
            public long Target;
            public MyUseObjectsComponent Component;
            public uint Detector;
            public NativeUiUseObject Proxy;
            public NativeHoloUiUseObject HoloProxy;
            public string WidgetKey;
        }
        readonly List<NativeUiBinding> _nativeUiBindings = new List<NativeUiBinding>();
        bool _nativeUiFaulted;

        // Keep native detector geometry and dispatch: observing a key cannot consume vanilla use.
        // Only this client's existing LCD use objects are wrapped; no physics or network entities are spawned.
        void TickUiUseObjects()
        {
            if (_nativeUiFaulted) return;
            try { TickUiUseObjectsCore(); }
            catch (Exception error)
            {
                // A broken native model/physics component cannot stop the renderer or retry endlessly.
                _nativeUiFaulted = true;
                try { ClearUiUseObjects(); } catch { }
                VRage.Utils.MyLog.Default.WriteLineAndConsole("HDR native UI disabled for this session: " + error.Message);
            }
        }
        void TickUiUseObjectsCore()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated || !ClientRenderingEnabled)
            { ClearUiUseObjects(); return; }
            if (_ticks % 6 != 0) return;
            var targets = new HashSet<long>();
            foreach (var display in _uiDisplays.Values)
            {
                if (display.Widgets.Count == 0) continue;
                targets.Add(display.TargetId);
                foreach (var scene in _scenes.Values)
                    if (scene.LcdSourceId == display.TargetId) targets.Add(scene.ConsoleId);
            }
            var rebuild = new HashSet<MyUseObjectsComponent>();
            var wanted = new Dictionary<string, HoloUiDetector>();
            if (UiClientCanInteract() && _uiHoveredDisplay != null && _uiHoveredWidget != null)
            {
                var display = _uiHoveredDisplay;
                var block = MyAPIGateway.Entities.GetEntityById(display.TargetId) as IMyProjector;
                var caller = MyAPIGateway.Entities.GetEntityById(display.CallerId) as IMyProgrammableBlock;
                long identity = MyAPIGateway.Session.Player.IdentityId;
                if (block != null && !block.Closed && caller != null && !caller.Closed
                    && block.HasPlayerAccess(identity) && caller.HasPlayerAccess(identity))
                {
                    var component = block.Components.Get<MyUseObjectsComponentBase>() as MyUseObjectsComponent;
                    var head = MyAPIGateway.Session.Player.Character.GetHeadMatrix(true, true, true, true);
                    Vector3D hit;
                    if (component != null && TryUiCurrentWidgetHit(display, _uiHoveredWidget, head.Translation, head.Forward, out hit)
                        && UiUnoccluded(MyAPIGateway.Session.Player.Character.EntityId, block, head.Translation, hit))
                    {
                        // Only a validated visible hit gets native input geometry. Blank and clipped
                        // parts of a large widget cannot steal the vanilla use action.
                        var localHit = Vector3D.Transform(hit, MatrixD.Invert(block.WorldMatrix));
                        localHit = new Vector3D(Math.Round(localHit.X / .02) * .02,
                            Math.Round(localHit.Y / .02) * .02, Math.Round(localHit.Z / .02) * .02);
                        var matrix = Matrix.CreateScale(.08f); matrix.Translation = (Vector3)localHit;
                        wanted[UiKey(display.CallerId, display.TargetId)] = new HoloUiDetector
                            { Target = block, Component = component, Matrix = matrix };
                    }
                }
            }
            for (int i = _nativeUiBindings.Count - 1; i >= 0; i--)
            {
                var binding = _nativeUiBindings[i];
                MyUseObjectsComponent.DetectorData data;
                HoloUiDetector detector;
                bool changed = binding.HoloProxy != null && (!wanted.TryGetValue(binding.WidgetKey, out detector)
                    || detector.Component != binding.Component || detector.Matrix != binding.HoloProxy.InputMatrix);
                if (changed || !targets.Contains(binding.Target)
                    || !binding.Component.DetectorInteractiveObjects.TryGetValue(binding.Detector, out data)
                    || !ReferenceEquals(data.UseObject, (IMyUseObject)binding.Proxy ?? binding.HoloProxy))
                {
                    if (RestoreUiBinding(binding))
                    { if (binding.HoloProxy != null) rebuild.Add(binding.Component); _nativeUiBindings.RemoveAt(i); }
                }
                else if (binding.HoloProxy != null) { binding.HoloProxy.Disabled = false; wanted.Remove(binding.WidgetKey); }
            }
            foreach (long id in targets)
            {
                var panel = MyAPIGateway.Entities.GetEntityById(id) as IMyTextPanel;
                if (panel == null || panel.Closed || panel.Components == null) continue;
                var component = panel.Components.Get<MyUseObjectsComponentBase>() as MyUseObjectsComponent;
                if (component == null || component.DetectorInteractiveObjects == null) continue;
                // Snapshot keys because replacing a struct entry changes the dictionary version.
                List<uint> keys;
                lock (component.DetectorInteractiveObjects) keys = new List<uint>(component.DetectorInteractiveObjects.Keys);
                int installed = 0;
                foreach (uint key in keys)
                {
                    if (installed++ >= 16 || _nativeUiBindings.Count >= 128) break;
                    lock (component.DetectorInteractiveObjects)
                    {
                        MyUseObjectsComponent.DetectorData data;
                        if (!component.DetectorInteractiveObjects.TryGetValue(key, out data) || data.UseObject == null || data.UseObject is NativeUiUseObject) continue;
                        // The screen detector is distinct from construction/inventory/terminal detectors.
                        if (data.DetectorName == null || data.DetectorName.IndexOf("textpanel", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var proxy = new NativeUiUseObject(this, id, data.UseObject);
                        component.DetectorInteractiveObjects[key] = new MyUseObjectsComponent.DetectorData(proxy, data.Matrix, data.DetectorName);
                        _nativeUiBindings.Add(new NativeUiBinding { Target = id, Component = component, Detector = key, Proxy = proxy });
                    }
                }
            }
            foreach (var entry in wanted)
            {
                if (_nativeUiBindings.Count >= 128) break;
                var detector = entry.Value;
                // AddDetector marks its ids for save serialization, but RemoveDetector does not
                // remove those saved ids. Local input entries must bypass that persistence path.
                string name = "hdr_ui_" + _nativeUiSerial++;
                uint id;
                NativeHoloUiUseObject proxy;
                lock (detector.Component.DetectorInteractiveObjects)
                {
                    id = (uint)detector.Component.DetectorInteractiveObjects.Count;
                    if (id >= 64) continue;
                    if (!UiDetectorOrderValid(detector.Component)) continue;
                    proxy = new NativeHoloUiUseObject(this, detector.Target, detector.Matrix, detector.Matrix);
                    detector.Component.DetectorInteractiveObjects.Add(id, new MyUseObjectsComponent.DetectorData(proxy, detector.Matrix, name));
                    if (!UiDetectorOrderValid(detector.Component))
                    { detector.Component.DetectorInteractiveObjects.Remove(id); continue; }
                }
                _nativeUiBindings.Add(new NativeUiBinding { Target = detector.Target.EntityId, Component = detector.Component,
                    Detector = id, HoloProxy = proxy, WidgetKey = entry.Key });
                rebuild.Add(detector.Component);
            }
            foreach (var component in rebuild) RebuildUiDetectorPhysics(component);
        }
        long _nativeUiSerial;
        sealed class HoloUiDetector { public IMyProjector Target; public MyUseObjectsComponent Component; public Matrix Matrix; }
        static bool RestoreUiBinding(NativeUiBinding binding)
        {
            if (binding.Component.DetectorInteractiveObjects == null) return true;
            lock (binding.Component.DetectorInteractiveObjects)
            {
            MyUseObjectsComponent.DetectorData data;
            if (binding.Component.DetectorInteractiveObjects.TryGetValue(binding.Detector, out data)
                && ReferenceEquals(data.UseObject, (IMyUseObject)binding.Proxy ?? binding.HoloProxy))
            {
                if (binding.HoloProxy != null)
                {
                    // Another mod may append a native saved detector after our transient entry.
                    // Keep an inert placeholder until it is safe to remove the tail without changing
                    // foreign shape keys or breaking that mod's private saved-detector ids.
                    binding.HoloProxy.Disabled = true;
                    if (!UiDetectorOrderValid(binding.Component)
                        || binding.Detector + 1 != binding.Component.DetectorInteractiveObjects.Count) return false;
                    binding.Component.DetectorInteractiveObjects.Remove(binding.Detector);
                }
                else binding.Component.DetectorInteractiveObjects[binding.Detector] = new MyUseObjectsComponent.DetectorData(binding.Proxy.Original, data.Matrix, data.DetectorName);
            }
            }
            return true;
        }
        void ClearUiUseObjects()
        {
            var rebuild = new HashSet<MyUseObjectsComponent>();
            for (int i = _nativeUiBindings.Count - 1; i >= 0; i--)
            {
                var binding=_nativeUiBindings[i];if(binding.HoloProxy!=null)binding.HoloProxy.Disabled=true;
                try{if(RestoreUiBinding(binding)){if(binding.HoloProxy!=null)rebuild.Add(binding.Component);_nativeUiBindings.RemoveAt(i);}}
                catch(Exception error){_nativeUiBindings.RemoveAt(i);NoteUiCleanupFault(error);}
            }
            foreach (var component in rebuild)try{RebuildUiDetectorPhysics(component);}catch(Exception error){NoteUiCleanupFault(error);}
        }
        bool _nativeUiCleanupWarned;
        void NoteUiCleanupFault(Exception error)
        {
            _nativeUiFaulted=true;if(_nativeUiCleanupWarned)return;_nativeUiCleanupWarned=true;
            if(VRage.Utils.MyLog.Default!=null)VRage.Utils.MyLog.Default.WriteLineAndConsole("HDR UI native cleanup: "+error.Message);
        }
        static void RebuildUiDetectorPhysics(MyUseObjectsComponent component)
        {
            if (component.Entity == null || component.Entity.Closed || !UiDetectorOrderValid(component)) return;
            bool enabled = component.DetectorPhysics != null ? component.DetectorPhysics.Enabled : component.Entity.InScene;
            component.RecreatePhysics();
            if (component.DetectorPhysics != null) component.DetectorPhysics.Enabled = enabled;
        }
        static bool UiDetectorOrderValid(MyUseObjectsComponent component)
        {
            lock (component.DetectorInteractiveObjects)
            {
                uint expected = 0;
                foreach (var entry in component.DetectorInteractiveObjects) if (entry.Key != expected++) return false;
                return true;
            }
        }
        sealed class NativeHoloUiUseObject : IMyUseObject
        {
            readonly HoloMapSession _session; readonly IMyProjector _target; readonly Matrix _local;
            public readonly Matrix InputMatrix;
            public bool Disabled;
            int _instance = -1; uint _render = uint.MaxValue;
            public NativeHoloUiUseObject(HoloMapSession session, IMyProjector target, Matrix local, Matrix input)
            { _session = session; _target = target; _local = local; InputMatrix = input; }
            public VRage.ModAPI.IMyEntity Owner { get { return _target; } }
            public IMyModelDummy Dummy { get { return null; } }
            public float InteractiveDistance { get { return (float)UiInteractionRange; } }
            public MatrixD ActivationMatrix { get { return (MatrixD)_local * _target.WorldMatrix; } }
            public MatrixD WorldMatrix { get { return _target.WorldMatrix; } }
            public uint RenderObjectID { get { return _render; } }
            public int InstanceID { get { return _instance; } }
            public bool ShowOverlay { get { return false; } }
            public UseActionEnum SupportedActions { get { return Disabled ? UseActionEnum.None : UseActionEnum.Manipulate; } }
            public UseActionEnum PrimaryAction { get { return UseActionEnum.Manipulate; } }
            public UseActionEnum SecondaryAction { get { return UseActionEnum.None; } }
            public bool ContinuousUsage { get { return false; } }
            public bool PlayIndicatorSound { get { return false; } }
            public bool ShouldUpdateTooltips { get { return false; } }
            public void Use(UseActionEnum action, VRage.ModAPI.IMyEntity user)
            {
                if (!Disabled && action == UseActionEnum.Manipulate && MyAPIGateway.Session != null && MyAPIGateway.Session.Player != null
                    && ReferenceEquals(user, MyAPIGateway.Session.Player.Character)) _session.TryConsumeUiUse(_target.EntityId);
            }
            public MyActionDescription GetActionInfo(UseActionEnum action)
            { return new MyActionDescription { Text = MyStringId.GetOrCompute("Use"), IsTextControlHint = true, FormatParams = new object[] { "HDR UI" } }; }
            public bool HandleInput() { return false; }
            public void OnSelectionLost() { }
            public void SetRenderID(uint id) { _render = id; }
            public void SetInstanceID(int id) { _instance = id; }
        }
        sealed class NativeUiUseObject : IMyUseObject
        {
            readonly HoloMapSession _session;
            readonly long _target;
            public readonly IMyUseObject Original;
            public NativeUiUseObject(HoloMapSession session, long target, IMyUseObject original)
            { _session = session; _target = target; Original = original; }
            public VRage.ModAPI.IMyEntity Owner { get { return Original.Owner; } }
            public IMyModelDummy Dummy { get { return Original.Dummy; } }
            public float InteractiveDistance { get { return Original.InteractiveDistance; } }
            public MatrixD ActivationMatrix { get { return Original.ActivationMatrix; } }
            public MatrixD WorldMatrix { get { return Original.WorldMatrix; } }
            public uint RenderObjectID { get { return Original.RenderObjectID; } }
            public int InstanceID { get { return Original.InstanceID; } }
            public bool ShowOverlay { get { return Original.ShowOverlay; } }
            public UseActionEnum SupportedActions { get { return Original.SupportedActions; } }
            public UseActionEnum PrimaryAction { get { return Original.PrimaryAction; } }
            public UseActionEnum SecondaryAction { get { return Original.SecondaryAction; } }
            public bool ContinuousUsage { get { return Original.ContinuousUsage; } }
            public bool PlayIndicatorSound { get { return Original.PlayIndicatorSound; } }
            public bool ShouldUpdateTooltips { get { return Original.ShouldUpdateTooltips; } }
            public void Use(UseActionEnum action, VRage.ModAPI.IMyEntity user)
            {
                if (action == Original.PrimaryAction && !Original.ContinuousUsage
                    && MyAPIGateway.Session != null && MyAPIGateway.Session.Player != null
                    && ReferenceEquals(user, MyAPIGateway.Session.Player.Character)
                    && _session.TryConsumeUiUse(_target)) return;
                Original.Use(action, user);
            }
            public MyActionDescription GetActionInfo(UseActionEnum action) { return Original.GetActionInfo(action); }
            public bool HandleInput() { return Original.HandleInput(); }
            public void OnSelectionLost() { Original.OnSelectionLost(); }
            public void SetRenderID(uint id) { Original.SetRenderID(id); }
            public void SetInstanceID(int id) { Original.SetInstanceID(id); }
        }
    }
}
