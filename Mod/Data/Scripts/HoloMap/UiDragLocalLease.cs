using System;
using System.Collections.Generic;

namespace HoloMap
{
    // This state predicts values, never rewrites retained geometry or its compile
    // identity. Rendering obtains Prediction only while both ownership barriers hold.
    public sealed class UiDragLocalLease
    {
        readonly Action<UiDragRequest> _send;
        readonly Action _release;
        readonly Func<long> _allocateSequence;
        readonly HashSet<long> _sentSequences=new HashSet<long>();
        UiDragRequest _identity;
        long _sequence, _lastAckSequence, _lastFrame=-1;
        long _predictionGeneration, _sentPredictionGeneration, _sentSequence;
        int _started, _lastAckTick, _lastSendTick;
        bool _pointerApplied, _grant, _held, _terminal;
        public double Prediction { get; private set; }
        public double Confirmed { get; private set; }
        public long ValueRevision { get; private set; }
        public long LeaseId { get; private set; }
        public bool Pending { get { return _identity!=null; } }
        public bool Active { get { return Pending&&_pointerApplied&&_grant&&!_terminal; } }
        public bool Held { get { return Pending&&_held&&!_terminal; } }
        public bool Finishing {get{return Pending&&_terminal;}}
        public long RequestId { get { return _identity==null?0:_identity.RequestId; } }
        public int MoveIntervalTicks=6, AckTimeoutTicks=180;
        public UiDragLocalLease(Action<UiDragRequest> send,Action release,Func<long> allocateSequence=null)
        {_send=send;_release=release;_allocateSequence=allocateSequence;}
        public bool Begin(UiDragRequest begin,int tick)
        {
            if(Pending||!UiDragWire.Valid(begin)||begin.Kind!=(int)UiDragKind.Begin)return false;
            _identity=UiDragWire.Copy(begin);_sequence=begin.Sequence;_lastAckSequence=0;_lastFrame=-1;
            _sentSequences.Clear();
            LeaseId=0;ValueRevision=begin.ValueRevision;Prediction=Confirmed=begin.Value;
            _predictionGeneration=0;_started=_lastAckTick=_lastSendTick=tick;_pointerApplied=_grant=_held=_terminal=false;Send(begin);return Pending;
        }
        public void Receive(UiDragAck ack,int tick)
        {
            if(!MatchesAcknowledgement(ack))return;
            if(ack.Terminal||ack.Status!=(int)UiDragStatus.Accepted)
            {if(ack.Status==(int)UiDragStatus.Accepted&&ack.ValueRevision>=ValueRevision){Prediction=Confirmed=ack.Value;ValueRevision=ack.ValueRevision;}Clear();return;}
            if(ack.LeaseId<=0||ack.ValueRevision<ValueRevision)return;
            if(_terminal){LeaseId=ack.LeaseId;ValueRevision=ack.ValueRevision;SendRequest(UiDragKind.Cancel,Confirmed);return;}
            if(ack.Sequence<=_lastAckSequence)return;
            LeaseId=ack.LeaseId;ValueRevision=ack.ValueRevision;Confirmed=ack.Value;_grant=true;_lastAckSequence=ack.Sequence;_lastAckTick=tick;
            // Reconcile only predictions covered by this acknowledgement. A newer
            // local pointer frame keeps its unsent/ongoing absolute prediction.
            if(ack.Sequence==_sentSequence&&_sentPredictionGeneration==_predictionGeneration)Prediction=Confirmed;
        }
        public bool MatchesAcknowledgement(UiDragAck ack)
        {
            return Pending&&UiDragWire.Valid(ack)&&ack.RequestId==_identity.RequestId&&ack.CallerId==_identity.CallerId
                &&ack.TargetId==_identity.TargetId&&ack.ControlId==_identity.ControlId&&ack.DefinitionRevision==_identity.DefinitionRevision
                &&ack.ViewerId==_identity.ViewerId&&ack.Sequence>=_lastAckSequence&&ack.Sequence<=_sequence&&_sentSequences.Contains(ack.Sequence)
                &&(LeaseId==0||ack.LeaseId==LeaseId);
        }
        public bool Pointer(UiPointerSample sample,int tick)
        {
            if(!Pending)return false;
            if((sample.Flags&(4|32|64))!=0){Cancel(tick);return false;}
            if(sample.Frame<=_lastFrame)return false;_lastFrame=sample.Frame;
            if(!sample.Active){_pointerApplied=false;if(_grant&&tick-_started>AckTimeoutTicks)Cancel(tick);return false;}
            _pointerApplied=true;
            if((sample.Released&1)!=0||_held&&(sample.Held&1)==0){End(tick);return false;}
            // An actual routed physical down may precede the network grant. Keep
            // that gesture latent; no prediction/send is enabled until the grant.
            if((sample.Pressed&1)!=0)_held=true;
            return Active&&_held;
        }
        public bool Predict(double value,int tick)
        {
            if(!Active||!_held||!UiDragWire.Finite(value))return false;
            Prediction=value;_predictionGeneration++;
            if(tick-_lastSendTick>=Math.Max(1,MoveIntervalTicks)){SendRequest(UiDragKind.Move,value);_lastSendTick=tick;}
            return true;
        }
        public void Tick(int tick,bool contextValid)
        {if(Pending&&_terminal&&tick-_lastAckTick>AckTimeoutTicks){Clear();return;}
            if(Pending&&(!contextValid||tick-_started>AckTimeoutTicks&&!_grant||tick-_lastAckTick>AckTimeoutTicks))Cancel(tick);}
        public void End(int tick)
        {
            if(!Pending||_terminal)return;
            bool commit=Active&&LeaseId>0;_terminal=true;_pointerApplied=false;
            if(commit)SendRequest(UiDragKind.End,Prediction);else SendRequest(UiDragKind.Cancel,Confirmed);
            Release();
        }
        public void Cancel(int tick)
        {
            if(!Pending)return;
            bool send=!_terminal;_terminal=true;_pointerApplied=false;
            if(send)SendRequest(UiDragKind.Cancel,Confirmed);
            Release();
        }
        void SendRequest(UiDragKind kind,double value)
        {
            long next;try{next=_allocateSequence==null?checked(_sequence+1):_allocateSequence();}catch{Clear();return;}
            if(!Pending)return;
            if(next<=_sequence){Clear();return;}_sequence=next;
            var p=UiDragWire.Copy(_identity);p.Kind=(int)kind;p.Sequence=next;p.LeaseId=LeaseId;p.ValueRevision=ValueRevision;p.Value=value;Send(p);
        }
        void Send(UiDragRequest p)
        {
            _sentSequence=p.Sequence;_sentPredictionGeneration=_predictionGeneration;
            if(_sentSequences.Count>=128){long oldest=long.MaxValue;foreach(long sequence in _sentSequences)if(sequence<oldest)oldest=sequence;_sentSequences.Remove(oldest);}
            _sentSequences.Add(p.Sequence);try{if(_send!=null)_send(p);}catch{Clear();}
        }
        void Release(){try{if(_release!=null)_release();}catch{}}
        public void Clear(){_identity=null;LeaseId=0;_grant=_pointerApplied=_held=_terminal=false;_sentSequences.Clear();Release();}
    }
}
