using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hdr.Html;
using Hdr.Mods;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using Block=Sandbox.ModAPI.Ingame.IMyTerminalBlock;

static partial class HostTests
{
    sealed partial class Core
    {
        internal sealed class SourceSlot
        {internal string Provider,Source;internal Vector4 Rect,Clip,UV;internal int Order;}
        internal sealed class SurfaceState
        {internal string Kind;internal double Width,Height;internal object[] Parameters;internal bool Visible=true;internal Block Anchor;internal MatrixD Pose;internal readonly Dictionary<string,SourceSlot> Slots=new Dictionary<string,SourceSlot>();internal readonly Dictionary<string,int> Orders=new Dictionary<string,int>();internal readonly Dictionary<string,MatrixD> SvgPoses=new Dictionary<string,MatrixD>();}
        internal readonly Dictionary<long,SurfaceState> Surfaces=new Dictionary<long,SurfaceState>();
        internal int ArtworkCalls,SourceCalls,RayCalls;
        internal bool SourceSupported=true,FailSlotOnce;
        SurfaceState Surface(long handle)
        {SurfaceState state;if(!Surfaces.TryGetValue(handle,out state)){state=new SurfaceState();Surfaces.Add(handle,state);}return state;}
        internal bool TrySurfaceCommand(string command,object[] args,out object result)
        {
            result=null;
            if(command=="destroy"){Surfaces.Remove((long)args[0]);return false;}
            if(command=="context-visible"){Surface((long)args[0]).Visible=(bool)args[1];result=true;return true;}
            if(command=="context-pose"){Surface((long)args[0]).Pose=(MatrixD)args[1];result=true;return true;}
            if(command=="context-surface")
            {var state=Surface((long)args[0]);state.Kind=(string)args[1];if(!new[]{"plane","cylinder","sphere","ellipsoid","mesh"}.Contains(state.Kind))throw new ArgumentException("Unsupported surface kind.");state.Width=(double)args[2];state.Height=(double)args[3];state.Parameters=args.Skip(4).ToArray();result=true;return true;}
            if(command=="context-anchor"){var state=Surface((long)args[0]);state.Anchor=args[1] as Block;if(state.Anchor==null)throw new ArgumentException("Actual anchor required.");result=true;return true;}
            if(command=="context-mapping"||command=="context-surface-error"||command=="context-surface-sided"){result=true;return true;}
            if(command=="context-slot-validate")
            {string provider=(string)args[1],source=(string)args[2];if(string.IsNullOrEmpty(provider)||provider.Any(c=>!(c>='a'&&c<='z'||c>='0'&&c<='9'||c=='-'))||string.IsNullOrEmpty(source))throw new ArgumentException("Invalid provider/source declaration.");result=true;return true;}
            if(command=="context-source-status")
            {var state=Surface((long)args[0]);result=new MyTuple<bool,bool,string>(SourceSupported,state.Anchor!=null,!SourceSupported?"Unsupported: provider did not negotiate local source consumers.":state.Anchor==null?"Pending: actual source anchor required.":"Ready");return true;}
            if(command=="context-surface-ray")
            {
                var origin=(Vector3D)args[1];var direction=(Vector3D)args[2];if(!HtmlFrontendDocument.Finite(origin.X)||!HtmlFrontendDocument.Finite(origin.Y)||!HtmlFrontendDocument.Finite(origin.Z)||!HtmlFrontendDocument.Finite(direction.X)||!HtmlFrontendDocument.Finite(direction.Y)||!HtmlFrontendDocument.Finite(direction.Z)||direction.LengthSquared()<1e-12)throw new ArgumentException("Invalid finite ray.");
                RayCalls++;var state=Surface((long)args[0]);bool hit=state.Visible&&Rendering&&Math.Abs(origin.X)<=state.Width*.5&&Math.Abs(origin.Y)<=state.Height*.5;result=new MyTuple<bool,Vector2,Vector3D>(hit,new Vector2((float)origin.X,(float)origin.Y),new Vector3D(origin.X,origin.Y,0));return true;
            }
            if(command=="context-slot")
            {
                if(FailSlotOnce){FailSlotOnce=false;throw new ArgumentException("Injected slot declaration failure.");}
                SourceCalls++;var state=Surface((long)args[0]);state.Slots[(string)args[1]]=new SourceSlot{Provider=(string)args[2],Source=(string)args[3],Rect=(Vector4)args[4],Clip=(Vector4)args[5],UV=(Vector4)args[6],Order=(int)args[7]};result=true;return true;
            }
            if(command=="context-slot-remove"){SourceCalls++;result=Surface((long)args[0]).Slots.Remove((string)args[1]);return true;}
            if(command=="context-slot-status"){var slot=Surface((long)args[0]).Slots[(string)args[1]];result=new MyTuple<bool,string>(true,"Ready");return true;}
            if(command.StartsWith("context-slot-",StringComparison.Ordinal)){SourceCalls++;result=true;return true;}
            return false;
        }
        internal void RecordArtwork(string command,object[] args)
        {ArtworkCalls++;var state=Surface((long)args[0]);string id=(string)args[1];if(command=="item-order")state.Orders[id]=(int)args[2];if(command=="svg")state.SvgPoses[id]=(MatrixD)args[3];if(command=="remove"){state.Orders.Remove(id);state.SvgPoses.Remove(id);}}
    }
    static Block SourceAnchor()
    {var block=DispatchProxy.Create<Block,HtmlHostProxy>();((HtmlHostProxy)block).Handler=delegate(MethodInfo method,object[] values){if(method.Name=="get_EntityId")return 901L;throw new Exception("Unexpected anchor call: "+method.Name);};return block;}
    static MyTuple<string,object[]>[] AnchorSettings(Block block)
    {return new[]{new MyTuple<string,object[]>("anchor",new object[]{block})};}
    static void SurfaceCreationAndRayInput()
    {
        using(var bus=new Bus())
        {
            var core=new Core();bus.CoreService=core.Service;var session=new HtmlFrontendSession();session.BeforeStart();
            using(var helper=new HdrHtmlApi("surface-input"))
            {
                foreach(string kind in new[]{"plane","cylinder","sphere","ellipsoid","mesh"})foreach(string backend in new[]{"vector","svg"})
                {
                    object[] parameters=kind=="ellipsoid"?new object[]{new Vector3D(2,1,2),Math.PI*2,Math.PI,"inside"}:kind=="mesh"?new object[]{new[]{Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitY},new[]{0,1,2},new[]{Vector2.Zero,Vector2.UnitX,Vector2.UnitY},"outside"}:kind=="plane"?new object[0]:new object[]{2d,Math.PI*2,Math.PI,"inside"};
                    long document=helper.CreateSurface(ButtonMarkup,ButtonCss,400,200,MatrixD.Identity,.005,kind,parameters,null,backend);var state=core.Surfaces.Values.Single();
                    Equal(kind,state.Kind,"Core receives the requested generic surface kind");Equal(2d,state.Width,"Logical width maps to centered canvas metres");Equal(1d,state.Height,"Logical height maps to centered canvas metres");Check(state.Visible,"Admission publishes the fully configured owned context");
                    if(backend=="svg")foreach(var pose in state.SvgPoses.Values)Check(pose.Translation==Vector3D.Zero,"Mapped grouped SVG preserves centered source coordinates");
                    helper.PointerRay(document,new Vector3D(-.9,.425,2),-Vector3D.UnitZ,true);helper.PointerRay(document,new Vector3D(-.9,.425,2),-Vector3D.UnitZ,false);
                    var events=helper.PollEvents(document);Equal(1,events.Length,"Committed mapped HTML hit produces one click");Equal("button",events[0].Item2,"Ray hit maps back to authored HTML node");
                    helper.PointerRay(document,new Vector3D(20,20,2),-Vector3D.UnitZ,true);helper.PointerRay(document,new Vector3D(20,20,2),-Vector3D.UnitZ,false);Equal(0,helper.PollEvents(document).Length,"A surface-ray miss cannot click a prior button");
                    helper.Destroy(document);Equal(0,core.Surfaces.Count,"Destroy retires only the owned local surface context");
                }
                Equal(40,core.RayCalls,"Every supplied world ray uses Core's shared inverse query");
            }
        }
    }
    const string SourceMarkup="<div id='camera'></div><button id='button' data-action='activate'>Go</button><input id='range' type='range' min='0' max='100' step='1' value='0' data-action='gain'/>";
    const string SourceCss="div {width:80px;height:40px;background:#222222;} button {width:100px;height:30px;} input {width:100px;height:24px;}";
    static void LocalSourcesAndRollback()
    {
        using(var bus=new Bus())
        {
            var core=new Core();bus.CoreService=core.Service;var session=new HtmlFrontendSession();session.BeforeStart();
            using(var helper=new HdrHtmlApi("surface-sources"))
            {
                long document=helper.CreateSurface(SourceMarkup,SourceCss,400,200,MatrixD.Identity,.005,"ellipsoid",new object[]{new Vector3D(2,1,2)},AnchorSettings(SourceAnchor()));
                Check(helper.AttachSource(document,"camera","camera-panorama","101"),"Local source attaches to explicit authored region: "+helper.Status(document).Item4.Item3);var state=core.Surfaces.Values.Single();var pair=state.Slots.Single();
                Equal(new Vector4(-1,.3f,.4f,.2f),pair.Value.Rect,"Full content rectangle uses centered metres and lower-left Y");Equal(new Vector4(0,0,1,1),pair.Value.UV,"Full source UV remains stable under HTML clipping");Check(state.Orders.Values.Any(o=>o<pair.Value.Order)&&state.Orders.Values.Any(o=>o>pair.Value.Order),"Artwork is split before and after the embedded source");
                var before=helper.Status(document);int artwork=core.ArtworkCalls;
                helper.Pointer(document,20,82,true);helper.PollEvents(document);
                Check(helper.AttachSource(document,"camera","camera-panorama","102"),"Source replacement commits without document replacement");var after=helper.Status(document);
                Equal(before.Item2,after.Item2,"Source switch does not reparse or increment layout revision");Equal(before.Item5,after.Item5,"Source switch does not relayout");Equal(artwork,core.ArtworkCalls,"Source switch makes no retained artwork mutation");Equal(pair.Key,state.Slots.Single().Key,"Source switch retains slot identity");
                helper.Pointer(document,80,82,true);Check(helper.PollEvents(document).Any(e=>e.Item1=="change"&&e.Item2=="range"),"Held range continues through source replacement");
                core.FailSlotOnce=true;Check(!helper.AttachSource(document,"camera","camera-panorama","103"),"Failed source declaration reports a false commit");Equal("102",state.Slots.Single().Value.Source,"Rollback retains prior source declaration");Equal(artwork,core.ArtworkCalls,"Source-only failure rollback does not rewrite artwork");Equal(after.Item3,helper.Status(document).Item3,"Failed source batch retains visible document revision");
                helper.Pointer(document,60,82,true);Check(helper.PollEvents(document).Any(e=>e.Item1=="change"),"Held range remains active after confirmed source rollback");
                Check(helper.DetachSource(document,"camera"),"Detach retires the active source");Equal(0,state.Slots.Count,"Detach removes only the document-owned slot");Equal("Detached",helper.SourceStatus(document,"camera").Item2,"Detached source status is explicit");
            }
        }
    }
    static void HudSourcesAndPixels()
    {
        using(var bus=new Bus())
        {
            var core=new Core();bus.CoreService=core.Service;var session=new HtmlFrontendSession();session.BeforeStart();
            using(var helper=new HdrHtmlApi("hud-sources"))
            {
                long document=helper.CreateHud(SourceMarkup,SourceCss,400,200,"svg");helper.SetSourceAnchor(document,SourceAnchor());Check(helper.AttachSource(document,"camera","lcd-texture","901:0"),"Existing HUD document admits legitimate anchored native source: "+helper.Status(document).Item4.Item3);var state=core.Surfaces.Values.Single();Check(state.Kind==null,"HUD source uses no curved world surface");Equal(new Vector4(0,0,80,40),state.Slots.Single().Value.Rect,"HUD source uses existing top-left Y-down pixels");
                helper.Pointer(document,20,55,true);helper.Pointer(document,20,55,false);Equal(1,helper.PollEvents(document).Length,"HUD authored button input remains pixel-based");var before=helper.Status(document);int artwork=core.ArtworkCalls;Check(helper.AttachSource(document,"camera","lcd-texture","902:0"),"HUD source switch commits");Equal(artwork,core.ArtworkCalls,"HUD source switch does not rebuild retained artwork");Equal(before.Item5,helper.Status(document).Item5,"HUD source switch does not rebuild layout");
                Check(helper.DetachSource(document,"camera"),"HUD source detach succeeds");helper.Destroy(document);Equal(0,core.Surfaces.Count,"HUD cleanup retires its legitimate context");
            }
        }
    }
    static void LocalSourceCapabilityFences()
    {
        using(var bus=new Bus())
        {
            var core=new Core();bus.CoreService=core.Service;var session=new HtmlFrontendSession();session.BeforeStart();
            using(var helper=new HdrHtmlApi("source-fences"))
            {
                long document=helper.CreateSurface(SourceMarkup,SourceCss,400,200,MatrixD.Identity,.005,"plane",new object[0]);int calls=core.SourceCalls;
                Reject<ArgumentException>(()=>helper.AttachSource(document,"camera","camera-panorama","101"),"anchor");Equal(calls,core.SourceCalls,"Missing anchor rejects before source mutations");helper.SetSourceAnchor(document,SourceAnchor());
                Reject<ArgumentException>(()=>helper.AttachSource(document,"missing","camera-panorama","101"),"explicit");Equal(calls,core.SourceCalls,"Missing node rejects before source mutations");
                Reject<ArgumentException>(()=>helper.AttachSource(document,"camera","bad?","101"),"provider");Equal(calls,core.SourceCalls,"Invalid provider declaration rejects before source mutations");
                Reject<ArgumentException>(()=>helper.AttachSource(document,"camera","camera-panorama","101",new[]{new MyTuple<string,object[]>("opacity",new object[]{2d})}),"range");Equal(calls,core.SourceCalls,"Invalid source hints reject before source mutations");
                core.SourceSupported=false;Reject<ArgumentException>(()=>helper.AttachSource(document,"camera","camera-panorama","101"),"Unsupported");Equal(calls,core.SourceCalls,"Unsupported consumer negotiation rejects before source mutation");core.SourceSupported=true;
                Check(helper.AttachSource(document,"camera","camera-panorama","101"),"Valid source remains available after rejected attempts: "+helper.Status(document).Item4.Item3);Check(helper.SourceCapabilities(document).Contains("ordered-artwork"),"Capabilities describe actual generic ordered renderer");
                Check(helper.Replace(document,"<button id='button'>Go</button>",ButtonCss,400,200),"Replacement without attached node is staged");session.UpdateAfterSimulation();Equal(0,core.Surfaces.Values.Single().Slots.Count,"Missing committed node deactivates its desired source slot");Check(helper.SourceStatus(document,"camera").Item2.StartsWith("Hidden",StringComparison.Ordinal),"Missing region retains desired source as hidden");
                helper.Destroy(document);long native=helper.CreateNativeLcd(ButtonMarkup,ButtonCss,new Surface().Api,true);Reject<ArgumentException>(()=>helper.AttachSource(native,"button","camera-panorama","101"),"Unsupported");
            }
        }
    }
    static void LegacyWorldSourcesAndPose()
    {
        using(var bus=new Bus())
        {
            var core=new Core();bus.CoreService=core.Service;var session=new HtmlFrontendSession();session.BeforeStart();
            using(var helper=new HdrHtmlApi("legacy-world-sources"))
            {
                var pose=MatrixD.CreateRotationZ(.3)*MatrixD.CreateTranslation(3,4,5);
                long document=helper.CreateWorld(SourceMarkup,SourceCss,400,200,pose,.005,"svg");var state=core.Surfaces.Values.Single();
                Equal("plane",state.Kind,"Legacy world HTML reuses a legitimate plane surface");var center=MatrixD.CreateTranslation(1,-.5,0)*pose;Equal(center,state.Pose,"Legacy top-left pose derives exactly the matching source center");
                Equal(pose.Translation,Vector3D.Transform(new Vector3D(-1,.5,0),state.Pose),"The original authored top-left world point is unchanged");
                helper.SetSourceAnchor(document,SourceAnchor());Check(helper.AttachSource(document,"camera","camera-panorama","101"),"Existing world document can attach a native source without replacement");
                helper.Pointer(document,20,55,true);helper.Pointer(document,20,55,false);Equal(1,helper.PollEvents(document).Length,"Existing supplied pixel pointer semantics remain unchanged");
                Check(helper.Resize(document,600,300),"Legacy world resize is staged");session.UpdateAfterSimulation();Equal(MatrixD.CreateTranslation(1.5,-.75,0)*pose,state.Pose,"Resize retains original top-left pose with updated centered canvas");Check(Vector3D.DistanceSquared(pose.Translation,Vector3D.Transform(new Vector3D(-1.5,.75,0),state.Pose))<1e-20,"Resized document retains its original authored world origin");
                Equal(3d,state.Width,"Resize updates the declared physical canvas width");Check(helper.SourceStatus(document,"camera").Item1,"Source remains committed after ordinary resize");
            }
        }
    }
    static void HostedGeometryForwarding()
    {
        using(var bus=new Bus())
        {
            var session=new HtmlFrontendSession();session.BeforeStart();using(var helper=new HdrHtmlApi("allowance-without-core"))
            {Reject<ArgumentException>(()=>helper.GeometrySettings(),"Requires mod");Reject<ArgumentException>(()=>helper.GeometryLimit(20,20),"Requires mod");}
        }
        using(var bus=new Bus())
        {
            var core=new Core();bus.CoreService=core.Service;var session=new HtmlFrontendSession();session.BeforeStart();
            using(var helper=new HdrHtmlApi("hosted-allowance"))
            {
                Equal(new MyTuple<int,int>(0,0),helper.GeometrySettings(),"Hosted Core owner starts with unlimited aggregate allowances");
                helper.GeometryLimit(2,2);Equal(new MyTuple<int,int>(2,2),helper.GeometrySettings(),"Typed hosted helper forwards the exact finite tuple");
                int before=core.ArtworkCalls;Reject<ArgumentException>(()=>helper.CreateHud(ButtonMarkup,ButtonCss,400,200),"owner geometry");Equal(before,core.ArtworkCalls,"Finite hosted owner rejection occurs before artwork mutation");Equal(0,core.Surfaces.Count,"Finite hosted owner preflight allocates no renderer context");
                helper.GeometryLimit();Equal(new MyTuple<int,int>(0,0),helper.GeometrySettings(),"Default forwarding restores the explicit unlimited sentinel");
                long document=helper.CreateHud(ButtonMarkup,ButtonCss,400,200);Check(helper.Status(document).Item3>0,"Unlimited hosted owner publishes the same valid document");
                helper.GeometryLimit(20000,30000);Equal(new MyTuple<int,int>(20000,30000),helper.GeometrySettings(),"Hosted allowances above prior 8192 aggregate maximum remain configurable");
                Reject<ArgumentException>(()=>helper.GeometryLimit(-1,0),"negative");Equal(new MyTuple<int,int>(20000,30000),helper.GeometrySettings(),"Rejected allowance preserves prior hosted Core settings");
                helper.Destroy(document);
            }
        }
    }
}
