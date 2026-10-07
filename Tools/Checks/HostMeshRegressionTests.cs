using System.Collections;
using HoloMap;
using Sandbox.ModAPI;
using VRageMath;
internal static class HostMeshRegressionTests
{
 public static int Run()
 {
  int checks=0;
  void Check(bool ok,string why){if(!ok)throw new Exception("Host mesh: "+why);checks++;}
  object Call(object o,string name,params object[] args)=>ClientReplicationTests.Call(o,name,args);
  object Field(object o,string name)=>ClientReplicationTests.Field(o,name);
  using var gateway=new ClientReplicationTests.GatewayScope{Server=true,Dedicated=false};
  var caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>m.Name switch {"get_EntityId"=>10L,"get_OwnerId"=>77L,"get_Closed"=>false,"IsSameConstructAs"=>true,_=>throw new Exception(m.Name)});
  var target=DrawTestProxy.Make(typeof(IMyTextPanel),(m,a)=>m.Name switch {"get_EntityId"=>20L,"get_Closed"=>false,"HasPlayerAccess"=>true,_=>throw new Exception(m.Name)});
  gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"?((long)a[0]==10?caller:target):null);
  var session=new HoloMapSession();var draw=(Func<string,object[],object>)Call(session,"DrawEndpoint",caller);draw("target",new[]{target});
  draw("text",new object[]{"text","HDR API",0,0,0});
  var pts=new[]{new Vector3D(-1,-1,0),new Vector3D(1,-1,0),new Vector3D(1,1,0),new Vector3D(-1,1,0)};
  draw("polygons",new object[]{"polygon",pts,new[]{new[]{0,1,2,3}},"cyan","blue"});
  draw("contours",new object[]{"contour",new[]{pts},"cyan","blue"});
  var scene=((IDictionary)Field(session,"_scenes"))[20L];var items=(IDictionary)Field(scene,"Items");int tick=0;
  foreach(string id in new[]{"text","polygon","contour"})
  {
   var item=items["10:"+id];Check(((Vector4[])Field(item,"TriangleColors")).Length==0,"host placeholder has empty colors: "+id);
   Verify(item,id);
  }
  var text=items["10:text"];draw("clip",new object[]{"text",new[]{new Vector4(1,0,0,0)}});Verify(text,"clipped text");
  var quad=Geometry.Polygons(pts,new[]{new[]{0,1,2,3}});
  try{MeshEffects.Solid(quad,Vector4.One,Vector4.One,new Vector4[0]);throw new Exception("Invalid attribute buffer accepted");}catch(ArgumentException){checks++;}
  return checks;
  void Verify(object item,string id)
  {
   ClientReplicationTests.SetField(session,"_ticks",tick++);
   Call(session,"ClientDisplayItem",scene,item,1);Call(session,"CompileQueuedDisplay");var compiled=Call(session,"ClientDisplayItem",scene,item,1);
   Check(compiled!=null,"host declaration compiles: "+id);var g=(Geometry)Field(compiled,"Geometry");
   Check(g.Triangles.Length>0,"generated topology exists: "+id);
   var mesh=new SurfaceMesh{Geometry=g,Colors=(Vector4[])Field(compiled,"TriangleColors"),EdgeColors=(Vector4[])Field(compiled,"EdgeColors"),UV=(Vector2[])Field(compiled,"UV")};
   MeshEffects.ValidateAttributes(mesh);Check(mesh.Colors.Length==g.Triangles.Length/3,"colors rebuilt for generated triangles: "+id);
   foreach(int index in g.Triangles)_=g.Points[index];foreach(var color in mesh.Colors)Check(color.W>0,"visible fill: "+id);
  }
 }
}
