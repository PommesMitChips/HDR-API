using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using HDR.Interactions;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        const long ModClientDiscovery=481770130, ModClientRequest=481770131;
        readonly Dictionary<string,ModClientOwner> _modClientOwners=new Dictionary<string,ModClientOwner>();
        long _modClientGeneration;bool _modClientActive,_modClientDrawing;
        Func<string,object[],object> _modClientService;
        void InitializeModClientApi()
        {
            if(_modClientActive||MyAPIGateway.Utilities.IsDedicated)return;
            _modClientActive=true;_modClientService=ModClientService;
            MyAPIGateway.Utilities.RegisterMessageHandler(ModClientRequest,ReceiveModClientRequest);
            try{MyAPIGateway.Utilities.SendModMessage(ModClientDiscovery,_modClientService);}catch{}
        }
        void ReceiveModClientRequest(object message)
        {var receive=message as Action<Func<string,object[],object>>;if(!_modClientActive||receive==null)return;try{receive(_modClientService);}catch{}}
        object ModClientService(string command,object[] args)
        {
            if(!_modClientActive)throw new ArgumentException("HDR client service has stopped.");
            var a=new DrawArgs(args);
            if(command=="version"){a.End();return "HDR.ModClient/1";}
            if(command=="capabilities"){a.End();return new[]{"client-local","hud-vector-postpp","world-vector-depth","text","svg","registered-material-uv","cooperative-pointer","event-poll","hologram-effects","procedural-particles","hologram-transitions","constrained-controls","values-cas","cooperative-drag"};}
            if(command=="plugin-status"){string feature=a.Text();a.End();return LocalPluginStatus(feature);}
            if(command=="draw-budget"){if(_modClientDrawing)throw new ArgumentException("Configure budget outside Draw.");int limit=a.Integer(20000);a.End();if(limit<16||limit>131072)throw new ArgumentException("Shared mod draw budget requires 16–131072 triangles per frame.");_modClientDrawBudget=limit;return true;}
            if(command!="open")throw new ArgumentException("Unknown HDR mod service command.");
            if(_modClientDrawing)throw new ArgumentException("Open contexts outside HDR drawing.");
            string id=ModClientRules.Id(a.Text());a.End();ModClientOwner previous;
            if(!_modClientOwners.TryGetValue(id,out previous)&&_modClientOwners.Count>=ModClientRules.MaxOwners)throw new ArgumentException("Client consumer limit reached.");
            if(previous!=null){foreach(var old in previous.Contexts.Values)ModClientCancel(old,"owner-replaced");previous.Released=true;previous.Contexts.Clear();previous.DrawContexts.Clear();}
            _modClientDrawOwners.Clear();
            var owner=new ModClientOwner{Id=id,Generation=++_modClientGeneration};_modClientOwners[id]=owner;
            return new Func<string,object[],object>((op,values)=>ModClientCommand(owner,op,values));
        }
        bool ModClientCurrent(ModClientOwner owner)
        {ModClientOwner current;return _modClientActive&&!owner.Released&&_modClientOwners.TryGetValue(owner.Id,out current)&&ReferenceEquals(current,owner)&&current.Generation==owner.Generation;}
        object ModClientCommand(ModClientOwner owner,string op,object[] values)
        {
            if(op=="valid"){var validArgs=new DrawArgs(values);validArgs.End();return ModClientCurrent(owner);}
            if(!ModClientCurrent(owner))throw new ArgumentException("HDR consumer endpoint has been revoked.");
            if(_modClientDrawing)throw new ArgumentException("Mutate contexts on the client simulation thread outside Draw.");
            var a=new DrawArgs(values);
            if(op=="plugin-status"){string feature=a.Text();a.End();return LocalPluginStatus(feature);}
            if(op=="release"){a.End();foreach(var old in owner.Contexts.Values)ModClientCancel(old,"owner-released");owner.Released=true;owner.Contexts.Clear();owner.DrawContexts.Clear();_modClientDrawOwners.Clear();_modClientOwners.Remove(owner.Id);return true;}
            if(op=="create-hud"||op=="create-world")
            {
                int order=a.Integer(0);MatrixD pose=op=="create-world"?a.Typed<MatrixD>():MatrixD.Identity;a.End();ModClientRules.Transform(pose);
                if(owner.Contexts.Count>=ModClientRules.MaxContexts)throw new ArgumentException("Consumer context limit reached.");
                var context=new ModClientContext{Handle=++owner.NextHandle,Hud=op=="create-hud",Pose=pose,Order=order};owner.DrawContexts.Clear();owner.Contexts.Add(context.Handle,context);return context.Handle;
            }
            if(op=="draw-limit"){int limit=a.Integer(4096);a.End();if(limit<1||limit>8192)throw new ArgumentException("Draw limit requires 1–8192 primitives per client frame.");owner.DrawLimit=limit;return true;}
            if(op=="viewport"){a.End();var camera=MyAPIGateway.Session==null?null:MyAPIGateway.Session.Camera;return camera==null?Vector2.Zero:camera.ViewportSize;}
            long handle=a.Typed<long>();ModClientContext c;if(!owner.Contexts.TryGetValue(handle,out c))throw new ArgumentException("Context handle does not belong to this consumer.");
            if(op=="destroy"){a.End();ModClientCancel(c,"context-destroyed");c.DrawItems.Clear();c.PendingPoses.Clear();owner.DrawContexts.Clear();owner.Contexts.Remove(handle);return true;}
            if(op=="clear"){a.End();ModClientCancel(c,"context-cleared");c.Items.Clear();c.Controls.Clear();c.Values.Clear();c.ControlOrder.Clear();c.PendingPoses.Clear();c.DrawItems.Clear();c.Bounds.Clear();c.BoundsOrder.Clear();c.Events.Clear();c.Hover=null;c.Pressed=false;ModClientDeclare(owner,c);return true;}
            if(op=="context-visible"){bool visible=a.Flag();a.End();if(!visible)ModClientCancel(c,"context-hidden");c.Visible=visible;if(!c.Visible){c.Events.Clear();c.Hover=null;c.Pressed=false;}return true;}
            if(op=="context-pose"){var pose=a.Typed<MatrixD>();a.End();ModClientRules.Transform(pose);if(c.Hud)throw new ArgumentException("HUD context pose is determined by the viewer.");foreach(var old in c.Items.Values)ModClientRules.Placement(old.Geometry,old.Transform,pose,false);ModClientCancel(c,"context-pose");c.Pose=pose;return true;}
            if(op=="pointer")
            {double x=a.Number(),y=a.Number();bool pressed=a.Flag();a.End();if(c.Controls.Count==0&&c.Capture==null){if(!ClientRenderingEnabled)ModClientCancel(c,"display-hidden");else ModClientRules.Pointer(c,x,y,pressed);return true;}LocalRay ray;if(!LocalRay.TryCreate(new Vector3D(x,y,c.Hud?1:1000),-Vector3D.UnitZ,out ray))throw new ArgumentException("Pointer coordinates exceed local bounds.");ModClientPointer(owner,c,ray,pressed);return true;}
            if(op=="pointer-ray"){var origin=a.Point();var direction=a.Point();bool pressed=a.Flag();a.End();if(c.Hud)throw new ArgumentException("HUD pointer uses pixel coordinates.");LocalRay ray;if(!ModClientRules.SafePoint(origin)||!ModClientRay(origin,direction,MatrixD.Invert(c.Pose),out ray))throw new ArgumentException("Invalid finite world pointer ray.");ModClientPointer(owner,c,ray,pressed);return true;}
            if(op=="pointer-cancel"){a.End();ModClientCancel(c,"consumer-focus-loss");ModClientRules.PointerHit(c,null,false);return true;}
            if(op=="poll-value-events"){a.End();var result=c.ValueEvents.ToArray();c.ValueEvents.Clear();return result;}
            if(op=="poll-events"){a.End();var result=c.Events.ToArray();c.Events.Clear();return result;}
            string id=ModClientRules.Id(a.Text());
            object interactionResult;if(ModClientInteractionCommand(owner,c,op,id,a,out interactionResult))return interactionResult;
            if(op=="bounds"){var rect=a.Typed<Vector4>();a.End();ModClientRules.Bound(rect);if(!c.Bounds.ContainsKey(id)&&c.Bounds.Count>=64)throw new ArgumentException("Context hit region limit reached.");c.Bounds[id]=rect;c.BoundsOrder.Remove(id);c.BoundsOrder.Add(id);return true;}
            if(op=="remove-bounds"){a.End();if(c.Capture!=null&&c.Capture.Control.Id==id)ModClientCancel(c,"bounds-removed");c.Bounds.Remove(id);c.BoundsOrder.Remove(id);if(c.Hover==id)c.Hover=null;return true;}
            if(op=="remove"){a.End();ModClientRemoveItemControls(c,id);ModClientDeclare(owner,c);c.DrawItems.Clear();return c.Items.Remove(id);}
            if(op=="effect"||op=="effect-clear"||op=="transition")
            {
                ModClientItem found;if(!c.Items.TryGetValue(id,out found))throw new ArgumentException("Unknown context item.");
                if(op=="effect-clear")
                {string type=a.Has?a.Text().ToLowerInvariant():null;a.End();found.Effects=type==null?null:ClearHologramEffect(found.Effects,type);if(type==null){found.EffectStart=0;found.EffectEntering=true;}if(type==null||type=="beams"||type=="rays")found.EffectBeamFan=false;return true;}
                var previous=found.Effects;HologramEffectSettings settings;bool entering=found.EffectEntering,fan=found.EffectBeamFan;
                if(op=="effect")
                {string type=a.Text().ToLowerInvariant();settings=ParseHologramEffect(previous,type,a);a.End();if(type=="rays")fan=true;else if(type=="beams")fan=false;}
                else
                {
                    string direction=a.Text().ToLowerInvariant(),style=a.Text("fade").ToLowerInvariant();double duration=a.Number(.5);a.End();
                    if(direction!="in"&&direction!="out")throw new ArgumentException("Transition direction requires in or out.");
                    int mode=style=="fade"?1:style=="wipe"?2:style=="dissolve"?3:0;if(mode==0)throw new ArgumentException("Transition style requires fade, wipe or dissolve.");
                    var v=previous==null?HologramEffectSettings.DefaultValues():previous.Values();var f=previous==null?HologramEffectSettings.DefaultFlags():previous.Flags();v[16]=duration;f[5]=mode;settings=HologramEffectSettings.Create(v,f);entering=direction=="in";
                }
                // Screen-space contexts retain one near-plane depth. Structural effects have world semantics.
                if(c.Hud&&(settings.Depth>0&&settings.DepthLayers>0||settings.Beams>0||Math.Abs(settings.RevealAxis.Z)>1e-6))throw new ArgumentException("HUD effects are planar; volume, projection rays and Z reveal axes require a world context.");
                found.Effects=settings;found.EffectEntering=entering;found.EffectBeamFan=fan;
                if(op=="transition"||previous==null)found.EffectStart=ModClientEffectTime();
                ModClientEffectBounds(found);return true;
            }
            if(op=="visible"||op=="transform")
            {ModClientItem found;if(!c.Items.TryGetValue(id,out found))throw new ArgumentException("Unknown context item.");if(op=="visible"){bool visible=a.Flag();a.End();if(!visible)ModClientCancelArtwork(c,id,"artwork-hidden");found.Visible=visible;}else{var pose=a.Typed<MatrixD>();a.End();ModClientRules.Placement(found.Geometry,pose,c.Pose,c.Hud);found.Transform=pose;ModClientRebaseControls(owner,c,found);}return true;}
            ModClientItem item;
            if(op=="mesh")
            {var points=a.Typed<Vector3D[]>();var triangles=a.Typed<int[]>();var color=a.Paint("white");var uv=a.Has?a.Nullable<Vector2[]>():null;string material=a.Has?a.Nullable<string>():null;a.End();item=ModClientRules.Mesh(id,points,triangles,color,uv,material);if(material!=null)ModClientMaterial(material);}
            else if(op=="wires")
            {var points=a.Typed<Vector3D[]>();var edges=a.Typed<Vector2I[]>();var color=a.Paint("white");double width=a.Number(c.Hud?1:.01);a.End();ModClientRules.Color(color);if(width<=0||width>(c.Hud?256:100))throw new ArgumentException("Wire width out of range.");item=new ModClientItem{Id=id,Geometry=Geometry.Wires(points,edges),Color=color,Thickness=width};}
            else if(op=="text")
            {string text=a.Text();var position=a.Point();double height=a.Number();var color=a.Paint("white");string anchor=a.Text("start");a.End();Geometry.ValidatePoints(new[]{position});ModClientRules.Color(color);item=new ModClientItem{Id=id,Geometry=VectorFont.Text(text,height,anchor),Color=color,Transform=(c.Hud?MatrixD.CreateScale(1,-1,1):MatrixD.Identity)*MatrixD.CreateTranslation(position)};}
            else if(op=="svg")
            {string source=a.Text();var transform=a.Typed<MatrixD>();int segments=a.Integer(12);a.End();ModClientRules.Transform(transform);var svg=Svg.Parse(source,segments);item=new ModClientItem{Id=id,Geometry=svg.Geometry,Colors=svg.Colors,Color=Vector4.One,Transform=transform};}
            else throw new ArgumentException("Unknown HDR client command: "+op);
            ModClientRules.Placement(item.Geometry,item.Transform,c.Pose,c.Hud);
            ModClientRules.Admit(owner,c,item);
            ModClientItem prior;if(c.Items.TryGetValue(id,out prior)){item.Effects=prior.Effects;item.EffectStart=prior.EffectStart;item.EffectEntering=prior.EffectEntering;item.EffectBeamFan=prior.EffectBeamFan;}
            ModClientRemoveItemControls(c,id);ModClientEffectBounds(item);item.Order=++owner.NextOrder;c.DrawItems.Clear();c.Items[id]=item;return true;
        }
        static void ModClientMaterial(string name)
        {ModClientRules.Material(name);foreach(var definition in Sandbox.Definitions.MyDefinitionManager.Static.GetTransparentMaterialDefinitions())if(definition.Id.SubtypeName==name)return;throw new ArgumentException("Transparent material is not registered: "+name);}
        void UnloadModClientApi()
        {
            if(_modClientActive&&MyAPIGateway.Utilities!=null)MyAPIGateway.Utilities.UnregisterMessageHandler(ModClientRequest,ReceiveModClientRequest);
            CancelModClientInteractions("world-unload");_modClientActive=false;foreach(var owner in _modClientOwners.Values){owner.Released=true;owner.Contexts.Clear();owner.DrawContexts.Clear();}_modClientOwners.Clear();_modClientDrawOwners.Clear();_modClientEffectNow=_modClientEffectLastTime=0;_modClientService=null;
        }
    }
}
