using System;
using HDR.Interactions;
using VRage;
using VRageMath;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        static long ModClientDeclare(ModClientOwner owner,ModClientContext c)
        {return c.DeclarationRevision=++owner.DeclarationRevision;}
        static double ModClientAngle(ModClientControl control,ModClientValue value,double scalar)
        {return control.AngleMin+(scalar-value.Range.Min)/value.Range.Span*(control.AngleMax-control.AngleMin);}
        static double ModClientScalar(ModClientControl control,ModClientValue value,double angle)
        {return value.Range.Min+(angle-control.AngleMin)/(control.AngleMax-control.AngleMin)*value.Range.Span;}
        static void ModClientValueEvent(ModClientContext c,string kind,ModClientControl control,ModClientValue value)
        {
            string id=control==null?"":control.Id;
            var sample=new MyTuple<string,string,string,MyTuple<double,long,long>>(kind,id,value.Id,new MyTuple<double,long,long>(value.Current,value.Revision,0));
            bool final=kind=="end"||kind=="cancel";
            if(!final)for(int i=0;i<c.ValueEvents.Count;i++)if(c.ValueEvents[i].Item1==kind&&c.ValueEvents[i].Item2==id&&c.ValueEvents[i].Item3==value.Id){c.ValueEvents[i]=sample;return;}
            int capacity=final?64:63;
            if(c.ValueEvents.Count>=capacity)
            {
                int replace=-1;for(int i=0;i<c.ValueEvents.Count;i++)if(c.ValueEvents[i].Item1!="end"&&c.ValueEvents[i].Item1!="cancel"){replace=i;break;}
                if(replace<0){if(!final)return;replace=0;}
                c.ValueEvents.RemoveAt(replace);
            }
            c.ValueEvents.Add(sample);
        }
        static void ModClientCancel(ModClientContext c,string reason)
        {
            var capture=c.Capture;
            if(capture!=null)
            {
                if(ReferenceEquals(capture.Value.Lease,capture.Control))capture.Value.Lease=null;
                ModClientValueEvent(c,"cancel",capture.Control,capture.Value);
                ModClientRules.PointerEvent(c,"cancel",capture.Control.Id);c.Capture=null;
            }
            c.PointerBlockedUntilRelease|=c.Pressed;c.Pressed=false;
        }
        void CancelModClientInteractions(string reason)
        {foreach(var owner in _modClientOwners.Values)foreach(var c in owner.Contexts.Values)ModClientCancel(c,reason);}
        static void ModClientCancelParticipant(ModClientContext c,ModClientControl control,string reason)
        {if(c.Capture!=null&&(ReferenceEquals(c.Capture.Control,control)||control.Value!=null&&control.Value==c.Capture.Value.Id))ModClientCancel(c,reason);}
        static void ModClientCancelArtwork(ModClientContext c,string artwork,string reason)
        {foreach(var control in c.Controls.Values)if(control.Artwork==artwork)ModClientCancelParticipant(c,control,reason);}
        static void ModClientRemoveItemControls(ModClientContext c,string artwork)
        {
            for(int i=c.ControlOrder.Count-1;i>=0;i--)
            {
                var control=c.Controls[c.ControlOrder[i]];if(control.Artwork!=artwork)continue;
                ModClientCancelParticipant(c,control,"artwork-removed");
                c.Controls.Remove(control.Id);c.ControlOrder.RemoveAt(i);
            }
        }
        static void ModClientRebaseControls(ModClientOwner owner,ModClientContext c,ModClientItem item)
        {
            foreach(var control in c.Controls.Values)if(control.Artwork==item.Id)
            {ModClientCancelParticipant(c,control,"source-pose");control.ReferencePose=item.Transform;ModClientValue value;if(control.Value!=null&&c.Values.TryGetValue(control.Value,out value))control.ReferenceValue=value.Current;control.Revision=ModClientDeclare(owner,c);}
        }
        static bool ModClientDelta(ModClientControl control,ModClientValue value,double scalar,out MatrixD delta)
        {
            delta=MatrixD.Identity;
            if(control.Kind==1)return control.Path.TryGetTranslationDelta(control.ReferenceValue,scalar,out delta);
            if(control.Kind==2)return control.Rotation.TryGetRotationDelta(ModClientAngle(control,value,control.ReferenceValue),ModClientAngle(control,value,scalar),out delta);
            return true;
        }
        static bool ModClientCommit(ModClientOwner owner,ModClientContext c,ModClientValue value,double requested,ModClientControl source,bool programmatic,out double canonical)
        {
            canonical=value.Current;if(!value.Range.TryNormalize(requested,out canonical))throw new ArgumentException("Value must be finite within the numeric domain.");
            c.PendingPoses.Clear();
            try
            {
                foreach(var control in c.Controls.Values)
                {
                    if(control.Value!=value.Id||control.Kind==0)continue;
                    ModClientItem item;MatrixD delta;if(!c.Items.TryGetValue(control.Artwork,out item)||!ModClientDelta(control,value,canonical,out delta)){c.PendingPoses.Clear();return false;}
                    var pose=delta*control.ReferencePose;ModClientSurfacePlacement(owner,c,item,pose);
                    c.PendingPoses.Add(new ModClientPoseUpdate{Item=item,Pose=pose});
                }
                if(c.Surface!=null&&!ModClientSurfacePendingFits(owner,c)){c.PendingPoses.Clear();return false;}
            }
            catch(ArgumentException){c.PendingPoses.Clear();return false;}
            if(programmatic&&c.Capture!=null&&ReferenceEquals(c.Capture.Value,value))ModClientCancel(c,"source-value");
            foreach(var update in c.PendingPoses)update.Item.Transform=update.Pose;
            c.PendingPoses.Clear();
            if(canonical!=value.Current){value.Current=canonical;value.Revision=++owner.ValueRevision;ModClientValueEvent(c,programmatic?"set":"change",source,value);}
            return true;
        }
        static bool ModClientRay(Vector3D origin,Vector3D direction,MatrixD inverse,out LocalRay ray)
        {return LocalRay.TryFromDirection(Vector3D.Transform(origin,inverse),Vector3D.TransformNormal(direction,inverse),out ray);}
        static bool ModClientPlane(LocalRay ray,out Vector3D point)
        {
            point=Vector3D.Zero;if(Math.Abs(ray.Direction.Z)<1e-8)return false;double distance=-ray.Origin.Z/ray.Direction.Z;
            if(distance<0||!Geometry.Finite(distance))return false;point=ray.Origin+ray.Direction*distance;return ModClientRules.SafePoint(point,1000000);
        }
        static ModClientControl ModClientControlHit(ModClientContext c,LocalRay contextRay)
        {
            ModClientControl nearest=null;double nearestDistance=double.PositiveInfinity;
            for(int i=c.ControlOrder.Count-1;i>=0;i--)
            {
                var control=c.Controls[c.ControlOrder[i]];ModClientItem item;LocalRay ray;Vector3D point;
                if(!c.Items.TryGetValue(control.Artwork,out item)||!item.Visible||!ModClientRay(contextRay.Origin,contextRay.Direction,MatrixD.Invert(item.Transform),out ray)||!ModClientPlane(ray,out point))continue;
                var r=control.Rect;if(point.X>=r.X&&point.Y>=r.Y&&point.X<=r.X+r.Z&&point.Y<=r.Y+r.W)
                {
                    if(c.Hud)return control;
                    double distance=Vector3D.Dot(Vector3D.Transform(point,item.Transform)-contextRay.Origin,contextRay.Direction);
                    if(Geometry.Finite(distance)&&distance>=0&&distance<nearestDistance){nearest=control;nearestDistance=distance;}
                }
            }
            return nearest;
        }
        void ModClientPointer(ModClientOwner owner,ModClientContext c,LocalRay ray,bool pressed)
        {
            if(!ClientRenderingEnabled||!c.Visible){ModClientCancel(c,"display-hidden");return;}
            if(c.PointerBlockedUntilRelease){if(!pressed)c.PointerBlockedUntilRelease=false;c.Pressed=false;return;}
            bool down=pressed&&!c.Pressed,up=!pressed&&c.Pressed;
            var hit=ModClientControlHit(c,ray);Vector3D point;string id=hit==null&&ModClientPlane(ray,out point)?ModClientRules.Hit(c,point.X,point.Y):hit==null?null:hit.Id;
            ModClientRules.PointerHit(c,id,pressed);
            if(down&&hit!=null)
            {
                ModClientValue value;ModClientItem item;LocalRay local;
                if(hit.Draggable&&hit.Kind!=0&&hit.Value!=null&&c.Values.TryGetValue(hit.Value,out value)&&value.Lease==null&&c.Items.TryGetValue(hit.Artwork,out item)&&ModClientRay(ray.Origin,ray.Direction,MatrixD.Invert(hit.ReferencePose),out local))
                {
                    var capture=new ModClientCapture{Control=hit,Value=value,Item=item};bool began;
                    if(hit.Kind==1)began=InteractionMath.TryBeginPathDrag(hit.Path,local,value.Current,out capture.Path);
                    else began=InteractionMath.TryBeginRotationDrag(hit.Rotation,local,ModClientAngle(hit,value,value.Current),out capture.Rotation);
                    if(began){c.Capture=capture;value.Lease=hit;ModClientRules.PointerEvent(c,"down",hit.Id);ModClientValueEvent(c,"begin",hit,value);}
                }
            }
            var current=c.Capture;
            if(current!=null)
            {
                ModClientItem currentItem;ModClientValue currentValue;
                if(!c.Controls.ContainsKey(current.Control.Id)||!c.Items.TryGetValue(current.Control.Artwork,out currentItem)||!ReferenceEquals(currentItem,current.Item)||!currentItem.Visible||!c.Values.TryGetValue(current.Value.Id,out currentValue)||!ReferenceEquals(currentValue,current.Value)||!ReferenceEquals(current.Value.Lease,current.Control))ModClientCancel(c,"capture-revoked");
                else
                {
                    LocalRay local;if(ModClientRay(ray.Origin,ray.Direction,MatrixD.Invert(current.Control.ReferencePose),out local))
                    {
                        double requested=0,canonical;bool solved;PathDragState path=current.Path;RotationDragState rotation=current.Rotation;
                        if(current.Control.Kind==1){solved=InteractionMath.TryUpdatePathDrag(current.Control.Path,local,current.Path,out path);requested=path.Value;}
                        else{solved=InteractionMath.TryUpdateRotationDrag(current.Control.Rotation,local,current.Rotation,out rotation);requested=ModClientScalar(current.Control,current.Value,rotation.Value);}
                        if(solved&&ModClientCommit(owner,c,current.Value,requested,current.Control,false,out canonical)){current.Path=path;current.Rotation=rotation;if(!down)ModClientRules.PointerEvent(c,"move",current.Control.Id);}
                    }
                    if(up){ModClientValueEvent(c,"end",current.Control,current.Value);ModClientRules.PointerEvent(c,"up",current.Control.Id);current.Value.Lease=null;c.Capture=null;}
                }
            }
        }
        bool ModClientInteractionCommand(ModClientOwner owner,ModClientContext c,string op,string id,DrawArgs a,out object result)
        {
            result=null;
            if(op=="value")
            {
                double initial=a.Number(),min=a.Number(),max=a.Number(),step=a.Number(0);a.End();NumericRange range;double canonical;
                if(!NumericRange.TryCreate(min,max,step,out range)||!range.TryNormalize(initial,out canonical))throw new ArgumentException("Invalid numeric value range or initial value.");
                if(c.Values.ContainsKey(id))throw new ArgumentException("Value is already declared; use set-value or remove-value.");
                if(c.Values.Count>=64)throw new ArgumentException("Context value limit reached.");
                c.Values.Add(id,new ModClientValue{Id=id,Range=range,Current=canonical,Revision=++owner.ValueRevision});ModClientDeclare(owner,c);result=true;return true;
            }
            if(op=="get-value"||op=="set-value"||op=="remove-value")
            {
                ModClientValue value;if(!c.Values.TryGetValue(id,out value))throw new ArgumentException("Unknown context value.");
                if(op=="get-value"){a.End();result=new MyTuple<double,long>(value.Current,value.Revision);return true;}
                if(op=="remove-value")
                {a.End();if(c.Capture!=null&&ReferenceEquals(c.Capture.Value,value))ModClientCancel(c,"value-removed");foreach(var control in c.Controls.Values)if(control.Value==id){control.Value=null;control.Kind=0;control.Path=null;control.Rotation=null;}c.Values.Remove(id);ModClientDeclare(owner,c);result=true;return true;}
                double requested=a.Number();long expected=a.Has?a.Typed<long>():-1;a.End();
                if(expected<-1)throw new ArgumentException("Expected revision must be -1 or nonnegative.");
                if(expected>=0&&expected!=value.Revision){result=new MyTuple<bool,double,long>(false,value.Current,value.Revision);return true;}
                double canonical;bool accepted=ModClientCommit(owner,c,value,requested,null,true,out canonical);result=new MyTuple<bool,double,long>(accepted,value.Current,value.Revision);return true;
            }
            if(op=="control")
            {
                string artwork=ModClientRules.Id(a.Text());var rect=a.Typed<Vector4>();a.End();ModClientRules.Bound(rect);ModClientItem item;
                if(!c.Items.TryGetValue(artwork,out item))throw new ArgumentException("Control artwork must already exist.");
                if(c.Surface!=null)ModClientSurfaceControlPose(item.Transform);
                foreach(var old in c.Controls.Values)if(old.Id!=id&&old.Artwork==artwork)throw new ArgumentException("Artwork can have one controlling declaration.");
                if(!c.Controls.ContainsKey(id)&&c.Controls.Count>=64)throw new ArgumentException("Context control limit reached.");
                ModClientControl previous;if(c.Controls.TryGetValue(id,out previous))ModClientCancelParticipant(c,previous,"control-replaced");
                var control=new ModClientControl{Id=id,Artwork=artwork,Rect=rect,ReferencePose=item.Transform,Revision=ModClientDeclare(owner,c)};c.Controls[id]=control;c.ControlOrder.Remove(id);c.ControlOrder.Add(id);result=true;return true;
            }
            if(op=="bind-value"||op=="draggable"||op=="constraint"||op=="remove-control")
            {
                ModClientControl control;if(!c.Controls.TryGetValue(id,out control))throw new ArgumentException("Unknown context control.");
                if(op=="remove-control"){a.End();ModClientCancelParticipant(c,control,"control-removed");c.Controls.Remove(id);c.ControlOrder.Remove(id);ModClientDeclare(owner,c);result=true;return true;}
                if(op=="draggable"){bool enabled=a.Flag(true);a.End();ModClientCancelParticipant(c,control,"drag-property");control.Draggable=enabled;control.Revision=ModClientDeclare(owner,c);result=true;return true;}
                ModClientValue value;ModClientItem item=c.Items[control.Artwork];
                if(op=="bind-value")
                {string valueId=ModClientRules.Id(a.Text());a.End();if(!c.Values.TryGetValue(valueId,out value))throw new ArgumentException("Unknown bound value.");if(control.Value!=valueId&&control.Kind!=0)throw new ArgumentException("Clear or redeclare the control before changing its constrained value binding.");ModClientCancelParticipant(c,control,"binding-changed");if(c.Capture!=null&&ReferenceEquals(c.Capture.Value,value))ModClientCancel(c,"binding-joined");control.Value=valueId;control.ReferenceValue=value.Current;control.ReferencePose=item.Transform;control.Revision=ModClientDeclare(owner,c);result=true;return true;}
                if(control.Value==null||!c.Values.TryGetValue(control.Value,out value))throw new ArgumentException("Bind a numeric value before declaring its constraint.");
                string kind=a.Text();PathConstraint path=null;RotationConstraint rotation=null;double angleMin=0,angleMax=0;int mode;
                if(kind=="line"||kind=="path")
                {
                    var points=kind=="line"?new[]{a.Point(),a.Point()}:a.Typed<Vector3D[]>();a.End();
                    if(c.Hud||c.Surface!=null)foreach(var p in points)if(Math.Abs(p.Z)>1e-6)throw new ArgumentException("Canvas paths must stay on Z=0.");
                    if(!PathConstraint.TryPolyline(value.Range,points,out path))throw new ArgumentException("Invalid finite path constraint.");mode=1;
                }
                else if(kind=="rotation")
                {
                    var pivot=a.Point();var axis=a.Point();angleMin=a.Number();angleMax=a.Number();a.End();NumericRange angles;
                    if((c.Hud||c.Surface!=null)&&(Math.Abs(pivot.Z)>1e-6||Math.Abs(axis.X)>1e-6||Math.Abs(axis.Y)>1e-6))throw new ArgumentException("Canvas rotations require a Z axis and Z=0 pivot.");
                    if(!NumericRange.TryCreate(angleMin,angleMax,0,out angles)||!RotationConstraint.TryCreate(angles,pivot,axis,out rotation))throw new ArgumentException("Invalid finite angular constraint.");mode=2;
                }
                else throw new ArgumentException("Constraint requires line, path or rotation.");
                ModClientCancelParticipant(c,control,"constraint-replaced");
                control.Path=path;control.Rotation=rotation;control.Kind=mode;control.AngleMin=angleMin;control.AngleMax=angleMax;control.ReferenceValue=value.Current;control.ReferencePose=item.Transform;control.Revision=ModClientDeclare(owner,c);result=true;return true;
            }
            return false;
        }
    }
}
