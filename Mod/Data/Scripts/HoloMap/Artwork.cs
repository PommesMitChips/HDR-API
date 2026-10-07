using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using PbBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        void RegisterArtworkApi()
        {
            _api.Add("PutSvg", new Func<PbBlock,PbBlock,string,string,MyTuple<MatrixD,int>,MyTuple<bool,string>>(PutSvg));
            _api.Add("PutSvgAsset", new Func<PbBlock,PbBlock,string,string,MyTuple<MatrixD,int>,MyTuple<bool,string>>(PutSvgAsset));
            _api.Add("PutImage", new Func<PbBlock,PbBlock,string,string,Vector2,Vector4,MyTuple<bool,string>>(PutImage));
            _api.Add("SetOpacity", new Func<PbBlock,PbBlock,string,float,MyTuple<bool,string>>(SetOpacity));
            _api.Add("PutPackedAnimationFrame", new Func<PbBlock,PbBlock,string,string,double,MyTuple<MatrixD,int>,MyTuple<bool,string>>(PutPackedAnimationFrame));
        }
        void PutSvgCore(PbBlock caller, IMyTerminalBlock target, string id, string text, MyTuple<MatrixD,int> options)
        {
            ValidateTransform(options.Item1);
            ValidateSvgSource(text,options.Item2);
            var scene=GetScene(target.EntityId);var replacing=new HashSet<string>{Key(caller.EntityId,id)};PackedState oldPacked;if(_packed.TryGetValue(PackedKey(caller.EntityId,target.EntityId,id),out oldPacked))for(int part=0;part<oldPacked.Animation.Parts;part++)replacing.Add(Key(caller.EntityId,id+"~"+part));ValidateSceneSources(scene,new[]{new Item{Geometry=EmptyDisplayGeometry(),SvgSource=text}},replacing);
            var saved=new Dictionary<string,Item>();foreach(var key in replacing){Item value;if(key!=Key(caller.EntityId,id)&&scene.Items.TryGetValue(key,out value)){saved.Add(key,value);scene.Items.Remove(key);}}
            _uiDeclarationWrite++;try{Put(caller,target,id,EmptyDisplayGeometry(),Vector4.Zero,Vector4.One,0.008f,false,options.Item1);}
            catch{foreach(var pair in saved)scene.Items[pair.Key]=pair.Value;throw;}
            finally{_uiDeclarationWrite--;}
            var item=_scenes[target.EntityId].Items[Key(caller.EntityId,id)];item.SvgSource=text;item.SvgSegments=options.Item2;
            NotifyUiArtworkChanged(caller.EntityId,target.EntityId,id,false);
            RemovePacked(caller.EntityId,target.EntityId,id);
        }
        MyTuple<bool,string> PutSvg(PbBlock caller,PbBlock console,string id,string text,MyTuple<MatrixD,int> options)
        { return Guard(() => { var target=Authorize(caller,console);ValidateWriteId(caller,console,id);PutSvgCore(caller,target,id,text,options); }); }
        MyTuple<bool,string> PutSvgAsset(PbBlock caller,PbBlock console,string id,string asset,MyTuple<MatrixD,int> options)
        {
            return Guard(() =>
            {
                var target=Authorize(caller,console);ValidateWriteId(caller,console,id);AssetName(asset);
                string text;
                using(var reader=MyAPIGateway.Utilities.ReadFileInModLocation("Data/Svg/"+asset+".svg",ModContext.ModItem))
                {
                    var buffer=new char[Svg.MaxCharacters+1];int count=0,n;
                    while(count<buffer.Length&&(n=reader.Read(buffer,count,buffer.Length-count))>0)count+=n;
                    if(count>Svg.MaxCharacters)throw new ArgumentException("SVG asset exceeds the character budget.");
                    text=new string(buffer,0,count);
                }
                PutSvgCore(caller,target,id,text,options);
            });
        }
        static void AssetName(string name)
        {
            if(string.IsNullOrEmpty(name)||name.Length>48)throw new ArgumentException("Asset name requires 1–48 letters, digits, underscores or hyphens.");
            foreach(char c in name)if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9')||c=='_'||c=='-'))throw new ArgumentException("Invalid asset name; paths/URLs are not accepted.");
        }
        static string ImageMaterial(string asset)
        {
            AssetName(asset);string name="HoloMap_Image_"+asset;
            foreach(var definition in Sandbox.Definitions.MyDefinitionManager.Static.GetTransparentMaterialDefinitions())
                if(definition.Id.SubtypeName==name)return name;
            throw new ArgumentException("Image asset is not registered: "+name);
        }
        MyTuple<bool,string> PutImage(PbBlock caller,PbBlock console,string id,string asset,Vector2 size,Vector4 tint)
        {
            return Guard(() =>
            {
                var target=Authorize(caller,console);ValidateWriteId(caller,console,id);string material=ImageMaterial(asset);
                if(!Geometry.Finite(size.X)||!Geometry.Finite(size.Y)||size.X<=0||size.Y<=0||size.X>50||size.Y>50)throw new ArgumentException("Image size must be finite and positive, at most 50 model units.");
                double x=size.X/2,y=size.Y/2;
                var geometry=new Geometry(new[]{new Vector3D(-x,y,0),new Vector3D(x,y,0),new Vector3D(x,-y,0),new Vector3D(-x,-y,0)},new int[0],new[]{0,1,2,0,2,3});
                Put(caller,target,id,geometry,Vector4.Zero,tint,0.008f,false,null,null,
                    new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)},material);
            });
        }
        MyTuple<bool,string> SetOpacity(PbBlock caller,PbBlock console,string id,float opacity)
        { return SetAppearance(caller,console,id,opacity,false); }

        sealed class PackedState
        {
            public long CallerId,ConsoleId; public string Id,Source;
            public PackedAnimation Animation; public int Frame=-1,Segments;
            public SvgMesh[] Meshes;
        }
        readonly Dictionary<string,PackedState> _packed=new Dictionary<string,PackedState>();
        static string PackedKey(long caller,long console,string id){return console+":"+Key(caller,id);}
        MyTuple<bool,string> PutPackedAnimationFrame(PbBlock caller,PbBlock console,string id,string data,double time,MyTuple<MatrixD,int> options)
        {
            return Guard(() =>
            {
                var target=Authorize(caller,console);ValidateWriteId(caller,console,id);
                if(id.Length>48)throw new ArgumentException("Animation ID must be at most 48 characters.");
                ValidateTransform(options.Item1);
                if(options.Item2<2||options.Item2>32)throw new ArgumentException("Animation curve segments must be 2–32.");
                if(!Geometry.Finite(time)||time<0||time>1e9)throw new ArgumentException("Invalid animation time.");
                string key=PackedKey(caller.EntityId,target.EntityId,id);PackedState previous;
                _packed.TryGetValue(key,out previous);
                var state=previous;
                if(state==null||state.Source!=data)
                {
                    if(state==null&&_packed.Count>=8)throw new ArgumentException("At most eight decoded animations may be active.");
                    state=new PackedState{CallerId=caller.EntityId,ConsoleId=target.EntityId,Id=id,Source=data,Animation=DecodeServerPacked(data)};
                }
                int frame=state.Animation.FrameAt(time);
                var scene=GetScene(target.EntityId);int objects=state.Animation.Parts;
                var replaced=new HashSet<string>{Key(caller.EntityId,id)};
                for(int part=0;part<state.Animation.Parts;part++)replaced.Add(Key(caller.EntityId,id+"~"+part));
                if(previous!=null)for(int part=0;part<previous.Animation.Parts;part++)replaced.Add(Key(caller.EntityId,id+"~"+part));
                foreach(var entry in scene.Items)if(!replaced.Contains(entry.Key))objects++;
                if(objects>MaxObjects)throw new ArgumentException("Animation exceeds Console object budget.");
                var prepared=new Item[state.Animation.Parts];
                for(int part=0;part<prepared.Length;part++)
                {
                    string text=state.Animation.Svg[frame*prepared.Length+part];ValidateSvgSource(text,options.Item2);
                    string itemId=id+"~"+part;Item prior;scene.Items.TryGetValue(Key(caller.EntityId,itemId),out prior);
                    prepared[part]=new Item{CallerId=caller.EntityId,Id=itemId,Geometry=EmptyDisplayGeometry(),SvgSource=text,SvgSegments=options.Item2,
                        FillColor=Vector4.One,LineColor=Vector4.Zero,Thickness=0.008f,Transform=options.Item1,Visible=prior==null||prior.Visible,
                        Layer=prior==null?"":prior.Layer,Opacity=prior==null?1:prior.Opacity,Emission=prior==null?0:prior.Emission,
                        ClipPlanes=prior==null?null:prior.ClipPlanes,Gradient=prior==null?null:prior.Gradient,
                        Effects=prior==null?null:prior.Effects,EffectStart=prior==null?0:prior.EffectStart,EffectEntering=prior==null||prior.EffectEntering};
                }
                ValidateSceneSources(scene,prepared,replaced);
                scene.Items.Remove(Key(caller.EntityId,id));
                if(previous!=null)for(int part=prepared.Length;part<previous.Animation.Parts;part++)scene.Items.Remove(Key(caller.EntityId,id+"~"+part));
                for(int part=0;part<prepared.Length;part++)scene.Items[Key(caller.EntityId,id+"~"+part)]=prepared[part];
                for(int part=0;part<prepared.Length;part++)NotifyUiArtworkChanged(caller.EntityId,target.EntityId,id+"~"+part,false);
                if(target is IMyTextPanel)scene.LcdCallerId=caller.EntityId;
                state.Meshes=null;state.Frame=frame;state.Segments=options.Item2;_packed[key]=state;
            });
        }
        void RemovePacked(long caller,long console,string id)
        {
            string key=PackedKey(caller,console,id);PackedState state;
            if(!_packed.TryGetValue(key,out state))return;
            Scene scene;if(_scenes.TryGetValue(console,out scene))for(int part=0;part<state.Animation.Parts;part++)scene.Items.Remove(Key(caller,id+"~"+part));
            _packed.Remove(key);
        }
        void ClearPacked(long caller,long console)
        {
            var keys=new List<string>();foreach(var pair in _packed)if(pair.Value.CallerId==caller&&pair.Value.ConsoleId==console)keys.Add(pair.Key);
            foreach(string key in keys)_packed.Remove(key);
        }
        void CleanupPacked()
        {
            var keys=new List<string>();foreach(var pair in _packed)
            {
                Scene scene;bool retained=false;
                if(_scenes.TryGetValue(pair.Value.ConsoleId,out scene))for(int part=0;part<pair.Value.Animation.Parts;part++)
                    if(scene.Items.ContainsKey(Key(pair.Value.CallerId,pair.Value.Id+"~"+part))){retained=true;break;}
                if(!retained)keys.Add(pair.Key);
            }
            foreach(string key in keys)_packed.Remove(key);
        }
    }
}
