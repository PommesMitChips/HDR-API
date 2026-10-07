using System;
using ProtoBuf;
using VRage;
using VRageMath;
using PbBlock=Sandbox.ModAPI.Ingame.IMyTerminalBlock;
namespace HoloMap
{
    [ProtoContract] public sealed class HoloEffectDeclaration
    {
        [ProtoMember(1)] public double[] Values;
        [ProtoMember(2)] public int[] Flags;
        [ProtoMember(3)] public double Start;
        [ProtoMember(4)] public bool Entering=true;
    }
    public sealed partial class HoloMapSession
    {
        bool _effectDrawActive;double _effectDrawNow;
        double HologramEffectNow {get{return _effectDrawActive?_effectDrawNow:AnimationNow/60d;}}
        void RegisterHologramEffectsApi()
        {
            _api.Add("SetHologramEffects",new Func<PbBlock,PbBlock,string,MyTuple<double[],int[]>,MyTuple<bool,string>>(SetHologramEffects));
            _api.Add("GetHologramEffects",new Func<PbBlock,PbBlock,string,MyTuple<double[],int[]>>(GetHologramEffects));
            _api.Add("ClearHologramEffects",new Func<PbBlock,PbBlock,string,MyTuple<bool,string>>(ClearHologramEffects));
            _api.Add("SetHologramTransition",new Func<PbBlock,PbBlock,string,bool,string,double,MyTuple<bool,string>>(SetHologramTransition));
        }
        static string[] HologramEffectTypes(){return new[]{"flicker","scan","volume","particles","beams","rays","budget","raw"};}
        static HoloEffectDeclaration ExportHologramEffects(Item item)
        {return item.Effects==null?null:new HoloEffectDeclaration{Values=item.Effects.Values(),Flags=item.Effects.Flags(),Start=item.EffectStart,Entering=item.EffectEntering};}
        static void ImportHologramEffects(Item item,HoloEffectDeclaration declaration)
        {
            if(declaration==null)return;
            if(!Geometry.Finite(declaration.Start)||declaration.Start<0||declaration.Start>1e9)throw new ArgumentException("Invalid hologram effect start time.");
            item.Effects=HologramEffectSettings.Create(declaration.Values,declaration.Flags);item.EffectStart=declaration.Start;item.EffectEntering=declaration.Entering;
        }
        MyTuple<bool,string> SetHologramEffects(PbBlock caller,PbBlock target,string id,MyTuple<double[],int[]> descriptor)
        {return Guard(()=>{var block=Authorize(caller,target);ValidateWriteId(caller,target,id);var settings=HologramEffectSettings.Create(descriptor.Item1,descriptor.Item2);var item=GetItem(caller,block,id);ValidateEffectTarget(block,item,settings);item.Effects=settings;_dirty=true;});}
        MyTuple<double[],int[]> GetHologramEffects(PbBlock caller,PbBlock target,string id)
        {var block=Authorize(caller,target);ValidateWriteId(caller,target,id);var item=GetItem(caller,block,id);return new MyTuple<double[],int[]>(item.Effects==null?HologramEffectSettings.DefaultValues():item.Effects.Values(),item.Effects==null?HologramEffectSettings.DefaultFlags():item.Effects.Flags());}
        MyTuple<bool,string> ClearHologramEffects(PbBlock caller,PbBlock target,string id)
        {return Guard(()=>{var block=Authorize(caller,target);ValidateWriteId(caller,target,id);var item=GetItem(caller,block,id);item.Effects=null;item.EffectStart=0;item.EffectEntering=true;_dirty=true;});}
        MyTuple<bool,string> SetHologramTransition(PbBlock caller,PbBlock target,string id,bool entering,string style,double duration)
        {return Guard(()=>{var block=Authorize(caller,target);ValidateWriteId(caller,target,id);var item=GetItem(caller,block,id);var current=item.Effects;var values=current==null?HologramEffectSettings.DefaultValues():current.Values();var flags=current==null?HologramEffectSettings.DefaultFlags():current.Flags();flags[5]=HologramTransitionStyle(style);values[16]=duration;var settings=HologramEffectSettings.Create(values,flags);ValidateEffectTarget(block,item,settings);item.Effects=settings;item.EffectStart=AnimationNow/60d;item.EffectEntering=entering;_dirty=true;});}
        static int HologramTransitionStyle(string style)
        {if(style=="fade")return 1;if(style=="wipe")return 2;if(style=="dissolve")return 3;throw new ArgumentException("Transition style must be fade, wipe or dissolve.");}
        void ValidateEffectTarget(PbBlock target,Item item,HologramEffectSettings settings)
        {
            if(target is Sandbox.ModAPI.Ingame.IMyTextPanel&&(settings.DepthLayers>0||settings.Beams>0))throw new ArgumentException("LCD effects support flicker, scan, particles and transitions; volume and projection beams require a world display.");
            if(IsProjectedId(item.Id))foreach(var screen in GetScene(target.EntityId).Screens.Values)
                if(screen.Data.CallerId==item.CallerId&&item.Id.StartsWith(ScreenPrefix(screen.Data.Id),StringComparison.Ordinal))ValidateProjectedHologramEffects(settings,screen.Data);
        }
        static void ValidateProjectedHologramEffects(HologramEffectSettings settings,HoloProjectedScreenData screen)
        {
            if(settings==null)return;
            if(screen.ContentRenderer==1&&(settings.DepthLayers>0||settings.Beams>0))throw new ArgumentException("Raster projected artwork supports flicker, scan, particles and transitions; volume and projection rays require flat vector/world rendering.");
            if(screen.ContentRenderer==0&&screen.SurfaceKind!=0&&(settings.ScanStrength>0||settings.ScanBoost>0||settings.Transition==2||settings.Particles>0||settings.DepthLayers>0||settings.Beams>0))throw new ArgumentException("Curved vector artwork supports flicker, fade and dissolve; use raster artwork for scan, wipe and particles, or flat vector/world rendering for volume and projection rays.");
        }
        static HologramEffectSettings ClearHologramEffect(HologramEffectSettings current,string type)
        {
            if(current==null)return null;var v=current.Values();var f=current.Flags();
            switch(type){case "flicker":v[0]=0;break;case "scan":v[2]=v[5]=0;break;case "volume":f[2]=0;v[6]=0;break;case "particles":f[3]=0;break;case "beams":case "rays":f[4]=0;break;case "transition":f[5]=0;break;default:throw new ArgumentException("Unknown hologram effect: "+type);}
            return HologramEffectSettings.Create(v,f);
        }
        static HologramEffectSettings ParseHologramEffect(HologramEffectSettings current,string type,DrawArgs a)
        {
            var v=current==null?HologramEffectSettings.DefaultValues():current.Values();var f=current==null?HologramEffectSettings.DefaultFlags():current.Flags();
            switch(type)
            {
                case "flicker":v[0]=a.Number(.08);v[1]=a.Number(12);break;
                case "scan":v[2]=a.Number(.35);v[3]=a.Number(.3);v[4]=a.Number(.04);v[5]=a.Number(.5);break;
                case "volume":v[6]=a.Number(.04);f[2]=a.Integer(4);v[7]=a.Number(.5);break;
                case "particles":f[3]=a.Integer(32);v[11]=a.Number(.008);v[9]=a.Number(.15);v[10]=a.Number(2);v[8]=a.Number(.05);v[12]=a.Number(.5);f[1]=a.Integer(1);break;
                case "beams":case "rays":f[4]=a.Integer(8);v[13]=a.Number(type=="rays"?.03:.003);v[14]=a.Number(type=="rays"?.12:.3);var p=a.Has?a.Point():Vector3D.Zero;v[21]=p.X;v[22]=p.Y;v[23]=p.Z;v[15]=a.Number(0);break;
                case "budget":f[7]=a.Integer(4096);break;
                case "raw":if(a.Peek is MyTuple<double[],int[]>){var tuple=a.Typed<MyTuple<double[],int[]>>();v=tuple.Item1;f=tuple.Item2;}else{v=a.Typed<double[]>();f=a.Typed<int[]>();}break;
                default:throw new ArgumentException("Unknown hologram effect: "+type);
            }
            return HologramEffectSettings.Create(v,f);
        }
    }
}
