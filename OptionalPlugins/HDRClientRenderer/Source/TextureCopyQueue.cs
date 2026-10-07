using System;
using System.Collections.Generic;

namespace HDRClientRenderer
{
    // Game-thread producers and a render-thread consumer share only bounded requests.
    // Cancel and execution serialize, so an old source cannot overwrite a reused slot.
    internal sealed class TextureCopyQueue
    {
        internal const int MaxFrames=4,MaxBytes=2*1024*1024;
        internal sealed class Request
        {
            internal string Source,Target;
            internal int Width,Height,Attempts;
            internal long Order;
        }
        readonly object gate=new object();
        readonly Dictionary<string,Request> requests=new Dictionary<string,Request>();
        long sequence;
        internal int Count { get {lock(gate)return requests.Count;} }
        static bool Target(string name)
        {
            const string prefix="HDR_ClientLcd_";int slot;
            return name!=null&&name.StartsWith(prefix,StringComparison.Ordinal)&&
                int.TryParse(name.Substring(prefix.Length),out slot)&&slot>=0&&slot<16;
        }
        internal bool Enqueue(string source,string target,int width,int height,Func<bool> register=null)
        {
            if(string.IsNullOrEmpty(source)||source.Length>256||!Target(target)||width<16||height<16||
                width>1024||height>1024||(long)width*height>262144||source==target)return false;
            lock(gate)
            {
                Request old;requests.TryGetValue(target,out old);
                if(old==null&&requests.Count>=16)return false;
                if(register!=null&&!register())return false;
                requests[target]=new Request{Source=source,Target=target,Width=width,Height=height,
                    Order=old==null?++sequence:old.Order,Attempts=old!=null&&old.Source==source&&old.Width==width&&old.Height==height?old.Attempts:0};
                return true;
            }
        }
        internal void Cancel(string target) {lock(gate)requests.Remove(target);}
        internal void Cancel(string target,Action cleanup){lock(gate){requests.Remove(target);cleanup();}}
        internal void Clear() {lock(gate)requests.Clear();}
        internal void Clear(Action cleanup){lock(gate){requests.Clear();cleanup();}}
        internal void Drain(Func<Request,bool> copy,Action<string> report,Func<Request,bool> eligible=null,Action<Request> failed=null)
        {
            lock(gate)
            {
                var ready=new List<Request>(requests.Values);ready.Sort((a,b)=>a.Order.CompareTo(b.Order));
                int frames=0,bytes=0;
                foreach(var request in ready)
                {
                    int cost=request.Width*request.Height*4;
                    if(frames>=MaxFrames||cost>MaxBytes-bytes)break;
                    if(eligible!=null&&!eligible(request))continue;
                    frames++;bytes+=cost;
                    try
                    {
                        if(copy(request)){requests.Remove(request.Target);continue;}
                        if(++request.Attempts<120)continue;
                        report("LCD GPU copy resources unavailable: "+request.Target);
                    }
                    catch(Exception ex){report("LCD GPU copy failed: "+ex.GetBaseException().Message);}
                    if(failed!=null)failed(request);
                    requests.Remove(request.Target);
                }
            }
        }
    }
}
