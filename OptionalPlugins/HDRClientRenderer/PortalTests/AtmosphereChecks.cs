using System.Reflection;
using System.Text.RegularExpressions;
using HDRClientRenderer;
using VRageMath;

internal static class AtmosphereChecks
{
    const string Atmosphere="Transparent/Atmosphere/AtmosphereGBuffer.hlsl",Cloud="Transparent/Clouds/Clouds.hlsl";
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    internal static void Run(PortalClipShaders compiler,Type macroType,Action<bool,string> check)
    {
        check(PortalClipSource.Family(Atmosphere)==PortalClipFamily.Atmosphere&&PortalClipSource.Family(Cloud)==PortalClipFamily.Cloud,"Native atmosphere and cloud assets have private variants");
        check(PortalCaptureDrawPolicy.Role(Atmosphere)==PortalDrawRole.Geometry&&PortalCaptureDrawPolicy.Role(Cloud)==PortalDrawRole.Geometry&&PortalCaptureDrawPolicy.Role("Transparent/Atmosphere/AtmosphereEnv.hlsl")==PortalDrawRole.Suppressed,"GBuffer atmosphere and clouds demand private warming while probe atmosphere stays suppressed");
        string baseline=null,changed=null;
        foreach(string quality in new[]{"", "LQ", "MQ", "HQ"})foreach(bool sampleFrequency in new[]{false,true})
        {
            var names=new List<string>();if(quality.Length>0)names.Add(quality);if(sampleFrequency)names.Add("SAMPLE_FREQ_PASS");
            Array macros=Array.CreateInstance(macroType,names.Count);
            for(int i=0;i<names.Count;i++)macros.SetValue(Activator.CreateInstance(macroType,new object[]{names[i],null}),i);
            var prepared=compiler.Prepare(new PortalClipDescriptor(null,Atmosphere,"headless",macros));
            byte[] bytes=compiler.CompilePrepared(prepared);
            check(bytes.Length>100&&System.Text.Encoding.ASCII.GetString(bytes,0,4)=="DXBC","Installed atmosphere native single-sample variant compiled "+quality+(sampleFrequency?" SAMPLE_FREQ_PASS":""));
            byte[] original=compiler.CompileFixed(prepared.Source);
            check(compiler.Inspect(original,false).SequenceEqual(compiler.Inspect(bytes,true)),"Atmosphere private variant retains native target signatures");
            check(Bindings(original).SequenceEqual(Bindings(bytes).Where(b=>!b.StartsWith(PortalClipSource.ThresholdName+"/",StringComparison.Ordinal))),"Atmosphere private variant retains fixed native resources plus t31");
            if(baseline==null){baseline=prepared.Source;changed=PortalClipSource.Inject(baseline,PortalClipFamily.Atmosphere);}
        }
        byte[] cloud=compiler.Compile(new PortalClipDescriptor(null,Cloud,"headless",null));
        check(cloud.Length>100,"Installed world cloud mesh private variant compiled");
        var cloudSource=PortalClipSource.Inject(compiler.Prepare(new PortalClipDescriptor(null,Cloud,"headless",null)).Source,PortalClipFamily.Cloud);
        check(cloudSource.Contains("input.positionScreen.xy,0))-input.positionScreen.z"),"Cloud cutoff uses its world mesh SV_Position");
        check(!changed.Contains("length(rayEnd)")&&!changed.Contains("position / PlanetScaleFactor")&&!changed.Contains("-svPos.z"),"Atmosphere never clips native proxy depth or measures an unbounded sky position");
        check(changed.Contains("mul(float4(hdrNdc, hdrThreshold, 1), frame_.Environment.inv_proj_matrix)")&&changed.Contains("hdrCutoffDistance / PlanetScaleFactor")&&changed.Contains("mul(float4(hdrNdc, input.native_depth, 1), frame_.Environment.inv_proj_matrix)"),"Atmosphere cutoff and foreground use complete native inverse projection for scaled world ray distance");
        check(changed.Contains("ComputeAtmosphere(-hdrWorldRay")&&!changed.Contains("compute_depth(hdrThreshold)")&&!changed.Contains("length(compute_screen_ray("),"Atmosphere direction and cutoff bypass diagonal-only native GBuffer reconstruction");
        check(changed.Contains("hdrThreshold <= 0")&&changed.Contains("if (IsDepthForeground(input.native_depth))"),"Clamped-zero shell cutoff fails closed while native zero foreground depth keeps sky endpoint handling");
        check(changed.Contains("depth = max(0, hdrFogEndAxial - hdrCutoffAxial)")&&changed.Contains("hdrStart >= hdrEnd"),"Fog omits removed axial prefix and empty intervals discard");
        foreach(string pattern in new[]{
            @"\bfloat4\s+ComputeAtmosphere\s*\(",@"\bclip\s*\(\s*depth\s*-\s*1000\s*\)",
            @"float3\s+rayEnd\s*=\s*position\s*/\s*PlanetScaleFactor",@"float3\s+Pa\s*=\s*0",
            @"float3\s+PaPTransmittance\s*=\s*0",@"float3\s+P\s*=\s*Pa\s*\+\s*ray\s*\*\s*stepLength\s*\*\s*i",
            @"float3\s+foggedColor\s*=\s*Fog",@"\boutput\s*=\s*ComputeAtmosphere",@"float4\s+svPos\s*:\s*SV_Position",
            @"matrix\s+inv_proj_matrix",@"cbuffer\s+Frame\s*:\s*register\s*\(\s*b0\s*\)",@"EnvironmentSettings\s+Environment\s*;",
            @"SurfaceInterface\s+input\s*=\s*read_gbuffer"})
        {
            var matches=Regex.Matches(baseline,pattern);check(matches.Count==1,"Atmosphere mutation fixture has one native anchor "+pattern);
            string hostile=baseline.Remove(matches[0].Index,matches[0].Length).Insert(matches[0].Index,"HDR_UNVERIFIED_ANCHOR");
            bool rejected=false;try{PortalClipSource.Inject(hostile,PortalClipFamily.Atmosphere);}catch(InvalidOperationException){rejected=true;}
            check(rejected,"Atmosphere changed native anchor fails closed "+pattern);
        }
        bool duplicateRejected=false;try{PortalClipSource.Inject(baseline+baseline,PortalClipFamily.Atmosphere);}catch(InvalidOperationException){duplicateRejected=true;}
        check(duplicateRejected,"Duplicated atmosphere integration refuses private compilation");
        MathChecks(check);
        ProjectionChecks(check);
    }
    static string[] Bindings(byte[] bytes)
    {
        var type=DirectCameraCaptureNative.FindType(null,"SharpDX.D3DCompiler.ShaderReflection");object reflection=type.GetConstructor(new[]{typeof(byte[])}).Invoke(new object[]{bytes});
        try{
            var desc=type.GetProperty("Description",All).GetValue(reflection);int count=(int)desc.GetType().GetField("BoundResources",All).GetValue(desc);var entries=new List<string>();
            var method=type.GetMethod("GetResourceBindingDescription",All,null,new[]{typeof(int)},null);
            for(int i=0;i<count;i++){var item=method.Invoke(reflection,new object[]{i});entries.Add(string.Join("/",new[]{"Name","Type","BindPoint","BindCount","Dimension"}.Select(n=>item.GetType().GetField(n,All).GetValue(item).ToString())));}
            return entries.OrderBy(e=>e,StringComparer.Ordinal).ToArray();
        }finally{((IDisposable)reflection).Dispose();}
    }
    static void MathChecks(Action<bool,string> check)
    {
        // Independent geometric examples: centre ray and 60-degree ray, both
        // ending on a sphere 50 scaled metres away, scale = 2 world metres.
        check(Interval(10,1,2,-50,50,double.PositiveInfinity,out var start,out var end,out var fog)&&start==5&&Math.Abs(end-49.95)<1e-9&&fog==90,"Inside observer centre ray starts after 10 metre guarded cutoff");
        check(Interval(10,2,2,-50,50,double.PositiveInfinity,out start,out end,out fog)&&start==10&&fog==40,"Off-axis 60-degree cutoff converts axial depth to double ray distance");
        check(Interval(10,1,2,20,50,60,out start,out end,out fog)&&Math.Abs(start-20.02)<1e-9&&Math.Abs(end-29.97)<1e-9&&fog==50,"Outside atmosphere retains guarded atmosphere entry and nearer surviving foreground");
        check(Interval(60,1,2,20,50,60,out start,out end,out fog)==false,"Foreground at far-shell cutoff has no surviving atmosphere interval");
        check(!Interval(101,1,2,-50,50,double.PositiveInfinity,out start,out end,out fog),"Shell beyond atmosphere far boundary discards sky interval");
        check(!Interval(10,1,2,80,50,double.PositiveInfinity,out start,out end,out fog),"Reversed atmosphere interval discards");
        foreach(double invalid in new[]{0,-1,double.NaN,double.PositiveInfinity})
        {
            check(!Interval(10,1,invalid,-50,50,60,out start,out end,out fog),"Invalid planet metre scale discards "+invalid);
            check(!Interval(10,invalid,2,-50,50,60,out start,out end,out fog),"Invalid camera ray scale discards "+invalid);
            check(!Interval(invalid,1,2,-50,50,60,out start,out end,out fog),"Invalid cutoff axial depth discards "+invalid);
        }
        check(!Interval(10,1,2,-50,double.NaN,60,out start,out end,out fog)&&!Interval(10,1,2,-50,double.PositiveInfinity,60,out start,out end,out fog),"Nonfinite atmosphere far bounds discard");
    }
    static bool Interval(double cutoff,double rayScale,double metres,double near,double far,double foregroundAxial,out double start,out double end,out double fog)
    {
        start=end=fog=0;if(!double.IsFinite(cutoff)||cutoff<=0||!double.IsFinite(rayScale)||rayScale<=0||!double.IsFinite(metres)||metres<=0||!double.IsFinite(near)||!double.IsFinite(far)||far<=0)return false;
        double bound=double.IsPositiveInfinity(foregroundAxial)?far:Math.Min(far,foregroundAxial*rayScale/metres);
        start=Math.Max(Math.Max(0,near)*1.001,cutoff*rayScale/metres);end=bound*.999;
        fog=Math.Max(0,(double.IsPositiveInfinity(foregroundAxial)?bound*metres/rayScale:foregroundAxial)-cutoff);
        return double.IsFinite(start)&&double.IsFinite(end)&&double.IsFinite(fog)&&start<end;
    }
    static void ProjectionChecks(Action<bool,string> check)
    {
        var native=MatrixD.Identity;native.M11=1.3;native.M22=1.8;native.M33=0;native.M34=-1;native.M43=.1;native.M44=0;
        var xyShear=native;xyShear.M12=.45;xyShear.M21=-.25;xyShear.M31=.18;xyShear.M32=-.21;
        // This matches paired-plane transport's rotated unequal differential,
        // with both XY coupling and a noncanonical homogeneous W plane.
        var paired=MatrixD.CreateRotationZ(.37)*MatrixD.CreateScale(1.9,.65,1.15)*MatrixD.CreateRotationY(.22)*native;
        paired*=(-1/paired.M34);
        var crop=MatrixD.Identity;crop.M11=2.3;crop.M22=1.7;crop.M41=-.65;crop.M42=.42;
        var cases=new[]{native,xyShear,paired,xyShear*crop,paired*crop};
        bool oldHelperMismatch=false;
        for(int c=0;c<cases.Length;c++)foreach(var uv in new[]{new Vector2D(.5,.5),new Vector2D(.2,.8),new Vector2D(.87,.24)})
        {
            var projection=cases[c];var ray=Vector3D.Zero;check(PortalProjection.ComplementaryProjection(projection)&&PortalProjection.ViewRay(projection,uv.X,uv.Y,out ray),"Complete projection fixture has supported threshold view ray "+c);
            // Float operations mirror native b0's matrix precision and HLSL mul.
            var inverse=(Matrix)MatrixD.Invert(projection);
            var near=Unproject(inverse,uv,1);var actualRay=Vector3D.Normalize(near);
            check(Vector3D.Distance(ray,actualRay)<2e-6,"Atmosphere reconstructed ray matches threshold ViewRay under rotated unequal/sheared/off-axis crop "+c);
            var cutoffClip=Vector4D.Transform(new Vector4D(ray*120,1),projection);
            var foregroundClip=Vector4D.Transform(new Vector4D(ray*180,1),projection);
            var cutoff=Unproject(inverse,uv,cutoffClip.Z/cutoffClip.W);
            var foreground=Unproject(inverse,uv,foregroundClip.Z/foregroundClip.W);
            check(Math.Abs(Vector3D.Dot(cutoff,actualRay)-120)<2e-4&&Math.Abs(Vector3D.Dot(foreground,actualRay)-180)<3e-4,"Full inverse reconstructs guarded shell and foreground ray distance "+c);
            check(Math.Abs((-foreground.Z)-(-cutoff.Z)-60*-ray.Z)<3e-4,"Residual atmosphere fog uses true axial endpoint difference "+c);
            var old=Vector3D.Normalize(new Vector3D((2*uv.X-1+projection.M31)/projection.M11,(1-2*uv.Y+projection.M32)/projection.M22,-1));
            oldHelperMismatch|=Vector3D.Distance(ray,old)>.02;
        }
        check(oldHelperMismatch,"Sheared and rotated unequal fixtures expose diagonal-only native helper error");
        var finiteFar=native;finiteFar.M33=.001;
        var beyondFar=Vector4D.Transform(new Vector4D(0,0,-120,1),finiteFar);
        double trueDepth=beyondFar.Z/beyondFar.W,clampedDepth=Math.Max(0,Math.Min(1,trueDepth));
        var farPoint=Unproject((Matrix)MatrixD.Invert(finiteFar),new Vector2D(.5,.5),clampedDepth);
        check(trueDepth<0&&clampedDepth==0&&PortalProjection.Finite(farPoint)&&Math.Abs(farPoint.Z+100)<1e-4&&-farPoint.Z<120,"Negative shell depth clamps to zero and inverse reconstruction would start before its true finite-far boundary");
        check(!(double.IsFinite(clampedDepth)&&clampedDepth>0&&clampedDepth<=1),"Clamped-zero beyond-far shell cannot authorize atmosphere integration of the interior");
        var infinite=Vector4.Transform(new Vector4(0,0,0,1),(Matrix)MatrixD.Invert(native));
        check(Math.Abs(infinite.W)<1e-12,"Infinite sky endpoint has homogeneous W zero and uses atmosphere far bound without division");
    }
    static Vector3D Unproject(Matrix inverse,Vector2D uv,double depth)
    {
        var h=Vector4.Transform(new Vector4((float)(2*uv.X-1),(float)(1-2*uv.Y),(float)depth,1),inverse);
        return new Vector3D(h.X/h.W,h.Y/h.W,h.Z/h.W);
    }
}
