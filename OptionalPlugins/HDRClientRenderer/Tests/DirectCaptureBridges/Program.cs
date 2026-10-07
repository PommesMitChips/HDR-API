using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;

static class Program
{
    const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object observed;
    static MethodInfo rgba;
    static object rgbaManager;
    public static object FileManager;
    static MethodInfo resetUser;
    static int allocated, initialBytes, destroyed;
    static bool failAllocation;
    static bool CaptureCreate(string __0,int __1,int __2,object __3,bool __4,byte[] __5,bool __6,ref object __result)
    {
        if(!__4||!__6||__1!=__2||Convert.ToInt32(__3)!=0)throw new Exception("Expected ready square RGBA mip target.");
        initialBytes=__5==null?0:__5.Length;
        object texture=Fake(Type("VRage.Render11.Resources.Internal.MyUserGeneratedTexture"));
        object size=Activator.CreateInstance(Type("VRageMath.Vector2I"),new object[]{__1,__2});
        rgba.Invoke(rgba.IsStatic?null:rgbaManager,new object[]{texture,__0,size,true,__5,true,true});
        __result=texture;return false;
    }
    static bool CaptureReset(object __instance,object __0)
    {
        var generated=Type("VRage.Render11.Resources.Internal.MyGeneratedTexture");
        object description=generated.GetField("m_desc",Instance).GetValue(__instance);
        int width=(int)description.GetType().GetField("Width").GetValue(description);
        int height=(int)description.GetType().GetField("Height").GetValue(description);
        int levels=(int)description.GetType().GetField("MipLevels").GetValue(description);
        int expected=1;for(int side=Math.Max(width,height);side>1;side>>=1)expected++;
        if(__0!=null)
        {
            long required=0;for(int i=0,w=width,h=height;i<levels;i++,w>>=1,h>>=1)required+=(long)w*h*4;
            throw new Exception("Unsafe CPU mip initialization: "+initialBytes+" bytes supplied; "+required+" bytes required across "+levels+" DataBox entries.");
        }
        if(levels!=expected||width!=height||Convert.ToInt32(description.GetType().GetField("Format").GetValue(description))!=29||
            (Convert.ToInt32(description.GetType().GetField("BindFlags").GetValue(description))&40)!=40)
            throw new Exception("Expected full mip descriptor with sRGB RTV/SRV bindings.");
        if(failAllocation)throw new InvalidOperationException("Injected native target allocation failure.");
        generated.GetField("m_rtv",Instance).SetValue(__instance,Fake(Type("SharpDX.Direct3D11.RenderTargetView")));
        generated.GetField("m_srv",Instance).SetValue(__instance,Fake(Type("SharpDX.Direct3D11.ShaderResourceView")));
        allocated++;Mark("INTERCEPT: actual engine Reset(NULL DataBox[]), "+width+" square, "+levels+" mips, format29, RTV/SRV; Texture2D constructor suppressed.");
        return false;
    }
    static bool CaptureDestroy() { destroyed++;return false; }
    static bool CaptureUserReset(object __instance,byte[] __0)
    { resetUser.Invoke(resetUser.IsStatic?null:rgbaManager,new object[]{__instance,__0});return false; }
    static bool Stop(object __0) { observed=__0; return false; }
    static bool StopScene() { return false; }
    static void ForeignWrapper(Assembly plugin,Type captureType,Harmony terminal)
    {
        var draw=Type("VRageRender.MyRender11").GetMethods(Static).Single(m=>m.Name=="DrawGameScene"&&m.GetParameters().Length==2);
        var foreign=new Harmony("CameraLCD.OfflineForeignPrefix");
        foreign.Patch(draw,prefix:new HarmonyMethod(typeof(CameraLCD.Patches.Patch_MyRender11).GetMethod("MyRender11_DrawGameScene_Prefix",Static)));
        terminal.Patch(draw,prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(StopScene),Static)){priority=Priority.Last});
        var direct=new DynamicMethod("HDRFixtureForeignWrapperCall",typeof(void),System.Type.EmptyTypes,typeof(Program).Module,true);
        var il=direct.GetILGenerator();var aux=il.DeclareLocal(draw.GetParameters()[1].ParameterType.GetElementType());
        il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Ldloca,aux);il.Emit(OpCodes.Call,draw);il.Emit(OpCodes.Ret);
        var invoke=(Action)direct.CreateDelegate(typeof(Action));
        object compatibility=null;
        try
        {
            // Build and execute the actual engine Harmony wrapper before adding the
            // guard. AggressiveInlining deliberately stresses a stale-prefix call.
            CameraLCD.Patches.Patch_MyRender11.Calls=0;
            CameraLCD.CameraLcdManager.DrawCalls=0;
            for(int i=0;i<32;i++)invoke();
            if(CameraLCD.Patches.Patch_MyRender11.Calls!=32||CameraLCD.CameraLcdManager.DrawCalls!=32)
                throw new Exception("Foreign prefix -> manager work fixture did not execute before guard.");
            var work=typeof(CameraLCD.CameraLcdManager).GetMethod(nameof(CameraLCD.CameraLcdManager.Draw));
            if(work.GetMethodBody().ExceptionHandlingClauses.Count<2||work.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.NoInlining))
                throw new Exception("Foreign work fixture must retain native-shaped enumeration/finally regions without a manufactured NoInlining attribute.");
            Mark("PREWARM: actual DrawGameScene wrapper JIT-called 32 times with aggressive-inline CameraLCD prefix -> manager work boundary; GPU body suppressed.");
            var compatibilityType=plugin.GetType("HDRClientRenderer.CameraLcdCompatibility",true);
            compatibility=Activator.CreateInstance(compatibilityType,true);
            compatibilityType.GetMethod("TryInstallForeignGuard",Instance).Invoke(compatibility,new object[]{(Action<string>)Mark});
            int before=CameraLCD.CameraLcdManager.DrawCalls;
            captureType.GetField("capturing",Static).SetValue(null,1);invoke();
            if(CameraLCD.CameraLcdManager.DrawCalls!=before)throw new Exception("Already-JIT scene wrapper still ran foreign CameraLCD manager work during HDR capture.");
            captureType.GetField("capturing",Static).SetValue(null,0);invoke();
            if(CameraLCD.CameraLcdManager.DrawCalls!=before+1)throw new Exception("Foreign work guard changed normal main-view capture.");
            ((IDisposable)compatibility).Dispose();compatibility=null;
            captureType.GetField("capturing",Static).SetValue(null,1);invoke();
            if(CameraLCD.CameraLcdManager.DrawCalls!=before+2)throw new Exception("Foreign manager work was not restored after guard disposal.");
            Mark("PASS: pre-existing JIT-called actual DrawGameScene wrapper suppresses CameraLCD capture work even when its prefix is inlined, preserves main view and restores on disposal; no GPU frame.");
        }
        finally { if(compatibility!=null)((IDisposable)compatibility).Dispose();captureType.GetField("capturing",Static).SetValue(null,0);foreign.UnpatchAll(foreign.Id); }
    }
    static Type Type(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).First(t=>t!=null);
    static object Fake(Type type)=>RuntimeHelpers.GetUninitializedObject(type);
    static void Mark(string text) { Console.WriteLine(text); Console.Out.Flush(); }
    static void CallSetter(MethodInfo method,object receiver,object argument)
    {
        var call=new DynamicMethod("HDRFixtureExactSetterCall",typeof(void),new[]{method.DeclaringType,method.GetParameters()[0].ParameterType},typeof(Program).Module,true);
        var il=call.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);
        foreach(var parameter in method.GetParameters().Skip(1))
        {
            if(parameter.ParameterType.IsValueType){var zero=il.DeclareLocal(parameter.ParameterType);il.Emit(OpCodes.Ldloc,zero);}
            else il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Callvirt,method);il.Emit(OpCodes.Ret);
        call.CreateDelegate(typeof(Action<,>).MakeGenericType(method.DeclaringType,method.GetParameters()[0].ParameterType)).DynamicInvoke(receiver,argument);
    }
    static int Main(string[] args)
    {
        string bin=@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        AssemblyLoadContext.Default.Resolving+=(context,name)=>{string path=Path.Combine(bin,name.Name+".dll");return File.Exists(path)?context.LoadFromAssemblyPath(path):null;};
        foreach(string dll in new[]{"VRage","VRage.Library","VRage.Math","VRage.Render","SharpDX","SharpDX.Direct3D11","SharpDX.D3DCompiler","VRage.Render11","Sandbox.Game"})
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin,dll+".dll"));
        var plugin=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
        var captureType=plugin.GetType("HDRClientRenderer.DirectCameraCapture",true);
        object capture=Activator.CreateInstance(captureType,true);
        var terminal=new Harmony("HDR.Tests.DirectCaptureBridge.Terminal");
        try
        {
            string mode=args.Length>1?args[1]:"shadow";
            if(mode=="state"){InitializedStateFixture.Run(plugin);return 0;}
            if(mode=="culling"){InitializedStateFixture.Run(plugin,()=>CaptureCullingFixture.Run(plugin));return 0;}
            if(mode=="particle"){InitializedStateFixture.Run(plugin,()=>ParticleBindingFixture.Run(plugin));return 0;}
            if(mode=="models"){InitializedStateFixture.Run(plugin,()=>CaptureModelFixture.Run(plugin));return 0;}
            if(mode=="occlusion"){InitializedStateFixture.Run(plugin,()=>CaptureOcclusionFixture.Run(plugin));return 0;}
            if(mode=="tiles"){InitializedStateFixture.Run(plugin,()=>CaptureTileFixture.Run(plugin));return 0;}
            if(mode=="portal-projection"){InitializedStateFixture.Run(plugin,()=>PortalProjectionFixture.Run(plugin));return 0;}
            if(mode=="portal-primary"){InitializedStateFixture.Run(plugin,()=>PortalPrimaryBoundaryFixture.Run(plugin));return 0;}
            if(mode=="foreign"){ForeignWrapper(plugin,captureType,terminal);return 0;}
            captureType.GetMethod("TryInstall",Instance).Invoke(capture,new object[]{(Action<string>)Mark});
            if(!(bool)captureType.GetProperty("Ready",Instance).GetValue(capture))throw new Exception("Capture did not install.");
            object native=captureType.GetField("native",Instance).GetValue(capture);
            var nativeType=native.GetType();
            captureType.GetField("capturing",Static).SetValue(null,1);
            if(mode=="shadow")
            {
                var method=(MethodInfo)nativeType.GetField("ShadowQueries",Instance).GetValue(native);
                object receiver=Fake(method.DeclaringType);
                Mark("ENTER: installed struct-result shadow-query detour; original body suppressed by actual HDR prefix.");
                object result=method.Invoke(receiver,new object[]{null});
                var count=method.ReturnType.GetProperty("Count",Instance);
                if(count!=null&&(int)count.GetValue(result)!=0)throw new Exception("Shadow guard did not return empty queries.");
                // Exercise an exact compiled call instruction as well as reflection.
                var call=new DynamicMethod("HDRFixtureShadowCall",method.ReturnType,new[]{method.DeclaringType},typeof(Program).Module,true);
                var il=call.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Callvirt,method);il.Emit(OpCodes.Ret);
                var signature=typeof(Func<,>).MakeGenericType(method.DeclaringType,method.ReturnType);
                object direct=call.CreateDelegate(signature).DynamicInvoke(receiver);
                if(direct==null)throw new Exception("Shadow detour failed through exact call.");
                Mark("PASS: actual exact-signature shadow-query prefix executes through reflection and exact typed call; no GPU body.");
            }
            else if(mode=="blend")
            {
                var method=(MethodInfo)nativeType.GetField("SetBlend",Instance).GetValue(native);
                var state=(FieldInfo)nativeType.GetField("OpaqueCopyBlend",Instance).GetValue(native);
                object previous=state.GetValue(null), replacement=Fake(Type("VRage.Render11.Resources.Internal.MyBlendState"));
                try
                {
                    state.SetValue(null,replacement);captureType.GetField("copyingCapture",Static).SetValue(null,true);
                    terminal.Patch(method,prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(Stop),Static)){priority=Priority.Last});
                    Mark("ENTER: installed ref-interface blend detour; terminal prefix prevents original renderer/GPU body.");
                    method.Invoke(Fake(method.DeclaringType),new object[]{null,null});
                    if(!ReferenceEquals(observed,replacement))throw new Exception("Blend detour did not write back exact interface argument.");
                    observed=null;CallSetter(method,Fake(method.DeclaringType),null);
                    if(!ReferenceEquals(observed,replacement))throw new Exception("Blend detour failed through exact typed call.");
                    Mark("PASS: actual nongeneric opaque-copy ref-interface detour writes back through reflection and exact typed call; no GPU body.");
                }
                finally{state.SetValue(null,previous);captureType.GetField("copyingCapture",Static).SetValue(null,false);}
            }
            else if(mode=="vertex")
            {
                object particles=nativeType.GetField("Particles",Instance).GetValue(native);
                var particlesType=particles.GetType();
                var method=(MethodInfo)particlesType.GetField("VertexSet",Instance).GetValue(particles);
                object source=Fake(method.GetParameters()[0].ParameterType), replacement=Fake(method.GetParameters()[0].ParameterType);
                particlesType.GetField("ordinaryOriginal",Instance).SetValue(particles,source);
                particlesType.GetField("ordinaryShader",Instance).SetValue(particles,replacement);
                particlesType.GetField("drawing",Static).SetValue(null,particles);
                terminal.Patch(method,prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(Stop),Static)){priority=Priority.Last});
                Mark("ENTER: installed ref-class vertex detour; terminal prefix prevents original renderer/GPU body.");
                method.Invoke(Fake(method.DeclaringType),new[]{source});
                if(!ReferenceEquals(observed,replacement))throw new Exception("Vertex detour did not write back exact shader argument.");
                observed=null;CallSetter(method,Fake(method.DeclaringType),source);
                if(!ReferenceEquals(observed,replacement))throw new Exception("Vertex detour failed through exact typed call.");
                particlesType.GetField("drawing",Static).SetValue(null,null);
                particlesType.GetField("ordinaryOriginal",Instance).SetValue(particles,null);
                particlesType.GetField("ordinaryShader",Instance).SetValue(particles,null);
                Mark("PASS: actual nongeneric particle ref-class detour writes back through reflection and exact typed call; no shader allocation or GPU body.");
            }
            else if(mode=="scene")
            {
                var method=(MethodInfo)nativeType.GetField("DrawScene",Instance).GetValue(native);
                terminal.Patch(method,prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(StopScene),Static)){priority=Priority.Last});
                Mark("ENTER: recursive patched scene invocation under HDR capture guard; terminal prefix prevents original scene/GPU body.");
                method.Invoke(null,new object[]{null,null});
                Mark("PASS: nested DrawGameScene detour completes with capture guard; no GPU body.");
            }
            else if(mode=="allocation")
            {
                var generatedManager=Type("VRage.Render11.Resources.MyGeneratedTextureManager");
                rgba=generatedManager.GetMethods(Static|Instance).Single(m=>m.Name=="CreateRGBA"&&m.GetParameters().Length==7&&m.GetParameters()[4].ParameterType==typeof(byte[]));
                var nativeFileField=nativeType.GetField("fileTextures",Instance);
                var originalFileField=(FieldInfo)nativeFileField.GetValue(native);
                var create=(MethodInfo)nativeType.GetField("createTarget",Instance).GetValue(native);
                var destroy=(MethodInfo)nativeType.GetField("destroyTarget",Instance).GetValue(native);
                var reset=Type("VRage.Render11.Resources.Internal.MyGeneratedTexture").GetMethods(Instance).Single(m=>m.Name=="Reset"&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType.IsArray);
                resetUser=generatedManager.GetMethod("ResetUserTexture",Static|Instance);
                var userReset=Type("VRage.Render11.Resources.Internal.MyUserGeneratedTexture").GetMethod("Reset",Instance,null,new[]{typeof(byte[])},null);
                try
                {
                    rgbaManager=Fake(generatedManager);FileManager=Fake(create.DeclaringType);
                    nativeFileField.SetValue(native,typeof(Program).GetField(nameof(FileManager),Static));
                    terminal.Patch(create,prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(CaptureCreate),Static)));
                    terminal.Patch(destroy,prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(CaptureDestroy),Static)));
                    terminal.Patch(reset,prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(CaptureReset),Static)));
                    terminal.Patch(userReset,prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(CaptureUserReset),Static)));
                    Mark("ENTER: actual native CreateTarget -> engine CreateRGBA/ResetUserTexture/Reset descriptor chain; final GPU reset intercepted.");
                    foreach(int size in new[]{64,256,512,1024,2048})
                    {
                        object texture=nativeType.GetMethod("CreateTarget",Instance).Invoke(native,new object[]{"HDR_DirectCamera_0",size});
                        if(texture==null||initialBytes!=0)throw new Exception("Native capture target supplied CPU pixels.");
                    }
                    if(allocated!=5||destroyed!=0)throw new Exception("Expected one null-data native reset per target.");
                    failAllocation=true;
                    try { nativeType.GetMethod("CreateTarget",Instance).Invoke(native,new object[]{"HDR_DirectCamera_0",256});throw new Exception("Injected reset failure did not escape."); }
                    catch(TargetInvocationException ex) { if(!ex.GetBaseException().Message.Contains("Injected native target allocation failure"))throw; }
                    finally { failAllocation=false; }
                    if(destroyed!=1)throw new Exception("Failed registered target was not destroyed exactly once.");
                    Mark("PASS: native camera allocation uses complete-mip sRGB descriptors and null initial subresource data at every supported/capped size; no GPU constructor/frame.");
                    Mark("PASS: an injected native reset failure retires the manager-registered target before retry; no GPU constructor/frame.");
                }
                finally { nativeFileField.SetValue(native,originalFileField);FileManager=null; }
            }
            else throw new Exception("Unknown fixture mode.");
            return 0;
        }
        catch(Exception ex){Mark(ex.GetBaseException().ToString());return 1;}
        finally
        {
            captureType.GetField("capturing",Static).SetValue(null,0);
            terminal.UnpatchAll(terminal.Id);
            captureType.GetMethod("Unpatch",Instance).Invoke(capture,null);
        }
    }
}
namespace CameraLCD.Patches
{
    public static class Patch_MyRender11
    {
        public static int Calls;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MyRender11_DrawGameScene_Prefix() { Calls++;CameraLcdManager.Draw(); }
    }
}
namespace CameraLCD
{
    public static class CameraLcdManager
    {
        public static int DrawCalls;
        static readonly List<int> sources=new List<int>{1,2,3};
        static readonly List<int> retired=new List<int>{4,5};
        static int count,finished;
        // Like the installed 254-byte Draw implementation, this work boundary has
        // two enumeration loops with finally disposal. It has no NoInlining tag.
        public static bool Draw()
        {
            DrawCalls++;
            int visible=0;
            try
            {
                foreach(int source in sources)
                {
                    if(source<=0)continue;
                    count+=source;
                    visible++;
                    if(count==int.MinValue)throw new InvalidOperationException("Unreachable fixture state.");
                }
            }
            finally { finished++; }
            try
            {
                foreach(int source in retired)
                {
                    if(source>0)count-=source;
                    else count+=source;
                    if(count==int.MaxValue)throw new InvalidOperationException("Unreachable fixture state.");
                }
            }
            finally { finished++; }
            return visible>0&&finished>0;
        }
    }
}
