using System;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  const long RasterBackendDiscovery=481770110,RasterBackendRegistration=481770111;
  Func<string,object[],object> _rasterBackend,_rasterService;
  long _rasterBackendGeneration,_rasterSerial;bool _rasterRegistered,_rasterDiscovering;
  long _rasterCapsGeneration=-1;
  int _rasterMaxWidth=1024,_rasterMaxHeight=1024,_rasterMaxPixels=262144;
  sealed class RasterImage
  {
   public string Material;public Vector2I Size;public object Lease;
   public Func<string,object[],object> Backend;public long Generation;
  }
  void RegisterRasterBackend()
  {
   if(_rasterRegistered)return;_rasterService=RasterService;MyAPIGateway.Utilities.RegisterMessageHandler(RasterBackendRegistration,ReceiveRasterBackend);_rasterRegistered=true;DiscoverRasterBackend();
  }
  void DiscoverRasterBackend()
  {
   if(!_rasterRegistered||_rasterDiscovering)return;_rasterDiscovering=true;
   try{MyAPIGateway.Utilities.SendModMessage(RasterBackendDiscovery,_rasterService);}catch{}finally{_rasterDiscovering=false;}
  }
  void ReceiveRasterBackend(object message)
  {
   if(!_rasterRegistered||!(message is MyTuple<int,Func<string,object[],object>>))return;
   var registration=(MyTuple<int,Func<string,object[],object>>)message;
   if(registration.Item1!=1||registration.Item2==null||ReferenceEquals(registration.Item2,_rasterBackend))return;
   _rasterBackend=registration.Item2;_rasterBackendGeneration++;PluginComponentRegistered("raster-ui");ClearRasterImages();DiscoverRasterBackend();
  }
  object RasterService(string command,object[] args)
  {
   if(!_rasterRegistered)return false;
   if(command=="unregister"&&args!=null&&args.Length==1&&ReferenceEquals(args[0],_rasterBackend))
   {_rasterBackend=null;_rasterBackendGeneration++;ClearRasterImages();return true;}
   return false;
  }
  static void ValidateRasterSize(Vector2I size)
  {
   if(size.X<16||size.Y<16||size.X>2048||size.Y>2048||(long)size.X*size.Y>RasterCanvas.MaxPixels)
    throw new ArgumentException("Raster size must be 16–2048 per side and at most 1048576 pixels.");
  }
  Vector2I RasterBackendSize(Vector2I requested)
  {
   if(_rasterCapsGeneration!=_rasterBackendGeneration)
   {
    _rasterMaxWidth=_rasterMaxHeight=1024;_rasterMaxPixels=262144;
    try
    {
     var value=_rasterBackend==null?null:_rasterBackend("caps",new object[0]);
     if(value is MyTuple<int,int,int,int>)
     {
      var caps=(MyTuple<int,int,int,int>)value;
      if(caps.Item1>=16&&caps.Item2>=16&&caps.Item3>=256&&caps.Item4>0)
      {_rasterMaxWidth=Math.Min(2048,caps.Item1);_rasterMaxHeight=Math.Min(2048,caps.Item2);_rasterMaxPixels=Math.Min(RasterCanvas.MaxPixels,caps.Item3);}
     }
    }
    catch{}
    _rasterCapsGeneration=_rasterBackendGeneration;
   }
   return DisplayLod.BoundResolution(requested,_rasterMaxWidth,_rasterMaxHeight,_rasterMaxPixels);
  }
  static string ValidateRasterMaterial(string name)
  {
   if(string.IsNullOrEmpty(name)||name.Length>128)throw new ArgumentException("Raster frame requires a bounded local material name.");
   foreach(char c in name)if(!(c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='_'||c=='-'||c=='.'))throw new ArgumentException("Invalid raster material name.");return name;
  }
  static void ValidateTextureSize(Vector2I size)
  {if(size.X<1||size.Y<1||size.X>4096||size.Y>4096||(long)size.X*size.Y>16777216)throw new ArgumentException("Local texture dimensions must be 1–4096.");}
  RasterImage UploadRaster(string key,Vector2I size,byte[] rgba)
  {
   ValidateRasterSize(size);if(rgba==null||rgba.Length!=(long)size.X*size.Y*4)throw new ArgumentException("Raster frame requires exactly width × height × 4 RGBA bytes.");
   var backend=_rasterBackend;long generation=_rasterBackendGeneration;if(backend==null){NotifyPluginUnavailable("raster-ui");return null;}
   object returned=backend("upload",new object[]{key,++_rasterSerial,size.X,size.Y,(byte[])rgba.Clone()});
   if(returned==null)return null;if(!(returned is MyTuple<string,object>))throw new ArgumentException("Unsupported raster backend upload result.");
   var result=(MyTuple<string,object>)returned;
   var image=new RasterImage{Size=size,Lease=result.Item2,Backend=backend,Generation=generation};
   try{image.Material=ValidateRasterMaterial(result.Item1);}catch{ReleaseRasterImage(image);throw;}
   if(!RasterImageCurrent(image)){ReleaseRasterImage(image);return null;}return image;
  }
  bool RasterImageCurrent(RasterImage image)
  {
   if(image==null||image.Lease==null||image.Generation!=_rasterBackendGeneration||!ReferenceEquals(image.Backend,_rasterBackend))return false;
   try{var valid=image.Backend("valid",new object[]{image.Lease});return valid is bool&&(bool)valid&&image.Generation==_rasterBackendGeneration&&ReferenceEquals(image.Backend,_rasterBackend);}catch{return false;}
  }
  static void ReleaseRasterImage(RasterImage image)
  {if(image==null||image.Backend==null||image.Lease==null)return;try{image.Backend("release",new object[]{image.Lease});}catch{}}
  static void RetireRasterImage(RasterImage prior,RasterImage replacement)
  {
   // A deferred upload may return the same published lease until its new serial
   // commits. Replacing its wrapper does not transfer or retire that ownership.
   if(prior!=null&&replacement!=null&&ReferenceEquals(prior.Lease,replacement.Lease)&&ReferenceEquals(prior.Backend,replacement.Backend)&&prior.Generation==replacement.Generation)return;
   ReleaseRasterImage(prior);
  }
  void ClearRasterImages()
  {
   var detached=new System.Collections.Generic.List<RasterImage>();
   foreach(var cache in _projectedCaches.Values)
   {if(cache.UiRaster!=null)detached.Add(cache.UiRaster);cache.UiRaster=null;cache.UiRasterMesh=null;if(cache.SourceRaster!=null)detached.Add(cache.SourceRaster);cache.SourceRaster=null;if(cache.SourceTexture)ClearExternalBudgetView(cache);cache.NextSample=-1;cache.Mapped.Remove("ui");}
   foreach(var image in detached)ReleaseRasterImage(image);
  }
  void UnregisterRasterBackend()
  {
   bool registered=_rasterRegistered;_rasterRegistered=false;_rasterBackend=null;_rasterService=null;_rasterBackendGeneration++;ClearRasterImages();
   if(registered&&MyAPIGateway.Utilities!=null)MyAPIGateway.Utilities.UnregisterMessageHandler(RasterBackendRegistration,ReceiveRasterBackend);
  }
  static SurfaceMesh RasterQuad(double width,double height)
  {
   return new SurfaceMesh{Geometry=new Geometry(new[]{new Vector3D(-width/2,-height/2,0),new Vector3D(width/2,-height/2,0),new Vector3D(width/2,height/2,0),new Vector3D(-width/2,height/2,0)},new int[0],new[]{0,1,2,0,2,3}),Colors=new[]{Vector4.One,Vector4.One},EdgeColors=new Vector4[0],UV=new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(1,0),new Vector2(0,0)}};
  }
  static void ValidateRasterUVRect(Vector4 rect)
  {if(!Geometry.Finite(rect.X)||!Geometry.Finite(rect.Y)||!Geometry.Finite(rect.Z)||!Geometry.Finite(rect.W)||rect.X<0||rect.Y<0||rect.Z<=0||rect.W<=0||(double)rect.X+rect.Z>1||(double)rect.Y+rect.W>1)throw new ArgumentException("Texture UV rectangle requires finite positive extents within the normalized texture bounds.");}
  SurfaceMesh BuildExternalRaster(Scene scene,HoloProjectedScreenData data,ProjectedCache cache,object returned,int points,int primitives,Func<string,object[],object> endpoint,long generation,ref bool transferred)
  {
   if(points<4||primitives<2){cache.SourceReduced=true;return null;}
   if(!(returned is MyTuple<int,object,object,bool,double>))throw new ArgumentException("Unsupported version 2 display frame.");
   var frame=(MyTuple<int,object,object,bool,double>)returned;
   if(!Geometry.Finite(frame.Item5)||frame.Item5<.1||data.SourceProvider!="camera-panorama"&&frame.Item5>(data.SourceProvider=="native-portal"?120:60))throw new ArgumentException("Invalid raster source refresh.");
   string material;Vector2I size;RasterImage uploaded=null;var uvRect=new Vector4(0,0,1,1);
   if(frame.Item1==1)
   {
    if(!(frame.Item2 is MyTuple<Vector2I,byte[],int>))throw new ArgumentException("RGBA frame requires size, bytes and format flags.");
    var bitmap=(MyTuple<Vector2I,byte[],int>)frame.Item2;if(bitmap.Item3!=0)throw new ArgumentException("Only straight-alpha sRGB RGBA8 is supported.");
    uploaded=UploadRaster(PackedKey(data.CallerId,scene.ConsoleId,data.Id)+":source",bitmap.Item1,bitmap.Item2);
    if(uploaded==null)return null;material=uploaded.Material;size=uploaded.Size;
   }
   else if(frame.Item1==2)
   {
    long textureGeneration;
    if(frame.Item2 is MyTuple<string,Vector2I,long>){var texture=(MyTuple<string,Vector2I,long>)frame.Item2;material=texture.Item1;size=texture.Item2;textureGeneration=texture.Item3;}
    else if(frame.Item2 is MyTuple<string,Vector2I,long,Vector4>){var texture=(MyTuple<string,Vector2I,long,Vector4>)frame.Item2;material=texture.Item1;size=texture.Item2;textureGeneration=texture.Item3;uvRect=texture.Item4;}
    else throw new ArgumentException("Texture frame requires material, size, generation and an optional normalized UV rectangle.");
    if(textureGeneration<=0)throw new ArgumentException("Texture frame requires a live generation.");
    material=ValidateRasterMaterial(material);ValidateTextureSize(size);ValidateRasterUVRect(uvRect);
   }
   else throw new ArgumentException("Unknown raster frame kind.");
   DisplayProvider provider;if(!_displayProviders.TryGetValue(data.SourceProvider,out provider)||provider.Generation!=generation||!ReferenceEquals(provider.Endpoint,endpoint)){ReleaseRasterImage(uploaded);return null;}
   var oldEndpoint=cache.SourceProtocol==2?cache.SourceEndpoint:null;var oldEvidence=cache.SourceEvidence;var oldRaster=cache.SourceRaster;cache.SourceRaster=null;
   transferred=true;cache.SourceProvider=data.SourceProvider;cache.SourceId=data.SourceId;cache.SourceEndpoint=provider.Endpoint;cache.SourceGeneration=provider.Generation;cache.SourceProtocol=2;cache.SourceEvidence=frame.Item3;cache.SourceReduced=frame.Item4;
   if(!ReferenceEquals(oldEvidence,frame.Item3))ReleaseProviderEvidence(oldEndpoint,oldEvidence);RetireRasterImage(oldRaster,uploaded);
   if(!ExternalEvidenceValid(scene,data,cache)){ReleaseRasterImage(uploaded);ClearExternalBudgetView(cache);return null;}
   cache.SourceRaster=uploaded;cache.SourceTexture=true;cache.SourceTextureMaterial=material;cache.SourceTextureSize=size;
   cache.NextCapture=_ticks/60d+1/Math.Min(frame.Item5,SourceRefreshLimit(data));
   if(cache.SourceQuad==null||cache.SourceQuadWidth!=data.CanvasWidth||cache.SourceQuadHeight!=data.CanvasHeight||cache.SourceQuadUV!=uvRect){var quad=RasterQuad(data.CanvasWidth,data.CanvasHeight);for(int i=0;i<quad.UV.Length;i++)quad.UV[i]=new Vector2(uvRect.X+quad.UV[i].X*uvRect.Z,uvRect.Y+quad.UV[i].Y*uvRect.W);cache.SourceQuad=quad;cache.SourceQuadWidth=data.CanvasWidth;cache.SourceQuadHeight=data.CanvasHeight;cache.SourceQuadUV=uvRect;}
   return cache.SourceQuad;
  }
 }
}
