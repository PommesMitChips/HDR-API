using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HDRClientRenderer;
using HarmonyLib;
using VRageMath;
internal static class NativeResolutionChecks
{
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static bool nullReset;
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void GetTextureDescription(IntPtr self,IntPtr output);
    static bool CaptureNullData(object[] __args){nullReset=__args.Length==1&&__args[0]==null;return false;}
    internal static unsafe void Run(Action<bool,string> check)
    {
        var assembly=DirectCameraCaptureNative.FindType(null,"VRageRender.MyRender11").Assembly;
        object current=null;long frame=12345;var reader=new PortalPrimaryViewNative(assembly,()=>current,()=>frame,()=>new Vector2I(2560,1440));PortalPrimaryView view;
        check(!reader.TrySnapshot(1,out view)&&view==null,"Uninitialized primary render matrices cannot publish a fake latched viewer");
        check(PortalPrimaryViewNative.PositionMatches(new Vector3D(1e12,2,-3),new Vector3D(1e12+.001,2,-3))&&!PortalPrimaryViewNative.PositionMatches(Vector3D.Zero,new Vector3D(.01,0,0)),"Primary double position agrees with native camera at distant-world precision and rejects stale origins");
        // Native instance fields are read unchanged; only the three static game
        // entry reads are replaced, avoiding a renderer/device initialization.
        var render=DirectCameraCaptureNative.FindType(null,"VRageRender.MyRender11");var environment=render.GetField("Environment",All);object env=RuntimeHelpers.GetUninitializedObject(environment.FieldType);
        var matrices=environment.FieldType.GetField("Matrices",All);object pose=RuntimeHelpers.GetUninitializedObject(matrices.FieldType);matrices.SetValue(env,pose);
        var world=MatrixD.CreateTranslation(1e12,20,-30);var p=Matrix.Zero;p.M11=p.M22=1;p.M34=-1;p.M43=.1f;
        pose.GetType().GetField("InvViewD",All).SetValue(pose,world);pose.GetType().GetField("Projection",All).SetValue(pose,p);pose.GetType().GetField("CameraPosition",All).SetValue(pose,world.Translation);
            current=env;
            check(reader.TrySnapshot(7,out view)&&view.Frame==12345&&view.Epoch==7&&view.Width==2560&&view.Height==1440&&view.Viewer.Translation==world.Translation,"Actual native environment reader latches double-precision primary pose, projection, frame, viewport and epoch atomically");
            pose.GetType().GetField("InvViewD",All).SetValue(pose,MatrixD.CreateTranslation(world.Translation+Vector3D.UnitX));
            check(view.Viewer.Translation==world.Translation&&!reader.TrySnapshot(7,out var stale),"Primary snapshot is immutable and refuses a camera position inconsistent with its native environment");
        pose.GetType().GetField("InvViewD",All).SetValue(pose,world);
        var nativeTexture=DirectCameraCaptureNative.FindType(null,"SharpDX.Direct3D11.Texture2D");object texture=RuntimeHelpers.GetUninitializedObject(nativeTexture);GC.SuppressFinalize(texture);
        var textureDesc=nativeTexture.GetProperty("Description",All);object physicalDescription=Activator.CreateInstance(textureDesc.PropertyType);textureDesc.PropertyType.GetField("Width").SetValue(physicalDescription,3840);textureDesc.PropertyType.GetField("Height").SetValue(physicalDescription,2160);
        FieldInfo nativePointer=null;for(var t=nativeTexture;t!=null&&nativePointer==null;t=t.BaseType)nativePointer=t.GetField("_nativePointer",All|BindingFlags.DeclaredOnly);
        var table=Marshal.AllocHGlobal(11*IntPtr.Size);var nativeInstance=Marshal.AllocHGlobal(IntPtr.Size);for(int i=0;i<11;i++)Marshal.WriteIntPtr(table,i*IntPtr.Size,IntPtr.Zero);Marshal.WriteIntPtr(nativeInstance,table);
        int descriptorReads=0;GetTextureDescription physicalDescriptionCall=(self,output)=>{descriptorReads++;Marshal.StructureToPtr(physicalDescription,output,false);};Marshal.WriteIntPtr(table,10*IntPtr.Size,Marshal.GetFunctionPointerForDelegate(physicalDescriptionCall));nativePointer.SetValue(texture,Pointer.Box(nativeInstance.ToPointer(),nativePointer.FieldType));
        try
        {
            var drsReader=new PortalPrimaryViewNative(assembly,()=>current,()=>frame,()=>new Vector2I(960,540),()=>reader.PresentationSize(texture,null));
            check(drsReader.TrySnapshot(7,out var drs)&&drs.Width==960&&drs.Height==540&&drs.PresentationWidth==3840&&drs.PresentationHeight==2160&&drs.Projection==view.Projection&&descriptorReads==1,"Actual SharpDX texture descriptor calli reads physical swapchain density while native viewport/projection remain reduced; CPU vtable only, no GPU/readback");
        }
        finally{nativePointer.SetValue(texture,Pointer.Box(null,nativePointer.FieldType));Marshal.FreeHGlobal(nativeInstance);Marshal.FreeHGlobal(table);GC.KeepAlive(physicalDescriptionCall);}
        var generated=DirectCameraCaptureNative.FindType(null,"VRage.Render11.Resources.Internal.MyGeneratedTexture");var user=DirectCameraCaptureNative.FindType(null,"VRage.Render11.Resources.Internal.MyUserGeneratedTexture");var description=generated.GetField("m_desc",All);var descriptor=Activator.CreateInstance(description.FieldType);
        void Set(string field,int value){var f=description.FieldType.GetField(field);f.SetValue(descriptor,f.FieldType.IsEnum?Enum.ToObject(f.FieldType,value):value);}
        Set("Width",2048);Set("Height",2048);Set("MipLevels",12);Set("Format",29);Set("ArraySize",1);Set("Usage",0);Set("BindFlags",40);Set("OptionFlags",1);
        var sample=description.FieldType.GetField("SampleDescription");var s=Activator.CreateInstance(sample.FieldType);sample.FieldType.GetField("Count").SetValue(s,1);sample.FieldType.GetField("Quality").SetValue(s,0);sample.SetValue(descriptor,s);
        object target=RuntimeHelpers.GetUninitializedObject(user);description.SetValue(target,descriptor);generated.GetField("m_isGeneratingMipmaps",All).SetValue(target,true);
        var single=new PortalSingleMipTargetNative(assembly);
        check(single.Prepare(target,2048,2048)&&single.Matches(target,2048,2048)&&single.Compatible(target,2048,2048),"Actual generated descriptor becomes a one-mip sRGB RTV before first activation without GPU allocation");
        var reset=generated.GetMethods(All).Single(m=>m.Name=="Reset"&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType.GetElementType()?.FullName=="SharpDX.DataBox");
        var resetIl=PatchProcessor.GetOriginalInstructions(reset);int autogen=resetIl.FindIndex(i=>i.operand is MethodInfo method&&method.Name=="GenerateMips");
        check(autogen>=4&&resetIl[autogen-4].opcode==OpCodes.Ldarg_1&&(resetIl[autogen-3].opcode==OpCodes.Brfalse_S||resetIl[autogen-3].opcode==OpCodes.Brfalse)&&resetIl.Skip(autogen+1).Any(i=>i.labels.Contains((Label)resetIl[autogen-3].operand)&&i.opcode==OpCodes.Ret),"Actual generated Reset skips mip generation for null initial data despite reserving RTV bindings");
        var harmony=new Harmony("HDR.Portal.SingleMipNullDataFixture");harmony.Patch(reset,prefix:new HarmonyMethod(typeof(NativeResolutionChecks).GetMethod("CaptureNullData",All)));
        try{user.GetMethod("Reset",All,null,new[]{typeof(byte[])},null).Invoke(target,new object[]{null});check(nullReset,"Actual user Reset(null) and manager reset preserve null DataBox data through the single-mip activation chain");}finally{harmony.UnpatchAll(harmony.Id);}
        var srvField=generated.GetField("m_srv",All);object srv=RuntimeHelpers.GetUninitializedObject(srvField.FieldType);GC.SuppressFinalize(srv);srvField.SetValue(target,srv);
        check(single.Prepare(target,2048,2048)&&!single.Activate(target,2048,2048)&&ReferenceEquals(srv,srvField.GetValue(target)),"Single-mip helper never Reset/Activate a live retained SRV");
        check(!single.Prepare(target,1024,1024)&&ReferenceEquals(srv,srvField.GetValue(target)),"Changing native bucket size refuses to resize a live texture");
        description.GetValue(target);descriptor=description.GetValue(target);Set("Format",28);description.SetValue(target,descriptor);
        check(!single.Prepare(target,2048,2048),"Single-mip native allocation refuses non-sRGB descriptor substitutions");
        int w,h;
        check(PortalActiveRectangle.TryCreate(new Vector2I(2048),2044,1532,2,out w,out h)&&w==2048&&h==1536,"Native viewport and ROI copy include exactly the active content plus two-pixel guard");
        check(!PortalActiveRectangle.TryCreate(new Vector2I(2048),2048,512,2,out w,out h)&&!PortalActiveRectangle.TryCreate(new Vector2I(1024),int.MaxValue,10,2,out w,out h)&&!PortalActiveRectangle.TryCreate(new Vector2I(4096),1,1,2,out w,out h),"Active ROI rejects gutter overflow, integer overflow and untrusted physical buckets");
        long pixels=0;var names=new HashSet<int>();for(int slot=0;slot<2;slot++)for(int bucket=256;bucket<=2048;bucket*=2){string name="HDR_ClientPortal_"+slot+"_N"+bucket;names.Add(PortalFrontTargetPool.Slot(name));pixels+=(long)bucket*bucket;check(PortalFrontTargetPool.IsNative(name)&&PortalFrontTargetPool.PhysicalSize(name)==bucket,"Native persistent bucket name matches fixed allocation "+name);}
        check(names.Count==8&&pixels*4==44564480&&PortalFrontTargetPool.TotalTargets==10&&PortalFrontTargetPool.Slot("HDR_ClientPortal_0_N4096")==-1,"Eight new single-mip fronts use exactly 42.5 MiB base RGBA and retain two legacy fronts");
    }
}
