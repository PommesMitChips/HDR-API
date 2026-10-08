using System;
using System.Collections.Generic;
using System.Globalization;
using VRage;
#if !MOD_SOURCE_CONSUMER_POLICY_TESTS
using System.Threading;
using Sandbox.ModAPI;
using VRageMath;
using GameBlock = Sandbox.ModAPI.IMyTerminalBlock;
#endif

namespace HDRClientRenderer
{
    // A local consumer is an opaque, current Core slot binding. Its key is a
    // store identity, never a programmable block or a fabricated game entity.
    internal sealed class ModSourceConsumerRegistry
    {
        internal sealed class Descriptor
        {
            internal MyTuple<string,long,long,long,long> Identity;
            internal object Anchor, Declaration;
            internal string Provider, Source;
            internal object[] Sources;
            internal MyTuple<int,int,double,bool> Demand;
            internal object Data;

            internal static Descriptor Read(object[] values)
            {
                if(values==null||values.Length!=10||!(values[0] is int)||(int)values[0]!=1||
                    !(values[1] is string)||(string)values[1]!="local-mod"||
                    !(values[2] is MyTuple<string,long,long,long,long>)||values[3]==null||
                    !(values[4] is string)||!(values[5] is string)||!(values[6] is object[])||
                    !(values[7] is MyTuple<int,int,double,bool>)||values[9]==null)return null;
                var identity=(MyTuple<string,long,long,long,long>)values[2];
                var demand=(MyTuple<int,int,double,bool>)values[7];
                var sources=(object[])values[6];string provider=(string)values[4],source=(string)values[5];
                if(string.IsNullOrEmpty(identity.Item1)||identity.Item1.Length>64||identity.Item2<=0||identity.Item3<=0||
                    identity.Item4<0||identity.Item5<0||!demand.Item4||demand.Item1<16||demand.Item2<16||
                    demand.Item1>4096||demand.Item2>4096||!Finite(demand.Item3)||demand.Item3<.1||demand.Item3>120||
                    sources.Length<1||sources.Length>6||string.IsNullOrEmpty(source))return null;
                foreach(object entity in sources)if(entity==null)return null;
                object data=values[8];
                if(provider=="lcd-texture")
                {
                    long entity;int index;if(sources.Length!=1||data!=null||!LcdId(source,out entity,out index))return null;
                }
                else if(provider=="camera-panorama")
                {
                    long[] cameras;if(!CameraIds(source,out cameras)||cameras.Length!=sources.Length||
                        !(data is MyTuple<MyTuple<double,double,double,int>,int>))return null;
                    var p=(MyTuple<MyTuple<double,double,double,int>,int>)data;var s=p.Item1;
                    if(!Finite(s.Item1)||s.Item1<60||s.Item1>120||!Finite(s.Item2)||s.Item2<0||s.Item2>25||
                        !Finite(s.Item3)||s.Item3<0||s.Item3>2||
                        s.Item4!=256&&s.Item4!=512&&s.Item4!=1024&&s.Item4!=2048||p.Item2<0||p.Item2>1)return null;
                }
                else if(provider=="native-portal")
                {
                    if(source.Length>64||sources.Length!=1||!(data is object[])||((object[])data).Length!=19)return null;
                    data=CopyData((object[])data);if(data==null)return null;
                }
                else return null;
                return new Descriptor{Identity=identity,Anchor=values[3],Provider=provider,Source=source,
                    Sources=(object[])sources.Clone(),Demand=demand,Data=data,Declaration=values[9]};
            }
            static object[] CopyData(object[] values)
            {
                var copy=(object[])values.Clone();
                for(int i=0;i<copy.Length;i++)
                {
                    var doubles=copy[i] as double[];var ints=copy[i] as int[];
                    if(doubles!=null){if(doubles.Length>6144)return null;copy[i]=(double[])doubles.Clone();foreach(double v in doubles)if(!Finite(v))return null;}
                    else if(ints!=null){if(ints.Length>12288)return null;copy[i]=(int[])ints.Clone();}
                    else if(!(copy[i] is int))return null;
                }
                return copy;
            }
            internal bool Same(Descriptor other)
            {
                if(other==null||!Identity.Equals(other.Identity)||!ReferenceEquals(Anchor,other.Anchor)||
                    !ReferenceEquals(Declaration,other.Declaration)||Provider!=other.Provider||Source!=other.Source||
                    Sources.Length!=other.Sources.Length)return false;
                for(int i=0;i<Sources.Length;i++)if(!ReferenceEquals(Sources[i],other.Sources[i]))return false;
                // A declared portal's world matrices follow its real moving
                // anchor. The existing provider validates those matrices and
                // rebuilds the same slot; they do not create a new consumer.
                if(Provider=="native-portal")return true;
                var a=Data as object[];var b=other.Data as object[];
                if(a==null)return Equals(Data,other.Data);
                if(b==null||a.Length!=b.Length)return false;
                for(int i=0;i<a.Length;i++)
                {
                    var aa=a[i] as Array;var bb=b[i] as Array;
                    if(aa==null){if(!Equals(a[i],b[i]))return false;continue;}
                    if(bb==null||aa.GetType()!=bb.GetType()||aa.Length!=bb.Length)return false;
                    for(int j=0;j<aa.Length;j++)if(!Equals(aa.GetValue(j),bb.GetValue(j)))return false;
                }
                return true;
            }
        }
        internal sealed class Consumer
        {
            internal long Key;internal object Binding;internal Descriptor State;internal bool Current=true;
            internal readonly List<Evidence> Leases=new List<Evidence>();
        }
        internal sealed class Evidence
        {
            internal Consumer Consumer;internal object Native;internal bool Released;
        }
        readonly Dictionary<object,Consumer> bindings=new Dictionary<object,Consumer>(ReferenceComparer.Instance);
        readonly Dictionary<long,Consumer> keys=new Dictionary<long,Consumer>();
        readonly HashSet<long> reserved=new HashSet<long>();
        readonly Func<Descriptor,bool> authorize;readonly Func<long,bool> keyAvailable;readonly Action<Evidence> release;
        Func<object,object[]> describe;long serial;
        internal ModSourceConsumerRegistry(Func<Descriptor,bool> authorize,Func<long,bool> keyAvailable,Action<Evidence> release)
        {this.authorize=authorize;this.keyAvailable=keyAvailable;this.release=release;}
        internal void Attach(Func<object,object[]> callback)
        {if(ReferenceEquals(describe,callback))return;Clear();describe=callback;}
        internal Consumer Resolve(object binding)
        {
            if(binding==null||describe==null)return null;
            Consumer prior;bindings.TryGetValue(binding,out prior);Descriptor next=null;
            try{next=Descriptor.Read(describe(binding));if(next!=null&&!authorize(next))next=null;}catch{next=null;}
            if(next==null){if(prior!=null)Remove(prior);return null;}
            if(prior!=null&&prior.State.Same(next)){prior.State=next;return prior;}
            if(prior!=null)Remove(prior);
            if(bindings.Count>=64)return null;
            long key;do{if(serial==long.MaxValue)return null;key=long.MaxValue-++serial;}while(!keyAvailable(key));
            var consumer=new Consumer{Key=key,Binding=binding,State=next};bindings.Add(binding,consumer);keys.Add(key,consumer);reserved.Add(key);return consumer;
        }
        internal bool IsLocalKey(long key){return reserved.Contains(key);}
        internal bool TryKey(long key,out Consumer consumer){return keys.TryGetValue(key,out consumer);}
        internal Consumer Find(long anchor,long key,string provider,string source)
        {
            Consumer found;if(!keys.TryGetValue(key,out found)||!ReferenceEquals(Resolve(found.Binding),found)||
                found.State.Provider!=provider||found.State.Source!=source)return null;
            return found;
        }
        internal Evidence Own(Consumer consumer,object native)
        {
            if(consumer==null||!consumer.Current||native==null)return null;
            foreach(var prior in consumer.Leases)if(!prior.Released&&ReferenceEquals(prior.Native,native))return prior;
            var evidence=new Evidence{Consumer=consumer,Native=native};consumer.Leases.Add(evidence);return evidence;
        }
        internal bool Valid(object binding,Evidence evidence)
        {return evidence!=null&&!evidence.Released&&evidence.Consumer.Current&&ReferenceEquals(binding,evidence.Consumer.Binding)&&ReferenceEquals(Resolve(binding),evidence.Consumer)&&evidence.Consumer.Leases.Contains(evidence);}
        internal bool Release(Evidence evidence)
        {
            if(evidence==null||evidence.Released||evidence.Consumer==null||!evidence.Consumer.Leases.Remove(evidence))return false;
            evidence.Released=true;try{release(evidence);}catch{}return true;
        }
        void Remove(Consumer consumer)
        {consumer.Current=false;bindings.Remove(consumer.Binding);keys.Remove(consumer.Key);foreach(var evidence in consumer.Leases.ToArray())Release(evidence);}
        internal void Prune(){foreach(var consumer in new List<Consumer>(bindings.Values))Resolve(consumer.Binding);}
        internal void Clear(){foreach(var consumer in new List<Consumer>(bindings.Values))Remove(consumer);describe=null;}
        internal static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
        internal static bool LcdId(string id,out long entity,out int index)
        {
            entity=0;index=-1;if(string.IsNullOrEmpty(id)||id.Length>64)return false;int colon=id.IndexOf(':');
            return colon>0&&colon==id.LastIndexOf(':')&&long.TryParse(id.Substring(0,colon),NumberStyles.None,CultureInfo.InvariantCulture,out entity)&&
                int.TryParse(id.Substring(colon+1),NumberStyles.None,CultureInfo.InvariantCulture,out index)&&entity>0&&index>=0&&index<32;
        }
        internal static bool CameraIds(string id,out long[] entities)
        {
            entities=null;if(string.IsNullOrEmpty(id)||id.Length>256)return false;var fields=id.Split(',');if(fields.Length<1||fields.Length>6)return false;
            var ids=new long[fields.Length];for(int i=0;i<ids.Length;i++){if(!long.TryParse(fields[i],NumberStyles.None,CultureInfo.InvariantCulture,out ids[i])||ids[i]<=0)return false;for(int j=0;j<i;j++)if(ids[j]==ids[i])return false;}
            entities=ids;return true;
        }
        sealed class ReferenceComparer:IEqualityComparer<object>
        {internal static readonly ReferenceComparer Instance=new ReferenceComparer();public new bool Equals(object a,object b){return ReferenceEquals(a,b);}public int GetHashCode(object value){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);}}
    }

#if !MOD_SOURCE_CONSUMER_POLICY_TESTS
    public sealed partial class HDRClientRendererPlugin
    {
        ModSourceConsumerRegistry modSourceConsumers;
        Func<string,object[],object> modSourceEndpoint;

        void NegotiateModSourceConsumers()
        {
            if(!registered||hdrService==null)return;
            if(modSourceEndpoint==null)modSourceEndpoint=ModSourceEndpoint;
            if(modSourceConsumers==null)modSourceConsumers=new ModSourceConsumerRegistry(ModSourceAuthorized,
                key=>MyAPIGateway.Entities.GetEntityById(key)==null,ReleaseModSourceEvidence);
            try{modSourceConsumers.Attach(hdrService("local-mod-native-consumers",new object[]{1,modSourceEndpoint}) as Func<object,object[]>);}catch{modSourceConsumers.Attach(null);}
        }
        bool ModSourceAuthorized(ModSourceConsumerRegistry.Descriptor descriptor)
        {
            if(!registered||!Ready()||MyAPIGateway.Session.Player==null||MyAPIGateway.Session.Player.Character==null)return false;
            var player=MyAPIGateway.Session.Player;var anchor=descriptor.Anchor as GameBlock;
            if(player.IdentityId==0||anchor==null||anchor.Closed||!anchor.IsWorking||anchor.OwnerId==0||
                !ReferenceEquals(anchor,MyAPIGateway.Entities.GetEntityById(anchor.EntityId))||!anchor.HasPlayerAccess(player.IdentityId)||
                Vector3D.DistanceSquared(player.Character.GetPosition(),anchor.GetPosition())>3600)return false;
            long[] expected=null;long lcdSource;int index;
            if(descriptor.Provider==ProviderId){if(!textureCopy.Ready||!ModSourceConsumerRegistry.LcdId(descriptor.Source,out lcdSource,out index))return false;expected=new[]{lcdSource};}
            else if(descriptor.Provider==PanoramaProviderId){if(!directCapture.Ready||!panoramaGpu.Ready||!cameraCompatibility.Ready||!ModSourceConsumerRegistry.CameraIds(descriptor.Source,out expected))return false;}
            else if(descriptor.Provider==PortalProviderId){if(portals==null||!portals.Ready)return false;expected=new[]{anchor.EntityId};}
            else return false;
            if(expected.Length!=descriptor.Sources.Length)return false;
            for(int i=0;i<expected.Length;i++)
            {
                var source=descriptor.Sources[i] as GameBlock;
                if(source==null||source.EntityId!=expected[i]||source.Closed||!source.IsWorking||
                    !ReferenceEquals(source,MyAPIGateway.Entities.GetEntityById(expected[i]))||!anchor.IsSameConstructAs(source)||
                    !source.HasPlayerAccess(anchor.OwnerId)||!source.HasPlayerAccess(player.IdentityId)||
                    descriptor.Provider==PanoramaProviderId&&!(source is Sandbox.Game.Entities.MyCameraBlock))return false;
            }
            return true;
        }
        ModSourceConsumerRegistry.Consumer ModSourceCurrent(long anchor,long key,string provider,string source)
        {
            if(modSourceConsumers==null)return null;var consumer=modSourceConsumers.Find(anchor,key,provider,source);
            var block=consumer==null?null:consumer.State.Anchor as GameBlock;return block!=null&&block.EntityId==anchor?consumer:null;
        }
        bool IsModSourceKey(long key){return modSourceConsumers!=null&&modSourceConsumers.IsLocalKey(key);}
        bool ModSourceEntityAuthorized(long anchor,long key,long source,string provider)
        {
            if(modSourceConsumers==null)return false;
            // Find source identity from the registered descriptor, never from a
            // caller-supplied camera/LCD entity ID alone.
            var consumer=modSourceConsumers.Find(anchor,key,provider,ModSourceId(key));
            if(consumer==null||(consumer.State.Anchor as GameBlock).EntityId!=anchor)return false;
            foreach(object entity in consumer.State.Sources){var block=entity as GameBlock;if(block!=null&&block.EntityId==source)return true;}return false;
        }
        string ModSourceId(long key)
        {
            ModSourceConsumerRegistry.Consumer consumer;
            return modSourceConsumers!=null&&modSourceConsumers.TryKey(key,out consumer)?consumer.State.Source:null;
        }
        object ModSourceEndpoint(string command,object[] args)
        {
            if(!registered||hdrService==null||modSourceConsumers==null||gameThreadId!=Thread.CurrentThread.ManagedThreadId||!Ready())return null;
            if(command=="version")return args!=null&&args.Length==0?"HDR.LocalModNative/1":null;
            if(command=="capabilities")
            {if(args==null||args.Length!=0)return null;var kinds=new List<string>();if(textureCopy.Ready)kinds.Add(ProviderId);if(CanCapture&&panoramaGpu.Ready)kinds.Add(PanoramaProviderId);if(portals!=null&&portals.Ready)kinds.Add(PortalProviderId);return kinds.ToArray();}
            if(command=="release")return args!=null&&args.Length==1&&modSourceConsumers.Release(args[0] as ModSourceConsumerRegistry.Evidence);
            if(command=="valid")
            {
                if(args==null||args.Length!=2)return false;var evidence=args[1] as ModSourceConsumerRegistry.Evidence;
                if(!modSourceConsumers.Valid(args[0],evidence))return false;
                var c=evidence.Consumer;var anchor=(GameBlock)c.State.Anchor;bool valid=ModSourceNativeValid(c,anchor.EntityId,evidence.Native);
                if(!valid)modSourceConsumers.Release(evidence);return valid;
            }
            if(command!="frame"||args==null||args.Length!=3||!(args[1] is int)||!(args[2] is int))return null;
            var consumer=modSourceConsumers.Resolve(args[0]);if(consumer==null)return null;
            int width=(int)args[1],height=(int)args[2],requestedWidth=width,requestedHeight=height;var state=consumer.State;long anchorId=((GameBlock)state.Anchor).EntityId;
            object native=null,body=null;bool reduced=false;double rate=state.Demand.Item3;
            if(state.Provider==ProviderId)
            {
                long source;int index;if(!ModSourceConsumerRegistry.LcdId(state.Source,out source,out index)||!LcdStore.BoundSize(width,height,out width,out height)||!ClientPixelBudget.BoundSize(width,height,16,pixelBudget.PixelLimit,out width,out height))return null;
                var lease=lcd.Acquire(anchorId,consumer.Key,state.Source,LocalModScreen(consumer),source,index,width,height);if(lease==null)return null;
                native=lease;body=new MyTuple<string,Vector2I,long>(lease.Material,new Vector2I(lease.Width,lease.Height),lease.Version);rate=Math.Min(rate,60);
            }
            else if(state.Provider==PanoramaProviderId)
            {
                if(!PanoramaStore.BoundSize(width,height,out width,out height)||!ClientPixelBudget.BoundSize(width,height,16,pixelBudget.PixelLimit,out width,out height))return null;
                var lease=panorama.Acquire(anchorId,consumer.Key,state.Source,LocalModScreen(consumer),width,height);if(lease==null)return null;
                native=lease;body=new MyTuple<string,Vector2I,long>(lease.Material,new Vector2I(lease.Width,lease.Height),lease.Version);rate=lease.RefreshRate;
            }
            else if(state.Provider==PortalProviderId)
            {
                var frame=portals.Request(anchorId,consumer.Key,state.Source,LocalModScreen(consumer));if(frame==null)return null;
                native=frame;body=new MyTuple<string,Vector2I,long,Vector4>(frame.Texture,new Vector2I(frame.Bucket,frame.Bucket),frame.Generation,frame.TextureUv);reduced=frame.Reduced;rate=frame.Declaration.Rate;
            }
            if(native==null)return null;
            var owned=modSourceConsumers.Own(consumer,native);if(owned==null)return null;
            return new MyTuple<int,object,object,bool,double>(2,body,owned,reduced||width!=requestedWidth||height!=requestedHeight,rate);
        }
        static string LocalModScreen(ModSourceConsumerRegistry.Consumer consumer){return "local-mod-"+consumer.Key.ToString(CultureInfo.InvariantCulture);}
        bool ModSourceNativeValid(ModSourceConsumerRegistry.Consumer c,long anchor,object native)
        {return c.State.Provider==ProviderId?lcd.Valid(native,anchor,c.Key,c.State.Source):c.State.Provider==PanoramaProviderId?panorama.Valid(native,anchor,c.Key,c.State.Source):c.State.Provider==PortalProviderId&&portals!=null&&portals.Valid(native as PortalProvider.Frame,anchor,c.Key,c.State.Source);}
        void ReleaseModSourceEvidence(ModSourceConsumerRegistry.Evidence evidence)
        {if(evidence.Consumer.State.Provider==ProviderId)lcd.Release(evidence.Native);else if(evidence.Consumer.State.Provider==PanoramaProviderId)panorama.Release(evidence.Native);else if(portals!=null)portals.Release(evidence.Native as PortalProvider.Frame);}
        void LeaveModSourceConsumers()
        {if(modSourceConsumers!=null)modSourceConsumers.Clear();}
    }
#endif
}
