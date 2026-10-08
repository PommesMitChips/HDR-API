using System;
using System.Collections.Generic;

namespace HoloMap
{
    // Immutable authority/context identity plus the value revision owned by this
    // lease. Hosts must return a fresh instance for begin, and compare source value
    // revision against ValueRevision on every validation and commit.
    public sealed class UiDragBinding
    {
        public long CallerId, TargetId, CharacterId, TileId, DefinitionRevision, ValueRevision;
        public long ViewerId;
        public string ControlId, ValueId;
        public int Mode;
        public double Value;
        public object Context;
    }
    public sealed class UiDragCommit
    {
        public bool Accepted;
        public long ValueRevision;
        public double Value;
        public UiDragStatus Status = UiDragStatus.Stale;
    }
    public sealed class UiDragTransactions
    {
        sealed class Peer { public long Sequence, RequestId; public int Started, Count, LastSeen, LastResync=-60; }
        sealed class Work { public ulong Sender; public UiDragRequest Request; public long MinimumSequence; }
        sealed class Lease { public ulong Sender; public long Id, RequestId, LastSequence; public UiDragBinding Binding; public int Started, LastSeen, LastMove; }
        readonly object _gate = new object();
        readonly Dictionary<ulong, Peer> _peers = new Dictionary<ulong, Peer>();
        readonly Queue<Work> _begins = new Queue<Work>();
        readonly Queue<Work> _sequenceRejections = new Queue<Work>();
        readonly Dictionary<long, Work> _moves = new Dictionary<long, Work>();
        readonly Dictionary<long, Work> _terminals = new Dictionary<long, Work>();
        readonly Dictionary<string, Work> _cancelledBegins = new Dictionary<string, Work>();
        readonly Dictionary<long, Lease> _leases = new Dictionary<long, Lease>();
        readonly Dictionary<string, long> _valueLocks = new Dictionary<string, long>();
        readonly Dictionary<long,ulong> _viewerOwners=new Dictionary<long,ulong>();
        readonly Dictionary<ulong,long> _peerViewers=new Dictionary<ulong,long>();
        readonly Dictionary<long,Work> _viewerHeartbeats=new Dictionary<long,Work>();
        readonly Dictionary<long,Work> _viewerTerminals=new Dictionary<long,Work>();
        readonly Func<ulong, UiDragRequest, UiDragBinding> _begin;
        readonly Func<ulong, UiDragBinding, bool> _validate;
        readonly Func<UiDragBinding, double, UiDragCommit> _commit;
        readonly Func<UiDragBinding, double?> _worldValue;
        readonly Action<ulong, UiDragAck> _ack;
        readonly Action<UiDragBinding, bool> _release;
        readonly Func<ulong,UiDragRequest,UiDragAck> _viewer;
        long _nextLease;
        int _globalStarted, _globalBegins;
        int _resyncStarted,_resyncCount;
        public int MoveIntervalTicks = 6; // 60 simulation ticks / 10 Hz.
        public int IdleTicks = 180, LifetimeTicks = 1800;
        public const int MaxPeers = 128, MaxLeases = 32, BeginCapacity = 16;
        public UiDragTransactions(Func<ulong,UiDragRequest,UiDragBinding> begin, Func<ulong,UiDragBinding,bool> validate,
            Func<UiDragBinding,double,UiDragCommit> commit, Func<UiDragBinding,double?> worldValue, Action<ulong,UiDragAck> ack,
            Action<UiDragBinding,bool> release = null,Func<ulong,UiDragRequest,UiDragAck> viewer=null)
        { _begin=begin;_validate=validate;_commit=commit;_worldValue=worldValue;_ack=ack;_release=release;_viewer=viewer; }
        public int ActiveLeases { get { lock(_gate) return _leases.Count; } }
        public int PendingMoves { get { lock(_gate) return _moves.Count; } }
        public int PendingTerminals { get { lock(_gate) return _terminals.Count; } }
        static string ValueKey(UiDragBinding b) { return b.CallerId + "/" + b.TargetId + "/" + b.ValueId; }
        public bool Submit(ulong sender, UiDragRequest request, int tick)
        {
            if(sender==0 || !UiDragWire.Valid(request)) return false;
            lock(_gate)
            {
                Peer peer;
                if(!_peers.TryGetValue(sender,out peer))
                { if(_peers.Count>=MaxPeers)return false;peer=new Peer{Started=tick};_peers.Add(sender,peer); }
                if(request.Sequence<=peer.Sequence)
                {if(request.Kind==(int)UiDragKind.Begin||request.Kind==(int)UiDragKind.Focus)QueueSequenceRejection(sender,request,peer,tick);return false;}
                // Accepted packet identity is consumed before queueing. Only the
                // latest move survives; replay cannot become valid later.
                peer.Sequence=request.Sequence;peer.LastSeen=tick;
                if(request.Kind==(int)UiDragKind.Begin||request.Kind==(int)UiDragKind.Focus)
                {
                    if(request.RequestId<=peer.RequestId){QueueSequenceRejection(sender,request,peer,tick);return false;}peer.RequestId=request.RequestId;
                    if(tick-peer.Started>=60){peer.Started=tick;peer.Count=0;}
                    if(tick-_globalStarted>=60){_globalStarted=tick;_globalBegins=0;}
                    if(peer.Count>=5||_globalBegins>=20||_begins.Count>=BeginCapacity)return false;
                    peer.Count++;_globalBegins++;
                    _begins.Enqueue(new Work{Sender=sender,Request=UiDragWire.Copy(request)});return true;
                }
                if(request.Kind==(int)UiDragKind.KeepAlive)
                {
                    ulong owner;if(!_viewerOwners.TryGetValue(request.ViewerId,out owner)||owner!=sender)return false;
                    if(_viewerTerminals.ContainsKey(request.ViewerId))return false;
                    _viewerHeartbeats[request.ViewerId]=new Work{Sender=sender,Request=UiDragWire.Copy(request)};return true;
                }
                if(request.Kind==(int)UiDragKind.Blur)
                {
                    long id=request.ViewerId;
                    if(id==0)
                    {
                        foreach(var pending in _begins)if(pending.Sender==sender&&pending.Request.Kind==(int)UiDragKind.Focus
                            &&pending.Request.RequestId==request.RequestId&&pending.Request.CallerId==request.CallerId&&pending.Request.TargetId==request.TargetId
                            &&pending.Request.ControlId==request.ControlId&&pending.Request.DefinitionRevision==request.DefinitionRevision
                            &&pending.Request.ValueRevision==request.ValueRevision&&pending.Request.Mode==request.Mode)
                        {_cancelledBegins[sender+"/"+request.RequestId]=new Work{Sender=sender,Request=UiDragWire.Copy(request)};return true;}
                        if(!_peerViewers.TryGetValue(sender,out id))return false;
                    }
                    ulong owner;if(!_viewerOwners.TryGetValue(id,out owner)||owner!=sender||_viewerTerminals.ContainsKey(id))return false;
                    var terminal=UiDragWire.Copy(request);terminal.ViewerId=terminal.LeaseId=id;
                    _viewerHeartbeats.Remove(id);_viewerTerminals[id]=new Work{Sender=sender,Request=terminal};return true;
                }
                Lease lease;
                if(request.Kind==(int)UiDragKind.Cancel&&request.LeaseId==0)
                {
                    foreach(var pending in _begins)if(pending.Sender==sender&&pending.Request.RequestId==request.RequestId
                        &&pending.Request.CallerId==request.CallerId&&pending.Request.TargetId==request.TargetId&&pending.Request.ControlId==request.ControlId
                        &&pending.Request.DefinitionRevision==request.DefinitionRevision&&pending.Request.ValueRevision==request.ValueRevision&&pending.Request.Mode==request.Mode)
                    {_cancelledBegins[sender+"/"+request.RequestId]=new Work{Sender=sender,Request=UiDragWire.Copy(request)};return true;}
                    foreach(var active in _leases.Values)if(active.Sender==sender&&active.RequestId==request.RequestId&&Matches(active,request))
                    {if(_terminals.ContainsKey(active.Id))return false;var cancel=UiDragWire.Copy(request);cancel.LeaseId=active.Id;_moves.Remove(active.Id);_terminals[active.Id]=new Work{Sender=sender,Request=cancel};return true;}
                    return false;
                }
                if(!_leases.TryGetValue(request.LeaseId,out lease)||lease.Sender!=sender||!Matches(lease,request))return false;
                if(_terminals.ContainsKey(lease.Id))return false;
                var work=new Work{Sender=sender,Request=UiDragWire.Copy(request)};
                if(request.Kind==(int)UiDragKind.Move)_moves[lease.Id]=work;
                else { _moves.Remove(lease.Id);_terminals[lease.Id]=work; } // One reserved slot per active lease.
                return true;
            }
        }
        static bool Matches(Lease lease, UiDragRequest p)
        {var b=lease.Binding;return p.RequestId==lease.RequestId&&p.CallerId==b.CallerId&&p.TargetId==b.TargetId&&p.ControlId==b.ControlId
            &&p.DefinitionRevision==b.DefinitionRevision&&p.Mode==b.Mode&&p.ValueRevision<=b.ValueRevision&&p.ViewerId==b.ViewerId;}
        void QueueSequenceRejection(ulong sender,UiDragRequest request,Peer peer,int tick)
        {
            if(tick-_resyncStarted>=60){_resyncStarted=tick;_resyncCount=0;}
            if(tick-peer.LastResync<60||_resyncCount>=20||_sequenceRejections.Count>=BeginCapacity)return;
            peer.LastResync=tick;_resyncCount++;
            _sequenceRejections.Enqueue(new Work{Sender=sender,Request=UiDragWire.Copy(request),MinimumSequence=Math.Max(peer.Sequence,peer.RequestId)});
        }
        public void Tick(int tick)
        {
            lock(_gate)
            {
                for(int i=0;i<4&&_sequenceRejections.Count>0;i++)Reject(_sequenceRejections.Dequeue(),UiDragStatus.Stale);
                var viewerTerminals=new List<Work>(_viewerTerminals.Values);_viewerTerminals.Clear();foreach(var terminal in viewerTerminals)ProcessViewer(terminal);
                // Never let moves consume the terminal budget. Terminals always
                // release even when current permission/context validation fails.
                var terminalIds=new List<long>(_terminals.Keys);
                foreach(long id in terminalIds){Work w=_terminals[id];_terminals.Remove(id);ProcessTerminal(w,tick);}
                for(int i=0;i<4&&_begins.Count>0;i++)ProcessBegin(_begins.Dequeue(),tick);
                var heartbeats=new List<Work>(_viewerHeartbeats.Values);_viewerHeartbeats.Clear();foreach(var heartbeat in heartbeats)ProcessViewer(heartbeat);
                var ids=new List<long>(_leases.Keys);
                foreach(long id in ids)
                {
                    Lease lease;if(!_leases.TryGetValue(id,out lease))continue;
                    if(tick-lease.LastSeen>Math.Max(1,IdleTicks)||tick-lease.Started>Math.Max(1,LifetimeTicks))
                    {Revoke(lease,UiDragStatus.TimedOut);continue;}
                    if(!ValidContext(lease)){Revoke(lease,UiDragStatus.ContextLost);continue;}
                    Work work;if(!_moves.TryGetValue(id,out work)||tick-lease.LastMove<Math.Max(1,MoveIntervalTicks))continue;
                    _moves.Remove(id);lease.LastMove=tick;lease.LastSeen=tick;lease.LastSequence=work.Request.Sequence;
                    var result=Commit(lease,work.Request);
                    Emit(lease,work.Request,result.Accepted?UiDragStatus.Accepted:result.Status,!result.Accepted);
                    if(!result.Accepted)Remove(lease);
                }
                if(tick%60==0)
                {
                    var stale=new List<ulong>();foreach(var entry in _peers)if(tick-entry.Value.LastSeen>3600&&!HasPeerLease(entry.Key))stale.Add(entry.Key);
                    foreach(ulong sender in stale)_peers.Remove(sender);
                }
            }
        }
        bool HasPeerLease(ulong sender){foreach(var lease in _leases.Values)if(lease.Sender==sender)return true;return false;}
        bool ValidContext(Lease lease){try{return _validate!=null&&_validate(lease.Sender,lease.Binding);}catch{return false;}}
        void ProcessBegin(Work work,int tick)
        {
            Work cancel;string pendingKey=work.Sender+"/"+work.Request.RequestId;
            if(_cancelledBegins.TryGetValue(pendingKey,out cancel)){_cancelledBegins.Remove(pendingKey);Reject(cancel,UiDragStatus.Cancelled);return;}
            if(work.Request.Kind==(int)UiDragKind.Focus){ProcessViewer(work);return;}
            if(_leases.Count>=MaxLeases||HasPeerLease(work.Sender)||_nextLease==long.MaxValue){Reject(work,UiDragStatus.Busy);return;}
            var p=work.Request;UiDragBinding b=null;try{if(_begin!=null)b=_begin(work.Sender,p);}catch{}
            if(b==null||b.CallerId!=p.CallerId||b.TargetId!=p.TargetId||b.ControlId!=p.ControlId||b.Mode!=p.Mode
                ||b.CharacterId==0||b.TileId==0||b.DefinitionRevision!=p.DefinitionRevision||b.ValueRevision!=p.ValueRevision
                ||!UiDragWire.Id(b.ValueId)||!UiDragWire.Finite(b.Value)) {Release(b,false);Reject(work,UiDragStatus.Stale);return;}
            string key=ValueKey(b);
            if(_leases.Count>=MaxLeases||_valueLocks.ContainsKey(key)||HasPeerLease(work.Sender)){Release(b,false);Reject(work,UiDragStatus.Busy);return;}
            if(_nextLease==long.MaxValue){Release(b,false);Reject(work,UiDragStatus.Busy);return;}
            var lease=new Lease{Sender=work.Sender,Id=++_nextLease,RequestId=p.RequestId,LastSequence=p.Sequence,Binding=b,Started=tick,LastSeen=tick,LastMove=tick};
            if(!ValidContext(lease)){Release(b,false);Reject(work,UiDragStatus.ContextLost);return;}
            _leases.Add(lease.Id,lease);_valueLocks.Add(key,lease.Id);Emit(lease,p,UiDragStatus.Accepted,false);
        }
        UiDragCommit Commit(Lease lease,UiDragRequest request)
        {
            if(!ValidContext(lease))return new UiDragCommit{Status=UiDragStatus.ContextLost};
            try
            {
                double value=request.Value;
                if(lease.Binding.Mode==(int)UiDragMode.WorldLook)
                {double? derived=_worldValue==null?null:_worldValue(lease.Binding);if(!derived.HasValue)return new UiDragCommit{Status=UiDragStatus.Bounds};value=derived.Value;}
                if(!UiDragWire.Finite(value)||_commit==null)return new UiDragCommit{Status=UiDragStatus.Bounds};
                var result=_commit(lease.Binding,value);
                if(result==null)return new UiDragCommit();
                if(!result.Accepted)return new UiDragCommit{Status=result.Status==UiDragStatus.Accepted?UiDragStatus.Stale:result.Status};
                if(!UiDragWire.Finite(result.Value))return new UiDragCommit{Status=UiDragStatus.Bounds};
                // A canonical no-op keeps its value revision; changing the value
                // requires a strictly newer revision. Neither case can regress.
                if(result.ValueRevision<lease.Binding.ValueRevision||result.ValueRevision==lease.Binding.ValueRevision&&result.Value!=lease.Binding.Value)
                    return new UiDragCommit{Status=UiDragStatus.Stale};
                lease.Binding.Value=result.Value;lease.Binding.ValueRevision=result.ValueRevision;return result;
            }
            catch{return new UiDragCommit{Status=UiDragStatus.ContextLost};}
        }
        void ProcessTerminal(Work work,int tick)
        {
            Lease lease;if(!_leases.TryGetValue(work.Request.LeaseId,out lease)||lease.Sender!=work.Sender||!Matches(lease,work.Request))return;
            lease.LastSequence=work.Request.Sequence;
            UiDragStatus status=UiDragStatus.Cancelled;
            if(work.Request.Kind==(int)UiDragKind.End)
            {
                if(tick-lease.LastSeen>Math.Max(1,IdleTicks)||tick-lease.Started>Math.Max(1,LifetimeTicks))status=UiDragStatus.TimedOut;
                else {var result=Commit(lease,work.Request);status=result.Accepted?UiDragStatus.Accepted:result.Status;}
            }
            Emit(lease,work.Request,status,true);Remove(lease,status==UiDragStatus.Accepted);
        }
        void Reject(Work work,UiDragStatus status)
        {var p=work.Request;Send(work.Sender,new UiDragAck{Kind=p.Kind,CallerId=p.CallerId,TargetId=p.TargetId,ControlId=p.ControlId,
            DefinitionRevision=p.DefinitionRevision,ValueRevision=p.ValueRevision,RequestId=p.RequestId,Sequence=p.Sequence,Status=(int)status,Terminal=true,MinimumSequence=work.MinimumSequence,ViewerId=p.ViewerId});}
        void Emit(Lease lease,UiDragRequest request,UiDragStatus status,bool terminal)
        {var b=lease.Binding;Send(lease.Sender,new UiDragAck{Kind=request.Kind,CallerId=b.CallerId,TargetId=b.TargetId,ControlId=b.ControlId,
            DefinitionRevision=b.DefinitionRevision,ValueRevision=b.ValueRevision,RequestId=lease.RequestId,Sequence=request.Sequence,
            LeaseId=lease.Id,Status=(int)status,Value=b.Value,TileId=b.TileId,CharacterId=b.CharacterId,Terminal=terminal,ViewerId=b.ViewerId});}
        void ProcessViewer(Work work)
        {
            if(work.Request.Kind==(int)UiDragKind.Focus&&_peerViewers.Count>=MaxLeases&&!_peerViewers.ContainsKey(work.Sender))
            {Reject(work,UiDragStatus.Busy);return;}
            UiDragAck result=null;try{if(_viewer!=null)result=_viewer(work.Sender,work.Request);}catch{}
            if(!UiDragWire.Valid(result)||result.CallerId!=work.Request.CallerId||result.TargetId!=work.Request.TargetId
                ||result.ControlId!=work.Request.ControlId||result.DefinitionRevision!=work.Request.DefinitionRevision
                ||result.RequestId!=work.Request.RequestId||result.Sequence!=work.Request.Sequence){Reject(work,UiDragStatus.ContextLost);return;}
            if(result.Status==(int)UiDragStatus.Accepted&&!result.Terminal)
            {
                if(result.ViewerId<=0||result.LeaseId!=result.ViewerId||_peerViewers.Count>=MaxLeases&&!_peerViewers.ContainsKey(work.Sender))
                {Reject(work,UiDragStatus.Busy);return;}
                long prior;if(_peerViewers.TryGetValue(work.Sender,out prior)&&prior!=result.ViewerId)_viewerOwners.Remove(prior);
                _peerViewers[work.Sender]=result.ViewerId;_viewerOwners[result.ViewerId]=work.Sender;
            }
            else if(result.ViewerId>0)RemoveViewer(work.Sender,result.ViewerId);
            Send(work.Sender,result);
        }
        public void RemoveViewer(ulong sender,long viewerId)
        {lock(_gate){ulong owner;if(_viewerOwners.TryGetValue(viewerId,out owner)&&owner==sender){_viewerOwners.Remove(viewerId);_viewerHeartbeats.Remove(viewerId);_viewerTerminals.Remove(viewerId);}
            long active;if(_peerViewers.TryGetValue(sender,out active)&&active==viewerId)_peerViewers.Remove(sender);}}
        void Send(ulong sender,UiDragAck ack){try{if(_ack!=null)_ack(sender,ack);}catch{}}
        void Release(UiDragBinding binding,bool commit){try{if(binding!=null&&_release!=null)_release(binding,commit);}catch{}}
        void Remove(Lease lease,bool commit=false){_leases.Remove(lease.Id);_valueLocks.Remove(ValueKey(lease.Binding));_moves.Remove(lease.Id);_terminals.Remove(lease.Id);Release(lease.Binding,commit);}
        void Revoke(Lease lease,UiDragStatus status)
        {Emit(lease,new UiDragRequest{Kind=(int)UiDragKind.Cancel,Sequence=lease.LastSequence},status,true);Remove(lease);}
        public void Preempt(long caller,long target,string valueId)
        {lock(_gate){long id;Lease lease;if(_valueLocks.TryGetValue(caller+"/"+target+"/"+valueId,out id)&&_leases.TryGetValue(id,out lease))Revoke(lease,UiDragStatus.Preempted);}}
        public void CancelPeer(ulong sender)
        {lock(_gate){var ids=new List<long>(_leases.Keys);foreach(long id in ids){var lease=_leases[id];if(lease.Sender==sender)Revoke(lease,UiDragStatus.Cancelled);}}}
        public void Clear(){lock(_gate){foreach(var lease in _leases.Values)Release(lease.Binding,false);_peers.Clear();_begins.Clear();_sequenceRejections.Clear();_moves.Clear();_terminals.Clear();_cancelledBegins.Clear();_leases.Clear();_valueLocks.Clear();_viewerOwners.Clear();_peerViewers.Clear();_viewerHeartbeats.Clear();_viewerTerminals.Clear();}}
    }
}
