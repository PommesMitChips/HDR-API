using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Input;
using VRageMath;
using HDR.Interactions;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        PointerInputBridge _uiPointer;
        UiDragLocalLease _uiDragLocal;
        bool _uiPointerRegistered,_uiDragGrabbed;
        int _uiPointerDiscoverTick=-120;
        int _uiDragInterval=6;
        long _uiDragCharacter,_uiDragTile;
        long _uiDragPhysicalGrantTile;
        long _uiDragDefinition,_uiDragSourceRevision,_uiDragSurfaceGeneration;string _uiDragScreenId;
        UiDisplay _uiDragDisplay;
        string _uiDragControlId,_uiDragBundle;
        UiControlData _uiDragControl;
        UiNumericValue _uiDragValue;
        MatrixD _uiDragView;
        double[] _uiDragLayout;
        PathConstraint _uiDragPath;RotationConstraint _uiDragRotation;
        PathDragState _uiDragPathState;RotationDragState _uiDragRotationState;
        sealed class UiDragPredictionBinding { public UiControlData Control;public PathConstraint Path;public RotationConstraint Rotation; }
        readonly Dictionary<string,UiDragPredictionBinding> _uiDragPredictionBindings=new Dictionary<string,UiDragPredictionBinding>();
        bool UiDragOwnsNativeCursor
        {get{UiPointerSample sample;return _uiDragLocal!=null&&(_uiDragLocal.Pending||UiViewerRequested)&&_uiPointer!=null&&_uiPointer.Ready&&_uiPointer.Token>0
            &&_uiPointer.Sample(out sample)&&(sample.Flags&(4|32|64))==0&&((sample.Flags&1)!=0||sample.Active);}}
        // Drag ACK work is marshalled through TickUiDragNetwork onto the simulation thread.
        // Use the established mod/PB-whitelisted scalar allocator, shared with
        // legacy presses/close and every drag move/terminal.
        long NextUiClientSequence(){return ++_uiSequence;}
        bool TryUiCurrentWidgetHit(UiDisplay display,UiWidget widget,Vector3D origin,Vector3D direction,out Vector3D hit,out long tile)
        {return widget!=null&&widget.Control!=null?TryUiControlHit(display,widget,origin,direction,out hit,out tile,UiControlLocalBundleGranted(display,widget)):TryUiWidgetHit(display,widget,origin,direction,out hit,out tile);}
        bool TryUiCurrentWidgetHit(UiDisplay display,UiWidget widget,Vector3D origin,Vector3D direction,out Vector3D hit)
        {long tile;return TryUiCurrentWidgetHit(display,widget,origin,direction,out hit,out tile);}
        bool UiControlLocalBundleGranted(UiDisplay display,UiWidget widget)
        {if(display!=null&&widget!=null&&_uiViewerGranted&&_uiViewerRequest!=null&&_uiViewerRequest.CallerId==display.CallerId
            &&_uiViewerRequest.TargetId==display.TargetId&&_uiViewerRequest.DefinitionRevision==display.Revision&&widget.Bundle==_uiViewerBundle&&widget.ScreenId==_uiViewerScreenId&&(widget.ScreenId==null||UiProjectedGeneration(display,widget)==_uiViewerSurfaceGeneration))
                return _uiViewerHiddenBundle;
            UiLocalMenu menu;return display!=null&&widget!=null&&_uiLocalMenus.TryGetValue(UiKey(display.CallerId,display.TargetId),out menu)
            &&menu.Revision==display.Revision&&menu.Bundle==widget.Bundle&&menu.ScreenId==widget.ScreenId&&(widget.ScreenId==null||UiProjectedGeneration(display,widget)==menu.SurfaceGeneration)&&_ticks-menu.LastActionTick<=1800;}
        void InitializeUiPointer()
        {
            if(MyAPIGateway.Utilities==null||MyAPIGateway.Utilities.IsDedicated)return;
            if(!_uiPointerRegistered)
            {
                _uiPointer=new PointerInputBridge();_uiDragLocal=new UiDragLocalLease(SendUiDragRequest,ReleaseUiDragPointer,NextUiClientSequence);
                _uiDragLocal.MoveIntervalTicks=_uiDragInterval;
                MyAPIGateway.Utilities.RegisterMessageHandler(PointerInputBridge.Registration,ReceiveUiPointerProvider);_uiPointerRegistered=true;
            }
            if(!_uiPointer.Ready&&_ticks-_uiPointerDiscoverTick>=120)
            {_uiPointerDiscoverTick=_ticks;MyAPIGateway.Utilities.SendModMessage(PointerInputBridge.Discovery,PointerInputBridge.Version);}
        }
        void ReceiveUiPointerProvider(object data)
        {if(_uiPointer!=null){_uiPointer.Receive(data);if(_uiPointer.Ready)PluginComponentRegistered("interactive-pointer");}}
        void ReleaseUiDragPointer(){if(!UiViewerRequested&&_uiPointer!=null)_uiPointer.Release();_uiDragGrabbed=false;}
        void UnregisterUiPointer()
        {
            CancelUiDragLocal();if(_uiDragLocal!=null)_uiDragLocal.Clear();
            try{if(_uiPointerRegistered&&MyAPIGateway.Utilities!=null)MyAPIGateway.Utilities.UnregisterMessageHandler(PointerInputBridge.Registration,ReceiveUiPointerProvider);}catch{}
            if(_uiPointer!=null)_uiPointer.Close();_uiPointer=null;_uiDragLocal=null;_uiPointerRegistered=false;
        }
        void CancelUiDragLocal()
        {CloseUiDragViewerLocal(true);if(_uiDragLocal!=null)_uiDragLocal.Cancel(_ticks);ReleaseUiDragPointer();}
        // Called only inside native USE dispatch after the same physical hotzone
        // was revalidated. Acquisition must precede a delayed server grant.
        bool TryBeginUiControlUse(UiDisplay display,UiWidget widget,long tileId)
        {return UiDragPersistentFocus?TryEnterUiDragViewer(display,widget,tileId):TryBeginUiControlUseSingle(display,widget,tileId);}
        bool TryBeginUiControlUseSingle(UiDisplay display,UiWidget widget,long tileId)
        {
            if(widget==null||widget.Control==null)return false;
            InitializeUiPointer();
            if(!widget.Control.Draggable||widget.Control.ValueId==null||widget.Control.ConstraintKind==null){SendUiWidgetPress(display,widget,3);return true;}
            if(_uiPointer==null||!_uiPointer.Ready){NotifyPluginUnavailable("interactive-pointer");return true;}
            if(_uiDragLocal==null||_uiDragLocal.Pending)return true;
            var character=MyAPIGateway.Session.Player.Character;var numeric=UiDragNumeric(display,widget.Control.ValueId);
            Scene source,tile;if(numeric==null||!_scenes.TryGetValue(display.TargetId,out source)||!_scenes.TryGetValue(tileId,out tile))return true;
            var control=UiValueRules.Copy(widget.Control);var value=UiValueRules.Copy(numeric);
            PathConstraint path;RotationConstraint rotation;if(!UiValueRules.Constraint(control,value,out path,out rotation))return true;
            string key=display.CallerId+"/"+display.TargetId+"/"+widget.Id+"/"+display.Revision;
            if(!_uiPointer.Acquire(character.EntityId,key))return true;
            _uiDragDisplay=display;_uiDragControlId=widget.Id;_uiDragBundle=widget.Bundle;_uiDragCharacter=character.EntityId;_uiDragTile=tileId;
            _uiDragPhysicalGrantTile=tileId;
            _uiDragDefinition=display.Revision;_uiDragSourceRevision=control.SourceRevision;_uiDragControl=control;_uiDragValue=value;
            _uiDragScreenId=widget.ScreenId;_uiDragSurfaceGeneration=widget.ScreenId==null?0:UiProjectedGeneration(display,widget);
            _uiDragView=LocalView(source);_uiDragLayout=CaptureUiFocusLayout(source,tile);_uiDragPath=path;_uiDragRotation=rotation;_uiDragGrabbed=false;
            _uiDragPredictionBindings.Clear();
            foreach(var coupled in display.Widgets)if(coupled.Control!=null&&coupled.Control.ValueId==value.Id&&coupled.Control.ConstraintKind!=null)
            {var c=UiValueRules.Copy(coupled.Control);PathConstraint p;RotationConstraint r;if(UiValueRules.Constraint(c,value,out p,out r))
                _uiDragPredictionBindings[c.Artwork]=new UiDragPredictionBinding{Control=c,Path=p,Rotation=r};}
            long sequence=NextUiClientSequence();
            var begin=new UiDragRequest{Kind=(int)UiDragKind.Begin,CallerId=display.CallerId,TargetId=display.TargetId,ControlId=widget.Id,
                DefinitionRevision=display.Revision,ValueRevision=numeric.Revision,RequestId=sequence,Sequence=sequence,
                Mode=(int)UiDragMode.FocusedPointer,Value=numeric.Value,ScreenId=widget.ScreenId,SurfaceGeneration=_uiDragSurfaceGeneration};
            if(!_uiDragLocal.Begin(begin,_ticks))ReleaseUiDragPointer();return true;
        }
        void ReceiveUiDragAcknowledgement(UiDragAck ack)
        {
            if(_uiDragLocal==null||!UiDragWire.Valid(ack))return;
            // Only this authenticated server ACK raises the shared client seed.
            // The server never accepts a client-supplied reset or lowers its floor.
            if(ack.MinimumSequence>_uiSequence)_uiSequence=ack.MinimumSequence;
            if(ack.Kind==(int)UiDragKind.Focus||ack.Kind==(int)UiDragKind.KeepAlive||ack.Kind==(int)UiDragKind.Blur)
            {ReceiveUiDragViewerAcknowledgement(ack);return;}
            if(!_uiDragLocal.MatchesAcknowledgement(ack))return;
            if(ack.Status==(int)UiDragStatus.Accepted&&!ack.Terminal
                &&(_uiDragDisplay==null||ack.CharacterId!=_uiDragCharacter||ack.TileId!=(_uiDragPhysicalGrantTile==0?_uiDragTile:_uiDragPhysicalGrantTile)))
            {CancelUiDragLocal();return;}
            _uiDragLocal.Receive(ack,_ticks);
        }
        bool UiDragLocalContext()
        {
            if(!UiClientCanInteract()||_uiDragDisplay==null||_uiPointer==null||!_uiPointer.Ready)return false;
            var character=MyAPIGateway.Session.Player.Character;if(character.EntityId!=_uiDragCharacter)return false;
            var current=GetUiDisplay(_uiDragDisplay.CallerId,_uiDragDisplay.TargetId);
            var widget=current==null?null:GetUiWidget(current.CallerId,current.TargetId,_uiDragControlId,false);
            var anchor=MyAPIGateway.Entities.GetEntityById(_uiDragTile) as IMyTerminalBlock;
            var caller=MyAPIGateway.Entities.GetEntityById(_uiDragDisplay.CallerId) as IMyProgrammableBlock;
            Scene source,tile;
            if(current==null||current.Revision!=_uiDragDefinition||widget==null||widget.Control==null
                ||widget.Control.SourceRevision!=_uiDragSourceRevision||!UiControlSourceValid(current,widget,UiControlLocalBundleGranted(current,widget))
                ||widget.ScreenId!=_uiDragScreenId||widget.ScreenId!=null&&UiProjectedGeneration(current,widget)!=_uiDragSurfaceGeneration
                ||!UiWidgetLocallyVisible(current,widget)||anchor==null||anchor.Closed||!anchor.IsWorking||caller==null||caller.Closed
                ||!caller.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)||!anchor.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)
                ||Vector3D.DistanceSquared(character.GetPosition(),anchor.GetPosition())>25
                ||!_scenes.TryGetValue(current.TargetId,out source)||!_scenes.TryGetValue(_uiDragTile,out tile)||_uiDragScreenId==null&&LocalView(source)!=_uiDragView)return false;
            var layout=CaptureUiFocusLayout(source,tile);if(layout.Length!=_uiDragLayout.Length)return false;
            for(int i=0;i<layout.Length;i++)if(layout[i]!=_uiDragLayout[i])return false;
            return true;
        }
        bool TickUiDragClient()
        {
            InitializeUiPointer();if(_uiDragLocal==null)return false;
            if(UiViewerRequested)return TickUiDragViewerClient();
            if(!_uiDragLocal.Pending)return false;
            bool valid=UiDragLocalContext()&&!MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Escape);
            _uiDragLocal.Tick(_ticks,valid);if(!valid||!_uiDragLocal.Pending)return false;
            UiPointerSample pointer;if(!_uiPointer.Sample(out pointer)){CancelUiDragLocal();return true;}
            bool drag=_uiDragLocal.Pointer(pointer,_ticks);
            // Preserve the first applied physical down position under ACK latency.
            // This latches constraint grab state only, never a value or scene pose.
            if(pointer.Active&&(pointer.Pressed&1)!=0&&_uiDragLocal.Held&&!_uiDragGrabbed)
            {LocalRay down;if(UiDragPointerRay(pointer,out down))UiDragBeginGrab(down);}
            if(!drag)return true;
            LocalRay ray;if(!UiDragPointerRay(pointer,out ray))return true;
            if(!_uiDragGrabbed)
            {
                if(!UiDragBeginGrab(ray))return true;
            }
            double proposed;
            if(_uiDragPath!=null){PathDragState next;if(!InteractionMath.TryUpdatePathDrag(_uiDragPath,ray,_uiDragPathState,out next))return true;_uiDragPathState=next;proposed=next.Value;}
            else {RotationDragState next;if(!InteractionMath.TryUpdateRotationDrag(_uiDragRotation,ray,_uiDragRotationState,out next))return true;_uiDragRotationState=next;proposed=UiValueRules.FromAngle(_uiDragControl,_uiDragValue,next.Value);}
            double canonical;if(UiValueRules.Range(_uiDragValue).TryNormalize(proposed,out canonical))_uiDragLocal.Predict(canonical,_ticks);
            return true;
        }
        bool UiDragBeginGrab(LocalRay ray)
        {
            double value=_uiDragLocal.Pending?_uiDragLocal.Confirmed:_uiDragValue.Value;
            bool begin=_uiDragPath!=null?InteractionMath.TryBeginPathDrag(_uiDragPath,ray,value,out _uiDragPathState)
                :InteractionMath.TryBeginRotationDrag(_uiDragRotation,ray,UiValueRules.ToAngle(_uiDragControl,_uiDragValue,value),out _uiDragRotationState);
            if(begin)_uiDragGrabbed=true;return begin;
        }
        bool UiDragPointerRay(UiPointerSample pointer,out LocalRay local)
        {
            local=default(LocalRay);var camera=MyAPIGateway.Session.Camera;if(camera==null)return false;
            Vector3D origin,direction;
            if(!UiPointerRay.Try(pointer.X,pointer.Y,MyAPIGateway.Input.GetMouseAreaSize(),camera.ViewportSize,camera.ViewportOffset,
                camera.ViewMatrix,camera.ProjectionMatrix,camera.Position,camera.WorldMatrix.Forward,out origin,out direction))return false;
            return UiDragLocalRay(_uiDragDisplay,_uiDragControl,_uiDragTile,origin,direction,out local);
        }
        bool UiDragLocalRay(UiDisplay display,UiControlData control,long tileId,Vector3D origin,Vector3D direction,out LocalRay local)
        {
            local=default(LocalRay);if(display==null||control==null)return false;
            var projected=display.Widgets.Find(w=>w.Control!=null&&w.Control.Artwork==control.Artwork);if(projected!=null&&projected.ScreenId!=null)return UiProjectedLocalRay(display,control,tileId,origin,direction,out local);
            MatrixD mapping;if(!UiDragWorldMapping(display,tileId,out mapping))return false;
            var tile=MyAPIGateway.Entities.GetEntityById(tileId) as IMyTerminalBlock;
            var panel=tile as IMyTextPanel;MatrixD inverse;
            if(panel==null)
            {inverse=MatrixD.Invert(UiValueRules.Matrix(control.ReferencePose)*mapping);return LocalRay.TryFromDirection(Vector3D.Transform(origin,inverse),Vector3D.TransformNormal(direction,inverse),out local);}
            // LCD rendering is a flat projection. Recover the canvas point from
            // the physical panel plane, then use the same orthographic source ray.
            Vector3D center,right,up,hit;if(!UiLcdPlane(panel,out center,out right,out up)
                ||!UiHitGeometry.Quad(origin,direction,center,right,up,UiInteractionRange,out hit))return false;
            var delta=hit-center;double xx=right.LengthSquared(),yy=up.LengthSquared(),xy=Vector3D.Dot(right,up),det=xx*yy-xy*xy;if(det<1e-16)return false;
            double u=(Vector3D.Dot(delta,right)*yy-Vector3D.Dot(delta,up)*xy)/det;
            double v=(Vector3D.Dot(delta,up)*xx-Vector3D.Dot(delta,right)*xy)/det;
            Scene source,screen;if(!_scenes.TryGetValue(display.TargetId,out source)||!_scenes.TryGetValue(tileId,out screen))return false;
            double x=((u+1)*.5+screen.LcdColumn)/screen.LcdColumns*screen.LcdWidth-screen.LcdWidth*.5;
            double y=screen.LcdHeight*.5-((1-v)*.5+screen.LcdRow)/screen.LcdRows*screen.LcdHeight;
            inverse=MatrixD.Invert(UiValueRules.Matrix(control.ReferencePose)*LocalView(source));
            return LocalRay.TryFromDirection(Vector3D.Transform(new Vector3D(x,y,1000),inverse),Vector3D.TransformNormal(Vector3D.Forward,inverse),out local);
        }
        void ApplyUiDragPrediction(Scene scene,Item rendered)
        {
            if(_uiDragLocal==null||!_uiDragLocal.Active||!_uiDragGrabbed||_uiDragDisplay==null||scene.ConsoleId!=_uiDragDisplay.TargetId
                ||rendered.CallerId!=_uiDragDisplay.CallerId)return;
            UiDragPredictionBinding binding;if(!_uiDragPredictionBindings.TryGetValue(rendered.Id,out binding))return;
            var control=binding.Control;
            MatrixD delta;
            bool valid=binding.Path!=null?binding.Path.TryGetTranslationDelta(control.ReferenceValue,_uiDragLocal.Prediction,out delta)
                :binding.Rotation.TryGetRotationDelta(UiValueRules.ToAngle(control,_uiDragValue,control.ReferenceValue),UiValueRules.ToAngle(control,_uiDragValue,_uiDragLocal.Prediction),out delta);
            if(!valid)return;var transform=delta*UiValueRules.Matrix(control.ReferencePose);
            try{FitsDisplay(rendered.Geometry,transform,scene);rendered.Transform=transform;}catch(ArgumentException){}
        }
        bool UiFocusControlContains(UiDisplay display,UiWidget widget,double x,double y)
        {
            if(widget.Control==null)return UiRules.Contains(widget,x,y);
            Scene scene;Item item;if(!UiControlSourceValid(display,widget,UiControlLocalBundleGranted(display,widget))||!_scenes.TryGetValue(display.TargetId,out scene)
                ||!scene.Items.TryGetValue(Key(display.CallerId,widget.Control.Artwork),out item))return false;
            Vector3D hit;return UiHitGeometry.Quad(new Vector3D(x,y,1000),Vector3D.Forward,
                Vector3D.Transform(new Vector3D(widget.X,widget.Y,0),item.Transform),
                Vector3D.TransformNormal(new Vector3D(widget.Width*.5,0,0),item.Transform),
                Vector3D.TransformNormal(new Vector3D(0,widget.Height*.5,0),item.Transform),1e6,out hit);
        }
        void SetUiDragRate(double hz)
        {
            if(!Geometry.Finite(hz)||hz<1||hz>30)throw new ArgumentException("UI drag rate must be 1–30 Hz.");
            int interval=Math.Max(2,(int)Math.Ceiling(60/hz));_uiDragInterval=interval;InitializeUiDragNetwork();_uiDrags.MoveIntervalTicks=interval;
            if(_uiDragLocal!=null)_uiDragLocal.MoveIntervalTicks=interval;
        }
    }
}
