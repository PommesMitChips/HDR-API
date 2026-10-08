using System;
using System.Collections.Generic;
using ProtoBuf;
using VRageMath;
using VRage.Game.GUI.TextPanel;
namespace HoloMap
{
 [ProtoContract] public sealed class HoloProjectedScreenData
 {
  [ProtoMember(1)] public long CallerId;
  [ProtoMember(2)] public string Id;
  [ProtoMember(3)] public double[] Pose;
  [ProtoMember(4)] public double Width=2;
  [ProtoMember(5)] public double Height=1.125;
  [ProtoMember(6)] public double CanvasWidth=2;
  [ProtoMember(7)] public double CanvasHeight=1.125;
  [ProtoMember(8)] public double RefreshHz=6;
  [ProtoMember(9,OverwriteList=true)] public float[] Background=new float[]{0,0,0,0};
  [ProtoMember(10)] public float Opacity=1;
  [ProtoMember(11)] public bool Visible=true;
  [ProtoMember(12)] public string Aspect="contain";
  [ProtoMember(13,OverwriteList=true)] public double[] View=new double[]{0,0,0,0,0,0,1};
  [ProtoMember(14)] public List<HoloProjectedSpriteData> Sprites;
  [ProtoMember(15)] public double SpriteWidth=512;
  [ProtoMember(16)] public double SpriteHeight=512;
  [ProtoMember(17)] public double[] SourcePoints;
  [ProtoMember(18)] public int[] SourceTriangles;
  [ProtoMember(19)] public float[] SourceColors;
  [ProtoMember(20,OverwriteList=true)] public double[] Camera=new double[]{0,0,-4,0,0,0,0,1,0,1.0471975512,.05,100};
  [ProtoMember(21)] public int RasterWidth=64;
  [ProtoMember(22)] public int RasterHeight=36;
  [ProtoMember(23)] public double OrbitSpeed;
  [ProtoMember(24)] public long OrbitStartTick;
  // Protobuf tags 25–35 are reserved; camera capture is a separate provider mod.
  [ProtoMember(36)] public string SourceProvider;
  [ProtoMember(37)] public string SourceId;
  [ProtoMember(38)] public int SurfaceKind;
  [ProtoMember(39)] public int SurfaceMapping;
  [ProtoMember(40)] public double SurfaceRadius=2;
  [ProtoMember(41)] public double SurfaceHorizontal=Math.PI*2;
  [ProtoMember(42)] public double SurfaceVertical=Math.PI;
  [ProtoMember(43)] public int SurfaceSide;
  [ProtoMember(44)] public double SurfaceError=.02;
  [ProtoMember(45)] public int ContentRenderer;
  [ProtoMember(46)] public int UiRasterWidth=256;
  [ProtoMember(47)] public int UiRasterHeight=144;
  [ProtoMember(48)] public int UiRasterSamples=4;
  [ProtoMember(49)] public bool TwoSided;
  [ProtoMember(50,IsRequired=true)] public float FrontOpacity=1;
  [ProtoMember(51,IsRequired=true)] public float BackOpacity=1;
  [ProtoMember(52)] public string PanoramaGroup;
  // Optional convex crop, in display-anchor coordinates: dot(normal, point) <= W.
  [ProtoMember(53)] public double[] SurfaceClip;
  [ProtoMember(54,IsRequired=true)] public double SourceFov=105;
  [ProtoMember(55,IsRequired=true)] public double SourceFeather=8;
  [ProtoMember(56,IsRequired=true)] public double SourceSaturation=1.15;
  [ProtoMember(57,IsRequired=true)] public int SourceCaptureResolution=1024;
  [ProtoMember(58)] public double[] SurfaceRadii;
  [ProtoMember(59)] public double[] SurfaceMeshPoints;
  [ProtoMember(60)] public int[] SurfaceMeshTriangles;
  [ProtoMember(61)] public float[] SurfaceMeshUV;
  [ProtoMember(62)] public int SourceCaptureProfile;
  [ProtoMember(63)] public HoloPortalData Portal;
  // Source slots are local canvas consumers. Tags 25–35 remain reserved.
  [ProtoMember(64)] public List<HoloProjectedSourceSlotData> SourceSlots;
  [ProtoMember(65)] public long UiSurfaceGeneration;
  internal Vector3D[] SurfacePointCache;internal double[] SurfacePointCacheSource;
  internal Vector2[] SurfaceUvCache;internal float[] SurfaceUvCacheSource;
  internal string CompositionSlotId;
 }
 [ProtoContract] public sealed class HoloProjectedSpriteData
 {
  [ProtoMember(1)] public int Type;
  [ProtoMember(2)] public string Data;
  [ProtoMember(3)] public float[] Position;
  [ProtoMember(4)] public float[] Size;
  [ProtoMember(5)] public float[] Color;
  [ProtoMember(6)] public string Font;
  [ProtoMember(7)] public int Alignment;
  [ProtoMember(8)] public float Rotation;
 }
 public sealed partial class HoloMapSession
 {
  sealed class ProjectedScreen{public HoloProjectedScreenData Data;public Geometry Source;public Vector4[] SourceColors;}
  const int MaxScreensPerAnchor=8,MaxProjectedScreens=16;
  static string ScreenPrefix(string id){return "!s!"+id+"!";}
  static bool IsProjectedId(string id){return id!=null&&id.StartsWith("!s!",StringComparison.Ordinal);}
  static string ScreenId(string id)
  {
   if(string.IsNullOrEmpty(id)||id.Length>12)throw new ArgumentException("Screen ID requires 1–12 lowercase letters, digits or hyphens.");
   foreach(char c in id)if(!(c>='a'&&c<='z'||c>='0'&&c<='9'||c=='-'))throw new ArgumentException("Invalid screen ID.");return id;
  }
  static string ScreenLayer(string id,string layer){layer=LayerRules.Name(layer);if(layer.Length>12)throw new ArgumentException("Screen layer names support at most 12 characters.");return "s_"+ScreenId(id)+"__"+layer;}
  static Vector4 ScreenColor(float[] a){if(a==null||a.Length!=4)throw new ArgumentException("Invalid screen RGBA.");var c=new Vector4(a[0],a[1],a[2],a[3]);Color(c);return c;}
  static void ScreenDimension(double value){if(!Geometry.Finite(value)||value<.05||value>12)throw new ArgumentException("Screen dimensions must be .05–12 meters.");}
  static void ValidateScreenData(HoloProjectedScreenData d)
  {

   if(d==null||d.CallerId==0||d.UiSurfaceGeneration<0)throw new ArgumentException("Invalid screen owner or UI surface generation.");ScreenId(d.Id);ValidateDisplaySource(d.SourceProvider,d.SourceId);var pose=ReadMatrix(d.Pose);ValidateTransform(pose);
   if(d.SourceCaptureProfile<0||d.SourceCaptureProfile>1)throw new ArgumentException("Camera quality profile is normal or lite.");
   if(!Geometry.Finite(d.SourceFov)||d.SourceFov<60||d.SourceFov>120||!Geometry.Finite(d.SourceFeather)||d.SourceFeather<0||d.SourceFeather>25||!Geometry.Finite(d.SourceSaturation)||d.SourceSaturation<0||d.SourceSaturation>2||d.SourceCaptureResolution!=256&&d.SourceCaptureResolution!=512&&d.SourceCaptureResolution!=1024&&d.SourceCaptureResolution!=2048)throw new ArgumentException("Panorama settings require FOV 60–120, feather 0–25, saturation 0–2 and capture size 256, 512, 1024 or 2048.");
   if(Math.Abs(pose.Right.LengthSquared()-1)>1e-8||Math.Abs(pose.Up.LengthSquared()-1)>1e-8||Math.Abs(pose.Backward.LengthSquared()-1)>1e-8||Math.Abs(Vector3D.Dot(pose.Right,pose.Up))>1e-8||Math.Abs(Vector3D.Dot(pose.Right,pose.Backward))>1e-8||Math.Abs(Vector3D.Dot(pose.Up,pose.Backward))>1e-8||pose.Determinant()<.999999)throw new ArgumentException("Screen pose must be a rigid, right-handed transform.");
   ScreenDimension(d.Width);ScreenDimension(d.Height);if(d.Width*d.Height>100||pose.Translation.Length()+Math.Sqrt(d.Width*d.Width+d.Height*d.Height)/2>25)throw new ArgumentException("Screen exceeds the anchor's 25 meter display range.");
   ScreenDimension(d.CanvasWidth);ScreenDimension(d.CanvasHeight);if(!Geometry.Finite(d.RefreshHz)||d.RefreshHz<1)throw new ArgumentException("Projected refresh must be finite and at least 1 Hz.");ScreenColor(d.Background);LayerRules.Opacity(d.Opacity);LayerRules.Opacity(d.FrontOpacity);LayerRules.Opacity(d.BackOpacity);
   if(d.Aspect!="stretch"&&d.Aspect!="contain"&&d.Aspect!="cover")throw new ArgumentException("Screen aspect policy is stretch, contain or cover.");
   if(d.View==null||d.View.Length!=7)throw new ArgumentException("Invalid screen view.");foreach(double n in d.View)if(!Geometry.Finite(n))throw new ArgumentException("Nonfinite screen view.");if(d.View[6]<1e-6||d.View[6]>1000||new Vector3D(d.View[0],d.View[1],d.View[2]).LengthSquared()>100)throw new ArgumentException("Invalid screen content view.");
   if(d.Camera==null||d.Camera.Length!=12)throw new ArgumentException("Invalid screen camera.");foreach(double n in d.Camera)if(!Geometry.Finite(n)||Math.Abs(n)>1000000)throw new ArgumentException("Nonfinite/unbounded screen camera.");
   var eye=new Vector3D(d.Camera[0],d.Camera[1],d.Camera[2]);var forward=new Vector3D(d.Camera[3],d.Camera[4],d.Camera[5])-eye;var up=new Vector3D(d.Camera[6],d.Camera[7],d.Camera[8]);if(eye.LengthSquared()>1e12||forward.LengthSquared()<1e-12||Vector3D.Cross(forward,up).LengthSquared()<1e-12||d.Camera[9]<.1||d.Camera[9]>2.8||d.Camera[10]<.01||d.Camera[11]<=d.Camera[10]||d.Camera[11]>1000000)throw new ArgumentException("Invalid perspective camera bounds/basis.");
   ValidateScreenSurface(d,pose);
   ValidatePortalSource(d);ValidateSourceSlots(d);
   if(d.SurfaceClip!=null){if(d.SurfaceClip.Length%4!=0||d.SurfaceClip.Length>32)throw new ArgumentException("Screen crop supports up to eight planes.");for(int i=0;i<d.SurfaceClip.Length;i+=4){for(int j=0;j<4;j++)if(!Geometry.Finite(d.SurfaceClip[i+j]))throw new ArgumentException("Nonfinite screen crop plane.");double length=new Vector3D(d.SurfaceClip[i],d.SurfaceClip[i+1],d.SurfaceClip[i+2]).Length();if(!Geometry.Finite(length)||length<1e-8||Math.Abs(d.SurfaceClip[i+3])/length>50)throw new ArgumentException("Invalid screen crop plane.");}}
   if(d.PanoramaGroup!=null){ScreenId(d.PanoramaGroup);if((d.SurfaceKind!=2&&d.SurfaceKind!=3)||d.SurfaceMapping!=2)throw new ArgumentException("Joined camera screens require a pinhole sphere.");}
   if(d.RasterWidth<8||d.RasterWidth>128||d.RasterHeight<8||d.RasterHeight>72||!Geometry.Finite(d.OrbitSpeed)||Math.Abs(d.OrbitSpeed)>2||d.OrbitStartTick<0||d.OrbitStartTick>int.MaxValue)throw new ArgumentException("Invalid screen capture policy.");
   if(!Geometry.Finite(d.SpriteWidth)||!Geometry.Finite(d.SpriteHeight)||d.SpriteWidth<1||d.SpriteHeight<1||d.SpriteWidth>4096||d.SpriteHeight>4096)throw new ArgumentException("Invalid sprite canvas.");
   if(d.Sprites!=null){if(d.Sprites.Count>128)throw new ArgumentException("Screen sprite count exceeds 128.");foreach(var s in d.Sprites)ValidateScreenSprite(s);ProjectedSprites.Validate(ScreenSprites(d.Sprites),d.SpriteWidth,d.SpriteHeight);}
   if(d.SourcePoints==null){if(d.SourceTriangles!=null||d.SourceColors!=null)throw new ArgumentException("Incomplete screen source mesh.");}
   else{if(d.SourcePoints.Length==0||d.SourcePoints.Length%3!=0||d.SourcePoints.Length>2048*3||d.SourceTriangles==null||d.SourceTriangles.Length%3!=0||d.SourceTriangles.Length>4096*3)throw new ArgumentException("Invalid perspective source mesh.");var points=new Vector3D[d.SourcePoints.Length/3];for(int i=0;i<points.Length;i++)points[i]=new Vector3D(d.SourcePoints[3*i],d.SourcePoints[3*i+1],d.SourcePoints[3*i+2]);Geometry.ValidatePoints(points);foreach(int i in d.SourceTriangles)if(i<0||i>=points.Length)throw new ArgumentException("Invalid perspective triangle index.");var colors=ReadColors(d.SourceColors,d.SourceTriangles.Length/3);if(colors==null)throw new ArgumentException("Perspective triangles require colors.");foreach(var c in colors)if(c.W!=1)throw new ArgumentException("Perspective source supports opaque triangles only.");}
  }
  static void ValidateScreenSprite(HoloProjectedSpriteData s)
  {
   if(s==null||s.Type<0||s.Type>3||s.Alignment<0||s.Alignment>2||!Geometry.Finite(s.Rotation)||Math.Abs(s.Rotation)>10000||(s.Data!=null&&s.Data.Length>512)||(s.Font!=null&&s.Font.Length>32))throw new ArgumentException("Invalid sprite declaration.");
   foreach(var v in new[]{s.Position,s.Size})if(v!=null){if(v.Length!=2)throw new ArgumentException("Invalid sprite vector.");foreach(float n in v)if(!Geometry.Finite(n)||Math.Abs(n)>1000000)throw new ArgumentException("Invalid sprite coordinate.");}if(s.Color!=null)ScreenColor(s.Color);
  }
  static ProjectedScreen ReadScreen(HoloProjectedScreenData d,bool deep=true)
  {
   ValidateScreenData(d);if(deep)d=CloneScreen(d,true);var s=new ProjectedScreen{Data=d};if(d.SourcePoints!=null){var p=new Vector3D[d.SourcePoints.Length/3];for(int i=0;i<p.Length;i++)p[i]=new Vector3D(d.SourcePoints[3*i],d.SourcePoints[3*i+1],d.SourcePoints[3*i+2]);s.Source=new Geometry(p,new int[0],(int[])d.SourceTriangles.Clone());s.SourceColors=ReadColors(d.SourceColors,d.SourceTriangles.Length/3);}return s;
  }
  List<HoloProjectedScreenData> ExportProjectedScreens(Scene scene){var result=new List<HoloProjectedScreenData>();foreach(var s in scene.Screens.Values)result.Add(s.Data);return result;}
  static void ImportProjectedScreens(Scene scene,List<HoloProjectedScreenData> screens)
  {if(screens!=null){if(screens.Count>MaxScreensPerAnchor)throw new ArgumentException("Screen count exceeded.");foreach(var d in screens){var s=ReadScreen(d);string key=Key(d.CallerId,d.Id);if(scene.Screens.ContainsKey(key))throw new ArgumentException("Duplicate screen identity.");scene.Screens.Add(key,s);}}ValidateCameraSourceSettings(scene);ValidateProjectedReferences(scene);}
  static bool SameCameraSourceSettings(HoloProjectedScreenData a,HoloProjectedScreenData b)
  {return a.SourceFov==b.SourceFov&&a.SourceFeather==b.SourceFeather&&a.SourceSaturation==b.SourceSaturation&&a.SourceCaptureResolution==b.SourceCaptureResolution&&a.SourceCaptureProfile==b.SourceCaptureProfile;}
  static void ValidateCameraSourceSettings(Scene scene)
  {
   var sources=new Dictionary<string,HoloProjectedScreenData>();
   foreach(var screen in scene.Screens.Values)
   {
    foreach(var d in ScreenSourceSettings(screen.Data)){if(d.SourceProvider!="camera-panorama")continue;
    string key=Key(d.CallerId,d.SourceId);HoloProjectedScreenData previous;
    if(sources.TryGetValue(key,out previous)&&!SameCameraSourceSettings(previous,d))throw new ArgumentException("Screens sharing a camera source for this PB require the same panorama settings.");
    sources[key]=d;}
   }
  }
  static void ValidateProjectedReferences(Scene scene)
  {
   foreach(var item in scene.Items.Values){ValidateProjectedReference(scene,item.CallerId,item.Id);ValidateProjectedLayerReference(scene,item.CallerId,item.Id,item.Layer);}
   foreach(var label in scene.Labels.Values){ValidateProjectedReference(scene,label.CallerId,label.Id);ValidateProjectedLayerReference(scene,label.CallerId,label.Id,label.Layer);}
   foreach(var layer in scene.Layers.Values)if(layer.Name.StartsWith("s_",StringComparison.Ordinal)&&layer.Name.IndexOf("__",StringComparison.Ordinal)>2){int end=layer.Name.IndexOf("__",StringComparison.Ordinal);string id=ScreenId(layer.Name.Substring(2,end-2));if(!scene.Screens.ContainsKey(Key(layer.CallerId,id)))throw new ArgumentException("Orphan projected layer.");}
  }
  static void ValidateProjectedLayerReference(Scene scene,long caller,string id,string layer)
  {if(!IsProjectedId(id)||string.IsNullOrEmpty(layer))return;int end=id.IndexOf('!',3);string sid=id.Substring(3,end-3);if(!layer.StartsWith("s_"+sid+"__",StringComparison.Ordinal))throw new ArgumentException("Projected object layer belongs to another namespace.");}
  static void ValidateProjectedReference(Scene scene,long caller,string id)
  {if(!IsProjectedId(id))return;int end=id.IndexOf('!',3);if(end<4||end>=id.Length-1)throw new ArgumentException("Malformed projected content ID.");string sid=ScreenId(id.Substring(3,end-3));if(!scene.Screens.ContainsKey(Key(caller,sid)))throw new ArgumentException("Orphan projected content.");}
  static int ProjectedSourceBytes(Scene scene){int result=0;foreach(var s in scene.Screens.Values){var d=s.Data;result+=SourceSlotBytes(d)+1024+(d.SourceProvider==null?0:d.SourceProvider.Length*3)+(d.SourceId==null?0:d.SourceId.Length*3);if(d.Portal!=null){result+=512;if(d.Portal.ExitPoints!=null)result+=d.Portal.ExitPoints.Length*8+d.Portal.ExitTriangles.Length*4+d.Portal.ExitUV.Length*8;}if(d.SurfaceMeshPoints!=null)result+=d.SurfaceMeshPoints.Length*8+d.SurfaceMeshTriangles.Length*4+d.SurfaceMeshUV.Length*4;if(d.SourcePoints!=null)result+=d.SourcePoints.Length*8+d.SourceTriangles.Length*4+d.SourceColors.Length*4;if(d.Sprites!=null)foreach(var sprite in d.Sprites)result+=128+(sprite.Data==null?0:sprite.Data.Length*3);}return result;}
  static void ProjectedSourceCounts(Scene scene,out int points,out int primitives){points=0;primitives=scene.Screens.Count*2;foreach(var s in scene.Screens.Values){if(s.Data.SurfaceMeshPoints!=null){points+=s.Data.SurfaceMeshPoints.Length/3;primitives+=s.Data.SurfaceMeshTriangles.Length/3;}if(s.Source!=null){points+=s.Source.Points.Length;primitives+=s.Source.Triangles.Length/3;}}}
  static HoloProjectedScreenData CloneScreen(HoloProjectedScreenData d,bool deep=false)
  {
   var c=new HoloProjectedScreenData{CallerId=d.CallerId,Id=d.Id,Pose=d.Pose,Width=d.Width,Height=d.Height,CanvasWidth=d.CanvasWidth,CanvasHeight=d.CanvasHeight,RefreshHz=d.RefreshHz,Background=d.Background,Opacity=d.Opacity,Visible=d.Visible,Aspect=d.Aspect,View=d.View,Sprites=d.Sprites,SpriteWidth=d.SpriteWidth,SpriteHeight=d.SpriteHeight,SourcePoints=d.SourcePoints,SourceTriangles=d.SourceTriangles,SourceColors=d.SourceColors,Camera=d.Camera,RasterWidth=d.RasterWidth,RasterHeight=d.RasterHeight,OrbitSpeed=d.OrbitSpeed,OrbitStartTick=d.OrbitStartTick,SourceProvider=d.SourceProvider,SourceId=d.SourceId,SurfaceKind=d.SurfaceKind,SurfaceMapping=d.SurfaceMapping,SurfaceRadius=d.SurfaceRadius,SurfaceHorizontal=d.SurfaceHorizontal,SurfaceVertical=d.SurfaceVertical,SurfaceSide=d.SurfaceSide,SurfaceError=d.SurfaceError,ContentRenderer=d.ContentRenderer,UiRasterWidth=d.UiRasterWidth,UiRasterHeight=d.UiRasterHeight,UiRasterSamples=d.UiRasterSamples,TwoSided=d.TwoSided,FrontOpacity=d.FrontOpacity,BackOpacity=d.BackOpacity,PanoramaGroup=d.PanoramaGroup,SurfaceClip=d.SurfaceClip};
   c.SurfaceRadii=d.SurfaceRadii;c.SurfaceMeshPoints=d.SurfaceMeshPoints;c.SurfaceMeshTriangles=d.SurfaceMeshTriangles;c.SurfaceMeshUV=d.SurfaceMeshUV;c.SurfacePointCache=d.SurfacePointCache;c.SurfacePointCacheSource=d.SurfacePointCacheSource;c.SurfaceUvCache=d.SurfaceUvCache;c.SurfaceUvCacheSource=d.SurfaceUvCacheSource;
   c.SourceFov=d.SourceFov;c.SourceFeather=d.SourceFeather;c.SourceSaturation=d.SourceSaturation;c.SourceCaptureResolution=d.SourceCaptureResolution;c.SourceCaptureProfile=d.SourceCaptureProfile;
   c.Portal=ClonePortal(d.Portal,deep);c.SourceSlots=CloneSourceSlots(d.SourceSlots);c.UiSurfaceGeneration=d.UiSurfaceGeneration;
   if(deep){if(d.SurfaceRadii!=null)c.SurfaceRadii=(double[])d.SurfaceRadii.Clone();if(d.SurfaceMeshPoints!=null){c.SurfaceMeshPoints=(double[])d.SurfaceMeshPoints.Clone();c.SurfaceMeshTriangles=(int[])d.SurfaceMeshTriangles.Clone();c.SurfaceMeshUV=(float[])d.SurfaceMeshUV.Clone();c.SurfacePointCache=null;c.SurfacePointCacheSource=null;c.SurfaceUvCache=null;c.SurfaceUvCacheSource=null;}if(d.SurfaceClip!=null)c.SurfaceClip=(double[])d.SurfaceClip.Clone();c.Pose=(double[])d.Pose.Clone();c.Background=(float[])d.Background.Clone();c.View=(double[])d.View.Clone();c.Camera=(double[])d.Camera.Clone();if(d.SourcePoints!=null){c.SourcePoints=(double[])d.SourcePoints.Clone();c.SourceTriangles=(int[])d.SourceTriangles.Clone();c.SourceColors=(float[])d.SourceColors.Clone();}if(d.Sprites!=null){c.Sprites=new List<HoloProjectedSpriteData>();foreach(var s in d.Sprites)c.Sprites.Add(new HoloProjectedSpriteData{Type=s.Type,Data=s.Data,Font=s.Font,Alignment=s.Alignment,Rotation=s.Rotation,Position=s.Position==null?null:(float[])s.Position.Clone(),Size=s.Size==null?null:(float[])s.Size.Clone(),Color=s.Color==null?null:(float[])s.Color.Clone()});}}return c;
  }
  static MySprite[] ScreenSprites(List<HoloProjectedSpriteData> data)
  {var sprites=new MySprite[data.Count];for(int i=0;i<sprites.Length;i++){var s=data[i];sprites[i]=new MySprite{Type=(SpriteType)s.Type,Data=s.Data,Position=s.Position==null?(Vector2?)null:new Vector2(s.Position[0],s.Position[1]),Size=s.Size==null?(Vector2?)null:new Vector2(s.Size[0],s.Size[1]),Color=s.Color==null?(Color?)null:new Color(ScreenColor(s.Color)),FontId=s.Font,Alignment=(TextAlignment)s.Alignment,RotationOrScale=s.Rotation};}return sprites;}
 }
}
