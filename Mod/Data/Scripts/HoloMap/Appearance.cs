using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using PbBlock=Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace HoloMap
{
    public sealed class GradientStyle
    {
        public Vector3D From,To;public Vector4 Start,End;public int Resolution;public bool Radial;
        public Vector4 Sample(Vector3D p)
        {return Palette(Parameter(p));}
        public double Parameter(Vector3D p){var d=To-From;return Radial?(p-From).Length()/d.Length():Vector3D.Dot(p-From,d)/d.LengthSquared();}
        public Vector4 Palette(double t){return MeshEffects.Mix(Start,End,t);}
    }
    public sealed partial class HoloMapSession
    {
        void RegisterAppearanceApi()
        {
            _api.Add("SetEmission",new Func<PbBlock,PbBlock,string,float,MyTuple<bool,string>>(SetEmission));
            _api.Add("SetClip",new Func<PbBlock,PbBlock,string,Vector4[],MyTuple<bool,string>>(SetClip));
            _api.Add("SetGradient",new Func<PbBlock,PbBlock,string,MyTuple<Vector3D,Vector3D,Vector4,Vector4>,MyTuple<int,bool>,MyTuple<bool,string>>(SetGradient));
            _api.Add("PutContours",new Func<PbBlock,PbBlock,string,Vector3D[][],MyTuple<Vector4,Vector4,float,bool>,bool,MyTuple<bool,string>>(PutContours));
            _api.Add("PutText",new Func<PbBlock,PbBlock,string,string,Vector4,MyTuple<double,MatrixD,string>,MyTuple<bool,string>>(PutText));
        }
        static void EmissionValue(float value){if(!Geometry.Finite(value)||value<0||value>20)throw new ArgumentException("Emission must be finite, 0–20.");}
        MyTuple<bool,string> SetAppearance(PbBlock caller,PbBlock console,string id,float value,bool emission)
        {
            return Guard(()=>
            {
                var target=Authorize(caller,console);ValidateWriteId(caller,console,id);if(emission)EmissionValue(value);else LayerRules.Opacity(value);
                Scene scene;Item item;Label label;if(!_scenes.TryGetValue(target.EntityId,out scene))throw new ArgumentException("Object ID does not exist.");bool found=false;string key=Key(caller.EntityId,id);
                if(scene.Items.TryGetValue(key,out item)){if(emission)item.Emission=value;else item.Opacity=value;found=true;}
                if(scene.Labels.TryGetValue(key,out label)){if(emission)label.Emission=value;else label.Opacity=value;found=true;}
                PackedState packed;if(_packed.TryGetValue(PackedKey(caller.EntityId,target.EntityId,id),out packed))
                    for(int i=0;i<packed.Animation.Parts;i++)if(scene.Items.TryGetValue(Key(caller.EntityId,id+"~"+i),out item)){if(emission)item.Emission=value;else item.Opacity=value;found=true;}
                if(!found)throw new ArgumentException("Object ID does not exist for this PB.");
            });
        }
        MyTuple<bool,string> SetEmission(PbBlock caller,PbBlock console,string id,float value){return SetAppearance(caller,console,id,value,true);}
        static SurfaceMesh StyledMesh(SurfaceMesh source,Vector4[] clip,GradientStyle gradient)
        {var mesh=MeshEffects.Clip(source,clip);if(gradient!=null)mesh=gradient.Radial?MeshEffects.RadialGradient(mesh,p=>(p-gradient.From)/(gradient.To-gradient.From).Length(),gradient.Palette,gradient.Resolution):MeshEffects.LinearGradient(mesh,gradient.Parameter,gradient.Palette,gradient.Resolution*8);return mesh;}
        SurfaceMesh SourceMesh(Item item){return item.Source??MeshEffects.Solid(item.Geometry,item.FillColor,item.LineColor,item.TriangleColors,item.EdgeColors,item.UV);}
        void ApplyStyles(PbBlock caller,PbBlock console,string id,Vector4[] planes,GradientStyle gradient,bool clipping)
        {
            var target=Authorize(caller,console);ValidateWriteId(caller,console,id);Scene scene;
            if(!_scenes.TryGetValue(target.EntityId,out scene))throw new ArgumentException("Object ID does not exist.");
            var items=new List<Item>();Item single;if(scene.Items.TryGetValue(Key(caller.EntityId,id),out single))items.Add(single);
            PackedState packed;if(_packed.TryGetValue(PackedKey(caller.EntityId,target.EntityId,id),out packed))for(int i=0;i<packed.Animation.Parts;i++)if(scene.Items.TryGetValue(Key(caller.EntityId,id+"~"+i),out single))items.Add(single);
            if(items.Count==0)throw new ArgumentException("Use geometry or PutText for clipping/gradients; PutLabel supports opacity and emission.");
            foreach(var item in items)
            { if(clipping)item.ClipPlanes=planes;else item.Gradient=gradient; }

        }
        MyTuple<bool,string> SetClip(PbBlock caller,PbBlock console,string id,Vector4[] planes)
        {return Guard(()=>{MeshEffects.ValidatePlanes(planes);ApplyStyles(caller,console,id,planes==null?null:(Vector4[])planes.Clone(),null,true);});}
        MyTuple<bool,string> SetGradient(PbBlock caller,PbBlock console,string id,MyTuple<Vector3D,Vector3D,Vector4,Vector4> paint,MyTuple<int,bool> options)
        {
            return Guard(()=>
            {
                GradientStyle gradient=null;
                if(options.Item1!=0){Geometry.ValidatePoints(new[]{paint.Item1,paint.Item2});Color(paint.Item3);Color(paint.Item4);if((paint.Item2-paint.Item1).LengthSquared()<1e-16||options.Item1<1||options.Item1>8)throw new ArgumentException("Gradient requires distinct endpoints and resolution 1–8 (0 clears).");gradient=new GradientStyle{From=paint.Item1,To=paint.Item2,Start=paint.Item3,End=paint.Item4,Resolution=options.Item1,Radial=options.Item2};}
                ApplyStyles(caller,console,id,null,gradient,false);
            });
        }
        MyTuple<bool,string> PutContours(PbBlock caller,PbBlock console,string id,Vector3D[][] contours,MyTuple<Vector4,Vector4,float,bool> style,bool evenOdd)
        {return Guard(()=>{var target=Authorize(caller,console);ValidateWriteId(caller,console,id);var copied=ValidateContours(contours);var points=new List<Vector3D>();foreach(var c in copied)points.AddRange(c);ValidateSceneSources(GetScene(target.EntityId),new[]{new Item{Geometry=new Geometry(points.ToArray(),new int[0],new int[0]),Contours=copied}},new HashSet<string>{Key(caller.EntityId,id)});Put(caller,target,id,new Geometry(points.ToArray(),new int[0],new int[0]),style.Item1,style.Item2,style.Item3,style.Item4);var item=_scenes[target.EntityId].Items[Key(caller.EntityId,id)];item.Contours=copied;item.EvenOdd=evenOdd;});}
        MyTuple<bool,string> PutText(PbBlock caller,PbBlock console,string id,string text,Vector4 color,MyTuple<double,MatrixD,string> placement)
        {return Guard(()=>{var target=Authorize(caller,console);ValidateWriteId(caller,console,id);ValidateText(text,placement.Item1,placement.Item3);ValidateSceneSources(GetScene(target.EntityId),new[]{new Item{Geometry=EmptyDisplayGeometry(),TextSource=text}},new HashSet<string>{Key(caller.EntityId,id)});_uiDeclarationWrite++;try{Put(caller,target,id,EmptyDisplayGeometry(),Vector4.Zero,color,0.008f,false,placement.Item2);}finally{_uiDeclarationWrite--;}var item=_scenes[target.EntityId].Items[Key(caller.EntityId,id)];item.TextSource=text;item.TextHeight=placement.Item1;item.TextAnchor=placement.Item3;NotifyUiArtworkChanged(caller.EntityId,target.EntityId,id,false);});}
    }
}
