using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
namespace HDRClientRenderer
{
    // Foundation is deliberately unavailable until a native owner installs and
    // validates the full pass/context coverage. It cannot masquerade as a source.
    internal static class PortalCaptureCapability
    {
        internal static bool Available {get{return PortalCaptureIntegrationNative.Available;}}
        internal static string Reason {get{return Available?"Geometry-only portal shell capture: private per-draw clipping; volume atmospheres/clouds and debug primitives omitted; existing native shadow lighting retained.":"Portal capture is disabled: installed shader assets and immediate/deferred/native draw hooks have not been certified and installed.";}}
    }
    internal sealed class PortalClipRegistry
    {
        internal const int MaxVariants=128,MaxBytecodeBytes=64*1024*1024,MaxCompilationsPerUpdate=2;
        sealed class ReferenceComparer:IEqualityComparer<object>
        {public new bool Equals(object a,object b){return ReferenceEquals(a,b);}public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}}
        internal sealed class Entry
        {
            internal readonly PortalClipDescriptor Descriptor;internal readonly int Epoch;
            internal byte[] Bytecode;internal object PrivateShader;internal string Failure;internal bool Building;
            internal Entry(PortalClipDescriptor descriptor,int epoch){Descriptor=descriptor;Epoch=epoch;}
        }
        readonly object gate=new object();readonly Dictionary<object,Entry> entries=new Dictionary<object,Entry>(new ReferenceComparer());
        readonly Queue<Entry> pending=new Queue<Entry>();int epoch,resident;
        internal PortalClipRegistry(int initialEpoch=0){if(initialEpoch<0)throw new ArgumentOutOfRangeException("initialEpoch");epoch=initialEpoch;}
        internal int Epoch{get{lock(gate)return epoch;}}internal int ResidentBytes{get{lock(gate)return resident;}}
        internal string Status{get{lock(gate)return entries.Count+"/"+MaxVariants+" variants, "+entries.Values.Count(e=>e.Bytecode!=null&&e.PrivateShader==null)+" awaiting GPU, "+pending.Count+" awaiting compile, "+entries.Values.Count(e=>e.Building)+" compiling off-thread, "+entries.Values.Count(e=>e.Failure!=null)+" rejected, "+resident+" bytecode bytes";}}
        internal bool Queue(PortalClipDescriptor descriptor)
        {
            if(descriptor==null||descriptor.Original==null||descriptor.Family==PortalClipFamily.Unknown)return false;
            lock(gate)
            {
                Entry existing;if(entries.TryGetValue(descriptor.Original,out existing))return existing.Failure==null;
                if(entries.Count>=MaxVariants)return false;var entry=new Entry(descriptor,epoch);entries.Add(descriptor.Original,entry);pending.Enqueue(entry);return true;
            }
        }
        internal int Warm(Func<PortalClipDescriptor,byte[]> compile)
        {
            if(compile==null)throw new ArgumentNullException("compile");int attempted=0;
            while(attempted<MaxCompilationsPerUpdate)
            {
                Entry item;lock(gate){if(pending.Count==0)break;item=pending.Dequeue();if(item.Epoch!=epoch)continue;item.Building=true;}
                byte[] bytes=null;string failure=null;try{bytes=compile(item.Descriptor);if(bytes==null||bytes.Length<4)failure="Portal compiler returned no validated bytecode.";}catch(Exception ex){failure=ex.GetBaseException().Message;}attempted++;
                lock(gate)
                {
                    item.Building=false;if(item.Epoch!=epoch)continue;
                    if(failure==null&&bytes.Length>MaxBytecodeBytes-resident)failure="Portal private bytecode quota exceeded.";
                    item.Failure=failure;if(failure==null){item.Bytecode=(byte[])bytes.Clone();resident+=bytes.Length;}
                }
            }
            return attempted;
        }
        internal Entry[] ClaimPending(int maximum)
        {
            if(maximum<1||maximum>MaxCompilationsPerUpdate)throw new ArgumentOutOfRangeException("maximum");
            lock(gate){var result=new List<Entry>();while(pending.Count!=0&&result.Count<maximum){var item=pending.Dequeue();if(item.Epoch!=epoch)continue;item.Building=true;result.Add(item);}return result.ToArray();}
        }
        internal void CompleteCompilation(Entry item,byte[] bytes,string failure)
        {
            lock(gate)
            {
                if(item==null||item.Epoch!=epoch)return;Entry current;if(!entries.TryGetValue(item.Descriptor.Original,out current)||!ReferenceEquals(item,current))return;
                item.Building=false;if(failure==null&&(bytes==null||bytes.Length<4))failure="Portal compiler returned no validated bytecode.";
                if(failure==null&&bytes.Length>MaxBytecodeBytes-resident)failure="Portal private bytecode quota exceeded.";
                item.Failure=failure;if(failure==null){item.Bytecode=(byte[])bytes.Clone();resident+=bytes.Length;}
            }
        }
        internal Entry Prepared(object original)
        {lock(gate){Entry item;return original!=null&&entries.TryGetValue(original,out item)&&item.Epoch==epoch&&item.Failure==null&&item.Bytecode!=null?item:null;}}
        internal Entry[] AwaitingAttachment()
        {lock(gate)return entries.Values.Where(item=>item.Epoch==epoch&&item.Failure==null&&item.Bytecode!=null&&item.PrivateShader==null).ToArray();}
        internal bool Attach(Entry item,object privateShader)
        {
            if(item==null||privateShader==null)return false;
            lock(gate){Entry current;if(item.Epoch!=epoch||!entries.TryGetValue(item.Descriptor.Original,out current)||!ReferenceEquals(current,item)||item.Bytecode==null||item.Failure!=null||item.PrivateShader!=null)return false;item.PrivateShader=privateShader;return true;}
        }
        internal bool Select(object original,PortalCaptureGate capture,bool cutoffBound,out object replacement)
        {
            replacement=null;if(capture==null)return false;if(capture.Epoch!=Epoch){capture.Reject("Portal device epoch changed during capture.");return false;}Entry item=Prepared(original);
            if(item==null||item.PrivateShader==null){capture.Reject(Issue(original));return false;}
            if(!capture.Observe(item.Descriptor.Family,true,cutoffBound))return false;replacement=item.PrivateShader;return true;
        }
        internal string Issue(object original)
        {
            lock(gate)
            {
                Entry item;if(original==null||!entries.TryGetValue(original,out item))return entries.Count>=MaxVariants?"Portal required shader exceeds the private variant capacity ("+MaxVariants+").":"Portal shader identity is absent from the private geometry registry.";
                if(item.Epoch!=epoch)return "Portal shader belongs to a stale device epoch.";
                if(item.Failure!=null)return "Portal private shader compilation rejected: "+item.Failure;
                if(item.Bytecode==null)return "Portal geometry shader is waiting for bounded CPU warmup.";
                if(item.PrivateShader==null)return "Portal geometry shader is waiting for bounded GPU attachment.";
                return "Portal shader selection failed its capture proof.";
            }
        }
        internal object[] NewEpoch()
        {
            lock(gate)
            {
                var retired=new HashSet<object>(new ReferenceComparer());foreach(var item in entries.Values)if(item.PrivateShader!=null)retired.Add(item.PrivateShader);
                entries.Clear();pending.Clear();resident=0;epoch=checked(epoch+1);return retired.ToArray();
            }
        }
    }
}
