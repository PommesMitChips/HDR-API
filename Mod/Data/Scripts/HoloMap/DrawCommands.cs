using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using PbBlock=Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace HoloMap
{
    // A single sandbox-compatible delegate. No mod-defined CLR types cross into a PB.
    public sealed partial class HoloMapSession
    {
        const string DrawPropertyId="HDR.Draw";
        sealed class DrawContext { public IMyProgrammableBlock Caller;public IMyTerminalBlock Target;public bool Paused;public string ScreenId; }
        readonly Dictionary<long,DrawContext> _drawContexts=new Dictionary<long,DrawContext>();
        void RegisterDrawApi()
        {
            var property=MyAPIGateway.TerminalControls.CreateProperty<Func<string,object[],object>,IMyProgrammableBlock>(DrawPropertyId);
            property.Getter=block=>DrawEndpoint(block as IMyProgrammableBlock);MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(property);
        }
        Func<string,object[],object> DrawEndpoint(IMyProgrammableBlock caller)
        {
            if(caller==null)throw new ArgumentException("Drawing endpoint requires a programmable block.");
            DrawContext context;if(!_drawContexts.TryGetValue(caller.EntityId,out context))
            {if(_drawContexts.Count>=128)throw new ArgumentException("Drawing context budget reached.");context=new DrawContext{Caller=caller};_drawContexts.Add(caller.EntityId,context);}
            context.Caller=caller;return (command,args)=>DrawCommand(context,command,args);
        }
        static void DrawCheck(MyTuple<bool,string> result){if(!result.Item1)throw new ArgumentException(result.Item2);}
        object DrawCommandCore(DrawContext context,string command,object[] values)
        {
            var caller=context.Caller;
            if(!MyAPIGateway.Multiplayer.IsServer)throw new ArgumentException("Drawing commands execute on the host/server through a PB.");
            if(caller==null||caller.Closed||!ReferenceEquals(MyAPIGateway.Entities.GetEntityById(caller.EntityId),caller))throw new ArgumentException("Drawing caller is no longer a registered PB.");
            if(string.IsNullOrWhiteSpace(command)||command.Length>64)throw new ArgumentException("Drawing command requires 1–64 characters.");
            var a=new DrawArgs(values);string op=command.ToLowerInvariant();
            // Utility queries are deliberately closed; there is no reflection or dynamic invocation.
            if(op=="version"){a.End();return "HDR.Draw/1";}
            if(op=="measure-text")return MeasureTextCommand(a);
            if(op=="geometry-cost")return GeometryCostCommand(a);
            if(op=="effect-types"){a.End();return HologramEffectTypes();}
            if(op=="capabilities"){var capabilityTarget=a.Has?a.Typed<PbBlock>():context.Target;a.End();return GetDisplayCapabilities(caller,capabilityTarget);}
            if(op=="plugin-status"){string feature=a.Text();a.End();return LocalPluginStatus(feature);}
            if(op=="rgb"){double r=a.Number(),g=a.Number(),b=a.Number();float alpha=(float)a.Number(1);a.End();if(r<0||r>255||g<0||g>255||b<0||b>255)throw new ArgumentException("RGB channels must be 0–255.");var color=new Vector4((float)r/255,(float)g/255,(float)b/255,alpha);Color(color);return color;}
            if(op=="rgba"){var c=new Vector4((float)a.Number(),(float)a.Number(),(float)a.Number(),(float)a.Number(1));a.End();Color(c);return c;}
            if(op=="pose"&&!(a.Peek is string)){var position=a.Point();double scale=a.Number(1);var rotation=new Vector3D(a.Number(0),a.Number(0),a.Number(0));a.End();var matrix=DrawPose(position,scale,rotation);ValidateTransform(matrix);return matrix;}
            if(op=="findtargets"||op=="findtarget"||op=="finddisplays"||op=="finddisplay")
            {
                // Accept the old helper's terminal argument during migration; discovery uses the caller's grid.
                if(a.Peek is Sandbox.ModAPI.Ingame.IMyGridTerminalSystem)a.Skip();string name=a.Text();a.End();var targets=DrawTargets(caller,name);
                if(op=="finddisplays"){var displays=new List<Sandbox.ModAPI.Ingame.IMyTerminalBlock>();foreach(var block in targets)displays.Add(block);return displays;}
                if(op=="findtarget")targets.RemoveAll(block=>!(block is IMyProjector));
                if(op=="findtargets"){var result=new List<Sandbox.ModAPI.Ingame.IMyProjector>();foreach(var block in targets)if(block is Sandbox.ModAPI.Ingame.IMyProjector)result.Add((Sandbox.ModAPI.Ingame.IMyProjector)block);return result;}
                var nearest=DrawNearest(caller,targets);if(nearest!=null)Authorize(caller,nearest);context.Target=nearest;return nearest;
            }
            if(op=="target")
            {
                object selected=a.Value();a.End();var target=selected as IMyTerminalBlock;
                if(selected is string)target=DrawNearest(caller,DrawTargets(caller,(string)selected));
                if(target==null)throw new ArgumentException("No Console/Projector/LCD target found.");Authorize(caller,target);context.Target=target;return true;
            }
            if(op=="pause"||op=="resume"||op=="updateanimations")
            {
                if(op=="updateanimations"){double elapsed=a.Number();if(elapsed<0)throw new ArgumentException("Elapsed time must be nonnegative.");}
                a.End();SetDrawPaused(context,op=="pause");return true; // animation time is now owned by the mod
            }
            if(op=="lcdgroup"){string name=a.Text();int columns=a.Integer(1),rows=a.Integer(1);double width=a.Number(2*columns),height=a.Number(2*rows);a.End();ConfigureLcdGroup(context,name,columns,rows,width,height);return true;}
            if(context.Target==null)throw new ArgumentException("Select a target first: H(\"target\", \"Holo Map\").");
            var targetBlock=Authorize(caller,context.Target);string id;
            if(op=="budget"||op=="render-budget"){int points=a.Integer(4096),primitives=a.Integer(8192),work=a.Integer(20000);a.End();SetRenderBudget(caller,targetBlock,points,primitives,work);return true;}
            if(op=="budget-settings"){a.End();var current=GetScene(targetBlock.EntityId);return new MyTuple<int,int,int>(current.PointBudget,current.PrimitiveBudget,current.DrawWorkBudget);}
            if(op=="lcd"){double width=a.Number(2),height=a.Number(2);a.End();ConfigureLcd(caller,targetBlock,width,height,1,1,0,0);return true;}
            if(op=="lcd-background"){var color=a.Paint();int surface=a.Integer(0);a.End();SetLcdBackground(caller,targetBlock,color,surface);return true;}
            if(op=="lcd-renderer"){string renderer=a.Text().ToLowerInvariant();a.End();SetLcdRenderer(caller,targetBlock,renderer);return true;}
            if(op=="lcd-refresh"){double hz=a.Number();a.End();SetLcdRefresh(caller,targetBlock,hz);return true;}
            if(op=="lcd-settings"){a.End();if(!(targetBlock is IMyTextPanel))throw new ArgumentException("LCD settings require an LCD panel.");var scene=GetScene(targetBlock.EntityId);return new MyTuple<string,double>(scene.LcdRenderer==1?"vector":"native",scene.LcdRefreshHz);}
            switch(op)
            {
                case "effect":
                {id=a.Text();string type=a.Text();var item=GetItem(caller,targetBlock,id);var effect=ParseHologramEffect(item.Effects,type,a);a.End();DrawCheck(SetHologramEffects(caller,targetBlock,id,new MyTuple<double[],int[]>(effect.Values(),effect.Flags())));return true;}
                case "effect-clear":
                {id=a.Text();if(!a.Has){a.End();DrawCheck(ClearHologramEffects(caller,targetBlock,id));return true;}string type=a.Text();a.End();var item=GetItem(caller,targetBlock,id);var effect=ClearHologramEffect(item.Effects,type);if(effect==null)DrawCheck(ClearHologramEffects(caller,targetBlock,id));else DrawCheck(SetHologramEffects(caller,targetBlock,id,new MyTuple<double[],int[]>(effect.Values(),effect.Flags())));return true;}
                case "effect-settings":{id=a.Text();a.End();return GetHologramEffects(caller,targetBlock,id);}
                case "transition":
                {id=a.Text();string direction=a.Text();if(direction!="in"&&direction!="out")throw new ArgumentException("Transition direction must be in or out.");string style=a.Text("fade");double effectDuration=a.Number(.5);a.End();DrawCheck(SetHologramTransition(caller,targetBlock,id,direction=="in",style,effectDuration));return true;}
                case "range": case "projection-range": {double range=a.Number(25);a.End();DrawCheck(SetDisplayRange(caller,targetBlock,range));return true;}
                case "volume": case "table-volume":
                {bool enabled=a.Flag(true);double height=a.Number(2),lowerX=a.Number(1),lowerZ=a.Number(0.75),upperX=a.Number(1.8),upperZ=a.Number(1.35),bottom=a.Number(0.1);a.End();DrawCheck(SetTableVolume(caller,targetBlock,enabled,height,lowerX,lowerZ,upperX,upperZ,bottom));return true;}
                case "layer-order": {string name=a.Text();int order=a.Integer(0);a.End();DrawCheck(SetLayerOrder(caller,targetBlock,name,order));return true;}
                case "lcd-release": a.End();ReleaseLcd(caller,targetBlock);return true;
                case "clear": a.End();DrawCheck(Clear(caller,targetBlock));ClearDrawAnimations(caller.EntityId,targetBlock.EntityId);return true;
                case "remove": id=a.Text();a.End();DrawCheck(Remove(caller,targetBlock,id));RemoveDrawAnimation(caller.EntityId,targetBlock.EntityId,id);return true;
                case "visible": case "setvisible": id=a.Text();bool visible=a.Flag();a.End();DrawCheck(SetVisible(caller,targetBlock,id,visible));return true;
                case "view": case "setview":
                {var offset=a.Point();Vector3D rotation;double scale;if(a.Peek is Vector3D){rotation=a.Point();scale=a.Number(1);}else{scale=a.Number(1);rotation=new Vector3D(a.Number(0),a.Number(0),a.Number(0));}a.End();DrawCheck(SetView(caller,targetBlock,offset,rotation,scale));return true;}
                case "transform": case "settransform": id=a.Text();var matrix=a.Typed<MatrixD>();a.End();DrawCheck(SetTransform(caller,targetBlock,id,matrix));ResetDrawAnimation(caller,targetBlock,id,matrix);return true;
                case "pose": id=a.Text();var position=a.Point();double size=a.Number(1);var angles=new Vector3D(a.Number(0),a.Number(0),a.Number(0));a.End();matrix=DrawPose(position,size,angles);DrawCheck(SetTransform(caller,targetBlock,id,matrix));ResetDrawAnimation(caller,targetBlock,id,matrix);return true;
                case "move": id=a.Text();position=a.Point();a.End();matrix=GetItem(caller,targetBlock,id).Transform;matrix.Translation=position;DrawCheck(SetTransform(caller,targetBlock,id,matrix));ResetDrawAnimation(caller,targetBlock,id,matrix);return true;
                case "line": case "putline":
                {id=a.Text();var from=a.Point();var to=a.Point();var c=a.Paint();float width=(float)a.Number(0.008);a.End();DrawCheck(PutWires(caller,targetBlock,id,new[]{from,to},new[]{new Vector2I(0,1)},c,width));return true;}
                case "circle": case "putcircle":
                {id=a.Text();var center=a.Point();double radius=a.Number();var c=a.Paint();int segments=a.Integer(64);float width=(float)a.Number(0.008);var normal=a.Has?a.Point():Vector3D.UnitZ;a.End();DrawCircle(caller,targetBlock,id,center,radius,c,segments,width,normal);return true;}
                case "curve": case "putcurve":
                {id=a.Text();var function=a.Typed<Func<double,Vector3D>>();double start=a.Number(),end=a.Number();var c=a.Paint();int segments=a.Integer(128);float width=(float)a.Number(0.008);bool closed=a.Flag(false);a.End();var points=DrawSample(function,start,end,segments,closed);var edges=new Vector2I[segments];for(int i=0;i<edges.Length;i++)edges[i]=new Vector2I(i,(i+1)%points.Length);DrawCheck(PutWires(caller,targetBlock,id,points,edges,c,width));return true;}
                case "wires": case "putwires": id=a.Text();var pts=a.Typed<Vector3D[]>();var connections=a.Typed<Vector2I[]>();var color=a.Paint();float thickness=(float)a.Number(0.008);a.End();DrawCheck(PutWires(caller,targetBlock,id,pts,connections,color,thickness));return true;
                case "polygons": case "putpolygons":
                {id=a.Text();pts=a.Typed<Vector3D[]>();var faces=a.Typed<int[][]>();var outline=a.Paint();var fill=a.Paint("#00dfff");thickness=(float)a.Number(0.008);bool shaded=a.Flag(true);a.End();DrawCheck(PutPolygons(caller,targetBlock,id,pts,faces,new MyTuple<Vector4,Vector4,float,bool>(outline,fill,thickness,shaded)));return true;}
                case "contours": case "putcontours":
                {id=a.Text();var contours=a.Typed<Vector3D[][]>();var outline=a.Paint();var fill=a.Paint();thickness=(float)a.Number(0.008);bool shaded=a.Flag(false),evenOdd=a.Flag(true);a.End();DrawCheck(PutContours(caller,targetBlock,id,contours,new MyTuple<Vector4,Vector4,float,bool>(outline,fill,thickness,shaded),evenOdd));return true;}
                case "points": case "vertices": case "putvertices":
                {id=a.Text();pts=a.Typed<Vector3D[]>();color=a.Paint();double radius=a.Number(0.01);thickness=(float)a.Number(0.004);a.End();DrawVertices(caller,targetBlock,id,pts,color,radius,thickness);return true;}
                case "svg": case "putsvg": case "svg-asset": case "putsvgasset":
                {id=a.Text();string svg=a.Text();matrix=a.Peek is MatrixD?a.Typed<MatrixD>():DrawPose(a.Has?a.Point():Vector3D.Zero,a.Number(0.01),Vector3D.Zero);int segments=a.Integer(12);a.End();var options=new MyTuple<MatrixD,int>(matrix,segments);DrawCheck(op=="svg-asset"||op=="putsvgasset"?PutSvgAsset(caller,targetBlock,id,svg,options):PutSvg(caller,targetBlock,id,svg,options));ResetDrawAnimation(caller,targetBlock,id,matrix);return true;}
                case "image": case "putimage":
                {id=a.Text();string asset=a.Text();Vector2 dimensions=a.Peek is Vector2?a.Typed<Vector2>():new Vector2((float)a.Number(),(float)a.Number());color=a.Paint("white");a.End();DrawCheck(PutImage(caller,targetBlock,id,asset,dimensions,color));return true;}
                case "text": case "label":
                {id=a.Text();string text=a.Text();position=a.Has?a.Point():Vector3D.Zero;double height=a.Number(op=="label"?0.035:0.1);color=a.Paint("white");string anchor=a.Text("middle");a.End();if(op=="label")DrawCheck(PutLabel(caller,targetBlock,id,position,text,color,(float)height));else{matrix=DrawPose(position,1,Vector3D.Zero);DrawCheck(PutText(caller,targetBlock,id,text,color,new MyTuple<double,MatrixD,string>(height,matrix,anchor)));ResetDrawAnimation(caller,targetBlock,id,matrix);}return true;}
                case "putlabel": id=a.Text();position=a.Point();string label=a.Text();color=a.Paint("white");float labelHeight=(float)a.Number(0.035);a.End();DrawCheck(PutLabel(caller,targetBlock,id,position,label,color,labelHeight));return true;
                case "puttext": id=a.Text();string words=a.Text();color=a.Paint("white");double textHeight=a.Number();matrix=a.Typed<MatrixD>();string align=a.Text("middle");a.End();DrawCheck(PutText(caller,targetBlock,id,words,color,new MyTuple<double,MatrixD,string>(textHeight,matrix,align)));ResetDrawAnimation(caller,targetBlock,id,matrix);return true;
                case "opacity": case "setopacity": case "transparency": case "settransparency": case "emission": case "setemission":
                {id=a.Text();float value=(float)a.Number();a.End();if(op=="transparency"||op=="settransparency"){LayerRules.Opacity(value);value=1-value;}DrawCheck(op=="emission"||op=="setemission"?SetEmission(caller,targetBlock,id,value):SetOpacity(caller,targetBlock,id,value));return true;}
                case "clip": case "setclip": id=a.Text();var planes=a.Has?a.Nullable<Vector4[]>():null;a.End();DrawCheck(SetClip(caller,targetBlock,id,planes));return true;
                case "clip-box": case "setclipbox": id=a.Text();var min=a.Point();var max=a.Point();a.End();DrawCheck(SetClip(caller,targetBlock,id,DrawBox(min,max)));return true;
                case "clip-circle": case "setclipcircle":
                {id=a.Text();var center=a.Peek is Vector2?a.Typed<Vector2>():new Vector2((float)a.Number(),(float)a.Number());double radius=a.Number();int sides=a.Integer(12);a.End();DrawCheck(SetClip(caller,targetBlock,id,DrawCircleClip(center,radius,sides)));return true;}
                case "gradient": case "setgradient":
                {id=a.Text();var from=a.Point();var to=a.Point();var start=a.Paint();var end=a.Paint();int resolution=a.Integer(3);bool radial=a.Flag(false);a.End();DrawCheck(SetGradient(caller,targetBlock,id,new MyTuple<Vector3D,Vector3D,Vector4,Vector4>(from,to,start,end),new MyTuple<int,bool>(resolution,radial)));return true;}
                case "cleargradient": id=a.Text();a.End();DrawCheck(SetGradient(caller,targetBlock,id,new MyTuple<Vector3D,Vector3D,Vector4,Vector4>(),new MyTuple<int,bool>(0,false)));return true;
                case "live": case "trackconstruct":
                {bool enabled=op=="live"?true:a.Flag(true);double radius=a.Number(0.55);var offset=a.Has?a.Point():Vector3D.Zero;a.End();DrawCheck(TrackConstruct(caller,targetBlock,enabled,radius,offset));return true;}
                case "track-world": case "trackconstructworld": double metres=a.Number();var origin=a.Point();a.End();DrawCheck(TrackConstructWorld(caller,targetBlock,true,metres,origin));return true;
                case "layer": case "setobjectlayer": id=a.Text();string layer=a.Text();a.End();DrawCheck(SetObjectLayer(caller,targetBlock,id,layer));return true;
                case "construct-layer": case "setconstructlayer": layer=a.Text();a.End();DrawCheck(SetConstructLayer(caller,targetBlock,layer));return true;
                case "layer-visible": case "setlayervisible": layer=a.Text();visible=a.Flag();a.End();DrawCheck(SetLayerVisible(caller,targetBlock,layer,visible));return true;
                case "layer-opacity": case "setlayeropacity": layer=a.Text();float alpha=(float)a.Number();a.End();DrawCheck(SetLayerOpacity(caller,targetBlock,layer,alpha));return true;
                case "layer-state": case "getlayerstate": layer=a.Text();a.End();var state=GetLayerState(caller,targetBlock,layer);if(!state.Item1)throw new ArgumentException(state.Item2);return new MyTuple<bool,float>(state.Item3,state.Item4);
                case "toggle": case "togglelayer": layer=a.Text();a.End();state=GetLayerState(caller,targetBlock,layer);if(!state.Item1)throw new ArgumentException(state.Item2);DrawCheck(SetLayerVisible(caller,targetBlock,layer,!state.Item3));return !state.Item3;
                case "solo": case "sololayer": layer=a.Text();var layers=a.Typed<string[]>();a.End();bool found=false;foreach(string name in layers)if(name==layer)found=true;if(!found)throw new ArgumentException("Selected layer is not in the supplied list.");foreach(string name in layers)DrawCheck(SetLayerVisible(caller,targetBlock,name,name==layer));return true;
                case "packedframe": case "putpackedanimationframe":
                {id=a.Text();string data=a.Text();double time=a.Number();matrix=a.Peek is MatrixD?a.Typed<MatrixD>():DrawPose(a.Has?a.Point():Vector3D.Zero,a.Number(0.003),Vector3D.Zero);int segments=a.Integer(3);a.End();DrawCheck(PutPackedAnimationFrame(caller,targetBlock,id,data,time,new MyTuple<MatrixD,int>(matrix,segments)));return true;}
                case "animate": id=a.Text();string channel=a.Text();double fromValue=a.Number(),toValue=a.Number(),duration=a.Number();bool loop=a.Flag(false),ping=a.Flag(false);a.End();BeginDrawAnimation(context,id,channel,fromValue,toValue,duration,loop,ping);return true;
                case "stopanimation": id=a.Text();a.End();StopDrawAnimation(caller.EntityId,targetBlock.EntityId,id);return true;
                case "playsvgframes":
                {id=a.Text();var frames=a.Typed<string[]>();double fps=a.Number();position=a.Point();double scale=a.Number(0.01);bool frameLoop=a.Flag(true);int segments=a.Integer(12);a.End();BeginDrawFrames(context,id,frames,fps,DrawPose(position,scale,Vector3D.Zero),frameLoop,segments);return true;}
                case "play":
                {id=a.Text();string data=a.Text();position=a.Has?a.Point():Vector3D.Zero;double scale=a.Number(0.003);int segments=a.Integer(3);a.End();BeginDrawPacked(context,id,data,DrawPose(position,scale,Vector3D.Zero),segments);return true;}
                default:throw new ArgumentException("Unknown drawing command: "+command);
            }
        }
        List<IMyTerminalBlock> DrawTargets(IMyProgrammableBlock caller,string name)
        {if(string.IsNullOrWhiteSpace(name)||name.Length>128)throw new ArgumentException("Target name requires 1–128 characters.");var result=new List<IMyTerminalBlock>();MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(caller.CubeGrid).GetBlocksOfType<IMyTerminalBlock>(result,b=>b.CustomName==name&&b.IsSameConstructAs(caller)&&(b is IMyProjector||b is IMyTextPanel));return result;}
        static IMyTerminalBlock DrawNearest(IMyProgrammableBlock caller,List<IMyTerminalBlock> targets)
        {IMyTerminalBlock result=null;double distance=double.MaxValue;foreach(var block in targets){double d=Vector3D.DistanceSquared(caller.GetPosition(),block.GetPosition());if(result==null||d<distance||d==distance&&block.EntityId<result.EntityId){result=block;distance=d;}}return result;}
        static MatrixD DrawPose(Vector3D position,double scale,Vector3D rotation){return MatrixD.CreateScale(scale)*MatrixD.CreateFromYawPitchRoll(rotation.Y,rotation.X,rotation.Z)*MatrixD.CreateTranslation(position);}
        void DrawCircle(PbBlock caller,PbBlock target,string id,Vector3D center,double radius,Vector4 color,int segments,float width,Vector3D normal)
        {if(!Geometry.Finite(radius)||radius<=0||!Geometry.Finite(normal.X)||!Geometry.Finite(normal.Y)||!Geometry.Finite(normal.Z)||normal.LengthSquared()<1e-20)throw new ArgumentException("Invalid circle radius/normal.");normal.Normalize();var seed=Math.Abs(normal.X)<0.8?Vector3D.UnitX:Vector3D.UnitY;var u=Vector3D.Normalize(Vector3D.Cross(normal,seed));var v=Vector3D.Cross(normal,u);var p=DrawSample(t=>center+radius*(Math.Cos(t)*u+Math.Sin(t)*v),0,2*Math.PI,segments,true);var e=new Vector2I[segments];for(int i=0;i<segments;i++)e[i]=new Vector2I(i,(i+1)%segments);DrawCheck(PutWires(caller,target,id,p,e,color,width));}
        public static Vector3D[] DrawSample(Func<double,Vector3D> f,double start,double end,int segments,bool closed)
        {if(f==null||!Geometry.Finite(start)||!Geometry.Finite(end)||!Geometry.Finite(end-start)||start==end||segments<(closed?3:1)||segments>(closed?2048:2047))throw new ArgumentException("Invalid curve function, bounds or segment count.");var p=new Vector3D[closed?segments:segments+1];for(int i=0;i<p.Length;i++)p[i]=f(start+(end-start)*i/segments);Geometry.ValidatePoints(p);return p;}
        void DrawVertices(PbBlock caller,PbBlock target,string id,Vector3D[] positions,Vector4 color,double radius,float width)
        {if(positions==null||positions.Length<1||positions.Length>341||!Geometry.Finite(radius)||radius<=0)throw new ArgumentException("Invalid vertex marker count/radius.");var p=new Vector3D[positions.Length*6];var e=new Vector2I[positions.Length*3];for(int i=0;i<positions.Length;i++)for(int axis=0;axis<3;axis++){var d=(axis==0?Vector3D.UnitX:axis==1?Vector3D.UnitY:Vector3D.UnitZ)*radius;int j=i*6+axis*2;p[j]=positions[i]-d;p[j+1]=positions[i]+d;e[i*3+axis]=new Vector2I(j,j+1);}DrawCheck(PutWires(caller,target,id,p,e,color,width));}
        static Vector4[] DrawBox(Vector3D min,Vector3D max)
        {Geometry.ValidatePoints(new[]{min,max});if(min.X>=max.X||min.Y>=max.Y||min.Z>=max.Z)throw new ArgumentException("Clip box corners must be ordered.");return new[]{new Vector4(1,0,0,(float)-min.X),new Vector4(-1,0,0,(float)max.X),new Vector4(0,1,0,(float)-min.Y),new Vector4(0,-1,0,(float)max.Y),new Vector4(0,0,1,(float)-min.Z),new Vector4(0,0,-1,(float)max.Z)};}
        static Vector4[] DrawCircleClip(Vector2 center,double radius,int sides)
        {if(!Geometry.Finite(center.X)||!Geometry.Finite(center.Y)||!Geometry.Finite(radius)||radius<=0||sides<3||sides>16)throw new ArgumentException("Invalid circular clip.");var p=new Vector4[sides];for(int i=0;i<sides;i++){double t=2*Math.PI*i/sides,x=Math.Cos(t),y=Math.Sin(t);p[i]=new Vector4((float)-x,(float)-y,0,(float)(radius+center.X*x+center.Y*y));}return p;}
    }
    public sealed class DrawArgs
    {
        readonly object[] values;int position;
        public DrawArgs(object[] args){values=args??new object[0];if(values.Length>32)throw new ArgumentException("Drawing commands accept at most 32 arguments.");}
        public bool Has{get{return position<values.Length;}}
        public object Peek{get{return Has?values[position]:null;}}
        public void Skip(){Value();}
        public object Value(){if(!Has)throw new ArgumentException("Missing drawing argument.");return values[position++];}
        public T Typed<T>(){var value=Value();if(!(value is T))throw new ArgumentException("Wrong drawing argument type.");return (T)value;}
        public T Nullable<T>() where T:class{var value=Value();if(value==null)return null;if(!(value is T))throw new ArgumentException("Wrong drawing argument type.");return (T)value;}
        public string Text(){return Typed<string>();}
        public string Text(string fallback){return Has?Text():fallback;}
        public double Number(){object v=Value();double n;if(v is double)n=(double)v;else if(v is float)n=(float)v;else if(v is int)n=(int)v;else if(v is long)n=(long)v;else if(v is byte)n=(byte)v;else throw new ArgumentException("Expected a numeric drawing argument.");if(!Geometry.Finite(n))throw new ArgumentException("Drawing numbers must be finite.");return n;}
        public double Number(double fallback){return Has?Number():fallback;}
        public int Integer(int fallback){double n=Number(fallback);if(n<int.MinValue||n>int.MaxValue||n!=(int)n)throw new ArgumentException("Expected an integer drawing argument.");return (int)n;}
        public bool Flag(){return Typed<bool>();}
        public bool Flag(bool fallback){return Has?Flag():fallback;}
        public Vector3D Point(){if(Peek is Vector3D)return Typed<Vector3D>();return new Vector3D(Number(),Number(),Number());}
        public Vector4 Paint(string fallback="cyan")
        {if(!Has)return Svg.ParseColor(fallback);object v=Value();if(v is Vector4)return (Vector4)v;if(v is string){string s=((string)v).ToLowerInvariant();if(s=="orange")s="#ff9a30";return Svg.ParseColor(s);}throw new ArgumentException("Paint must be an RGBA Vector4 or color name/hex string.");}
        public void End(){if(Has)throw new ArgumentException("Too many drawing arguments for this command.");}
    }
}


