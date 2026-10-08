using System;
using System.Collections.Generic;
using HDR.Interactions;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        sealed class UiValueEvent
        {public string Kind,Control,ValueId;public double Value;public long Revision,Player;}
        sealed class UiBindingTrack
        {public Item Item;public MatrixD Expected;public long SourceRevision;public PathConstraint Path;public RotationConstraint Rotation;}
        sealed class UiValueOwner
        {public long Caller,Target;public IMyProgrammableBlock CallerEntity;public IMyTerminalBlock TargetEntity;public string Program;public int LastWake=-10000;public readonly List<UiValueEvent> Events=new List<UiValueEvent>();public readonly Dictionary<string,UiBindingTrack> Bindings=new Dictionary<string,UiBindingTrack>();public readonly Dictionary<string,UiArtworkProof> Proofs=new Dictionary<string,UiArtworkProof>();}
        sealed class UiValueLease
        {public long Id,Caller,Target,Player,Definition,Source,SurfaceGeneration;public string Control,ValueId,ScreenId;public int LastSeen;public bool Scalar,AllowHiddenBundle;public PathDragState Path;public RotationDragState Rotation;}
        sealed class UiPoseCommit
        {public UiWidget Widget;public Item Item;public UiBindingTrack Track;public MatrixD Pose;}
        readonly Dictionary<string,UiValueOwner> _uiValueOwners=new Dictionary<string,UiValueOwner>();
        readonly Dictionary<long,UiValueLease> _uiValueLeases=new Dictionary<long,UiValueLease>();
        readonly Dictionary<string,long> _uiValueLocks=new Dictionary<string,long>();
        readonly HashSet<string> _uiValueWakePending=new HashSet<string>();
        long _uiDataRevision,_uiValueLeaseSequence;bool _uiValueMutation,_uiValueFlushing;int _uiDeclarationWrite;
        static string UiValueLock(long caller,long target,string value){return UiKey(caller,target)+":"+value;}
        static UiNumericValue UiFindValue(UiDisplay display,string id)
        {return display==null?null:display.Values.Find(v=>v.Id==id);}
        static UiWidget UiFindControl(UiDisplay display,string id)
        {return display==null?null:display.Widgets.Find(w=>w.Id==id&&w.Control!=null);}
        long UiNextDataRevision()
        {if(_uiDataRevision==long.MaxValue)throw new ArgumentException("UI data revision budget exhausted.");return _uiDataRevision+1;}
        void UiValueServer()
        {if(!MyAPIGateway.Multiplayer.IsServer||_uiValueMutation)throw new ArgumentException("UI values require a non-reentrant server command.");}
        UiValueOwner UiValueOwnerFor(IMyProgrammableBlock caller,IMyTerminalBlock target,bool create)
        {
            string key=UiKey(caller.EntityId,target.EntityId);UiValueOwner owner;
            if(_uiValueOwners.TryGetValue(key,out owner)&&!UiValueOwnerValid(owner)){if(!create)return null;ClearUiNumericOwner(owner,"cancel");owner=null;}
            if(owner==null&&create){if(_uiValueOwners.Count>=UiRules.MaxDisplays)throw new ArgumentException("UI value owner budget reached.");owner=new UiValueOwner{Caller=caller.EntityId,Target=target.EntityId,CallerEntity=caller,TargetEntity=target,Program=caller.ProgramData};_uiValueOwners[key]=owner;}
            return owner;
        }
        bool UiValueOwnerValid(UiValueOwner owner)
        {
            try{var caller=MyAPIGateway.Entities.GetEntityById(owner.Caller) as IMyProgrammableBlock;var target=MyAPIGateway.Entities.GetEntityById(owner.Target) as IMyTerminalBlock;if(caller==null||target==null||!ReferenceEquals(caller,owner.CallerEntity)||!ReferenceEquals(target,owner.TargetEntity)||!caller.IsWorking||!target.IsWorking||caller.ProgramData!=owner.Program)return false;Authorize(caller,target);return true;}catch{return false;}
        }
        void ClearUiNumericOwner(UiValueOwner owner,string kind)
        {
            UiCancelLeases(owner.Caller,owner.Target,null,kind);var display=GetUiDisplay(owner.Caller,owner.Target);if(display!=null){var next=UiRules.Copy(display);next.Values.Clear();next.Widgets.RemoveAll(w=>w.Control!=null);next.ValueNotify=null;next.DataRevision=UiNextDataRevision();if(_uiRevision==long.MaxValue)throw new ArgumentException("UI revision budget exhausted.");next.Revision=++_uiRevision;_uiDataRevision=next.DataRevision;_uiDisplays[UiKey(owner.Caller,owner.Target)]=next;_dirty=true;}
            string key=UiKey(owner.Caller,owner.Target);_uiValueOwners.Remove(key);_uiValueWakePending.Remove(key);
        }
        void ClearUiValueDisplay(long caller,long target)
        {UiCancelLeases(caller,target,null,"cancel");string key=UiKey(caller,target);_uiValueOwners.Remove(key);_uiValueWakePending.Remove(key);}
        void ClearUiValueRuntime()
        {_uiValueOwners.Clear();_uiValueLeases.Clear();_uiValueLocks.Clear();_uiValueWakePending.Clear();_uiDataRevision=0;_uiValueLeaseSequence=0;_uiValueMutation=_uiValueFlushing=false;}
        static double[] UiPointValues(Vector3D p){return new[]{p.X,p.Y,p.Z};}
        static double[] UiPathValues(Vector3D[] points)
        {if(points==null||points.Length<2||points.Length>256)throw new ArgumentException("Constraint path requires 2–256 points.");var a=new double[points.Length*3];for(int i=0;i<points.Length;i++){a[3*i]=points[i].X;a[3*i+1]=points[i].Y;a[3*i+2]=points[i].Z;}return a;}
        void UiAnimationControlAllowed(long caller,long target,string artwork,bool packed=false)
        {
            var d=GetUiDisplay(caller,target);if(d==null)return;foreach(var w in d.Widgets)if(w.Control!=null&&(w.Control.Artwork==artwork||packed&&w.Control.Artwork.StartsWith(artwork+"~",StringComparison.Ordinal))&&w.Control.ValueId!=null&&w.Control.ConstraintKind!=null)throw new ArgumentException("Artwork has a numeric control binding; unbind it before starting an animation.");
        }
        void UiNoActiveAnimation(long caller,long target,string artwork)
        {foreach(var state in _drawAnimations.Values)if(state.Data.CallerId==caller&&state.Data.ConsoleId==target&&(state.Id==artwork||state.Data.Packed!=null&&artwork.StartsWith(state.Id+"~",StringComparison.Ordinal)))throw new ArgumentException("Stop the artwork animation before binding or changing a numeric control.");}
        void UiCommitNumericDefinition(DrawContext context,UiDisplay next,UiValueOwner owner)
        {
            if(_uiRevision==long.MaxValue)throw new ArgumentException("UI revision budget exhausted.");next.Revision=_uiRevision+1;UiRules.Display(next);
            foreach(var widget in next.Widgets)if(widget.Control!=null){var item=UiOwnedControlItem(context.Caller,context.Target,widget);UiNoActiveAnimation(next.CallerId,next.TargetId,item.Id);UiValidateLcdConstraint(context.Target,widget.Control);UiValidateControlPose(context.Target,item,UiSourceProof(owner,item),item.Transform,GetScene(next.TargetId));}
            int values=next.Values.Count,widgets=next.Widgets.Count;foreach(var d in _uiDisplays.Values)if(d.CallerId!=next.CallerId||d.TargetId!=next.TargetId){values+=d.Values.Count;widgets+=d.Widgets.Count;}
            if(values>UiValueRules.MaxTotalValues||widgets>UiRules.MaxTotalWidgets||GetUiDisplay(next.CallerId,next.TargetId)==null&&_uiDisplays.Count>=UiRules.MaxDisplays)throw new ArgumentException("Global UI value/control budget reached.");
            UiAdmitMetadata(next);
            GetScene(next.TargetId);UiCancelLeases(next.CallerId,next.TargetId,null,"cancel");_uiRevision=next.Revision;_uiDataRevision=Math.Max(_uiDataRevision,next.DataRevision);_uiDisplays[UiKey(next.CallerId,next.TargetId)]=next;owner.Bindings.Clear();UiPruneSourceProofs(owner,next);_dirty=true;
        }
        static void UiPruneSourceProofs(UiValueOwner owner,UiDisplay display)
        {var live=new HashSet<string>();foreach(var w in display.Widgets)if(w.Control!=null)live.Add(w.Control.Artwork);var remove=new List<string>();foreach(var id in owner.Proofs.Keys)if(!live.Contains(id))remove.Add(id);foreach(var id in remove)owner.Proofs.Remove(id);}
        void UiRebaseControl(UiWidget widget,UiNumericValue value,Item item,long source)
        {widget.Control.ReferencePose=MatrixValues(item.Transform);widget.Control.ReferenceValue=value==null?0:value.Value;widget.Control.SourceRevision=source;}
        bool TryUiValueCommand(DrawContext context,string op,DrawArgs args,out object result)
        {
            result=null;switch(op){case "value":case "control":case "bind-value":case "draggable":case "constraint":case "get-value":case "set-value":case "poll-value-events":case "remove-value":case "value-notify":break;default:return false;}
            UiValueServer();var target=op=="get-value"?AuthorizeAccess(context.Caller,context.Target):Authorize(context.Caller,context.Target);if(!context.Caller.IsWorking||!target.IsWorking)throw new ArgumentException("UI values require a working PB and display.");bool create=op!="get-value"&&op!="set-value"&&op!="poll-value-events"&&op!="remove-value";var owner=UiValueOwnerFor(context.Caller,target,create);if(owner==null)throw new ArgumentException("UI value owner is inactive.");var old=GetUiDisplay(context.Caller.EntityId,target.EntityId);
            if(op=="get-value"){string id=UiRules.Id(args.Text());args.End();var v=UiFindValue(old,id);if(v==null)throw new ArgumentException("UI value does not exist.");result=new MyTuple<double,long>(v.Value,v.Revision);return true;}
            if(op=="set-value"){string id=UiRules.Id(args.Text());double requested=args.Number();long expected=args.Has?UiRevisionArgument(args.Value()):-1;args.End();result=UiSetValue(context.Caller.EntityId,target.EntityId,id,requested,expected,0,null,false);return true;}
            if(op=="poll-value-events")
            {
                string prefix=args.Text("");args.End();if(prefix.Length>0)UiRules.Id(prefix);
                var matching=new List<MyTuple<string,string,string,MyTuple<double,long,long>>>();
                foreach(var e in owner.Events)if(UiValueEventMatches(e,prefix))matching.Add(new MyTuple<string,string,string,MyTuple<double,long,long>>(e.Kind,e.Control,e.ValueId,new MyTuple<double,long,long>(e.Value,e.Revision,e.Player)));
                var events=matching.ToArray();owner.Events.RemoveAll(e=>UiValueEventMatches(e,prefix));
                if(owner.Events.Count==0)_uiValueWakePending.Remove(UiKey(owner.Caller,owner.Target));result=events;return true;
            }
            var next=old==null?new UiDisplay{CallerId=context.Caller.EntityId,TargetId=target.EntityId}:UiRules.Copy(old);
            if(op=="remove-value")
            {
                string id=UiRules.Id(args.Text());args.End();var value=UiFindValue(next,id);
                if(value==null){result=false;return true;}
                foreach(var w in next.Widgets)if(w.Control!=null&&w.Control.ValueId==id)throw new ArgumentException("Unbind every control before removing its numeric value.");
                next.Values.Remove(value);next.DataRevision=UiNextDataRevision();UiCommitNumericDefinition(context,next,owner);
                owner.Events.RemoveAll(e=>e.ValueId==id);
                if(owner.Events.Count==0)_uiValueWakePending.Remove(UiKey(owner.Caller,owner.Target));result=true;return true;
            }
            if(op=="value-notify"){next.ValueNotify=args.Text("");args.End();if(next.ValueNotify.Length>128)throw new ArgumentException("Value notification uses at most 128 fixed argument characters.");UiCommitNumericDefinition(context,next,owner);result=true;return true;}
            if(op=="value")
            {
                string id=UiRules.Id(args.Text());double initial=args.Number(),min=args.Number(),max=args.Number(),step=args.Number(0);args.End();NumericRange range;double canonical;if(!NumericRange.TryCreate(min,max,step,out range)||!range.TryNormalize(initial,out canonical))throw new ArgumentException("Invalid numeric value/range/step.");var v=UiFindValue(next,id);
                if(v!=null&&v.Min==min&&v.Max==max&&v.Step==step){result=new MyTuple<double,long>(v.Value,v.Revision);return true;}
                long revision=UiNextDataRevision();if(v==null){if(next.Values.Count>=UiValueRules.MaxValues)throw new ArgumentException("UI value budget reached.");v=new UiNumericValue{Id=id};next.Values.Add(v);}v.Min=min;v.Max=max;v.Step=step;v.Value=canonical;v.Revision=revision;next.DataRevision=revision;
                foreach(var w in next.Widgets)if(w.Control!=null&&w.Control.ValueId==id){var item=UiOwnedControlItem(context.Caller,target,w);UiNoActiveAnimation(owner.Caller,owner.Target,item.Id);UiRebaseControl(w,v,item,revision);}UiCommitNumericDefinition(context,next,owner);result=new MyTuple<double,long>(v.Value,v.Revision);return true;
            }
            string controlId=UiRules.Id(args.Text());var widget=UiFindControl(next,controlId);
            if(op=="control")
            {
                string bundle=UiRules.Id(args.Text()),artwork=UiSelectedArtwork(context,args.Text());ValidateWriteId(context.Caller,target,artwork);var item=GetItem(context.Caller,target,artwork);UiNoActiveAnimation(owner.Caller,owner.Target,artwork);if(!next.Bundles.Exists(b=>b.Id==bundle))throw new ArgumentException("Declare the UI bundle first.");
                foreach(var other in next.Widgets)if(other.Control!=null&&other.Id!=controlId&&other.Control.Artwork==artwork)throw new ArgumentException("Artwork already has a numeric control.");
                var control=new UiWidget{Id=controlId,Bundle=bundle,X=args.Number(),Y=args.Number(),Width=args.Number(),Height=args.Number(),ActionKind="control",Argument="",Control=new UiControlData{Artwork=artwork},ScreenId=context.ScreenId};args.End();long source=UiNextDataRevision();next.DataRevision=source;UiRebaseControl(control,null,item,source);
                var prior=next.Widgets.Find(w=>w.Id==controlId);if(prior!=null){if(prior.Bundle!=bundle)throw new ArgumentException("Remove a control before moving it to another bundle.");next.Widgets.Remove(prior);}next.Widgets.Add(control);UiCommitNumericDefinition(context,next,owner);result=true;return true;
            }
            if(widget==null)throw new ArgumentException("Artwork control does not exist.");var c=widget.Control;var sourceItem=UiOwnedControlItem(context.Caller,target,widget);UiNoActiveAnimation(owner.Caller,owner.Target,c.Artwork);long epoch=UiNextDataRevision();next.DataRevision=epoch;
            if(op=="bind-value"){string id=args.Text();args.End();if(id==""){c.ValueId=null;c.ConstraintKind=null;c.Points=c.Pivot=c.Axis=null;}else{UiRules.Id(id);if(UiFindValue(next,id)==null)throw new ArgumentException("Declare the UI value first.");c.ValueId=id;}}
            else if(op=="draggable"){c.Draggable=args.Flag(true);args.End();}
            else
            {
                if(c.ValueId==null)throw new ArgumentException("Bind a numeric value before declaring a constraint.");string kind=args.Text();c.ConstraintKind=kind;c.Points=c.Pivot=c.Axis=null;
                if(kind=="line")c.Points=UiPathValues(new[]{args.Point(),args.Point()});else if(kind=="path")c.Points=UiPathValues(args.Typed<Vector3D[]>());else if(kind=="rotation"){c.Pivot=UiPointValues(args.Point());c.Axis=UiPointValues(args.Point());c.AngleMin=args.Number();c.AngleMax=args.Number();}else throw new ArgumentException("Constraints are line, path or rotation.");args.End();
            }
            UiRebaseControl(widget,UiFindValue(next,c.ValueId),sourceItem,epoch);UiCommitNumericDefinition(context,next,owner);result=true;return true;
        }
        static bool UiValueEventMatches(UiValueEvent value,string prefix)
        {return prefix.Length==0||value.Control!=null&&value.Control.StartsWith(prefix,StringComparison.Ordinal)||value.ValueId!=null&&value.ValueId.StartsWith(prefix,StringComparison.Ordinal);}
        static long UiRevisionArgument(object value)
        {if(value is long)return (long)value;if(value is int)return (int)value;throw new ArgumentException("Expected revision must be an integer.");}
        UiBindingTrack UiTrack(UiDisplay display,UiWidget widget)
        {
            UiValueOwner owner;if(!_uiValueOwners.TryGetValue(UiKey(display.CallerId,display.TargetId),out owner))throw new ArgumentException("Numeric control owner is inactive.");var caller=MyAPIGateway.Entities.GetEntityById(display.CallerId) as IMyProgrammableBlock;var target=MyAPIGateway.Entities.GetEntityById(display.TargetId) as IMyTerminalBlock;var item=UiOwnedControlItem(caller,target,widget);UiBindingTrack track;
            if(owner.Bindings.TryGetValue(widget.Id,out track)&&track.SourceRevision==widget.Control.SourceRevision){if(!ReferenceEquals(track.Item,item)||track.Expected!=item.Transform)throw new ArgumentException("Artwork changed; rebase the control before writing its value.");return track;}
            UiValidateControlPose(target,item,UiSourceProof(owner,item),item.Transform,GetScene(display.TargetId));track=new UiBindingTrack{Item=item,Expected=item.Transform,SourceRevision=widget.Control.SourceRevision};var value=UiFindValue(display,widget.Control.ValueId);if(widget.Control.ConstraintKind!=null&&!UiValueRules.Constraint(widget.Control,value,out track.Path,out track.Rotation))throw new ArgumentException("Invalid numeric control constraint.");owner.Bindings[widget.Id]=track;return track;
        }
        bool UiControlSourceValid(UiDisplay display,UiWidget widget)
        {return UiControlSourceValid(display,widget,false);}
        bool UiControlSourceValid(UiDisplay display,UiWidget widget,bool allowHiddenBundle)
        {
            try
            {
                if(display==null||widget==null||widget.Control==null||!HasVisibleUiWidget(display,widget,allowHiddenBundle))return false;Scene scene;if(!_scenes.TryGetValue(display.TargetId,out scene))return false;Item item;if(!scene.Items.TryGetValue(Key(display.CallerId,widget.Control.Artwork),out item)||item.CallerId!=display.CallerId||!item.Visible||item.Opacity<=0)return false;
                if(widget.ScreenId!=null&&!item.Id.StartsWith(ScreenPrefix(widget.ScreenId),StringComparison.Ordinal)||widget.ScreenId==null&&IsProjectedId(item.Id))return false;
                if(LayerAlpha(scene,item.CallerId,item.Layer)<=0){Layer layer;if(!allowHiddenBundle||item.Layer!=UiRenderLayer(widget.ScreenId,widget.Bundle)||!scene.Layers.TryGetValue(Key(item.CallerId,item.Layer),out layer)||layer.Opacity<=0)return false;}
                if(MyAPIGateway.Multiplayer.IsServer){UiValueOwner owner;if(!_uiValueOwners.TryGetValue(UiKey(display.CallerId,display.TargetId),out owner)||!UiValueOwnerValid(owner))return false;UiNoActiveAnimation(display.CallerId,display.TargetId,item.Id);UiTrack(display,widget);}return true;
            }catch{return false;}
        }
        List<UiPoseCommit> UiPrepareValuePoses(UiDisplay display,UiNumericValue value,double canonical)
        {
            var result=new List<UiPoseCommit>();var caller=MyAPIGateway.Entities.GetEntityById(display.CallerId) as IMyProgrammableBlock;var target=MyAPIGateway.Entities.GetEntityById(display.TargetId) as IMyTerminalBlock;var scene=GetScene(display.TargetId);
            foreach(var widget in display.Widgets)if(widget.Control!=null&&widget.Control.ValueId==value.Id&&widget.Control.ConstraintKind!=null)
            {
                UiNoActiveAnimation(display.CallerId,display.TargetId,widget.Control.Artwork);var track=UiTrack(display,widget);var control=widget.Control;MatrixD delta;bool valid=track.Path!=null?track.Path.TryGetTranslationDelta(control.ReferenceValue,canonical,out delta):track.Rotation.TryGetRotationDelta(UiValueRules.ToAngle(control,value,control.ReferenceValue),UiValueRules.ToAngle(control,value,canonical),out delta);
                if(!valid)throw new ArgumentException("Constraint could not produce a bounded pose.");var pose=delta*UiValueRules.Matrix(control.ReferencePose);UiValueOwner owner=_uiValueOwners[UiKey(display.CallerId,display.TargetId)];UiValidateLcdConstraint(target,control);UiValidateControlPose(target,track.Item,UiSourceProof(owner,track.Item),pose,scene);result.Add(new UiPoseCommit{Widget=widget,Item=track.Item,Track=track,Pose=pose});
            }
            return result;
        }
        MyTuple<bool,double,long> UiSetValue(long callerId,long targetId,string valueId,double requested,long expected,long player,UiValueLease lease,bool fromLease)
        {
            UiValueServer();var caller=MyAPIGateway.Entities.GetEntityById(callerId) as IMyProgrammableBlock;var target=MyAPIGateway.Entities.GetEntityById(targetId) as IMyTerminalBlock;Authorize(caller,target);var owner=UiValueOwnerFor(caller,target,false);if(owner==null||!UiValueOwnerValid(owner))throw new ArgumentException("UI value owner is inactive.");var display=GetUiDisplay(callerId,targetId);var value=UiFindValue(display,valueId);if(value==null)throw new ArgumentException("UI value does not exist.");if(expected< -1)throw new ArgumentException("Expected revision must be -1 or nonnegative.");if(expected>=0&&expected!=value.Revision)return new MyTuple<bool,double,long>(false,value.Value,value.Revision);
            double canonical;if(!UiValueRules.Range(value).TryNormalize(requested,out canonical))throw new ArgumentException("Requested value must be finite and bounded.");var poses=UiPrepareValuePoses(display,value,canonical);bool changed=canonical!=value.Value;long locked;bool active=_uiValueLocks.TryGetValue(UiValueLock(callerId,targetId,valueId),out locked);
            if(fromLease&&(lease==null||!active||locked!=lease.Id||lease.Player!=player))return new MyTuple<bool,double,long>(false,value.Value,value.Revision);
            long revision=changed||!fromLease&&active?UiNextDataRevision():_uiDataRevision;
            _uiValueMutation=true;try
            {
                if(!fromLease&&active)UiCancelLeases(callerId,targetId,valueId,"cancel");
                if(changed){foreach(var p in poses){p.Item.Transform=p.Pose;p.Track.Expected=p.Pose;}value.Value=canonical;value.Revision=revision;display.DataRevision=revision;_uiDataRevision=revision;_dirty=true;UiPostValueEvent(display,lease==null?"":lease.Control,value,"change",player);}
                if(!fromLease&&active){display.DataRevision=revision;_uiDataRevision=revision;foreach(var w in display.Widgets)if(w.Control!=null&&w.Control.ValueId==valueId){var item=UiOwnedControlItem(caller,target,w);UiRebaseControl(w,value,item,revision);owner.Bindings.Remove(w.Id);}_dirty=true;}
            }finally{_uiValueMutation=false;}
            return new MyTuple<bool,double,long>(true,value.Value,value.Revision);
        }
        void UiPostValueEvent(UiDisplay display,string control,UiNumericValue value,string kind,long player)
        {
            UiValueOwner owner;if(!_uiValueOwners.TryGetValue(UiKey(display.CallerId,display.TargetId),out owner))return;var e=new UiValueEvent{Kind=kind,Control=control,ValueId=value==null?"":value.Id,Value=value==null?0:value.Value,Revision=value==null?0:value.Revision,Player=player};
            if(kind=="change")for(int i=owner.Events.Count-1;i>=0;i--){var prior=owner.Events[i];if(prior.Kind==kind&&prior.Control==control&&prior.ValueId==e.ValueId&&prior.Player==player){owner.Events[i]=e;if(!string.IsNullOrEmpty(display.ValueNotify))_uiValueWakePending.Add(UiKey(owner.Caller,owner.Target));return;}}
            if(owner.Events.Count>=UiValueRules.MaxEvents){int change=owner.Events.FindIndex(v=>v.Kind=="change");owner.Events.RemoveAt(change>=0?change:0);}owner.Events.Add(e);if(!string.IsNullOrEmpty(display.ValueNotify))_uiValueWakePending.Add(UiKey(owner.Caller,owner.Target));
        }
        bool UiLeaseHuman(UiDisplay display,UiWidget widget,long player,bool allowHiddenBundle=false)
        {
            if(player<=0||!UiControlSourceValid(display,widget,allowHiddenBundle))return false;var caller=MyAPIGateway.Entities.GetEntityById(display.CallerId) as IMyProgrammableBlock;var target=MyAPIGateway.Entities.GetEntityById(display.TargetId) as IMyTerminalBlock;return caller.HasPlayerAccess(player)&&target.HasPlayerAccess(player);
        }
        bool UiTryBeginValueLeaseCore(long caller,long target,string control,long player,LocalRay ray,long expectedDef,long expectedValue,long expectedSource,bool scalar,bool allowHiddenBundle,out long leaseId)
        {
            leaseId=0;try
            {
                UiValueServer();var d=GetUiDisplay(caller,target);var w=UiFindControl(d,control);if(d==null||d.Revision!=expectedDef||w==null||!w.Control.Draggable||w.Control.SourceRevision!=expectedSource||!UiLeaseHuman(d,w,player,allowHiddenBundle))return false;var v=UiFindValue(d,w.Control.ValueId);if(v==null||v.Revision!=expectedValue||w.Control.ConstraintKind==null)return false;string key=UiValueLock(caller,target,v.Id);if(_uiValueLocks.ContainsKey(key)||_uiValueLeaseSequence==long.MaxValue)return false;var track=UiTrack(d,w);var lease=new UiValueLease{Id=_uiValueLeaseSequence+1,Caller=caller,Target=target,Player=player,Definition=expectedDef,Source=expectedSource,Control=control,ValueId=v.Id,LastSeen=_ticks,Scalar=scalar,AllowHiddenBundle=allowHiddenBundle};
                lease.ScreenId=w.ScreenId;lease.SurfaceGeneration=w.ScreenId==null?0:UiProjectedGeneration(d,w);if(lease.SurfaceGeneration<0)return false;
                if(!scalar){bool begin=track.Path!=null?InteractionMath.TryBeginPathDrag(track.Path,ray,v.Value,out lease.Path):InteractionMath.TryBeginRotationDrag(track.Rotation,ray,UiValueRules.ToAngle(w.Control,v,v.Value),out lease.Rotation);if(!begin)return false;}
                _uiValueLeaseSequence=lease.Id;_uiValueLeases.Add(lease.Id,lease);_uiValueLocks[key]=lease.Id;leaseId=lease.Id;UiPostValueEvent(d,control,v,"begin",player);return true;
            }catch{return false;}
        }
        bool UiTryBeginValueLease(long caller,long target,string control,long player,LocalRay ray,long expectedDef,long expectedValue,long expectedSource,out long lease)
        {return UiTryBeginValueLeaseCore(caller,target,control,player,ray,expectedDef,expectedValue,expectedSource,false,false,out lease);}
        bool UiTryBeginValueLease(long caller,long target,string control,long player,LocalRay ray,long expectedDef,long expectedValue,long expectedSource,bool allowHiddenBundle,out long lease)
        {return UiTryBeginValueLeaseCore(caller,target,control,player,ray,expectedDef,expectedValue,expectedSource,false,allowHiddenBundle,out lease);}
        bool UiTryBeginValueLeaseScalar(long caller,long target,string control,long player,long expectedDef,long expectedValue,long expectedSource,out long lease)
        {return UiTryBeginValueLeaseCore(caller,target,control,player,default(LocalRay),expectedDef,expectedValue,expectedSource,true,false,out lease);}
        bool UiTryBeginValueLeaseScalar(long caller,long target,string control,long player,long expectedDef,long expectedValue,long expectedSource,bool allowHiddenBundle,out long lease)
        {return UiTryBeginValueLeaseCore(caller,target,control,player,default(LocalRay),expectedDef,expectedValue,expectedSource,true,allowHiddenBundle,out lease);}
        bool UiValidateValueLease(long id,long player)
        {
            UiValueLease lease;if(!_uiValueLeases.TryGetValue(id,out lease)||lease.Player!=player)return false;var d=GetUiDisplay(lease.Caller,lease.Target);var w=UiFindControl(d,lease.Control);return d!=null&&d.Revision==lease.Definition&&w!=null&&w.ScreenId==lease.ScreenId&&(w.ScreenId==null||UiProjectedGeneration(d,w)==lease.SurfaceGeneration)&&w.Control.SourceRevision==lease.Source&&w.Control.Draggable&&w.Control.ValueId==lease.ValueId&&UiLeaseHuman(d,w,player,lease.AllowHiddenBundle);
        }
        bool UiTryCommitValueLeaseScalar(long id,long player,double requested,out MyTuple<bool,double,long> result)
        {
            result=new MyTuple<bool,double,long>();try{UiValueLease lease;if(!_uiValueLeases.TryGetValue(id,out lease)||!lease.Scalar||!UiValidateValueLease(id,player))return false;result=UiSetValue(lease.Caller,lease.Target,lease.ValueId,requested,-1,player,lease,true);if(result.Item1)lease.LastSeen=_ticks;return result.Item1;}catch{return false;}
        }
        bool UiTryUpdateValueLease(long id,long player,LocalRay ray,out MyTuple<bool,double,long> result)
        {
            result=new MyTuple<bool,double,long>();try
            {
                UiValueLease lease;if(!_uiValueLeases.TryGetValue(id,out lease)||lease.Scalar||!UiValidateValueLease(id,player))return false;var d=GetUiDisplay(lease.Caller,lease.Target);var w=UiFindControl(d,lease.Control);var v=UiFindValue(d,lease.ValueId);var track=UiTrack(d,w);PathDragState path=lease.Path;RotationDragState rotation=lease.Rotation;bool updated=track.Path!=null?InteractionMath.TryUpdatePathDrag(track.Path,ray,lease.Path,out path):InteractionMath.TryUpdateRotationDrag(track.Rotation,ray,lease.Rotation,out rotation);if(!updated)return false;double request=track.Path!=null?path.Value:UiValueRules.FromAngle(w.Control,v,rotation.Value);result=UiSetValue(lease.Caller,lease.Target,lease.ValueId,request,-1,player,lease,true);if(!result.Item1)return false;lease.Path=path;lease.Rotation=rotation;lease.LastSeen=_ticks;return true;
            }catch{return false;}
        }
        bool UiEndValueLease(long id,long player,bool commit)
        {
            UiValueLease lease;if(!_uiValueLeases.TryGetValue(id,out lease)||lease.Player!=player)return false;bool valid=UiValidateValueLease(id,player);_uiValueLeases.Remove(id);string key=UiValueLock(lease.Caller,lease.Target,lease.ValueId);long held;if(_uiValueLocks.TryGetValue(key,out held)&&held==id)_uiValueLocks.Remove(key);var d=GetUiDisplay(lease.Caller,lease.Target);if(d!=null)UiPostValueEvent(d,lease.Control,UiFindValue(d,lease.ValueId),commit&&valid?"commit":"cancel",player);return valid;
        }
        bool UiClickControl(long caller,long target,string control,long player)
        {return UiClickControl(caller,target,control,player,false);}
        bool UiClickControl(long caller,long target,string control,long player,bool allowHiddenBundle)
        {var d=GetUiDisplay(caller,target);var w=UiFindControl(d,control);if(w==null||w.Control.Draggable||!UiLeaseHuman(d,w,player,allowHiddenBundle))return false;UiPostValueEvent(d,control,UiFindValue(d,w.Control.ValueId),"click",player);return true;}
        void UiCancelLeases(long caller,long target,string value,string kind)
        {var remove=new List<long>();foreach(var lease in _uiValueLeases.Values)if(lease.Caller==caller&&lease.Target==target&&(value==null||lease.ValueId==value))remove.Add(lease.Id);foreach(long id in remove){UiValueLease lease;if(_uiValueLeases.TryGetValue(id,out lease))UiEndValueLease(id,lease.Player,false);}}
        void NotifyUiArtworkChanged(long caller,long target,string artwork,bool removed)
        {
            if(_uiValueMutation||_uiDeclarationWrite>0)return;var d=GetUiDisplay(caller,target);UiValueOwner owner;if(d==null||!_uiValueOwners.TryGetValue(UiKey(caller,target),out owner))return;if(removed)owner.Proofs.Remove(artwork);var changed=new List<UiWidget>();foreach(var w in d.Widgets)if(w.Control!=null&&w.Control.Artwork==artwork)changed.Add(w);if(changed.Count==0)return;long revision=UiNextDataRevision();foreach(var w in changed){UiCancelLeases(caller,target,w.Control.ValueId,"cancel");owner.Bindings.Remove(w.Id);if(removed){d.Widgets.Remove(w);owner.Proofs.Remove(artwork);}else{Scene scene;Item item;if(_scenes.TryGetValue(target,out scene)&&scene.Items.TryGetValue(Key(caller,artwork),out item)){UiRebaseControl(w,UiFindValue(d,w.Control.ValueId),item,revision);try{UiSourceProof(owner,item);}catch{/* A changed invalid declaration cannot acquire another gesture. */}}}}if(removed){if(_uiRevision==long.MaxValue)throw new ArgumentException("UI revision budget exhausted.");d.Revision=++_uiRevision;}d.DataRevision=revision;_uiDataRevision=revision;_dirty=true;
        }
        void NotifyUiDisplayViewChanged(long target)
        {
            foreach(var display in new List<UiDisplay>(_uiDisplays.Values))if(display.TargetId==target)
            {
                UiValueOwner owner;if(!_uiValueOwners.TryGetValue(UiKey(display.CallerId,target),out owner)||!display.Widgets.Exists(w=>w.Control!=null&&w.ScreenId==null))continue;long revision=UiNextDataRevision();foreach(var lease in new List<UiValueLease>(_uiValueLeases.Values))if(lease.Caller==display.CallerId&&lease.Target==target&&lease.ScreenId==null)UiEndValueLease(lease.Id,lease.Player,false);
                foreach(var widget in display.Widgets)if(widget.Control!=null&&widget.ScreenId==null){Scene scene;Item item;if(_scenes.TryGetValue(target,out scene)&&scene.Items.TryGetValue(Key(display.CallerId,widget.Control.Artwork),out item))UiRebaseControl(widget,UiFindValue(display,widget.Control.ValueId),item,revision);}owner.Bindings.Clear();display.DataRevision=revision;_uiDataRevision=revision;_dirty=true;
            }
        }
        void ReconcileUiValueDefinitions(UiDisplay previous,UiDisplay next)
        {if(previous==null)return;UiValueOwner owner;if(!_uiValueOwners.TryGetValue(UiKey(previous.CallerId,previous.TargetId),out owner))return;UiCancelLeases(previous.CallerId,previous.TargetId,null,"cancel");owner.Bindings.Clear();UiPruneSourceProofs(owner,next);}
        void TickUiValueState()
        {
            if(!MyAPIGateway.Multiplayer.IsServer)return;var owners=new List<UiValueOwner>(_uiValueOwners.Values);foreach(var owner in owners)if(!UiValueOwnerValid(owner)){ClearUiNumericOwner(owner,"cancel");}
            var leases=new List<UiValueLease>(_uiValueLeases.Values);foreach(var lease in leases)if(_ticks-lease.LastSeen>180||!UiValidateValueLease(lease.Id,lease.Player))UiEndValueLease(lease.Id,lease.Player,false);
        }
        void TickUiValueWakes()
        {
            if(!MyAPIGateway.Multiplayer.IsServer||_uiValueMutation||_uiValueFlushing)return;_uiValueFlushing=true;try
            {
                int budget=4;foreach(string key in new List<string>(_uiValueWakePending))
                {if(budget--<=0)break;UiValueOwner owner;if(!_uiValueOwners.TryGetValue(key,out owner)||!UiValueOwnerValid(owner)){_uiValueWakePending.Remove(key);continue;}var d=GetUiDisplay(owner.Caller,owner.Target);if(d==null||owner.Events.Count==0||string.IsNullOrEmpty(d.ValueNotify)){_uiValueWakePending.Remove(key);continue;}if(_ticks-owner.LastWake<6)continue;var caller=MyAPIGateway.Entities.GetEntityById(owner.Caller) as IMyProgrammableBlock;owner.LastWake=_ticks;_uiValueWakePending.Remove(key);bool accepted=false;try{accepted=caller.TryRun(d.ValueNotify);}catch{/* Best-effort callback failure never escapes the simulation tick. */}UiValueOwner current;if(!accepted&&_uiValueOwners.TryGetValue(key,out current)&&ReferenceEquals(current,owner)&&UiValueOwnerValid(owner)&&owner.Events.Count>0)_uiValueWakePending.Add(key);}
            }finally{_uiValueFlushing=false;}
        }
    }
}
