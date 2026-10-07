using Hdr.Mods;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRageMath;

namespace HdrExamples
{
    // Copy with Api/Mods/HdrModApi.cs. Cosmetic effects are client-local and need no renderer plugin.
    // Rebuild retained objects after reconnect; configure once, rather than issuing effect calls in Draw.
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class HologramEffectsModExample : MySessionComponentBase
    {
        HdrModApi hdr;long world,hud,connection;int retry;
        public override void BeforeStart()
        {if(!MyAPIGateway.Utilities.IsDedicated)hdr=new HdrModApi("example.hdr.hologram-effects");}
        public override void UpdateAfterSimulation()
        {
            if(hdr==null)return;
            if(!hdr.Ready){if(++retry>=60){retry=0;hdr.Request();}return;}
            if(connection!=hdr.ConnectionGeneration){connection=hdr.ConnectionGeneration;world=hud=0;}
            if(world!=0||MyAPIGateway.Session.Camera==null)return;
            var camera=MatrixD.Invert(MyAPIGateway.Session.Camera.ViewMatrix);
            world=hdr.CreateWorld(MatrixD.CreateWorld(camera.Translation+camera.Forward*3,camera.Forward,camera.Up));
            hdr.Text(world,"title","HOLOGRAM",new Vector3D(-.8,0,0),.3,new Vector4(0,.8f,1,.8f));
            hdr.Effect(world,"title","flicker",.12,9);
            hdr.Effect(world,"title","scan",.2,.3,.06,.6);
            hdr.Effect(world,"title","volume",.08,4,.5);
            hdr.Effect(world,"title","particles",24,.012,.05,2,.08,.35,42);
            hdr.Effect(world,"title","rays",6,.02,.08,new Vector3D(0,-.7,-.8),.005);
            hdr.Effect(world,"title","budget",2048);
            hdr.Transition(world,"title",true,"dissolve",1.2);
            hud=hdr.CreateHud(10);
            hdr.Text(hud,"status","Holographic effects / client local",new Vector3D(32,48,0),22,new Vector4(0,.8f,1,1));
            hdr.Effect(hud,"status","flicker",.06,10);
            hdr.Effect(hud,"status","scan",.1,.25,.1,.35);
            hdr.Effect(hud,"status","particles",8,1.5,2,2,2,.25,7);
            hdr.Transition(hud,"status",true,"wipe",.7);
        }
        protected override void UnloadData(){if(hdr!=null)hdr.Dispose();hdr=null;world=hud=0;}
    }
}
