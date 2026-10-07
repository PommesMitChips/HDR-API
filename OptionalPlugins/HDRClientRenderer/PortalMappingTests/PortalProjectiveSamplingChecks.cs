using HDRClientRenderer;
using VRageMath;

internal static partial class Program
{
    static void ProjectiveSamplingChecks()
    {
        int witnessed=0;
        foreach(double distance in new[]{.2,.5,2d,8d})foreach(double lateral in new[]{0d,.4})foreach(double height in new[]{-.2,.3})foreach(bool rotated in new[]{false,true})
        {
            var raw=NativeDescriptor();var entry=rotated?MatrixD.CreateRotationY(.35)*MatrixD.CreateRotationX(.17):MatrixD.Identity;
            entry.Translation=new Vector3D(1e9,-2e9,3e9);var exit=MatrixD.CreateRotationY(-.7)*MatrixD.CreateRotationZ(.2);exit.Translation=entry.Translation+new Vector3D(30,5,-40);
            raw[4]=Values(entry);raw[10]=Values(exit);raw[15]=Values(exit);raw[5]=new[]{2d,3d,0d};raw[11]=new[]{4d,1.5,0d};raw[16]=new[]{10d,10d,10d};((double[])raw[17])[0]=.7;
            PortalProvider.Declaration declaration;string error;Check(PortalProvider.Declaration.TryParse(raw,out declaration,out error),"Projective affine declaration parses");
            var eye=entry.Translation+entry.Backward*distance+entry.Right*lateral+entry.Up*height;
            var viewer=MatrixD.CreateWorld(eye,entry.Translation-eye,entry.Up);PortalRayMapSpec original;
            Check(declaration.Snapshot(4,101,viewer,ReverseProjection(),out original),"Projective virtual camera snapshot");
            var primary=new PortalPrimaryView(viewer,ReverseProjection(),12,1920,1080,4);PortalResolutionPlan plan;
            Check(PortalResolution.TryPlan(original,primary,4096,2730,0,ClientPixelBudget.MaxJobPixels,out plan),"Projective near/far lateral/height clipped crop plan");PortalRayMapSpec cropped;
            Check(original.WithProjection(plan.Projection,plan.ProbeU,plan.ProbeV,out cropped),"Projective capture retains matching crop");PortalProjectiveSampling sampling;
            Check(PortalProjectiveSampling.TryCreate(cropped,primary,plan.CaptureSize,plan.CaptureSize,.7f,1.3f,out sampling),"Paired-plane direct source sampling proof builds");
            var constants=sampling.CopyConstants();Check(constants.Length*4==PortalProjectiveSampling.ConstantBytes&&constants[16]==.7f&&constants[17]==1.3f&&constants[18]==plan.CaptureSize&&constants[19]==plan.CaptureSize,"Projective80-byte rowmajor matrix/settings ABI");constants[0]=float.NaN;Check(!float.IsNaN(sampling.CopyConstants()[0]),"Projective constants caller cannot mutate immutable sampling proof");
            foreach(double du in new[]{-.001,0,.001})foreach(double dv in new[]{-.001,0,.001})
            {
                PortalSurfaceSample point;PortalRaySample cpu=default;Vector2D direct;double u=plan.ProbeU+du,v=plan.ProbeV+dv;
                Check(cropped.Entry.Evaluate(u,v,0,out point)&&cropped.Sample(u,v,out cpu),"Visible clipped entry samples remain exact affine rays");
                Check(sampling.TryImageUv(point.Point-eye,101,4,12,out direct)&&Vector2D.Distance(direct,cpu.ImageUv)<2e-6,"Direct projective entry fragment agrees with full CPU shell-ray sampling without artificial zoom");
                var f=sampling.CopyConstants();var m=new System.Numerics.Matrix4x4(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12],f[13],f[14],f[15]);var rel=point.Point-eye;
                var clip=System.Numerics.Vector4.Transform(new System.Numerics.Vector4((float)rel.X,(float)rel.Y,(float)rel.Z,1),m);var gpu=new Vector2D(.5+.5*clip.X/clip.W,.5-.5*clip.Y/clip.W);
                Check(Vector2D.Distance(gpu,direct)<2e-6,"Software float-HLSL primary-relative projective projection matches CPU double");witnessed++;
                Check(sampling.ContainsEntryPoint(new Vector3D((float)rel.X,(float)rel.Y,(float)rel.Z),primary)&&!sampling.ContainsEntryPoint(rel+entry.Backward*.02,primary),"Projective native float vertices belong to authorized entry plane and exclude an unrelated parallel quad");
                Check(!sampling.TryImageUv(rel,102,4,12,out direct)&&!sampling.TryImageUv(rel,101,5,12,out direct)&&!sampling.TryImageUv(rel,101,4,13,out direct),"Mixed old/new colour generation, epoch or captured window frame fails publication pairing");
            }
            var laterEye=eye+entry.Right*.01;var later=new PortalPrimaryView(MatrixD.CreateWorld(laterEye,entry.Translation-laterEye,entry.Up),ReverseProjection(),13,1920,1080,4);PortalProjectiveSampling rebased;
            Check(sampling.TryRebase(later,out rebased)&&rebased.Matches(101,4,12),"Primary coordinate rebase keeps immutable captured image identity");PortalSurfaceSample centre;cropped.Entry.Evaluate(plan.ProbeU,plan.ProbeV,0,out centre);Vector2D oldUv,newUv;
            Check(sampling.TryImageUv(centre.Point-eye,101,4,12,out oldUv)&&rebased.TryImageUv(centre.Point-laterEye,101,4,12,out newUv)&&Vector2D.Distance(oldUv,newUv)<1e-7,"Reused captured image rebasing changes only primary coordinate origin, never its crop/zoom");
            Check(!sampling.TryRebase(new PortalPrimaryView(viewer,ReverseProjection(),11,1920,1080,4),out rebased)&&!sampling.TryRebase(new PortalPrimaryView(viewer,ReverseProjection(),13,1920,1080,5),out rebased),"Prior-frame and different-device coordinate snapshots are rejected");
        }
        Check(witnessed==288,"Projective proof covers32 rotated/nonaxis near/far/lateral/height fixtures");
        // A tilted plane's perspective texture coordinate is a rational map.
        // Linear endpoint interpolation cannot replace homogeneous projection.
        var tilt=MatrixD.CreateRotationY(.6);var a=Vector3D.Transform(new Vector3D(-1,0,0),tilt);var b=Vector3D.Transform(new Vector3D(1,0,0),tilt);
        double left=.5+.5*a.X/(2-a.Z),right=.5+.5*b.X/(2-b.Z),linear=(left+right)*.5;
        Check(Math.Abs(linear-.5)>.05,"Affine UV scale/bias alone cannot sample a tilted plane's raw perspective capture");
        PortalProjectiveSampling invalid;var simple=Spec(Plane(MatrixD.Identity),Plane(MatrixD.CreateTranslation(0,0,-10)),Sphere(MatrixD.CreateTranslation(0,0,-10)),MatrixD.CreateTranslation(0,0,5),MatrixD.CreateTranslation(0,0,-5));
        Check(!PortalProjectiveSampling.TryCreate(simple,new PortalPrimaryView(MatrixD.CreateTranslation(.01,0,5),ReverseProjection(),1,1920,1080,2),512,512,1,1,out invalid),"Initial sampling proof cannot mix old captured eye and fresh primary pose");
        PortalRayMapSpec wrongEye;Check(PortalRayMapSpec.TryCreate(2,7,simple.Entry,0,simple.Exit,0,simple.Shell,simple.ViewerPose,MatrixD.CreateTranslation(1,0,-5),simple.Projection,simple.Transport,simple.ImageProjection,simple.Accuracy,1,out wrongEye)&&!PortalProjectiveSampling.Eligible(wrongEye),"Direct source path rejects off-line capture eye that would create strict geometric gaps");
        var outside=Spec(simple.Entry,Plane(MatrixD.CreateTranslation(0,0,-10),5,5),simple.Shell,simple.ViewerPose,simple.CapturePose);
        Check(!PortalProjectiveSampling.Eligible(outside),"Direct raw sampling falls back when exit patch leaves convex shell and needs per-ray start-distance validity");
        ProjectiveProviderChecks();
    }
    static void ProjectiveProviderChecks()
    {
        long frame=1;var budget=new ClientPixelBudget(()=>new ClientPixelBudget.Frame(frame,1920,1080));budget.Configure(new ClientRenderSettings());
        var world=new PortalWorld{Descriptor=NativeDescriptor()};var native=new PortalNative{World=world,ProjectiveEnabled=true,PrimaryViewer=MatrixD.CreateTranslation(0,0,.2)};var provider=new PortalProvider(world,native);provider.SetBudget(budget);
        provider.Request(1,2,"screen","screen");provider.RenderPending();var near=provider.Request(1,2,"screen","screen");
        Check(near!=null&&near.Projective&&near.Width==2048&&near.Height==2048&&near.TextureUv==new Vector4(0,0,1,1)&&native.Captures==1&&native.Compositions==0&&native.PrivateTargets==1,"Close native pane publishes actual2k cropped source directly, bypassing fullUV grid and staging");
        Check(near.Publication.Sampling.Matches(near.Publication.Generation,native.Epoch,near.Publication.ViewFrame)&&native.ProjectiveSamples[near.Target].Matches(native.Pixels[near.Target],native.Epoch,near.Publication.ViewFrame),"Raw front pixels and per-target immutable projective proof commit together");
        Check(budget.Spent==2048*2048&&provider.Reason.Contains("projective")&&provider.Reason.Contains("capture age")&&!provider.Reason.Contains("view age"),"Direct capture charges actual square source work and reports capture age without implying measured display latency");
        var historical=near.Publication;world.Time=.02;native.PrimaryViewer=MatrixD.CreateTranslation(.01,.01,.2);frame++;provider.Prune();provider.RenderPending();
        Check(provider.Valid(near,1,2,"screen")&&near.Publication.Generation>historical.Generation&&native.ProjectiveSamples[near.Target]==near.Publication.Sampling,"Same front lease atomically advances its own image/projection publication without old/new mixing");
        provider.Dispose();native.Drain();
        world=new PortalWorld{Descriptor=NativeDescriptor()};native=new PortalNative{World=world,ProjectiveEnabled=true,PrimaryViewer=MatrixD.CreateTranslation(0,0,2),ViewWidth=640,ViewHeight=360,PresentationWidth=1920,PresentationHeight=1080};provider=new PortalProvider(world,native);
        provider.Request(1,2,"screen","screen");provider.RenderPending();var displayDensity=provider.Request(1,2,"screen","screen");
        Check(displayDensity.Width==1024&&provider.Reason.Contains("viewport 640x360 / presentation 1920x1080"),"Native source density uses physical presentation pixels despite smaller dynamic-resolution viewport");provider.Dispose();native.Drain();
        // Mode selection can replace the interpretation of a stable front name.
        // Failed capture must not transfer ownership or revoke its old pixels.
        world=new PortalWorld{Descriptor=NativeDescriptor()};native=new PortalNative{World=world};provider=new PortalProvider(world,native);provider.Request(1,2,"screen","screen");provider.RenderPending();var oldComposed=provider.Request(1,2,"screen","screen");var composedPixels=native.Pixels[oldComposed.Target];int oldStamp=native.Stamps[oldComposed.Texture];
        world.Time=.02;native.ProjectiveEnabled=true;native.FailCapture=true;provider.Prune();provider.RenderPending();
        Check(provider.Valid(oldComposed,1,2,"screen")&&native.Pixels[oldComposed.Target]==composedPixels&&native.Stamps[oldComposed.Texture]==oldStamp,"Failed composed-to-raw capture preserves old pixels, evidence and exact retirement ownership stamp");
        native.FailCapture=false;world.Time=.04;provider.Prune();provider.RenderPending();var rawReplacement=provider.Request(1,2,"screen","screen");
        Check(rawReplacement.Projective&&!ReferenceEquals(rawReplacement,oldComposed)&&ReferenceEquals(rawReplacement.Target,oldComposed.Target)&&oldComposed.Publication==null&&native.ProjectiveSamples[rawReplacement.Target]==rawReplacement.Publication.Sampling,"Successful same-name mode publication atomically revokes previous interpretation and commits new source proof");provider.Release(oldComposed);native.Drain();Check(provider.Valid(rawReplacement,1,2,"screen")&&native.Pixels[rawReplacement.Target]==rawReplacement.Publication.Generation,"Old composed retirement cannot tombstone newly committed raw owner");provider.Dispose();native.Drain();
        world=new PortalWorld{Descriptor=NativeDescriptor()};world.Descriptor[11]=new[]{5d,5d,0d};native=new PortalNative{World=world,ProjectiveEnabled=true};provider=new PortalProvider(world,native);
        provider.Request(1,2,"screen","screen");provider.RenderPending();var partial=provider.Request(1,2,"screen","screen");
        Check(partial!=null&&!partial.Projective&&partial.Publication.Sampling==null&&partial.Reduced&&native.Compositions==1&&native.ProjectiveSamples.Count==0,"Strict exit outside shell retains per-ray partial-gap compositor and never registers a raw projective image");provider.Dispose();native.Drain();
        // Every retained target keeps its own capture proof across layout offers.
        world=new PortalWorld{Descriptor=NativeDescriptor()};native=new PortalNative{World=world,ProjectiveEnabled=true,PrimaryViewer=MatrixD.CreateTranslation(0,0,8)};provider=new PortalProvider(world,native);provider.Request(1,2,"screen","screen");provider.RenderPending();var a=provider.Request(1,2,"screen","screen");var oldA=a.Publication;
        world.Time=.02;native.PrimaryViewer=MatrixD.CreateTranslation(0,0,2);provider.Prune();provider.RenderPending();var b=Offered(provider);
        Check(a.Bucket==256&&b.Bucket==1024&&provider.Valid(a,1,2,"screen")&&native.Pixels[a.Target]==oldA.Generation&&native.ProjectiveSamples[a.Target]==oldA.Sampling&&native.ProjectiveSamples[b.Target]==b.Publication.Sampling,"Held A and offered B retain independent matching pixels/poses while native source density changes");
        var oldB=b.Publication;world.Time=.04;native.PrimaryViewer=MatrixD.CreateTranslation(.01,0,2);provider.Prune();provider.RenderPending();
        Check(ReferenceEquals(a.Publication,oldA)&&b.Publication.Generation>oldB.Generation&&native.ProjectiveSamples[a.Target].ViewFrame==oldA.ViewFrame&&native.ProjectiveSamples[b.Target].ViewFrame==b.Publication.ViewFrame,"Refreshing offered B never rebases or relabels held A to B capture metadata");
        world.Time=.06;native.FailPublish=true;provider.Prune();provider.RenderPending();var fallback=provider.Request(1,2,"screen","screen");
        Check(ReferenceEquals(fallback,a)&&a.Publication==oldA&&b.Publication==null&&!native.ProjectiveSamples.ContainsKey(b.Target)&&native.ProjectiveSamples[a.Target]==oldA.Sampling,"Failed raw B copy unregisters its private sampling and preserves held A image/proof for reacquisition");
        native.FailPublish=false;world.Time=.08;provider.Prune();provider.RenderPending();var replacement=provider.Request(1,2,"screen","screen");
        Check(!ReferenceEquals(replacement,b)&&replacement.Projective&&native.ProjectiveSamples[replacement.Target]==replacement.Publication.Sampling,"Failed raw front recovery cannot resurrect old offered proof");provider.Release(b);Check(provider.Valid(replacement,1,2,"screen"),"Revoked offered release does not cancel new raw publication");
        provider.Release(a);provider.Dispose();native.Drain();
        world=new PortalWorld{Descriptor=NativeDescriptor()};native=new PortalNative{World=world,ProjectiveEnabled=true,PrimaryViewer=MatrixD.CreateTranslation(0,0,8)};provider=new PortalProvider(world,native);provider.Request(1,2,"screen","screen");provider.RenderPending();a=provider.Request(1,2,"screen","screen");oldA=a.Publication;
        world.Time=.02;native.PrimaryViewer=MatrixD.CreateTranslation(0,0,2);provider.Prune();provider.RenderPending();b=Offered(provider);
        world.Time=.04;provider.Prune();native.DuringPublish=()=>world.Time=.291;provider.RenderPending();
        Check(b.Publication==null&&!native.ProjectiveSamples.ContainsKey(b.Target)&&ReferenceEquals(provider.Request(1,2,"screen","screen"),a)&&a.Publication==oldA,"Post-copy immutable proof expiration unregisters raw B and reacquires unchanged A under fresh authorization");
        native.ProjectiveEnabled=false;Check(!provider.Valid(a,1,2,"screen"),"Raw source evidence is invalid when its private projective shader proof becomes unavailable");provider.Prune();provider.RenderPending();var composed=provider.Request(1,2,"screen","screen");
        Check(composed!=null&&!composed.Projective&&a.Publication==oldA&&native.Compositions==1&&native.ProjectiveSamples[a.Target]==oldA.Sampling&&!native.ProjectiveSamples.ContainsKey(composed.Target),"FullUV publication keeps distinct held A raw proof intact while registering B with its own composed interpretation");
        provider.Dispose();native.Drain();
    }
}
