using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using HoloMap;
using VRageMath;

internal static class Program
{
    static int _checks;
    static readonly string GameBin = Environment.GetEnvironmentVariable("SE_BIN") ?? @"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
    const string Header = "using System;using System.Collections.Generic;using System.Linq;using System.Text;using Sandbox.ModAPI.Ingame;using Sandbox.ModAPI.Interfaces;using SpaceEngineers.Game.ModAPI.Ingame;using VRage.Game;using VRage.Game.ModAPI.Ingame;using VRage.Game.ModAPI.Ingame.Utilities;using VRageMath;public class Program:MyGridProgram{";
    static void Main(string[] args)
    {
        try
        {
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            { string path = Path.Combine(GameBin, name.Name + ".dll"); return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null; };
            if(args.Length>0 && args[0]=="--billboard-il")
            {
                foreach(var method in typeof(VRage.Game.MyTransparentGeometry).GetMethods(BindingFlags.Static|BindingFlags.Public))
                    if(method.Name=="AddAttachedQuad" || method.Name=="AddTriangleBillboard"
                        || (method.Name=="AddLineBillboard" && method.GetParameters().Any(p=>p.ParameterType==typeof(uint))))DumpIl(method);
                foreach(var method in typeof(VRage.Game.Components.MyRenderComponentBase).GetMethods(BindingFlags.Instance|BindingFlags.Public))
                    if(method.Name=="SetParent" || method.Name.Contains("RenderObjectID"))Console.WriteLine(method);
                return;
            }
            if (args.Length > 0 && args[0] == "--render-il")
            {
                var sandbox = Assembly.LoadFrom(Path.Combine(GameBin,"Sandbox.Game.dll"));
                foreach (string typeName in new[]{"Sandbox.Game.Components.MyRenderComponentCubeBlock","Sandbox.Game.Components.MyRenderComponentCubeGrid"})
                {
                    var type=sandbox.GetType(typeName,true);
                    foreach(var method in type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                        if(method.Name.Contains("RenderObjects") || method.Name.Contains("Visibility") || method.Name=="UpdateGridParent") DumpIl(method);
                    if(typeName.EndsWith("CubeBlock"))
                    {
                        for(var parent=type.BaseType;parent!=null;parent=parent.BaseType)
                            foreach(var method in parent.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                                if(method.Name=="AddRenderObjects" || method.Name=="UpdateRenderObjectVisibility" || method.Name=="set_Visible" || method.Name=="OnAddedToScene")DumpIl(method);
                    }
                }
                foreach(var method in typeof(VRage.Game.Entity.MyEntity).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                    if(method.Name=="OnAddedToScene")DumpIl(method);
                return;
            }
            if (args.Length > 0 && args[0] == "--inspect-preview")
            {
                foreach (var name in new[] { "MyObjectBuilder_MechanicalConnectionBlock", "MyObjectBuilder_AttachableTopBlock", "MyObjectBuilder_ShipConnector", "MyObjectBuilder_MotorBase", "MyObjectBuilder_PistonBase" })
                {
                    Type type = null;
                    foreach (var dll in new[] {"Sandbox.Common.dll","Sandbox.Game.dll","VRage.Game.dll","SpaceEngineers.ObjectBuilders.dll"})
                        type = type ?? Assembly.LoadFrom(Path.Combine(GameBin,dll)).GetType("Sandbox.Common.ObjectBuilders."+name);
                    Console.WriteLine(name);
                    if (type != null) foreach (var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)) Console.WriteLine(field);
                }
                foreach(var property in typeof(VRage.Game.Components.MyPositionComponentBase).GetProperties()) Console.WriteLine(property);
                return;
            }
            if (args.Length > 0 && args[0] == "--inspect-sandbox")
            {
                var scripting = Assembly.LoadFrom(Path.Combine(GameBin, "VRage.Scripting.dll"));
                foreach (var name in new[] { "VRage.Scripting.MyScriptCompiler", "VRage.Scripting.MyScriptWhitelist", "VRage.Scripting.Analyzers.WhitelistDiagnosticAnalyzer" })
                {
                    var type = scripting.GetType(name, true);
                    Console.WriteLine(name);
                    foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        if (member is MethodBase || member is PropertyInfo || member is FieldInfo) Console.WriteLine(member);
                }
                var sandbox = Assembly.LoadFrom(Path.Combine(GameBin, "Sandbox.Game.dll"));
                foreach (var name in new[] { "Sandbox.MySandboxGame", "Sandbox.Game.World.MyScriptManager" })
                {
                    var type = sandbox.GetType(name);
                    if (type == null) continue;
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                        if (method.Name.Contains("Compiler") || method.Name.Contains("Whitelist") || method.Name.Contains("Scripting")) Console.WriteLine(name + ": " + method);
                }
                return;
            }
            string root = Path.GetFullPath(args.Length == 0 ? "../.." : args[0]);
            GeometryTests();
            NetworkTests();
            PreviewTests();
            LabelTests();
            LayerTests();
            SvgTests();
            AppearanceTests();
            AppearancePacketTests();
            _checks+=DrawCommandTests.Run();
            _checks+=ClientReplicationTests.Run();
            _checks+=NewFeatureIntegrationTests.Run();
            _checks+=UiNetworkTests.Run();
            _checks+=UiApiTests.Run();
            _checks+=UiIntegrationTests.Run();
            _checks+=RichHudInputTests.Run();
            _checks+=LcdBackendTests.Run();
            _checks+=HostMeshRegressionTests.Run();
            _checks+=LcdFaultIsolationTests.Run();
            _checks+=PerspectiveViewTests.Run();
            _checks+=ProjectedSpriteTests.Run();
            _checks+=ProjectedScreenNetworkTests.Run();
            _checks+=ProjectedScreenIntegrationTests.Run();
            _checks+=ExternalSourceRenderBudgetTests.Run();
            _checks+=DisplaySourceBridgeTests.Run();
            _checks+=PortalSourceTests.Run();
            _checks+=DisplaySourceOcclusionTests.Run();
            _checks+=SurfaceMappingTests.Run();
            _checks+=GeneralSurfaceTests.Run();
            _checks+=CameraPanoramaTests.Run();
            _checks+=CameraSourceSettingsTests.Run();
            _checks+=CameraSourceDemandIntegrationTests.Run();
            _checks+=CameraDensityMapTests.Run();
            _checks+=CallerLifetimeTests.Run();
            _checks+=RasterCanvasTests.Run(root);
            _checks+=RasterSurfaceIntegrationTests.Run();
            _checks+=DisplayLodTests.Run();
            _checks+=RenderBudgetSettingsTests.Run();
            _checks+=LcdTriangleTests.Run(root);
            _checks+=DisplayCapabilityTests.Run();
            _checks+=ModClientApiTests.Run();
            _checks+=ClientBudgetPartitionTests.Run();
            _checks+=PluginCapabilityTests.Run();
            _checks+=HologramEffectTests.Run();
            _checks+=HologramEffectsIntegrationTests.Run();
            _checks+=HologramEffectsRasterTests.Run();
            _checks+=ModClientEffectTests.Run();
            _checks+=ModClientInteractionTests.Run();
            _checks+=SdkBindingTests.Run();
            _checks+=UiValueStageTests.Run();
            _checks+=NativeUiDragNetworkTests.Run();
            _checks+=NativeUiDragInputTests.Run();
            _checks+=UiDragLifecycleIntegrationTests.Run();
            _checks+=HtmlPbCoreSeamTests.Run();
            _checks+=PersistentUiDragTests.Run();
            _checks+=UiDragHitTests.Run();
            _checks+=UiDragTransportTests.Run();
            Compile(root);
            Console.WriteLine($"PASS: {_checks} geometry/curve/SVG/appearance/animation/network/asset/command assertions; mod + PB examples and portal API helper compile against installed game assemblies; PB memory-safe rewrite.");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }
    static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); _checks++; }
    static void Reject(Action action, string name)
    { try { action(); } catch (ArgumentException) { _checks++; return; } throw new Exception("Expected rejection: " + name); }
    static Vector3D P(double x, double y, double z = 0) => new Vector3D(x, y, z);
    static double Area(Geometry g)
    {
        double area = 0;
        for (int i = 0; i < g.Triangles.Length; i += 3)
            area += Vector3D.Cross(g.Points[g.Triangles[i + 1]] - g.Points[g.Triangles[i]], g.Points[g.Triangles[i + 2]] - g.Points[g.Triangles[i]]).Length() / 2;
        return area;
    }
    static void GeometryTests()
    {
        var square = new[] { P(0,0), P(2,0), P(2,2), P(0,2) };
        var g = Geometry.Polygons(square, new[] { new[] {0,1,2,3} });
        Assert(g.Triangles.Length == 6 && g.Edges.Length == 8 && Math.Abs(Area(g)-4) < 1e-9, "quad coverage");
        var reversed = Geometry.Polygons(square, new[] { new[] {3,2,1,0} });
        Assert(Math.Abs(Area(reversed)-4) < 1e-9, "reversed winding");
        Assert(Vector3D.Cross(reversed.Points[reversed.Triangles[1]]-reversed.Points[reversed.Triangles[0]], reversed.Points[reversed.Triangles[2]]-reversed.Points[reversed.Triangles[0]]).Z < 0, "winding preserved");
        var concave = Geometry.Polygons(new[] { P(0,0),P(3,0),P(3,1),P(1,1),P(1,3),P(0,3) }, new[] {new[] {0,1,2,3,4,5}});
        Assert(concave.Triangles.Length == 12 && Math.Abs(Area(concave)-5) < 1e-9, "concave L coverage");
        var shared = Geometry.Polygons(square, new[] { new[] {0,1,2}, new[] {0,2,3} });
        Assert(shared.Edges.Length == 10, "shared edges drawn once");
        var separate = Geometry.Polygons(new[] { P(0,0),P(1,0),P(0,1),P(4,0),P(5,0),P(4,1) }, new[] { new[] {0,1,2},new[] {3,4,5} });
        Assert(separate.Edges.Length == 12 && Math.Abs(Area(separate)-1) < 1e-9, "disconnected faces");
        var straight = Geometry.Polygons(new[] {P(0,0),P(1,0),P(2,0),P(2,2),P(0,2)}, new[] {new[] {0,1,2,3,4}});
        Assert(straight.Edges.Length == 10 && Math.Abs(Area(straight)-4) < 1e-9, "collinear outline vertex");
        var rotated = square.Select(p => Vector3D.Transform(p, MatrixD.CreateFromYawPitchRoll(0.5,0.7,0.3)) + P(10000,-20000,30000)).ToArray();
        Assert(Math.Abs(Area(Geometry.Polygons(rotated,new[]{new[]{0,1,2,3}}))-4) < 1e-7, "arbitrary plane and translated coordinates");
        var wirePoints = new[] {P(0,0),P(1,0),P(1,1)};
        var wire = Geometry.Wires(wirePoints,new[]{new Vector2I(0,1),new Vector2I(1,0),new Vector2I(1,2)});
        Assert(wire.Edges.Length == 4, "deduplicated wires");
        wirePoints[0] = P(999,999); Assert(wire.Points[0] == P(0,0), "caller arrays copied");
        Reject(()=>Geometry.Polygons(square,new[]{new[]{0,1,2,0}}),"repeated index");
        Reject(()=>Geometry.Polygons(square,new[]{new[]{0,2,1,3}}),"bow tie");
        Reject(()=>Geometry.Polygons(new[]{P(0,0),P(2,0),P(2,2,0.1),P(0,2)},new[]{new[]{0,1,2,3}}),"nonplanar face");
        Reject(()=>Geometry.Polygons(new[]{P(0,0),P(1,0),P(2,0)},new[]{new[]{0,1,2}}),"zero-area face");
        Reject(()=>Geometry.Polygons(square,new[]{new[]{0,1,9}}),"out-of-range face index");
        Reject(()=>Geometry.Polygons(new[]{P(0,0),P(0,0),P(1,1)},new[]{new[]{0,1,2}}),"coincident face vertices");
        Reject(()=>Geometry.Wires(new[]{P(0,0),P(0,0)},new[]{new Vector2I(0,1)}),"zero-length wire");
        Reject(()=>Geometry.Wires(square,new[]{new Vector2I(-1,1)}),"negative edge index");
        Reject(()=>Geometry.Wires(new[]{P(double.NaN,0),P(1,0)},new[]{new Vector2I(0,1)}),"NaN");
        Reject(()=>Geometry.Wires(new[]{P(double.PositiveInfinity,0),P(1,0)},new[]{new Vector2I(0,1)}),"infinity");
        Reject(()=>Geometry.Wires(new Vector3D[Geometry.MaxPoints+1],new[]{new Vector2I(0,1)}),"point budget");
        // Many concave star faces exercise ear clipping in different orientations.
        for (int count = 8; count <= 40; count += 2)
        {
            var points = Enumerable.Range(0,count).Select(i=>P(Math.Cos(i*2*Math.PI/count)*(i%2==0?2:1),Math.Sin(i*2*Math.PI/count)*(i%2==0?2:1))).ToArray();
            var star = Geometry.Polygons(points,new[]{Enumerable.Range(0,count).ToArray()});
            double expected = 0;
            for (int i=0;i<count;i++) expected += points[i].X*points[(i+1)%count].Y-points[(i+1)%count].X*points[i].Y;
            Assert(star.Triangles.Length == (count-2)*3 && Math.Abs(Area(star)-Math.Abs(expected)/2)<1e-8,"concave star "+count);
        }
    }
    static void Compile(string root)
    {
        string framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319");
        var references = new[] {"mscorlib.dll","System.dll","System.Core.dll","System.Xml.dll"}.Select(p=>Path.Combine(framework,p))
            .Concat(new[]{"netstandard.dll","System.Collections.Immutable.dll","Sandbox.Common.dll","Sandbox.Game.dll","SpaceEngineers.Game.dll","SpaceEngineers.ObjectBuilders.dll","VRage.dll","VRage.Game.dll","VRage.Library.dll","VRage.Math.dll","VRage.Render.dll","VRage.Input.dll","VRage.Scripting.dll","ProtoBuf.Net.Core.dll","ProtoBuf.Net.dll"}.Select(p=>Path.Combine(GameBin,p)))
            .Select(p=>(MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
        var options = new CSharpParseOptions(LanguageVersion.CSharp6);
        _checks+=ModClientApiTests.Compile(root,references,options);
        var portalApiTree=CSharpSyntaxTree.ParseText(Header+File.ReadAllText(Path.Combine(root,"Api/PortalApi.cs"))+"}",options,"PortalApi.cs");
        RequireCompile(CSharpCompilation.Create("HdrPortalApi",new[]{portalApiTree},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
        var modTrees = Directory.GetFiles(Path.Combine(root,"Mod/Data/Scripts/HoloMap"),"*.cs").Select(p=>CSharpSyntaxTree.ParseText("using Sandbox.ModAPI;"+Environment.NewLine+File.ReadAllText(p),options,p));
        var mod = CSharpCompilation.Create("HoloMapMod",modTrees,references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        RequireCompile(mod);
        ValidateModExceptions(mod);
        // The game supplies the mod API namespace, which can collide with Ingame imports.
        // Reproduce the actual LCD load failure so plain SDK compilation cannot miss it.
        var ambiguousSurface = CSharpSyntaxTree.ParseText("using Sandbox.ModAPI;using Sandbox.ModAPI.Ingame;class SurfaceRegression { IMyTextSurface surface; }", options);
        var ambiguousCompilation = CSharpCompilation.Create("SurfaceRegression",new[]{ambiguousSurface},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert(ambiguousCompilation.GetDiagnostics().Any(d=>d.Id=="CS0104"),"game default namespace exposes ambiguous text surface");
        var sandboxViolations = KnownSandboxViolations(mod);
        if (sandboxViolations.Count != 0) throw new Exception(string.Join(Environment.NewLine,sandboxViolations));
        var sandboxFixture = CSharpSyntaxTree.ParseText("public class SandboxRegression { public string Bad(VRage.Game.Entity.MyEntity e) { return e.Model.AssetName; } public string Good(VRage.ModAPI.IMyEntity e) { return e.Model.AssetName; } }",options);
        var fixtureCompilation = CSharpCompilation.Create("SandboxRegression",new[]{sandboxFixture},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        RequireCompile(fixtureCompilation);
        Assert(KnownSandboxViolations(fixtureCompilation).Count == 1,"sandbox regression rejects concrete MyModel.AssetName but permits IMyModel.AssetName");
        string fullApi=File.ReadAllText(Path.Combine(root,"Api/HoloMapApi.cs"));
        _checks+=TypedApiBoundaryTests.Run(root,references,options);
        var sizes=new List<object>();
        string BuildBody(string example)
        {
            string source=File.ReadAllText(Path.Combine(root,"Examples",example+".cs"));
            if(example=="SvgFramesDemo")source+=Environment.NewLine+File.ReadAllText(Path.Combine(root,"artifacts/pulse.frames.cs"));
            string profile="";
            if(example!="PackedPelican"&&example!="MultiConsoleAnimation")
            {
                profile=ApiProfiles.Generate(fullApi,source,Header,references,options);
                File.WriteAllText(Path.Combine(root,"artifacts",example+".api.cs"),profile);
                Assert(profile.Length<fullApi.Length,"generated API profile smaller than full wrapper: "+example);
            }
            string result=source+(profile.Length==0?"":Environment.NewLine+profile);
            sizes.Add(new{example,bodyCharacters=source.Length,fullApiCharacters=fullApi.Length,profileCharacters=profile.Length,
                pasteCharacters=result.Length,fullPasteCharacters=source.Length+(profile.Length==0?0:Environment.NewLine.Length+fullApi.Length)});
            return result;
        }
        string body = BuildBody("ConsoleDemo");
        Directory.CreateDirectory(Path.Combine(root,"artifacts"));
        File.WriteAllText(Path.Combine(root,"artifacts/ConsoleDemo.pb.cs"),body);
        var tree = CSharpSyntaxTree.ParseText(Header+body+"}",options,"ConsoleDemo.pb.cs");
        var pb = CSharpCompilation.Create("HoloMapPB",new[]{tree},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        RequireCompile(pb);
        var rewriter = Assembly.LoadFrom(Path.Combine(GameBin,"VRage.Scripting.dll")).GetType("VRage.Scripting.Rewriters.TypeSafetyAndBlockRewriter",true);
        var visitor = (CSharpSyntaxRewriter)Activator.CreateInstance(rewriter,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{pb.GetSemanticModel(tree),tree,true},null);
        var rewritten = CSharpSyntaxTree.Create((CSharpSyntaxNode)visitor.Visit(tree.GetRoot()),options);
        RequireCompile(pb.ReplaceSyntaxTree(tree,rewritten));
        foreach (string example in new[] { "SvgDemo", "SvgFramesDemo", "PackedPelican", "MultiConsoleAnimation", "AppearanceDemo" })
        {
            string exampleBody=BuildBody(example);
            File.WriteAllText(Path.Combine(root,"artifacts",example+".pb.cs"),exampleBody);
            var exampleTree=CSharpSyntaxTree.ParseText(Header+exampleBody+"}",options,example+".pb.cs");
            foreach(var literal in exampleTree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax>())
            {
                string svg=literal.Token.ValueText;
                if(svg.TrimStart().StartsWith("<svg",StringComparison.Ordinal))
                { var mesh=Svg.Parse(svg);Assert(mesh.Geometry.Points.Length<=Geometry.MaxPoints,"generated demo SVG parses within budget"); }
            }
            var exampleCompilation=CSharpCompilation.Create("HoloMap"+example,new[]{exampleTree},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            RequireCompile(exampleCompilation);
            var exampleVisitor=(CSharpSyntaxRewriter)Activator.CreateInstance(rewriter,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{exampleCompilation.GetSemanticModel(exampleTree),exampleTree,true},null);
            var exampleRewritten=CSharpSyntaxTree.Create((CSharpSyntaxNode)exampleVisitor.Visit(exampleTree.GetRoot()),options);
            RequireCompile(exampleCompilation.ReplaceSyntaxTree(exampleTree,exampleRewritten));
        }
        File.WriteAllText(Path.Combine(root,"artifacts/ApiSizeAudit.json"),System.Text.Json.JsonSerializer.Serialize(sizes,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        var drawSizes=new List<object>();
        foreach(string name in new[]{"PortalDemo","DrawDemo","PackedPelicanDraw","MultiConsoleAnimationDraw","LcdDemo","ProjectedScreenDemo","RasterSurfaceDemo","SphericalCameraDemo","DirectCameraSphereDemo","GeneralSurfaceDemo","HdrFeaturesDemo","UiDemo","InteractiveControlsDemo","HologramEffectsDemo","AppearanceDemo","ConsoleDemo","SvgDemo","SvgFramesDemo"})
        {
            string source=File.ReadAllText(Path.Combine(root,"Examples",name+".cs"));
            if(name=="SvgFramesDemo")source+=Environment.NewLine+File.ReadAllText(Path.Combine(root,"artifacts/pulse.frames.cs"));
            bool direct=name=="PortalDemo"||name=="DrawDemo"||name=="PackedPelicanDraw"||name=="MultiConsoleAnimationDraw"||name=="LcdDemo"||name=="ProjectedScreenDemo"||name=="RasterSurfaceDemo"||name=="SphericalCameraDemo"||name=="DirectCameraSphereDemo"||name=="GeneralSurfaceDemo"||name=="HdrFeaturesDemo"||name=="UiDemo"||name=="InteractiveControlsDemo"||name=="HologramEffectsDemo";
            string drawBody=direct?source:DrawScriptBuilder.Generate(source,Header,options);
            string artifact=direct?name:name+".Draw";
            File.WriteAllText(Path.Combine(root,"artifacts",artifact+".pb.cs"),drawBody);
            if(name=="DirectCameraSphereDemo")File.WriteAllText(Path.Combine(root,"artifacts","DirectCameraSphere_v14.pb.cs"),drawBody);
            var drawTree=CSharpSyntaxTree.ParseText(Header+drawBody+"}",options,artifact+".pb.cs");
            var drawCompilation=CSharpCompilation.Create("HoloDraw"+name,new[]{drawTree},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));RequireCompile(drawCompilation);
            if(name=="SphericalCameraDemo")_checks+=SphericalCameraDemoTests.Run(drawCompilation);
            if(name=="DirectCameraSphereDemo")_checks+=DirectCameraSphereDemoTests.Run(drawCompilation);
            if(name=="PortalDemo")_checks+=PortalDemoTests.Run(drawCompilation);
            var drawVisitor=(CSharpSyntaxRewriter)Activator.CreateInstance(rewriter,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{drawCompilation.GetSemanticModel(drawTree),drawTree,true},null);
            var safeDraw=CSharpSyntaxTree.Create((CSharpSyntaxNode)drawVisitor.Visit(drawTree.GetRoot()),options);RequireCompile(drawCompilation.ReplaceSyntaxTree(drawTree,safeDraw));
            Assert(!drawBody.Contains("class HoloMapApi")&&!drawBody.Contains("new HoloMapApi"),"drawing PB has no copied API class: "+name);
            drawSizes.Add(new{name,characters=drawBody.Length});
        }
        File.WriteAllText(Path.Combine(root,"artifacts/DrawApiSizes.json"),System.Text.Json.JsonSerializer.Serialize(drawSizes,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        // Run pure curve sampling from emitted wrapper code in the current runtime.
        var curveTree = CSharpSyntaxTree.ParseText("using System;using System.Collections.Generic;using Sandbox.ModAPI.Ingame;using Sandbox.ModAPI.Interfaces;using VRageMath;"+fullApi, options);
        var curve = CSharpCompilation.Create("HoloMapCurveTests",new[]{curveTree},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream(); var result = curve.Emit(stream);
        if (!result.Success) throw new Exception(string.Join(Environment.NewLine,result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
        var apiType = Assembly.Load(stream.ToArray()).GetType("HoloMapApi");
        HelperTests(apiType);
        AnimationTests(apiType);
        var sample = apiType.GetMethod("SampleCurve");
        var closed = (Vector3D[])sample.Invoke(null,new object[]{new Func<double,Vector3D>(t=>P(Math.Cos(t),Math.Sin(t))),0d,2*Math.PI,64,true});
        Assert(closed.Length==64 && (closed[0]-closed[63]).Length()>0,"closed curve excludes duplicate endpoint");
        var open = (Vector3D[])sample.Invoke(null,new object[]{new Func<double,Vector3D>(t=>P(t,t*t)),-1d,1d,16,false});
        Assert(open.Length==17 && open[0]==P(-1,1) && open[16]==P(1,1),"open curve includes endpoints");
        try { sample.Invoke(null,new object[]{new Func<double,Vector3D>(t=>P(double.NaN,0)),0d,1d,16,false}); throw new Exception("Nonfinite curve accepted."); }
        catch(TargetInvocationException ex) { Assert(ex.InnerException is ArgumentException,"nonfinite curve rejected"); }
        AssetTests(root);
        PackedAnimationTests(root);
        SampleAnimationTests(root);
    }
    static void SampleAnimationTests(string root)
    {
        var directory=Path.Combine(root,"artifacts/SvgSamples");
        foreach(string name in new[]{"navigation-compass","ship-schematic","radar-sweep"})
        {
            string data=File.ReadAllText(Path.Combine(directory,name+".customdata.txt"));
            var animation=PackedAnimation.Decode(data);
            byte[] bytes=Convert.FromBase64String(data.Substring("HoloMapAnimation1:".Length));
            Assert(PackedAnimation.Decompress(bytes).SequenceEqual(File.ReadAllBytes(Path.Combine(directory,name+".animation.txt"))),"sample compression round trip: "+name);
            Assert(animation.Parts==1&&animation.Frames==(name=="radar-sweep"?32:1),"sample animation/static frame count: "+name);
            Assert(data.Length<60000,"sample Custom Data size: "+name);
            var positions=new List<string>();
            foreach(string frame in animation.Svg)
            {
                var mesh=Svg.Parse(frame,3);
                Assert(mesh.Geometry.Points.Length<=2048&&mesh.Geometry.Triangles.Length/3<=4096,"sample frame geometry budget: "+name);
                Assert(mesh.Colors.Length==mesh.Geometry.Triangles.Length/3,"sample frame colors: "+name);
                Assert(mesh.Geometry.Points.All(p=>p.Length()*0.003<25),"sample default placement fits display: "+name);
                positions.Add(string.Join(",",mesh.Geometry.Points.Select(p=>p.ToString())));
            }
            if(name=="radar-sweep")
            {
                Assert(positions.Distinct().Count()==32,"radar sweep has 32 distinct geometry frames");
                Assert(animation.FrameAt(8)==0,"radar sweep loops in eight seconds");
            }
            else Assert(Svg.Parse(File.ReadAllText(Path.Combine(directory,name+".svg")),3).Geometry.Triangles.Length>0,"static raw SVG is importer-compatible: "+name);
        }
    }
    static void PackedAnimationTests(string root)
    {
        string text=File.ReadAllText(Path.Combine(root,"artifacts/pelican.customdata.txt"));
        var animation=PackedAnimation.Decode(text);
        byte[] compressed=Convert.FromBase64String(text.Substring("HoloMapAnimation1:".Length));
        Assert(PackedAnimation.Decompress(compressed).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"artifacts/pelican.animation.txt"))),"packed export decompresses byte-exactly");
        Assert(text.Length<60000&&animation.Frames==72&&animation.Parts==3,"pelican compressed data fits transport budget");
        Assert(animation.FrameAt(0)==0&&animation.FrameAt(0.25)==1&&animation.FrameAt(18)==0,"packed animation frame clock and loop");
        int max=0,maxPrimitives=0;
        for(int frame=0;frame<animation.Frames;frame++)
        {
            int total=0,primitives=0;
            for(int part=0;part<animation.Parts;part++)
            {
                try { var mesh=Svg.Parse(animation.Svg[frame*animation.Parts+part],3);total+=mesh.Geometry.Points.Length;primitives+=mesh.Geometry.Triangles.Length/3;Assert(mesh.Colors.Length==mesh.Geometry.Triangles.Length/3,"packed frame color budget"); }
                catch(Exception e){throw new Exception("Pelican frame "+frame+" part "+part+": "+e.Message,e);}
            }
            max=Math.Max(max,total);
            maxPrimitives=Math.Max(maxPrimitives,primitives);
        }
        Console.WriteLine("Pelican maximum scene points: "+max);
        Assert(max<=4096,"all pelican frames fit scene geometry budget");
        Assert(maxPrimitives<=8192,"all pelican frames fit scene primitive budget");
        Reject(()=>PackedAnimation.Decode(new string('a',60001)),"packed character budget");
        Reject(()=>PackedAnimation.Decode("HoloMapAnimation1:!"),"packed invalid base64");
        Reject(()=>PackedAnimation.Decompress(new byte[7]),"packed short header");
        byte[] bad=(byte[])compressed.Clone();bad[0]=0;Reject(()=>PackedAnimation.Decompress(bad),"packed wrong magic");
        bad=(byte[])compressed.Clone();bad[7]=127;Reject(()=>PackedAnimation.Decompress(bad),"packed decompression bomb length");
        Reject(()=>PackedAnimation.Decompress(compressed.Take(compressed.Length-1).ToArray()),"packed truncated stream");
        Reject(()=>PackedAnimation.Decompress(compressed.Concat(new byte[]{0}).ToArray()),"packed trailing bytes");
        Reject(()=>PackedAnimation.Decompress(new byte[]{72,77,67,49,3,0,0,0,0,0,0,0}),"packed zero-distance match");
        Reject(()=>PackedAnimation.Decompress(new byte[]{72,77,67,49,3,0,0,0,0,1,0,0}),"packed match before beginning");
        Reject(()=>PackedAnimation.Decompress(new byte[]{72,77,67,49,2,0,0,0,1,65,1,0,0}),"packed match overflows output");
        Reject(()=>animation.FrameAt(double.NaN),"packed nonfinite frame time");
        Reject(()=>animation.FrameAt(-1),"packed negative frame time");
        Assert(PackedAnimation.Decode(" \n"+text+"\n ").Frames==72,"packed paste whitespace tolerated");
        MultiConsoleCacheTests(animation);
        foreach(string payload in new[]{"HMA1\nNaN\n1\n1\n<svg/>\n","HMA1\n4\n999999\n1\n<svg/>\n","HMA1\n4\n1\n9\n<svg/>\n","HMA1\n4\n1\n1\n<svg/>"})
        {
            var bytes=System.Text.Encoding.ASCII.GetBytes(payload);var encoded=new List<byte>{72,77,67,49};encoded.AddRange(BitConverter.GetBytes(bytes.Length));
            for(int i=0;i<bytes.Length;i+=8){int count=Math.Min(8,bytes.Length-i);encoded.Add((byte)((1<<count)-1));encoded.AddRange(bytes.Skip(i).Take(count));}
            Reject(()=>PackedAnimation.Decode("HoloMapAnimation1:"+Convert.ToBase64String(encoded.ToArray())),"packed invalid frame table");
        }
    }
    static void MultiConsoleCacheTests(PackedAnimation animation)
    {
        var type=typeof(HoloMapSession);var flags=BindingFlags.NonPublic|BindingFlags.Static;
        var key=type.GetMethod("PackedKey",flags);
        string a=(string)key.Invoke(null,new object[]{77L,100L,"pelican"});
        string b=(string)key.Invoke(null,new object[]{77L,200L,"pelican"});
        string c=(string)key.Invoke(null,new object[]{88L,100L,"pelican"});
        Assert(a!=b&&a!=c&&b!=c,"same PB and animation ID remain isolated across Consoles and owners");
        var session=new HoloMapSession();var instance=BindingFlags.NonPublic|BindingFlags.Instance;
        var caches=(System.Collections.IDictionary)type.GetField("_packed",instance).GetValue(session);
        var scenes=(System.Collections.IDictionary)type.GetField("_scenes",instance).GetValue(session);
        var stateType=type.GetNestedType("PackedState",BindingFlags.NonPublic);
        var sceneType=type.GetNestedType("Scene",BindingFlags.NonPublic);var itemType=type.GetNestedType("Item",BindingFlags.NonPublic);
        void Add(string cacheKey,long caller,long console,string payload)
        {
            var state=Activator.CreateInstance(stateType,true);
            stateType.GetField("CallerId").SetValue(state,caller);stateType.GetField("ConsoleId").SetValue(state,console);
            stateType.GetField("Id").SetValue(state,"pelican");stateType.GetField("Source").SetValue(state,payload);
            stateType.GetField("Animation").SetValue(state,animation);caches.Add(cacheKey,state);
            if(!scenes.Contains(console))scenes.Add(console,Activator.CreateInstance(sceneType,true));
            var items=(System.Collections.IDictionary)sceneType.GetField("Items").GetValue(scenes[console]);
            items.Add(caller+":pelican~0",Activator.CreateInstance(itemType,true));
        }
        Add(a,77,100,"different animation A");Add(b,77,200,"different animation B");Add(c,88,100,"another PB");
        Assert(!ReferenceEquals(caches[a],caches[b])&&stateType.GetField("Source").GetValue(caches[b]).Equals("different animation B"),"two Consoles retain independent payload/cache state");
        type.GetMethod("RemovePacked",instance).Invoke(session,new object[]{77L,100L,"pelican"});
        Assert(!caches.Contains(a)&&caches.Contains(b)&&caches.Contains(c),"removing one Console animation preserves other Console and PB caches");
        var itemsA=(System.Collections.IDictionary)sceneType.GetField("Items").GetValue(scenes[100L]);
        var itemsB=(System.Collections.IDictionary)sceneType.GetField("Items").GetValue(scenes[200L]);
        Assert(!itemsA.Contains("77:pelican~0")&&itemsA.Contains("88:pelican~0")&&itemsB.Contains("77:pelican~0"),"animation removal is scoped to the correct Console and PB geometry");
        type.GetMethod("ClearPacked",instance).Invoke(session,new object[]{77L,200L});
        Assert(caches.Count==1&&caches.Contains(c),"clear animation cache preserves another PB's cache");
        itemsA.Clear();type.GetMethod("CleanupPacked",instance).Invoke(session,null);
        Assert(caches.Count==0,"removed Console geometry releases stale animation cache");
    }
    static void AssetTests(string root)
    {
        foreach(string asset in Directory.GetFiles(Path.Combine(root,"Mod/Data/Svg"),"*.svg"))
            Assert(Svg.Parse(File.ReadAllText(asset)).Geometry.Triangles.Length>0,"packaged SVG parses: "+Path.GetFileName(asset));
        byte[] dds=File.ReadAllBytes(Path.Combine(root,"Mod/Textures/HoloMap/Demo.dds"));
        uint Word(int index)=>BitConverter.ToUInt32(dds,index*4);
        Assert(Word(0)==0x20534444 && Word(1)==124 && Word(19)==32,"packaged image DDS header");
        Assert(Word(3)==64 && Word(4)==96 && Word(5)==96*4 && dds.Length==128+96*64*4,"packaged image dimensions and payload");
        Assert(Word(23)==0xFF && Word(24)==0xFF00 && Word(25)==0xFF0000 && Word(26)==0xFF000000,"packaged image RGBA masks");
        Assert(dds.Skip(128).Where((_,i)=>i%4==3).Any(a=>a==0) && dds.Skip(128).Where((_,i)=>i%4==3).Any(a=>a==255),"packaged image contains transparent and opaque pixels");
        string material=File.ReadAllText(Path.Combine(root,"Mod/Data/Image_Demo.sbc"));
        Assert(material.Contains("HoloMap_Image_Demo") && material.Contains("Textures\\HoloMap\\Demo.dds"),"packaged image material references shipped DDS");
    }
    static void NetworkTests()
    {
        var snapshot = new HoloSnapshot();
        var scene = new HoloSceneData { ConsoleId = 123, View = new[] { 0d,0.8,0,0,0,0,1 }, RootId=987, TrackingCallerId=456, ShipView=new[]{0.55,0d,0,0}, MetresPerUnit=250, ShipOrigin=new[]{2d,3,4} };
        var item = new HoloItemData { CallerId = 456, Id = "triangle", Points = new[] { 0d,0,0,1,0,0,0,1,0 },
            Edges = new[] {0,1,1,2,2,0}, Triangles = new[] {0,1,2}, Style = new[] {1f,1,1,1,0,1,1,0.3f,0.008f},
            Transform = new[] {1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1}, Visible = true, Shaded = true, Layer="terrain" };
        scene.Items.Add(item); snapshot.Scenes.Add(scene);
        scene.Labels.Add(new HoloLabelData {CallerId=456,Id="station-label",Text="STATION",Position=new[]{0d,0.08,0},Style=new[]{1f,1,1,1,0.035f},Visible=true,Layer="labels"});
        scene.ConstructLayer="ship";
        scene.Layers.Add(new HoloLayerData {CallerId=456,Name="terrain",Visible=false,Opacity=0.4f});
        scene.Layers.Add(new HoloLayerData {CallerId=456,Name="labels",Visible=true,Opacity=0.7f});
        using var stream = new MemoryStream();
        var model = ProtoBuf.Meta.RuntimeTypeModel.Create();
        model.Serialize(stream, snapshot); stream.Position = 0;
        var copy = (HoloSnapshot)model.Deserialize(stream, null, typeof(HoloSnapshot));
        Assert(copy.Scenes.Count == 1 && copy.Scenes[0].Items.Count == 1 && copy.Scenes[0].Items[0].Points.SequenceEqual(item.Points), "network geometry round trip");
        Assert(copy.Scenes[0].Items[0].Style.SequenceEqual(item.Style) && copy.Scenes[0].Items[0].Visible && copy.Scenes[0].Items[0].Shaded,"network style round trip");
        Assert(copy.Scenes[0].RootId==987 && copy.Scenes[0].TrackingCallerId==456 && copy.Scenes[0].ShipView.SequenceEqual(scene.ShipView),"live construct network config round trip");
        Assert(copy.Scenes[0].MetresPerUnit==250 && copy.Scenes[0].ShipOrigin.SequenceEqual(scene.ShipOrigin),"shared terrain/ship frame network round trip");
        Assert(copy.Scenes[0].Labels.Count==1 && copy.Scenes[0].Labels[0].Text=="STATION" && copy.Scenes[0].Labels[0].Style[4]==0.035f,"label text and appearance survive replication");
        Assert(copy.Scenes[0].Layers.Count==2 && !copy.Scenes[0].Layers[0].Visible && copy.Scenes[0].Layers[0].Opacity==0.4f,"hidden layer visibility and opacity survive serialization");
        Assert(copy.Scenes[0].Items[0].Layer=="terrain" && copy.Scenes[0].Labels[0].Layer=="labels" && copy.Scenes[0].ConstructLayer=="ship","geometry labels and live ship retain layer assignments");
        // Client snapshot application is atomic even when a later object is invalid.
        var session = new HoloMapSession();
        var apply = typeof(HoloMapSession).GetMethod("ApplySnapshot", BindingFlags.NonPublic|BindingFlags.Instance);
        var scenesField = typeof(HoloMapSession).GetField("_scenes", BindingFlags.NonPublic|BindingFlags.Instance);
        apply.Invoke(session,new object[]{copy});
        var scenes = (System.Collections.IDictionary)scenesField.GetValue(session);
        Assert(scenes.Count == 1,"client applies valid snapshot");
        var prior = scenes[123L]; copy.Scenes[0].Items[0].Triangles = new[]{0,1,999};
        var layerAlpha=typeof(HoloMapSession).GetMethod("LayerAlpha",BindingFlags.NonPublic|BindingFlags.Static);
        Assert((float)layerAlpha.Invoke(null,new object[]{prior,456L,"terrain"})==0,"hidden terrain layer suppresses drawing");
        Assert((float)layerAlpha.Invoke(null,new object[]{prior,789L,"terrain"})==1,"same layer name from another PB remains independent");
        Assert((float)layerAlpha.Invoke(null,new object[]{prior,456L,"labels"})==0.7f,"visible labels inherit layer opacity");
        var retainedItems=(System.Collections.IDictionary)prior.GetType().GetField("Items").GetValue(prior);
        Assert(retainedItems.Count==1,"hiding a layer retains its geometry");
        try { apply.Invoke(session,new object[]{copy}); throw new Exception("Invalid packet applied."); }
        catch(TargetInvocationException ex) { Assert(ex.InnerException is ArgumentException,"bad network index rejected"); }
        Assert(ReferenceEquals(scenes[123L],prior),"invalid snapshot preserves previous scene");
        copy.Scenes[0].Items[0].Triangles=new[]{0,1,2};copy.Scenes[0].Labels[0].Style[4]=float.NaN;
        try{apply.Invoke(session,new object[]{copy});throw new Exception("Invalid label packet applied.");}
        catch(TargetInvocationException ex){Assert(ex.InnerException is ArgumentException,"nonfinite network label rejected");}
        Assert(ReferenceEquals(scenes[123L],prior),"invalid label preserves previous scene");
        copy.Scenes[0].Labels[0].Style[4]=0.035f;copy.Scenes[0].Layers[0].Opacity=float.NaN;
        try{apply.Invoke(session,new object[]{copy});throw new Exception("Invalid layer packet applied.");}
        catch(TargetInvocationException ex){Assert(ex.InnerException is ArgumentException,"invalid network layer opacity rejected");}
        Assert(ReferenceEquals(scenes[123L],prior),"invalid layer preserves previous scene");
        var request = new HoloFrame { Request = true };
        using var requestStream = new MemoryStream(); model.Serialize(requestStream,request);
        Assert(requestStream.Length < 4096,"request fits SE packet budget");
        var frame = new HoloFrame { Revision=1,Index=0,Count=1,Length=2800,Payload=new byte[2800] };
        using var frameStream = new MemoryStream(); model.Serialize(frameStream,frame);
        Assert(frameStream.Length < 4096,"chunk fits SE packet budget");
    }
    static void PreviewTests()
    {
        var motor = new Sandbox.Common.ObjectBuilders.MyObjectBuilder_MotorStator { EntityId=99, TopBlockId=123, RotorEntityId=456, WeldedEntityId=789, Enabled=true, IsWelded=true, ForceWeld=true };
        var connector = new Sandbox.Common.ObjectBuilders.MyObjectBuilder_ShipConnector { EntityId=88, Connected=true, ConnectedEntityId=111, Enabled=true, IsConnecting=true, IsApproaching=true };
        var projector = new Sandbox.Common.ObjectBuilders.MyObjectBuilder_Projector { EntityId=77, Enabled=true,
            ProjectedGrid=new VRage.Game.MyObjectBuilder_CubeGrid(), ProjectedGrids=new List<VRage.Game.MyObjectBuilder_CubeGrid>{new VRage.Game.MyObjectBuilder_CubeGrid()} };
        var builder = new VRage.Game.MyObjectBuilder_CubeGrid {EntityId=22, CreatePhysics=true, IsStatic=false,Editable=true,DestructibleBlocks=true};
        builder.CubeBlocks.Add(motor); builder.CubeBlocks.Add(connector); builder.CubeBlocks.Add(projector);
        PreviewBuilder.Prepare(builder);
        Assert(builder.EntityId==0 && !builder.CreatePhysics && builder.IsStatic && !builder.Editable && !builder.DestructibleBlocks,"preview has no physics or editable gameplay state");
        Assert(motor.EntityId==0 && motor.TopBlockId==null && motor.RotorEntityId==null && motor.WeldedEntityId==null && !motor.IsWelded && !motor.ForceWeld,"preview drops original mechanical connection IDs");
        Assert(connector.EntityId==0 && !connector.Connected && connector.ConnectedEntityId==0 && !connector.IsConnecting && !connector.IsApproaching,"preview drops original docking links");
        Assert(!motor.Enabled && !connector.Enabled && !projector.Enabled,"preview disables functional blocks");
        Assert(projector.ProjectedGrid==null && projector.ProjectedGrids==null,"preview strips recursive projector payloads");
        var root = MatrixD.CreateRotationY(0.6)*MatrixD.CreateTranslation(500,700,-900);
        var subgrid = MatrixD.CreateRotationX(0.3)*MatrixD.CreateTranslation(4,2,1)*root;
        var relative = subgrid*MatrixD.Invert(root);
        Assert(Vector3D.Distance(relative.Translation,new Vector3D(4,2,1))<1e-8,"subgrid retains root-relative articulation");
        var world = relative*MatrixD.CreateTranslation(-new Vector3D(1,0,0))*MatrixD.CreateScale(0.1)*MatrixD.CreateTranslation(0,0.8,0);
        var pose = MatrixD.Normalize(world);
        Assert(Vector3D.Distance(world.Translation,pose.Translation)<1e-9 && Math.Abs(pose.Right.Length()-1)<1e-9,"normalizing preview pose preserves scaled translation");
        var sourceBlock = MatrixD.CreateRotationZ(0.2)*MatrixD.CreateTranslation(2,3,4)*subgrid;
        var display = MatrixD.CreateRotationX(0.1)*MatrixD.CreateTranslation(100,200,300);
        var worldToDisplay = MatrixD.Invert(root)*MatrixD.CreateTranslation(-new Vector3D(1,0,0))*MatrixD.CreateScale(0.1)*MatrixD.CreateTranslation(0,0.8,0)*display;
        var blockPose = MatrixD.Normalize(sourceBlock*worldToDisplay);
        var expectedBlockPosition = Vector3D.Transform(new Vector3D(2,3,4),relative*MatrixD.CreateTranslation(-new Vector3D(1,0,0))*MatrixD.CreateScale(0.1)*MatrixD.CreateTranslation(0,0.8,0)*display);
        Assert(Vector3D.Distance(blockPose.Translation,expectedBlockPosition)<1e-8,"fat-block pose follows the same map transform as armor");
        var movingSubpart = MatrixD.CreateRotationY(0.5)*MatrixD.CreateTranslation(0,1,0)*sourceBlock;
        var subpartPose = MatrixD.Normalize(movingSubpart*worldToDisplay);
        Assert(Math.Abs(Vector3D.Distance(blockPose.Translation,subpartPose.Translation)-0.1)<1e-8,"subpart offset is scaled exactly once");
        // Piston-like three-segment hierarchy. Uniform scale belongs only to the
        // grid; child local transforms remain unscaled at every nesting depth.
        var miniatureGrid = MatrixD.CreateScale(0.01)*MatrixD.CreateTranslation(50,60,70);
        var localBlock = MatrixD.CreateTranslation(2,3,4);
        var localSegment = MatrixD.CreateTranslation(0,1,0);
        var basePose = localBlock*miniatureGrid;
        var segment1 = localSegment*basePose;
        var segment2 = localSegment*segment1;
        var segment3 = localSegment*segment2;
        Assert(Math.Abs(segment1.Right.Length()-0.01)<1e-10 && Math.Abs(segment2.Right.Length()-0.01)<1e-10
            && Math.Abs(segment3.Right.Length()-0.01)<1e-10,"nested piston segments inherit uniform scale only once");
        Assert(Math.Abs(Vector3D.Distance(basePose.Translation,segment3.Translation)-0.03)<1e-9,"three piston local offsets retain miniature spacing");
        var localView = MatrixD.CreateScale(0.7)*MatrixD.CreateRotationY(0.2)*MatrixD.CreateTranslation(0,0.85,0);
        var anchorA = MatrixD.CreateRotationX(0.3)*MatrixD.CreateTranslation(12345,-23456,34567);
        var anchorB = MatrixD.CreateRotationY(-0.4)*MatrixD.CreateTranslation(12350,-23459,34565);
        var vertex = new Vector3D(0.4,0.2,-0.3);
        var parentLocalA = Vector3D.Transform(Vector3D.Transform(vertex,localView*anchorA),MatrixD.Invert(anchorA));
        var parentLocalB = Vector3D.Transform(Vector3D.Transform(vertex,localView*anchorB),MatrixD.Invert(anchorB));
        Assert(Vector3D.Distance(parentLocalA,parentLocalB)<1e-8,"attached wire and triangle coordinates stay invariant when Console moves");
    }
    static void LabelTests()
    {
        Assert(GlyphFont.Layout("").Count==0 && GlyphFont.Layout(" ").Count==0,"empty and space labels have no glyph pixels");
        var a=GlyphFont.Layout("A");var lower=GlyphFont.Layout("a");
        Assert(a.Count>0 && a.Count==lower.Count && a.Select(r=>r.X).SequenceEqual(lower.Select(r=>r.X)),"label font normalizes lowercase");
        Assert(GlyphFont.Layout("@").Count==GlyphFont.Layout("?").Count,"unsupported characters use fallback glyph");
        Assert(a.All(r=>r.Width>=1 && r.Width<=5 && r.Y>=-3 && r.Y<=3),"glyph runs stay inside 5x7 grid");
        var cameraA=MatrixD.CreateWorld(Vector3D.Zero,Vector3D.Forward,Vector3D.Up);
        var cameraB=MatrixD.CreateWorld(Vector3D.Zero,Vector3D.Backward,Vector3D.Up);
        var normalA=Vector3D.Normalize(Vector3D.Cross(-cameraA.Up,cameraA.Right));
        var normalB=Vector3D.Normalize(Vector3D.Cross(-cameraB.Up,cameraB.Right));
        Assert(Vector3D.Dot(normalA,cameraA.Backward)>0.99 && Vector3D.Dot(normalB,cameraB.Backward)>0.99,"label quad faces each viewer independently");
    }
    static void LayerTests()
    {
        Assert(LayerRules.Name(" Terrain ")=="terrain","layer names canonicalize case and whitespace");
        Assert(LayerRules.Name("radar_contacts-2")=="radar_contacts-2","custom layer names supported");
        Reject(()=>LayerRules.Name(""),"empty layer name");
        Reject(()=>LayerRules.Name("terrain/ship"),"invalid layer delimiter");
        Reject(()=>LayerRules.Name(new string('a',33)),"layer name budget");
        Reject(()=>LayerRules.Opacity(float.NaN),"nonfinite layer opacity");
        Reject(()=>LayerRules.Opacity(-0.1f),"negative layer opacity");
        Reject(()=>LayerRules.Opacity(1.1f),"layer opacity above one");
        Assert(LayerRules.Alpha(false,1)==0 && LayerRules.Alpha(true,0)==0 && LayerRules.Alpha(true,0.4f)==0.4f,"layer alpha combines visibility and opacity");
        Assert(Math.Abs(LayerRules.PreviewTransparency(1)-0.35f)<1e-6 && LayerRules.PreviewTransparency(0)==1
            && Math.Abs(LayerRules.PreviewTransparency(0.5f)-0.675f)<1e-6,"native ship fade preserves baseline hologram transparency");
    }
    static void SvgTests()
    {
        var square=Svg.Parse("<svg viewBox='0 0 10 10'><rect width='10' height='10' fill='#0ff'/></svg>");
        Assert(Math.Abs(Area(square.Geometry)-100)<1e-8,"SVG rectangle fill area");
        Assert(square.Geometry.Points[0]==P(-5,5) && square.Colors.All(c=>c==new Vector4(0,1,1,1)),"SVG centered coordinates and per-triangle color");
        var concave=Svg.Parse("<svg><path d='M0 0 H3 V1 H1 V3 H0 Z' fill='red'/></svg>");
        Assert(Math.Abs(Area(concave.Geometry)-5)<1e-8,"SVG concave fill triangulates");
        var nested=Svg.Parse("<svg><g transform='translate(10 20)' opacity='0.5'><rect width='2' height='3' transform='scale(2)' style='fill: #123456; fill-opacity: 0.4'/></g></svg>");
        Assert(nested.Geometry.Points[0]==P(10,-20) && Math.Abs(Area(nested.Geometry)-24)<1e-8,"SVG parent and child transforms compose");
        Assert(nested.Colors.All(c=>Math.Abs(c.W-0.2f)<1e-6),"SVG inherited opacity and inline style");
        Assert(Vector3D.Distance(Vector3D.Transform(P(1,0),Svg.Transform("translate(10 0) scale(2)")),P(12,0))<1e-8,"SVG transform list order");
        Assert(Vector3D.Distance(Vector3D.Transform(P(2,1),Svg.Transform("rotate(90 1 1)")),P(1,2))<1e-8,"SVG rotation pivot");
        Assert(Vector3D.Transform(P(2,3),Svg.Transform("matrix(1 0 0 1 4 5)"))==P(6,8),"SVG matrix convention");
        var circle=Svg.Parse("<svg><circle r='10' fill='blue'/></svg>",16);
        Assert(Math.Abs(Area(circle.Geometry)-Math.PI*100)<1 && circle.Geometry.Points.Length==64,"SVG circle tessellation");
        var ellipse=Svg.Parse("<svg><ellipse rx='10' ry='3'/></svg>",16);
        Assert(Math.Abs(Area(ellipse.Geometry)-Math.PI*30)<0.3,"SVG ellipse tessellation");
        foreach(string path in new[]{"M0 0 l10 0 v10 h-10 z","M0 0 C0 10 10 10 10 0 S20 -10 20 0","M0 0 Q5 10 10 0 T20 0","M0 0 A10 10 0 0 1 20 0","M0 0 a10 5 30 1 0 20 0","M0 0 L10 0 M0 10 L10 10","M0 0 L10 0 Z L0 10"})
        {
            var g=Svg.Parse("<svg><path fill='none' stroke='cyan' stroke-width='0.2' d='"+path+"'/></svg>");
            Assert(g.Geometry.Triangles.Length>0 && g.Colors.Length==g.Geometry.Triangles.Length/3,"SVG path command: "+path);
        }
        var open=Svg.Parse("<svg><polyline points='0,0 10,0 10,10' fill='none' stroke='white' stroke-width='2'/></svg>");
        Assert(open.Geometry.Points.All(p=>p.X>=0 && p.Y<=1),"SVG butt-cap strokes do not extend past endpoints");
        var lines=Svg.Parse("<svg><line x1='0' y1='0' x2='10' y2='0' stroke='red' stroke-width='2'/></svg>");
        Assert(Math.Abs(Area(lines.Geometry)-20)<1e-9,"SVG line stroke width is geometry");
        foreach(string bad in new[]{
            "<svg><script/></svg>","<!DOCTYPE svg><svg/>","<svg><image href='http://example.com'/></svg>",
            "<svg><rect width='10' height='10' onclick='x'/></svg>","<svg><rect width='10' height='10' fill='url(#a)'/></svg>",
            "<svg><rect width='10' height='10' clip-path='url(#a)'/></svg>","<svg><rect width='10' height='10' fill='&xx;'/></svg>",
            "<svg><path d='M0 0 L1e999 0'/></svg>","<svg><path d='M0 0 L1'/></svg>",
            "<svg><path d='L0 0'/></svg>","<svg><g></svg>","<svg><rect width='2px' height='3'/></svg>",
            "<svg><rect width='2' height='3' style='filter:blur(2px)'/></svg>","<svg><rect width='2' height='3' fill-opacity='2'/></svg>",
            "<svg><path fill='none' stroke='white' d='M0 0 A1 1 0 2 0 1 1'/></svg>",
            "<svg><rect width='2' height='3'/>","<svg/><svg/>"})
            Reject(()=>Svg.Parse(bad),"hostile/unsupported SVG: "+bad);
        Reject(()=>Svg.Parse(new string(' ',Svg.MaxCharacters+1)),"SVG character budget");
        Reject(()=>Svg.Parse("<svg><circle r='1'/></svg>",33),"SVG curve sample budget");
        string many="<svg>"+string.Concat(Enumerable.Repeat("<circle r='1' fill='none' stroke='white'/>",20))+"</svg>";
        Reject(()=>Svg.Parse(many),"SVG mesh budget");
        ArtworkPacketTests(square);
    }
    static void ArtworkPacketTests(SvgMesh mesh)
    {
        var snapshot=new HoloSnapshot();var scene=new HoloSceneData{ConsoleId=222,View=new[]{0d,0.8,0,0,0,0,1}};snapshot.Scenes.Add(scene);
        var item=new HoloItemData{CallerId=456,Id="svg",Points=mesh.Geometry.Points.SelectMany(p=>new[]{p.X,p.Y,p.Z}).ToArray(),Triangles=mesh.Geometry.Triangles,
            TriangleColors=mesh.Colors.SelectMany(c=>new[]{c.X,c.Y,c.Z,c.W}).ToArray(),Style=new[]{0f,0,0,0,1,1,1,1,0.008f},
            Transform=new[]{1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1},Visible=true,Opacity=0};scene.Items.Add(item);
        var model=ProtoBuf.Meta.RuntimeTypeModel.Create();using var stream=new MemoryStream();model.Serialize(stream,snapshot);stream.Position=0;
        var copy=(HoloSnapshot)model.Deserialize(stream,null,typeof(HoloSnapshot));
        Assert(copy.Scenes[0].Items[0].Edges.Length==0 && copy.Scenes[0].Items[0].TriangleColors.SequenceEqual(item.TriangleColors),"SVG mesh colors and empty edges survive network");
        Assert(copy.Scenes[0].Items[0].Opacity==0,"zero object opacity survives network");
        var session=new HoloMapSession();var apply=typeof(HoloMapSession).GetMethod("ApplySnapshot",BindingFlags.NonPublic|BindingFlags.Instance);
        apply.Invoke(session,new object[]{copy});
        Assert(true,"client applies SVG mesh");
        foreach(Action mutate in new Action[]{()=>copy.Scenes[0].Items[0].TriangleColors=new[]{1f},()=>copy.Scenes[0].Items[0].TriangleColors[0]=float.NaN,
            ()=>copy.Scenes[0].Items[0].UV=new[]{0f},()=>copy.Scenes[0].Items[0].Opacity=-1})
        {
            copy.Scenes[0].Items[0].TriangleColors=(float[])item.TriangleColors.Clone();copy.Scenes[0].Items[0].UV=null;copy.Scenes[0].Items[0].Opacity=1;mutate();
            try{apply.Invoke(session,new object[]{copy});throw new Exception("Invalid artwork packet accepted.");}catch(TargetInvocationException e){Assert(e.InnerException is ArgumentException,"invalid artwork packet rejected");}
        }
        var uvValues=typeof(HoloMapSession).GetMethod("UvValues",BindingFlags.NonPublic|BindingFlags.Static);
        var readUv=typeof(HoloMapSession).GetMethod("ReadUv",BindingFlags.NonPublic|BindingFlags.Static);
        var uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
        var values=(float[])uvValues.Invoke(null,new object[]{uv});
        Assert(((Vector2[])readUv.Invoke(null,new object[]{values,4})).SequenceEqual(uv),"image UV corners round trip");
        item.UV=values;item.Material="HoloMap_Image_Demo";stream.SetLength(0);model.Serialize(stream,snapshot);stream.Position=0;
        var imageCopy=(HoloSnapshot)model.Deserialize(stream,null,typeof(HoloSnapshot));
        Assert(imageCopy.Scenes[0].Items[0].Material==item.Material && imageCopy.Scenes[0].Items[0].UV.SequenceEqual(values),"image material and UV data survive network");
    }
    static void HelperTests(Type apiType)
    {
        var pose=apiType.GetMethod("Pose",new[]{typeof(Vector3D),typeof(double),typeof(Vector3D?)});
        var expected=MatrixD.CreateScale(0.4)*MatrixD.CreateFromYawPitchRoll(0.2,0.3,0.1)*MatrixD.CreateTranslation(5,6,7);
        var actual=(MatrixD)pose.Invoke(null,new object[]{P(5,6,7),0.4,(Vector3D?)P(0.3,0.2,0.1)});
        Assert(Vector3D.Distance(Vector3D.Transform(P(2,3,4),expected),Vector3D.Transform(P(2,3,4),actual))<1e-9,"Pose helper preserves scale/rotation/translation order and axis convention");
        var rgb=(Vector4)apiType.GetMethod("Rgb").Invoke(null,new object[]{(byte)10,(byte)128,(byte)255,0.5f});
        Assert(rgb==new Vector4(10/255f,128/255f,1,0.5f),"Rgb helper converts byte channels without changing alpha");
        try{apiType.GetMethod("Rgba").Invoke(null,new object[]{float.NaN,0f,0f,1f});throw new Exception("Invalid helper color accepted.");}
        catch(TargetInvocationException e){Assert(e.InnerException is ArgumentException,"Rgba helper rejects nonfinite colors");}
        var bind=apiType.GetMethod("Bind",BindingFlags.NonPublic|BindingFlags.Static).MakeGenericMethod(typeof(Func<string>));
        var dictionary=new Dictionary<string,Delegate>{{"Version",new Func<string>(()=>"0.1.0")}};
        var arguments=new object[]{null,dictionary,"Version"};bind.Invoke(null,arguments);
        Assert(((Func<string>)arguments[0])()=="0.1.0","inferred Bind preserves typed delegate invocation");
        dictionary["Version"]=new Func<int>(()=>1);arguments=new object[]{null,dictionary,"Version"};bind.Invoke(null,arguments);
        Assert(arguments[0]==null,"inferred Bind rejects incompatible delegate signatures");
    }
    static void AnimationTests(Type apiType)
    {
        var method=apiType.GetMethod("AnimationValue");
        double Value(double time,bool loop=false,bool ping=false)=>(double)method.Invoke(null,new object[]{0d,10d,time,2d,loop,ping});
        Assert(Value(1)==5 && Value(3)==10,"animation interpolates and clamps final value");
        Assert(Value(2,true)==0 && Value(3,true)==5,"animation looping");
        Assert(Value(2,true,true)==10 && Value(3,true,true)==5 && Value(4,true,true)==0,"animation ping-pong looping");
        Assert(Value(5,false,true)==0,"animation one-shot ping-pong ends at start");
        try{Value(double.NaN);throw new Exception("Bad animation time accepted.");}catch(TargetInvocationException e){Assert(e.InnerException is ArgumentException,"nonfinite animation time rejected");}
    }
    static void AppearanceTests()
    {
        var outer=new[]{P(0,0),P(4,0),P(4,4),P(0,4)};
        var hole=new[]{P(1,1),P(3,1),P(3,3),P(1,3)};
        Assert(Math.Abs(Area(PlanarFill.Tessellate(new[]{outer,hole},true))-12)<1e-8,"even-odd native polygon hole removes inner area");
        Assert(Math.Abs(Area(PlanarFill.Tessellate(new[]{outer,hole},false))-16)<1e-8,"nonzero same-winding nested contour remains filled");
        Assert(Math.Abs(Area(PlanarFill.Tessellate(new[]{outer,hole.Reverse().ToArray()},false))-12)<1e-8,"nonzero opposite-winding contour creates hole");
        var island=new[]{P(1.5,1.5),P(2.5,1.5),P(2.5,2.5),P(1.5,2.5)};
        Assert(Math.Abs(Area(PlanarFill.Tessellate(new[]{outer,hole,island},true))-13)<1e-8,"nested island inside polygon hole");
        var shifted=hole.Select(p=>p+P(5,0)).ToArray();
        Assert(Math.Abs(Area(PlanarFill.Tessellate(new[]{outer,shifted},true))-20)<1e-8,"disconnected filled native contours");
        var rotation=MatrixD.CreateFromYawPitchRoll(0.3,0.8,0.2)*MatrixD.CreateTranslation(100,200,300);
        Assert(Math.Abs(Area(PlanarFill.Tessellate(new[]{outer.Select(p=>Vector3D.Transform(p,rotation)).ToArray(),hole.Select(p=>Vector3D.Transform(p,rotation)).ToArray()},true))-12)<1e-6,"holes in arbitrary 3D planar surface: " + Area(PlanarFill.Tessellate(new[]{outer.Select(p=>Vector3D.Transform(p,rotation)).ToArray(),hole.Select(p=>Vector3D.Transform(p,rotation)).ToArray()},true)));
        Reject(()=>PlanarFill.Tessellate(new[]{outer,new[]{P(1,1),P(2,1,1),P(1,2)}}),"nonplanar hole rejected");
        var quad=Geometry.Polygons(outer,new[]{new[]{0,1,2,3}});
        var mesh=MeshEffects.Solid(quad,Vector4.One,Vector4.One,null,null,new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)});
        var clipped=MeshEffects.Clip(mesh,new[]{new Vector4(1,0,0,-2)});
        Assert(Math.Abs(Area(clipped.Geometry)-8)<1e-8 && clipped.Geometry.Points.All(p=>p.X>=2-1e-8),"native plane clips fill and outlines");
        Assert(clipped.UV.Zip(clipped.Geometry.Points,(uv,p)=>Math.Abs(uv.X-p.X/4)<1e-6&&Math.Abs(uv.Y-p.Y/4)<1e-6).All(x=>x),"clipped textured quads interpolate UVs");
        var wire=MeshEffects.Solid(Geometry.Wires(new[]{P(-2,0),P(2,0)},new[]{new Vector2I(0,1)}),Vector4.Zero,Vector4.One);
        var halfWire=MeshEffects.Clip(wire,new[]{new Vector4(1,0,0,0)});
        Assert(halfWire.Geometry.Edges.Length==2 && halfWire.Geometry.Points.Min(p=>p.X)==0&&halfWire.Geometry.Points.Max(p=>p.X)==2,"native line is trimmed at clip boundary");
        var empty=MeshEffects.Clip(mesh,new[]{new Vector4(1,0,0,-10)});
        Assert(empty.Geometry.Triangles.Length==0&&empty.Geometry.Edges.Length==0,"fully clipped objects retain empty valid geometry");
        Reject(()=>MeshEffects.ValidatePlanes(new[]{new Vector4(0,0,0,1)}),"zero clip normal rejected");
        Reject(()=>MeshEffects.ValidatePlanes(new[]{new Vector4(float.NaN,0,0,1)}),"nonfinite clip plane rejected");
        var gradient=new GradientStyle{From=P(0,0),To=P(4,0),Start=new Vector4(1,0,0,0.25f),End=new Vector4(0,0,1,1),Resolution=4};
        var shaded=MeshEffects.Gradient(mesh,gradient.Sample,4);
        Assert(Math.Abs(Area(shaded.Geometry)-16)<1e-8,"native gradient subdivision preserves surface area");
        Assert(shaded.Colors.Any(c=>c.X>c.Z)&&shaded.Colors.Any(c=>c.Z>c.X)&&shaded.EdgeColors.Length==16,"gradient colors apply to filled triangles and line segments");
        Assert(shaded.Colors.All(c=>c.W>=0.25&&c.W<=1),"gradient alpha is interpolated");
        var bands=MeshEffects.LinearGradient(mesh,gradient.Parameter,gradient.Palette,32);
        Assert(Math.Abs(Area(bands.Geometry)-16)<1e-8,"shared linear gradient bands preserve surface area");
        Assert(SurfaceColorAt(bands,P(1.26,0.4))==SurfaceColorAt(bands,P(1.26,3.6)),"same linear coordinate has identical color across original triangles");
        var flipped=MeshEffects.Solid(Geometry.Polygons(outer,new[]{new[]{0,1,3},new[]{1,2,3}}),Vector4.One,Vector4.One);
        var flippedBands=MeshEffects.LinearGradient(flipped,gradient.Parameter,gradient.Palette,32);
        Assert(SurfaceColorAt(bands,P(1.26,2.4))==SurfaceColorAt(flippedBands,P(1.26,2.4)),"linear gradient color is independent of triangulation diagonal");
        Assert(bands.Colors.Select(c=>c.X).Distinct().Count()==32,"linear gradient has 32 consistent global color levels");
        Assert(bands.Geometry.Points.Length<500,"shared gradient bands fit modest geometry budget");
        gradient.Radial=true;Assert(gradient.Sample(P(0,0)).X==1&&gradient.Sample(P(4,0)).Z==1,"radial native gradient reaches endpoint colors");
        Assert(MeshEffects.Text("MAP",0.1).Triangles.Length>0,"native fixed-plane text is mesh geometry");
        var startText=MeshEffects.Text("HI",7,"start");var endText=MeshEffects.Text("HI",7,"end");
        Assert(startText.Points.Min(p=>p.X)>=0&&endText.Points.Max(p=>p.X)<=0,"native text anchor alignment");
        var donut=Svg.Parse("<svg viewBox='0 0 4 4'><path fill-rule='evenodd' d='M0 0 H4 V4 H0 Z M1 1 H3 V3 H1 Z'/></svg>");
        Assert(Math.Abs(Area(donut.Geometry)-12)<1e-8,"SVG even-odd holes");
        var nonzero=Svg.Parse("<svg><path d='M0 0 H4 V4 H0 Z M1 1 V3 H3 V1 Z'/></svg>");
        Assert(Math.Abs(Area(nonzero.Geometry)-12)<1e-8,"SVG default nonzero hole winding");
        var text=Svg.Parse("<svg><text x='10' y='20' font-size='7' text-anchor='middle' fill='#0ff'>HI</text></svg>");
        Assert(text.Geometry.Triangles.Length>0&&text.Colors.All(c=>c.Y==1&&c.Z==1),"SVG text uses glyph geometry and fill color");
        string defs="<defs><linearGradient id='g'><stop offset='0%' stop-color='red'/><stop offset='100%' stop-color='blue'/></linearGradient></defs>";
        var svgGradient=Svg.Parse("<svg viewBox='0 0 10 10'>"+defs+"<rect width='10' height='10' fill='url(#g)'/></svg>");
        Assert(Math.Abs(Area(svgGradient.Geometry)-100)<1e-8&&svgGradient.Colors.Any(c=>c.X>c.Z)&&svgGradient.Colors.Any(c=>c.Z>c.X),"SVG bounding-box linear gradient is sampled across geometry");
        var radial=Svg.Parse("<svg><defs><radialGradient id='r'><stop offset='0' stop-color='white'/><stop offset='1' stop-color='blue'/></radialGradient></defs><rect width='10' height='10' fill='url(#r)'/></svg>");
        Assert(radial.Colors.Select(c=>c.X).Distinct().Count()>2,"SVG radial gradient varies across surface");
        var clip=Svg.Parse("<svg><defs><clipPath id='c'><rect x='2' width='2' height='4'/></clipPath></defs><rect width='4' height='4' clip-path='url(#c)'/></svg>");
        Assert(Math.Abs(Area(clip.Geometry)-8)<1e-6&&clip.Geometry.Points.All(p=>p.X>=2-1e-6),"SVG clipPath trims fill geometry");
        var clippedStroke=Svg.Parse("<svg><defs><clipPath id='c'><rect width='2' height='2'/></clipPath></defs><line x1='-2' y1='1' x2='4' y2='1' stroke='white' stroke-width='1' clip-path='url(#c)'/></svg>");
        Assert(Math.Abs(Area(clippedStroke.Geometry)-2)<1e-6,"SVG clipping applies to strokes");
        var unionClip=Svg.Parse("<svg clip-path='url(#c)'><defs><clipPath id='c'><rect width='3' height='4'/><rect x='1' width='3' height='4'/></clipPath></defs><rect width='4' height='4'/></svg>");
        Assert(Math.Abs(Area(unionClip.Geometry)-16)<1e-6,"root SVG clip and overlapping clip children form a union");
        var userGradient=Svg.Parse("<svg viewBox='0 0 10 10'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x2='100%'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='10' height='10' fill='url(#g)'/></svg>");
        Assert(userGradient.Colors.Any(c=>c.X>c.Z)&&userGradient.Colors.Any(c=>c.Z>c.X),"user-space SVG gradient percentages use viewport dimensions");
        foreach(string bad in new[]{"<svg><text font-size='7'>&evil;</text></svg>","<svg><defs><linearGradient id='g'><stop offset='2'/></linearGradient></defs><rect width='2' height='2' fill='url(#g)'/></svg>","<svg><defs><clipPath id='c' clipPathUnits='objectBoundingBox'><rect width='1' height='1'/></clipPath></defs><rect width='2' height='2' clip-path='url(#c)'/></svg>"})Reject(()=>Svg.Parse(bad),"invalid advanced SVG rejected");
    }
    static Vector4 SurfaceColorAt(SurfaceMesh mesh,Vector3D p)
    {
        for(int i=0;i<mesh.Geometry.Triangles.Length;i+=3)
        {
            var a=mesh.Geometry.Points[mesh.Geometry.Triangles[i]];var b=mesh.Geometry.Points[mesh.Geometry.Triangles[i+1]];var c=mesh.Geometry.Points[mesh.Geometry.Triangles[i+2]];
            double s=Vector3D.Cross(b-a,c-a).Z;if(Math.Abs(s)<1e-12)continue;s=s>0?1:-1;
            if(s*Vector3D.Cross(b-a,p-a).Z>=-1e-8&&s*Vector3D.Cross(c-b,p-b).Z>=-1e-8&&s*Vector3D.Cross(a-c,p-c).Z>=-1e-8)return mesh.Colors[i/3];
        }
        throw new Exception("Gradient test point is outside mesh.");
    }
    static void AppearancePacketTests()
    {
        var snapshot=new HoloSnapshot();var scene=new HoloSceneData{ConsoleId=900,View=new[]{0d,0.8,0,0,0,0,1}};snapshot.Scenes.Add(scene);
        var item=new HoloItemData{CallerId=77,Id="styled",Points=new[]{0d,0,0,1,0,0,0,1,0},Edges=new[]{0,1},Triangles=new[]{0,1,2},
            Style=new[]{1f,1,1,1,1,1,1,1,0.008f},Transform=new[]{1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1},Visible=true,
            EdgeColors=new[]{1f,0,0,0.5f},TriangleColors=new[]{0f,1,0,0.7f},Opacity=0.4f,Emission=4.5f};scene.Items.Add(item);
        scene.Labels.Add(new HoloLabelData{CallerId=77,Id="label",Text="TEST",Position=new[]{0d,0,0},Style=new[]{1f,1,1,1,0.03f},Visible=true,Opacity=0.3f,Emission=2.5f});
        var model=ProtoBuf.Meta.RuntimeTypeModel.Create();using var stream=new MemoryStream();model.Serialize(stream,snapshot);stream.Position=0;
        var copy=(HoloSnapshot)model.Deserialize(stream,null,typeof(HoloSnapshot));
        Assert(copy.Scenes[0].Items[0].EdgeColors.SequenceEqual(item.EdgeColors)&&copy.Scenes[0].Items[0].Emission==4.5f,"line gradient colors and emission survive network");
        Assert(copy.Scenes[0].Labels[0].Opacity==0.3f&&copy.Scenes[0].Labels[0].Emission==2.5f,"label opacity and emission survive network");
        var session=new HoloMapSession();var apply=typeof(HoloMapSession).GetMethod("ApplySnapshot",BindingFlags.NonPublic|BindingFlags.Instance);apply.Invoke(session,new object[]{copy});Assert(true,"client applies appearance snapshot");
        foreach(Action bad in new Action[]{()=>copy.Scenes[0].Items[0].Emission=float.NaN,()=>copy.Scenes[0].Items[0].EdgeColors=new[]{1f},()=>copy.Scenes[0].Labels[0].Emission=21,()=>copy.Scenes[0].Labels[0].Opacity=-1})
        {
            copy.Scenes[0].Items[0].Emission=4.5f;copy.Scenes[0].Items[0].EdgeColors=item.EdgeColors;copy.Scenes[0].Labels[0].Emission=2.5f;copy.Scenes[0].Labels[0].Opacity=0.3f;bad();
            try{apply.Invoke(session,new object[]{copy});throw new Exception("Invalid appearance packet accepted.");}catch(TargetInvocationException e){Assert(e.InnerException is ArgumentException,"bad appearance packet rejected");}
        }
        var premultiply=typeof(HoloMapSession).GetMethod("Premultiply",BindingFlags.NonPublic|BindingFlags.Static);
        var color=(Vector4)premultiply.Invoke(null,new object[]{new Vector4(1,0.5f,0,0.25f),5f});
        Assert(color.X==1.25f&&color.Y==0.625f&&color.W==0.25f,"emission boosts HDR RGB without changing transparency");
    }
    static void ValidateModExceptions(CSharpCompilation compilation)
    {
        // Installed default-whitelist registrations permit these exception types.
        // A normal C# build alone does not enforce the game's sandbox whitelist.
        var allowed=new HashSet<string>{"System.Exception","System.ArgumentException","System.ArgumentNullException","System.InvalidOperationException","System.FormatException","System.NullReferenceException","System.DivideByZeroException","System.InvalidCastException","System.IO.FileNotFoundException","System.NotSupportedException"};
        var checkedTypes=new HashSet<string>();
        foreach(var tree in compilation.SyntaxTrees)
        {
            var model=compilation.GetSemanticModel(tree);
            foreach(var syntax in tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax>())
            {
                var type=model.GetTypeInfo(syntax).Type as INamedTypeSymbol;if(type==null||!type.Name.EndsWith("Exception",StringComparison.Ordinal))continue;
                string name=type.ToDisplayString();if(!checkedTypes.Add(name))continue;
                bool exception=false;for(var parent=type;parent!=null;parent=parent.BaseType)if(parent.ToDisplayString()=="System.Exception"){exception=true;break;}
                if(exception)Assert(allowed.Contains(name),"mod exception type is sandbox-allowed: "+name);
            }
        }
        Assert(!allowed.Contains("System.ArgumentOutOfRangeException"),"regression: out-of-range exception is prohibited by the game");
    }
    static void RequireCompile(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream(); var result = compilation.Emit(stream);
        if (!result.Success) throw new Exception(string.Join(Environment.NewLine,result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error).Take(25)));
    }
    // Narrow regression checks grounded in actual game-log whitelist failures.
    // These do not replace the game's full runtime sandbox analyzer.
    static List<string> KnownSandboxViolations(CSharpCompilation compilation)
    {
        var failures = new List<string>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>())
            {
                var symbol = model.GetSymbolInfo(node).Symbol;
                if (symbol != null && symbol.Name == "AssetName" && symbol.ContainingType != null
                    && symbol.ContainingType.ToDisplayString() == "VRage.Game.Models.MyModel")
                    failures.Add(node.GetLocation()+": SE prohibits MyModel.AssetName; access IMyModel through IMyEntity instead.");
            }
            foreach (var node in tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax>())
            {
                var type = model.GetTypeInfo(node).Type as INamedTypeSymbol;
                if (type != null && type.Name == "ReadOnlyDictionary" && type.ContainingNamespace.ToDisplayString() == "System.Collections.ObjectModel")
                    failures.Add(node.GetLocation()+": SE prohibits ReadOnlyDictionary; use ImmutableDictionary.");
            }
        }
        return failures;
    }
    static void DumpIl(MethodBase method)
    {
        Console.WriteLine(method.DeclaringType.FullName+"::"+method);
        var bytes=method.GetMethodBody()?.GetILAsByteArray(); if(bytes==null)return;
        var opcodes=typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public)
            .Select(f=>(System.Reflection.Emit.OpCode)f.GetValue(null)).ToDictionary(o=>unchecked((ushort)o.Value));
        int position=0;
        while(position<bytes.Length)
        {
            int start=position; ushort code=bytes[position++]; if(code==0xfe)code=(ushort)(0xfe00|bytes[position++]);
            var op=opcodes[code]; object value="";
            switch(op.OperandType)
            {
                case System.Reflection.Emit.OperandType.InlineNone: break;
                case System.Reflection.Emit.OperandType.ShortInlineI: value=(sbyte)bytes[position++]; break;
                case System.Reflection.Emit.OperandType.ShortInlineVar: value=bytes[position++]; break;
                case System.Reflection.Emit.OperandType.InlineVar: value=BitConverter.ToUInt16(bytes,position);position+=2;break;
                case System.Reflection.Emit.OperandType.ShortInlineBrTarget: value=position+1+(sbyte)bytes[position];position++;break;
                case System.Reflection.Emit.OperandType.InlineBrTarget: value=position+4+BitConverter.ToInt32(bytes,position);position+=4;break;
                case System.Reflection.Emit.OperandType.InlineI: value=BitConverter.ToInt32(bytes,position);position+=4;break;
                case System.Reflection.Emit.OperandType.InlineI8: value=BitConverter.ToInt64(bytes,position);position+=8;break;
                case System.Reflection.Emit.OperandType.ShortInlineR: value=BitConverter.ToSingle(bytes,position);position+=4;break;
                case System.Reflection.Emit.OperandType.InlineR: value=BitConverter.ToDouble(bytes,position);position+=8;break;
                case System.Reflection.Emit.OperandType.InlineSwitch:
                    int count=BitConverter.ToInt32(bytes,position);position+=4+count*4;value="switch "+count;break;
                default:
                    int token=BitConverter.ToInt32(bytes,position);position+=4;
                    try { value=op.OperandType==System.Reflection.Emit.OperandType.InlineString?method.Module.ResolveString(token):method.Module.ResolveMember(token); }
                    catch { value=token.ToString("X"); }
                    break;
            }
            Console.WriteLine(start.ToString("X4")+" "+op.Name+" "+value);
        }
    }
}
