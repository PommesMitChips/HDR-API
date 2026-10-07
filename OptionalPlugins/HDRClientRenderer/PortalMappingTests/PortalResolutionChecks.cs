using System.Reflection;
using HDRClientRenderer;
using VRageMath;

internal static partial class Program
{
    static object[] NativeDescriptor(double rate=60)
    {var raw=Descriptor();raw[18]=new[]{4096,2730,0,0};((double[])raw[17])[1]=rate;return raw;}
    static PortalProvider.Frame Offered(PortalProvider provider)
    {var slots=(Array)typeof(PortalProvider).GetField("slots",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(provider);var slot=slots.GetValue(0);return (PortalProvider.Frame)slot.GetType().GetField("Completed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(slot);}
    static void ResolutionChecks()
    {
        var world=new PortalWorld{Descriptor=NativeDescriptor()};var native=new PortalNative{World=world,PrimaryViewer=MatrixD.CreateTranslation(.2,.1,8),AutoFrame=false,ViewFrame=1};var provider=new PortalProvider(world,native);
        provider.Request(1,2,"screen","screen");world.ForbidLocalView=true;provider.RenderPending();world.ForbidLocalView=false;var first=provider.Request(1,2,"screen","screen");
        Check(first!=null&&first.Publication.ViewerPose==native.PrimaryViewer.Value&&first.Publication.ViewerPose!=world.Viewer&&first.Publication.ViewFrame==1,"Capture and mapping use fresh render primary view rather than game request pose");
        Check(!provider.Reason.Contains("Game camera API"),"Render-stage capture never reads game camera APIs");
        Check(first.PhysicalWidth==256&&first.PhysicalHeight==256&&first.TextureUv.X>0&&first.TextureUv.Z<1,"Small visible portal selects stable native bucket with guarded active UV rectangle");
        Check(native.CapturedSpec.Projection.M11>ReverseProjection().M11,"Planar source capture crops its pinhole to portal extent rather than full monitor FOV");
        int count=native.Captures;provider.RenderPending();Check(native.Captures==count,"One primary frame never causes multiple captures");
        native.ViewFrame=2;native.PrimaryViewer=MatrixD.CreateTranslation(.3,.2,8);world.Time=1d/60;provider.Prune();provider.RenderPending();
        Check(native.Captures==count+1&&first.Publication.ViewerPose==native.PrimaryViewer.Value&&first.Publication.ViewFrame==2,"Autonomous render cadence refreshes latest eye without endpoint reacquisition");
        native.ViewAvailable=false;world.Time+=1d/60;provider.RenderPending();Check(native.Captures==count+1&&provider.Valid(first,1,2,"screen"),"Unavailable primary snapshot preserves completed image without stale-eye capture");native.ViewAvailable=true;
        Check(provider.Reason.Contains("requested 4096x2730")&&provider.Reason.Contains("information")&&provider.Reason.Contains("capture")&&provider.Reason.Contains("capture age"),"Status discloses requested versus chosen resolution and capture-frame age without claiming presentation latency");
        provider.Dispose();native.Drain();
        foreach(var cadence in new[]{new[]{60,60},new[]{30,60},new[]{120,144}})
        {
            world=new PortalWorld{Descriptor=NativeDescriptor(cadence[0])};native=new PortalNative{World=world,AutoFrame=false};provider=new PortalProvider(world,native);provider.Request(1,2,"screen","screen");
            for(int tick=0;tick<cadence[1];tick++){world.Time=(double)tick/cadence[1];native.ViewFrame=tick;provider.Prune();provider.RenderPending();}
            Check(Math.Abs(native.Captures-cadence[0])<=1,"Phase-anchored cadence reaches requested "+cadence[0]+" Hz on "+cadence[1]+" Hz main frames without threshold halving");
            count=native.Captures;world.Time=20;native.ViewFrame++;provider.Request(1,2,"screen","screen");provider.RenderPending();provider.RenderPending();Check(native.Captures==count+1,"Long pause drops missed capture debt with no catch-up burst");provider.Dispose();native.Drain();
        }
        // Acquired A survives offered B. Returning to A before the slow consumer
        // accepts B reuses A's stable handle and retires only the unused offer.
        world=new PortalWorld{Descriptor=NativeDescriptor()};native=new PortalNative{World=world,PrimaryViewer=MatrixD.CreateTranslation(0,0,8)};provider=new PortalProvider(world,native);provider.Request(1,2,"screen","screen");provider.RenderPending();var a=provider.Request(1,2,"screen","screen");var uvA=a.TextureUv;var pixelsA=native.Pixels[a.Target];
        world.Time=.02;native.PrimaryViewer=MatrixD.CreateTranslation(0,0,2);provider.Prune();provider.RenderPending();var b=Offered(provider);
        Check(b!=null&&!ReferenceEquals(a,b)&&b.Bucket==1024&&provider.Valid(a,1,2,"screen")&&native.Pixels[a.Target]==pixelsA,"New adaptive bucket offer preserves acquired old frame and pixels before handoff");
        world.Time=.04;native.PrimaryViewer=MatrixD.CreateTranslation(0,0,8);provider.Prune();provider.RenderPending();
        Check(ReferenceEquals(Offered(provider),a)&&provider.Valid(a,1,2,"screen")&&a.TextureUv==uvA&&b.Publication==null,"A to B to A before acquisition reuses A without UV mutation or blanking");
        world.Time=.06;native.PrimaryViewer=MatrixD.CreateTranslation(0,0,2);provider.Prune();provider.RenderPending();b=provider.Request(1,2,"screen","screen");Check(b!=null&&!ReferenceEquals(a,b)&&provider.Valid(a,1,2,"screen")&&provider.Valid(b,1,2,"screen"),"Both handoff leases remain authorized until old acquired evidence is released");
        var bRect=b.TextureUv;world.Time=.08;native.PrimaryViewer=MatrixD.CreateTranslation(.01,0,2);provider.Prune();provider.RenderPending();
        Check(ReferenceEquals(Offered(provider),b)&&b.TextureUv==bRect&&provider.Valid(b,1,2,"screen"),"Refreshing claimed B while old A is held preserves claim and immutable same-bucket ROI");
        world.Time=.1;native.PrimaryViewer=MatrixD.CreateTranslation(0,0,8);provider.Prune();count=native.Captures;provider.RenderPending();Check(native.Captures==count,"Refreshed claimed B cannot be retired as unclaimed when layout changes before old A release");
        provider.Release(a);provider.RenderPending();var newA=Offered(provider);Check(newA!=null&&newA.Bucket==256&&!ReferenceEquals(newA,a),"Released A reacquisition gets fresh resource eligibility even with stable wrapper");native.Drain();
        Check(newA.Publication!=null&&native.Pixels[newA.Target]==newA.Publication.Generation&&provider.Valid(b,1,2,"screen"),"Delayed old-A retirement cannot clear reacquired A or cancel held B");
        provider.Release(b);provider.Dispose();native.Drain();
        FailedOfferChecks();BudgetAvailabilityChecks();CropChecks();
    }
    static void FailedOfferChecks()
    {
        var world=new PortalWorld{Descriptor=NativeDescriptor()};var native=new PortalNative{World=world,PrimaryViewer=MatrixD.CreateTranslation(0,0,8)};var provider=new PortalProvider(world,native);
        provider.Request(1,2,"screen","screen");provider.RenderPending();var acquired=provider.Request(1,2,"screen","screen");var publication=acquired.Publication;var pixels=native.Pixels[acquired.Target];var rectangle=acquired.TextureUv;
        world.Time=.02;native.PrimaryViewer=MatrixD.CreateTranslation(0,0,2);provider.Prune();provider.RenderPending();var offered=Offered(provider);
        Check(offered!=null&&!ReferenceEquals(offered,acquired),"Failed-copy actor has distinct acquired A and offered B");
        world.Time=.04;native.PrimaryViewer=MatrixD.CreateTranslation(.01,0,2);provider.Prune();native.FailPublish=true;provider.RenderPending();
        Check(offered.Publication==null&&!provider.Valid(offered,1,2,"screen")&&provider.Valid(acquired,1,2,"screen"),"Failed offered-front copy permanently revokes B while untouched acquired A remains valid");
        var fallback=provider.Request(1,2,"screen","screen");
        Check(ReferenceEquals(fallback,acquired)&&ReferenceEquals(acquired.Publication,publication)&&native.Pixels[acquired.Target]==pixels&&acquired.TextureUv==rectangle,"Next mod acquisition receives held A with unchanged pixels, publication and UV after B copy failure");
        native.FailPublish=false;world.Time=.06;provider.Prune();provider.RenderPending();var recovered=provider.Request(1,2,"screen","screen");
        Check(recovered!=null&&!ReferenceEquals(recovered,offered)&&!ReferenceEquals(recovered,acquired)&&offered.Publication==null,"Successful later B capture creates fresh evidence without reviving failed offered lease");
        provider.Release(offered);Check(provider.Valid(acquired,1,2,"screen")&&provider.Valid(recovered,1,2,"screen"),"Release of failed offered B cannot cancel retained A or replacement B");native.Drain();
        Check(native.Pixels[recovered.Target]==recovered.Publication.Generation,"Delayed failed-B retirement stamp cannot clear its newly acquired replacement front");
        provider.Release(acquired);provider.Dispose();native.Drain();
        world=new PortalWorld{Descriptor=NativeDescriptor()};native=new PortalNative{World=world,PrimaryViewer=MatrixD.CreateTranslation(0,0,8)};provider=new PortalProvider(world,native);
        provider.Request(1,2,"screen","screen");provider.RenderPending();acquired=provider.Request(1,2,"screen","screen");publication=acquired.Publication;pixels=native.Pixels[acquired.Target];rectangle=acquired.TextureUv;
        world.Time=.02;native.PrimaryViewer=MatrixD.CreateTranslation(0,0,2);provider.Prune();provider.RenderPending();offered=Offered(provider);
        world.Time=.04;provider.Prune();native.DuringPublish=()=>world.Time=.291;provider.RenderPending();
        Check(offered.Publication==null&&!provider.Valid(offered,1,2,"screen")&&provider.Valid(acquired,1,2,"screen"),"Authorization expiring during successful copy revokes offered B without publishing beyond immutable proof");
        fallback=provider.Request(1,2,"screen","screen");
        Check(ReferenceEquals(fallback,acquired)&&ReferenceEquals(acquired.Publication,publication)&&native.Pixels[acquired.Target]==pixels&&acquired.TextureUv==rectangle,"Fresh game authorization reacquires untouched A after post-copy proof expiration instead of blanking valid held geometry");
        provider.Prune();provider.RenderPending();recovered=provider.Request(1,2,"screen","screen");
        Check(recovered!=null&&!ReferenceEquals(recovered,offered)&&!ReferenceEquals(recovered,acquired)&&offered.Publication==null,"Fresh bounded proof permits distinct B recovery after post-copy expiration without reviving revoked B");
        provider.Release(offered);provider.Release(acquired);provider.Dispose();native.Drain();
    }
    static void BudgetAvailabilityChecks()
    {
        long frame=1;var budget=new ClientPixelBudget(()=>new ClientPixelBudget.Frame(frame,1000,1000));Check(budget.AvailablePixels==1000000,"Fresh frame exposes full unconfigured viewport budget");
        Check(budget.TrySpend("test",ClientPixelBudget.Kind.Capture,100000)&&budget.AvailablePixels==900000,"Same-frame remaining budget subtracts actual spend");frame++;Check(budget.AvailablePixels==1000000,"New-frame available budget never subtracts prior-frame spend");frame=0;Check(budget.AvailablePixels==0,"Regressing native frame cannot expose a new grant");
        frame=3;budget.Configure(new ClientRenderSettings());Check(budget.AvailablePixels==int.MaxValue&&budget.Request("full2k",ClientPixelBudget.Kind.Capture,8388608)&&budget.TrySpend("full2k",ClientPixelBudget.Kind.Capture,8388608),"Configured unlimited preference admits validated full2k source plus output job");Check(!budget.Request("unsafe",ClientPixelBudget.Kind.Capture,8388609),"Individual job safety cap remains distinct from viewport defaults");
        PortalProvider.Declaration declaration;string error;Check(PortalProvider.Declaration.TryParse(NativeDescriptor(),out declaration,out error),"Native4096 request is declarative quality ceiling");PortalRayMapSpec spec;declaration.Snapshot(1,1,MatrixD.CreateTranslation(0,0,1),ReverseProjection(),out spec);
        var primary=new PortalPrimaryView(MatrixD.CreateTranslation(0,0,1),ReverseProjection(),1,3840,2160,1);PortalResolutionPlan plan;
        Check(PortalResolution.TryPlan(spec,primary,4096,2730,0,ClientPixelBudget.MaxJobPixels,out plan)&&plan.BucketSize==2048&&plan.CaptureSize==2048&&plan.Reduced,"Native demand validates effective2k cap and respects unlimited safety maximum");
        Check(PortalResolution.TryPlan(spec,primary,4096,2730,0,1280*720,out plan)&&plan.Pixels<=1280*720&&plan.BucketSize<2048&&plan.Reduced,"Explicit small viewport budget proportionally lowers quality instead of stalling forever");
        var portrait=PortalResolution.Crop(ReverseProjection(),-.2,-.6,.4,.8);Check(PortalProjection.ComplementaryProjection(portrait)&&portrait.M43==ReverseProjection().M43&&portrait.M34==-1,"Off-axis crop leaves native depth and homogeneous w unchanged");
    }
    static void CropChecks()
    {
        var raw=NativeDescriptor();raw[5]=new[]{2d,3d,0d};raw[11]=new[]{6d,1d,0d};((double[])raw[17])[0]=.5;PortalProvider.Declaration declaration;string error;PortalProvider.Declaration.TryParse(raw,out declaration,out error);
        var viewer=MatrixD.CreateWorld(new Vector3D(.4,.2,5),new Vector3D(-.4,-.2,-5),Vector3D.Normalize(new Vector3D(.2,1,0)));PortalRayMapSpec original;Check(declaration.Snapshot(1,1,viewer,ReverseProjection(),out original),"Rotated anisotropic portal pinhole remains accepted");
        var primary=new PortalPrimaryView(viewer,ReverseProjection(),1,1920,1080,1);PortalResolutionPlan plan;Check(PortalResolution.TryPlan(original,primary,4096,2730,0,8388608,out plan),"Sheared affine pinhole receives bounded crop plan");PortalRayMapSpec cropped;Check(original.WithProjection(plan.Projection,plan.ProbeU,plan.ProbeV,out cropped),"Crop preserves invertible noncanonical projection");
        foreach(double u in new[]{.1,.4,.8})foreach(double v in new[]{.1,.5,.9})
        {
            PortalMappedRay a,b;Check(original.Ray(u,v,out a)&&cropped.Ray(u,v,out b)&&Vector3D.Distance(a.Direction,b.Direction)<1e-12&&Vector3D.Distance(a.Origin,b.Origin)<1e-12,"Crop changes sample coordinates without changing transported scene ray geometry");
        }
        Check(Math.Abs(cropped.Projection.M14)>1e-8||Math.Abs(cropped.Projection.M24)>1e-8,"Accepted crop fixture contains homogeneous-w shear requiring full inverse atmosphere reconstruction");
        PortalRayPacket packet;Check(PortalRayPacket.TryBuild(cropped,2044,1363,out packet,out error)&&packet.IsAffine&&packet.TextureWidth==2,"Large validated affine information grid never expands per-pixel CPU packet");
        Check(PortalProjectionShaders.EllipsoidThresholdConstants(cropped,2048,2048)!=null,"Bounded2k capture has valid4M linear cutoff constants");
    }
}
