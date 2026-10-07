using Hdr.Mods;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRageMath;

namespace HdrExamples
{
    // Copy together with Api/Mods/HdrModApi.cs into your mod's Data/Scripts folder.
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class HudMenuModExample : MySessionComponentBase
    {
        HdrModApi hdr;long hud,connection;bool alternate;int retryTicks;
        public override void BeforeStart()
        {if(!MyAPIGateway.Utilities.IsDedicated)hdr=new HdrModApi("example.hdr.hud-menu");}
        public override void UpdateAfterSimulation()
        {
            if(hdr==null)return;
            if(!hdr.Ready){if(++retryTicks>=60){retryTicks=0;hdr.Request();}return;}
            if(connection!=hdr.ConnectionGeneration){connection=hdr.ConnectionGeneration;hud=0;alternate=false;}
            if(hud==0)
            {
                hud=hdr.CreateHud(10);
                hdr.Rect(hud,"panel",32,32,380,170,new Vector4(.02f,.04f,.06f,.92f));
                hdr.Text(hud,"title","HDR mod HUD",new Vector3D(52,55,0),28,new Vector4(0,.85f,1,1));
                hdr.Rect(hud,"button",52,110,180,50,new Vector4(.03f,.2f,.25f,.95f));
                hdr.Text(hud,"caption","Change colour",new Vector3D(64,122,0),22,Vector4.One);
                hdr.Bounds(hud,"change",new Vector4(52,110,180,50));
            }
            // This example participates only while the game's own cursor is visible.
            // A production menu should cooperate with its input provider; HDR never captures global input.
            var viewport=hdr.Viewport;var area=MyAPIGateway.Input.GetMouseAreaSize();
            if(MyAPIGateway.Gui.IsCursorVisible&&area.X>0&&area.Y>0)
            {
                var pointer=MyAPIGateway.Input.GetMousePosition();
                hdr.Pointer(hud,pointer.X*viewport.X/area.X,pointer.Y*viewport.Y/area.Y,MyAPIGateway.Input.IsLeftMousePressed());
            }
            else hdr.Pointer(hud,-1,-1,false);
            foreach(var e in hdr.PollEvents(hud))if(e.Item1=="click"&&e.Item2=="change")
            {alternate=!alternate;hdr.Rect(hud,"button",52,110,180,50,alternate?new Vector4(.3f,.12f,.02f,.95f):new Vector4(.03f,.2f,.25f,.95f));hdr.Text(hud,"caption","Change colour",new Vector3D(64,122,0),22,Vector4.One);}
        }
        protected override void UnloadData(){if(hdr!=null)hdr.Dispose();hdr=null;hud=0;}
    }
}
