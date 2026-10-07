using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.ModAPI.Ingame;
using VRageMath;

internal static class PortalDemoTests
{
 public static int Run(CSharpCompilation compilation)
 {
  using var image=new MemoryStream();if(!compilation.Emit(image).Success)throw new Exception("Portal PB compilation failed.");image.Position=0;
  var type=AssemblyLoadContext.Default.LoadFromStream(image).GetType("Program",true);int checks=0;
  void Check(bool b,string why){if(!b)throw new Exception("Portal PB: "+why);checks++;}
  foreach(int count in new[]{0,1,2})
  {
   var p=RuntimeHelpers.GetUninitializedObject(type);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
   var commands=new List<(string Op,object[] Args)>();var echoes=new List<string>();bool exists=false;UpdateFrequency frequency=UpdateFrequency.None;
   void Set(string name,object value)=>type.GetField(name,flags).SetValue(p,value);
   void Base(string name,object value)=>typeof(MyGridProgram).GetField("<"+name+">k__BackingField",flags).SetValue(p,value);
   var anchors=new List<IMyProjector>();for(int i=0;i<count;i++){int id=i;anchors.Add((IMyProjector)DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>m.Name switch{"get_EntityId"=>20L+id,"get_Closed"=>false,"IsSameConstructAs"=>true,"get_CustomName"=>"HDR Portal","get_BlockDefinition"=>new VRage.ObjectBuilders.SerializableDefinitionId(typeof(Sandbox.Common.ObjectBuilders.MyObjectBuilder_Projector),"LargeProjector"),_=>throw new Exception(m.Name)}));}
   Base("GridTerminalSystem",DrawTestProxy.Make(typeof(IMyGridTerminalSystem),(m,a)=>{if(m.Name!="GetBlocksOfType"||m.GetGenericArguments()[0]!=typeof(IMyProjector))throw new Exception("Unexpected portal terminal query.");var list=(List<IMyProjector>)a[0];var pred=(Func<IMyProjector,bool>)a[1];foreach(var anchor in anchors)if(pred(anchor))list.Add(anchor);return null;}));
   Base("Runtime",DrawTestProxy.Make(typeof(IMyGridProgramRuntimeInfo),(m,a)=>{if(m.Name=="set_UpdateFrequency"){frequency=(UpdateFrequency)a[0];return null;}throw new Exception(m.Name);}));
   Base("Echo",new Action<string>(s=>echoes.Add(s)));
   Base("Me",DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>m.Name=="get_EntityId"?10L:throw new Exception(m.Name)));
   Set("_shape","plane");Set("_transport","differential");Set("_accuracy","strict");Set("_size",4096);Set("_rate",60);Set("_native",true);
   Set("_hdr",new Func<string,object[],object>((op,args)=>{commands.Add((op,args));if(op=="screen-exists")return exists;if(op=="screen"&&args.Length>1)exists=true;if(op=="screen-remove")exists=false;return true;}));
   void Main(string arg,UpdateType source=UpdateType.Terminal)=>type.GetMethod("Main").Invoke(p,new object[]{arg,source});
   Main("");
   if(count!=1){Check(commands.Count==0&&frequency==UpdateFrequency.None&&echoes.Last().Contains("exactly one"),"ambiguous/missing anchor refuses all declaration writes");continue;}
   Check(commands.Any(c=>c.Op=="screen-portal-plane")&&frequency==UpdateFrequency.Update100,"default run installs strict paired plane and slow liveness polling");
   Check((string)commands.Last(c=>c.Op=="screen-portal-plane").Args[4]=="strict","demo defaults to strict capture rather than silently approximating");
   Check((int)commands.Last(c=>c.Op=="screen-resolution").Args[0]==4096&&(int)commands.Last(c=>c.Op=="screen-resolution").Args[1]==2730&&(int)commands.Last(c=>c.Op=="screen-refresh").Args[0]==60,"native demo requests viewport-density ceiling with stable 3:2 layout and 60 Hz refresh");
   Check(echoes.Last().Contains("4m above")&&echoes.Last().Contains("30m ahead")&&echoes.Last().Contains("transparent gaps"),"output says where to look and explains strict sparse output");
   int before=commands.Count;Main("",UpdateType.Update100);Check(commands.Skip(before).All(c=>c.Op=="target"||c.Op=="screen-exists"),"ordinary polling never republishes or captures");
   Main("size 256");Main("rate 12");Check((int)commands.Last(c=>c.Op=="screen-resolution").Args[0]==256&&(int)commands.Last(c=>c.Op=="screen-refresh").Args[0]==12,"size and rate commands configure subsequent portal declarations");
   Main("size native");Main("rate 120");Check((int)commands.Last(c=>c.Op=="screen-resolution").Args[1]==2730&&(int)commands.Last(c=>c.Op=="screen-refresh").Args[0]==120,"native sizing restores stable aspect and higher requested rates");
   Main("ellipsoid");Check(commands.Any(c=>c.Op=="screen-ellipsoid")&&commands.Last(c=>c.Op.StartsWith("screen-portal-")&&c.Op!="screen-portal-settings"&&c.Op!="screen-portal-shell").Op=="screen-portal-ellipsoid","ellipsoid selects real entry and exit geometry");
   Main("mesh");Check(commands.Any(c=>c.Op=="screen-mesh")&&commands.Any(c=>c.Op=="screen-portal-mesh"),"mesh demo uses authored entry and paired exit");
   Main("approximate");Check(commands.Last(c=>c.Op=="screen-portal-mesh").Args.Last().Equals("approximate"),"approximation requires explicit user command");
   Main("stealth");var stealthEntry=(MatrixD)commands.Last(c=>c.Op=="screen"&&c.Args.Length>1).Args[1];var stealthExit=(MatrixD)commands.Last(c=>c.Op=="screen-portal-ellipsoid").Args[0];
   Check(stealthEntry==stealthExit&&stealthEntry.Translation==new Vector3D(0,4,0),"stealth pairs coincident entry and exit at the stated bubble centre");
   Check(commands.Last(c=>c.Op=="screen-ellipsoid").Args.Last().Equals("outside")&&commands.Last(c=>c.Op=="screen-portal-exit-side").Args[0].Equals("outside"),"stealth uses matching outward ellipsoid charts");
   Check(commands.Last(c=>c.Op=="screen-two-sided").Args[0] is false&&(int)commands.Last(c=>c.Op=="screen-opacity").Args[0]==1,"stealth exterior is opaque and front-only");
   Check((Vector3D)commands.Last(c=>c.Op=="screen-portal-shell").Args[1]==new Vector3D(2.05,1.55,2.05)&&echoes.Last().Contains("Look from outside"),"stealth shell encloses the bubble with explicit viewing instructions");
   Main("differential");Check(((MatrixD)commands.Last(c=>c.Op=="screen-portal-mesh").Args[0]).Translation==new Vector3D(0,4,-30),"differential restores the selected paired shape and distant exit");
   Main("clear");Check(!exists&&frequency==UpdateFrequency.None&&commands.Where(c=>c.Op=="screen-exists").All(c=>c.Args[0].Equals("portal")),"clear removes only demo-owned portal and stops polling");
  }
  return checks;
 }
}
