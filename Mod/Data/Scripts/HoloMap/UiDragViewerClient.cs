using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        bool UiDragPersistentFocus=true;
        UiDragRequest _uiViewerRequest;
        UiDisplay _uiViewerDisplay;
        string _uiViewerBundle,_uiViewerScreenId;long _uiViewerSurfaceGeneration;
        long _uiViewerId,_uiViewerCharacter,_uiViewerTile,_uiViewerLastFrame=-1;
        bool _uiViewerGranted,_uiViewerDownHeld,_uiViewerHiddenBundle;
        int _uiViewerStarted,_uiViewerLastGrant,_uiViewerLastKeep;
        MatrixD _uiViewerView;
        double[] _uiViewerLayout;
        readonly Dictionary<string,long> _uiViewerSources=new Dictionary<string,long>();
        readonly HashSet<long> _uiViewerSent=new HashSet<long>();
        UiWidget _uiViewerDownControl;
        UiNumericValue _uiViewerDownValue;
        UiPointerSample _uiViewerDownSample;
        long _uiViewerDownTile;
        bool UiViewerRequested {get{return _uiViewerRequest!=null;}}

        bool TryEnterUiDragViewer(UiDisplay display,UiWidget widget,long tileId)
        {
            if(widget==null||widget.Control==null)return false;
            InitializeUiPointer();
            if(UiViewerRequested){CloseUiDragViewerLocal(true);return true;}
            if(_uiPointer==null||!_uiPointer.Ready)
            {NotifyPluginUnavailable("interactive-pointer");return true;}
            if(_uiDragLocal==null||_uiDragLocal.Pending)return true;
            var character=MyAPIGateway.Session.Player.Character;Scene source,tile;
            if(!_scenes.TryGetValue(display.TargetId,out source)||!_scenes.TryGetValue(tileId,out tile))return true;
            string key=display.CallerId+"/"+display.TargetId+"/"+widget.Bundle+"/"+display.Revision;
            if(!_uiPointer.Acquire(character.EntityId,key))return true;
            _uiViewerHiddenBundle=UiControlLocalBundleGranted(display,widget);
            long sequence=NextUiClientSequence();
            _uiViewerRequest=new UiDragRequest{Kind=(int)UiDragKind.Focus,CallerId=display.CallerId,TargetId=display.TargetId,
                ControlId=widget.Id,DefinitionRevision=display.Revision,ValueRevision=widget.Control.SourceRevision,
                RequestId=sequence,Sequence=sequence,Mode=(int)UiDragMode.FocusedPointer,ScreenId=widget.ScreenId,SurfaceGeneration=widget.ScreenId==null?0:UiProjectedGeneration(display,widget)};
            _uiViewerDisplay=display;_uiViewerBundle=widget.Bundle;_uiViewerCharacter=character.EntityId;_uiViewerTile=tileId;
            _uiViewerScreenId=widget.ScreenId;_uiViewerSurfaceGeneration=_uiViewerRequest.SurfaceGeneration;
            _uiViewerView=LocalView(source);_uiViewerLayout=CaptureUiFocusLayout(source,tile);_uiViewerId=0;_uiViewerGranted=false;
            _uiViewerStarted=_uiViewerLastGrant=_uiViewerLastKeep=_ticks;_uiViewerLastFrame=-1;_uiViewerDownHeld=false;
            _uiViewerDownControl=null;_uiViewerDownValue=null;_uiViewerSent.Clear();_uiViewerSent.Add(sequence);_uiViewerSources.Clear();
            foreach(var control in display.Widgets)if(control.Bundle==widget.Bundle&&control.ScreenId==widget.ScreenId&&control.Control!=null)_uiViewerSources[control.Id]=control.Control.SourceRevision;
            SendUiDragRequest(_uiViewerRequest);return true;
        }
        void ReceiveUiDragViewerAcknowledgement(UiDragAck ack)
        {
            var pending=_uiViewerRequest;
            if(pending==null||ack.CallerId!=pending.CallerId||ack.TargetId!=pending.TargetId||ack.ControlId!=pending.ControlId
                ||ack.DefinitionRevision!=pending.DefinitionRevision||ack.RequestId!=pending.RequestId||!_uiViewerSent.Contains(ack.Sequence)
                ||_uiViewerId!=0&&ack.ViewerId!=_uiViewerId)return;
            if(ack.Terminal||ack.Status!=(int)UiDragStatus.Accepted){CloseUiDragViewerLocal(false);return;}
            if(ack.ViewerId<=0||ack.LeaseId!=ack.ViewerId||ack.CharacterId!=_uiViewerCharacter||ack.TileId!=_uiViewerTile
                ||ack.ValueRevision!=pending.ValueRevision){CloseUiDragViewerLocal(true);return;}
            if(ack.ScreenId!=_uiViewerScreenId||ack.SurfaceGeneration!=_uiViewerSurfaceGeneration){CloseUiDragViewerLocal(true);return;}
            _uiViewerId=ack.ViewerId;_uiViewerGranted=true;_uiViewerLastGrant=_ticks;
        }
        void CloseUiDragViewerLocal(bool notify)
        {
            bool had=UiViewerRequested;var previous=_uiViewerRequest;long id=_uiViewerId;
            if(had&&notify)
            {var blur=UiDragWire.Copy(previous);blur.Kind=(int)UiDragKind.Blur;blur.Sequence=NextUiClientSequence();blur.ViewerId=blur.LeaseId=id;
                try{SendUiDragRequest(blur);}catch{}}
            _uiViewerRequest=null;_uiViewerDisplay=null;_uiViewerGranted=false;_uiViewerId=0;
            _uiViewerDownHeld=false;_uiViewerDownControl=null;_uiViewerDownValue=null;_uiViewerSent.Clear();_uiViewerSources.Clear();
            if(_uiDragLocal!=null)_uiDragLocal.Cancel(_ticks);
            if(_uiPointer!=null)_uiPointer.Release();_uiDragGrabbed=false;
        }
        bool UiDragViewerLocalContext()
        {
            if(!UiViewerRequested||!UiClientCanInteract()||_uiPointer==null||!_uiPointer.Ready||_uiPointer.Token<=0)return false;
            var player=MyAPIGateway.Session.Player;if(player.Character.EntityId!=_uiViewerCharacter)return false;
            var display=GetUiDisplay(_uiViewerRequest.CallerId,_uiViewerRequest.TargetId);
            var anchor=MyAPIGateway.Entities.GetEntityById(_uiViewerTile) as IMyTerminalBlock;
            var caller=MyAPIGateway.Entities.GetEntityById(_uiViewerRequest.CallerId) as IMyProgrammableBlock;
            Scene source,tile;if(display==null||display.Revision!=_uiViewerRequest.DefinitionRevision||anchor==null||anchor.Closed||!anchor.IsWorking
                ||caller==null||caller.Closed||!caller.IsWorking||!caller.HasPlayerAccess(player.IdentityId)||!anchor.HasPlayerAccess(player.IdentityId)
                ||Vector3D.DistanceSquared(player.Character.GetPosition(),anchor.GetPosition())>25
                ||!_scenes.TryGetValue(display.TargetId,out source)||!_scenes.TryGetValue(_uiViewerTile,out tile)||_uiViewerScreenId==null&&LocalView(source)!=_uiViewerView)return false;
            var initial=GetUiWidget(display.CallerId,display.TargetId,_uiViewerRequest.ControlId,false);if(initial==null||initial.ScreenId!=_uiViewerScreenId||initial.ScreenId!=null&&UiProjectedGeneration(display,initial)!=_uiViewerSurfaceGeneration)return false;
            var layout=CaptureUiFocusLayout(source,tile);if(layout.Length!=_uiViewerLayout.Length)return false;
            for(int i=0;i<layout.Length;i++)if(layout[i]!=_uiViewerLayout[i])return false;
            bool hidden=_uiViewerGranted?_uiViewerHiddenBundle:UiControlLocalBundleGranted(display,GetUiWidget(display.CallerId,display.TargetId,_uiViewerRequest.ControlId,false));
            int count=0;foreach(var widget in display.Widgets)if(widget.Bundle==_uiViewerBundle&&widget.ScreenId==_uiViewerScreenId&&widget.Control!=null)
            {
                long stamp;count++;if(!_uiViewerSources.TryGetValue(widget.Id,out stamp)||stamp!=widget.Control.SourceRevision)return false;
                if(widget.Id==_uiViewerRequest.ControlId&&!widget.Visible)return false;
                if(widget.Visible&&(!UiControlSourceValid(display,widget,hidden)||!HasVisibleUiWidget(display,widget,hidden)))return false;
            }
            return count==_uiViewerSources.Count;
        }
        bool UiViewerPointerWorld(UiPointerSample pointer,out Vector3D origin,out Vector3D direction)
        {
            origin=direction=Vector3D.Zero;var camera=MyAPIGateway.Session.Camera;if(camera==null)return false;
            return UiPointerRay.Try(pointer.X,pointer.Y,MyAPIGateway.Input.GetMouseAreaSize(),camera.ViewportSize,camera.ViewportOffset,
                camera.ViewMatrix,camera.ProjectionMatrix,camera.Position,camera.WorldMatrix.Forward,out origin,out direction);
        }
        UiWidget UiViewerPick(UiPointerSample pointer,out long tileId)
        {
            tileId=0;Vector3D origin,direction;if(!UiViewerPointerWorld(pointer,out origin,out direction))return null;
            var display=GetUiDisplay(_uiViewerRequest.CallerId,_uiViewerRequest.TargetId);if(display==null)return null;
            UiWidget found=null;double nearest=UiInteractionRange*UiInteractionRange;
            for(int i=display.Widgets.Count-1;i>=0;i--)
            {
                var widget=display.Widgets[i];if(widget.Bundle!=_uiViewerBundle||widget.ScreenId!=_uiViewerScreenId||widget.Control==null)continue;
                bool hidden=UiControlLocalBundleGranted(display,widget);Vector3D hit;long tile;
                if(!HasVisibleUiWidget(display,widget,hidden)||!TryUiControlHit(display,widget,origin,direction,out hit,out tile,hidden))continue;
                double distance=Vector3D.DistanceSquared(origin,hit);if(found!=null&&distance>=nearest)continue;
                var physical=MyAPIGateway.Entities.GetEntityById(tile) as IMyTerminalBlock;
                if(physical==null||!physical.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)
                    ||!UiUnoccluded(_uiViewerCharacter,physical,origin,hit))continue;
                found=widget;nearest=distance;tileId=tile;
            }
            return found;
        }
        bool PrepareUiViewerControl(UiWidget widget,UiNumericValue value,long tileId)
        {
            var display=GetUiDisplay(_uiViewerRequest.CallerId,_uiViewerRequest.TargetId);Scene source,tile;
            if(display==null||value==null||!_scenes.TryGetValue(display.TargetId,out source)||!_scenes.TryGetValue(tileId,out tile))return false;
            var control=UiValueRules.Copy(widget.Control);var numeric=UiValueRules.Copy(value);HDR.Interactions.PathConstraint path;HDR.Interactions.RotationConstraint rotation;
            if(!UiValueRules.Constraint(control,numeric,out path,out rotation))return false;
            _uiDragDisplay=display;_uiDragControlId=widget.Id;_uiDragBundle=widget.Bundle;_uiDragCharacter=_uiViewerCharacter;_uiDragTile=tileId;
            _uiDragPhysicalGrantTile=_uiViewerTile;_uiDragDefinition=display.Revision;_uiDragSourceRevision=control.SourceRevision;
            _uiDragScreenId=widget.ScreenId;_uiDragSurfaceGeneration=widget.ScreenId==null?0:UiProjectedGeneration(display,widget);
            _uiDragControl=control;_uiDragValue=numeric;_uiDragView=LocalView(source);_uiDragLayout=CaptureUiFocusLayout(source,tile);
            _uiDragPath=path;_uiDragRotation=rotation;_uiDragGrabbed=false;_uiDragPredictionBindings.Clear();
            foreach(var coupled in display.Widgets)if(coupled.Control!=null&&coupled.Control.ValueId==numeric.Id&&coupled.Control.ConstraintKind!=null)
            {var c=UiValueRules.Copy(coupled.Control);HDR.Interactions.PathConstraint p;HDR.Interactions.RotationConstraint r;
                if(UiValueRules.Constraint(c,numeric,out p,out r))_uiDragPredictionBindings[c.Artwork]=new UiDragPredictionBinding{Control=c,Path=p,Rotation=r};}
            return true;
        }
        void UiViewerLatchDown(UiWidget widget,long tileId,UiPointerSample pointer)
        {
            _uiViewerDownControl=widget;_uiViewerDownTile=tileId;_uiViewerDownSample=pointer;_uiViewerDownHeld=true;
            _uiViewerDownValue=UiDragNumeric(GetUiDisplay(_uiViewerRequest.CallerId,_uiViewerRequest.TargetId),widget.Control.ValueId);
            if(_uiViewerDownValue!=null)_uiViewerDownValue=UiValueRules.Copy(_uiViewerDownValue);
            if(widget.Control.Draggable&&_uiViewerDownValue!=null&&PrepareUiViewerControl(widget,_uiViewerDownValue,tileId))
            {HDR.Interactions.LocalRay down;if(UiDragPointerRay(pointer,out down))UiDragBeginGrab(down);}
        }
        void UiViewerStartGesture(UiPointerSample current)
        {
            if(!_uiViewerGranted||!_uiViewerDownHeld||_uiViewerDownControl==null||_uiDragLocal.Pending)return;
            var display=GetUiDisplay(_uiViewerRequest.CallerId,_uiViewerRequest.TargetId);
            var widget=display==null?null:GetUiWidget(display.CallerId,display.TargetId,_uiViewerDownControl.Id,false);
            if(widget==null||widget.Control==null||widget.Control.SourceRevision!=_uiViewerDownControl.Control.SourceRevision)
            {_uiViewerDownHeld=false;return;}
            if(!widget.Control.Draggable)
            {SendUiWidgetPress(display,widget,4);_uiViewerDownHeld=false;return;}
            var numeric=UiDragNumeric(display,widget.Control.ValueId);
            if(numeric==null||_uiViewerDownValue==null||numeric.Revision!=_uiViewerDownValue.Revision||!_uiDragGrabbed)
            {_uiViewerDownHeld=false;return;}
            long sequence=NextUiClientSequence();
            var begin=new UiDragRequest{Kind=(int)UiDragKind.Begin,CallerId=display.CallerId,TargetId=display.TargetId,ControlId=widget.Id,
                DefinitionRevision=display.Revision,ValueRevision=numeric.Revision,RequestId=sequence,Sequence=sequence,
                Mode=(int)UiDragMode.FocusedPointer,Value=numeric.Value,ViewerId=_uiViewerId,ScreenId=widget.ScreenId,SurfaceGeneration=_uiDragSurfaceGeneration};
            Vector3D origin,direction;if(widget.ScreenId!=null&&UiViewerPointerWorld(_uiViewerDownSample,out origin,out direction)){begin.OriginX=origin.X;begin.OriginY=origin.Y;begin.OriginZ=origin.Z;begin.DirectionX=direction.X;begin.DirectionY=direction.Y;begin.DirectionZ=direction.Z;}
            if(_uiDragLocal.Begin(begin,_ticks))_uiDragLocal.Pointer(_uiViewerDownSample,_ticks);
        }
        bool TickUiDragViewerClient()
        {
            if(!UiDragViewerLocalContext()||MyAPIGateway.Input.IsNewKeyPressed(VRage.Input.MyKeys.Escape)
                ||_ticks-_uiViewerLastGrant>180||!_uiViewerGranted&&_ticks-_uiViewerStarted>120)
            {CloseUiDragViewerLocal(true);return true;}
            UiPointerSample pointer;if(!_uiPointer.Sample(out pointer)||(pointer.Flags&(4|32|64))!=0)
            {CloseUiDragViewerLocal(true);return true;}
            if(!pointer.Active)return true;
            if(_uiViewerGranted&&_ticks-_uiViewerLastKeep>=60)
            {
                var heartbeat=UiDragWire.Copy(_uiViewerRequest);heartbeat.Kind=(int)UiDragKind.KeepAlive;heartbeat.Sequence=NextUiClientSequence();
                heartbeat.ViewerId=heartbeat.LeaseId=_uiViewerId;_uiViewerLastKeep=_ticks;
                if(_uiViewerSent.Count>=64)_uiViewerSent.Clear();_uiViewerSent.Add(heartbeat.Sequence);SendUiDragRequest(heartbeat);
            }
            if(pointer.Frame<=_uiViewerLastFrame)return true;_uiViewerLastFrame=pointer.Frame;
            if((pointer.Pressed&1)!=0&&!_uiDragLocal.Pending)
            {long tile;var widget=UiViewerPick(pointer,out tile);if(widget!=null)UiViewerLatchDown(widget,tile,pointer);}
            if((pointer.Released&1)!=0||_uiViewerDownHeld&&(pointer.Held&1)==0)_uiViewerDownHeld=false;
            UiViewerStartGesture(pointer);
            if(!_uiDragLocal.Pending)return true;
            _uiDragLocal.Tick(_ticks,UiDragLocalContext());
            if(pointer.Active&&(pointer.Released&1)!=0&&_uiDragLocal.Active&&_uiDragGrabbed)UiDragUpdatePrediction(pointer);
            bool drag=_uiDragLocal.Pointer(pointer,_ticks);
            if(drag&&_uiDragGrabbed)UiDragUpdatePrediction(pointer);
            return true;
        }
        void UiDragUpdatePrediction(UiPointerSample pointer)
        {
            HDR.Interactions.LocalRay ray;if(!UiDragPointerRay(pointer,out ray))return;double proposed;
            if(_uiDragPath!=null){HDR.Interactions.PathDragState next;if(!HDR.Interactions.InteractionMath.TryUpdatePathDrag(_uiDragPath,ray,_uiDragPathState,out next))return;_uiDragPathState=next;proposed=next.Value;}
            else{HDR.Interactions.RotationDragState next;if(!HDR.Interactions.InteractionMath.TryUpdateRotationDrag(_uiDragRotation,ray,_uiDragRotationState,out next))return;_uiDragRotationState=next;proposed=UiValueRules.FromAngle(_uiDragControl,_uiDragValue,next.Value);}
            double value;if(UiValueRules.Range(_uiDragValue).TryNormalize(proposed,out value))_uiDragLocal.Predict(value,_ticks);
        }
    }
}
