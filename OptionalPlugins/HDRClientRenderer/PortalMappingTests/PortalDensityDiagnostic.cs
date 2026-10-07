#if PORTAL_DENSITY_DIAGNOSTIC
using System;
using System.IO;
using System.Runtime.Loader;
using HDRClientRenderer;
using VRageMath;

// Read-only production diagnosis. Runs CPU math only, without native resources.
// -p:DefineConstants=PORTAL_DENSITY_DIAGNOSTIC -p:StartupObject=PortalDensityDiagnostic
internal static class PortalDensityDiagnostic
{
    static int checks;
    static void Require(bool condition,string reason){checks++;if(!condition)throw new Exception(reason);}
    static MatrixD Projection(){var p=MatrixD.Identity;p.M33=0;p.M34=-1;p.M43=.1;p.M44=0;return p;}
    static PortalRayMapSpec Spec(Vector3D eye)
    {
        PortalSurfaceMap entry=null,exit=null,shell=null;PortalRayMapSpec spec;var destination=MatrixD.CreateTranslation(0,0,-10);
        Require(PortalSurfaceMap.TryPlane(MatrixD.Identity,2,2,out entry)&&PortalSurfaceMap.TryPlane(destination,2,2,out exit)&&PortalSurfaceMap.TryEllipsoid(destination,new Vector3D(2),out shell),"Diagnostic surfaces");
        Require(PortalRayMapSpec.TryCreate(1,1,entry,0,exit,0,shell,MatrixD.CreateTranslation(eye),MatrixD.CreateTranslation(eye+destination.Translation),Projection(),PortalRayTransport.Differential,PortalImageProjection.Perspective,PortalRayAccuracy.ExactCaptureLine,1,out spec),"Diagnostic identity portal spec");return spec;
    }
    static void Density(int requested,double eyeDistance,double lateral)
    {
        var eye=new Vector3D(lateral,0,eyeDistance);var spec=Spec(eye);var view=new PortalPrimaryView(MatrixD.CreateTranslation(eye),Projection(),1,1920,1080,1);PortalResolutionPlan plan;
        Require(PortalResolution.TryPlan(spec,view,requested,requested,0,ClientPixelBudget.MaxJobPixels,out plan),"Visible portal plan");
        // For this axis-aligned plane the local projected UV Jacobian is exact:
        // dx/du = viewportWidth * M11 * physicalWidth / (2*eyeDistance).
        double fullSpan=1920/eyeDistance,texelsPerScreenPixel=plan.OutputWidth/fullSpan;
        double visibleTexels=plan.VisibleWidth*texelsPerScreenPixel;
        double finiteDifference=ScreenX(spec.Entry,.500001,eye)-ScreenX(spec.Entry,.5,eye);
        Require(Math.Abs(finiteDifference/.000001-fullSpan)<1e-5,"Projected UV derivative agrees with finite difference");
        if(eyeDistance<=.2)Require(texelsPerScreenPixel<.22,"Close-up output undersamples despite maximum native bucket");
        PortalRayMapSpec cropped;Require(spec.WithProjection(plan.Projection,plan.ProbeU,plan.ProbeV,out cropped),"Crop valid");
        foreach(double u in new[]{.48,.5,.52})
        {
            PortalMappedRay before,after;Require(spec.Ray(u,.5,out before)&&cropped.Ray(u,.5,out after)&&Vector3D.Distance(before.Direction,after.Direction)<1e-12,"Cropping changes capture coordinates, not outgoing scene rays");
        }
        foreach(double offset in new[]{-.001,0,.001})
        {
            PortalMappedRay ray;PortalRaySample cpu=default;Require(cropped.Ray(plan.ProbeU+offset,plan.ProbeV,out ray)&&cropped.Sample(ray,out cpu),"Visible cropped ray sample");
            var hit=ray.CaptureOrigin+ray.CaptureDirection*cpu.ShellDistance;var p=cropped.Projection;
            var floatProjection=new System.Numerics.Matrix4x4((float)p.M11,(float)p.M12,(float)p.M13,(float)p.M14,(float)p.M21,(float)p.M22,(float)p.M23,(float)p.M24,(float)p.M31,(float)p.M32,(float)p.M33,(float)p.M34,(float)p.M41,(float)p.M42,(float)p.M43,(float)p.M44);
            // Software counterpart of HLSL mul(float4(hit,1),HDRProjection),
            // including float upload, row-vector multiplication and UV divide.
            var clip=System.Numerics.Vector4.Transform(new System.Numerics.Vector4((float)hit.X,(float)hit.Y,(float)hit.Z,1),floatProjection);
            var gpuUv=new Vector2D(.5+.5*clip.X/clip.W,.5-.5*clip.Y/clip.W);
            Require(Vector2D.Distance(gpuUv,cpu.ImageUv)<1e-6,"CPU and software float-HLSL cropped image coordinates agree during walk and lateral clipping");
        }
        Console.WriteLine($"request={requested}, eye=({lateral:F2},0,{eyeDistance:F2})m: visible={plan.VisibleWidth:F1}px, full-UV-span={fullSpan:F1}px, output={plan.OutputWidth}, bucket={plan.BucketSize}, capture={plan.CaptureSize}, visible-information={visibleTexels:F1} texels, density={texelsPerScreenPixel:F3} texels/display-pixel, each-information-texel={1/texelsPerScreenPixel:F2}px");
    }
    static double ScreenX(PortalSurfaceMap entry,double u,Vector3D eye)
    {PortalSurfaceSample p;Require(entry.Evaluate(u,.5,0,out p),"Entry evaluation");return 960*(p.Point.X-eye.X)/eye.Z;}
    static void PriorPose(double oldDistance,double newDistance,double deltaX,double featureX)
    {
        // Identity translation portal: feature 30m beyond exit is equivalent to
        // a virtual feature z=-30 in the entry coordinate system. Old capture's
        // physical entry intersection is displayed by the next primary view.
        const double beyond=30;double oldEntryX=featureX*oldDistance/(oldDistance+beyond);
        double displayed=960*(oldEntryX-deltaX)/newDistance;
        double current=960*(featureX-deltaX)/(newDistance+beyond);
        double error=displayed-current;
        if(deltaX>0)Require(Math.Abs(error)>25,"One-frame baked prior pose creates measurable lateral parallax error");
        else Require(Math.Abs(error)>3,"One-frame baked prior pose creates measurable near/far apparent zoom error");
        Console.WriteLine($"prior-pose display: eyeZ {oldDistance:F2}->{newDistance:F2}m, lateral delta={deltaX:F3}m, landmarkX={featureX:F2}m / 30m beyond exit: displayed={displayed:F2}px, current-correct={current:F2}px, temporal-error={error:F2}px");
    }
    static void Main()
    {
        const string bin=@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64";
        AssemblyLoadContext.Default.Resolving+=(context,name)=>{var path=Path.Combine(bin,name.Name+".dll");return File.Exists(path)?context.LoadFromAssemblyPath(path):null;};
        foreach(int request in new[]{512,4096})foreach(double distance in new[]{2d,1d,.5,.2})foreach(double lateral in new[]{0d,.6})Density(request,distance,lateral);
        PriorPose(1,1,.03,0);PriorPose(.2,.2,.03,0);PriorPose(1,.9,0,1);PriorPose(.2,.18,0,1);
        Console.WriteLine($"PASS: {checks} close-clipping density/crop and prior-pose CPU proof checks. No production changes or GPU/game work.");
    }
}
#endif
