using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        // A viewer owns no numeric value lock. Its initial physical proof permits
        // later gestures in this bundle while the fixed entry context survives.
        sealed class UiDragViewerGrant
        {
            public ulong Sender;
            public long Id, CallerId, TargetId, DefinitionRevision, SourceRevision;
            public long CharacterId, Identity, TileId, TileSource, RequestId, Sequence;
            public string ControlId, Bundle;
            public Vector3D EntryLocal;
            public MatrixD View;
            public double[] Layout;
            public object Controller, Character, Caller, Target, Tile;
            public bool HiddenBundle;
            public int Started, LastSeen;
            public readonly Dictionary<string,long> Sources=new Dictionary<string,long>();
        }
        const int UiDragViewerCapacity=32, UiDragViewerIdleTicks=1800, UiDragViewerLifetimeTicks=36000;
        readonly Dictionary<ulong,UiDragViewerGrant> _uiDragViewers=new Dictionary<ulong,UiDragViewerGrant>();
        long _uiDragViewerSequence=DateTime.UtcNow.Ticks;

        static long UiDragViewerStamp(UiDisplay display,UiWidget widget)
        {
            return widget==null||widget.Control==null?0:widget.Control.SourceRevision;
        }
        UiDragAck UiDragViewerAcknowledgement(UiDragRequest request,UiDragViewerGrant grant,UiDragStatus status,bool terminal)
        {
            var ack=new UiDragAck{Kind=request.Kind,CallerId=request.CallerId,TargetId=request.TargetId,ControlId=request.ControlId,
                DefinitionRevision=request.DefinitionRevision,ValueRevision=request.ValueRevision,RequestId=request.RequestId,
                Sequence=request.Sequence,Status=(int)status,Terminal=terminal,ViewerId=request.ViewerId,LeaseId=request.ViewerId};
            if(grant==null)return ack;
            ack.CallerId=grant.CallerId;ack.TargetId=grant.TargetId;ack.ControlId=grant.ControlId;ack.DefinitionRevision=grant.DefinitionRevision;
            ack.RequestId=grant.RequestId;ack.ViewerId=grant.Id;ack.LeaseId=grant.Id;ack.TileId=grant.TileId;ack.CharacterId=grant.CharacterId;
            ack.ValueRevision=grant.SourceRevision;
            return ack;
        }
        UiDragAck ProcessUiDragViewerRequest(ulong sender,UiDragRequest request)
        {
            if(request==null||sender==0||MyAPIGateway.Multiplayer==null||!MyAPIGateway.Multiplayer.IsServer)
                return request==null?null:UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);
            try
            {
                if(request.Kind==(int)UiDragKind.Blur)
                {
                    UiDragViewerGrant closing;
                    if(!_uiDragViewers.TryGetValue(sender,out closing)||request.ViewerId!=closing.Id||request.LeaseId!=closing.Id
                        ||request.RequestId!=closing.RequestId||request.CallerId!=closing.CallerId||request.TargetId!=closing.TargetId
                        ||request.ControlId!=closing.ControlId||request.DefinitionRevision!=closing.DefinitionRevision
                        ||request.ValueRevision!=closing.SourceRevision||request.Mode!=(int)UiDragMode.FocusedPointer
                        ||request.Sequence<=closing.Sequence)
                    {
                        var rejected=UiDragViewerAcknowledgement(request,null,UiDragStatus.Invalid,true);
                        // A stale nonce must not retire the current viewer's core
                        // owner map merely because the peer supplied its ID.
                        rejected.ViewerId=0;rejected.LeaseId=0;return rejected;
                    }
                    // Scope identity, rather than current visibility/access,
                    // authorizes release after the player has lost context.
                    closing.Sequence=request.Sequence;
                    var closed=UiDragViewerAcknowledgement(request,closing,UiDragStatus.Cancelled,true);
                    CloseUiDragViewer(sender);return closed;
                }
                if(request.Kind==(int)UiDragKind.KeepAlive)
                {
                    UiDragViewerGrant existing;
                    if(!_uiDragViewers.TryGetValue(sender,out existing)||request.ViewerId!=existing.Id)
                        return UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);
                    if(request.LeaseId!=existing.Id||request.RequestId!=existing.RequestId||request.CallerId!=existing.CallerId||request.TargetId!=existing.TargetId
                        ||request.ControlId!=existing.ControlId||request.DefinitionRevision!=existing.DefinitionRevision
                        ||request.ValueRevision!=existing.SourceRevision
                        ||request.Mode!=(int)UiDragMode.FocusedPointer||request.Sequence<=existing.Sequence)
                    {
                        var rejected=UiDragViewerAcknowledgement(request,null,UiDragStatus.Invalid,true);
                        rejected.ViewerId=0;rejected.LeaseId=0;return rejected;
                    }
                    if(!ValidateUiDragViewer(existing,null))
                    {CloseUiDragViewer(sender);return UiDragViewerAcknowledgement(request,existing,UiDragStatus.ContextLost,true);}
                    existing.Sequence=request.Sequence;existing.LastSeen=_ticks;
                    return UiDragViewerAcknowledgement(request,existing,UiDragStatus.Accepted,false);
                }
                if(request.Kind!=(int)UiDragKind.Focus||request.Mode!=(int)UiDragMode.FocusedPointer||request.ViewerId!=0||request.LeaseId!=0)
                    return UiDragViewerAcknowledgement(request,null,UiDragStatus.Invalid,true);
                if(_uiDragViewers.Count>=UiDragViewerCapacity&&!_uiDragViewers.ContainsKey(sender)||_uiDragViewerSequence==long.MaxValue)
                    return UiDragViewerAcknowledgement(request,null,UiDragStatus.Busy,true);
                var player=UiDragPlayer(sender);if(player==null)return UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);
                var caller=MyAPIGateway.Entities.GetEntityById(request.CallerId) as IMyProgrammableBlock;
                var anchor=MyAPIGateway.Entities.GetEntityById(request.TargetId) as IMyTerminalBlock;
                if(caller==null||anchor==null||caller.Closed||anchor.Closed||!caller.IsWorking||!anchor.IsWorking)
                    return UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);
                Authorize(caller,anchor);
                if(!caller.HasPlayerAccess(player.IdentityId)||!anchor.HasPlayerAccess(player.IdentityId))
                    return UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);
                var display=GetUiDisplay(request.CallerId,request.TargetId);var widget=GetUiWidget(request.CallerId,request.TargetId,request.ControlId,false);
                if(display==null||display.Revision!=request.DefinitionRevision||widget==null||widget.Control==null
                    ||UiDragViewerStamp(display,widget)!=request.ValueRevision)
                    return UiDragViewerAcknowledgement(request,null,UiDragStatus.Stale,true);
                bool opened=false;UiPeer peer;if(_uiPeers.TryGetValue(sender,out peer))opened=UiDragBundleGrant(peer,player,display,widget);
                if(!HasVisibleUiWidget(display,widget,opened)||!UiControlSourceValid(display,widget,opened))
                    return UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);
                var head=player.Character.GetHeadMatrix(true,true,true,true);Vector3D hit;long tileId;
                // Native Use is a client-only gate. This grant independently proves
                // the player's exact head ray against the initial authored hotzone.
                if(!TryUiControlHit(display,widget,head.Translation,head.Forward,out hit,out tileId,opened))
                    return UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);
                var tile=MyAPIGateway.Entities.GetEntityById(tileId) as IMyTerminalBlock;
                if(tile==null||tile.Closed||!tile.IsWorking||!tile.HasPlayerAccess(player.IdentityId)
                    ||Vector3D.DistanceSquared(player.Character.GetPosition(),tile.GetPosition())>25
                    ||Vector3D.DistanceSquared(head.Translation,hit)>36||!UiUnoccluded(player.Character.EntityId,tile,head.Translation,hit))
                    return UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);
                Authorize(caller,tile);Scene source,tileScene;
                if(!_scenes.TryGetValue(display.TargetId,out source)||!_scenes.TryGetValue(tileId,out tileScene))
                    return UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);
                var grant=new UiDragViewerGrant{Sender=sender,Id=++_uiDragViewerSequence,CallerId=display.CallerId,TargetId=display.TargetId,
                    DefinitionRevision=display.Revision,SourceRevision=widget.Control.SourceRevision,ControlId=widget.Id,Bundle=widget.Bundle,
                    CharacterId=player.Character.EntityId,Identity=player.IdentityId,Controller=player.Controller,Character=player.Character,
                    Caller=caller,Target=anchor,Tile=tile,TileId=tileId,TileSource=tileScene.LcdSourceId,RequestId=request.RequestId,Sequence=request.Sequence,
                    EntryLocal=Vector3D.Transform(hit,MatrixD.Invert(tile.WorldMatrix)),View=LocalView(source),Layout=CaptureUiFocusLayout(source,tileScene),
                    HiddenBundle=opened,Started=_ticks,LastSeen=_ticks};
                foreach(var control in display.Widgets)if(control.Bundle==grant.Bundle&&control.Control!=null)
                {
                    if(control.Visible&&!UiControlSourceValid(display,control,opened))return UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);
                    grant.Sources.Add(control.Id,control.Control.SourceRevision);
                }
                if(!ValidateUiDragViewer(grant,player))return UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);
                CloseUiDragViewer(sender);_uiDragViewers[sender]=grant;
                return UiDragViewerAcknowledgement(request,grant,UiDragStatus.Accepted,false);
            }
            catch{return UiDragViewerAcknowledgement(request,null,UiDragStatus.ContextLost,true);}
        }
        bool ValidateUiDragViewer(UiDragViewerGrant grant,IMyPlayer player)
        {
            try
            {
                if(grant==null||MyAPIGateway.Multiplayer==null||!MyAPIGateway.Multiplayer.IsServer
                    ||_ticks-grant.LastSeen>UiDragViewerIdleTicks||_ticks-grant.Started>UiDragViewerLifetimeTicks)return false;
                if(player==null)player=UiDragPlayer(grant.Sender);
                if(player==null||player.SteamUserId!=grant.Sender||player.IdentityId!=grant.Identity||player.Character==null
                    ||player.Character.Closed||player.Character.IsDead||player.Character.EntityId!=grant.CharacterId||player.Controller==null
                    ||!ReferenceEquals(player.Controller.ControlledEntity,player.Character)
                    ||!ReferenceEquals(MyAPIGateway.Entities.GetEntityById(grant.CharacterId),player.Character)
                    ||!ReferenceEquals(player.Character,grant.Character)||!ReferenceEquals(player.Controller,grant.Controller))return false;
                var caller=MyAPIGateway.Entities.GetEntityById(grant.CallerId) as IMyProgrammableBlock;
                var anchor=MyAPIGateway.Entities.GetEntityById(grant.TargetId) as IMyTerminalBlock;
                var tile=MyAPIGateway.Entities.GetEntityById(grant.TileId) as IMyTerminalBlock;
                if(caller==null||anchor==null||tile==null||!ReferenceEquals(caller,grant.Caller)||!ReferenceEquals(anchor,grant.Target)
                    ||!ReferenceEquals(tile,grant.Tile)||caller.Closed||anchor.Closed||tile.Closed||!caller.IsWorking||!anchor.IsWorking||!tile.IsWorking)return false;
                Authorize(caller,anchor);Authorize(caller,tile);
                if(!caller.HasPlayerAccess(grant.Identity)||!anchor.HasPlayerAccess(grant.Identity)||!tile.HasPlayerAccess(grant.Identity)
                    ||Vector3D.DistanceSquared(player.Character.GetPosition(),tile.GetPosition())>25)return false;
                var display=GetUiDisplay(grant.CallerId,grant.TargetId);var initial=GetUiWidget(grant.CallerId,grant.TargetId,grant.ControlId,false);
                if(display==null||display.Revision!=grant.DefinitionRevision||initial==null||initial.Control==null||initial.Bundle!=grant.Bundle
                    ||initial.Control.SourceRevision!=grant.SourceRevision||!HasVisibleUiWidget(display,initial,grant.HiddenBundle))return false;
                int sourceCount=0;
                foreach(var widget in display.Widgets)if(widget.Bundle==grant.Bundle&&widget.Control!=null)
                {
                    long epoch;if(!grant.Sources.TryGetValue(widget.Id,out epoch)||epoch!=widget.Control.SourceRevision
                        ||widget.Visible&&!UiControlSourceValid(display,widget,grant.HiddenBundle))return false;
                    sourceCount++;
                }
                if(sourceCount!=grant.Sources.Count)return false;
                Scene source,tileScene;MatrixD mapping;
                if(!_scenes.TryGetValue(grant.TargetId,out source)||!_scenes.TryGetValue(grant.TileId,out tileScene)
                    ||tileScene.LcdSourceId!=grant.TileSource||LocalView(source)!=grant.View||!UiDragWorldMapping(display,grant.TileId,out mapping))return false;
                var layout=CaptureUiFocusLayout(source,tileScene);if(grant.Layout==null||layout.Length!=grant.Layout.Length)return false;
                for(int i=0;i<layout.Length;i++)if(layout[i]!=grant.Layout[i])return false;
                var head=player.Character.GetHeadMatrix(true,true,true,true);var entry=Vector3D.Transform(grant.EntryLocal,tile.WorldMatrix);var toward=entry-head.Translation;
                return toward.LengthSquared()>=1e-8&&toward.LengthSquared()<=36&&Vector3D.Dot(Vector3D.Normalize(toward),head.Forward)>=.25
                    &&UiUnoccluded(player.Character.EntityId,tile,head.Translation,entry);
            }
            catch{return false;}
        }
        bool UiDragViewerPreflight(ulong sender,long id,IMyPlayer player,out UiDragViewerGrant grant)
        {
            grant=null;UiDragViewerGrant found;
            if(!_uiDragViewers.TryGetValue(sender,out found)||found.Id!=id)return false;
            if(!ValidateUiDragViewer(found,player)){RevokeUiDragViewer(found);return false;}
            grant=found;return true;
        }
        bool UiDragViewerFor(ulong sender,long id,UiDisplay display,UiWidget widget,IMyPlayer player,out UiDragViewerGrant grant)
        {
            grant=null;UiDragViewerGrant found;if(!UiDragViewerPreflight(sender,id,player,out found))return false;
            if(display==null||widget==null||widget.Control==null||display.CallerId!=found.CallerId||display.TargetId!=found.TargetId
                ||display.Revision!=found.DefinitionRevision||widget.Bundle!=found.Bundle
                ||!HasVisibleUiWidget(display,widget,found.HiddenBundle)||!UiControlSourceValid(display,widget,found.HiddenBundle))return false;
            grant=found;return true;
        }
        bool UiDragViewerClick(ulong sender,long expectedViewerId,UiDisplay display,UiWidget widget,IMyPlayer player,out Vector3D hit,out long tileId)
        {
            hit=Vector3D.Zero;tileId=0;UiDragViewerGrant current,grant;
            // Check the packet's viewer nonce before context validation can
            // revoke anything. A retired click cannot operate a successor.
            if(expectedViewerId<=0||!_uiDragViewers.TryGetValue(sender,out current)||current.Id!=expectedViewerId
                ||!UiDragViewerPreflight(sender,expectedViewerId,player,out grant)
                ||widget==null||widget.Control==null||widget.Control.Draggable
                ||!UiDragViewerFor(sender,expectedViewerId,display,widget,player,out grant))return false;
            var tile=MyAPIGateway.Entities.GetEntityById(grant.TileId) as IMyTerminalBlock;
            if(tile==null)return false;hit=Vector3D.Transform(grant.EntryLocal,tile.WorldMatrix);tileId=grant.TileId;return true;
        }
        void RevokeUiDragViewer(UiDragViewerGrant grant)
        {
            var request=new UiDragRequest{Kind=(int)UiDragKind.Focus,CallerId=grant.CallerId,TargetId=grant.TargetId,ControlId=grant.ControlId,
                DefinitionRevision=grant.DefinitionRevision,ValueRevision=Math.Max(1,grant.SourceRevision),RequestId=grant.RequestId,
                Sequence=grant.Sequence,ViewerId=grant.Id,LeaseId=grant.Id};
            var ack=UiDragViewerAcknowledgement(request,grant,UiDragStatus.ContextLost,true);
            CloseUiDragViewer(grant.Sender);SendUiDragAcknowledgement(grant.Sender,ack);
        }
        void TickUiDragViewers()
        {
            if(_uiDragViewers.Count==0)return;
            var grants=new List<UiDragViewerGrant>(_uiDragViewers.Values);
            foreach(var grant in grants)if(!ValidateUiDragViewer(grant,null))RevokeUiDragViewer(grant);
        }
        void CloseUiDragViewer(ulong sender)
        {
            UiDragViewerGrant grant;if(!_uiDragViewers.TryGetValue(sender,out grant))return;
            _uiDragViewers.Remove(sender);
            if(_uiDrags!=null){_uiDrags.RemoveViewer(sender,grant.Id);_uiDrags.CancelPeer(sender);}
        }
        void ClearUiDragViewers(){_uiDragViewers.Clear();}
    }
}
