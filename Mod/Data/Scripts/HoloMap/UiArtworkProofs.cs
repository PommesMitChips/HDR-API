using System;
using Sandbox.ModAPI;
using VRageMath;
namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        sealed class UiArtworkProof
        {
            public string Svg,Text,Anchor,Error;public int Segments;public double Height;public object Points,Faces,Contours;public bool EvenOdd;public Geometry Enclosure;public int Compiles;
            public bool Matches(Item item)
            {return Svg==item.SvgSource&&Text==item.TextSource&&Anchor==item.TextAnchor&&Segments==item.SvgSegments&&Height==item.TextHeight&&Faces==item.Faces&&Contours==item.Contours&&EvenOdd==item.EvenOdd&&(item.SvgSource!=null||item.TextSource!=null||Points==item.Geometry.Points);}
        }
        UiArtworkProof UiSourceProof(UiValueOwner owner,Item item)
        {
            UiArtworkProof proof;if(owner.Proofs.TryGetValue(item.Id,out proof)&&proof.Matches(item)){if(proof.Error!=null)throw new ArgumentException(proof.Error);return proof;}
            int count=proof==null?0:proof.Compiles;proof=new UiArtworkProof{Svg=item.SvgSource,Text=item.TextSource,Anchor=item.TextAnchor,Segments=item.SvgSegments,Height=item.TextHeight,Points=item.Geometry.Points,Faces=item.Faces,Contours=item.Contours,EvenOdd=item.EvenOdd,Compiles=count+1};owner.Proofs[item.Id]=proof;
            try
            {
                Geometry geometry;using(GeometryWork.Begin(200000))
                {
                    if(item.SvgSource!=null){ValidateSvgSource(item.SvgSource,item.SvgSegments);geometry=Svg.Parse(item.SvgSource,item.SvgSegments).Geometry;}
                    else if(item.TextSource!=null){ValidateText(item.TextSource,item.TextHeight,item.TextAnchor);geometry=MeshEffects.Text(item.TextSource,item.TextHeight,item.TextAnchor);}
                    else geometry=item.Geometry;
                }
                if(geometry.Points.Length==0)throw new ArgumentException("Interactive artwork has no bounded geometry.");var min=geometry.Points[0];var max=min;foreach(var p in geometry.Points){if(!Geometry.Finite(p.X)||!Geometry.Finite(p.Y)||!Geometry.Finite(p.Z))throw new ArgumentException("Interactive artwork bounds must be finite.");min=Vector3D.Min(min,p);max=Vector3D.Max(max,p);}
                var corners=new Vector3D[8];for(int i=0;i<8;i++)corners[i]=new Vector3D((i&1)==0?min.X:max.X,(i&2)==0?min.Y:max.Y,(i&4)==0?min.Z:max.Z);proof.Enclosure=new Geometry(corners,new int[0],new int[0]);return proof;
            }
            catch(Exception e){proof.Error="Interactive artwork requires a bounded source enclosure: "+e.Message;throw new ArgumentException(proof.Error);}
        }
        void UiValidateControlPose(IMyTerminalBlock target,Item item,UiArtworkProof proof,MatrixD pose,Scene scene)
        {
            UiValueRules.Matrix(MatrixValues(pose));
            if(IsProjectedId(item.Id))
            {
                HoloProjectedScreenData screen=null;foreach(var candidate in scene.Screens.Values)if(candidate.Data.CallerId==item.CallerId&&item.Id.StartsWith(ScreenPrefix(candidate.Data.Id),StringComparison.Ordinal)){screen=candidate.Data;break;}
                if(screen==null)throw new ArgumentException("Projected control screen is missing.");var mapping=pose*ProjectedContentView(screen);double determinant=mapping.M11*mapping.M22-mapping.M12*mapping.M21;
                if(!Geometry.Finite(determinant)||Math.Abs(determinant)<1e-15)throw new ArgumentException("Projected control pose must preserve the canvas plane.");
                foreach(var corner in proof.Enclosure.Points){var p=Vector3D.Transform(corner,mapping);if(!FiniteDisplayPoint(p)||Math.Abs(p.Z)>1e-6||Math.Abs(p.X)>screen.CanvasWidth*.5+1e-7||Math.Abs(p.Y)>screen.CanvasHeight*.5+1e-7)throw new ArgumentException("Projected controls require planar artwork fitting the current content canvas.");}return;
            }
            FitsDisplay(proof.Enclosure,pose,scene);
            if(target is Sandbox.ModAPI.Ingame.IMyTextPanel)
            {
                var mapping=pose*LocalView(scene);foreach(var corner in proof.Enclosure.Points){var p=Vector3D.Transform(corner,mapping);if(!Geometry.Finite(p.Z)||Math.Abs(p.Z)>1e-6)throw new ArgumentException("LCD controls require planar artwork and in-plane motion.");}
                double determinant=mapping.M11*mapping.M22-mapping.M12*mapping.M21;if(!Geometry.Finite(determinant)||Math.Abs(determinant)<1e-15)throw new ArgumentException("LCD control pose must preserve a nondegenerate canvas plane.");
            }
        }
        static void UiValidateLcdConstraint(IMyTerminalBlock target,UiControlData control)
        {
            if(!(target is Sandbox.ModAPI.Ingame.IMyTextPanel)&&!IsProjectedId(control.Artwork)||control.ConstraintKind==null)return;
            if(control.ConstraintKind=="line"||control.ConstraintKind=="path"){foreach(var p in UiValueRules.Points(control.Points))if(Math.Abs(p.Z)>1e-9)throw new ArgumentException("LCD paths must stay in the artwork XY plane.");}
            else{var pivot=UiValueRules.Point(control.Pivot);var axis=UiValueRules.Point(control.Axis);if(Math.Abs(pivot.Z)>1e-9||Math.Abs(axis.X)>1e-9||Math.Abs(axis.Y)>1e-9||Math.Abs(axis.Z)<1e-9)throw new ArgumentException("LCD rotations require an XY pivot and the local Z axis.");}
        }
        void UiValidateDisplayViewChange(IMyTerminalBlock target,Scene candidate)
        {
            foreach(var display in _uiDisplays.Values)if(display.TargetId==target.EntityId){UiValueOwner owner;if(!_uiValueOwners.TryGetValue(UiKey(display.CallerId,display.TargetId),out owner))continue;foreach(var widget in display.Widgets)if(widget.Control!=null&&widget.ScreenId==null){Scene scene;Item item;if(_scenes.TryGetValue(target.EntityId,out scene)&&scene.Items.TryGetValue(Key(display.CallerId,widget.Control.Artwork),out item))UiValidateControlPose(target,item,UiSourceProof(owner,item),item.Transform,candidate);}}
        }
        void UiValidateArtworkPoseMutation(long caller,long target,string id,MatrixD pose)
        {
            var d=GetUiDisplay(caller,target);UiValueOwner owner;if(d==null||!_uiValueOwners.TryGetValue(UiKey(caller,target),out owner)||!d.Widgets.Exists(w=>w.Control!=null&&w.Control.Artwork==id))return;
            Scene scene;Item item;if(_scenes.TryGetValue(target,out scene)&&scene.Items.TryGetValue(Key(caller,id),out item))UiValidateControlPose(MyAPIGateway.Entities.GetEntityById(target) as IMyTerminalBlock,item,UiSourceProof(owner,item),pose,scene);
        }
    }
}
