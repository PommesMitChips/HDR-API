using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI.Ingame;
using VRageMath;
namespace HoloMap
{
 // Engine-managed selection marker. HoloMapSession draws bounded local frames;
 // a PB is never asked to send a sprite stream across the network.
 [MyTextSurfaceScript("HDRAPI", "HDR API")]
 public sealed class HoloMapSurfaceScript : MyTextSurfaceScriptBase
 {
  public HoloMapSurfaceScript(Sandbox.ModAPI.Ingame.IMyTextSurface surface,VRage.Game.ModAPI.Ingame.IMyCubeBlock block,Vector2 size):base(surface,block,size){}
  public override ScriptUpdate NeedsUpdate {get{return ScriptUpdate.Update10000;}}
  public override void Run(){}
 }
}
