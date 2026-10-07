using System.Reflection;
using System.Runtime.Loader;
using HDRClientRenderer;
using VRageMath;

namespace HDRClientRenderer
{
    // Lookup/compiler reflection only; no renderer, GPU or game is initialized.
    internal static class DirectCameraCaptureNative
    {internal static Type FindType(Assembly assembly,string name){return AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null)??throw new Exception("Missing installed type "+name);}}
}
internal static partial class Program
{
    static int checks;
    static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
    static bool Close(double a,double b,double tolerance=1e-8){return Math.Abs(a-b)<=tolerance;}
    static bool Close(Vector3D a,Vector3D b,double tolerance=1e-8){return Vector3D.Distance(a,b)<=tolerance;}
    static MatrixD ReverseProjection(){var p=MatrixD.Identity;p.M33=0;p.M34=-1;p.M43=.1;p.M44=0;return p;}
    static PortalSurfaceMap Plane(MatrixD pose,double width=2,double height=2)
    {PortalSurfaceMap p;Check(PortalSurfaceMap.TryPlane(pose,width,height,out p),"Create plane");return p;}
    static PortalSurfaceMap Sphere(MatrixD pose,double radius=2)
    {PortalSurfaceMap p;Check(PortalSurfaceMap.TryEllipsoid(pose,new Vector3D(radius),out p),"Create spherical shell");return p;}
    static PortalRayMapSpec Spec(PortalSurfaceMap entry,PortalSurfaceMap exit,PortalSurfaceMap shell,MatrixD viewer,MatrixD capture,
        PortalRayTransport transport=PortalRayTransport.Differential,PortalRayAccuracy accuracy=PortalRayAccuracy.ExactCaptureLine,PortalImageProjection image=PortalImageProjection.Perspective,int entryChart=0,int exitChart=0,double normalScale=1)
    {
        PortalRayMapSpec spec;Check(PortalRayMapSpec.TryCreate(2,7,entry,entryChart,exit,exitChart,shell,viewer,capture,ReverseProjection(),transport,image,accuracy,normalScale,out spec),"Create ray map snapshot");return spec;
    }
    static void Main()
    {
        string bin=@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        AssemblyLoadContext.Default.Resolving+=(c,n)=>{var p=Path.Combine(bin,n.Name+".dll");return File.Exists(p)?c.LoadFromAssemblyPath(p):null;};
        try{MathChecks();PortalPerspectiveChecks();ProviderChecks();ShaderChecks(bin);Console.WriteLine("PASS: "+checks+" general/stealth mapping, provider authorization/concurrency/budget, native ABI and installed shader compiler assertions; no GPU objects/game frames.");}
        catch(Exception ex){Console.WriteLine(ex.GetBaseException());Environment.ExitCode=1;}
    }
    static void MathChecks()
    {
        var identity=MatrixD.Identity;PortalSurfaceMap rejected;PortalSurfaceSample sample;
        Check(!PortalSurfaceMap.TryPlane(MatrixD.CreateScale(2),2,2,out rejected),"Reject nonrigid frame");
        Check(!PortalSurfaceMap.TryPlane(identity,double.NaN,2,out rejected),"Reject NaN dimensions");
        Check(!PortalSurfaceMap.TryEllipsoid(identity,new Vector3D(.01,2,2),out rejected),"Reject microscopic ellipsoid");
        var entry=Plane(identity);Check(entry.Evaluate(.75,.25,0,out sample)&&Close(sample.Point,new Vector3D(.5,.5,0)),"Plane coordinate orientation");
        Check(Close(sample.Du,new Vector3D(2,0,0))&&Close(sample.Dv,new Vector3D(0,-2,0)),"Plane differential");
        Check(!entry.Evaluate(-.1,.5,0,out sample)&&!entry.Evaluate(.5,.5,1,out sample),"UV and chart fail closed");
        double near,far,distance;
        Check(PortalRayMapping.EllipsoidRoots(new Vector3D(0,0,5),-Vector3D.UnitZ,new Vector3D(2),out near,out far)&&Close(near,3)&&Close(far,7),"Ellipsoid exterior roots");
        Check(PortalRayMapping.EllipsoidRoots(Vector3D.Zero,-Vector3D.UnitX,new Vector3D(4,2,1),out near,out far)&&Close(near,-4)&&Close(far,4),"Anisotropic shell roots");
        Check(PortalRayMapping.EllipsoidRoots(new Vector3D(2,0,5),-Vector3D.UnitZ,new Vector3D(2),out near,out far)&&Close(near,5)&&Close(far,5),"Tangent shell ray");
        Check(!PortalRayMapping.EllipsoidRoots(new Vector3D(3,0,5),-Vector3D.UnitZ,new Vector3D(2),out near,out far),"Shell miss");
        Check(!PortalRayMapping.EllipsoidRoots(Vector3D.Zero,new Vector3D(2,0,0),new Vector3D(2),out near,out far),"Reject nonunit ray");
        var anisotropicBoundary=new Vector3D(Math.Sqrt(2),1/Math.Sqrt(2),0);var outward=new Vector3D(-.8,.6,0);PortalSurfaceMap normalShell;
        Check(PortalSurfaceMap.TryEllipsoid(MatrixD.Identity,new Vector3D(2,1,1),out normalShell),"Create anisotropic shell normal regression");
        var normalCamera=MatrixD.CreateWorld(anisotropicBoundary-outward,outward,Vector3D.UnitZ);var normalSpec=Spec(Plane(MatrixD.Identity),Plane(MatrixD.Identity),normalShell,MatrixD.CreateTranslation(0,0,5),normalCamera);
        var inverseNormalCamera=MatrixD.Transpose(normalCamera.GetOrientation());var normalRay=new PortalMappedRay(anisotropicBoundary-outward*.5,outward,Vector3D.TransformNormal(outward*.5,inverseNormalCamera),Vector3D.TransformNormal(outward,inverseNormalCamera),1);
        PortalRaySample normalSample;Check(normalSpec.Sample(normalRay,out normalSample)&&normalSample.Exact,"Anisotropic FAR branch uses implicit gradient r-squared weighting consistent with HLSL");
        Check(PortalRayMapping.EllipsoidRoots(new Vector3D(0,0,1e9),-Vector3D.UnitZ,new Vector3D(.1,.2,.1),out near,out far)&&Close(far,1e9+.1,1e-6),"Distant small ellipsoid avoids discriminant cancellation");
        Check(PortalProjection.FarIntersection(new Vector3D(0,0,-1e9),-Vector3D.UnitZ,.1,out far)&&Close(far,1e9+.1,1e-6),"Distant small sphere avoids discriminant cancellation");
        Vector3D inflated;double nativeGuard;
        Check(PortalProjectionShaders.NativeShell(new Vector3D(0,0,-1000),new Vector3D(2),out inflated,out nativeGuard)&&inflated.X>2.01&&nativeGuard>.01,"Native float capture inflates shell and guard beyond eye-coordinate rounding");
        Check(!PortalProjectionShaders.NativeShell(new Vector3D(0,0,-1e6),new Vector3D(.1),out inflated,out nativeGuard),"Unstable far-eye/small-shell native ratio fails closed");
        Check(!PortalProjectionShaders.NativeShell(Vector3D.Zero,new Vector3D(250,.05,1),out inflated,out nativeGuard),"Ill-conditioned ellipsoid native axes fail closed");
        Check(PortalProjectionShaders.NativeShell(new Vector3D(0,0,-100),new Vector3D(2),out inflated,out nativeGuard),"Conservative native sphere cutoff test shell");
        Check(PortalProjection.FarIntersection(new Vector3D(0,0,-100),Vector3D.Normalize(new Vector3D(2,0,-100)),inflated.X,out far)&&far>Math.Sqrt(10004),"Authored grazing boundary remains ahead of the conservative far cutoff");
        var sphere=Sphere(identity);Check(sphere.Evaluate(.5,.5,0,out sample)&&Close(sample.Point,new Vector3D(0,0,2)),"Angular ellipsoid convention");
        Check(!sphere.Evaluate(.5,0,0,out sample)&&!sphere.Evaluate(.5,1,0,out sample),"Singular pole chart rejected");
        Check(sphere.FirstHit(new Vector3D(0,0,5),-Vector3D.UnitZ,out distance)&&Close(distance,3),"Ellipsoid near branch");
        Check(sphere.FirstHit(Vector3D.Zero,-Vector3D.UnitZ,out distance)&&Close(distance,2),"Ellipsoid inside far branch");
        Check(!entry.FirstHit(new Vector3D(2,0,5),-Vector3D.UnitZ,out distance),"Bounded plane aperture miss");
        var destination=MatrixD.CreateTranslation(0,0,-10);var exit=Plane(destination);var shell=Sphere(destination);
        var viewer=MatrixD.CreateTranslation(0,0,5);var camera=MatrixD.CreateTranslation(0,0,-5);var spec=Spec(entry,exit,shell,viewer,camera);PortalMappedRay ray;PortalRaySample sampled;
        Check(spec.Ray(.5,.5,out ray)&&Close(ray.Origin,destination.Translation)&&Close(ray.Direction,-Vector3D.UnitZ),"Differential transports central sight ray");
        Check(spec.Sample(.5,.5,out sampled)&&sampled.Exact&&Close(sampled.ImageUv.X,.5)&&Close(sampled.ImageUv.Y,.5)&&Close(sampled.ShellDistance,2),"Perspective capture exact line");
        Check(Close(sampled.EntryDistance,5),"Local entry depth provenance retained");
        Check(spec.Sample(.8,.4,out sampled)&&sampled.Exact,"Translated equal planes preserve every camera line");
        Check(spec.Cutoff(.5,.5,out distance)&&Close(distance,.1/7.0001),"Capture ellipsoid cutoff skips complete interior along capture eye ray");
        Check(PortalProjectionShaders.EllipsoidThresholdConstants(spec,128,128).Length*4==208,"Ellipsoid R32 threshold constant ABI 208 bytes");
        PortalSurfaceMap anisotropic;Check(PortalSurfaceMap.TryEllipsoid(destination,new Vector3D(4,3,1),out anisotropic),"Create triaxial capture shell");
        var anisotropicSpec=Spec(entry,exit,anisotropic,viewer,camera);
        Check(anisotropicSpec.Cutoff(.5,.5,out distance)&&Close(distance,.1/6.0001),"Anisotropic cutoff uses actual exit axis radius");
        Check(!Spec(entry,exit,anisotropic,viewer,MatrixD.CreateTranslation(20,0,-5)).Cutoff(.5,.5,out distance),"Capture rays missing ellipsoid fail closed");
        var internalCapture=Spec(entry,exit,anisotropic,viewer,destination);
        Check(internalCapture.Cutoff(.5,.5,out distance)&&Close(distance,.1/1.0001),"Inside ellipsoid eye chooses far exit crossing");
        var stretched=Spec(entry,Plane(destination,4,2),shell,MatrixD.CreateTranslation(1,0,5),camera,accuracy:PortalRayAccuracy.ApproximateShell);
        Check(stretched.Ray(.5,.5,out ray)&&Close(ray.Direction,Vector3D.Normalize(new Vector3D(-2,0,-5))),"UV differential transports anisotropic tangent scale");
        var offEye=MatrixD.CreateTranslation(1,0,-5);var strict=Spec(entry,exit,shell,viewer,offEye);
        Check(!strict.Sample(.5,.5,out sampled),"Reject off-line capture rather than claim exact parallax");
        var approximate=Spec(entry,exit,shell,viewer,offEye,accuracy:PortalRayAccuracy.ApproximateShell);
        Check(approximate.Sample(.5,.5,out sampled)&&!sampled.Exact,"Explicit shell approximation retains accuracy evidence");
        var nonlinearExit=Sphere(destination,2);var nonlinearShell=Sphere(destination,4);
        var nonlinearCamera=MatrixD.CreateWorld(destination.Translation,Vector3D.UnitZ,Vector3D.UnitY);
        var nonlinear=Spec(entry,nonlinearExit,nonlinearShell,viewer,nonlinearCamera,accuracy:PortalRayAccuracy.ApproximateShell);
        Check(nonlinear.Ray(.5,.5,out ray)&&Close(ray.Origin,new Vector3D(0,0,-8))&&Close(ray.Direction,Vector3D.UnitZ),"Plane-to-ellipsoid differential mapping");
        Check(nonlinear.Sample(.5,.5,out sampled)&&sampled.Exact&&Close(sampled.ImageUv.X,.5),"General shell sampling honours capture camera orientation");
        Check(nonlinear.Sample(.6,.5,out sampled)&&!sampled.Exact,"Nonlinear off-centre warp has explicit approximation evidence");
        var wrongBranch=Spec(entry,nonlinearExit,nonlinearShell,viewer,camera,accuracy:PortalRayAccuracy.ApproximateShell);
        Check(!wrongBranch.Sample(.5,.5,out sampled),"Approximate sampling cannot target the capture camera's entering shell branch");
        var behind=Spec(entry,exit,shell,viewer,MatrixD.CreateTranslation(0,0,-20));Check(!behind.Sample(.5,.5,out sampled),"Collinear camera beyond hit is reverse sight line");
        var stealth=Spec(sphere,sphere,Sphere(identity,4),viewer,viewer,PortalRayTransport.StealthWorldDirection);
        Check(stealth.Ray(.5,.5,out ray)&&Close(ray.Direction,-Vector3D.UnitZ)&&Close(ray.Origin,new Vector3D(0,0,2)),"Stealth retains actual viewer ray and skin origin");
        Check(stealth.Sample(.5,.5,out sampled)&&sampled.Exact&&Close(sampled.ShellDistance,6),"Stealth skips shell interior to far outgoing crossing");
        Check(!stealth.Ray(0,.5,out ray),"Far sphere skin branch is transparent");
        Check(!Spec(entry,exit,shell,viewer,camera,PortalRayTransport.StealthWorldDirection).Ray(.5,.5,out ray),"Stealth cannot silently teleport its origin");
        var sideViewer=MatrixD.CreateTranslation(5,0,0);var secondStealth=Spec(sphere,sphere,Sphere(identity,4),sideViewer,sideViewer,PortalRayTransport.StealthWorldDirection,image:PortalImageProjection.Angular);
        Check(secondStealth.Ray(.25,.5,out ray)&&Close(ray.Direction,-Vector3D.UnitX),"Second viewer changes outgoing stealth ray");
        var angular=Spec(entry,exit,shell,viewer,camera,image:PortalImageProjection.Angular);
        Check(angular.Sample(.5,.5,out sampled)&&(Close(sampled.ImageUv.X,0)||Close(sampled.ImageUv.X,1))&&Close(sampled.ImageUv.Y,.5),"Angular backward camera direction wraps at U seam");
        Check(!angular.Cutoff(.5,.5,out distance)&&PortalProjectionShaders.EllipsoidThresholdConstants(angular,128,128)==null,"Angular capture requires per-face perspective cutoff");
        MeshChecks(entry,viewer);
        PortalRayMapSpec bad;Check(!PortalRayMapSpec.TryCreate(0,0,entry,0,exit,0,entry,viewer,camera,ReverseProjection(),PortalRayTransport.Differential,PortalImageProjection.Angular,PortalRayAccuracy.ExactCaptureLine,1,out bad),"Capture shell must be ellipsoid");
        Check(!PortalRayMapSpec.TryCreate(0,0,entry,0,exit,0,shell,viewer,camera,MatrixD.CreatePerspectiveFieldOfView(1,1,.1,100),PortalRayTransport.Differential,PortalImageProjection.Perspective,PortalRayAccuracy.ExactCaptureLine,1,out bad),"Reject forward-Z capture metadata");
        Check(!PortalRayMapSpec.TryCreate(0,0,entry,0,exit,0,shell,viewer,camera,ReverseProjection(),(PortalRayTransport)42,PortalImageProjection.Perspective,PortalRayAccuracy.ExactCaptureLine,1,out bad),"Reject unknown transport");
        PortalRayPacket packet;string error;Check(PortalRayPacket.TryBuild(spec,8,8,out packet,out error)&&packet.ValidPixels==64&&packet.Spec.Generation==7,"Bounded complete per-viewer ray packet");
        var origins=packet.CopyOrigins();origins[3]=0;Check(packet.CopyOrigins()[3]==1,"Packet upload copies cannot mutate snapshot");
        Check(packet.CopyDirections()[3]>0,"Packet retains entry distance without remote depth output");
        Check(PortalProjectionShaders.GeneralConstants(packet,1,1).Length*4==160,"General compositor constant ABI 160 bytes");
        Check(PortalProjectionShaders.GeneralConstants(packet,float.NaN,1)==null,"Reject nonfinite colour controls");
        Check(!PortalRayPacket.TryBuild(spec,2049,2049,out packet,out error)&&packet==null,"Validated 2048 affine cap rejects oversized packet");
        Check(!PortalRayPacket.TryBuild(strict,8,8,out packet,out error)&&packet==null,"No strict valid rays cannot publish packet");
        LargeCoordinateChecks();
    }
    static void MeshChecks(PortalSurfaceMap entry,MatrixD viewer)
    {
        var points=new[]{new Vector3D(-1,-1,0),new Vector3D(1,-1,0),new Vector3D(-1,1,0),new Vector3D(-1,-1,-2),new Vector3D(1,-1,-2),new Vector3D(-1,1,-2)};
        var uv=new[]{new Vector2D(0,0),new Vector2D(1,0),new Vector2D(0,1),new Vector2D(0,0),new Vector2D(1,0),new Vector2D(0,1)};var indices=new[]{0,1,2,3,4,5};PortalSurfaceMap mesh;PortalSurfaceSample sample;
        Check(PortalSurfaceMap.TryMesh(MatrixD.Identity,points,uv,indices,out mesh)&&mesh.ChartCount==2,"Overlapping mesh UV islands use explicit charts");
        Check(mesh.Evaluate(.25,.25,0,out sample)&&Close(sample.Point,new Vector3D(-.5,-.5,0)),"Barycentric mesh mapping");
        Check(mesh.Evaluate(.25,.25,1,out sample)&&Close(sample.Point,new Vector3D(-.5,-.5,-2)),"Independent chart at identical UV");
        Check(!mesh.Evaluate(.9,.9,0,out sample),"UV outside declared triangle rejected");
        points[0]=new Vector3D(double.NaN);uv[0]=new Vector2D(double.NaN);indices[0]=-1;
        Check(mesh.Evaluate(.25,.25,0,out sample)&&Close(sample.Point,new Vector3D(-.5,-.5,0)),"Mesh declaration clones all caller arrays");
        PortalMappedRay ray;var spec=Spec(mesh,mesh,Sphere(MatrixD.Identity,4),viewer,viewer,PortalRayTransport.StealthWorldDirection,image:PortalImageProjection.Angular,entryChart:1,exitChart:1);
        Check(!spec.Ray(.25,.25,out ray),"Occluded mesh chart cannot double paint nearer chart");
        Check(!PortalSurfaceMap.TryMesh(MatrixD.Identity,new[]{Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitX},new[]{Vector2D.Zero,new Vector2D(1,0),new Vector2D(0,1)},new[]{0,1,2},out mesh),"Degenerate physical triangles rejected");
        Check(!PortalSurfaceMap.TryMesh(MatrixD.Identity,new[]{Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitY},new[]{Vector2D.Zero,new Vector2D(1,0),new Vector2D(.5,0)},new[]{0,1,2},out mesh),"Degenerate UV triangles rejected");
        Check(PortalSurfaceMap.TryMesh(MatrixD.Identity,new[]{Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitY},new[]{Vector2D.Zero,new Vector2D(0,1),new Vector2D(1,0)},new[]{0,1,2},out mesh)&&mesh.Evaluate(.2,.2,0,out sample)&&Close(sample.Normal,Vector3D.UnitZ),"Mirrored mesh UV retains authored outward winding");
    }
    static void LargeCoordinateChecks()
    {
        var entry=MatrixD.CreateWorld(new Vector3D(1e12,-1e12,1e12),Vector3D.Normalize(new Vector3D(1,0,-1)),Vector3D.UnitY);
        var exit=MatrixD.CreateWorld(new Vector3D(-1e12,1e12,-1e12),Vector3D.UnitX,Vector3D.UnitY);var viewer=entry;viewer.Translation+=entry.Right*2+entry.Up+entry.Backward*5;PortalCaptureSpec spec;string error;
        Check(PortalCaptureSpec.TryCreate(0,0,entry,2,exit,4,viewer,ReverseProjection(),128,128,out spec,out error),"Large rotated Cartesian snapshot");
        var expected=exit.Translation+exit.Right*4+exit.Up*2+exit.Backward*10;
        Check(Close(spec.VirtualPose.Translation,expected,.001),"Camera mapping subtracts double origin before rotation");
        Check(Close(spec.MapPoint(entry.Translation+entry.Up),exit.Translation+exit.Up*2,.001),"Point mapping avoids large affine inversion");
        var plane=Plane(entry);var shell=Sphere(entry,4);var same=Spec(plane,plane,shell,viewer,viewer,PortalRayTransport.StealthWorldDirection,image:PortalImageProjection.Angular);PortalRaySample hit;
        Check(same.Sample(.5,.5,out hit)&&hit.Exact,"General mappings retain large-coordinate viewer line");
    }
    static void ShaderChecks(string bin)
    {
        foreach(string dll in new[]{"VRage.Math","VRage.Render","VRage.Library","VRage","SharpDX","SharpDX.DXGI","SharpDX.Direct3D11","SharpDX.D3DCompiler","VRage.Render11"})AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin,dll+".dll"));
        var compiler=new PortalClipShaders(DirectCameraCaptureNative.FindType(null,"VRageRender.MyRender11").Assembly);
        var gpu=new PortalNativeGpu(DirectCameraCaptureNative.FindType(null,"VRageRender.MyRender11").Assembly,HeadlessCapture,action=>throw new Exception("Headless test must not enter GPU context"),action=>{},()=>1,()=>false);
        Check(gpu.ShaderBytes>100&&!gpu.Ready,"Installed compositor target/ray upload ABI validates without allocation");gpu.Dispose();
        Check(compiler.Inspect(compiler.CompileFixed(PortalProjectionShaders.GeneralComposite),false).Length==1,"Installed HLSL general shell-hit compositor compiles with colour target only");
        Check(compiler.Inspect(compiler.CompileFixed(PortalProjectionShaders.EllipsoidThreshold),false).Length==1,"Installed HLSL anisotropic reversed-Z capture threshold compiles");
        Check(compiler.Inspect(compiler.CompileFixed(PortalProjectionShaders.Composite),false).Length==1,"Existing Cartesian compositor retains compiler ABI");
        Check(compiler.Inspect(compiler.CompileFixed(PortalProjectionShaders.Threshold),false).Length==1,"Existing sphere threshold retains compiler ABI");
    }
    static bool HeadlessCapture(object target,int resolution,PortalRayMapSpec spec,out string error){error="Headless";return false;}
}
