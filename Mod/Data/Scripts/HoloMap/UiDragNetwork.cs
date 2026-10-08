using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRageMath;
using HDR.Interactions;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        sealed class UiDragServerContext
        {
            public long Identity, InnerLease, SourceRevision, TileSource, ViewerId;
            public string Bundle,ScreenId;public long SurfaceGeneration;
            public Vector3D EntryLocal;
            public MatrixD View;
            public double[] Layout;
            public UiControlData Control;
            public UiNumericValue Numeric;
            public bool RequiresGrant;
            public bool RequiresFocusGrant;
            public PathConstraint Path; public RotationConstraint Rotation;
            public PathDragState PathState; public RotationDragState RotationState;
        }
        UiDragTransactions _uiDrags;
        readonly object _uiDragAckGate=new object();
        readonly Queue<UiDragAck> _uiDragAcks=new Queue<UiDragAck>();
        readonly Dictionary<long,UiDragAck> _uiDragTerminalAcks=new Dictionary<long,UiDragAck>();
        void InitializeUiDragNetwork()
        {
            if(_uiDrags==null){_uiDrags=new UiDragTransactions(BeginUiDragAuthority,ValidateUiDragAuthority,
                CommitUiDragAuthority,UiDragWorldValue,SendUiDragAcknowledgement,ReleaseUiDragAuthority,ProcessUiDragViewerRequest);_uiDrags.MoveIntervalTicks=_uiDragInterval;_uiDrags.PointerValue=UiDragProjectedValue;}
        }
        void ReceiveUiDragMessage(byte[] bytes,ulong sender,bool fromServer)
        {
            if(bytes==null||bytes.Length>UiDragWire.PacketLimit||!UiDragWire.IsDrag(bytes))return;
            try
            {
                if(MyAPIGateway.Multiplayer.IsServer)
                {
                    if(fromServer||sender==0)return;
                    var body=UiDragWire.Body(bytes);if(body==null)return;
                    var request=MyAPIGateway.Utilities.SerializeFromBinary<UiDragRequest>(body);
                    if(!UiDragWire.Valid(request))return;
                    InitializeUiDragNetwork();_uiDrags.Submit(sender,request,_ticks);
                }
                else
                {
                    if(!fromServer||sender!=0&&sender!=MyAPIGateway.Multiplayer.ServerId)return;
                    var body=UiDragWire.Body(bytes);if(body==null)return;
                    QueueUiDragAcknowledgement(MyAPIGateway.Utilities.SerializeFromBinary<UiDragAck>(body));
                }
            }
            catch { }
        }
        void SendUiDragRequest(UiDragRequest request)
        {
            if(request.ScreenId!=null&&request.Mode==(int)UiDragMode.FocusedPointer&&request.Kind!=(int)UiDragKind.Focus&&request.Kind!=(int)UiDragKind.Blur&&request.Kind!=(int)UiDragKind.KeepAlive&&request.Kind!=(int)UiDragKind.Cancel&&!(request.Kind==(int)UiDragKind.Begin&&new Vector3D(request.DirectionX,request.DirectionY,request.DirectionZ).LengthSquared()>.5))
            {
                UiPointerSample pointer;Vector3D origin,direction;if(_uiPointer==null||!_uiPointer.Sample(out pointer)||!UiViewerPointerWorld(pointer,out origin,out direction))return;
                request.OriginX=origin.X;request.OriginY=origin.Y;request.OriginZ=origin.Z;request.DirectionX=direction.X;request.DirectionY=direction.Y;request.DirectionZ=direction.Z;
            }
            if(!UiDragWire.Valid(request)||MyAPIGateway.Multiplayer==null||MyAPIGateway.Utilities==null)return;
            if(MyAPIGateway.Multiplayer.IsServer)
            {var player=MyAPIGateway.Session.LocalHumanPlayer;if(player!=null){InitializeUiDragNetwork();_uiDrags.Submit(player.SteamUserId,request,_ticks);}}
            else {var data=UiDragWire.Wrap(MyAPIGateway.Utilities.SerializeToBinary(request));if(data!=null)MyAPIGateway.Multiplayer.SendMessageToServer(UiNetworkChannel,data,true);}
        }
        void SendUiDragAcknowledgement(ulong sender,UiDragAck ack)
        {
            UiPeer legacyPeer;
            if(_uiPeers.TryGetValue(sender,out legacyPeer)&&legacyPeer.Sequence>ack.MinimumSequence)ack.MinimumSequence=legacyPeer.Sequence;
            if(ack.Sequence>ack.MinimumSequence)ack.MinimumSequence=ack.Sequence;
            var local=MyAPIGateway.Session.LocalHumanPlayer;
            if(local!=null&&local.SteamUserId==sender){ReceiveUiDragAcknowledgement(ack);return;}
            var bytes=UiDragWire.Wrap(MyAPIGateway.Utilities.SerializeToBinary(ack));
            if(bytes!=null)MyAPIGateway.Multiplayer.SendMessageTo(UiNetworkChannel,bytes,sender,true);
        }
        void TickUiDragNetwork()
        {
            lock(_uiDragAckGate)
            {while(_uiDragAcks.Count>0)ReceiveUiDragAcknowledgement(_uiDragAcks.Dequeue());
                var terminal=new List<UiDragAck>(_uiDragTerminalAcks.Values);_uiDragTerminalAcks.Clear();
                terminal.Sort((a,b)=>a.Sequence.CompareTo(b.Sequence));foreach(var ack in terminal)ReceiveUiDragAcknowledgement(ack);}
            // Reserved viewer terminals release their original scope even after
            // context loss. Gesture preflights and lease validation still reject
            // invalid context before any value commit in this transaction tick.
            if(MyAPIGateway.Multiplayer.IsServer){if(_uiDrags!=null)_uiDrags.Tick(_ticks);TickUiDragViewers();}
        }
        void QueueUiDragAcknowledgement(UiDragAck ack)
        {
            if(!UiDragWire.Valid(ack))return;lock(_uiDragAckGate)
            {
                if(ack.Terminal)
                {
                    if(_uiDragTerminalAcks.Count>=32&&!_uiDragTerminalAcks.ContainsKey(ack.RequestId))
                    {long oldest=long.MaxValue;foreach(long id in _uiDragTerminalAcks.Keys)if(id<oldest)oldest=id;
                        if(ack.RequestId<=oldest)return;_uiDragTerminalAcks.Remove(oldest);}
                    UiDragAck prior;if(!_uiDragTerminalAcks.TryGetValue(ack.RequestId,out prior)||ack.Sequence>=prior.Sequence)_uiDragTerminalAcks[ack.RequestId]=ack;
                }
                else if(_uiDragAcks.Count<64)_uiDragAcks.Enqueue(ack);
            }
        }
        void ClearUiDragNetwork()
        {if(_uiDrags!=null)_uiDrags.Clear();_uiDrags=null;ClearUiDragViewers();lock(_uiDragAckGate){_uiDragAcks.Clear();_uiDragTerminalAcks.Clear();}}
        IMyPlayer UiDragPlayer(ulong sender)
        {
            var players=new List<IMyPlayer>();MyAPIGateway.Players.GetPlayers(players,p=>p.SteamUserId==sender);
            if(players.Count!=1)return null;var player=players[0];var character=player.Character;
            if(player.IdentityId==0||character==null||character.Closed||character.IsDead||player.Controller==null
                ||!ReferenceEquals(player.Controller.ControlledEntity,character)
                ||!ReferenceEquals(MyAPIGateway.Entities.GetEntityById(character.EntityId),character))return null;
            return player;
        }
        static UiNumericValue UiDragNumeric(UiDisplay display,string valueId)
        {if(display!=null&&display.Values!=null)foreach(var value in display.Values)if(value.Id==valueId)return value;return null;}
        bool UiDragBundleGrant(UiPeer peer,IMyPlayer player,UiDisplay display,UiWidget widget)
        {
            return peer!=null&&player!=null&&display!=null&&widget!=null&&player.Character!=null
                &&peer.OpenCaller==display.CallerId&&peer.OpenTarget==display.TargetId&&peer.OpenRevision==display.Revision
                &&peer.OpenBundle==widget.Bundle&&peer.OpenCharacter==player.Character.EntityId&&_ticks<=peer.OpenExpires
                &&peer.OpenScreenId==widget.ScreenId&&(widget.ScreenId==null||UiProjectedGeneration(display,widget)==peer.OpenSurfaceGeneration)
                &&(!peer.IsFocus||UiFocusGrantContext(peer,player,display));
        }
        UiDragBinding BeginUiDragAuthority(ulong sender,UiDragRequest request)
        {
            UiDragViewerGrant viewer=null;
            if(request.ViewerId>0&&!UiDragViewerPreflight(sender,request.ViewerId,null,out viewer))return null;
            var player=UiDragPlayer(sender);if(player==null)return null;
            var caller=MyAPIGateway.Entities.GetEntityById(request.CallerId) as IMyProgrammableBlock;
            var anchor=MyAPIGateway.Entities.GetEntityById(request.TargetId) as IMyTerminalBlock;
            if(caller==null||anchor==null||caller.Closed||anchor.Closed||!caller.IsWorking||!anchor.IsWorking)return null;
            Authorize(caller,anchor);if(!caller.HasPlayerAccess(player.IdentityId)||!anchor.HasPlayerAccess(player.IdentityId))return null;
            var display=GetUiDisplay(request.CallerId,request.TargetId);
            var widget=GetUiWidget(request.CallerId,request.TargetId,request.ControlId,false);
            if(display==null||display.Revision!=request.DefinitionRevision||widget==null||widget.Control==null
                ||!widget.Control.Draggable||widget.Control.ValueId==null||widget.Control.ConstraintKind==null)return null;
            if(widget.ScreenId!=request.ScreenId||widget.ScreenId!=null&&UiProjectedGeneration(display,widget)!=request.SurfaceGeneration)return null;
            var numeric=UiDragNumeric(display,widget.Control.ValueId);
            if(numeric==null||numeric.Revision!=request.ValueRevision)return null;
            var head=player.Character.GetHeadMatrix(true,true,true,true);Vector3D hit;long tileId;
            bool opened=false;UiPeer peer;
            if(_uiPeers.TryGetValue(sender,out peer))opened=UiDragBundleGrant(peer,player,display,widget);
            if(request.ViewerId>0)
            {
                if(request.Mode!=(int)UiDragMode.FocusedPointer||!UiDragViewerFor(sender,request.ViewerId,display,widget,player,out viewer))return null;
                opened=viewer.HiddenBundle;tileId=viewer.TileId;
                var viewerTile=MyAPIGateway.Entities.GetEntityById(tileId) as IMyTerminalBlock;if(viewerTile==null)return null;
                hit=Vector3D.Transform(viewer.EntryLocal,viewerTile.WorldMatrix);
            }
            else {hit=Vector3D.Zero;tileId=0;}
            if(!HasVisibleUiWidget(display,widget,opened)||!UiControlSourceValid(display,widget,opened))return null;
            if(viewer==null)
            {
                if(!TryUiControlHit(display,widget,head.Translation,head.Forward,out hit,out tileId,opened))return null;
            }
            if(widget.ScreenId!=null&&request.Mode==(int)UiDragMode.FocusedPointer)
            {LocalRay pointerRay;if(!UiProjectedRequestRay(player,display,widget,request,out pointerRay)||!TryUiProjectedHit(display,widget,new Vector3D(request.OriginX,request.OriginY,request.OriginZ),new Vector3D(request.DirectionX,request.DirectionY,request.DirectionZ),out hit,out tileId))return null;}
            var tile=MyAPIGateway.Entities.GetEntityById(tileId) as IMyTerminalBlock;
            if(tile==null||tile.Closed||!tile.IsWorking||!tile.HasPlayerAccess(player.IdentityId)
                ||Vector3D.DistanceSquared(player.Character.GetPosition(),tile.GetPosition())>25
                ||Vector3D.DistanceSquared(head.Translation,hit)>36||!UiUnoccluded(player.Character.EntityId,tile,head.Translation,hit))return null;
            Authorize(caller,tile);Scene source,tileScene;
            if(!_scenes.TryGetValue(display.TargetId,out source)||!_scenes.TryGetValue(tileId,out tileScene))return null;
            long innerLease;
            if(!UiTryBeginValueLeaseScalar(request.CallerId,request.TargetId,request.ControlId,player.IdentityId,
                request.DefinitionRevision,request.ValueRevision,widget.Control.SourceRevision,opened,out innerLease))return null;
            var context=new UiDragServerContext{Identity=player.IdentityId,InnerLease=innerLease,SourceRevision=widget.Control.SourceRevision,
                Bundle=widget.Bundle,TileSource=tileScene.LcdSourceId,ViewerId=viewer==null?0:viewer.Id,
                EntryLocal=viewer==null?Vector3D.Transform(hit,MatrixD.Invert(tile.WorldMatrix)):viewer.EntryLocal,
                View=viewer==null?LocalView(source):viewer.View,Layout=viewer==null?CaptureUiFocusLayout(source,tileScene):viewer.Layout,
                Control=UiValueRules.Copy(widget.Control),Numeric=UiValueRules.Copy(numeric),
                RequiresGrant=viewer==null&&opened,RequiresFocusGrant=viewer==null&&opened&&peer.IsFocus};
            context.ScreenId=widget.ScreenId;context.SurfaceGeneration=widget.ScreenId==null?0:UiProjectedGeneration(display,widget);
            var binding=new UiDragBinding{CallerId=request.CallerId,TargetId=request.TargetId,CharacterId=player.Character.EntityId,TileId=tileId,
                ControlId=request.ControlId,ValueId=numeric.Id,DefinitionRevision=display.Revision,ValueRevision=numeric.Revision,Mode=request.Mode,Value=numeric.Value,
                ViewerId=viewer==null?0:viewer.Id,Context=context};
            binding.ScreenId=context.ScreenId;binding.SurfaceGeneration=context.SurfaceGeneration;
            if(request.Mode==(int)UiDragMode.WorldLook||widget.ScreenId!=null)
            {
                LocalRay ray;
                if(!UiValueRules.Constraint(context.Control,context.Numeric,out context.Path,out context.Rotation)
                    ||!(widget.ScreenId!=null&&request.Mode==(int)UiDragMode.FocusedPointer?UiProjectedRequestRay(player,display,widget,request,out ray):UiDragLocalRay(display,context.Control,tileId,head.Translation,head.Forward,out ray))
                    ||context.Path!=null&&!InteractionMath.TryBeginPathDrag(context.Path,ray,numeric.Value,out context.PathState)
                    ||context.Rotation!=null&&!InteractionMath.TryBeginRotationDrag(context.Rotation,ray,UiValueRules.ToAngle(context.Control,numeric,numeric.Value),out context.RotationState))
                {UiEndValueLease(innerLease,player.IdentityId,false);return null;}
            }
            return binding;
        }
        bool ValidateUiDragAuthority(ulong sender,UiDragBinding binding)
        {
            var context=binding.Context as UiDragServerContext;if(context==null)return false;
            var player=UiDragPlayer(sender);if(player==null||player.IdentityId!=context.Identity||player.Character.EntityId!=binding.CharacterId)return false;
            var caller=MyAPIGateway.Entities.GetEntityById(binding.CallerId) as IMyProgrammableBlock;
            var anchor=MyAPIGateway.Entities.GetEntityById(binding.TargetId) as IMyTerminalBlock;
            var tile=MyAPIGateway.Entities.GetEntityById(binding.TileId) as IMyTerminalBlock;
            if(caller==null||anchor==null||tile==null||caller.Closed||anchor.Closed||tile.Closed||!caller.IsWorking||!anchor.IsWorking||!tile.IsWorking)return false;
            Authorize(caller,anchor);Authorize(caller,tile);
            if(!caller.HasPlayerAccess(context.Identity)||!anchor.HasPlayerAccess(context.Identity)||!tile.HasPlayerAccess(context.Identity)
                ||Vector3D.DistanceSquared(player.Character.GetPosition(),tile.GetPosition())>25)return false;
            var display=GetUiDisplay(binding.CallerId,binding.TargetId);var widget=GetUiWidget(binding.CallerId,binding.TargetId,binding.ControlId,false);
            var numeric=UiDragNumeric(display,binding.ValueId);
            bool opened=false;UiPeer peer;if(_uiPeers.TryGetValue(sender,out peer)&&peer.OpenBundle==context.Bundle)
                opened=UiDragBundleGrant(peer,player,display,widget);
            if(context.ViewerId>0)
            {
                UiDragViewerGrant viewer;
                if(binding.ViewerId!=context.ViewerId||!_uiDragViewers.TryGetValue(sender,out viewer)||viewer.Id!=context.ViewerId
                    ||!ValidateUiDragViewer(viewer,player)||viewer.Bundle!=context.Bundle||viewer.CallerId!=binding.CallerId
                    ||viewer.TargetId!=binding.TargetId||viewer.DefinitionRevision!=binding.DefinitionRevision||viewer.TileId!=binding.TileId)return false;
                opened=viewer.HiddenBundle;
            }
            else if(binding.ViewerId!=0)return false;
            if(context.RequiresGrant&&(!opened||context.RequiresFocusGrant&&!peer.IsFocus))return false;
            if(display==null||display.Revision!=binding.DefinitionRevision||widget==null||widget.Bundle!=context.Bundle||widget.Control==null
                ||widget.Control.SourceRevision!=context.SourceRevision||numeric==null||numeric.Revision!=binding.ValueRevision
                ||!UiControlSourceValid(display,widget,opened)||!UiValidateValueLease(context.InnerLease,context.Identity))return false;
            // Source declaration, logical view and tile layout are frozen; the owning
            // block may move in the world without changing the object-local grab.
            if(widget.ScreenId!=context.ScreenId||widget.ScreenId!=null&&UiProjectedGeneration(display,widget)!=context.SurfaceGeneration)return false;
            Scene source,tileScene;if(!_scenes.TryGetValue(binding.TargetId,out source)||!_scenes.TryGetValue(binding.TileId,out tileScene)
                ||tileScene.LcdSourceId!=context.TileSource||context.ScreenId==null&&LocalView(source)!=context.View)return false;
            var layout=CaptureUiFocusLayout(source,tileScene);if(layout.Length!=context.Layout.Length)return false;
            for(int i=0;i<layout.Length;i++)if(layout[i]!=context.Layout[i])return false;
            if(!HasVisibleUiWidget(display,widget,opened))return false;
            var head=player.Character.GetHeadMatrix(true,true,true,true);var entry=Vector3D.Transform(context.EntryLocal,tile.WorldMatrix);var toward=entry-head.Translation;
            return toward.LengthSquared()>=1e-8&&toward.LengthSquared()<=36&&Vector3D.Dot(Vector3D.Normalize(toward),head.Forward)>=.25
                &&UiUnoccluded(player.Character.EntityId,tile,head.Translation,entry);
        }
        UiDragCommit CommitUiDragAuthority(UiDragBinding binding,double value)
        {
            var context=(UiDragServerContext)binding.Context;MyTuple<bool,double,long> result;
            if(!UiTryCommitValueLeaseScalar(context.InnerLease,context.Identity,value,out result)||!result.Item1)
                return new UiDragCommit{Status=UiDragStatus.Stale};
            return new UiDragCommit{Accepted=true,Value=result.Item2,ValueRevision=result.Item3};
        }
        double? UiDragWorldValue(UiDragBinding binding)
        {
            var context=(UiDragServerContext)binding.Context;
            var character=MyAPIGateway.Entities.GetEntityById(binding.CharacterId) as IMyCharacter;if(character==null)return null;
            var head=character.GetHeadMatrix(true,true,true,true);var display=GetUiDisplay(binding.CallerId,binding.TargetId);LocalRay ray;
            if(!UiDragLocalRay(display,context.Control,binding.TileId,head.Translation,head.Forward,out ray))return null;
            if(context.Path!=null){PathDragState next;if(!InteractionMath.TryUpdatePathDrag(context.Path,ray,context.PathState,out next))return null;context.PathState=next;return next.Value;}
            RotationDragState rotate;if(!InteractionMath.TryUpdateRotationDrag(context.Rotation,ray,context.RotationState,out rotate))return null;
            context.RotationState=rotate;double canonical;return UiValueRules.Range(context.Numeric).TryNormalize(UiValueRules.FromAngle(context.Control,context.Numeric,rotate.Value),out canonical)?(double?)canonical:null;
        }
        void ReleaseUiDragAuthority(UiDragBinding binding,bool commit)
        {var context=binding.Context as UiDragServerContext;if(context!=null)UiEndValueLease(context.InnerLease,context.Identity,commit);}
    }
}
