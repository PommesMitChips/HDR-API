using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HoloMap;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VRage;
using VRageMath;

internal static class ModClientApiTests
{
    public static int Run()
    {
        int count=0;
        void Check(bool value,string name){if(!value)throw new Exception("Mod client: "+name);count++;}
        void Reject(Action action,string name){try{action();}catch(ArgumentException){count++;return;}throw new Exception("Expected mod client rejection: "+name);}
        var points=new[]{Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitY};var indices=new[]{0,1,2};var uv=new[]{Vector2.Zero,Vector2.UnitX,Vector2.UnitY};
        var mesh=ModClientRules.Mesh("triangle",points,indices,Vector4.One,uv,"test-material");
        points[0]=new Vector3D(999);indices[0]=2;uv[0]=Vector2.One;
        Check(mesh.Geometry.Points[0]==Vector3D.Zero&&mesh.Geometry.Triangles[0]==0&&mesh.UV[0]==Vector2.Zero,"consumer arrays detached");
        Reject(()=>ModClientRules.Mesh("bad",mesh.Geometry.Points,new[]{0,1,9},Vector4.One),"bad indices");
        Reject(()=>ModClientRules.Mesh("bad",mesh.Geometry.Points,new[]{0,1},Vector4.One),"incomplete triangle");
        Reject(()=>ModClientRules.Mesh("bad",mesh.Geometry.Points,new[]{0,1,2},new Vector4(float.NaN,0,0,1)),"NaN colour");
        Reject(()=>ModClientRules.Mesh("bad",mesh.Geometry.Points,new[]{0,1,2},Vector4.One,uv,null),"missing material");
        Reject(()=>ModClientRules.Mesh("bad",mesh.Geometry.Points,new[]{0,1,2},Vector4.One,new[]{new Vector2(float.NaN),Vector2.Zero,Vector2.Zero},"test"),"NaN UV");
        Reject(()=>ModClientRules.Mesh("bad",mesh.Geometry.Points,new[]{0,1,2},Vector4.One,mesh.UV,"hdr_clientPortal_0_N512"),"reserved native resource alias");
        Reject(()=>ModClientRules.Id("\n"),"control identifier");Reject(()=>ModClientRules.Id(new string('x',65)),"long ID");
        Reject(()=>ModClientRules.Transform(MatrixD.CreateScale(1e200)),"overflowing determinant/scale");
        Reject(()=>ModClientRules.Placement(mesh.Geometry,MatrixD.CreateTranslation(double.NaN,0,0),MatrixD.Identity,true),"nonfinite geometry placement");
        Vector3D invalidNormal;Check(!ModClientRules.Normal(new Vector3D(1e200,0,0),new Vector3D(0,1e200,0),Vector3D.UnitZ,Vector3D.Zero,out invalidNormal),"overflowed triangle cannot reach native GPU");
        Reject(()=>ModClientRules.Bound(new Vector4(0,0,0,1)),"empty hit rectangle");
        var context=new ModClientContext();context.Bounds.Add("under",new Vector4(0,0,10,10));context.BoundsOrder.Add("under");context.Bounds.Add("over",new Vector4(2,2,5,5));context.BoundsOrder.Add("over");
        Check(ModClientRules.Hit(context,4,4)=="over","last region wins overlap");Check(ModClientRules.Hit(context,10,10)=="under","rectangle inclusive boundary");Check(ModClientRules.Hit(context,double.NaN,2)==null,"nonfinite hit rejected");
        ModClientRules.Pointer(context,4,4,true);ModClientRules.Pointer(context,4,4,true);
        var events=context.Events.ToArray();Check(events.Length==2&&events[0].Item1=="enter"&&events[1].Item1=="click","click rising edge only");
        ModClientRules.Pointer(context,-1,-1,false);Check(context.Events.ToArray().Last().Item1=="leave","leaving event");
        for(int i=0;i<100;i++){ModClientRules.Pointer(context,4,4,false);ModClientRules.Pointer(context,-1,-1,false);}Check(context.Events.Count==64,"event queue bounded");
        context.Visible=false;Check(ModClientRules.Hit(context,4,4)==null,"invisible context no hit");context.Events.Clear();ModClientRules.Pointer(context,4,4,true);Check(context.Events.Count==0,"hidden context pointer cannot generate events");
        Reject(()=>ModClientRules.Pointer(context,double.PositiveInfinity,0,false),"infinite pointer");
        var owner=new ModClientOwner();var c=new ModClientContext();owner.Contexts.Add(1,c);
        for(int i=0;i<64;i++){var item=ModClientRules.Mesh("i"+i,mesh.Geometry.Points,new[]{0,1,2},Vector4.One);ModClientRules.Admit(owner,c,item);c.Items.Add(item.Id,item);}
        Reject(()=>ModClientRules.Admit(owner,c,ModClientRules.Mesh("overflow",mesh.Geometry.Points,new[]{0,1,2},Vector4.One)),"item cap");
        ModClientRules.Admit(owner,c,ModClientRules.Mesh("i0",mesh.Geometry.Points,new[]{0,1,2},Vector4.One));count++;
        var largeOwner=new ModClientOwner{PointLimit=8192,PrimitiveLimit=8192};var largeContext=new ModClientContext();largeOwner.Contexts.Add(1,largeContext);
        var largePoints=new Vector3D[2048];for(int i=0;i<largePoints.Length;i++)largePoints[i]=new Vector3D(i,0,0);
        for(int i=0;i<4;i++){var item=ModClientRules.Mesh("large"+i,largePoints,new[]{0,1,2},Vector4.One);ModClientRules.Admit(largeOwner,largeContext,item);largeContext.Items.Add(item.Id,item);}
        Reject(()=>ModClientRules.Admit(largeOwner,largeContext,ModClientRules.Mesh("overflow",largePoints,new[]{0,1,2},Vector4.One)),"aggregate consumer point cap");
        // Exact pixel round trips under standard perspective, shear and transformed viewer poses.
        var viewport=new Vector2(2560,1440);var world=MatrixD.CreateFromYawPitchRoll(.7,-.4,.2)*MatrixD.CreateTranslation(100,-40,90);
        var projection=MatrixD.CreatePerspectiveFieldOfView(1.2,viewport.X/viewport.Y,.1,10000);projection.M31=.2;projection.M32=-.1;
        foreach(var pixel in new[]{Vector3D.Zero,new Vector3D(1280,720,0),new Vector3D(2560,1440,0),new Vector3D(31,1123,0)})
        {
            var w=ModClientRules.PixelWorld(pixel,viewport,MatrixD.Invert(projection),world);var v=Vector4D.Transform(new Vector4D(w,1),MatrixD.Invert(world)*projection);
            double x=(v.X/v.W+1)*.5*viewport.X,y=(1-v.Y/v.W)*.5*viewport.Y;
            Check(Math.Abs(pixel.X-x)<1e-6&&Math.Abs(pixel.Y-y)<1e-6,"sheared perspective pixel roundtrip");
        }
        Reject(()=>ModClientRules.PixelWorld(Vector3D.Zero,Vector2.Zero,MatrixD.Identity,MatrixD.Identity),"empty viewport");
        var reversed=projection;reversed.M33=0;reversed.M34=-1;reversed.M43=.1;reversed.M44=0;
        var reversePixel=new Vector3D(400,1000,0);var reverseWorld=ModClientRules.PixelWorld(reversePixel,viewport,MatrixD.Invert(reversed),world);var reverseClip=Vector4D.Transform(new Vector4D(reverseWorld,1),MatrixD.Invert(world)*reversed);
        Check(Math.Abs((reverseClip.X/reverseClip.W+1)*.5*viewport.X-reversePixel.X)<1e-6&&Math.Abs((1-reverseClip.Y/reverseClip.W)*.5*viewport.Y-reversePixel.Y)<1e-6,"infinite reverse Z pixel roundtrip");
        Check(Enum.IsDefined(typeof(VRageRender.MyBillboard.BlendTypeEnum),"PostPP"),"installed engine public HUD blend");
        // Runtime endpoint lifecycle: stale release must not remove a same-ID replacement (ABA).
        var session=new HoloMapSession();var type=typeof(HoloMapSession);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        type.GetField("_modClientActive",flags).SetValue(session,true);
        var service=(Func<string,object[],object>)Delegate.CreateDelegate(typeof(Func<string,object[],object>),session,type.GetMethod("ModClientService",flags));
        var submitted=new List<ModClientTriangleSubmission>();type.GetField("_modClientOfflineSubmit",flags).SetValue(session,new Action<ModClientTriangleSubmission>(item=>submitted.Add(item)));
        type.GetField("_modClientHudDistance",flags).SetValue(session,.01);
        var draw=type.GetMethod("DrawModClientItem",flags);
        var realHud=new ModClientContext{Hud=true};var realItem=ModClientRules.Mesh("actual",new[]{new Vector3D(20,30,0),new Vector3D(300,30,0),new Vector3D(20,200,0)},new[]{0,1,2},new Vector4(1,.5f,.2f,.5f));
        object[] drawArgs={realHud,realItem,viewport,MatrixD.Invert(projection),world,world.Translation,2};draw.Invoke(session,drawArgs);
        Check(submitted.Count==1&&submitted[0].Hud&&(int)drawArgs[6]==1,"actual HUD submission executes and bills shared budget");
        var emitted=submitted[0];Check(ModClientRules.SafePoint(emitted.A-world.Translation,1)&&Geometry.Finite(emitted.Normal.X)&&emitted.Color.W==.5f&&emitted.Color.X==.5f,"actual HUD near-plane finite premultiplied submission");
        realItem.Transform=MatrixD.CreateScale(1e200);drawArgs[6]=2;draw.Invoke(session,drawArgs);Check(submitted.Count==1&&(int)drawArgs[6]==2,"invalid generated placement emits no GPU submission");
        Check((string)service("version",new object[0])=="HDR.ModClient/1","independent protocol");
        var endpoint=(Func<string,object[],object>)service("open",new object[]{"test.consumer"});
        long handle=(long)endpoint("create-hud",new object[]{0});Check(handle>0,"consumer context created");
        endpoint("mesh",new object[]{handle,"a",mesh.Geometry.Points,new[]{0,1,2},Vector4.One});
        var runtimeOwners=(Dictionary<string,ModClientOwner>)type.GetField("_modClientOwners",flags).GetValue(session);var saved=runtimeOwners["test.consumer"].Contexts[handle].Items["a"];
        Reject(()=>endpoint("mesh",new object[]{handle,"a",mesh.Geometry.Points,new[]{0,1,2},Vector4.One,mesh.UV,"HDR_ClientPortal_0_N512"}),"private renderer material through public endpoint");
        Check(ReferenceEquals(saved,runtimeOwners["test.consumer"].Contexts[handle].Items["a"]),"invalid replacement preserves retained content");
        Reject(()=>endpoint("transform",new object[]{handle,"a",MatrixD.CreateTranslation(0,0,1)}),"HUD transform leaves plane");
        endpoint("bounds",new object[]{handle,"button",new Vector4(0,0,10,10)});endpoint("pointer",new object[]{handle,2,2,true});
        var polled=(MyTuple<string,string>[])endpoint("poll-events",new object[]{handle});Check(polled.Length==2,"public event polling");Check(((MyTuple<string,string>[])endpoint("poll-events",new object[]{handle})).Length==0,"event drain");
        Reject(()=>endpoint("clear",new object[]{handle+100}),"foreign handle");
        var replacement=(Func<string,object[],object>)service("open",new object[]{"test.consumer"});
        Check((bool)replacement("valid",new object[0])&&!(bool)endpoint("valid",new object[0]),"stale endpoint validity is nonthrowing");
        Reject(()=>endpoint("release",new object[0]),"stale endpoint release");long fresh=(long)replacement("create-hud",new object[]{1});Check(fresh==1,"replacement still owns factory slot");
        type.GetField("_modClientDrawing",flags).SetValue(session,true);Reject(()=>replacement("clear",new object[]{fresh}),"mutation during Draw");type.GetField("_modClientDrawing",flags).SetValue(session,false);
        replacement("release",new object[0]);Reject(()=>replacement("create-hud",new object[]{0}),"released endpoint revoked");
        Reject(()=>service("draw-budget",new object[]{0}),"zero shared draw budget");Reject(()=>service("draw-budget",new object[]{131073}),"unbounded shared draw budget");service("draw-budget",new object[]{131072});count++;
        var limited=(Func<string,object[],object>)service("open",new object[]{"limited"});for(int i=0;i<16;i++)limited("create-hud",new object[]{i});Reject(()=>limited("create-hud",new object[]{0}),"context cap");
        Reject(()=>limited("draw-limit",new object[]{0}),"zero owner draw limit");Reject(()=>limited("draw-limit",new object[]{8193}),"unbounded owner draw limit");
        for(int i=0;i<15;i++)service("open",new object[]{"owner"+i});Reject(()=>service("open",new object[]{"seventeenth"}),"consumer cap");
        var same=(Func<string,object[],object>)service("open",new object[]{"owner0"});Check((long)same("create-hud",new object[]{0})==1,"replacement permitted at full consumer cap");
        limited("release",new object[0]);
        var live=(Func<string,object[],object>)service("open",new object[]{"after"});type.GetMethod("UnloadModClientApi",flags).Invoke(session,null);Reject(()=>live("create-hud",new object[]{0}),"world unload revokes endpoint");Reject(()=>service("open",new object[]{"later"}),"stopped factory revoked");
        return count;
    }
    public static int Compile(string root,MetadataReference[] references,CSharpParseOptions options)
    {
        var paths=new[]{"Api/Mods/HdrModApi.cs","Examples/Mods/HudMenuModExample.cs","Examples/Mods/HologramEffectsModExample.cs","Examples/Mods/WorldRotorModExample.cs","Examples/Mods/InteractiveControlsModExample.cs"};
        var trees=paths.Select(path=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,path)),options,path)).ToArray();
        var compilation=CSharpCompilation.Create("HdrModConsumer",trees,references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var failures=compilation.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();
        if(failures.Length!=0)throw new Exception("Mod client consumer compilation: "+string.Join(Environment.NewLine,failures.Select(x=>x.ToString())));
        using(var stream=new MemoryStream())
        {
            var emitted=compilation.Emit(stream);if(!emitted.Success)throw new Exception("Mod client consumer emit failed.");
            var assembly=Assembly.Load(stream.ToArray());var type=assembly.GetType("Hdr.Mods.HdrModApi");
            var wrapper=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;type.GetField("ownerId",flags).SetValue(wrapper,"reconnect-test");
            bool current=true;int generation=0;
            Func<string,object[],object> service=(command,args)=>
            {
                if(command=="version")return "HDR.ModClient/1";
                if(command=="open"){int own=++generation;current=true;return new Func<string,object[],object>((op,values)=>{if(op=="valid")return current&&own==generation;if(op=="release")return true;if(op=="bad")throw new ArgumentException("input error");return 1L;});}
                return null;
            };
            void Check(bool value,string name){if(!value)throw new Exception("Consumer wrapper: "+name);}
            var receive=type.GetMethod("Receive",flags);receive.Invoke(wrapper,new object[]{service});
            Check((bool)type.GetProperty("Ready").GetValue(wrapper)&&(long)type.GetProperty("ConnectionGeneration").GetValue(wrapper)==1,"initial connection");
            try{type.GetMethod("Call").Invoke(wrapper,new object[]{"bad",new object[0]});throw new Exception("Expected input error.");}catch(TargetInvocationException error){if(!(error.InnerException is ArgumentException))throw;}
            Check((bool)type.GetProperty("Ready").GetValue(wrapper),"input errors do not invalidate live connection");
            current=false;Check(!(bool)type.GetProperty("Ready").GetValue(wrapper),"revoked endpoint invalidates Ready");
            receive.Invoke(wrapper,new object[]{service});Check((bool)type.GetProperty("Ready").GetValue(wrapper)&&(long)type.GetProperty("ConnectionGeneration").GetValue(wrapper)==2,"same factory reconnects with new generation");
            return 5;
        }
    }
}
