using System;
using System.Collections.Generic;
using ProtoBuf;
using VRage;
using VRageMath;
namespace HoloMap
{
 [ProtoContract] public sealed class HoloProjectedSourceSlotData
 {
  [ProtoMember(1)] public string Id;
  [ProtoMember(2)] public string Provider;
  [ProtoMember(3)] public string SourceId;
  // XY is the lower-left canvas position in metres; ZW is width/height.
  [ProtoMember(4)] public double[] Rect;
  [ProtoMember(5)] public double[] Clip;
  // Texture UV XY is top-left; ZW is normalized width/height.
  [ProtoMember(6)] public float[] UV;
  [ProtoMember(7)] public int Order;
  [ProtoMember(8,IsRequired=true)] public float Opacity=1;
  [ProtoMember(9,IsRequired=true)] public bool Visible=true;
  [ProtoMember(10)] public string Layer;
  [ProtoMember(11)] public double RefreshHz;
  [ProtoMember(12)] public long Revision=1;
  [ProtoMember(13)] public bool CameraSettingsOverride;
  [ProtoMember(14)] public double SourceFov=105;
  [ProtoMember(15)] public double SourceFeather=8;
  [ProtoMember(16)] public double SourceSaturation=1.15;
  [ProtoMember(17)] public int SourceCaptureResolution=1024;
  [ProtoMember(18)] public int SourceCaptureProfile;
  [ProtoMember(19)] public double[] Pose;
  [ProtoMember(20)] public int CameraSettingsMask;
 }
 public sealed partial class HoloMapSession
 {
  const int MaxSourceSlotsPerScreen=16;
  static int ProjectedSourceSlotCount(Scene scene){int count=0;foreach(var screen in scene.Screens.Values)if(screen.Data.SourceSlots!=null)count+=screen.Data.SourceSlots.Count;return count;}
  static int SourceSlotBytes(HoloProjectedScreenData d){int bytes=0;if(d.SourceSlots!=null)foreach(var s in d.SourceSlots)bytes+=512+(s.Id.Length+s.Provider.Length+s.SourceId.Length+(s.Layer==null?0:s.Layer.Length))*3;return bytes;}
  static string SourceSlotId(string id){ScreenId(id);return id;}
  static double[] SlotRect(Vector4 r){return new[]{(double)r.X,r.Y,r.Z,r.W};}
  static List<HoloProjectedSourceSlotData> CloneSourceSlots(List<HoloProjectedSourceSlotData> source)
  {
   if(source==null)return null;var result=new List<HoloProjectedSourceSlotData>(source.Count);
   foreach(var s in source){if(s==null){result.Add(null);continue;}result.Add(new HoloProjectedSourceSlotData{Id=s.Id,Provider=s.Provider,SourceId=s.SourceId,Rect=s.Rect==null?null:(double[])s.Rect.Clone(),Clip=s.Clip==null?null:(double[])s.Clip.Clone(),UV=s.UV==null?null:(float[])s.UV.Clone(),Pose=s.Pose==null?null:(double[])s.Pose.Clone(),Order=s.Order,Opacity=s.Opacity,Visible=s.Visible,Layer=s.Layer,RefreshHz=s.RefreshHz,Revision=s.Revision,CameraSettingsOverride=s.CameraSettingsOverride,CameraSettingsMask=s.CameraSettingsMask,SourceFov=s.SourceFov,SourceFeather=s.SourceFeather,SourceSaturation=s.SourceSaturation,SourceCaptureResolution=s.SourceCaptureResolution,SourceCaptureProfile=s.SourceCaptureProfile});}return result;
  }
  static void ValidateSlotRect(double[] rect)
  {if(rect==null||rect.Length!=4)throw new ArgumentException("Source slot rectangle requires XY position and positive width/height.");foreach(double n in rect)if(!Geometry.Finite(n)||Math.Abs(n)>24)throw new ArgumentException("Source slot rectangle must be finite and bounded to 24 metres.");if(rect[2]<=0||rect[3]<=0||rect[2]>12||rect[3]>12)throw new ArgumentException("Source slot dimensions must be positive and at most 12 metres.");}
  static void ValidateSourceSlots(HoloProjectedScreenData d)
  {
   if(d.SourceSlots==null)return;if(d.SourceSlots.Count>MaxSourceSlotsPerScreen)throw new ArgumentException("Screen source slot limit is 16.");var ids=new HashSet<string>();
   foreach(var s in d.SourceSlots)
   {if(s==null||!ids.Add(SourceSlotId(s.Id)))throw new ArgumentException("Duplicate or missing source slot identity.");ValidateDisplaySource(s.Provider,s.SourceId);if(string.IsNullOrEmpty(s.Provider))throw new ArgumentException("Source slots require an explicit provider.");ValidateSlotRect(s.Rect);ValidateSlotRect(s.Clip);if(s.UV==null||s.UV.Length!=4)throw new ArgumentException("Source slots require a normalized UV rectangle.");ValidateRasterUVRect(new Vector4(s.UV[0],s.UV[1],s.UV[2],s.UV[3]));LayerRules.Opacity(s.Opacity);if(s.Order < -10000||s.Order>10000||s.Revision<0||!Geometry.Finite(s.RefreshHz)||s.RefreshHz!=0&&(s.RefreshHz<1||s.RefreshHz>120))throw new ArgumentException("Invalid source slot order, revision or refresh.");if(s.Layer!=null){LayerRules.Name(s.Layer);if(s.Layer.Length>12)throw new ArgumentException("Source slot layer is at most 12 characters.");}if(s.CameraSettingsOverride){var settings=SourceSlotSettings(d,s);if(!Geometry.Finite(settings.SourceFov)||settings.SourceFov<60||settings.SourceFov>120||!Geometry.Finite(settings.SourceFeather)||settings.SourceFeather<0||settings.SourceFeather>25||!Geometry.Finite(settings.SourceSaturation)||settings.SourceSaturation<0||settings.SourceSaturation>2||settings.SourceCaptureProfile<0||settings.SourceCaptureProfile>1||settings.SourceCaptureResolution!=256&&settings.SourceCaptureResolution!=512&&settings.SourceCaptureResolution!=1024&&settings.SourceCaptureResolution!=2048)throw new ArgumentException("Invalid slot camera settings.");}}
   foreach(var s in d.SourceSlots){if(s.CameraSettingsMask<0||s.CameraSettingsMask>3||!s.CameraSettingsOverride&&s.CameraSettingsMask!=0)throw new ArgumentException("Invalid explicit source settings mask.");if(s.Pose!=null){var pose=ReadMatrix(s.Pose);ValidateTransform(pose);if(pose.Translation.Length()>25)throw new ArgumentException("Source slot pose exceeds canvas coordinate bounds.");}}
  }
  static HoloProjectedScreenData SourceSlotSettings(HoloProjectedScreenData parent,HoloProjectedSourceSlotData slot)
  {
   var d=CloneScreen(parent);d.CompositionSlotId=slot.Id;d.SourceSlots=null;d.SourceProvider=slot.Provider;d.SourceId=slot.SourceId;d.Sprites=null;d.SourcePoints=null;d.SourceTriangles=null;d.SourceColors=null;d.CanvasWidth=slot.Rect[2];d.CanvasHeight=slot.Rect[3];d.Background=new float[4];d.Visible=parent.Visible&&slot.Visible;d.Opacity=parent.Opacity*slot.Opacity;if(slot.RefreshHz>0)d.RefreshHz=slot.RefreshHz;
   if(slot.CameraSettingsOverride){int mask=slot.CameraSettingsMask==0?3:slot.CameraSettingsMask;if((mask&1)!=0){d.SourceFov=slot.SourceFov;d.SourceFeather=slot.SourceFeather;d.SourceSaturation=slot.SourceSaturation;d.SourceCaptureResolution=slot.SourceCaptureResolution;}if((mask&2)!=0)d.SourceCaptureProfile=slot.SourceCaptureProfile;}return d;
  }
  static IEnumerable<HoloProjectedScreenData> ScreenSourceSettings(HoloProjectedScreenData parent)
  {if(HasExternalSource(parent))yield return parent;if(parent.SourceSlots!=null)foreach(var slot in parent.SourceSlots)yield return SourceSlotSettings(parent,slot);}
  static bool ScreenHasProvider(HoloProjectedScreenData parent,string provider)
  {foreach(var d in ScreenSourceSettings(parent))if(d.SourceProvider==provider)return true;return false;}
  static HoloProjectedSourceSlotData FindSourceSlot(HoloProjectedScreenData d,string id)
  {SourceSlotId(id);if(d.SourceSlots!=null)foreach(var slot in d.SourceSlots)if(slot.Id==id)return slot;throw new ArgumentException("Source slot does not exist for this screen.");}
  object SourceSlotCommand(DrawContext context,string op,DrawArgs a,HoloProjectedScreenData d)
  {
   string id=SourceSlotId(a.Text());
   if(op=="screen-slot")
   {
    string provider=a.Text(),source=a.Text();var rect=a.Typed<Vector4>();var clip=a.Typed<Vector4>();var uv=a.Typed<Vector4>();int order=a.Integer(0);float opacity=(float)a.Number(1);a.End();if(d.SourceSlots==null)d.SourceSlots=new List<HoloProjectedSourceSlotData>();var existing=d.SourceSlots.Find(s=>s.Id==id);long revision=existing==null?1:checked(existing.Revision+1);if(existing!=null)d.SourceSlots.Remove(existing);d.SourceSlots.Add(new HoloProjectedSourceSlotData{Id=id,Provider=provider,SourceId=source,Rect=SlotRect(rect),Clip=SlotRect(clip),UV=new[]{uv.X,uv.Y,uv.Z,uv.W},Order=order,Opacity=opacity,Revision=revision});
   }
   else
   {
    var slot=FindSourceSlot(d,id);
    switch(op)
    {
     case "screen-slot-remove":a.End();d.SourceSlots.Remove(slot);break;
     case "screen-slot-visible":slot.Visible=a.Flag();a.End();break;
     case "screen-slot-opacity":slot.Opacity=(float)a.Number();a.End();break;
     case "screen-slot-refresh":slot.RefreshHz=a.Number(0);a.End();break;
     case "screen-slot-layer":slot.Layer=a.Text();if(slot.Layer=="")slot.Layer=null;a.End();break;
     case "screen-slot-order":slot.Order=a.Integer(0);a.End();break;
     case "screen-slot-pose":slot.Pose=MatrixValues(a.Typed<MatrixD>());a.End();break;
     case "screen-slot-panorama":slot.CameraSettingsOverride=true;slot.CameraSettingsMask|=1;slot.SourceFov=a.Number(105);slot.SourceFeather=a.Number(8);slot.SourceSaturation=a.Number(1.15);slot.SourceCaptureResolution=a.Integer(1024);a.End();break;
     case "screen-slot-camera-quality":slot.CameraSettingsOverride=true;slot.CameraSettingsMask|=2;string profile=a.Text();slot.SourceCaptureProfile=profile=="normal"?0:profile=="lite"?1:-1;a.End();break;
     case "screen-slot-inherit":slot.CameraSettingsOverride=false;slot.CameraSettingsMask=0;slot.RefreshHz=0;slot.Layer=null;a.End();break;
     case "screen-slot-settings":a.End();return new MyTuple<string,string,Vector4,Vector4,Vector4,int>(slot.Provider,slot.SourceId,new Vector4((float)slot.Rect[0],(float)slot.Rect[1],(float)slot.Rect[2],(float)slot.Rect[3]),new Vector4((float)slot.Clip[0],(float)slot.Clip[1],(float)slot.Clip[2],(float)slot.Clip[3]),new Vector4(slot.UV[0],slot.UV[1],slot.UV[2],slot.UV[3]),slot.Order);
     case "screen-slot-status":a.End();return SourceSlotStatus(context,d,slot);
     default:throw new ArgumentException("Unknown source slot command.");
    }
    slot.Revision=checked(slot.Revision+1);
   }
   CommitScreen(context,d);return true;
  }
 }
}
