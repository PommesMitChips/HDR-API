using HDRClientRenderer;
using VRageMath;

internal static partial class Program
{
    sealed class PortalWorld : PortalProvider.IWorld
    {
        internal bool Allowed=true,Live=true,ForbidLocalView;internal double Time;internal MatrixD Viewer=MatrixD.CreateTranslation(0,0,5);internal object[] Descriptor=Descriptor();
        public double Now=>Time;
        public bool Active(long anchor,long caller,string source,string screen)=>Live;
        public bool Authorized(long anchor,long caller,string source)=>Allowed;
        public bool TryDescriptor(long anchor,long caller,string source,out object[] descriptor){descriptor=Descriptor;return true;}
        public bool TryLocalView(out MatrixD viewer,out MatrixD projection){if(ForbidLocalView)throw new Exception("Game camera API queried from renderer");viewer=Viewer;projection=ReverseProjection();return true;}
    }
    sealed class PortalNative : PortalProvider.INative
    {
        internal long Epoch=4;internal Action DuringCapture,DuringCompose,DuringPublish;internal int Captures,Compositions,Publications;internal PortalRayMapSpec CapturedSpec;
        internal bool FailCapture,FailCompose,FailPublish,ProjectiveEnabled;
        internal PortalWorld World;internal MatrixD? PrimaryViewer;internal int ViewWidth=1920,ViewHeight=1080,PresentationWidth,PresentationHeight;internal long ViewFrame;internal bool AutoFrame=true,ViewAvailable=true;
        internal readonly Dictionary<string,int> Stamps=new();
        internal readonly Dictionary<object,long> Pixels=new();
        internal readonly Dictionary<object,Vector2I> Sizes=new();internal readonly Dictionary<string,int> Creates=new();
        internal readonly Dictionary<string,object> Targets=new();internal readonly Queue<Action> Cleanup=new();
        public bool Ready=>true;public string Reason=>"Fake installed adapter";public long DeviceEpoch=>Epoch;
        public bool ProjectiveReady=>ProjectiveEnabled;
        internal readonly Dictionary<object,PortalProjectiveSampling> ProjectiveSamples=new();
        public bool TryView(out PortalPrimaryView view){if(!ViewAvailable){view=null;return false;}view=new PortalPrimaryView(PrimaryViewer??World.Viewer,ReverseProjection(),AutoFrame?++ViewFrame:ViewFrame,ViewWidth,ViewHeight,Epoch,PresentationWidth,PresentationHeight);return true;}
        internal int PrivateTargets=>Targets.Keys.Count(name=>!name.StartsWith("HDR_ClientPortal_"));
        public object CreateTarget(string name,int width,int height)
        {
            int expected=PortalFrontTargetPool.PhysicalSize(name);
            Check(width==height&&PortalResolution.IsBucket(width)&&(expected==0||expected==width),"Native target uses a bounded fixed bucket or square capture");
            if(name.StartsWith("HDR_ClientPortal_")&&Targets.TryGetValue(name,out var retained)){Check(Sizes[retained]==new Vector2I(width,height),"Billboard front wrapper dimensions never change");Stamps[name]=Stamps[name]+1;return retained;}
            Check(!Targets.ContainsKey(name),"Private native slot names never reused before retirement");var target=new object();Targets.Add(name,target);Pixels.Add(target,0);Sizes.Add(target,new Vector2I(width,height));Creates[name]=Creates.TryGetValue(name,out var count)?count+1:1;Stamps[name]=Stamps.TryGetValue(name,out var stamp)?stamp+1:1;return target;
        }
        public void DestroyTarget(string name)
        {
            if(!Targets.TryGetValue(name,out var target))return;
            if(name.StartsWith("HDR_ClientPortal_")){Pixels[target]=0;return;} // Transparent tombstone preserves manager/material lookup.
            Targets.Remove(name);Pixels.Remove(target);Sizes.Remove(target);
        }
        public Action FrontRetirement(string name){var target=Targets[name];int stamp=Stamps[name];return ()=>{if(Targets.TryGetValue(name,out var current)&&ReferenceEquals(current,target)&&Stamps[name]==stamp){Pixels[target]=0;ProjectiveSamples.Remove(target);}};}
        public bool Capture(object target,int resolution,PortalRayMapSpec spec,out string error){Check(Pixels.ContainsKey(target),"Capture never uses destroyed device-epoch texture object");Check(PortalResolution.IsBucket(resolution)&&Sizes[target]==new Vector2I(resolution,resolution),"Source capture uses actual planned square resolution");Captures++;CapturedSpec=spec;Pixels[target]=spec.Generation;var callback=DuringCapture;DuringCapture=null;callback?.Invoke();error=FailCapture?"Simulated capture failure":null;return !FailCapture;}
        public bool Compose(object capture,object output,PortalRayPacket packet,float saturation,float brightness,out string error){Compositions++;Check(packet.Spec==CapturedSpec,"Native composition binds its matching captured ray spec");Check(packet.OutputWidth+2*packet.Gutter<=Sizes[output].X&&packet.OutputHeight+2*packet.Gutter<=Sizes[output].Y,"Active composition fits its immutable bucket");Pixels[output]=CapturedSpec.Generation;var callback=DuringCompose;DuringCompose=null;callback?.Invoke();error=FailCompose?"Simulated partial GPU composition failure":null;return !FailCompose;}
        public bool Publish(object staging,object front,int activeWidth,int activeHeight,int gutter,out string error){Publications++;Check(!ReferenceEquals(staging,front),"Published front never aliases composition staging");Check(Sizes[front]==Sizes[staging]&&activeWidth+2*gutter<=Sizes[front].X&&activeHeight+2*gutter<=Sizes[front].Y,"Publication region and guard stay inside compatible physical buckets");Pixels[front]=Pixels[staging];var callback=DuringPublish;DuringPublish=null;callback?.Invoke();error=FailPublish?"Simulated copy submitted before publication failure":null;if(!FailPublish)ProjectiveSamples.Remove(front);return !FailPublish;}
        public bool PublishProjective(object capture,object front,PortalProjectiveSampling sample,out string error){Publications++;Check(ProjectiveReady&&sample.Matches(Pixels[capture],Epoch,sample.ViewFrame)&&Sizes[capture]==Sizes[front],"Projective raw image publication pairs exact source generation, device and physical dimensions");Pixels[front]=Pixels[capture];ProjectiveSamples[front]=sample;var callback=DuringPublish;DuringPublish=null;callback?.Invoke();error=FailPublish?"Simulated raw copy submitted before publication failure":null;return !FailPublish;}
        internal void DeviceEnd(){Epoch++;Targets.Clear();Pixels.Clear();Sizes.Clear();ProjectiveSamples.Clear();}
        public void EnqueueCleanup(Action cleanup){Cleanup.Enqueue(cleanup);}
        internal void Drain(){while(Cleanup.Count>0)Cleanup.Dequeue()();}
    }
    static double[] Values(MatrixD m)=>new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
    static object[] Descriptor()=>new object[]{1,0,0,0,Values(MatrixD.Identity),new[]{2d,2d,0d},Array.Empty<double>(),Array.Empty<int>(),Array.Empty<double>(),0,Values(MatrixD.CreateTranslation(0,0,-10)),new[]{2d,2d,0d},Array.Empty<double>(),Array.Empty<int>(),Array.Empty<double>(),Values(MatrixD.CreateTranslation(0,0,-10)),new[]{2d,2d,2d},new[]{1d,30d,1d,1d},new[]{64,64,0,0}};
    static void ProviderChecks()
    {
        PortalProvider.Declaration declaration;string error;var descriptor=Descriptor();
        Check(PortalProvider.Declaration.TryParse(descriptor,out declaration,out error),"Authorized numeric descriptor parses");
        ((double[])descriptor[5])[0]=double.NaN;
        PortalRayMapSpec spec;Check(declaration.Snapshot(4,1,MatrixD.CreateTranslation(0,0,5),ReverseProjection(),out spec),"Declaration cloned caller arrays before later mutation");
        Check(!PortalProvider.Declaration.TryParse(descriptor,out declaration,out error),"Nonfinite portal descriptor rejected");
        descriptor=Descriptor();descriptor[6]=new object();Check(!PortalProvider.Declaration.TryParse(descriptor,out declaration,out error),"Caller code/objects cannot enter native mapping descriptor");
        descriptor=Descriptor();((int[])descriptor[18])[0]=4097;Check(!PortalProvider.Declaration.TryParse(descriptor,out declaration,out error),"Native quality request ceiling remains bounded at4096");
        descriptor=Descriptor();((double[])descriptor[11])[0]=4;
        Check(PortalProvider.Declaration.TryParse(descriptor,out declaration,out error)&&declaration.Snapshot(4,2,MatrixD.CreateTranslation(1,0,5),ReverseProjection(),out spec),"Anisotropic affine portal derives virtual capture projection");
        PortalRaySample sample;Check(spec.Sample(.7,.4,out sample)&&sample.Exact,"Anisotropic plane mapping matches one pinhole over off-centre samples");
        PortalSurfaceMap ellipsoid;PortalSurfaceSample surface=default;
        Check(PortalSurfaceMap.TryEllipsoid(MatrixD.Identity,new Vector3D(4,2,1),true,out ellipsoid)&&ellipsoid.Evaluate(.625,.4,0,out surface),"Radial ellipsoid declaration");
        double lon=(.625-.5)*2*Math.PI,lat=(.5-.4)*Math.PI;var radial=new Vector3D(-Math.Sin(lon)*Math.Cos(lat),Math.Sin(lat),Math.Cos(lon)*Math.Cos(lat));
        Check(Vector3D.Cross(surface.Point,radial).Length()<1e-9,"Ellipsoid UV uses existing screen radial sight-line mapping");
        Check(PortalSurfaceMap.TryEllipsoid(MatrixD.Identity,new Vector3D(4,2,1),false,out ellipsoid)&&ellipsoid.Evaluate(.625,.4,0,out surface)&&surface.Point.X>0,"Outside ellipsoid longitude handedness matches screen renderer");
        descriptor=Descriptor();descriptor[3]=descriptor[9]=2;descriptor[5]=descriptor[11]=new[]{0d,0d,0d};descriptor[6]=new[]{-1d,-1d,0d,1d,-1d,0d,-1d,1d,0d};descriptor[12]=((double[])descriptor[6]).Clone();descriptor[7]=descriptor[13]=new[]{0,1,2};descriptor[8]=descriptor[14]=new[]{0d,0d,1d,0d,0d,1d};
        Check(PortalProvider.Declaration.TryParse(descriptor,out declaration,out error)&&declaration.Snapshot(4,3,MatrixD.CreateTranslation(0,0,5),ReverseProjection(),out spec)&&spec.Sample(.25,.25,out sample)&&sample.Exact,"Single-triangle UV mesh provider derives exact affine camera path");
        descriptor=Descriptor();descriptor[1]=1;descriptor[9]=0;descriptor[10]=Values(MatrixD.Identity);descriptor[15]=Values(MatrixD.Identity);descriptor[16]=new[]{4d,4d,4d};
        Check(PortalProvider.Declaration.TryParse(descriptor,out declaration,out error)&&declaration.Snapshot(4,4,MatrixD.CreateTranslation(0,0,5),ReverseProjection(),out spec)&&spec.Sample(.6,.5,out sample)&&sample.Exact,"Callable stealth skin snapshot retains exact viewer sight lines");
        var world=new PortalWorld();var native=new PortalNative { World=world };var provider=new PortalProvider(world,native);
        Check(provider.Request(1,2,"screen","screen")==null,"Unrendered source stays unavailable");provider.RenderPending();var frame=provider.Request(1,2,"screen","screen");
        Check(frame!=null&&frame.Texture=="HDR_ClientPortal_0_N256"&&provider.Valid(frame,1,2,"screen"),"Successful capture/composition publishes callable authorized bucket frame");
        Check(native.CapturedSpec.Epoch==native.Epoch,"Portal spec epoch matches native registry/device epoch");
        world.Viewer.Translation+=Vector3D.UnitX*.1;world.Time+=.04;
        Check(ReferenceEquals(provider.Request(1,2,"screen","screen"),frame)&&provider.Valid(frame,1,2,"screen"),"Viewer motion retains complete prior-pose image with its honest stamp");
        var capturedViewer=world.Viewer;var initialPublication=frame.Publication;
        native.DuringCapture=()=>{world.Viewer.Translation+=Vector3D.UnitX*.1;provider.Request(1,2,"screen","screen");};provider.RenderPending();var second=provider.Request(1,2,"screen","screen");
        var secondPublication=second.Publication;
        Check(second!=null&&secondPublication.ViewerPose==capturedViewer&&secondPublication.Generation==native.CapturedSpec.Generation,"Request during capture cannot mutate published pose/generation");
        Check(second.ViewerPose!=world.Viewer,"Prior-pose retention remains explicitly stamped");
        Check(ReferenceEquals(frame,second)&&frame.Generation==1&&initialPublication.ViewerPose!=secondPublication.ViewerPose,"Successful publication retains resource lease and swaps immutable pixel metadata");
        if(!ReferenceEquals(frame,second))provider.Release(frame);Check(provider.Valid(second,1,2,"screen"),"Mod reference-equal acquisition never releases the retained live lease");
        world.Time+=.04;provider.Request(1,2,"screen","screen");int before=native.Compositions;
        native.DuringCapture=()=>{world.Descriptor=Descriptor();((double[])world.Descriptor[17])[3]=.5;provider.Request(1,2,"screen","screen");};provider.RenderPending();
        Check(native.Compositions==before&&provider.Request(1,2,"screen","screen")==null,"Descriptor change during capture cancels stale composition/publication");
        provider.RenderPending();var third=provider.Request(1,2,"screen","screen");Check(third!=null&&third.Declaration.Brightness==.5,"Pending replacement captures its own immutable declaration");
        provider.Release(second);Check(!ReferenceEquals(second,third)&&second.Publication==null&&provider.Valid(third,1,2,"screen"),"Releasing revoked configuration lease cannot cancel replacement lease");
        world.Allowed=false;Check(!provider.Valid(third,1,2,"screen")&&provider.Request(1,2,"screen","screen")==null,"Authorization revocation clears provider evidence");native.Drain();Check(native.PrivateTargets==0&&native.Targets.Count==1&&native.Pixels[native.Targets[third.Texture]]==0,"Revoked source retires private targets and leaves transparent billboard front");
        world.Allowed=true;provider.Request(1,2,"screen","screen");native.DuringCapture=()=>provider.Cancel(1,2,"screen");before=native.Compositions;provider.RenderPending();
        Check(native.Compositions==before&&provider.Request(1,2,"screen","screen")==null,"Cancel during capture prevents publication and slot reuse before cleanup");native.Drain();
        provider.Request(1,2,"screen","screen");provider.RenderPending();third=provider.Request(1,2,"screen","screen");native.Epoch++;
        Check(!provider.Valid(third,1,2,"screen")&&provider.Request(1,2,"screen","screen")==null,"Device epoch change rejects old GPU image evidence");provider.NewEpoch();int resetCaptureCount=native.Captures;provider.RenderPending();Check(native.Captures==resetCaptureCount,"Epoch reset waits for queued resource retirement before reuse");native.Drain();provider.RenderPending();
        Check(native.CapturedSpec.Epoch==native.Epoch,"After reset new captures stamp the current native epoch");provider.Dispose();native.Drain();Check(native.PrivateTargets==0&&native.Targets.Count>=1&&native.Targets.Count<=PortalProvider.MaxSources&&native.Pixels.Values.All(value=>value==0),"Dispose retires private resources while retaining bounded transparent front lookup");
        BudgetChecks();
        PublicationChecks();
        RetainedLeaseChecks();
        FrontLifetimeChecks();
        AffineChecks();
        ResolutionChecks();
        ProjectiveSamplingChecks();
        NativeFloatBoundaryChecks();
        world=new PortalWorld();native=new PortalNative { World=world };provider=new PortalProvider(world,native);provider.Request(1,2,"first","first");provider.Request(1,2,"second","second");provider.RenderPending();world.Viewer.Translation+=Vector3D.UnitX*.1;provider.Request(1,2,"first","first");provider.RenderPending();Check(provider.Request(1,2,"second","second")!=null,"Continuous viewer updates on first source cannot starve second portal");provider.Dispose();native.Drain();
        world=new PortalWorld();native=new PortalNative { World=world };provider=new PortalProvider(world,native);Check(!provider.HasDemand&&!provider.PendingWork,"Undeclared portal creates no render demand");provider.Request(1,2,"screen","screen");Check(provider.HasDemand&&provider.PendingWork,"Authorized visible source creates bounded render demand");provider.ClearWorld();int departedCount=native.Captures;provider.RenderPending();Check(!provider.HasDemand&&!provider.PendingWork&&native.Captures==departedCount,"World departure cannot rerender old authorization-bearing declarations");native.Drain();provider.Dispose();native.Drain();
    }
    static void NativeFloatBoundaryChecks()
    {
        foreach(double radius in new[]{.05,2d,250d})foreach(double ratio in new[]{2d,100d,8192d})foreach(double fraction in new[]{0d,.5,.999999})
        {
            double eye=radius*ratio,x=radius/Math.Sqrt(eye*eye-radius*radius)*fraction;var ray=Vector3D.Normalize(new Vector3D(x,0,-1));var centre=new Vector3D(0,0,-eye);double authoredFar;
            Check(PortalProjection.FarIntersection(centre,ray,radius,out authoredFar),"Physical authored near-tangent far boundary exists");Vector3D inflated;double guard;
            if(eye>1e6){Check(!PortalProjectionShaders.NativeShell(centre,new Vector3D(radius),out inflated,out guard),"Native coordinate upload cap rejects oversized eye offset");continue;}
            Check(PortalProjectionShaders.NativeShell(centre,new Vector3D(radius),out inflated,out guard),"Native boundary stays inside bounded conditioning policy radius="+radius+" ratio="+ratio);
            var d=System.Numerics.Vector3.Normalize(new System.Numerics.Vector3((float)ray.X,(float)ray.Y,(float)ray.Z));var c=new System.Numerics.Vector3(0,0,(float)-eye);var cross=System.Numerics.Vector3.Cross(d,c);float r=(float)inflated.X;
            float discriminant=r*r-System.Numerics.Vector3.Dot(cross,cross);Check(discriminant>0,"Conservative float shell retains authored grazing aperture");
            float far=System.Numerics.Vector3.Dot(d,c)+MathF.Sqrt(discriminant);float cutoffDepth=.1f/(-d.Z*(far+(float)guard));float boundaryDepth=.1f/-(float)(ray.Z*authoredFar);
            Check(boundaryDepth>cutoffDepth,"Float reversed-Z cutoff rejects authored boundary despite distance/tangent rounding");
        }
    }
    static void PublicationChecks()
    {
        var world=new PortalWorld();var native=new PortalNative { World=world };var provider=new PortalProvider(world,native);provider.Request(1,2,"screen","screen");provider.RenderPending();var first=provider.Request(1,2,"screen","screen");var front=native.Targets[first.Texture];long pixels=native.Pixels[front];int commits=native.Publications;
        world.Time+=.04;world.Viewer.Translation+=Vector3D.UnitX*.1;provider.Request(1,2,"screen","screen");native.FailCompose=true;provider.RenderPending();
        Check(native.Pixels[front]==pixels&&native.Publications==commits&&provider.Valid(first,1,2,"screen"),"Partial composition failure preserves valid front pixels and stamp");native.FailCompose=false;
        provider.Request(1,2,"screen","screen");native.DuringCompose=()=>{world.Descriptor=Descriptor();((double[])world.Descriptor[17])[3]=.75;provider.Request(1,2,"screen","screen");};provider.RenderPending();
        Check(native.Pixels[front]==pixels&&native.Publications==commits,"Configuration changes after staging write never touch published front");
        provider.RenderPending();var second=provider.Request(1,2,"screen","screen");Check(second!=null&&native.Pixels[front]==second.Publication.Generation,"Successful front commit publishes matching pixel generation");
        world.Time+=.04;world.Viewer.Translation+=Vector3D.UnitX*.1;provider.Request(1,2,"screen","screen");native.FailPublish=true;provider.RenderPending();
        Check(!provider.Valid(second,1,2,"screen")&&second.Publication==null&&provider.Request(1,2,"screen","screen")==null,"Copy failure after front write cannot leave old frame evidence valid");native.FailPublish=false;provider.RenderPending();
        var recovery=provider.Request(1,2,"screen","screen");Check(recovery!=null&&!ReferenceEquals(second,recovery)&&recovery.Generation!=second.Generation&&!provider.Valid(second,1,2,"screen"),"Successful recovery creates distinct lease eligibility and never resurrects failed-copy evidence");provider.Release(second);Check(provider.Valid(recovery,1,2,"screen"),"Release of failed-copy evidence leaves recovered lease live");
        var beforeReset=native.Targets[recovery.Texture];native.DeviceEnd();provider.Request(1,2,"screen","screen");provider.RenderPending();var afterReset=provider.Request(1,2,"screen","screen");
        Check(afterReset!=null&&afterReset.DeviceEpoch==native.Epoch&&!ReferenceEquals(beforeReset,native.Targets[afterReset.Texture]),"Unchanged-size source recreates generated targets after manager device-end removal");
        world.Time+=.04;provider.Request(1,2,"screen","screen");int captures=native.Captures;world.Allowed=false;provider.Prune();provider.RenderPending();
        Check(!provider.HasDemand&&native.Captures==captures,"Game-thread prune revokes authorization before pending native capture");native.Drain();provider.Dispose();native.Drain();
        world=new PortalWorld();native=new PortalNative { World=world };provider=new PortalProvider(world,native);provider.Request(1,2,"screen","screen");world.Time=.3;provider.RenderPending();Check(native.Captures==0,"Expired immutable authorization proof never starts GPU capture");provider.Prune();provider.Request(1,2,"screen","screen");provider.RenderPending();Check(native.Captures==1,"Fresh game-thread authorization proof permits pending capture");provider.Dispose();native.Drain();
    }
    static void RetainedLeaseChecks()
    {
        var world=new PortalWorld();var native=new PortalNative { World=world };var provider=new PortalProvider(world,native);provider.Request(1,2,"screen","screen");provider.RenderPending();var cached=provider.Request(1,2,"screen","screen");var historical=cached.Publication;long eligibility=cached.Generation;var front=native.Targets[cached.Texture];int acquisitions=0;
        // Reproduce a 60 Hz cached Draw/valid loop with 6 Hz geometry acquisition
        // and 30 Hz native publications. New pixel frames must not blank the
        // consumer while it waits for its independent acquisition deadline.
        for(int tick=1;tick<=60;tick++)
        {
            world.Time=tick/60d;world.Viewer.Translation+=Vector3D.UnitX*.001;provider.Prune();
            if(tick%2==0){provider.Request(1,2,"screen","screen");provider.RenderPending();}
            Check(provider.Valid(cached,1,2,"screen"),"Cached 6 Hz consumer stays valid across faster native publications, tick="+tick);
            var publication=cached.Publication;Check(publication!=null&&publication.Generation==native.Pixels[front],"Retained lease metadata always identifies current front pixels, tick="+tick);
            if(tick%10==0)
            {
                var replacement=provider.Request(1,2,"screen","screen");acquisitions++;
                if(!ReferenceEquals(cached,replacement))provider.Release(cached);
                Check(ReferenceEquals(cached,replacement)&&replacement.Generation==eligibility,"Replacement acquisition reuses stable evidence and eligibility marker");cached=replacement;
            }
        }
        Check(acquisitions==6&&native.Publications==31,"Regression exercised multiple native publications between each cached acquisition");
        Check(!ReferenceEquals(historical,cached.Publication)&&historical.ViewerPose!=cached.Publication.ViewerPose&&historical.Generation<cached.Publication.Generation,"Old publication snapshots remain immutable while lease follows newest pixels");
        var beforeFailure=cached.Publication;world.Time+=.04;world.Viewer.Translation+=Vector3D.UnitX*.01;provider.Request(1,2,"screen","screen");native.FailCompose=true;provider.RenderPending();
        Check(provider.Valid(cached,1,2,"screen")&&ReferenceEquals(beforeFailure,cached.Publication)&&native.Pixels[front]==beforeFailure.Generation,"Failed staging composition retains cached lease and committed publication");native.FailCompose=false;
        world.Allowed=false;provider.Prune();Check(!provider.Valid(cached,1,2,"screen")&&cached.Publication==null,"Authorization revocation permanently ends retained lease");native.Drain();world.Allowed=true;provider.Request(1,2,"screen","screen");provider.RenderPending();var authorized=provider.Request(1,2,"screen","screen");Check(!ReferenceEquals(cached,authorized)&&!provider.Valid(cached,1,2,"screen"),"Reauthorization cannot resurrect old retained evidence");provider.Release(cached);Check(provider.Valid(authorized,1,2,"screen"),"Revoked lease release cannot cancel reauthorized source");
        provider.Cancel(1,2,"screen");Check(!provider.Valid(authorized,1,2,"screen")&&authorized.Publication==null,"Explicit cancellation revokes retained publication lease");native.Drain();provider.Request(1,2,"screen","screen");provider.RenderPending();var reset=provider.Request(1,2,"screen","screen");provider.NewEpoch();Check(!provider.Valid(reset,1,2,"screen")&&reset.Publication==null,"Device reset suspends source and revokes old lease before retirement");native.Drain();provider.RenderPending();var renewed=provider.Request(1,2,"screen","screen");Check(renewed!=null&&!ReferenceEquals(reset,renewed)&&!provider.Valid(reset,1,2,"screen"),"Reset creates new resource lease without resurrecting cached evidence");provider.Release(reset);Check(provider.Valid(renewed,1,2,"screen"),"Release after reset cannot revoke new resource lease");provider.ClearWorld();Check(!provider.Valid(renewed,1,2,"screen")&&renewed.Publication==null,"World departure revokes retained source eligibility");native.Drain();provider.Dispose();native.Drain();
    }
    static void FrontLifetimeChecks()
    {
        var world=new PortalWorld();var native=new PortalNative { World=world };var provider=new PortalProvider(world,native);provider.Request(1,2,"screen","screen");provider.RenderPending();var lease=provider.Request(1,2,"screen","screen");
        var materialName=lease.Texture;var queuedTexture=native.Targets[materialName];int captureCreates=native.Creates["HDR_PortalCapture_0"],stagingCreates=native.Creates.Where(p=>p.Key.StartsWith("HDR_PortalStaging_0")).Sum(p=>p.Value);
        Check(lease.Width==64&&lease.Height==64&&native.Sizes[queuedTexture]==new Vector2I(256,256),"Frame reports logical information resolution independently of persistent physical bucket");
        // ScreenLod becoming invisible clears mod cached geometry and releases
        // the stable evidence, but a previously submitted primary billboard may
        // still resolve this generated material after the cleanup boundary.
        provider.Release(lease);native.Drain();
        Check(!provider.Valid(lease,1,2,"screen")&&lease.Publication==null,"LOD/demand release revokes render eligibility immediately");
        Check(ReferenceEquals(native.Targets[materialName],queuedTexture)&&native.Pixels[queuedTexture]==0&&native.PrivateTargets==0,"Queued primary billboard retains nonnull transparent texture after LOD release");
        provider.Request(1,2,"screen","screen");provider.RenderPending();var resumed=provider.Request(1,2,"screen","screen");
        Check(resumed!=null&&!ReferenceEquals(lease,resumed)&&ReferenceEquals(native.Targets[materialName],queuedTexture),"Demand return reuses front wrapper without resurrecting revoked lease");
        var dimensions=(int[])world.Descriptor[18];dimensions[0]=256;dimensions[1]=144;world.Time+=.04;provider.Request(1,2,"screen","screen");provider.RenderPending();var resized=provider.Request(1,2,"screen","screen");
        Check(resized!=null&&resized.Width==256&&resized.Height==144&&resized.Texture!=materialName&&ReferenceEquals(native.Targets[materialName],queuedTexture),"Quality transition selects another stable bucket without resetting old billboard wrapper");
        Check(native.Creates["HDR_PortalCapture_0"]==captureCreates+1&&native.Creates.Where(p=>p.Key.StartsWith("HDR_PortalStaging_0")).Sum(p=>p.Value)==stagingCreates+2,"Output bucket changes resize private staging while retained capture remains valid");
        Check(!provider.Valid(resumed,1,2,"screen")&&resumed.Publication==null,"Configuration resize still revokes prior source evidence");
        provider.Release(resumed);Check(provider.Valid(resized,1,2,"screen"),"Releasing resized source's old evidence cannot retire new source");
        provider.ClearWorld();native.Drain();Check(native.PrivateTargets==0&&ReferenceEquals(native.Targets[materialName],queuedTexture)&&native.Pixels[queuedTexture]==0,"World departure tombstones cached material texture instead of deleting its lookup");provider.Dispose();native.Drain();
        // Existing compositor indexes its logical ray textures through normalized
        // fullscreen UV, so rendering onto fixed512² does not read past small or
        // nonsquare ray grids and preserves the authored 0..1 surface domain.
        Check(PortalProjectionShaders.GeneralComposite.Contains("uv*HDRRayDimensions.xy")&&!PortalProjectionShaders.GeneralComposite.Contains("position.xy/HDRRayDimensions"),"Production compositor uses logical UV lookup independent of physical viewport");
        foreach(var logical in new[]{new Vector2I(64,64),new Vector2I(256,144),new Vector2I(512,512)})
        {
            int firstX=(int)((.5/512)*logical.X),firstY=(int)((.5/512)*logical.Y),lastX=(int)((511.5/512)*logical.X),lastY=(int)((511.5/512)*logical.Y);
            Check(firstX==0&&firstY==0&&lastX==logical.X-1&&lastY==logical.Y-1,"Fixed physical viewport covers every edge of logical UV grid "+logical);
        }
    }
    static void BudgetChecks()
    {
        long number=1;var budget=new ClientPixelBudget(()=>new ClientPixelBudget.Frame(number,1920,1080));var world=new PortalWorld();var native=new PortalNative { World=world };var provider=new PortalProvider(world,native);provider.SetBudget(budget);
        provider.Request(1,2,"screen","screen");provider.RenderPending();Check(budget.Spent==256*256+68*68&&native.Captures==1&&native.Compositions==1,"Actual cropped capture and active guarded ROI charge shared pixel budget");
        world.Time+=1;provider.Request(1,2,"screen","screen");provider.RenderPending();Check(native.Captures==1,"Source cannot spend twice in same render frame");number++;provider.RenderPending();Check(native.Captures==2,"Pending budget work resumes in next main frame");provider.Dispose();native.Drain();
        var smallBudget=new ClientPixelBudget(()=>new ClientPixelBudget.Frame(1,128,128));world=new PortalWorld();native=new PortalNative { World=world };provider=new PortalProvider(world,native);provider.SetBudget(smallBudget);provider.Request(1,2,"screen","screen");provider.RenderPending();Check(native.Captures==0&&native.Targets.Count==0,"Insufficient pixel grant never allocates or starts native portal capture");provider.Dispose();native.Drain();
        var intermediateBudget=new ClientPixelBudget(()=>new ClientPixelBudget.Frame(1,600,600));world=new PortalWorld();native=new PortalNative { World=world };provider=new PortalProvider(world,native);provider.SetBudget(intermediateBudget);provider.Request(1,2,"screen","screen");provider.RenderPending();Check(native.Captures==1&&intermediateBudget.Spent<=360000,"Active ROI planning fits viewport limits that rejected old fixed work");provider.Dispose();native.Drain();
        var previousCaptureBudget=new ClientPixelBudget(()=>new ClientPixelBudget.Frame(1,1024,1024));world=new PortalWorld();native=new PortalNative { World=world };provider=new PortalProvider(world,native);provider.SetBudget(previousCaptureBudget);provider.Request(1,2,"screen","screen");provider.RenderPending();Check(native.Captures==1&&previousCaptureBudget.Spent<=1048576,"One-megapixel budget admits proportionate cropped portal work");provider.Dispose();native.Drain();
    }
}
