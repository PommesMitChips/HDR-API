using System;
using System.Collections.Generic;
using System.Threading;
using VRageMath;

namespace HDRClientRenderer
{
    // Raster provider boundary: callers receive a generated texture/material,
    // never renderer objects. Only numeric, authorized declarations enter here.
    internal sealed class PortalProvider : IDisposable
    {
        internal const int MaxSources=2;
        // Billboard materials keep these named front resources for the render
        // manager lifetime. Native front/staging rasters are fixed-size even
        // when the declaration chooses a smaller logical ray/information grid.
        internal const int CaptureSize=1024,PhysicalSize=512,PhysicalWorkPixels=CaptureSize*CaptureSize+PhysicalSize*PhysicalSize;
        internal interface IWorld
        {
            double Now {get;}
            bool Active(long anchor,long caller,string sourceId,string screenId);
            bool Authorized(long anchor,long caller,string sourceId);
            bool TryDescriptor(long anchor,long caller,string sourceId,out object[] descriptor);
            bool TryLocalView(out MatrixD viewer,out MatrixD projection);
        }
        internal interface INative
        {
            bool Ready {get;} string Reason {get;} long DeviceEpoch {get;}
            bool ProjectiveReady {get;}
            bool TryView(out PortalPrimaryView view);
            object CreateTarget(string name,int width,int height);
            Action FrontRetirement(string name);
            void DestroyTarget(string name);
            bool Capture(object target,int resolution,PortalRayMapSpec spec,out string error);
            bool Compose(object capture,object output,PortalRayPacket packet,float saturation,float brightness,out string error);
            bool Publish(object staging,object front,int activeWidth,int activeHeight,int gutter,out string error);
            bool PublishProjective(object capture,object front,PortalProjectiveSampling sample,out string error);
            void EnqueueCleanup(Action cleanup);
        }
        internal sealed class Declaration
        {
            internal readonly PortalSurfaceMap Entry,Exit,Shell;
            internal readonly PortalRayTransport Transport;
            internal readonly PortalRayAccuracy Accuracy;
            internal readonly double NormalScale,Rate;
            internal readonly float Saturation,Brightness;
            internal readonly int Width,Height,EntryChart,ExitChart;
            readonly object[] descriptor;
            Declaration(object[] values,PortalSurfaceMap entry,PortalSurfaceMap exit,PortalSurfaceMap shell)
            {
                descriptor=values;Entry=entry;Exit=exit;Shell=shell;Transport=(PortalRayTransport)(int)values[1];Accuracy=(PortalRayAccuracy)(int)values[2];
                var settings=(double[])values[17];var size=(int[])values[18];NormalScale=settings[0];Rate=settings[1];Saturation=(float)settings[2];Brightness=(float)settings[3];
                Width=size[0];Height=size[1];EntryChart=size[2];ExitChart=size[3];
            }
            internal bool Same(Declaration other)
            {
                if(other==null)return false;
                for(int i=0;i<descriptor.Length;i++)
                {
                    var a=descriptor[i] as Array;var b=other.descriptor[i] as Array;
                    if(a==null){if(!Equals(descriptor[i],other.descriptor[i]))return false;continue;}
                    if(b==null||a.GetType()!=b.GetType()||a.Length!=b.Length)return false;
                    for(int j=0;j<a.Length;j++)if(!Equals(a.GetValue(j),b.GetValue(j)))return false;
                }
                return true;
            }
            internal static bool TryParse(object[] input,out Declaration result,out string error)
            {
                result=null;error="Invalid bounded portal descriptor v1.";
                if(input==null||input.Length!=19||!(input[0] is int)||(int)input[0]!=1||!(input[1] is int)||!(input[2] is int)||(int)input[1]<0||(int)input[1]>1||(int)input[2]<0||(int)input[2]>1)return false;
                var values=(object[])input.Clone();
                for(int i=0;i<values.Length;i++)
                {
                    var doubles=values[i] as double[];var integers=values[i] as int[];
                    if(doubles!=null){if(doubles.Length>PortalSurfaceMap.MaxVertices*3)return false;values[i]=(double[])doubles.Clone();foreach(double n in doubles)if(!PortalProjection.Finite(n))return false;}
                    else if(integers!=null){if(integers.Length>PortalSurfaceMap.MaxTriangles*3)return false;values[i]=(int[])integers.Clone();}
                    else if(!(values[i] is int))return false;
                }
                PortalSurfaceMap entry,exit,shell;MatrixD shellPose;
                if(!Surface(values,3,out entry)||!Surface(values,9,out exit)||!Matrix(values[15] as double[],out shellPose))return false;
                var radii=values[16] as double[];var settings=values[17] as double[];var size=values[18] as int[];
                if(radii==null||radii.Length!=3||!PortalSurfaceMap.TryEllipsoid(shellPose,new Vector3D(radii[0],radii[1],radii[2]),out shell)||
                    settings==null||settings.Length!=4||Math.Abs(settings[0])<1d/64||Math.Abs(settings[0])>64||settings[1]<1||settings[1]>120||settings[2]<0||settings[2]>2||settings[3]<0||settings[3]>4||
                    size==null||size.Length!=4||size[0]<64||size[1]<64||size[0]>4096||size[1]>4096||size[2]<0||size[2]>=entry.ChartCount||size[3]<0||size[3]>=exit.ChartCount||entry.Kind==PortalSurfaceKind.UvMesh&&entry.ChartCount!=1)return false;
                result=new Declaration(values,entry,exit,shell);error=null;return true;
            }
            static bool Surface(object[] values,int at,out PortalSurfaceMap result)
            {
                result=null;if(!(values[at] is int))return false;int kind=(int)values[at];MatrixD pose;
                var extent=values[at+2] as double[];var p=values[at+3] as double[];var indices=values[at+4] as int[];var tex=values[at+5] as double[];
                if(!Matrix(values[at+1] as double[],out pose)||extent==null||extent.Length!=3||p==null||indices==null||tex==null)return false;
                if(kind==0||kind==1||kind==3)
                {
                    if(p.Length!=0||indices.Length!=0||tex.Length!=0)return false;
                    return kind==0?extent[2]==0&&PortalSurfaceMap.TryPlane(pose,extent[0],extent[1],out result):PortalSurfaceMap.TryEllipsoid(pose,new Vector3D(extent[0],extent[1],extent[2]),kind==1,out result);
                }
                if(kind!=2||p.Length%3!=0||tex.Length%2!=0||p.Length/3!=tex.Length/2||extent[0]!=0||extent[1]!=0||extent[2]!=0)return false;
                var points=new Vector3D[p.Length/3];var uv=new Vector2D[tex.Length/2];for(int i=0;i<points.Length;i++){points[i]=new Vector3D(p[i*3],p[i*3+1],p[i*3+2]);uv[i]=new Vector2D(tex[i*2],tex[i*2+1]);}
                return PortalSurfaceMap.TryMesh(pose,points,uv,indices,out result);
            }
            internal static bool Matrix(double[] a,out MatrixD matrix)
            {
                matrix=MatrixD.Identity;if(a==null||a.Length!=16)return false;foreach(double n in a)if(!PortalProjection.Finite(n))return false;
                matrix=new MatrixD(a[0],a[1],a[2],a[3],a[4],a[5],a[6],a[7],a[8],a[9],a[10],a[11],a[12],a[13],a[14],a[15]);return PortalProjection.Rigid(matrix);
            }
            internal bool Snapshot(int epoch,long generation,MatrixD viewer,MatrixD projection,out PortalRayMapSpec result)
            {
                result=null;if(!PortalProjection.Rigid(viewer)||!PortalProjection.ComplementaryProjection(projection)||Math.Abs(projection.Determinant())<1e-18)return false;
                MatrixD camera=viewer, captureProjection=projection;
                if(Transport==PortalRayTransport.Differential)
                {
                    double u,v;PortalSurfaceSample a,b;if(!Entry.ChartCentre(EntryChart,out u,out v)||!Entry.Evaluate(u,v,EntryChart,out a)||!Exit.Evaluate(u,v,ExitChart,out b))return false;
                    var j=MatrixD.Identity;j.Right=TransportVector(Vector3D.UnitX,a,b,NormalScale);j.Up=TransportVector(Vector3D.UnitY,a,b,NormalScale);j.Backward=TransportVector(Vector3D.UnitZ,a,b,NormalScale);
                    if(!PortalProjection.Finite(j)||Math.Abs(j.Determinant())<1e-18)return false;
                    camera=MatrixD.CreateWorld(b.Point+Vector3D.TransformNormal(viewer.Translation-a.Point,j),Vector3D.TransformNormal(viewer.Forward,j),Vector3D.TransformNormal(viewer.Up,j));
                    if(!PortalProjection.Rigid(camera))return false;
                    // Affine plane warps share one virtual eye; transform the
                    // pinhole itself so anisotropic portals remain ray-correct.
                    // Nonlinear surfaces use this local chart linearization;
                    // strict packet samples reject every off-line outgoing ray.
                    captureProjection=camera.GetOrientation()*MatrixD.Invert(j)*MatrixD.Transpose(viewer.GetOrientation())*projection;
                    double scale=-captureProjection.M34;if(!PortalProjection.Positive(scale))return false;captureProjection=ScaleMatrix(captureProjection,1/scale);
                }
                return PortalRayMapSpec.TryCreate(epoch,generation,Entry,EntryChart,Exit,ExitChart,Shell,viewer,camera,captureProjection,Transport,PortalImageProjection.Perspective,Accuracy,NormalScale,out result);
            }
            static Vector3D TransportVector(Vector3D vector,PortalSurfaceSample a,PortalSurfaceSample b,double normal)
            {
                double aa=a.Du.LengthSquared(),ab=Vector3D.Dot(a.Du,a.Dv),bb=a.Dv.LengthSquared(),det=aa*bb-ab*ab,da=Vector3D.Dot(vector,a.Du),db=Vector3D.Dot(vector,a.Dv);
                return ((da*bb-db*ab)/det)*b.Du+((db*aa-da*ab)/det)*b.Dv+Vector3D.Dot(vector,a.Normal)*normal*b.Normal;
            }
            static MatrixD ScaleMatrix(MatrixD m,double s)
            {return new MatrixD(m.M11*s,m.M12*s,m.M13*s,m.M14*s,m.M21*s,m.M22*s,m.M23*s,m.M24*s,m.M31*s,m.M32*s,m.M33*s,m.M34*s,m.M41*s,m.M42*s,m.M43*s,m.M44*s);}
        }
        internal sealed class Frame
        {
            // Stable front-resource lease, retained by the mod between its slow
            // acquisitions. Generation is an eligibility marker for this lease,
            // NOT a pixel revision: every successful front copy atomically swaps
            // Publication, whose immutable fields describe the current pixels.
            internal sealed class Published
            {
                internal readonly long Generation;
                internal readonly MatrixD ViewerPose,ViewerProjection;
                internal readonly int ValidPixels;
                internal readonly long ViewFrame;internal readonly int CaptureSize;internal readonly bool Reduced;internal readonly PortalProjectiveSampling Sampling;
                internal readonly int ViewportWidth,ViewportHeight,PresentationWidth,PresentationHeight;
                internal Published(long generation,MatrixD viewer,MatrixD projection,int validPixels,long viewFrame,int captureSize,bool reduced,PortalProjectiveSampling sampling=null,PortalPrimaryView primary=null)
                {Generation=generation;ViewerPose=viewer;ViewerProjection=projection;ValidPixels=validPixels;ViewFrame=viewFrame;CaptureSize=captureSize;Reduced=reduced;Sampling=sampling;ViewportWidth=primary==null?0:primary.Width;ViewportHeight=primary==null?0:primary.Height;PresentationWidth=primary==null?0:primary.PresentationWidth;PresentationHeight=primary==null?0:primary.PresentationHeight;}
            }
            internal readonly string Texture;internal readonly int Width,Height;internal readonly long Generation,DeviceEpoch;
            internal readonly Declaration Declaration;
            internal readonly Vector4 TextureUv;internal readonly object Target;internal readonly int Bucket,OutputWidth,OutputHeight;internal readonly bool Projective;internal readonly Action Retire;
            internal int PhysicalWidth{get{return Bucket;}}internal int PhysicalHeight{get{return Bucket;}}
            Published publication;
            // Read this once when comparing several publication fields. Reading
            // independent convenience properties may span an ordinary publish.
            internal Published Publication{get{return Volatile.Read(ref publication);}}
            internal int ValidPixels{get{var p=Publication;return p==null?0:p.ValidPixels;}}
            internal bool Reduced{get{var p=Publication;return p==null||p.Reduced||p.ValidPixels<(long)Width*Height;}}
            internal MatrixD ViewerPose{get{var p=Publication;return p==null?MatrixD.Identity:p.ViewerPose;}}
            internal MatrixD ViewerProjection{get{var p=Publication;return p==null?MatrixD.Identity:p.ViewerProjection;}}
            internal Frame(string texture,Declaration declaration,long leaseGeneration,long deviceEpoch,object target,PortalResolutionPlan plan,int informationWidth,int informationHeight,Action retire)
            {Texture=texture;Width=informationWidth;Height=informationHeight;Generation=leaseGeneration;DeviceEpoch=deviceEpoch;Declaration=declaration;Target=target;Bucket=plan.BucketSize;OutputWidth=plan.OutputWidth;OutputHeight=plan.OutputHeight;TextureUv=plan.TextureUv;Projective=plan.Projective;Retire=retire;}
            internal void Publish(Published next){Volatile.Write(ref publication,next);}
            internal void Revoke(){Volatile.Write(ref publication,null);}
        }
        sealed class Slot
        {
            internal long Anchor,Caller,Generation;internal string Source,Screen;internal Declaration Declaration;internal MatrixD Viewer,Projection;
            internal double LastDemand,AuthorizationUntil,LastRender=double.NegativeInfinity;internal Frame Completed;internal bool Pending,InFlight,RetireRequested;
            internal Frame Held;internal long LastAttemptFrame=long.MinValue;internal bool CompletedClaimed;internal double NextDue;
            internal readonly List<Frame> Retired=new List<Frame>();
            internal object Capture,Output,Staging;internal string CaptureName,OutputName,StagingName;internal int Width,Height;
            internal long TargetEpoch=long.MinValue;
        }
        sealed class Work
        {
            internal readonly Slot Slot;internal readonly Declaration Declaration;internal readonly MatrixD Viewer,Projection;
            internal readonly long Generation,DeviceEpoch;internal readonly int Epoch,Index;
            internal readonly double RequestedAt;
            internal readonly double AuthorizationUntil;
            internal readonly PortalResolutionPlan Plan;internal readonly PortalPrimaryView View;
            internal Work(Slot slot,int epoch,int index,long stamp,PortalPrimaryView view,PortalResolutionPlan plan,double now)
            {Slot=slot;Declaration=slot.Declaration;Viewer=view.Viewer;Projection=view.Projection;Generation=stamp;Epoch=epoch;Index=index;DeviceEpoch=view.Epoch;RequestedAt=now;AuthorizationUntil=slot.AuthorizationUntil;View=view;Plan=plan;}
        }
        readonly object gate=new object();readonly IWorld world;readonly INative native;readonly Slot[] slots=new Slot[MaxSources];
        readonly bool[] retiring=new bool[MaxSources];ClientPixelBudget budget;
        int epoch,nextSlot;long generation,leaseGeneration,lastPrimaryFrame;bool disposed;string failure;
        internal bool Ready{get{return !disposed&&native.Ready;}}
        internal string Reason
        {
            get
            {
                lock(gate)
                {
                    var text=new System.Text.StringBuilder(failure??native.Reason);
                    foreach(var slot in slots)if(slot!=null&&slot.Completed!=null&&slot.Completed.Publication!=null)
                    {
                        var frame=slot.Completed;var publication=frame.Publication;
                        text.Append("; ").Append(slot.Screen).Append(" requested ").Append(slot.Declaration.Width).Append('x').Append(slot.Declaration.Height)
                            .Append(" -> information ").Append(frame.Width).Append('x').Append(frame.Height).Append(" / capture ").Append(publication.CaptureSize)
                            .Append(" / bucket ").Append(frame.Bucket).Append(" / ").Append(slot.Declaration.Rate).Append(" Hz / capture age ")
                            .Append(Math.Max(0,lastPrimaryFrame-publication.ViewFrame)).Append(" frames");
                        text.Append(frame.Projective?" / projective":" / full UV");
                        if(publication.ViewportWidth!=publication.PresentationWidth||publication.ViewportHeight!=publication.PresentationHeight)
                            text.Append(" / viewport ").Append(publication.ViewportWidth).Append('x').Append(publication.ViewportHeight).Append(" / presentation ").Append(publication.PresentationWidth).Append('x').Append(publication.PresentationHeight);
                        if(frame.Reduced)text.Append(" / capped or partial");
                    }
                    return text.ToString();
                }
            }
        }
        internal bool HasDemand{get{lock(gate){foreach(var slot in slots)if(slot!=null)return true;return false;}}}
        internal bool PendingWork{get{lock(gate){foreach(var slot in slots)if(slot!=null&&slot.Pending&&!slot.RetireRequested)return true;return false;}}}
        internal PortalProvider(IWorld world,INative native){this.world=world??throw new ArgumentNullException("world");this.native=native??throw new ArgumentNullException("native");}
        internal void SetBudget(ClientPixelBudget shared){budget=shared;}
        static string BudgetKey(int index){return "HDR.Portal."+index;}
        internal Frame Request(long anchor,long caller,string sourceId,string screenId)
        {
            if(!Ready||anchor<=0||caller<=0||string.IsNullOrEmpty(sourceId)||sourceId.Length>64||string.IsNullOrEmpty(screenId)||screenId.Length>64)return null;
            object[] raw;Declaration declaration;string error=null;MatrixD viewer,projection;
            if(!world.Active(anchor,caller,sourceId,screenId)||!world.Authorized(anchor,caller,sourceId)||!world.TryDescriptor(anchor,caller,sourceId,out raw)||!Declaration.TryParse(raw,out declaration,out error)||!world.TryLocalView(out viewer,out projection))
            {failure=error??"Portal source has no visible authorized declaration or local viewer.";Cancel(anchor,caller,sourceId);return null;}
            lock(gate)
            {
                int index=-1;for(int i=0;i<slots.Length;i++)if(slots[i]!=null&&slots[i].Anchor==anchor&&slots[i].Caller==caller&&slots[i].Source==sourceId&&slots[i].Screen==screenId){index=i;break;}
                if(index<0)for(int i=0;i<slots.Length;i++)if(slots[i]==null&&!retiring[i]){index=i;slots[i]=new Slot{Anchor=anchor,Caller=caller,Source=sourceId,Screen=screenId};break;}
                if(index<0)return null;var slot=slots[index];double now=world.Now;if(!PortalProjection.Finite(now))return null;
                bool declarationChanged=slot.Declaration==null||!slot.Declaration.Same(declaration);
                bool deviceChanged=slot.Completed!=null&&slot.Completed.DeviceEpoch!=native.DeviceEpoch;
                bool changed=declarationChanged||!Near(slot.Viewer,viewer)||!Near(slot.Projection,projection)||deviceChanged;
                if(changed){slot.Declaration=declaration;slot.Viewer=viewer;slot.Projection=projection;slot.Generation=++generation;if(declarationChanged||deviceChanged)RevokeCompleted(slot);slot.Pending=true;}
                else if(now-slot.LastRender>=1/declaration.Rate)slot.Pending=true;
                slot.LastDemand=now;slot.AuthorizationUntil=now+.25;
                if(slot.Completed!=null&&slot.Held!=null&&!ReferenceEquals(slot.Held,slot.Completed))
                {
                    // The endpoint now offers the new binding. Keep the prior
                    // acquired lease alive until the mod explicitly releases it.
                }
                else if(slot.Completed!=null)slot.Held=slot.Completed;
                if(slot.Held!=null&&slot.Held.Publication==null){var retired=slot.Held;slot.Held=slot.Completed;native.EnqueueCleanup(()=>RetireFrame(retired));}
                if(slot.Completed!=null)slot.CompletedClaimed=true;
                return slot.Completed;
            }
        }
        internal void RenderPending()
        {
            if(!Ready)return;PortalPrimaryView view;if(!native.TryView(out view)||view==null||!view.Valid||view.Epoch!=native.DeviceEpoch)return;double now=world.Now;lock(gate)lastPrimaryFrame=view.Frame;
            // One bounded native capture and compose per main frame. The root
            // invokes this only on its serialized isolated render boundary.
            Work work=null;
            lock(gate)for(int offset=0;offset<slots.Length;offset++)
            {
                int i=(nextSlot+offset)%slots.Length;if(slots[i]==null||slots[i].InFlight||slots[i].RetireRequested)continue;var candidate=slots[i];
                if(!PortalProjection.Finite(now)||now>candidate.AuthorizationUntil||candidate.LastAttemptFrame==view.Frame)continue;
                if(candidate.Completed!=null&&now+1e-9<candidate.NextDue)continue;
                PortalRayMapSpec baseSpec;PortalResolutionPlan plan;int available=budget==null?ClientPixelBudget.MaxJobPixels:Math.Min(ClientPixelBudget.MaxJobPixels,budget.AvailablePixels);
                if(view.Epoch>int.MaxValue||!candidate.Declaration.Snapshot((int)view.Epoch,generation+1,view.Viewer,view.Projection,out baseSpec))continue;
                bool projective=native.ProjectiveReady&&PortalProjectiveSampling.Eligible(baseSpec);
                if(!PortalResolution.TryPlan(baseSpec,view,candidate.Declaration.Width,candidate.Declaration.Height,candidate.Completed==null?0:candidate.Completed.Bucket,available,out plan,projective))continue;
                // At most acquired+offered bindings per source. While the old
                // geometry is held, do not introduce a third adaptive layout.
                if(candidate.Held!=null&&candidate.Completed!=null&&!ReferenceEquals(candidate.Held,candidate.Completed)&&plan.BucketSize!=candidate.Held.Bucket&&plan.BucketSize!=candidate.Completed.Bucket)continue;
                if(candidate.Held!=null&&candidate.Completed!=null&&!ReferenceEquals(candidate.Held,candidate.Completed)&&candidate.CompletedClaimed&&plan.BucketSize!=candidate.Completed.Bucket)continue;
                int pixels=plan.Pixels;
                if(budget!=null&&!budget.TrySpend(BudgetKey(i),ClientPixelBudget.Kind.Capture,pixels))continue;
                work=new Work(candidate,epoch,i,++generation,view,plan,now);candidate.Pending=false;candidate.InFlight=true;candidate.LastAttemptFrame=view.Frame;nextSlot=(i+1)%slots.Length;break;
            }
            if(work==null)return;var item=work.Slot;
            try
            {
                PortalRayMapSpec spec;PortalRayPacket packet=null;PortalProjectiveSampling sampling=null;string error;
                if(work.DeviceEpoch<0||work.DeviceEpoch>int.MaxValue||!Current(work))return;
                DrainRetired(item);
                if(!work.Declaration.Snapshot((int)work.DeviceEpoch,work.Generation,work.Viewer,work.Projection,out spec)){failure="Portal virtual camera/projection cannot represent this declaration.";return;}
                if(!spec.WithProjection(work.Plan.Projection,work.Plan.ProbeU,work.Plan.ProbeV,out spec))return;
                bool affine=spec.Entry.Kind==PortalSurfaceKind.Plane&&spec.Exit.Kind==PortalSurfaceKind.Plane&&spec.Transport==PortalRayTransport.Differential;
                int informationWidth=work.Plan.OutputWidth,informationHeight=work.Plan.OutputHeight;
                if(work.Plan.Projective)
                {if(!PortalProjectiveSampling.TryCreate(spec,work.View,work.Plan.CaptureSize,work.Plan.CaptureSize,work.Declaration.Saturation,work.Declaration.Brightness,out sampling)){failure="Portal direct source projection proof failed.";return;}}
                else
                {
                    if(!affine){double scale=Math.Min(1,512d/Math.Max(informationWidth,informationHeight));informationWidth=Math.Max(1,(int)(informationWidth*scale));informationHeight=Math.Max(1,(int)(informationHeight*scale));}
                    if(!PortalRayPacket.TryBuild(spec,informationWidth,informationHeight,out packet,out error)){failure=error;return;}packet=packet.WithOutput(work.Plan.OutputWidth,work.Plan.OutputHeight,PortalResolution.Gutter);
                }
                if(item.TargetEpoch!=work.DeviceEpoch){lock(gate)RevokeCompleted(item);Retire(item);item.TargetEpoch=work.DeviceEpoch;}
                if(item.Capture==null||item.Width!=work.Plan.CaptureSize)
                {
                    if(item.CaptureName!=null)native.DestroyTarget(item.CaptureName);item.CaptureName="HDR_PortalCapture_"+work.Index;item.Capture=native.CreateTarget(item.CaptureName,work.Plan.CaptureSize,work.Plan.CaptureSize);item.Width=work.Plan.CaptureSize;
                }
                if(!work.Plan.Projective&&(item.Staging==null||item.Height!=work.Plan.BucketSize)){if(item.StagingName!=null)native.DestroyTarget(item.StagingName);item.StagingName="HDR_PortalStaging_"+work.Index+"_N"+work.Plan.BucketSize;item.Staging=native.CreateTarget(item.StagingName,work.Plan.BucketSize,work.Plan.BucketSize);item.Height=work.Plan.BucketSize;}
                Frame targetLease;lock(gate)targetLease=Matching(item.Completed,work.Plan)?item.Completed:Matching(item.Held,work.Plan)?item.Held:null;
                object target=targetLease==null?null:targetLease.Target;
                if(!Current(work))return;
                if(!native.Capture(item.Capture,work.Plan.CaptureSize,spec,out error)){failure=error;return;}
                if(!Current(work))return;
                if(!work.Plan.Projective&&!native.Compose(item.Capture,item.Staging,packet,work.Declaration.Saturation,work.Declaration.Brightness,out error)){failure=error;return;}
                lock(gate)
                {
                    if(!Current(work))return;
                    // Staging writes cannot alter an existing published frame.
                    // Copy failure may already have submitted GPU commands, so
                    // invalidate old evidence BEFORE touching the front target.
                    // Request/cancel commits share this lock with publication.
                    var lease=targetLease;
                    // Acquiring a stable front changes its retirement stamp.
                    // Defer that ownership transfer until capture/composition
                    // succeed so failed work preserves the old lease's cleanup.
                    if(target==null)target=native.CreateTarget("HDR_ClientPortal_"+work.Index+"_N"+work.Plan.BucketSize,work.Plan.BucketSize,work.Plan.BucketSize);
                    // A mode/layout change can reuse the same stable front
                    // wrapper. Revoke only evidence for those overwritten
                    // pixels; a distinct held front retains its own mapping.
                    RevokeDifferentInterpretation(item,target,lease);
                    bool claimed=ReferenceEquals(lease,item.Held)||ReferenceEquals(lease,item.Completed)&&item.CompletedClaimed;
                    if(lease!=null)lease.Revoke();
                    if(ReferenceEquals(item.Completed,lease))item.Completed=null;
                    if(lease==null){string name="HDR_ClientPortal_"+work.Index+"_N"+work.Plan.BucketSize;lease=new Frame(name,work.Declaration,++leaseGeneration,work.DeviceEpoch,target,work.Plan,informationWidth,informationHeight,native.FrontRetirement(name));}
                    bool copied;
                    try{copied=work.Plan.Projective?native.PublishProjective(item.Capture,target,sampling,out error):native.Publish(item.Staging,target,packet.OutputWidth,packet.OutputHeight,packet.Gutter,out error);}
                    catch{FailedPublication(item,lease);throw;}
                    if(!copied){FailedPublication(item,lease);failure=error;return;}
                    if(!Current(work)){FailedPublication(item,lease);return;}
                    if(item.Completed!=null&&!ReferenceEquals(item.Completed,item.Held)&&!ReferenceEquals(item.Completed,lease))RetireFrame(item.Completed);
                    int valid=work.Plan.Projective?informationWidth*informationHeight:packet.ValidPixels;
                    lease.Publish(new Frame.Published(work.Generation,work.Viewer,work.Projection,valid,work.View.Frame,work.Plan.CaptureSize,work.Plan.Reduced||!work.Plan.Projective&&(!packet.CoverageExact||packet.Width<packet.OutputWidth||packet.Height<packet.OutputHeight),sampling,work.View));
                    item.Completed=lease;item.CompletedClaimed=claimed;item.LastRender=work.RequestedAt;item.NextDue=AdvanceDue(item.NextDue,work.RequestedAt,work.Declaration.Rate);failure=null;
                }
                if(budget!=null)budget.RecordBandwidth(work.Plan.Projective?3L*work.Plan.CaptureSize*work.Plan.CaptureSize:(long)work.Plan.CaptureSize*work.Plan.CaptureSize+work.Plan.DrawWidth*work.Plan.DrawHeight*5L+packet.TextureWidth*packet.TextureHeight*2L);
            }
            catch(Exception ex){failure=ex.GetBaseException().Message;}
            finally
            {
                bool retire;lock(gate){item.InFlight=false;retire=item.RetireRequested;}
                if(retire)RetireQueued(item,work.Index);
                if(budget!=null)budget.Complete(BudgetKey(work.Index));
            }
        }
        bool Current(Work work)
        {
            // A newer viewer pose may arrive while the GPU renders. Publish the
            // completed immutable prior-pose frame with its own truthful stamp,
            // then service the newer pose; declaration/auth changes still cancel.
            double now=world.Now;
            lock(gate)return !disposed&&PortalProjection.Finite(now)&&now<=work.AuthorizationUntil&&now<=work.Slot.AuthorizationUntil&&epoch==work.Epoch&&ReferenceEquals(slots[work.Index],work.Slot)&&work.Slot.Declaration.Same(work.Declaration)&&native.DeviceEpoch==work.DeviceEpoch&&(!work.Plan.Projective||native.ProjectiveReady);
        }
        internal bool Valid(Frame frame,long anchor,long caller,string sourceId)
        {
            if(!Ready||frame==null||frame.DeviceEpoch!=native.DeviceEpoch||frame.Projective&&!native.ProjectiveReady)return false;Slot slot=null;
            lock(gate)foreach(var candidate in slots)if(candidate!=null&&candidate.Anchor==anchor&&candidate.Caller==caller&&candidate.Source==sourceId&&(ReferenceEquals(candidate.Completed,frame)||ReferenceEquals(candidate.Held,frame))){slot=candidate;break;}
            if(slot==null||!world.Active(anchor,caller,sourceId,slot.Screen)||!world.Authorized(anchor,caller,sourceId))return false;
            object[] raw;Declaration declaration;string error;
            if(!world.TryDescriptor(anchor,caller,sourceId,out raw)||!Declaration.TryParse(raw,out declaration,out error)||!frame.Declaration.Same(declaration))return false;
            lock(gate)return Array.IndexOf(slots,slot)>=0&&(ReferenceEquals(slot.Completed,frame)||ReferenceEquals(slot.Held,frame))&&frame.Publication!=null;
        }
        internal void Release(Frame frame)
        {
            if(frame==null)return;lock(gate)foreach(var slot in slots)if(slot!=null)
            {
                if(ReferenceEquals(slot.Held,frame)&&slot.Completed!=null&&!ReferenceEquals(slot.Completed,frame)){slot.Held=slot.Completed;frame.Revoke();native.EnqueueCleanup(()=>RetireFrame(frame));break;}
                if(ReferenceEquals(slot.Completed,frame)&&slot.Held!=null&&!ReferenceEquals(slot.Held,frame)){slot.Completed=slot.Held;slot.Pending=true;frame.Revoke();native.EnqueueCleanup(()=>RetireFrame(frame));break;}
                if(ReferenceEquals(slot.Completed,frame)||ReferenceEquals(slot.Held,frame)){Cancel(slot.Anchor,slot.Caller,slot.Source);break;}
            }
        }
        internal void Cancel(long anchor,long caller,string sourceId)
        {
            lock(gate)for(int i=0;i<slots.Length;i++)if(slots[i]!=null&&slots[i].Anchor==anchor&&slots[i].Caller==caller&&slots[i].Source==sourceId)
            {var retired=slots[i];int index=i;slots[i]=null;retiring[index]=true;retired.Generation=++generation;RevokeCompleted(retired);if(budget!=null)budget.Cancel(BudgetKey(index));native.EnqueueCleanup(()=>RetireQueued(retired,index));}
        }
        internal void Prune()
        {
            // Game-thread tick only: entity authorization is never queried from
            // render workers. Work carries the bounded immutable proof interval.
            var now=world.Now;Slot[] snapshot;lock(gate)snapshot=(Slot[])slots.Clone();
            foreach(var s in snapshot)if(s!=null)
            {
                bool valid=PortalProjection.Finite(now)&&now-s.LastDemand<=1&&world.Active(s.Anchor,s.Caller,s.Source,s.Screen)&&world.Authorized(s.Anchor,s.Caller,s.Source);
                lock(gate)
                {
                    if(Array.IndexOf(slots,s)<0)continue;
                    if(!valid)Cancel(s.Anchor,s.Caller,s.Source);else s.AuthorizationUntil=now+.25;
                }
            }
        }
        internal void NewEpoch()
        {lock(gate){epoch++;for(int i=0;i<slots.Length;i++)if(slots[i]!=null){var s=slots[i];int index=i;s.Generation=++generation;RevokeCompleted(s);s.Pending=true;s.RetireRequested=true;native.EnqueueCleanup(()=>RetireQueued(s,index));}}}
        internal void ClearWorld()
        {
            // World departure removes authorization-bearing declarations. A
            // device reset may rerender live slots; a new world must reauthorize
            // and explicitly request every source through the normal endpoint.
            lock(gate){epoch++;for(int i=0;i<slots.Length;i++)if(slots[i]!=null){var slot=slots[i];Cancel(slot.Anchor,slot.Caller,slot.Source);}failure=null;}
        }
        void Retire(Slot slot)
        // Native DestroyTarget tombstones the two permanent front names; only
        // private capture/staging targets are removed from the manager lookup.
        // Retiring logical demand must not invalidate queued billboard texture
        // references or permit a missing generated material in primary Gather.
        {
            if(slot.CaptureName!=null)native.DestroyTarget(slot.CaptureName);if(slot.OutputName!=null)native.DestroyTarget(slot.OutputName);if(slot.StagingName!=null)native.DestroyTarget(slot.StagingName);
            DrainRetired(slot);
            slot.Capture=slot.Output=slot.Staging=null;slot.CaptureName=slot.OutputName=slot.StagingName=null;slot.TargetEpoch=long.MinValue;slot.Width=slot.Height=0;
        }
        void DrainRetired(Slot slot){Frame[] retired;lock(gate){retired=slot.Retired.ToArray();slot.Retired.Clear();}foreach(var frame in retired)RetireFrame(frame);}
        static bool Matching(Frame frame,PortalResolutionPlan plan){return frame!=null&&frame.Publication!=null&&frame.Projective==plan.Projective&&frame.Bucket==plan.BucketSize&&frame.OutputWidth==plan.OutputWidth&&frame.OutputHeight==plan.OutputHeight;}
        static void RevokeDifferentInterpretation(Slot slot,object target,Frame updating)
        {
            var completed=slot.Completed;var held=slot.Held;
            if(completed!=null&&!ReferenceEquals(completed,updating)&&ReferenceEquals(completed.Target,target))
            {completed.Revoke();if(!slot.Retired.Contains(completed))slot.Retired.Add(completed);slot.Completed=null;slot.CompletedClaimed=false;}
            if(held!=null&&!ReferenceEquals(held,updating)&&ReferenceEquals(held.Target,target))
            {held.Revoke();if(!slot.Retired.Contains(held))slot.Retired.Add(held);slot.Held=null;}
        }
        static void FailedPublication(Slot slot,Frame failed)
        {
            // An offered binding may fail while the consumer still displays a
            // distinct acquired front. Keep that untouched front available for
            // its next acquisition; the failed lease is permanently revoked.
            // A single-front failure has no fallback because its pixels changed.
            if(failed!=null)RetireFrame(failed);
            if(slot.Completed==null&&slot.Held!=null&&slot.Held.Publication!=null)
            {slot.Completed=slot.Held;slot.CompletedClaimed=true;slot.Pending=true;}
        }
        internal static double AdvanceDue(double previous,double now,double rate)
        {
            double step=1/rate;if(!PortalProjection.Positive(rate)||!PortalProjection.Finite(now))return double.PositiveInfinity;
            double next=previous>0?previous+step:now+step;
            if(next<=now+1e-9)next+=(Math.Floor((now-next)/step)+1)*step;return next;
        }
        static void RetireFrame(Frame frame){frame.Revoke();if(frame.Retire!=null)frame.Retire();}
        void RetireQueued(Slot slot,int index)
        {lock(gate){if(slot.InFlight){slot.RetireRequested=true;return;}}try{Retire(slot);}finally{lock(gate){slot.RetireRequested=false;retiring[index]=false;}}}
        public void Dispose(){lock(gate){disposed=true;epoch++;for(int i=0;i<slots.Length;i++)if(slots[i]!=null){var s=slots[i];int index=i;slots[i]=null;retiring[index]=true;RevokeCompleted(s);if(budget!=null)budget.Cancel(BudgetKey(index));native.EnqueueCleanup(()=>RetireQueued(s,index));}}}
        static void RevokeCompleted(Slot slot)
        {
            if(slot.Completed!=null){slot.Completed.Revoke();if(!slot.Retired.Contains(slot.Completed))slot.Retired.Add(slot.Completed);}
            if(slot.Held!=null){slot.Held.Revoke();if(!slot.Retired.Contains(slot.Held))slot.Retired.Add(slot.Held);}slot.Completed=slot.Held=null;slot.CompletedClaimed=false;slot.NextDue=0;
        }
        static bool Near(MatrixD a,MatrixD b)
        {return a==b;} // Viewer-dependent pixels never reuse a different pose.
    }
}
