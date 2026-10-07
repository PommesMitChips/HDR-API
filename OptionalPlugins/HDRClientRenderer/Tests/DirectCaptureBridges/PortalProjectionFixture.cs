using System.Reflection;
using System.Runtime.InteropServices;
using VRageMath;

static class PortalProjectionFixture
{
    const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type Engine(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null);
    internal static void Run(Assembly plugin)
    {
        int checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception(why); checks++; }
        var normalize = plugin.GetType("HDRClientRenderer.HDRClientRendererPlugin", true).GetMethod("PortalProjectionFromCamera", S);
        var ordinary = MatrixD.CreatePerspectiveFieldOfView(.8, 1.7, .2, 3000);
        ordinary.M31 = .17; ordinary.M32 = -.11; ordinary.M12 = .03; ordinary.M21 = -.02;
        object[] args = { ordinary, MatrixD.Identity };
        Check((bool)normalize.Invoke(null, args), "Actual gameplay-style RH projection must convert for native portal capture.");
        var reverse = (MatrixD)args[1];
        Check(reverse.M43 > 0 && reverse.M33 >= 0 && reverse.M11 == ordinary.M11 && reverse.M12 == ordinary.M12 && reverse.M31 == ordinary.M31, "Conversion preserves authored screen rays and reverses only depth.");
        foreach (double z in new[] { -.2, -1d, -100d, -3000d })
        {
            var a = Vector4D.Transform(new Vector4D(.1, -.07, z, 1), ordinary);
            var b = Vector4D.Transform(new Vector4D(.1, -.07, z, 1), reverse);
            Check(Math.Abs(a.X / a.W - b.X / b.W) < 1e-12 && Math.Abs(a.Y / a.W - b.Y / b.W) < 1e-12 && Math.Abs(b.Z / b.W - (1 - a.Z / a.W)) < 1e-12, "Reverse Z preserves projected position at every tested depth.");
        }
        args = new object[] { reverse, MatrixD.Identity };
        Check((bool)normalize.Invoke(null, args) && (MatrixD)args[1] == reverse, "Complementary matrices are not reversed twice.");
        var render = Engine("VRageRender.MyRender11");
        var adapterType = plugin.GetType("HDRClientRenderer.DirectCameraCaptureNative", true);
        var adapter = Activator.CreateInstance(adapterType, I, null, new object[] { render.Assembly }, null);
        var envField = render.GetField("Environment", S);
        var matricesField = envField.FieldType.GetField("Matrices", I);
        var matrices = matricesField.GetValue(envField.GetValue(null));
        var snapshotType = adapterType.GetNestedType("CameraMatricesSnapshot", BindingFlags.NonPublic);
        var snapshot = Activator.CreateInstance(snapshotType, I, null, new object[] { envField, matricesField }, null);
        try
        {
            var message = adapterType.GetMethod("CameraMessage", I).Invoke(adapter, new object[] { MatrixD.Identity, 42d, matrices, null, (MatrixD?)reverse });
            object Read(object value, string member) => value.GetType().GetField(member, I)?.GetValue(value) ??
                value.GetType().GetProperty(member, I)?.GetValue(value) ?? throw new Exception("Missing installed projection member: " + member);
            Check((float)Read(message, "FOV") == 0, "Authored portal FOV selects actual engine explicit-projection branch, even below camera panorama's 60-degree bound.");
            Check((float)Read(message, "ProjectionOffsetX") == (float)reverse.M31 && (float)Read(message, "ProjectionOffsetY") == (float)reverse.M32, "Setup's unconditional offsets preserve the authored off-axis projection.");
            var setup = (MethodInfo)adapterType.GetField("setup", I).GetValue(adapter);
            setup.Invoke(null, new[] { message, matrices, Enum.ToObject(setup.GetParameters()[2].ParameterType, 0) });
            var installed = (Matrix)Read(matrices, "Projection");
            var distant = (Matrix)Read(matrices, "ProjectionForSkybox");
            Check(installed == (Matrix)reverse && distant == (Matrix)reverse, "Installed renderer preserves both authored near/far projection matrices exactly; no symmetric FOV substitution.");
            Check(installed.M12 != 0 && installed.M21 != 0 && installed.M11 != installed.M22, "Native setup retains skew and anisotropic aspect.");
            var common=Engine("VRageRender.MyCommon");var fill=common.GetMethod("UpdateFrameConstantsInternal",S);var layout=fill.GetParameters()[1].ParameterType.GetElementType();
            void VerifyFrame(MatrixD authored,string label)
            {
                var supplied=adapterType.GetMethod("CameraMessage",I).Invoke(adapter,new object[]{MatrixD.Identity,42d,matrices,null,(MatrixD?)authored});
                setup.Invoke(null,new[]{supplied,matrices,Enum.ToObject(setup.GetParameters()[2].ParameterType,0)});
                var expected=Matrix.Invert((Matrix)authored);
                Check((Matrix)Read(matrices,"Projection")== (Matrix)authored&&(Matrix)Read(matrices,"InvProjection")==expected,label+": actual setup retains all sixteen authored elements and computes full float inverse.");
                object[] fillArgs={matrices,Activator.CreateInstance(layout),Enum.ToObject(fill.GetParameters()[2].ParameterType,0)};fill.Invoke(null,fillArgs);
                object environment=Read(fillArgs[1],"Environment");var uploaded=(Matrix)Read(environment,"InvProjection");
                Check(uploaded==Matrix.Transpose(expected)&&(Matrix)Read(environment,"Projection")==Matrix.Transpose((Matrix)authored),label+": actual frame b0 CPU generator writes the complete transposed inverse and projection.");
                Check(Marshal.OffsetOf(layout,"Environment").ToInt64()==0&&Marshal.OffsetOf(environment.GetType(),"InvProjection").ToInt64()==320,label+": native frame/environment inverse layout agrees with HLSL b0 matrix offset.");
                var mappingType=Engine("VRageRender.MyMapping");object mapping=Activator.CreateInstance(mappingType,true);var memory=Marshal.AllocHGlobal(Marshal.SizeOf(layout));
                try
                {
                    mappingType.GetField("m_dataPointer",I).SetValue(mapping,memory);
                    var write=mappingType.GetMethods(I).Single(m=>m.Name=="WriteAndPosition"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType.IsByRef).MakeGenericMethod(layout);
                    write.Invoke(mapping,new[]{fillArgs[1]});
                    var nativeBytes=new byte[64];var expectedBytes=new byte[64];var expectedMemory=Marshal.AllocHGlobal(64);
                    try{Marshal.StructureToPtr(Matrix.Transpose(expected),expectedMemory,false);Marshal.Copy(expectedMemory,expectedBytes,0,64);Marshal.Copy(IntPtr.Add(memory,320),nativeBytes,0,64);}
                    finally{Marshal.FreeHGlobal(expectedMemory);}
                    Check(Marshal.SizeOf(layout)==1280&&nativeBytes.SequenceEqual(expectedBytes)&&Marshal.PtrToStructure<Matrix>(IntPtr.Add(memory,64))==Matrix.Transpose((Matrix)authored)&&(IntPtr)mappingType.GetField("m_dataPointer",I).GetValue(mapping)==IntPtr.Add(memory,1280),label+": actual native upload preserves all sixteen inverse float bits at b0+320 and advances exactly1280 bytes without GPU mapping.");
                }
                finally{Marshal.FreeHGlobal(memory);}
                foreach(var uv in new[]{new Vector2D(.15,.2),new Vector2D(.5,.5),new Vector2D(.87,.81)})
                {
                    var screen=new Vector4D(uv.X*2-1,1-uv.Y*2,1,1);var threshold=Vector4D.Transform(screen,MatrixD.Invert(authored));var native=Vector4.Transform((Vector4)screen,Matrix.Transpose(uploaded));
                    var nativeRay=Vector3D.Normalize(new Vector3D(native.X/native.W,native.Y/native.W,native.Z/native.W));var thresholdRay=Vector3D.Normalize(new Vector3D(threshold.X/threshold.W,threshold.Y/threshold.W,threshold.Z/threshold.W));
                    Check(Vector3D.Distance(nativeRay,thresholdRay)<2e-6,label+": actual native b0 float inverse ray matches full threshold projection ray.");
                }
            }
            VerifyFrame(reverse,"authored shear/off-axis");
            // Build an actual unequal, rotated differential plane snapshot through
            // the production declaration math, then crop its full pinhole.
            var surfaceType=plugin.GetType("HDRClientRenderer.PortalSurfaceMap",true);var plane=surfaceType.GetMethod("TryPlane",S);
            object[] entryArgs={MatrixD.Identity,4d,2d,null};Check((bool)plane.Invoke(null,entryArgs),"Paired native projection entry plane created.");
            object[] exitArgs={MatrixD.CreateRotationZ(.4)*MatrixD.CreateRotationY(-.3),7d,1d,null};Check((bool)plane.Invoke(null,exitArgs),"Paired native projection unequal rotated exit plane created.");
            object[] shellArgs={MatrixD.Identity,new Vector3D(10),null};Check((bool)surfaceType.GetMethods(S).Single(m=>m.Name=="TryEllipsoid"&&m.GetParameters().Length==3).Invoke(null,shellArgs),"Paired native projection shell created.");
            var declaration=plugin.GetType("HDRClientRenderer.PortalProvider+Declaration",true);var descriptor=new object[19];descriptor[1]=0;descriptor[2]=0;descriptor[17]=new[]{1d,60d,1d,1d};descriptor[18]=new[]{512,512,0,0};
            object authoredDeclaration=Activator.CreateInstance(declaration,I,null,new[]{descriptor,entryArgs[3],exitArgs[3],shellArgs[2]},null);
            object[] snapshotArgs={1,2L,MatrixD.CreateTranslation(.3,-.2,3),reverse,null};Check((bool)declaration.GetMethod("Snapshot",I).Invoke(authoredDeclaration,snapshotArgs),"Production rotated unequal plane snapshot supplies native projection.");
            var paired=(MatrixD)Read(snapshotArgs[4],"Projection");VerifyFrame(paired,"paired rotated unequal plane");
            var crop=MatrixD.Identity;crop.M11=1.6;crop.M22=1.4;crop.M41=-.1;crop.M42=.2;VerifyFrame(paired*crop,"paired rotated unequal plane crop");
        }
        finally { snapshotType.GetMethod("Restore", I).Invoke(snapshot, null); }
        Console.WriteLine("PASS: " + checks + " real native portal projection assertions; no GPU objects or game frames.");
    }
}
