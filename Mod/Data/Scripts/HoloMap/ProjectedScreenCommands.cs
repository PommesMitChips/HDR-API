using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using VRage.Game.GUI.TextPanel;
using PbBlock=Sandbox.ModAPI.Ingame.IMyTerminalBlock;
namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  string _screenWritePrefix,_screenWriteLayerPrefix;long _screenWriteCaller,_screenWriteAnchor;
  void ValidateWriteId(PbBlock caller,PbBlock target,string id)
  {ValidateId(id);if(IsProjectedId(id)&&(_screenWritePrefix==null||caller.EntityId!=_screenWriteCaller||target.EntityId!=_screenWriteAnchor||!id.StartsWith(_screenWritePrefix,StringComparison.Ordinal)))throw new ArgumentException("Projected content IDs are reserved; select a screen through HDR.Draw.");}
  void ValidateWriteLayer(Scene scene,long caller,string name)
  {if(name.StartsWith("s_",StringComparison.Ordinal)&&name.IndexOf("__",StringComparison.Ordinal)>2&&(_screenWriteLayerPrefix==null||caller!=_screenWriteCaller||scene.ConsoleId!=_screenWriteAnchor||!name.StartsWith(_screenWriteLayerPrefix,StringComparison.Ordinal)))throw new ArgumentException("Projected layer names are reserved.");}
  object DrawCommand(DrawContext context,string command,object[] values)
  {
   string previous=_screenWritePrefix,layerPrevious=_screenWriteLayerPrefix;long oldCaller=_screenWriteCaller,oldAnchor=_screenWriteAnchor;
   _screenWritePrefix=null;_screenWriteLayerPrefix=null;_screenWriteCaller=0;_screenWriteAnchor=0;
   try
   {
    string op=command==null?null:command.ToLowerInvariant();
    if(op=="target"||op=="finddisplay"||op=="findtarget"||op=="lcdgroup")context.ScreenId=null;
    if(op!=null&&(op.StartsWith("screen",StringComparison.Ordinal)||op=="sprites"))return ProjectedCommand(context,op,new DrawArgs(values));
    if(context.ScreenId==null||op=="capabilities"||op=="plugin-status"||op=="effect-types"||op=="measure-text"||op=="geometry-cost")return DrawCommandCore(context,command,values);
    var screen=SelectedScreen(context);string prefix=ScreenPrefix(context.ScreenId);_screenWritePrefix=prefix;_screenWriteLayerPrefix=ScreenLayer(context.ScreenId,"x").Substring(0,ScreenLayer(context.ScreenId,"x").Length-1);_screenWriteCaller=context.Caller.EntityId;_screenWriteAnchor=context.Target.EntityId;
    if(op=="clear"){new DrawArgs(values).End();ClearScreenContent(GetScene(context.Target.EntityId),screen);return true;}
    if(op=="view"||op=="setview")
    {var args=new DrawArgs(values);var offset=args.Point();Vector3D rotation;double scale;if(args.Peek is Vector3D){rotation=args.Point();scale=args.Number(1);}else{scale=args.Number(1);rotation=new Vector3D(args.Number(0),args.Number(0),args.Number(0));}args.End();var d=CloneScreen(screen.Data);d.View=new[]{offset.X,offset.Y,offset.Z,rotation.X,rotation.Y,rotation.Z,scale};CommitScreen(context,d);return true;}
    // Closed command vocabulary: no anchor-wide controls or PB callbacks in a child scope.
    bool objectCommand=false,layerCommand=false;int secondLayer=-1;
    switch(op)
    {
     case "line":case "putline":case "circle":case "putcircle":case "wires":case "putwires":case "polygons":case "putpolygons":case "contours":case "putcontours":case "points":case "vertices":case "putvertices":case "svg":case "putsvg":case "svg-asset":case "putsvgasset":case "image":case "putimage":case "text":case "label":case "putlabel":case "puttext":case "remove":case "visible":case "setvisible":case "transform":case "settransform":case "opacity":case "setopacity":case "transparency":case "settransparency":case "emission":case "setemission":case "clip":case "setclip":case "clip-box":case "setclipbox":case "clip-circle":case "setclipcircle":case "gradient":case "setgradient":case "animate":case "stopanimation":case "playsvgframes":case "play":objectCommand=true;break;
     case "effect":case "effect-clear":case "effect-settings":case "transition":objectCommand=true;break;
     case "pose":objectCommand=values!=null&&values.Length>0&&values[0] is string;break;
     case "layer":case "setobjectlayer":objectCommand=true;secondLayer=1;break;
     case "layer-remove":case "layer-order":case "layer-visible":case "setlayervisible":case "layer-opacity":case "setlayeropacity":case "layer-state":case "getlayerstate":case "toggle":case "togglelayer":case "solo":case "sololayer":layerCommand=true;break;
     case "rgb":case "rgba":case "version":case "findtargets":case "finddisplays":break;
     default:throw new ArgumentException("This command is not supported in a projected screen. Select the anchor again for anchor controls.");
    }
    var copied=values==null?new object[0]:(object[])values.Clone();
    if(objectCommand){if(copied.Length==0||!(copied[0] is string))throw new ArgumentException("Missing screen object ID.");string local=(string)copied[0];ValidateId(local);if(local.Length>32||local.IndexOf('!')>=0||local.IndexOf('~')>=0)throw new ArgumentException("Screen object IDs support at most 32 characters without ! or ~.");copied[0]=prefix+local;}
    if(layerCommand){if(copied.Length==0||!(copied[0] is string))throw new ArgumentException("Missing layer name.");copied[0]=ScreenLayer(context.ScreenId,(string)copied[0]);if(op=="solo"||op=="sololayer"){if(copied.Length<2||!(copied[1] is string[]))throw new ArgumentException("Missing layer list.");var names=(string[])copied[1];var mapped=new string[names.Length];for(int i=0;i<names.Length;i++)mapped[i]=ScreenLayer(context.ScreenId,names[i]);copied[1]=mapped;}}
    if(secondLayer>=0){if(copied.Length<=secondLayer||!(copied[secondLayer] is string))throw new ArgumentException("Missing layer name.");copied[secondLayer]=ScreenLayer(context.ScreenId,(string)copied[secondLayer]);}
    return DrawCommandCore(context,command,copied);
   }
   finally{_screenWritePrefix=previous;_screenWriteLayerPrefix=layerPrevious;_screenWriteCaller=oldCaller;_screenWriteAnchor=oldAnchor;}
  }
  ProjectedScreen SelectedScreen(DrawContext c)
  {if(c.Target==null||c.ScreenId==null)throw new ArgumentException("Select a projected screen first.");Authorize(c.Caller,c.Target);Scene scene;ProjectedScreen s;if(!_scenes.TryGetValue(c.Target.EntityId,out scene)||!scene.Screens.TryGetValue(Key(c.Caller.EntityId,c.ScreenId),out s))throw new ArgumentException("Selected screen no longer exists for this PB.");return s;}
  object ProjectedCommand(DrawContext c,string op,DrawArgs a)
  {
   if(c.Caller==null||c.Caller.Closed||!ReferenceEquals(MyAPIGateway.Entities.GetEntityById(c.Caller.EntityId),c.Caller)||c.Target==null)throw new ArgumentException("Select a live display anchor first.");var target=Authorize(c.Caller,c.Target);
   if(!(target is IMyProjector))throw new ArgumentException("Floating HDR screens require a Console or Projector anchor.");
   if(op=="screen-exists"){string id=ScreenId(a.Text());a.End();Scene scene;return _scenes.TryGetValue(target.EntityId,out scene)&&scene.Screens.ContainsKey(Key(c.Caller.EntityId,id));}
   if(op=="screen-slot-validate"){string provider=a.Text(),source=a.Text();a.End();ValidateDisplaySource(provider,source);if(string.IsNullOrEmpty(provider))throw new ArgumentException("Source slots require an explicit provider.");if(provider=="native-portal"){ScreenId(source);Scene scene;ProjectedScreen portal;if(!_scenes.TryGetValue(target.EntityId,out scene)||!scene.Screens.TryGetValue(Key(c.Caller.EntityId,source),out portal)||portal.Data.SourceProvider!="native-portal"||portal.Data.Portal==null)throw new ArgumentException("Native portal video windows require a real declared portal source screen.");}return true;}
   if(op=="screen-target") {string id=a.Text();a.End();if(id==""){c.ScreenId=null;return true;}ScreenId(id);var scene=GetScene(target.EntityId);if(!scene.Screens.ContainsKey(Key(c.Caller.EntityId,id)))throw new ArgumentException("Screen does not exist for this PB.");c.ScreenId=id;return true;}
   if(op=="screen")
   {string id=ScreenId(a.Text());if(!a.Has){a.End();var scene=GetScene(target.EntityId);if(!scene.Screens.ContainsKey(Key(c.Caller.EntityId,id)))throw new ArgumentException("Screen does not exist for this PB.");c.ScreenId=id;return true;}var pose=a.Typed<MatrixD>();double w=a.Number(2),h=a.Number(1.125),cw=a.Number(w),ch=a.Number(h);a.End();var scene2=GetScene(target.EntityId);ProjectedScreen old;var d=scene2.Screens.TryGetValue(Key(c.Caller.EntityId,id),out old)?CloneScreen(old.Data):new HoloProjectedScreenData{CallerId=c.Caller.EntityId,Id=id};d.Pose=MatrixValues(pose);d.Width=w;d.Height=h;d.CanvasWidth=cw;d.CanvasHeight=ch;CommitScreen(c,d);c.ScreenId=id;return true;}
   var selected=SelectedScreen(c);var data=CloneScreen(selected.Data);
   if(op=="screen-slot"||op.StartsWith("screen-slot-",StringComparison.Ordinal))return SourceSlotCommand(c,op,a,data);
   switch(op)
   {
    case "screen-portal-plane":
    {var p=data.Portal??NewPortal();p.ExitKind=0;p.ExitPose=MatrixValues(a.Typed<MatrixD>());if(data.Portal==null)p.ShellPose=(double[])p.ExitPose.Clone();p.ExitExtent=new[]{a.Number(),a.Number(),0d};p.ExitPoints=null;p.ExitTriangles=null;p.ExitUV=null;p.ExitChart=0;PortalModes(p,a);data.Portal=p;data.Aspect="stretch";data.SourceProvider="native-portal";data.SourceId=data.Id;break;}
    case "screen-portal-ellipsoid":
    {var p=data.Portal??NewPortal();p.ExitKind=1;p.ExitPose=MatrixValues(a.Typed<MatrixD>());if(data.Portal==null)p.ShellPose=(double[])p.ExitPose.Clone();var r=a.Point();p.ExitExtent=new[]{r.X,r.Y,r.Z};p.ExitPoints=null;p.ExitTriangles=null;p.ExitUV=null;p.ExitChart=0;PortalModes(p,a);data.Portal=p;data.Aspect="stretch";data.SourceProvider="native-portal";data.SourceId=data.Id;break;}
    case "screen-portal-mesh":
    {var p=data.Portal??NewPortal();p.ExitKind=2;p.ExitPose=MatrixValues(a.Typed<MatrixD>());if(data.Portal==null)p.ShellPose=(double[])p.ExitPose.Clone();var points=a.Typed<Vector3D[]>();var triangles=a.Typed<int[]>();var uv=a.Typed<Vector2[]>();if(points.Length<3||points.Length>2048||triangles.Length<3||triangles.Length%3!=0||triangles.Length>4096*3||uv.Length!=points.Length)throw new ArgumentException("Portal mesh exceeds its vertex/triangle bounds.");p.ExitTriangles=(int[])triangles.Clone();p.ExitPoints=new double[points.Length*3];p.ExitUV=new double[uv.Length*2];for(int i=0;i<points.Length;i++){p.ExitPoints[i*3]=points[i].X;p.ExitPoints[i*3+1]=points[i].Y;p.ExitPoints[i*3+2]=points[i].Z;}for(int i=0;i<uv.Length;i++){p.ExitUV[i*2]=uv[i].X;p.ExitUV[i*2+1]=uv[i].Y;}p.ExitExtent=new double[3];p.ExitChart=0;PortalModes(p,a);data.Portal=p;data.Aspect="stretch";data.SourceProvider="native-portal";data.SourceId=data.Id;break;}
    case "screen-portal-shell":
    {var p=data.Portal??NewPortal();p.ShellPose=MatrixValues(a.Typed<MatrixD>());var r=a.Point();p.ShellRadii=new[]{r.X,r.Y,r.Z};a.End();data.Portal=p;break;}
    case "screen-portal-settings":
    {var p=data.Portal??NewPortal();p.NormalScale=a.Number(1);p.Saturation=a.Number(1);p.Brightness=a.Number(1);a.End();data.Portal=p;break;}
    case "screen-portal-transport":
    {var p=data.Portal??NewPortal();PortalModes(p,a);data.Portal=p;break;}
    case "screen-portal-exit-side":
    {var p=data.Portal??NewPortal();if(p.ExitKind!=1&&p.ExitKind!=3)throw new ArgumentException("Portal exit side applies to ellipsoids.");string side=a.Text();a.End();p.ExitKind=side=="inside"?1:side=="outside"?3:-1;data.Portal=p;break;}
    case "screen-portal-charts":
    {var p=data.Portal??NewPortal();p.EntryChart=a.Integer(0);p.ExitChart=a.Integer(0);a.End();data.Portal=p;break;}
    case "screen-portal-clear":a.End();data.Portal=null;if(data.SourceProvider=="native-portal"){data.SourceProvider=null;data.SourceId=null;}break;
    case "screen-surface":
    {data.SurfaceRadii=null;data.SurfaceMeshPoints=null;data.SurfaceMeshTriangles=null;data.SurfaceMeshUV=null;string kind=a.Text();data.SurfaceKind=kind=="plane"?0:kind=="cylinder"?1:kind=="sphere"?2:-1;data.SurfaceRadius=a.Number(2);data.SurfaceHorizontal=a.Number(Math.PI*2);data.SurfaceVertical=a.Number(Math.PI);string side=a.Has?a.Text():"inside";data.SurfaceSide=side=="inside"?0:side=="outside"?1:-1;a.End();break;}
    case "screen-ellipsoid":
    {var radii=a.Point();data.SurfaceKind=3;data.SurfaceRadii=new[]{radii.X,radii.Y,radii.Z};data.SurfaceMeshPoints=null;data.SurfaceMeshTriangles=null;data.SurfaceMeshUV=null;data.SurfaceHorizontal=a.Number(Math.PI*2);data.SurfaceVertical=a.Number(Math.PI);string side=a.Text("inside");data.SurfaceSide=side=="inside"?0:side=="outside"?1:-1;a.End();break;}
    case "screen-mesh":
    {var points=a.Typed<Vector3D[]>();var triangles=a.Typed<int[]>();var uv=a.Typed<Vector2[]>();string side=a.Text("outside");a.End();SurfaceMapping.ValidateAuthoredMesh(points,triangles,uv);data.SurfaceKind=4;data.SurfaceMapping=0;data.PanoramaGroup=null;data.SurfaceRadii=null;data.SurfaceSide=side=="inside"?0:side=="outside"?1:-1;data.SurfaceMeshPoints=new double[points.Length*3];data.SurfaceMeshUV=new float[uv.Length*2];for(int i=0;i<points.Length;i++){data.SurfaceMeshPoints[i*3]=points[i].X;data.SurfaceMeshPoints[i*3+1]=points[i].Y;data.SurfaceMeshPoints[i*3+2]=points[i].Z;data.SurfaceMeshUV[i*2]=uv[i].X;data.SurfaceMeshUV[i*2+1]=uv[i].Y;}data.SurfaceMeshTriangles=(int[])triangles.Clone();break;}
    case "screen-mapping":
    {string mode=a.Text();data.SurfaceMapping=mode=="angular"||mode=="equirectangular"?0:mode=="geodesic"?1:mode=="pinhole"?2:-1;a.End();break;}
    case "screen-quality":data.SurfaceError=a.Number(.02);a.End();break;
    case "screen-join":data.PanoramaGroup=a.Has?a.Text():null;if(data.PanoramaGroup=="")data.PanoramaGroup=null;a.End();break;
    case "screen-clip":
    {var planes=a.Has?a.Typed<Vector4[]>():new Vector4[0];a.End();data.SurfaceClip=new double[planes.Length*4];for(int i=0;i<planes.Length;i++){data.SurfaceClip[i*4]=planes[i].X;data.SurfaceClip[i*4+1]=planes[i].Y;data.SurfaceClip[i*4+2]=planes[i].Z;data.SurfaceClip[i*4+3]=planes[i].W;}break;}
    case "screen-resolution":data.UiRasterWidth=a.Integer(256);data.UiRasterHeight=a.Integer(144);a.End();break;
    case "screen-renderer":
    {string mode=a.Text();data.ContentRenderer=mode=="vector"?0:mode=="raster"?1:-1;data.UiRasterWidth=a.Integer(256);data.UiRasterHeight=a.Integer(144);data.UiRasterSamples=a.Integer(4);a.End();break;}
    case "screen-source":data.SourceProvider=a.Text();data.SourceId=a.Text();a.End();break;
    case "screen-camera-quality":{string profile=a.Text();a.End();data.SourceCaptureProfile=profile=="normal"?0:profile=="lite"?1:-1;break;}
    case "screen-camera-quality-settings":a.End();return data.SourceCaptureProfile==1?"lite":"normal";
    case "screen-panorama":data.SourceFov=a.Number(105);data.SourceFeather=a.Number(8);data.SourceSaturation=a.Number(1.15);data.SourceCaptureResolution=a.Integer(1024);a.End();break;
    case "screen-panorama-settings":a.End();return new MyTuple<double,double,double,int>(data.SourceFov,data.SourceFeather,data.SourceSaturation,data.SourceCaptureResolution);
    case "screen-source-clear":a.End();data.SourceProvider=null;data.SourceId=null;data.Portal=null;break;
    case "screen-settings":a.End();return new MyTuple<string,double,double,double>(data.Id,data.Width,data.Height,data.RefreshHz);
    case "screen-remove":a.End();UiNotifyProjectedScreenRemoved(c.Caller.EntityId,target.EntityId,data.Id);ClearScreenContent(GetScene(target.EntityId),selected);GetScene(target.EntityId).Screens.Remove(Key(c.Caller.EntityId,data.Id));c.ScreenId=null;_dirty=true;return true;
    case "screen-refresh":data.RefreshHz=a.Number();a.End();break;
    case "screen-background":data.Background=ColorValues(new[]{a.Paint()});a.End();break;
    case "screen-opacity":data.Opacity=(float)a.Number();a.End();break;
    case "screen-two-sided":data.TwoSided=a.Flag(true);data.FrontOpacity=(float)a.Number(1);data.BackOpacity=(float)a.Number(1);a.End();break;
    case "screen-side-opacity":data.FrontOpacity=(float)a.Number();data.BackOpacity=(float)a.Number();a.End();break;
    case "screen-side-settings":a.End();return new MyTuple<bool,float,float>(data.TwoSided,data.FrontOpacity,data.BackOpacity);
    case "screen-visible":data.Visible=a.Flag();a.End();break;
    case "screen-aspect":data.Aspect=a.Text();a.End();break;
    case "screen-pose":data.Pose=MatrixValues(a.Typed<MatrixD>());a.End();break;
    case "screen-camera":{var eye=a.Point();var look=a.Point();var up=a.Point();double fov=a.Number(Math.PI/3),near=a.Number(.05),far=a.Number(100);a.End();data.Camera=new[]{eye.X,eye.Y,eye.Z,look.X,look.Y,look.Z,up.X,up.Y,up.Z,fov,near,far};break;}
    case "screen-raster":data.RasterWidth=a.Integer(64);data.RasterHeight=a.Integer(36);a.End();break;
    case "screen-orbit":data.OrbitSpeed=a.Number();data.OrbitStartTick=_ticks;a.End();break;
    case "screen-scene":{var p=a.Typed<Vector3D[]>();var tris=a.Typed<int[]>();var colors=a.Typed<Vector4[]>();a.End();Geometry.ValidatePoints(p);data.SourcePoints=new double[p.Length*3];for(int i=0;i<p.Length;i++){data.SourcePoints[3*i]=p[i].X;data.SourcePoints[3*i+1]=p[i].Y;data.SourcePoints[3*i+2]=p[i].Z;}data.SourceTriangles=(int[])tris.Clone();data.SourceColors=ColorValues(colors);data.SourceProvider=null;data.SourceId=null;break;}
    case "screen-scene-clear":a.End();data.SourcePoints=null;data.SourceTriangles=null;data.SourceColors=null;break;
    case "sprites":{var sprites=a.Typed<MySprite[]>();data.SpriteWidth=a.Number(512);data.SpriteHeight=a.Number(512);a.End();ProjectedSprites.Validate(sprites,data.SpriteWidth,data.SpriteHeight);data.Sprites=new List<HoloProjectedSpriteData>();foreach(var s in sprites)data.Sprites.Add(new HoloProjectedSpriteData{Type=(int)s.Type,Data=s.Data,Position=s.Position.HasValue?new[]{s.Position.Value.X,s.Position.Value.Y}:null,Size=s.Size.HasValue?new[]{s.Size.Value.X,s.Size.Value.Y}:null,Color=s.Color.HasValue?ColorValues(new[]{s.Color.Value.ToVector4()}):null,Font=s.FontId,Alignment=(int)s.Alignment,Rotation=s.RotationOrScale});break;}
    case "screen-sprites-clear":a.End();data.Sprites=null;break;
    default:throw new ArgumentException("Unknown projected screen command.");
   }
   CommitScreen(c,data);return true;
  }
  static void PortalModes(HoloPortalData p,DrawArgs a)
  {string mode=a.Text("differential"),accuracy=a.Text("strict");a.End();p.Transport=mode=="differential"?0:mode=="stealth"?1:-1;p.Accuracy=accuracy=="strict"?0:accuracy=="approximate"?1:-1;}
  void CommitScreen(DrawContext c,HoloProjectedScreenData data)
  {
   var target=Authorize(c.Caller,c.Target);var scene=GetScene(target.EntityId);string key=Key(c.Caller.EntityId,data.Id);var prepared=ReadScreen(data,false);ProjectedScreen old;bool had=scene.Screens.TryGetValue(key,out old);
   foreach(var item in scene.Items.Values)if(item.CallerId==data.CallerId&&item.Id.StartsWith(ScreenPrefix(data.Id),StringComparison.Ordinal))ValidateProjectedHologramEffects(item.Effects,data);
   if(had&&ReferenceEquals(old.Data.SourcePoints,data.SourcePoints)){prepared.Source=old.Source;prepared.SourceColors=old.SourceColors;}
   if(!had&&scene.Screens.Count>=MaxScreensPerAnchor)throw new ArgumentException("Anchor screen limit reached.");scene.Screens[key]=prepared;
   try{ValidateCameraSourceSettings(scene);}
   catch{if(had)scene.Screens[key]=old;else scene.Screens.Remove(key);throw;}
   try{int screens=0;foreach(var other in _scenes.Values)screens+=other.Screens.Count;if(screens>MaxProjectedScreens)throw new ArgumentException("Global screen limit reached.");int points,primitives;ProjectedSourceCounts(scene,out points,out primitives);foreach(var item in scene.Items.Values){points+=item.Geometry.Points.Length;primitives+=item.Geometry.Triangles.Length/3+item.Geometry.Edges.Length/2;}if(points>scene.PointBudget||primitives>scene.PrimitiveBudget||scene.Items.Count+scene.Screens.Count+ProjectedSourceSlotCount(scene)>MaxObjects)throw new ArgumentException("Shared anchor geometry/object budget exceeded.");ValidateSceneSources(scene,new Item[0],new HashSet<string>());UiStampProjectedSurfaceMutation(scene,had?old.Data:null,prepared.Data);}
   catch{if(had)scene.Screens[key]=old;else scene.Screens.Remove(key);throw;}_dirty=true;
  }
  void ClearScreenContent(Scene scene,ProjectedScreen s)
  {
   string prefix=ScreenPrefix(s.Data.Id),layerPrefix="s_"+s.Data.Id+"__";long caller=s.Data.CallerId;var keys=new List<string>();foreach(var pair in scene.Items)if(pair.Value.CallerId==caller&&pair.Value.Id.StartsWith(prefix,StringComparison.Ordinal))keys.Add(pair.Key);foreach(var key in keys)scene.Items.Remove(key);keys.Clear();foreach(var pair in scene.Labels)if(pair.Value.CallerId==caller&&pair.Value.Id.StartsWith(prefix,StringComparison.Ordinal))keys.Add(pair.Key);foreach(var key in keys)scene.Labels.Remove(key);keys.Clear();foreach(var pair in scene.Layers)if(pair.Value.CallerId==caller&&pair.Value.Name.StartsWith(layerPrefix,StringComparison.Ordinal))keys.Add(pair.Key);foreach(var key in keys)scene.Layers.Remove(key);
   keys.Clear();foreach(var pair in _drawAnimations)if(pair.Value.Data.CallerId==caller&&pair.Value.Data.ConsoleId==scene.ConsoleId&&pair.Value.Id.StartsWith(prefix,StringComparison.Ordinal))keys.Add(pair.Value.Id);foreach(string id in keys)RemoveDrawAnimation(caller,scene.ConsoleId,id);
   keys.Clear();foreach(var pair in _packed)if(pair.Value.CallerId==caller&&pair.Value.ConsoleId==scene.ConsoleId&&pair.Value.Id.StartsWith(prefix,StringComparison.Ordinal))keys.Add(pair.Value.Id);foreach(string id in keys)RemovePacked(caller,scene.ConsoleId,id);
   var d=CloneScreen(s.Data);d.Sprites=null;d.SourcePoints=null;d.SourceTriangles=null;d.SourceColors=null;d.SourceProvider=null;d.SourceId=null;d.SourceSlots=null;scene.Screens[Key(caller,d.Id)]=ReadScreen(d);_dirty=true;
  }
 }
}
