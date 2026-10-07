using System;
using System.Collections.Generic;
using System.Reflection;
using HoloMap;
using VRage;
using VRageMath;

internal static class ModClientEffectTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new Exception("Mod client effects: "+label);checks++;}
        void Reject(Action action,string label){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Expected mod effect rejection: "+label);}
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(HoloMapSession);var session=new HoloMapSession();
        void Field(string name,object value){type.GetField(name,flags).SetValue(session,value);}
        Field("_modClientActive",true);Field("_modClientEffectOfflineTime",0d);
        var service=(Func<string,object[],object>)Delegate.CreateDelegate(typeof(Func<string,object[],object>),session,type.GetMethod("ModClientService",flags));
        var endpoint=(Func<string,object[],object>)service("open",new object[]{"effects-test"});
        long world=(long)endpoint("create-world",new object[]{0,MatrixD.Identity}),hud=(long)endpoint("create-hud",new object[]{0});
        var source=new[]{new Vector3D(-1,-1,0),new Vector3D(1,-1,0),new Vector3D(1,1,0),new Vector3D(-1,1,0)};var triangles=new[]{0,1,2,0,2,3};var tint=new Vector4(.1f,.7f,1,.8f);
        endpoint("mesh",new object[]{world,"panel",source,triangles,tint});endpoint("mesh",new object[]{hud,"panel",source,triangles,tint});
        var owners=(Dictionary<string,ModClientOwner>)type.GetField("_modClientOwners",flags).GetValue(session);var context=owners["effects-test"].Contexts[world];var item=context.Items["panel"];
        var geometry=item.Geometry;var immutablePoints=geometry.Points;var immutableTriangles=geometry.Triangles;var worldScratch=geometry.WorldPoints;
        var submitted=new List<ModClientTriangleSubmission>();Field("_modClientOfflineSubmit",new Action<ModClientTriangleSubmission>(s=>submitted.Add(s)));
        var viewport=new Vector2(1920,1080);var projection=MatrixD.CreatePerspectiveFieldOfView(1.1,viewport.X/viewport.Y,.1,1000);var camera=MatrixD.CreateTranslation(0,0,5);
        var draw=type.GetMethod("DrawModClientItem",flags);var extras=type.GetMethod("DrawModClientEffects",flags);
        int Draw(ModClientContext c,ModClientItem i,int grant,double now,bool fx=true)
        {
            Field("_modClientEffectNow",now);object[] args={c,i,viewport,MatrixD.Invert(projection),camera,camera.Translation,grant};draw.Invoke(session,args);if(fx)extras.Invoke(session,args);return (int)args[6];
        }
        endpoint("effect",new object[]{world,"panel","flicker",.3,12});endpoint("effect",new object[]{world,"panel","volume",.1,4,.6});
        var settings=item.Effects;double started=item.EffectStart;
        Reject(()=>endpoint("effect",new object[]{world,"panel","flicker",double.NaN,12}),"NaN effect value");
        Reject(()=>endpoint("effect",new object[]{world,"panel","particles",999999}),"unbounded particle count");
        Reject(()=>endpoint("transition",new object[]{world,"panel","out","unsupported",1}),"transition style");
        Check(ReferenceEquals(settings,item.Effects)&&item.EffectStart==started,"invalid configuration is atomic");
        Reject(()=>endpoint("effect",new object[]{hud,"panel","volume",.1,2,.5}),"HUD volume requires world context");
        Reject(()=>endpoint("effect",new object[]{hud,"panel","beams",4,.01,.5,Vector3D.Zero,0}),"HUD rays require world context");
        Reject(()=>endpoint("effect",new object[]{world,"missing","flicker"}),"unknown effect target");
        endpoint("effect",new object[]{world,"panel","budget",100});
        int remaining=Draw(context,item,100,.1);Check(submitted.Count==10&&remaining==90,"four ghost shells and source are billed exactly");
        Check(ReferenceEquals(geometry,item.Geometry)&&ReferenceEquals(immutablePoints,item.Geometry.Points)&&ReferenceEquals(immutableTriangles,item.Geometry.Triangles)&&ReferenceEquals(worldScratch,item.Geometry.WorldPoints)&&item.Color==tint,"source mesh and colours retained by identity");
        Check(source[0]==immutablePoints[0]&&immutablePoints[0].Z==0,"shells do not edit source vertices");
        bool front=false,back=false;foreach(var triangle in submitted){front|=triangle.A.Z>0;back|=triangle.A.Z<0;Check(ModClientRules.SafePoint(triangle.A-camera.Translation,1000000)&&Geometry.Finite(triangle.Normal.X)&&triangle.Color.W>=0&&triangle.Color.W<=1,"finite depth submission");}
        Check(front&&back,"volume exists on both sides of source plane");
        var snapshot=submitted.ToArray();submitted.Clear();Draw(context,item,100,.1);
        Check(submitted.Count==snapshot.Length,"frozen effect frame count");for(int i=0;i<snapshot.Length;i++)Check(submitted[i].A==snapshot[i].A&&submitted[i].Color==snapshot[i].Color,"frozen-time deterministic frame");
        submitted.Clear();endpoint("effect",new object[]{world,"panel","budget",1});remaining=Draw(context,item,20,.1);Check(submitted.Count==2&&remaining==18,"effect ceiling never hides accepted source content");
        endpoint("effect-clear",new object[]{world,"panel","volume"});Check(item.Effects.DepthLayers==0&&item.Effects.FlickerStrength==.3,"selective clearing preserves other effects");
        endpoint("effect-clear",new object[]{world,"panel"});Check(item.Effects==null,"clear all effects");
        // Transition lifetime comes from one retained simulation clock; configuration is not a draw call.
        Field("_modClientEffectOfflineTime",2d);endpoint("transition",new object[]{world,"panel","in","fade",1d});Check(item.EffectStart==2&&item.EffectEntering,"transition uses retained local start");
        submitted.Clear();Draw(context,item,20,2.5,false);Check(submitted.Count==2&&Math.Abs(submitted[0].Color.W-.4)<1e-6,"fade midpoint colour sampled by actual sink");
        Field("_modClientEffectOfflineTime",3d);endpoint("transition",new object[]{world,"panel","out","fade",1d});submitted.Clear();remaining=Draw(context,item,20,4);Check(submitted.Count==0&&remaining==20&&context.Items.ContainsKey("panel"),"completed exit is invisible without deleting item");
        endpoint("effect-clear",new object[]{world,"panel"});endpoint("effect",new object[]{world,"panel","particles",12,.02,.1,2,.05,.5,42});
        submitted.Clear();remaining=Draw(context,item,26,4.37);Check(submitted.Count>2&&submitted.Count<=26&&26-remaining==submitted.Count,"particles execute within actual triangle grant");
        var all=submitted.ToArray();submitted.Clear();remaining=Draw(context,item,6,4.37);Check(submitted.Count<=6&&6-remaining==submitted.Count,"reduced particle grant billed exactly");
        for(int i=0;i<submitted.Count;i++)Check(submitted[i].A==all[i].A&&submitted[i].Color==all[i].Color,"reduced grant retains deterministic prefix");
        submitted.Clear();endpoint("effect-clear",new object[]{world,"panel"});endpoint("effect",new object[]{world,"panel","beams",4,.02,.2,new Vector3D(0,0,-1),.01});
        remaining=Draw(context,item,10,5);Check(submitted.Count==10&&remaining==0,"projection beams emit world depth strips");
        submitted.Clear();endpoint("effect",new object[]{world,"panel","rays",4,.02,.2,new Vector3D(0,0,-1),.01});Draw(context,item,10,5);Check(submitted.Count==10,"projection fans are bounded paired triangles");
        endpoint("effect",new object[]{world,"panel","particles",4,.02,.1,2,.05,.5,42});
        foreach(string style in new[]{"wipe","dissolve"})
        {Field("_modClientEffectOfflineTime",6d);endpoint("transition",new object[]{world,"panel","out",style,1d});submitted.Clear();remaining=Draw(context,item,100,7);Check(submitted.Count==0&&remaining==100,"fully exited "+style+" hides particles and projection rays too");}
        endpoint("effect-clear",new object[]{world,"panel"});endpoint("effect",new object[]{world,"panel","particles",1,.02,0,2,0,.5,42});
        var particleFrame=HologramEffectKernel.Evaluate(item.Effects,7.4,item.EffectStart,item.EffectEntering);HologramEffectParticle atEye;
        Check(HologramEffectKernel.Particle(item.Effects,particleFrame,0,item.EffectMin,item.EffectMax,out atEye),"particle centre guard fixture has live sample");
        context.Pose=MatrixD.CreateTranslation(camera.Translation-atEye.Position);submitted.Clear();Draw(context,item,10,7.4);Check(submitted.Count==2,"particle centred on viewer is not submitted");context.Pose=MatrixD.Identity;
        endpoint("effect-clear",new object[]{world,"panel"});endpoint("effect",new object[]{world,"panel","scan",.3,.5,.1,1});submitted.Clear();Draw(context,item,50,5.123);
        Check(submitted.Count>2,"scan refresh band has actual clipped geometry");for(int i=2;i<submitted.Count;i++)Check(submitted[i].A.X>=-1.000001&&submitted[i].A.X<=1.000001&&submitted[i].A.Y>=-1.000001&&submitted[i].A.Y<=1.000001,"refresh bar clipped to source faces");
        var hudContext=owners["effects-test"].Contexts[hud];var hudItem=hudContext.Items["panel"];
        endpoint("effect",new object[]{hud,"panel","particles",6,1,.1,2,.1,.5,7});submitted.Clear();Field("_modClientHudDistance",.101);Draw(hudContext,hudItem,20,6.45);
        Check(submitted.Count>2,"HUD procedural particles execute");foreach(var triangle in submitted)Check(triangle.Hud&&Math.Abs(triangle.A.Z-(camera.Translation.Z-.101))<1e-6&&Math.Abs(triangle.B.Z-(camera.Translation.Z-.101))<1e-6,"HUD particles retain exact near-plane depth");
        endpoint("mesh",new object[]{world,"panel",source,triangles,tint});Check(context.Items["panel"].Effects.ScanStrength==.3,"retained replacement preserves configured effect");
        endpoint("wires",new object[]{world,"outline",new[]{new Vector3D(-1,0,0),new Vector3D(1,0,0)},new[]{new Vector2I(0,1)},tint,.02});
        var outline=context.Items["outline"];outline.Colors=new Vector4[0];endpoint("effect",new object[]{world,"outline","volume",.1,2,.5});submitted.Clear();Draw(context,outline,20,8);Check(submitted.Count==6,"wire-only SVG colour arrays do not suppress volume effects");
        var oldEndpoint=endpoint;endpoint=(Func<string,object[],object>)service("open",new object[]{"effects-test"});Check(!(bool)oldEndpoint("valid",new object[0]),"reconnect revokes stale effects owner");
        endpoint("create-world",new object[]{0,MatrixD.Identity});Check(owners["effects-test"].Contexts[1].Items.Count==0,"reconnect starts with no orphan effects");
        type.GetMethod("UnloadModClientApi",flags).Invoke(session,null);Check(!(bool)endpoint("valid",new object[0]),"world unload revokes effects endpoint");
        return checks;
    }
}
