using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using HarmonyLib;
using HDRClientRenderer;
using VRageRender;

// An offline marker stands in for the installed producer; no camera or game runs here.
namespace CameraLCD
{
    public static class CameraViewRenderer { public static bool IsDrawing { get; set; } }
}
namespace HDRClientRenderer
{
    // This offline marker exercises the isolation branch without starting native
    // capture. The real service is compiled in the independently built plugin.
    internal static class DirectCameraCapture { internal static bool IsCapturing { get; set; } }
}
internal static class Program
{
    static List<MyBillboard> testBillboards;
    static int billboardGetterCalls;
    static bool SupplyBillboards(ref List<MyBillboard> __result){billboardGetterCalls++;__result=testBillboards;return false;}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static bool ReadBillboardPair()
    {
        var counted=MyRenderProxy.BillboardsRead;
        CameraLCD.CameraViewRenderer.IsDrawing=false;
        var filled=MyRenderProxy.BillboardsRead;
        return ReferenceEquals(counted,filled)&&counted.Count==1;
    }
    static void StableBillboardPair()
    {
        var flags=BindingFlags.NonPublic|BindingFlags.Static;
        var getter=typeof(MyRenderProxy).GetProperty("BillboardsRead").GetGetMethod();
        var consumer=typeof(Program).GetMethod("ReadBillboardPair",flags);
        var snapshot=typeof(CameraLcdCompatibility).GetMethod("CaptureTranspiler",flags);
        var harmony=new Harmony("HDRClientRenderer.Tests.StableBillboardPair");
        testBillboards=new List<MyBillboard>{new MyBillboard{Material=VRage.Utils.MyStringId.GetOrCompute("Smoke")},
            new MyBillboard{Material=VRage.Utils.MyStringId.GetOrCompute("HDR_ClientLcd_0")}};
        billboardGetterCalls=0;
        try
        {
            harmony.Patch(getter,prefix:new HarmonyMethod(typeof(Program).GetMethod("SupplyBillboards",flags)));
            harmony.Patch(consumer,transpiler:new HarmonyMethod(snapshot));
            CameraLCD.CameraViewRenderer.IsDrawing=true;
            if(!(bool)consumer.Invoke(null,null)||billboardGetterCalls!=1)
                throw new Exception("Bucket count/fill must share one filtered snapshot even when capture state changes between reads.");
            CameraLCD.CameraViewRenderer.IsDrawing=false;DirectCameraCapture.IsCapturing=true;billboardGetterCalls=0;
            if(!(bool)consumer.Invoke(null,null)||billboardGetterCalls!=1)
                throw new Exception("Direct capture must use the same isolated snapshot without CameraLCD drawing.");
        }
        finally{CameraLCD.CameraViewRenderer.IsDrawing=false;DirectCameraCapture.IsCapturing=false;harmony.Unpatch(consumer,HarmonyPatchType.All,harmony.Id);harmony.Unpatch(getter,HarmonyPatchType.All,harmony.Id);}
        Console.WriteLine("PASS: transpiled count/fill reads share one snapshot across capture-state changes; no engine list or GPU work.");
    }
    static object quadContext;
    static MyViewport? quadViewport;
    static bool quadVertices;
    static int quadCalls;
    static bool CaptureQuad(object __0,MyViewport? __1,bool __2)
    {quadContext=__0;quadViewport=__1;quadVertices=__2;quadCalls++;return false;}
    static void CopyVertexBinding(Assembly rendererAssembly)
    {
        var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        var screenPass=rendererAssembly.GetType("VRageRender.MyScreenPass",true);
        var draw=screenPass.GetMethods(flags).Single(method=>method.Name=="DrawFullscreenQuad"&&method.GetParameters().Length==3);
        var harmony=new Harmony("HDRClientRenderer.Tests.CopyVertexBinding");
        try
        {
            harmony.Patch(draw,prefix:new HarmonyMethod(typeof(Program).GetMethod("CaptureQuad",flags)));
            var pass=new NativeTextureCopy.GpuPass(rendererAssembly);
            // All renderer work is intercepted: this managed marker has no GPU state.
            var context=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(rendererAssembly.GetType("VRage.Render11.RenderContext.MyRenderContext",true));
            quadCalls=0;quadVertices=false;quadViewport=null;quadContext=null;
            pass.DrawCopyQuad(context,384,192);
            if(quadCalls!=1||!ReferenceEquals(quadContext,context)||!quadVertices||!quadViewport.HasValue||
                quadViewport.Value.Width!=384||quadViewport.Value.Height!=192||
                quadViewport.Value.OffsetX!=0||quadViewport.Value.OffsetY!=0)
                throw new Exception("Camera copies must bind fullscreen vertices after ClearState and use the destination viewport.");
        }
        finally{harmony.Unpatch(draw,HarmonyPatchType.All,harmony.Id);}
        Console.WriteLine("PASS: actual camera copy draw requests vertex rebinding and exact destination viewport; engine draw intercepted, no GPU work.");
    }
    static bool writableTarget,readyTarget;
    static byte[] initialPixels;
    static readonly List<string> creationCalls=new List<string>();
    static bool resetNull;
    static bool CaptureTextureCreation(byte[] __5,bool __6,bool __7)
    { creationCalls.Add("create");initialPixels=__5;writableTarget=__6;readyTarget=__7;return false; }
    static bool CaptureTextureReset(string __0,byte[] __1){creationCalls.Add("reset:"+__0);resetNull=__1==null;return false;}
    static void WritableLcdTarget()
    {
        var create=typeof(MyRenderProxy).GetMethod("CreateGeneratedTexture",BindingFlags.Public|BindingFlags.Static);
        var reset=typeof(MyRenderProxy).GetMethod("ResetGeneratedTexture",BindingFlags.Public|BindingFlags.Static);
        var harmony=new Harmony("HDRClientRenderer.Tests.WritableTarget");
        try
        {
            harmony.Patch(create,prefix:new HarmonyMethod(typeof(Program).GetMethod("CaptureTextureCreation",BindingFlags.NonPublic|BindingFlags.Static)));
            harmony.Patch(reset,prefix:new HarmonyMethod(typeof(Program).GetMethod("CaptureTextureReset",BindingFlags.NonPublic|BindingFlags.Static)));
            var pluginAssembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"HDRClientRenderer.dll"));
            var pluginType=pluginAssembly.GetType("HDRClientRenderer.HDRClientRendererPlugin",true);
            var worldType=pluginType.GetNestedType("LcdWorld",BindingFlags.NonPublic);
            var plugin=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(pluginType);
            var world=Activator.CreateInstance(worldType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{plugin},null);
            creationCalls.Clear();resetNull=false;
            worldType.GetMethod("CreateTarget").Invoke(world,new object[]{"HDR_ClientLcd_0",256,256});
            if(!writableTarget)throw new Exception("LCD copy target is sample-only; VRAGE will not create its render-target view.");
            if(!readyTarget||initialPixels!=null||creationCalls.Count!=1||creationCalls[0]!="create")
                throw new Exception("LCD mip target must create only a null-data descriptor; render-thread activation owns the reset.");
        }
        finally{harmony.Unpatch(create,HarmonyPatchType.All,harmony.Id);harmony.Unpatch(reset,HarmonyPatchType.All,harmony.Id);}
        Console.WriteLine("PASS: actual client LCD creation queues descriptor-only null-data Create; no unsafe base-only CPU data or eager reset.");
    }
    static void PanoramaShaderAbi(Assembly assembly)
    {
        var basis=new PanoramaStore.Face{Camera=1,Generation=1,Texture="HDR_DirectCamera_Test",Width=1024,Height=1024,
            Profile=0,ContentRevision=1,RightX=1,RightY=0,RightZ=0,UpX=0,UpY=1,UpZ=0,ForwardX=0,ForwardY=0,ForwardZ=1,
            TanHorizontal=1,TanVertical=1,Evidence=new object(),AnchorInverse=new[]{1d,0,0,0,1,0,0,0,1}};
        if(PanoramaShader.Constants(new[]{basis},new PanoramaStore.Settings()).Length*4!=18832)throw new Exception("Panorama b1 atlas upload footprint changed.");
        var pass=new NativePanoramaGpu.GpuPass(assembly);
        if(pass.ShaderBytes<=0||pass.ShaderBytes>256*1024)throw new Exception("Fixed panorama ps_5_0 failed actual native HLSL compilation.");
        var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        var draw=assembly.GetType("VRageRender.MyScreenPass",true).GetMethods(flags).Single(method=>method.Name=="DrawFullscreenQuad"&&method.GetParameters().Length==3);
        var harmony=new Harmony("HDRClientRenderer.Tests.PanoramaShaderAbi");
        try
        {
            harmony.Patch(draw,prefix:new HarmonyMethod(typeof(Program).GetMethod("CaptureQuad",flags)));
            var context=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(assembly.GetType("VRage.Render11.RenderContext.MyRenderContext",true));
            quadCalls=0;quadVertices=false;quadViewport=null;
            pass.DrawQuad(context,2048,1024);
            if(quadCalls!=1||!quadVertices||!quadViewport.HasValue||quadViewport.Value.Width!=2048||quadViewport.Value.Height!=1024)
                throw new Exception("Panorama fullscreen draw did not bind vertices and exact bounded target viewport.");
            var mainMarker=new object();
            if(pass.IsMainTarget(null)||NativePanoramaGpu.GpuPass.SameMainTarget(new object(),mainMarker)||
                !NativePanoramaGpu.GpuPass.SameMainTarget(mainMarker,mainMarker))
                throw new Exception("Panorama main-view gate must require exact nonnull backbuffer identity.");
        }
        finally{harmony.Unpatch(draw,HarmonyPatchType.All,harmony.Id);pass.Dispose();}
        var messages=new List<string>();
        using(var compositor=new NativePanoramaGpu())
        {
            compositor.SetResolver(face=>face);
            compositor.TryInstall(messages.Add);
            if(!compositor.Ready||!messages.Exists(message=>message.Contains("compositor installed")))
                throw new Exception("Actual panorama renderer ABI/hook failed: "+string.Join("; ",messages));
        }
        Console.WriteLine("PASS: fixed panorama PS compiles with native D3DCompiler; b1/CB/SRV/sampler/RGBA/mip/main-frame ABI binds and hook installs/removes; draw intercepted, no GPU frame.");
    }
    static void TransparentPanoramaTarget()
    {
        var create=typeof(MyRenderProxy).GetMethod("CreateGeneratedTexture",BindingFlags.Public|BindingFlags.Static);
        var reset=typeof(MyRenderProxy).GetMethod("ResetGeneratedTexture",BindingFlags.Public|BindingFlags.Static);
        var harmony=new Harmony("HDRClientRenderer.Tests.TransparentPanoramaTarget");
        try
        {
            harmony.Patch(create,prefix:new HarmonyMethod(typeof(Program).GetMethod("CaptureTextureCreation",BindingFlags.NonPublic|BindingFlags.Static)));
            harmony.Patch(reset,prefix:new HarmonyMethod(typeof(Program).GetMethod("CaptureTextureReset",BindingFlags.NonPublic|BindingFlags.Static)));
            var pluginAssembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"HDRClientRenderer.dll"));
            var pluginType=pluginAssembly.GetType("HDRClientRenderer.HDRClientRendererPlugin",true);
            var worldType=pluginType.GetNestedType("PanoramaWorld",BindingFlags.NonPublic);
            var plugin=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(pluginType);
            var world=Activator.CreateInstance(worldType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{plugin},null);
            initialPixels=null;writableTarget=readyTarget=false;resetNull=false;creationCalls.Clear();
            worldType.GetMethod("CreateTarget").Invoke(world,new object[]{"HDR_ClientPanorama_0",512,256});
            if(!writableTarget||!readyTarget||initialPixels!=null||creationCalls.Count!=1||creationCalls[0]!="create")
                throw new Exception("Panorama mip targets must create only a null descriptor; activation must occur after descriptor validation.");
        }
        finally{harmony.Unpatch(create,HarmonyPatchType.All,harmony.Id);harmony.Unpatch(reset,HarmonyPatchType.All,harmony.Id);}
        Console.WriteLine("PASS: actual panorama creation queues descriptor-only null-data Create; intercepted, no CPU mip buffer or GPU allocation.");
    }
    static readonly List<string> mipCalls=new List<string>();
    static object mipTarget;
    static bool CaptureClear(){mipCalls.Add("clear");return false;}
    static bool CaptureMips(object __0){mipCalls.Add("mips");mipTarget=__0;return false;}
    static void MipRegenerationAbi(Assembly assembly)
    {
        var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        var contextType=assembly.GetType("VRage.Render11.RenderContext.MyRenderContext",true);
        var srvType=assembly.GetType("VRage.Render11.Resources.ISrvBindable",true);
        var clear=contextType.GetMethod("ClearState",flags,null,Type.EmptyTypes,null);
        var generate=contextType.GetMethod("GenerateMips",flags,null,new[]{srvType},null);
        var harmony=new Harmony("HDRClientRenderer.Tests.MipRegenerationAbi");
        try
        {
            harmony.Patch(clear,prefix:new HarmonyMethod(typeof(Program).GetMethod("CaptureClear",BindingFlags.Static|BindingFlags.NonPublic)));
            harmony.Patch(generate,prefix:new HarmonyMethod(typeof(Program).GetMethod("CaptureMips",BindingFlags.Static|BindingFlags.NonPublic)));
            var context=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(contextType);
            var target=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(assembly.GetType("VRage.Render11.Resources.Internal.MyUserGeneratedTexture",true));
            var lcd=new NativeTextureCopy.GpuPass(assembly);
            using(var panorama=new NativePanoramaGpu.GpuPass(assembly))
            {
                foreach(Action<object,object> refresh in new Action<object,object>[] {lcd.RegenerateTargetMips,panorama.RegenerateTargetMips})
                {
                    mipCalls.Clear();mipTarget=null;refresh(context,target);
                    if(mipCalls.Count!=2||mipCalls[0]!="clear"||mipCalls[1]!="mips"||!ReferenceEquals(mipTarget,target))
                        throw new Exception("Copied/composited target mips must regenerate once after RTV unbinding.");
                }
            }
        }
        finally{harmony.Unpatch(clear,HarmonyPatchType.All,harmony.Id);harmony.Unpatch(generate,HarmonyPatchType.All,harmony.Id);}
        Console.WriteLine("PASS: LCD and panorama mip refresh unbind targets first and invoke the actual GenerateMips ABI once; both engine calls intercepted, no GPU work.");
    }
    static void DirectCameraCaptureAbi()
    {
        var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"HDRClientRenderer.dll"));
        var captureType=assembly.GetType("HDRClientRenderer.DirectCameraCapture",true);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var capture=Activator.CreateInstance(captureType,true);
        var messages=new List<string>();
        try
        {
            captureType.GetMethod("TryInstall",flags).Invoke(capture,new object[]{(Action<string>)messages.Add});
            if(!(bool)captureType.GetProperty("Ready",flags).GetValue(capture))
                throw new Exception("Native direct camera capture ABI did not bind: "+string.Join("; ",messages));
            captureType.GetMethod("NewEpoch",flags).Invoke(capture,null);
        }
        finally{((IDisposable)capture).Dispose();}
        Console.WriteLine("PASS: actual native direct camera capture ABI and scoped hooks install/remove from the built client; no camera demand, GPU target or frame.");
    }
    static void PanoramaUploadAbi(Assembly assembly)
    {
        var type=assembly.GetType("VRageRender.MyMapping",true);
        var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var pointer=type.GetField("m_dataPointer",flags);
        var write=type.GetMethods(flags).Single(method=>method.Name=="WriteAndPosition"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==3).MakeGenericMethod(typeof(float));
        var values=Enumerable.Range(0,PanoramaShader.ConstantFloats).Select(index=>index*.125f).ToArray();
        const int guard=32;int length=PanoramaShader.ConstantBytes+guard*2;
        IntPtr memory=Marshal.AllocHGlobal(length);
        try
        {
            var sentinels=Enumerable.Repeat((byte)0xA5,length).ToArray();Marshal.Copy(sentinels,0,memory,length);
            object mapping=Activator.CreateInstance(type);IntPtr start=IntPtr.Add(memory,guard);pointer.SetValue(mapping,start);
            write.Invoke(mapping,new object[]{values,values.Length,0});
            var observed=new float[values.Length];Marshal.Copy(start,observed,0,observed.Length);
            var bytes=new byte[length];Marshal.Copy(memory,bytes,0,length);
            if(!observed.SequenceEqual(values)||!bytes.Take(guard).All(value=>value==0xA5)||!bytes.Skip(guard+PanoramaShader.ConstantBytes).All(value=>value==0xA5)||
                (IntPtr)pointer.GetValue(mapping)!=IntPtr.Add(start,PanoramaShader.ConstantBytes))
                throw new Exception("Installed MyMapping float-array upload footprint differs from fixed atlas constants.");
            if(PanoramaShader.ValidConstants(new float[PanoramaShader.ConstantFloats-1])||PanoramaShader.ValidConstants(new float[PanoramaShader.ConstantFloats+1]))throw new Exception("Unsafe panorama upload length accepted.");
            var invalid=new float[PanoramaShader.ConstantFloats];invalid[4]=float.NaN;
            if(PanoramaShader.ValidConstants(invalid))throw new Exception("Nonfinite panorama upload accepted.");
        }
        finally{Marshal.FreeHGlobal(memory);}
        Console.WriteLine("PASS: installed MyMapping.WriteAndPosition<float> writes exactly "+PanoramaShader.ConstantBytes+" atlas bytes with intact CPU canaries; no GPU mapping or frame.");
    }
    static int resetActivations;
    static bool CaptureUserReset(byte[] __0){if(__0!=null)throw new Exception("Generated target supplied CPU subresources.");resetActivations++;return false;}
    static void GeneratedTargetDescriptorAbi(Assembly assembly)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var generated=assembly.GetType("VRage.Render11.Resources.Internal.MyGeneratedTexture",true);
        var user=assembly.GetType("VRage.Render11.Resources.Internal.MyUserGeneratedTexture",true);
        var field=generated.GetField("m_desc",flags);var descType=field.FieldType;
        object descriptor=Activator.CreateInstance(descType);
        void Set(string name,int value){var target=descType.GetField(name);target.SetValue(descriptor,target.FieldType.IsEnum?Enum.ToObject(target.FieldType,value):(object)value);}
        Set("Width",724);Set("Height",362);Set("ArraySize",1);Set("Format",29);Set("Usage",0);Set("BindFlags",40);Set("OptionFlags",1);Set("MipLevels",11);
        var sampleField=descType.GetField("SampleDescription");object sample=Activator.CreateInstance(sampleField.FieldType);
        sampleField.FieldType.GetField("Count").SetValue(sample,1);sampleField.FieldType.GetField("Quality").SetValue(sample,0);sampleField.SetValue(descriptor,sample);
        object texture=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(user);field.SetValue(texture,descriptor);
        var reset=user.GetMethod("Reset",flags,null,new[]{typeof(byte[])},null);var harmony=new Harmony("HDRClientRenderer.Tests.GeneratedTargetDescriptorAbi");
        try
        {
            harmony.Patch(reset,prefix:new HarmonyMethod(typeof(Program).GetMethod("CaptureUserReset",BindingFlags.Static|BindingFlags.NonPublic)));
            var activate=new PanoramaGeneratedTarget(assembly);resetActivations=0;
            if(activate.Matches(texture,720,362)||activate.Activate(texture,720,362)||resetActivations!=0)throw new Exception("Stale same-name target size accepted.");
            if(!activate.Activate(texture,724,362)||resetActivations!=1||(int)descType.GetField("MipLevels").GetValue(field.GetValue(texture))!=10)
                throw new Exception("NPOT descriptor not corrected before null reset.");
            descriptor=field.GetValue(texture);Set("Format",28);field.SetValue(texture,descriptor);
            if(activate.Activate(texture,724,362)||resetActivations!=1)throw new Exception("Non-sRGB target accepted.");
        }
        finally{harmony.Unpatch(reset,HarmonyPatchType.All,harmony.Id);}
        Console.WriteLine("PASS: shared target adapter rejects stale size/format, clamps NPOT mip count and invokes only Reset(null); real engine reset intercepted, no GPU work.");
    }
    static object rasterManager,rasterTexture;
    static bool rasterExists;
    static int rasterCreates,rasterResets;
    static bool RasterLookup(ref object __1,ref bool __result){__result=rasterExists;__1=rasterExists?rasterTexture:null;return false;}
    static bool RasterCreate(int __1,int __2,object __3,bool __4,byte[] __5,bool __6,ref object __result)
    {
        if(__1!=64||__2!=32||Convert.ToInt32(__3)!=0||__4||!__6||__5==null||__5.Length!=64*32*4)throw new Exception("UI allocation must use exactly one RGBA base level.");
        rasterCreates++;__result=rasterTexture;return false;
    }
    static bool RasterReset(byte[] __1){if(__1==null||__1.Length!=64*32*4)throw new Exception("UI reset base data changed.");rasterResets++;return false;}
    static void RasterUploadAbi(Assembly assembly)
    {
        var pass=new NativeRasterUpload.GpuPass(assembly);var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var generated=assembly.GetType("VRage.Render11.Resources.Internal.MyGeneratedTexture",true);
        rasterTexture=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(assembly.GetType("VRage.Render11.Resources.Internal.MyUserGeneratedTexture",true));
        var generatedManager=assembly.GetType("VRage.Render11.Resources.MyGeneratedTextureManager",true);
        var rgba=generatedManager.GetMethods(flags|BindingFlags.Static).Single(method=>method.Name=="CreateRGBA"&&method.GetParameters().Length==7&&method.GetParameters()[4].ParameterType==typeof(byte[]));
        rgba.Invoke(rgba.IsStatic?null:System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(generatedManager),new object[]{rasterTexture,"HDR_ClientRaster_0",new VRageMath.Vector2I(64,32),true,null,false,true});
        var field=generated.GetField("m_desc",flags);object description=field.GetValue(rasterTexture);
        void Set(string name,int value){var member=description.GetType().GetField(name);member.SetValue(description,member.FieldType.IsEnum?Enum.ToObject(member.FieldType,value):value);}
        Set("Width",64);Set("Height",32);Set("MipLevels",1);Set("ArraySize",1);Set("Format",29);Set("BindFlags",8);Set("Usage",0);Set("OptionFlags",0);field.SetValue(rasterTexture,description);
        var create=(MethodInfo)typeof(NativeRasterUpload.GpuPass).GetField("create",flags).GetValue(pass);
        var reset=(MethodInfo)typeof(NativeRasterUpload.GpuPass).GetField("reset",flags).GetValue(pass);
        var lookup=(MethodInfo)typeof(NativeRasterUpload.GpuPass).GetField("tryTexture",flags).GetValue(pass);
        rasterManager=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(create.DeclaringType);
        typeof(NativeRasterUpload.GpuPass).GetField("fileTextures",flags).SetValue(pass,typeof(Program).GetField(nameof(rasterManager),BindingFlags.Static|BindingFlags.NonPublic));
        var harmony=new Harmony("HDRClientRenderer.Tests.UiUploadAbi");var hook=BindingFlags.NonPublic|BindingFlags.Static;
        try
        {
            harmony.Patch(create,prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(RasterCreate),hook)));
            harmony.Patch(reset,prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(RasterReset),hook)));
            harmony.Patch(lookup,prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(RasterLookup),hook)));
            rasterExists=false;rasterCreates=rasterResets=0;pass.Upload("HDR_ClientRaster_0",64,32,new byte[64*32*4]);
            rasterExists=true;pass.Upload("HDR_ClientRaster_0",64,32,new byte[64*32*4]);
            if(rasterCreates!=1||rasterResets!=1)throw new Exception("UI upload did not use the installed create/reset ABI.");
            Set("MipLevels",7);field.SetValue(rasterTexture,description);bool rejected=false;
            try{pass.Upload("HDR_ClientRaster_0",64,32,new byte[64*32*4]);}catch(InvalidOperationException){rejected=true;}
            if(!rejected||rasterResets!=1)throw new Exception("UI base-only data reached a multi-mip engine reset.");
        }
        finally{harmony.UnpatchAll(harmony.Id);rasterManager=rasterTexture=null;}
        var messages=new List<string>();
        using(var uploader=new NativeRasterUpload())
        {
            uploader.TryInstall(messages.Add);if(!uploader.Ready)throw new Exception("Main-frame UI upload ABI/hook unavailable: "+string.Join("; ",messages));
            var budget=new ClientPixelBudget(()=>new ClientPixelBudget.Frame(0,10,10));uploader.SetBudget(budget);
            if(uploader.Enqueue("HDR_ClientRaster_0",16,16,new byte[16*16*4],1)||budget.Pending!=0)throw new Exception("Oversized UI upload became a permanent queued job.");
            var ready=(int[])typeof(NativeRasterUpload).GetField("ready",flags).GetValue(uploader);var serials=(long[])typeof(NativeRasterUpload).GetField("serials",flags).GetValue(uploader);
            ready[0]=1;serials[0]=7;
            if(!uploader.Enqueue("HDR_ClientRaster_0",4,4,new byte[64],8)||!uploader.TargetReady("HDR_ClientRaster_0",7)||uploader.TargetReady("HDR_ClientRaster_0",8))
                throw new Exception("Deferred UI update did not retain exact previously published serial.");
            uploader.Cancel("HDR_ClientRaster_0");if(budget.Pending!=0||uploader.TargetReady("HDR_ClientRaster_0",7))throw new Exception("UI retirement retained stale publication.");
        }
        Console.WriteLine("PASS: actual UI create/reset ABI is single-mip, unsafe mip resets are rejected, viewport oversize cannot queue and deferred serial retains its front; native calls intercepted, no GPU upload/frame.");
    }
    static void BudgetViewportAbi(Assembly assembly)
    {
        var reader=new ClientPixelBudgetNative(assembly);
        var inactive=new ClientPixelBudget(()=>throw new InvalidOperationException("Offline renderer not initialized."));
        if(inactive.PixelLimit!=0||inactive.TrySpend("ui",ClientPixelBudget.Kind.Ui,1))throw new Exception("Unavailable main viewport did not fail closed.");
        Console.WriteLine("PASS: shared budget binds actual main Backbuffer.Size and FrameCounter; unavailable reader fails closed, no live viewport read or GPU resource/frame.");
    }
    static int Main()
    {
        try { Run(); return 0; }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void Run()
    {
        string gameBin=Environment.GetEnvironmentVariable("SE_BIN")??@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        AssemblyLoadContext.Default.Resolving+=(context,name)=>
        {
            string path=Path.Combine(gameBin,name.Name+".dll");
            return File.Exists(path)?context.LoadFromAssemblyPath(path):null;
        };
        var assembly=Assembly.LoadFrom(Path.Combine(gameBin,"VRage.Render11.dll"));
        var renderer=assembly.GetType("VRageRender.MyBillboardRenderer",true);
        var prepare=renderer.GetMethod("PrepareList",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
        var messages=new List<string>();
        using(var compatibility=new CameraLcdCompatibility())
        {
            compatibility.TryInstall(messages.Add);
            if(!messages.Any(message=>message.Contains("isolation installed")))
                throw new Exception("Capture isolation did not patch installed renderer: "+string.Join("; ",messages));
            var patches=Harmony.GetPatchInfo(prepare);
            if(patches==null||!patches.Transpilers.Any(patch=>patch.owner=="HDRClientRenderer.CameraCaptureIsolation"))
                throw new Exception("Capture isolation patch is missing.");
            StableBillboardPair();
        }
        var remaining=Harmony.GetPatchInfo(prepare);
        if(remaining!=null&&remaining.Transpilers.Any(patch=>patch.owner=="HDRClientRenderer.CameraCaptureIsolation"))
            throw new Exception("Capture isolation did not remove its own patch.");
        Console.WriteLine("PASS: capture isolation installs and uninstalls against the actual game renderer; two billboard-list reads validated.");
        WritableLcdTarget();
        CopyVertexBinding(assembly);
        var nativeMessages=new List<string>();
        using(var copier=new NativeTextureCopy())
        {
            copier.TryInstall(nativeMessages.Add);
            if(!copier.Ready||!nativeMessages.Any(message=>message.Contains("GPU copy installed")))
                throw new Exception("Native GPU copy ABI did not bind: "+string.Join("; ",nativeMessages));
        }
        Console.WriteLine("PASS: direct LCD GPU copy ABI binds and hook installs/removes against actual game renderer; no frame drawn.");
        PanoramaShaderAbi(assembly);
        TransparentPanoramaTarget();
        MipRegenerationAbi(assembly);
        DirectCameraCaptureAbi();
        PanoramaUploadAbi(assembly);
        GeneratedTargetDescriptorAbi(assembly);
        RasterUploadAbi(assembly);
        BudgetViewportAbi(assembly);
    }
}
