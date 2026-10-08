using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;

namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  // Component registration is local to a viewing client. It does not assert
  // that a GPU capture has completed, or that remote viewers have a plugin.
  readonly HashSet<string> _pluginNotices=new HashSet<string>();
  static string PluginFeatureGroup(string feature)
  {return feature=="raster-ui"?"raster-ui":feature=="camera-panorama"||feature=="lcd-texture"?"camera-images":feature=="native-portal"?"native-portal":feature=="interactive-pointer"?"interactive-pointer":null;}
  static string PluginFeatureLabel(string feature)
  {return feature=="raster-ui"?"raster UI":feature=="lcd-texture"?"LCD camera textures":feature=="camera-panorama"?"camera images":feature=="interactive-pointer"?"interactive pointer":"native portals";}
  MyTuple<bool,bool,string> LocalPluginStatus(string feature)
  {
   if(PluginFeatureGroup(feature)==null)return new MyTuple<bool,bool,string>(false,false,"Unknown plugin capability: "+(feature??"<null>"));
   string label=PluginFeatureLabel(feature);bool pointer=feature=="interactive-pointer";
   if(MyAPIGateway.Utilities==null||MyAPIGateway.Utilities.IsDedicated)
    return new MyTuple<bool,bool,string>(true,false,"Viewer-dependent capability: "+label+" requires HDR Client Renderer"+(pointer?" 0.9.13 or later":"")+" on each viewing client.");
   DisplayProvider provider;
   bool registered=pointer?_uiPointer!=null&&_uiPointer.Ready:feature=="raster-ui"?_rasterBackend!=null:_displayProviders.TryGetValue(feature,out provider)&&provider.Endpoint!=null;
   return new MyTuple<bool,bool,string>(true,registered,registered?(pointer?"Component registered locally; confirmed input routing is required before interaction.":"Component registered locally; GPU/frame readiness is separate."):"Requires plugin: HDR Client Renderer ("+label+(pointer?"; 0.9.13 or later":"")+").");
  }
  bool NotifyPluginUnavailable(string feature)
  {
   var status=LocalPluginStatus(feature);if(!status.Item1||status.Item2)return false;
   if(MyAPIGateway.Utilities==null||MyAPIGateway.Utilities.IsDedicated)return true;
   if(_pluginNotices.Add(PluginFeatureGroup(feature)))
   {
    try{VRage.Utils.MyLog.Default.WriteLine("HDR API: "+status.Item3);}catch{}
    try{MyAPIGateway.Utilities.ShowMessage("HDR API",status.Item3);}catch{}
   }
   return true;
  }
  void PluginComponentRegistered(string feature)
  {string group=PluginFeatureGroup(feature);if(group!=null)_pluginNotices.Remove(group);}
  void ResetPluginNotices(){_pluginNotices.Clear();}
  bool UseProjectedVectorFallback(HoloProjectedScreenData data)
  {return data.ContentRenderer!=1||_rasterBackend!=null;}
 }
}
