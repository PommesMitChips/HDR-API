using System;
using Hdr.Mods;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRageMath;
namespace HdrExamples
{
    // Copy with the staged HdrModApi.cs. This sample is client-local, not a gameplay rotor controller.
    // The consuming mod must supply a world ray only after its input provider owns that pointer sample.
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class WorldRotorModExample:MySessionComponentBase
    {
        HdrModApi hdr;long context,generation;int retry;bool inputOwned;double angle=.5;
        public double NormalizedAngle {get{return angle;}set{angle=value;}}
        public override void BeforeStart(){if(!MyAPIGateway.Utilities.IsDedicated)hdr=new HdrModApi("example.hdr.world-rotor");}
        public override void UpdateAfterSimulation()
        {
            if(hdr==null)return;
            if(!hdr.Ready){inputOwned=false;if(++retry>=60){retry=0;hdr.Request();}return;}
            if(generation!=hdr.ConnectionGeneration){generation=hdr.ConnectionGeneration;context=0;inputOwned=false;}
            if(context==0)
            {
                if(MyAPIGateway.Session.Camera==null)return;var camera=MatrixD.Invert(MyAPIGateway.Session.Camera.ViewMatrix);
                context=hdr.CreateWorld(MatrixD.CreateWorld(camera.Translation+camera.Forward*3,camera.Forward,camera.Up));
                hdr.Mesh(context,"rotor",new[]{new Vector3D(-.8,-.08,0),new Vector3D(.8,-.08,0),new Vector3D(.8,.08,0),new Vector3D(-.8,.08,0)},new[]{0,1,2,0,2,3},new Vector4(0,.8f,1,.9f));
                hdr.Value(context,"angle",angle,0,1);
                hdr.Control(context,"turn","rotor",new Vector4(-.9f,-.12f,1.8f,.24f));
                hdr.BindControlValue(context,"turn","angle");
                hdr.ConstraintRotation(context,"turn",Vector3D.Zero,Vector3D.UnitZ,-Math.PI/2,Math.PI/2);
                hdr.Draggable(context,"turn",true);
                hdr.BindValue(context,"angle",()=>angle,value=>angle=value);
            }
            // Run adapters from simulation, outside Draw. Consumer-owned setters must own replication.
            hdr.UpdateBindings();
        }
        public void BeginCooperativeInput(){inputOwned=true;}
        public void SubmitOwnedWorldRay(Vector3D origin,Vector3D direction,bool leftHeld)
        {if(inputOwned&&hdr!=null&&hdr.Ready&&context!=0)hdr.PointerRay(context,origin,direction,leftHeld);}
        public void EndCooperativeInput()
        {inputOwned=false;if(hdr!=null&&hdr.Ready&&context!=0)hdr.CancelPointer(context);}
        protected override void UnloadData(){inputOwned=false;if(hdr!=null)hdr.Dispose();hdr=null;context=0;}
    }
}
