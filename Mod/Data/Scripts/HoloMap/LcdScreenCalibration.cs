using System;
using Sandbox.ModAPI;
using VRageMath;
namespace HoloMap
{
 // Physical front screen in block-local coordinates. No render-depth offset.
 public struct LcdScreenBasis
 {
  public Vector3D Center,HalfRight,HalfUp,Normal;
 }
 public static class LcdScreenCalibration
 {
  sealed class Entry
  {
   public readonly string Model,Surface;
   public readonly int Rotation;
   public readonly LcdScreenBasis Basis;
   public Entry(string model,string surface,int rotation,Vector3D center,Vector3D right,Vector3D up)
   {Model=model;Surface=surface;Rotation=rotation;Basis=new LcdScreenBasis{Center=center,HalfRight=right,HalfUp=up,Normal=Vector3D.Normalize(Vector3D.Cross(right,up))};}
  }
  // Generated from actual packed MWM vertices and UV corners by Tools/ScreenCalibration.
  // Transparent panels deliberately describe only their +local-Z front screen.
  // The small transparent panel's unrotated mesh differs from its rotated meshes.
  static readonly Entry[] Entries=new Entry[]
  {
   new Entry("models/cubes/small/lcdpanelwide.mwm", "ScreenArea", 0, new Vector3D(0,0,-0.11083984375), new Vector3D(1.4912109375,0,0), new Vector3D(0,0.74072265625,0)),
   new Entry("models/cubes/small/lcdpanelwide.mwm", "ScreenArea90", 1, new Vector3D(0,0,-0.11083984375), new Vector3D(0,-0.74072265625,0), new Vector3D(1.4912109375,0,0)),
   new Entry("models/cubes/small/lcdpanelwide.mwm", "ScreenArea180", 2, new Vector3D(0,0,-0.11083984375), new Vector3D(-1.4912109375,0,0), new Vector3D(0,-0.74072265625,0)),
   new Entry("models/cubes/small/lcdpanelwide.mwm", "ScreenArea270", 3, new Vector3D(0,0,-0.11083984375), new Vector3D(0,0.74072265625,0), new Vector3D(-1.4912109375,0,0)),
   new Entry("models/cubes/small/lcdpanel.mwm", "ScreenArea", 0, new Vector3D(0,0,-0.1107177734375), new Vector3D(0.74169921875,0,0), new Vector3D(0,0.74169921875,0)),
   new Entry("models/cubes/small/lcdpanel.mwm", "ScreenArea90", 1, new Vector3D(0,0,-0.1107177734375), new Vector3D(0,-0.74169921875,0), new Vector3D(0.74169921875,0,0)),
   new Entry("models/cubes/small/lcdpanel.mwm", "ScreenArea180", 2, new Vector3D(0,0,-0.1107177734375), new Vector3D(-0.74169921875,0,0), new Vector3D(0,-0.74169921875,0)),
   new Entry("models/cubes/small/lcdpanel.mwm", "ScreenArea270", 3, new Vector3D(0,0,-0.1107177734375), new Vector3D(0,0.74169921875,0), new Vector3D(-0.74169921875,0,0)),
   new Entry("models/cubes/large/lcdpanel.mwm", "ScreenArea", 0, new Vector3D(0,0,-1.0234375), new Vector3D(1.234375,0,0), new Vector3D(0,1.234375,0)),
   new Entry("models/cubes/large/lcdpanel.mwm", "ScreenArea90", 1, new Vector3D(0,0,-1.0234375), new Vector3D(0,-1.234375,0), new Vector3D(1.234375,0,0)),
   new Entry("models/cubes/large/lcdpanel.mwm", "ScreenArea180", 2, new Vector3D(0,0,-1.0234375), new Vector3D(-1.234375,0,0), new Vector3D(0,-1.234375,0)),
   new Entry("models/cubes/large/lcdpanel.mwm", "ScreenArea270", 3, new Vector3D(0,0,-1.0234375), new Vector3D(0,1.234375,0), new Vector3D(-1.234375,0,0)),
   new Entry("models/cubes/large/lcdpanelwide.mwm", "ScreenArea", 0, new Vector3D(0,0,-1.0234375), new Vector3D(2.484375,0,0), new Vector3D(0,1.234375,0)),
   new Entry("models/cubes/large/lcdpanelwide.mwm", "ScreenArea90", 1, new Vector3D(0,0,-1.0234375), new Vector3D(0,-1.234375,0), new Vector3D(2.484375,0,0)),
   new Entry("models/cubes/large/lcdpanelwide.mwm", "ScreenArea180", 2, new Vector3D(0,0,-1.0234375), new Vector3D(-2.484375,0,0), new Vector3D(0,-1.234375,0)),
   new Entry("models/cubes/large/lcdpanelwide.mwm", "ScreenArea270", 3, new Vector3D(0,0,-1.0234375), new Vector3D(0,1.234375,0), new Vector3D(-2.484375,0,0)),
   new Entry("models/cubes/large/transparentlcd.mwm", "TransparentScreenArea", 0, new Vector3D(0,0,-1.1923828125), new Vector3D(1.083984375,0,0), new Vector3D(0,1.083984375,0)),
   new Entry("models/cubes/large/transparentlcd.mwm", "TransparentScreenArea90", 1, new Vector3D(0,0,-1.1923828125), new Vector3D(0,-1.083984375,0), new Vector3D(1.083984375,0,0)),
   new Entry("models/cubes/large/transparentlcd.mwm", "TransparentScreenArea180", 2, new Vector3D(0,0,-1.1923828125), new Vector3D(-1.083984375,0,0), new Vector3D(0,-1.083984375,0)),
   new Entry("models/cubes/large/transparentlcd.mwm", "TransparentScreenArea270", 3, new Vector3D(0,0,-1.1923828125), new Vector3D(0,1.083984375,0), new Vector3D(-1.083984375,0,0)),
   new Entry("models/cubes/small/transparentlcd.mwm", "TransparentScreenArea", 0, new Vector3D(0,-0.0015869140625,-0.2314453125), new Vector3D(0.2176513671875,0,0), new Vector3D(0,0.2176513671875,0)),
   new Entry("models/cubes/small/transparentlcd.mwm", "TransparentScreenArea90", 1, new Vector3D(0,0,-0.230224609375), new Vector3D(0,-0.216796875,0), new Vector3D(0.216796875,0,0)),
   new Entry("models/cubes/small/transparentlcd.mwm", "TransparentScreenArea180", 2, new Vector3D(0,0,-0.230224609375), new Vector3D(-0.216796875,0,0), new Vector3D(0,-0.216796875,0)),
   new Entry("models/cubes/small/transparentlcd.mwm", "TransparentScreenArea270", 3, new Vector3D(0,0,-0.230224609375), new Vector3D(0,0.216796875,0), new Vector3D(-0.216796875,0,0)),
  };
  public static bool TryGet(IMyTextPanel panel,out LcdScreenBasis basis)
  {
   basis=new LcdScreenBasis();
   if(panel==null||panel.Closed)return false;
   // Resolve through the whitelisted model interface, never concrete MyModel.
   var entity=(VRage.ModAPI.IMyEntity)panel;var model=entity.Model;
   if(model==null)return false;
   IMyLcdSurfaceComponent rotation;
   if(!panel.Components.TryGet<IMyLcdSurfaceComponent>(out rotation)||rotation==null)return false;
   var surface=(Sandbox.ModAPI.Ingame.IMyTextSurface)panel;
   return TryGet(model.AssetName,surface.Name,rotation.SelectedRotationIndex,out basis);
  }
  // Pure lookup used by tests. Unknown models, material names and rotation states
  // fail closed so the caller can retain native LCD rendering.
  public static bool TryGet(string modelAsset,string surfaceName,int rotationIndex,out LcdScreenBasis basis)
  {
   basis=new LcdScreenBasis();
   if(string.IsNullOrEmpty(modelAsset)||string.IsNullOrEmpty(surfaceName)||rotationIndex<0||rotationIndex>3)return false;
   foreach(var entry in Entries)
    if(entry.Rotation==rotationIndex&&string.Equals(entry.Surface,surfaceName,StringComparison.Ordinal)
       &&SameAsset(entry.Model,modelAsset)){basis=entry.Basis;return true;}
   return false;
  }
  // Allocation-free slash/case normalization; arbitrary rooted/suffix paths are
  // intentionally not accepted as trusted vanilla models.
  static bool SameAsset(string expected,string actual)
  {
   if(expected.Length!=actual.Length)return false;
   for(int i=0;i<expected.Length;i++)
   {char value=actual[i]=='\\'?'/':actual[i];if(char.ToLowerInvariant(value)!=expected[i])return false;}
   return true;
  }
 }
}

