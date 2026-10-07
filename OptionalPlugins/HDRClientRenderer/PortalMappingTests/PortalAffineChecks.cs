using HDRClientRenderer;
using VRageMath;
using F3=System.Numerics.Vector3;
using F4=System.Numerics.Vector4;
using FMatrix=System.Numerics.Matrix4x4;

internal static partial class Program
{
    static void AffineChecks()
    {
        foreach(double normalScale in new[]{.5,2d,-1d})
        {
            var descriptor=Descriptor();descriptor[5]=new[]{2d,3d,0d};descriptor[11]=new[]{6d,1d,0d};((double[])descriptor[17])[0]=normalScale;
            var entryPose=MatrixD.CreateWorld(new Vector3D(3,-2,7),Vector3D.Normalize(new Vector3D(.3,.1,-1)),Vector3D.UnitY);
            var exitPose=MatrixD.CreateWorld(new Vector3D(-4,3,-20),Vector3D.Normalize(new Vector3D(-1,.2,.4)),Vector3D.UnitY);
            descriptor[4]=Values(entryPose);descriptor[10]=descriptor[15]=Values(exitPose);descriptor[16]=new[]{8d,5d,6d};
            PortalProvider.Declaration declaration;string error;Check(PortalProvider.Declaration.TryParse(descriptor,out declaration,out error),"Affine rotated/asymmetric declaration");
            var eye=entryPose.Translation+entryPose.Right*.5+entryPose.Up*.2+entryPose.Backward*4;
            var viewer=MatrixD.CreateWorld(eye,entryPose.Translation-eye,entryPose.Up+entryPose.Right*.2);PortalRayMapSpec spec;
            Check(declaration.Snapshot(1,17,viewer,ReverseProjection(),out spec),"Affine rotated/off-centre virtual capture snapshot");
            PortalRayPacket packet;Check(PortalRayPacket.TryBuild(spec,64,48,out packet,out error)&&packet.IsAffine&&packet.TextureWidth==2&&packet.TextureHeight==2,"Exact planar mapping uses bounded2x2 raw coefficients");
            Check(packet.CopyOrigins().Length==16&&packet.CopyDirections().Length==16&&PortalProjectionShaders.GeneralConstants(packet,1,1)[39]==1,"Affine coefficient upload and mode flag have exact ABI");
            foreach(double u in new[]{0d,.117,.499,.823,1d})foreach(double v in new[]{0d,.251,.51,.917,1d})
            {
                var software=AffineShaderRay(packet,u,v);int x=Math.Min(packet.Width-1,(int)(u*packet.Width)),y=Math.Min(packet.Height-1,(int)(v*packet.Height));PortalMappedRay reference;
                Check(spec.Ray((x+.5)/packet.Width,(y+.5)/packet.Height,out reference),"Dense reference ray exists at point-sampled logical texel");
                Check(Vector3D.Distance(software.CaptureOrigin,reference.CaptureOrigin)<2e-5&&Vector3D.Distance(software.CaptureDirection,reference.CaptureDirection)<4e-7,"SoftwareHLSL affine origin/raw-normalized direction equals dense CPU ray");
                PortalRaySample cpu;Vector2D gpuUv;bool cpuValid=spec.Sample(reference,out cpu),gpuValid=ShaderSample(packet,software,out gpuUv);
                Check(cpuValid==gpuValid,"SoftwareHLSL and dense CPU agree on strict shell branch/FOV sample validity");
                if(cpuValid)Check(Vector2D.Distance(cpu.ImageUv,gpuUv)<2e-5,"SoftwareHLSL and dense CPU perspective samples agree");
            }
        }
        var origin=Plane(MatrixD.Identity);var destination=MatrixD.CreateTranslation(0,0,-10);var exit=Plane(destination);var shell=Sphere(destination);
        foreach(var accuracy in new[]{PortalRayAccuracy.ExactCaptureLine,PortalRayAccuracy.ApproximateShell})
        {
            var spec=Spec(origin,exit,shell,MatrixD.CreateTranslation(0,0,5),MatrixD.CreateTranslation(.02,0,-5),accuracy:accuracy);PortalRayPacket packet;string error;
            bool accepted=PortalRayPacket.TryBuild(spec,64,64,out packet,out error);
            if(accuracy==PortalRayAccuracy.ExactCaptureLine)Check(!accepted,"Strict off-line planar capture still fails closed through dense fallback");
            else
            {
                Check(accepted&&packet.IsAffine&&!packet.CoverageExact&&packet.ValidPixels<=5,"Approximate affine field reports conservative positive coverage lower bound");
                foreach(double u in new[]{.1,.4,.8})foreach(double v in new[]{.2,.6,.9})
                {var ray=AffineShaderRay(packet,u,v);Vector2D uv;PortalRaySample cpu;Check(ShaderSample(packet,ray,out uv)&&spec.Sample(ray,out cpu)&&Vector2D.Distance(uv,cpu.ImageUv)<1e-5,"Approximate affine GPU shell sampling matches scalar CPU oracle");}
            }
        }
        var eyeOnPlane=Spec(origin,exit,shell,MatrixD.Identity,MatrixD.CreateTranslation(0,0,-5));PortalRayPacket rejected;string failure;
        Check(!PortalRayPacket.TryBuild(eyeOnPlane,64,64,out rejected,out failure),"Coplanar eye has no positive entry crossing and never creates affine packet");
        // Plane crosses a tiny shell nearu=.315: no corner/centre probe sees it,
        // but a bounded dense fallback must preserve the narrow valid aperture.
        var tinyPose=MatrixD.CreateTranslation(-.375,0,0);var narrow=Spec(origin,origin,Sphere(tinyPose,.055),MatrixD.CreateTranslation(0,0,5),MatrixD.CreateTranslation(0,0,5));
        Check(PortalRayPacket.TryBuild(narrow,64,64,out rejected,out failure)&&!rejected.IsAffine&&rejected.ValidPixels>0&&rejected.CoverageExact,"Invalid affine probes fall back to dense verification rather than falsely declare all-miss");
        var forwardShell=Sphere(MatrixD.CreateTranslation(0,0,20));var allMiss=Spec(origin,origin,forwardShell,MatrixD.CreateTranslation(0,0,5),MatrixD.CreateTranslation(0,0,5));
        Check(!PortalRayPacket.TryBuild(allMiss,64,64,out rejected,out failure),"True all-miss plane remains unavailable after probe and dense validation");
        var nonPlanar=Spec(origin,Sphere(destination),Sphere(destination,4),MatrixD.CreateTranslation(0,0,5),MatrixD.CreateWorld(destination.Translation,Vector3D.UnitZ,Vector3D.UnitY),accuracy:PortalRayAccuracy.ApproximateShell);
        Check(PortalRayPacket.TryBuild(nonPlanar,64,64,out rejected,out failure)&&!rejected.IsAffine&&rejected.TextureWidth==64&&rejected.TextureHeight==64,"Nonlinear surfaces retain dense point packet path");
        Check(!PortalRayPacket.TryBuild(nonPlanar,1024,1024,out rejected,out failure),"Affine optimization never bypasses logical pixel/work limits");
    }
    static F3 V(float[] a,int offset)=>new F3(a[offset],a[offset+1],a[offset+2]);
    static Vector3D D(F3 v)=>new Vector3D(v.X,v.Y,v.Z);
    static PortalMappedRay AffineShaderRay(PortalRayPacket packet,double surfaceU,double surfaceV)
    {
        int x=Math.Min(packet.Width-1,(int)(surfaceU*packet.Width)),y=Math.Min(packet.Height-1,(int)(surfaceV*packet.Height));float u=(x+.5f)/packet.Width,v=(y+.5f)/packet.Height;
        var o=packet.CopyOrigins();var d=packet.CopyDirections();var origin=V(o,0)+u*V(o,4)+v*V(o,8);var direction=F3.Normalize(V(d,0)+u*V(d,4)+v*V(d,8));var orientation=packet.Spec.CapturePose.GetOrientation();
        return new PortalMappedRay(packet.Spec.CapturePose.Translation+Vector3D.TransformNormal(D(origin),orientation),Vector3D.TransformNormal(D(direction),orientation),D(origin),D(direction),1);
    }
    static bool ShaderSample(PortalRayPacket packet,PortalMappedRay ray,out Vector2D uv)
    {
        uv=Vector2D.Zero;var c=PortalProjectionShaders.GeneralConstants(packet,1,1);var origin=new F3((float)ray.CaptureOrigin.X,(float)ray.CaptureOrigin.Y,(float)ray.CaptureOrigin.Z);var direction=F3.Normalize(new F3((float)ray.CaptureDirection.X,(float)ray.CaptureDirection.Y,(float)ray.CaptureDirection.Z));
        var delta=origin-V(c,16);var right=V(c,20);var up=V(c,24);var backward=V(c,28);var o=new F3(F3.Dot(delta,right),F3.Dot(delta,up),F3.Dot(delta,backward));var d=new F3(F3.Dot(direction,right),F3.Dot(direction,up),F3.Dot(direction,backward));
        float a=F3.Dot(d,d),b=F3.Dot(o,d),cc=F3.Dot(o,o)-1;var perpendicular=F3.Cross(o,d);float disc=a-F3.Dot(perpendicular,perpendicular);if(!float.IsFinite(disc)||disc<0||a<=0)return false;
        float root=MathF.Sqrt(disc),q=-b-(b>=0?root:-root),far=MathF.Abs(q)<1e-20f?-b/a:MathF.Max(q/a,cc/q);if(!float.IsFinite(far)||far<=1e-6)return false;var hit=origin+direction*far;float distance=hit.Length();if(!float.IsFinite(distance)||distance<1e-6)return false;
        var camera=hit/distance;var normalRay=new F3(F3.Dot(camera,right),F3.Dot(camera,up),F3.Dot(camera,backward));if(F3.Dot(o+d*far,normalRay)<-1e-7)return false;
        if(c[35]<.5f&&(F3.Cross(origin,direction).Length()>c[38]||F3.Dot(hit,direction)<=0))return false;
        var projection=new FMatrix(c[0],c[1],c[2],c[3],c[4],c[5],c[6],c[7],c[8],c[9],c[10],c[11],c[12],c[13],c[14],c[15]);var clip=F4.Transform(new F4(hit,1),projection);if(clip.W<=0)return false;float su=.5f+.5f*clip.X/clip.W,sv=.5f-.5f*clip.Y/clip.W;
        if(!float.IsFinite(su)||!float.IsFinite(sv)||su<0||sv<0||su>1||sv>1)return false;uv=new Vector2D(su,sv);return true;
    }
}
