using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage;
using VRage.Utils;
using VRageMath;

namespace HoloMap
{
    public static class DrawClock
    {
        public static double Value(double from,double to,double time,double duration,bool loop,bool ping)
        {
            if(!Geometry.Finite(from)||!Geometry.Finite(to)||!Geometry.Finite(time)||time<0||!Geometry.Finite(duration)||duration<0.001)throw new ArgumentException("Invalid animation time/bounds.");
            double t=time/duration;if(loop){t%=ping?2:1;if(ping&&t>1)t=2-t;}else{t=Math.Min(t,ping?2:1);if(ping&&t>1)t=2-t;}return from+(to-from)*t;
        }
    }
    [ProtoContract] public sealed class HoloAnimationTrack
    {
        [ProtoMember(1)] public string Channel;
        [ProtoMember(2)] public double From;
        [ProtoMember(3)] public double To;
        [ProtoMember(4)] public double Duration;
        [ProtoMember(5)] public double Start;
        [ProtoMember(6)] public bool Loop;
        [ProtoMember(7)] public bool Ping;
    }
    [ProtoContract] public sealed class HoloAnimationData
    {
        [ProtoMember(1)] public long CallerId;
        [ProtoMember(2)] public long ConsoleId;
        [ProtoMember(3)] public string Id;
        [ProtoMember(4)] public double[] Base;
        [ProtoMember(5)] public long StartTick;
        [ProtoMember(6)] public long PauseTick;
        [ProtoMember(7)] public long PausedTicks;
        [ProtoMember(8)] public bool Paused;
        [ProtoMember(9)] public List<HoloAnimationTrack> Tracks=new List<HoloAnimationTrack>();
        [ProtoMember(10)] public string[] Frames;
        [ProtoMember(11)] public string Packed;
        [ProtoMember(12)] public double Fps;
        [ProtoMember(13)] public int Segments;
        [ProtoMember(14)] public bool Loop;
    }
    public sealed partial class HoloMapSession
    {
        sealed class DrawAnimation
        {public DrawContext Context;public IMyTerminalBlock Target;public string Id;public HoloAnimationData Data;}
        readonly Dictionary<string,DrawAnimation> _drawAnimations=new Dictionary<string,DrawAnimation>();
        readonly Dictionary<string,HoloAnimationData> _remoteAnimations=new Dictionary<string,HoloAnimationData>();
        readonly Dictionary<string,PackedAnimation> _clientPackedAnimations=new Dictionary<string,PackedAnimation>();
        long _animationClockOffset;int _animationDecodeTick=-1;readonly HashSet<string> _failedAnimationSources=new HashSet<string>();
        long AnimationNow {get{return MyAPIGateway.Multiplayer.IsServer?_ticks:_ticks+_animationClockOffset;}}
        static double AnimationTime(HoloAnimationData data,long now)
        {return Math.Max(0,Math.Min(1e9,((data.Paused?data.PauseTick:now)-data.StartTick-data.PausedTicks)/60.0));}
        static long AnimationBytes(HoloAnimationData data)
        {long bytes=2048;if(data.Frames!=null)foreach(string frame in data.Frames)bytes+=3L*frame.Length;if(data.Packed!=null)bytes+=3L*data.Packed.Length;return bytes;}
        void ValidateAnimationAdmission(DrawContext context,string id,string[] frames,string packed)
        {
            UiAnimationControlAllowed(context.Caller.EntityId,context.Target.EntityId,id,packed!=null);
            long bytes=32768+UiRules.ReplicationReserve+AnimationReplicationBytes();string key=PackedKey(context.Caller.EntityId,context.Target.EntityId,id);DrawAnimation old;
            if(_drawAnimations.TryGetValue(key,out old))bytes-=AnimationBytes(old.Data);
            var candidate=frames==null&&packed==null&&old!=null?old.Data:new HoloAnimationData{Frames=frames,Packed=packed};bytes+=AnimationBytes(candidate);
            long characters=0;int packedCount=0;
            foreach(var state in _drawAnimations.Values)if(state!=old)
            {if(state.Data.Frames!=null)foreach(string frame in state.Data.Frames)characters+=frame.Length;if(state.Data.Packed!=null){characters+=state.Data.Packed.Length;packedCount++;}}
            if(candidate.Frames!=null)foreach(string frame in candidate.Frames)characters+=frame.Length;if(candidate.Packed!=null){characters+=candidate.Packed.Length;packedCount++;}
            if(characters>480000||packedCount>8)throw new ArgumentException("Global animation source/count budget exceeded.");
            foreach(var scene in _scenes.Values){bytes+=ProjectedSourceBytes(scene);foreach(var item in scene.Items.Values)bytes+=ReplicationItemBytes(item);}
            // Reserve the new first-frame placeholders too. Counting the replaced items is conservative,
            // ensuring a rejected change leaves the existing scene and timeline intact.
            if(frames!=null)bytes+=1024+3L*frames[0].Length;
            if(packed!=null){var table=DecodeServerPacked(packed);for(int i=0;i<table.Parts;i++)bytes+=1024+3L*table.Svg[i].Length;}
            if(bytes>1572864)throw new ArgumentException("Global animation replication budget exceeded.");
        }
        DrawAnimation DrawState(DrawContext context,string id)
        {
            ValidateId(id);string key=PackedKey(context.Caller.EntityId,context.Target.EntityId,id);DrawAnimation state;
            if(!_drawAnimations.TryGetValue(key,out state))
            {
                if(_drawAnimations.Count>=128)throw new ArgumentException("Animation object budget reached.");
                state=new DrawAnimation{Context=context,Target=context.Target,Id=id,Data=new HoloAnimationData{CallerId=context.Caller.EntityId,ConsoleId=context.Target.EntityId,Id=id,Base=MatrixValues(GetItem(context.Caller,context.Target,id).Transform),StartTick=_ticks}};
                _drawAnimations.Add(key,state);
            }
            return state;
        }
        void ResetDrawAnimation(IMyProgrammableBlock caller,IMyTerminalBlock target,string id,MatrixD transform)
        {RemoveDrawAnimation(caller.EntityId,target.EntityId,id);}
        void RemoveDrawAnimation(long caller,long target,string id)
        {if(_drawAnimations.Remove(PackedKey(caller,target,id))){_dirty=true;CleanupClientAnimationCaches();}}
        void StopDrawAnimation(long caller,long target,string id)
        {
            string key=PackedKey(caller,target,id);DrawAnimation state;Scene scene;
            if(!_drawAnimations.TryGetValue(key,out state))return;
            if(_scenes.TryGetValue(target,out scene))
            {
                var frozen=new List<Item>();var keys=new HashSet<string>();
                foreach(var item in scene.Items.Values)if(item.CallerId==caller&&(item.Id==id||state.Data.Packed!=null&&item.Id.StartsWith(id+"~",StringComparison.Ordinal)))
                {frozen.Add(GetAnimatedItem(target,item));keys.Add(Key(item.CallerId,item.Id));}
                ValidateSceneSources(scene,frozen.ToArray(),keys);
                foreach(var item in frozen)scene.Items[Key(item.CallerId,item.Id)]=item;
            }
            RemoveDrawAnimation(caller,target,id);
        }
        void ClearDrawAnimations(long caller,long target)
        {var keys=new List<string>();foreach(var pair in _drawAnimations)if(pair.Value.Data.CallerId==caller&&pair.Value.Data.ConsoleId==target)keys.Add(pair.Key);foreach(string key in keys)_drawAnimations.Remove(key);if(keys.Count>0){_dirty=true;CleanupClientAnimationCaches();}}
        void SetDrawPaused(DrawContext context,bool paused)
        {
            if(context.ScreenId==null)context.Paused=paused;
            foreach(var state in _drawAnimations.Values)if(state.Context==context&&state.Data.Paused!=paused&&(context.ScreenId==null?!IsProjectedId(state.Id):state.Data.ConsoleId==context.Target.EntityId&&state.Id.StartsWith(ScreenPrefix(context.ScreenId),StringComparison.Ordinal)))
            {var data=state.Data;if(paused)data.PauseTick=_ticks;else data.PausedTicks+=Math.Max(0,_ticks-data.PauseTick);data.Paused=paused;_dirty=true;}
        }
        static void ValidateTrack(HoloAnimationTrack track)
        {
            if(track==null||track.Channel==null)throw new ArgumentException("Invalid animation track.");string channel=track.Channel;
            if(channel!="x"&&channel!="y"&&channel!="z"&&channel!="pitch"&&channel!="yaw"&&channel!="rotation"&&channel!="scale"&&channel!="opacity")throw new ArgumentException("Unknown animation channel.");
            double limit=(channel=="x"||channel=="y"||channel=="z")?25:channel=="scale"?1000:1e6;
            if(!Geometry.Finite(track.From)||!Geometry.Finite(track.To)||!Geometry.Finite(track.Duration)||track.Duration<0.001||track.Duration>86400||Math.Abs(track.From)>limit||Math.Abs(track.To)>limit||!Geometry.Finite(track.Start)||track.Start<0||track.Start>1e9
                ||channel=="scale"&&(track.From<1e-6||track.To<1e-6)||channel=="opacity"&&(track.From<0||track.To<0||track.From>1||track.To>1))throw new ArgumentException("Invalid animation bounds/duration.");
        }
        void BeginDrawAnimation(DrawContext context,string id,string channel,double from,double to,double duration,bool loop,bool ping)
        {
            var track=new HoloAnimationTrack{Channel=channel.ToLowerInvariant(),From=from,To=to,Duration=duration,Loop=loop,Ping=ping};ValidateTrack(track);
            ValidateAnimationAdmission(context,id,null,null);var state=DrawState(context,id);SetDrawPaused(context,false);track.Start=AnimationTime(state.Data,_ticks);
            for(int i=state.Data.Tracks.Count-1;i>=0;i--)if(state.Data.Tracks[i].Channel==track.Channel)state.Data.Tracks.RemoveAt(i);
            state.Data.Tracks.Add(track);_dirty=true;
        }
        void BeginDrawFrames(DrawContext context,string id,string[] frames,double fps,MatrixD placement,bool loop,int segments)
        {
            ValidateId(id);ValidateTransform(placement);if(!_drawAnimations.ContainsKey(PackedKey(context.Caller.EntityId,context.Target.EntityId,id))&&_drawAnimations.Count>=128)throw new ArgumentException("Animation object budget reached.");
            if(frames==null||frames.Length<1||frames.Length>300||!Geometry.Finite(fps)||fps<1||fps>30||segments<2||segments>32)throw new ArgumentException("Invalid SVG frame animation.");
            int size=0;foreach(string frame in frames){if(string.IsNullOrEmpty(frame)||frame.Length>Svg.MaxCharacters)throw new ArgumentException("Invalid SVG frame.");size+=frame.Length;if(size>60000)throw new ArgumentException("SVG frame character budget exceeded.");}
            ValidateAnimationAdmission(context,id,frames,null);DrawCheck(PutSvg(context.Caller,context.Target,id,frames[0],new MyTuple<MatrixD,int>(placement,segments)));
            ResetDrawAnimation(context.Caller,context.Target,id,placement);var state=DrawState(context,id);state.Data.Base=MatrixValues(placement);state.Data.Frames=(string[])frames.Clone();state.Data.Fps=fps;state.Data.Segments=segments;state.Data.Loop=loop;SetDrawPaused(context,false);_dirty=true;
        }
        void BeginDrawPacked(DrawContext context,string id,string data,MatrixD placement,int segments)
        {
            if(string.IsNullOrEmpty(data)||data.Length>PackedAnimation.MaxEncodedCharacters)throw new ArgumentException("Packed animation source exceeds its character budget.");
            data=data.Trim();
            ValidateId(id);if(!_drawAnimations.ContainsKey(PackedKey(context.Caller.EntityId,context.Target.EntityId,id))&&_drawAnimations.Count>=128)throw new ArgumentException("Animation object budget reached.");
            ValidateAnimationAdmission(context,id,null,data);DrawCheck(PutPackedAnimationFrame(context.Caller,context.Target,id,data,0,new MyTuple<MatrixD,int>(placement,segments)));
            ResetDrawAnimation(context.Caller,context.Target,id,placement);string key=PackedKey(context.Caller.EntityId,context.Target.EntityId,id);
            var state=new DrawAnimation{Context=context,Target=context.Target,Id=id,Data=new HoloAnimationData{CallerId=context.Caller.EntityId,ConsoleId=context.Target.EntityId,Id=id,Base=MatrixValues(placement),StartTick=_ticks,Packed=data,Segments=segments,Loop=true}};
            _drawAnimations[key]=state;SetDrawPaused(context,false);_dirty=true;
        }
        void TickDrawAnimations()
        {
            if(!MyAPIGateway.Multiplayer.IsServer||_ticks%60!=0)return;var remove=new List<string>();
            foreach(var pair in _drawAnimations)
            {try{var state=pair.Value;Authorize(state.Context.Caller,state.Target);Scene scene;if(!_scenes.TryGetValue(state.Data.ConsoleId,out scene))remove.Add(pair.Key);else{bool found=false;foreach(var item in scene.Items.Values)if(item.CallerId==state.Data.CallerId&&(item.Id==state.Id||state.Data.Packed!=null&&item.Id.StartsWith(state.Id+"~",StringComparison.Ordinal))){found=true;break;}if(!found)remove.Add(pair.Key);}}catch(Exception){remove.Add(pair.Key);}}
            foreach(string key in remove)_drawAnimations.Remove(key);if(remove.Count>0){_dirty=true;CleanupClientAnimationCaches();}
            var expired=new List<long>();foreach(var pair in _drawContexts)if(pair.Value.Caller.Closed||!ReferenceEquals(MyAPIGateway.Entities.GetEntityById(pair.Key),pair.Value.Caller))expired.Add(pair.Key);foreach(long key in expired)_drawContexts.Remove(key);
        }
        long AnimationReplicationBytes()
        {long bytes=0;foreach(var state in _drawAnimations.Values)bytes+=AnimationBytes(state.Data);return bytes;}
        List<HoloAnimationData> ExportDrawAnimations(long consoleId)
        {var list=new List<HoloAnimationData>();foreach(var state in _drawAnimations.Values)if(state.Data.ConsoleId==consoleId)list.Add(CloneAnimationData(state.Data));return list;}
        static HoloAnimationData CloneAnimationData(HoloAnimationData data)
        {
            var copy=new HoloAnimationData{CallerId=data.CallerId,ConsoleId=data.ConsoleId,Id=data.Id,Base=(double[])data.Base.Clone(),StartTick=data.StartTick,PauseTick=data.PauseTick,PausedTicks=data.PausedTicks,Paused=data.Paused,Frames=data.Frames==null?null:(string[])data.Frames.Clone(),Packed=data.Packed,Fps=data.Fps,Segments=data.Segments,Loop=data.Loop};
            foreach(var track in data.Tracks)copy.Tracks.Add(new HoloAnimationTrack{Channel=track.Channel,From=track.From,To=track.To,Duration=track.Duration,Start=track.Start,Loop=track.Loop,Ping=track.Ping});return copy;
        }
        void SynchronizeDrawAnimationScenes(HashSet<long> present)
        {var remove=new List<string>();foreach(var pair in _remoteAnimations)if(!present.Contains(pair.Value.ConsoleId))remove.Add(pair.Key);foreach(string key in remove)_remoteAnimations.Remove(key);CleanupClientAnimationCaches();}
        void ValidateDrawAnimations(List<HoloAnimationData> list,long consoleId)
        {
            if(list==null)return;if(list.Count>128)throw new ArgumentException("Animation descriptor budget exceeded.");var keys=new HashSet<string>();int total=0;
            foreach(var data in list)
            {
                if(data==null||data.ConsoleId!=consoleId||data.CallerId==0)throw new ArgumentException("Invalid animation owner/target.");ValidateId(data.Id);ValidateTransform(ReadMatrix(data.Base));
                if(!keys.Add(PackedKey(data.CallerId,consoleId,data.Id))||data.StartTick<0||data.StartTick>int.MaxValue||data.PauseTick<0||data.PauseTick>int.MaxValue||data.PausedTicks<0||data.PausedTicks>int.MaxValue||data.Paused&&data.PauseTick<data.StartTick||data.Tracks==null||data.Tracks.Count>8)throw new ArgumentException("Invalid animation clock/tracks.");
                var channels=new HashSet<string>();foreach(var track in data.Tracks){ValidateTrack(track);if(!channels.Add(track.Channel))throw new ArgumentException("Duplicate animation channel.");}
                if(data.Frames!=null)
                {if(data.Packed!=null||data.Frames.Length<1||data.Frames.Length>300||!Geometry.Finite(data.Fps)||data.Fps<1||data.Fps>30)throw new ArgumentException("Invalid frame animation.");int size=0;foreach(string frame in data.Frames){if(string.IsNullOrEmpty(frame)||frame.Length>Svg.MaxCharacters)throw new ArgumentException("Invalid SVG frame.");size+=frame.Length;}if(size>60000)throw new ArgumentException("Frame source budget exceeded.");total+=size;}
                if(data.Packed!=null){if(data.Packed.Length>60000||!data.Packed.StartsWith("HoloMapAnimation1:",StringComparison.Ordinal))throw new ArgumentException("Invalid packed animation source.");total+=data.Packed.Length;}
                if((data.Packed!=null||data.Frames!=null)&&(data.Segments<2||data.Segments>32))throw new ArgumentException("Invalid animation curve resolution.");
                if(total>480000)throw new ArgumentException("Scene animation source budget exceeded.");
            }
        }
        void ImportDrawAnimations(List<HoloAnimationData> list,long consoleId)
        {
            var remove=new List<string>();foreach(var pair in _remoteAnimations)if(pair.Value.ConsoleId==consoleId)remove.Add(pair.Key);foreach(string key in remove)_remoteAnimations.Remove(key);
            if(list!=null)foreach(var data in list)_remoteAnimations[PackedKey(data.CallerId,consoleId,data.Id)]=data;
            CleanupClientAnimationCaches();
        }
        void CleanupClientAnimationCaches()
        {
            var sources=new HashSet<string>();
            if(MyAPIGateway.Multiplayer.IsServer){foreach(var state in _drawAnimations.Values)if(state.Data.Packed!=null)sources.Add(state.Data.Packed);}
            else foreach(var data in _remoteAnimations.Values)if(data.Packed!=null)sources.Add(data.Packed);
            var remove=new List<string>();foreach(string source in _clientPackedAnimations.Keys)if(!sources.Contains(source))remove.Add(source);foreach(string source in remove)_clientPackedAnimations.Remove(source);
            remove.Clear();foreach(string source in _failedAnimationSources)if(!sources.Contains(source))remove.Add(source);foreach(string source in remove)_failedAnimationSources.Remove(source);
        }
        void SetAnimationClock(long serverTick)
        {if(serverTick<0||serverTick>int.MaxValue)throw new ArgumentException("Invalid server clock.");_animationClockOffset=serverTick-_ticks;}
        Item CloneAnimationItem(Item item)
        {return new Item{CallerId=item.CallerId,Id=item.Id,Effects=item.Effects,EffectStart=item.EffectStart,EffectEntering=item.EffectEntering,Geometry=item.Geometry,TriangleColors=item.TriangleColors,EdgeColors=item.EdgeColors,Source=item.Source,Declaration=item.Declaration,ContentIdentity=item.ContentIdentity,ClipPlanes=item.ClipPlanes,Gradient=item.Gradient,Emission=item.Emission,UV=item.UV,Material=item.Material,Opacity=item.Opacity,LineColor=item.LineColor,FillColor=item.FillColor,Thickness=item.Thickness,Shaded=item.Shaded,Visible=item.Visible,Layer=item.Layer,Transform=item.Transform,SvgSource=item.SvgSource,SvgSegments=item.SvgSegments,Faces=item.Faces,Contours=item.Contours,EvenOdd=item.EvenOdd,TextSource=item.TextSource,TextAnchor=item.TextAnchor,TextHeight=item.TextHeight};}
        Item GetAnimatedItem(long consoleId,Item item)
        {
            HoloAnimationData data=null;int part=-1;string id=item.Id;string key=PackedKey(item.CallerId,consoleId,id);
            if(MyAPIGateway.Multiplayer.IsServer){DrawAnimation state;if(_drawAnimations.TryGetValue(key,out state))data=state.Data;}else _remoteAnimations.TryGetValue(key,out data);
            if(data==null)
            {
                int separator=id.LastIndexOf('~');if(separator<=0||!int.TryParse(id.Substring(separator+1),out part))return item;
                key=PackedKey(item.CallerId,consoleId,id.Substring(0,separator));
                if(MyAPIGateway.Multiplayer.IsServer){DrawAnimation state;if(_drawAnimations.TryGetValue(key,out state))data=state.Data;}else _remoteAnimations.TryGetValue(key,out data);
                if(data==null||data.Packed==null)return item;
            }
            try
            {
                var result=CloneAnimationItem(item);double time=AnimationTime(data,AnimationNow),x=0,y=0,z=0,pitch=0,yaw=0,roll=0,scale=1;
                foreach(var track in data.Tracks)
                {double value=DrawClock.Value(track.From,track.To,Math.Max(0,time-track.Start),track.Duration,track.Loop,track.Ping);switch(track.Channel){case "x":x=value;break;case "y":y=value;break;case "z":z=value;break;case "pitch":pitch=value;break;case "yaw":yaw=value;break;case "rotation":roll=value;break;case "scale":scale=value;break;case "opacity":result.Opacity=(float)value;break;}}
                result.Transform=MatrixD.CreateScale(scale)*MatrixD.CreateFromYawPitchRoll(yaw,pitch,roll)*ReadMatrix(data.Base)*MatrixD.CreateTranslation(x,y,z);
                ValidateTransform(result.Transform);
                if(data.Frames!=null){int frame=data.Loop?(int)(Math.Floor(time*data.Fps)%data.Frames.Length):(int)Math.Min(data.Frames.Length-1,Math.Floor(time*data.Fps));result.SvgSource=data.Frames[frame];result.SvgSegments=data.Segments;}
                else if(data.Packed!=null)
                {
                    PackedAnimation animation;PackedState serverPacked;if(MyAPIGateway.Multiplayer.IsServer&&_packed.TryGetValue(key,out serverPacked)&&serverPacked.Source==data.Packed)animation=serverPacked.Animation;else if(!_clientPackedAnimations.TryGetValue(data.Packed,out animation)){if(_clientPackedAnimations.Count>=8||_animationDecodeTick==_ticks||_failedAnimationSources.Contains(data.Packed))return item;_animationDecodeTick=_ticks;try{animation=PackedAnimation.Decode(data.Packed);_clientPackedAnimations[data.Packed]=animation;}catch{_failedAnimationSources.Add(data.Packed);throw;}}
                    if(part<0||part>=animation.Parts)return item;result.SvgSource=animation.Svg[animation.FrameAt(time)*animation.Parts+part];result.SvgSegments=data.Segments;
                }
                return result;
            }
            catch(Exception){return item;}
        }
        void ClearClientAnimations(){_remoteAnimations.Clear();_clientPackedAnimations.Clear();_failedAnimationSources.Clear();_animationDecodeTick=-1;_animationClockOffset=0;}
    }
}













