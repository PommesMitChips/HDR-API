using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.ModAPI.Ingame;
using VRageMath;

internal static class SphericalCameraDemoTests
{
    public static int Run(CSharpCompilation compilation)
    {
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        if (!result.Success) throw new Exception(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        image.Position = 0;
        var programType = AssemblyLoadContext.Default.LoadFromStream(image).GetType("Program", true);
        var program = RuntimeHelpers.GetUninitializedObject(programType);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => programType.GetField(name, flags)!.SetValue(program, value);
        object Call(string name, params object[] args) => programType.GetMethod(name, flags)!.Invoke(program, args);
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception("Spherical camera demo: " + reason); checks++; }
        Set("_hemisphere", true);

        var world = MatrixD.CreateWorld(new Vector3D(10, 20, 30), -Vector3D.UnitY, Vector3D.UnitZ);
        IMyCameraBlock Camera(long id, string name, string subtype) {
            object Rename(object[] args) { name = (string)args[0]; return null; }
            return (IMyCameraBlock)DrawTestProxy.Make(typeof(IMyCameraBlock), (m, a) => m.Name switch {
                "get_EntityId" => id, "get_CustomName" => name, "set_CustomName" => Rename(a), "get_CubeGrid" => null, "IsSameConstructAs" => true,
                "get_WorldMatrix" => world,
                "get_BlockDefinition" => new VRage.ObjectBuilders.SerializableDefinitionId(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_CameraBlock), subtype),
                _ => throw new Exception(m.Name)
            });
        }
        var cube = Camera(10, "Dome", "Camera360Large");
        var normal = Camera(20, "Normal", "LargeCameraBlock");
        var extra = Camera(30, "Other Dome", "Camera360Small");
        var cameras = new List<IMyCameraBlock> { cube, normal };
        int writes = 0;
        IMyTextPanel Panel(string name, string text, bool square = true) {
            object Write(object[] args) { text = (string)args[0]; writes++; return null; }
            return (IMyTextPanel)DrawTestProxy.Make(typeof(IMyTextPanel), (m, a) => m.Name switch {
                "get_CustomName" => name, "get_CustomData" => text, "set_CustomData" => Write(a),
                "get_CubeGrid" => null, "IsSameConstructAs" => true,
                "get_TextureSize" => square ? new Vector2(512, 512) : new Vector2(1024, 512),
                _ => throw new Exception(m.Name)
            });
        }
        List<IMyTextPanel> Panels(int count, string binding) => Enumerable.Range(1, count).Select(i => Panel("HDR Video " + i, binding)).Reverse().ToList();
        IMyCameraBlock Select(List<IMyTextPanel> panels, List<IMyCameraBlock> sources = null) => (IMyCameraBlock)Call("SelectPanorama", panels, sources ?? cameras);

        var six = Panels(6, "0:Dome\nCamera360.Face=front\n1:Normal\nKeep.This=setting");
        Check(ReferenceEquals(Select(six), cube), "six identical forward bindings automatically select cube capture despite unused ordinary cameras");
        Call("ConfigurePanorama", six, cube, cameras);
        var sixFaces = six.Select(lcd => (int)Call("SourceFace", lcd, cube)).ToArray();
        Check(sixFaces.SequenceEqual(new[] { 0, 1, 2, 3, 4, 5 }), "the reported six-identical-images setup is repaired into six distinct selectors in LCD name order");
        Check(six.All(lcd => lcd.CustomData.Contains("0:Dome\n") && lcd.CustomData.Contains("1:Normal\nKeep.This=setting")), "only the owned surface-0 binding changes; other surfaces and settings survive");
        Check(six.All(lcd => (bool)Call("SourceHemisphere", lcd, cube)), "automatic setup keeps physical hemisphere coverage");
        int previousWrites = writes;
        Call("ConfigurePanorama", six, cube, cameras);
        Check(writes == previousWrites, "repeated setup avoids redundant CustomData changes and capture resets");

        var display = (IMyTerminalBlock)DrawTestProxy.Make(typeof(IMyTerminalBlock), (m, a) => m.Name == "get_WorldMatrix" ? MatrixD.Identity : throw new Exception(m.Name));
        Set("_display", display);
        var directions = sixFaces.Select(face => ((MatrixD)Call("Pose", cube, face)).Backward).ToArray();
        Check(directions.Distinct().Count() == 6 && Vector3D.Distance(directions[0], world.Forward) < 1e-9, "separate face selectors produce six different poses with the front following the downward camera");

        var five = Panels(5, "");
        Check(ReferenceEquals(Select(five), cube), "fresh unbound buffers discover the single 360 Camera automatically");
        Call("ConfigurePanorama", five, cube, cameras);
        Check(five.Select(lcd => (int)Call("SourceFace", lcd, cube)).SequenceEqual(new[] { 0, 1, 3, 4, 5 }), "five-buffer hemispheres assign only the five contributing views");
        Check(ReferenceEquals(Select(five, new List<IMyCameraBlock> { cube, extra }), cube), "bound buffers select their camera even when another 360 Camera is present");

        var mixed = Panels(6, "0:Dome");
        mixed[0].CustomData = "0:Normal";
        var mixedBefore = mixed.Select(lcd => lcd.CustomData).ToArray();
        Check(Select(mixed) == null && mixed.Select(lcd => lcd.CustomData).SequenceEqual(mixedBefore), "automatic detection preserves a mixed camera array without retargeting it");
        var ordinary = Panels(6, "0:Normal");
        Check(Select(ordinary) == null, "ordinary camera arrays are never rewritten merely because a spare 360 Camera exists");
        var unbound = Panels(5, "");
        Check(Select(unbound, new List<IMyCameraBlock> { cube, extra }) == null, "an unbound setup with two panorama cameras stays ambiguous instead of choosing arbitrarily");
        Check(Select(new List<IMyTextPanel> { Panel("HDR Video", "0:Dome") }) == null, "a single manually selected cube view remains a single-view setup");

        Set("_cameraMode", 2);
        Check(Select(six) == null, "the explicit array command overrides automatic cube detection");
        Set("_cameraMode", 1);
        Check(ReferenceEquals(Select(ordinary), cube), "the explicit 360 command can intentionally replace existing array bindings");
        Set("_cameraMode", 0);
        Set("_hemisphere", false);
        Check(Select(five) == null && ReferenceEquals(Select(six), cube), "full-sphere capture requires six buffers");
        Call("ConfigurePanorama", six, cube, cameras);
        Check(six.All(lcd => !(bool)Call("SourceHemisphere", lcd, cube)), "switching to sphere updates every buffer's capture coverage");

        var invalid = Panels(6, "0:Dome");
        invalid[0] = Panel("HDR Video 6", "0:Dome", false);
        previousWrites = writes;
        try { Call("ConfigurePanorama", invalid, cube, cameras); throw new Exception("Non-square capture accepted."); }
        catch (TargetInvocationException error) when (error.InnerException!.Message.Contains("square")) { checks++; }
        Check(writes == previousWrites, "invalid texture geometry fails before changing any LCD binding");

        var multiple = Enumerable.Range(0, 6).Select(i => Camera(100 + i, "360 Camera", "Camera360Large")).ToList();
        multiple.Add(normal);
        var perCamera = Panels(6, "0:Dome\nKeep.This=setting");
        Call("ConfigureCameraArray", perCamera, multiple);
        var sources = perCamera.Select(lcd => (IMyCameraBlock)Call("SourceCamera", lcd, multiple)).ToArray();
        Check(sources.All(camera => camera != null) && sources.Select(camera => camera.EntityId).Distinct().Count() == 6, "six new cameras with duplicate default names get distinct LCD bindings instead of repeating the first camera");
        Check(sources.Select(camera => camera.CustomName).Distinct().Count() == 6 && normal.CustomName == "Normal", "only the selected cameras' conflicting names are changed");
        Check(perCamera.All(lcd => (int)Call("SourceFace", lcd, sources[Array.IndexOf(perCamera.ToArray(), lcd)]) == 0), "efficient multi-camera stitching uses one forward capture per camera rather than five per camera");
        Check(perCamera.All(lcd => lcd.CustomData.Contains("Keep.This=setting")), "per-camera configuration preserves unrelated custom settings");
        previousWrites = writes;
        Call("ConfigureCameraArray", perCamera, multiple);
        Check(writes == previousWrites, "repeated multi-camera setup is stable without rebinding or renaming");
        return checks;
    }
}
