using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Hdr.Mods;
using HoloMap;
using VRage;
using VRageMath;
internal static class ModClientInteractionTests
{
    public static int Run()
    {
        int count=0;void Check(bool v,string label){if(!v)throw new Exception("Mod interaction: "+label);count++;}
        void Reject(Action a,string label){try{a();}catch(ArgumentException){count++;return;}throw new Exception("Expected rejection: "+label);}
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(HoloMapSession);var session=new HoloMapSession();type.GetField("_modClientActive",flags).SetValue(session,true);
        var service=(Func<string,object[],object>)Delegate.CreateDelegate(typeof(Func<string,object[],object>),session,type.GetMethod("ModClientService",flags));
        var endpoint=(Func<string,object[],object>)service("open",new object[]{"interaction-tests"});
        object Call(string op,params object[] args){return endpoint(op,args);}
        long hud=(long)Call("create-hud",0);var owners=(Dictionary<string,ModClientOwner>)type.GetField("_modClientOwners",flags).GetValue(session);var c=owners["interaction-tests"].Contexts[hud];
        var points=new[]{new Vector3D(-1,-1,0),new Vector3D(1,-1,0),new Vector3D(1,1,0),new Vector3D(-1,1,0)};
        void Artwork(string id,double x){Call("mesh",hud,id,points,new[]{0,1,2,0,2,3},Vector4.One);Call("transform",hud,id,MatrixD.CreateTranslation(x,0,0));}
        Artwork("slider",0);Artwork("coupled",20);Call("value",hud,"value",0d,0d,10d,1d);
        Call("control",hud,"drag","slider",new Vector4(-1,-1,2,2));Call("bind-value",hud,"drag","value");Call("constraint",hud,"drag","line",Vector3D.Zero,new Vector3D(10,0,0));Call("draggable",hud,"drag",true);
        Call("control",hud,"other","coupled",new Vector4(-1,-1,2,2));Call("bind-value",hud,"other","value");Call("constraint",hud,"other","line",Vector3D.Zero,new Vector3D(10,0,0));
        var identity=c.Items["slider"].Geometry;long declaration=c.DeclarationRevision;var before=(MyTuple<double,long>)Call("get-value",hud,"value");
        Call("pointer",hud,.5,0,true);Check(c.Capture!=null,"down captures bound control");Call("pointer",hud,5.5,0,true);
        var current=(MyTuple<double,long>)Call("get-value",hud,"value");Check(current.Item1==5&&current.Item2>before.Item2,"drag preserves initial offset and snaps");Check(c.Items["slider"].Transform.Translation.X==5&&c.Items["coupled"].Transform.Translation.X==25,"coupled poses commit together");Check(c.DeclarationRevision==declaration&&ReferenceEquals(identity,c.Items["slider"].Geometry),"motion is data-only and keeps mesh identity");
        var submitted=new List<ModClientTriangleSubmission>();type.GetField("_modClientOfflineSubmit",flags).SetValue(session,new Action<ModClientTriangleSubmission>(s=>submitted.Add(s)));
        var viewport=new Vector2(1920,1080);var projection=MatrixD.CreatePerspectiveFieldOfView(1.1,viewport.X/viewport.Y,.1,1000);var camera=MatrixD.CreateTranslation(0,0,5);
        object[] drawArgs={c,c.Items["slider"],viewport,MatrixD.Invert(projection),camera,camera.Translation,8};type.GetMethod("DrawModClientItem",flags).Invoke(session,drawArgs);
        var clip=Vector4D.Transform(new Vector4D(submitted[0].A,1),MatrixD.Invert(camera)*projection);Check(submitted.Count==2&&(int)drawArgs[6]==6&&Math.Abs((clip.X/clip.W+1)*viewport.X*.5-4)<1e-6,"actual retained draw sink consumes committed control pose");
        Call("pointer",hud,100,0,false);current=(MyTuple<double,long>)Call("get-value",hud,"value");Check(current.Item1==10&&c.Capture==null,"capture continues beyond bounds and releases");
        var events=(MyTuple<string,string,string,MyTuple<double,long,long>>[])Call("poll-value-events",hud);Check(events.Length>=3&&events[events.Length-1].Item1=="end"&&events[events.Length-1].Item4.Item3==0,"typed final event carries canonical local value");
        var legacy=(MyTuple<string,string>[])Call("poll-events",hud);bool click=false,down=false,up=false;foreach(var e in legacy){click|=e.Item1=="click";down|=e.Item1=="down";up|=e.Item1=="up";}Check(click&&down&&up,"legacy tuple shape and click retained alongside drag events");
        var stale=(MyTuple<bool,double,long>)Call("set-value",hud,"value",2d,before.Item2);Check(!stale.Item1&&stale.Item2==10,"stale CAS does not overwrite value");
        Call("pointer",hud,10,0,true);Check(c.Capture!=null,"second capture at transformed bounds");Call("set-value",hud,"value",3d,current.Item2);Check(c.Capture==null&&c.PointerBlockedUntilRelease,"source write preempts drag until release");Call("pointer",hud,50,0,true);Check(((MyTuple<double,long>)Call("get-value",hud,"value")).Item1==3,"held pointer cannot recapture after source preemption");Call("pointer",hud,50,0,false);
        Reject(()=>Call("set-value",hud,"value",double.NaN),"NaN value");Reject(()=>Call("constraint",hud,"drag","line",Vector3D.Zero,new Vector3D(1,0,1)),"HUD depth path");Reject(()=>Call("constraint",hud,"drag","rotation",Vector3D.Zero,Vector3D.UnitY,0d,1d),"HUD depth rotation");
        long pair=(long)Call("create-hud",0);var pairContext=owners["interaction-tests"].Contexts[pair];Call("value",pair,"shared",0d,0d,10d);
        void PairControl(string name,double x)
        {if(!pairContext.Items.ContainsKey(name)){Call("mesh",pair,name,points,new[]{0,1,2,0,2,3},Vector4.One);Call("transform",pair,name,MatrixD.CreateTranslation(x,0,0));}Call("control",pair,name,name,new Vector4(-1,-1,2,2));Call("bind-value",pair,name,"shared");Call("constraint",pair,name,"line",Vector3D.Zero,new Vector3D(10,0,0));Call("draggable",pair,name,true);}
        PairControl("A",0);PairControl("B",20);
        void PreemptsPartner(Action mutation,string label)
        {
            var p=pairContext.Items["A"].Transform.Translation;Call("pointer",pair,p.X,p.Y,false);Call("pointer",pair,p.X,p.Y,true);Check(pairContext.Capture!=null,"partner preemption fixture "+label);
            var state=(MyTuple<double,long>)Call("get-value",pair,"shared");var pose=pairContext.Items["A"].Transform;mutation();Check(pairContext.Capture==null&&pairContext.PointerBlockedUntilRelease,"shared-value lease preempted by "+label);Call("pointer",pair,p.X+100,p.Y,true);Check(((MyTuple<double,long>)Call("get-value",pair,"shared")).Item1==state.Item1&&pairContext.Items["A"].Transform==pose,"old A gesture cannot overwrite partner change "+label);Call("pointer",pair,p.X,p.Y,false);
        }
        PreemptsPartner(()=>Call("transform",pair,"B",MatrixD.CreateTranslation(100,0,0)),"B source pose");
        PreemptsPartner(()=>Call("constraint",pair,"B","line",Vector3D.Zero,new Vector3D(30,0,0)),"B constraint");
        PreemptsPartner(()=>Call("bind-value",pair,"B","shared"),"B binding");
        PreemptsPartner(()=>Call("draggable",pair,"B",false),"B drag property");
        PreemptsPartner(()=>Call("control",pair,"B","B",new Vector4(-1,-1,2,2)),"B control replacement");PairControl("B",100);
        PreemptsPartner(()=>Call("remove-control",pair,"B"),"B control removal");PairControl("B",100);
        PreemptsPartner(()=>Call("mesh",pair,"B",points,new[]{0,1,2,0,2,3},Vector4.One),"B artwork replacement");PairControl("B",100);
        PreemptsPartner(()=>Call("remove",pair,"B"),"B artwork removal");PairControl("B",100);
        PreemptsPartner(()=>Call("visible",pair,"B",false),"B artwork hidden");
        Call("pointer",pair,.5,0,true);Call("pointer",pair,20,0,true);Check(((MyTuple<double,long>)Call("get-value",pair,"shared")).Item1==10,"continuous off-centre grab reaches hard endpoint after overdrag");Call("pointer",pair,20,0,false);
        // One coupled placement failure must retain every pose, value revision and live capture.
        Artwork("danger",999999999995d);Call("control",hud,"danger","danger",new Vector4(-1,-1,2,2));Call("bind-value",hud,"danger","value");Call("constraint",hud,"danger","line",Vector3D.Zero,new Vector3D(100,0,0));
        var state=(MyTuple<double,long>)Call("get-value",hud,"value");var saved=c.Items["slider"].Transform;var failed=(MyTuple<bool,double,long>)Call("set-value",hud,"value",10d,state.Item2);Check(!failed.Item1&&failed.Item2==state.Item1&&failed.Item3==state.Item2&&c.Items["slider"].Transform==saved,"coupled fit admission atomic");Call("remove-control",hud,"danger");Call("remove",hud,"danger");
        Call("pointer",hud,3.5,0,true);Check(c.Capture!=null,"capture fixture");Call("pointer-cancel",hud);Check(c.Capture==null&&c.PointerBlockedUntilRelease,"explicit focus loss cancels");Call("pointer",hud,3.5,0,false);Call("pointer",hud,3.5,0,true);Call("visible",hud,"slider",false);Check(c.Capture==null,"hidden artwork cancels");Call("visible",hud,"slider",true);Call("pointer",hud,3.5,0,false);
        // Source replacement revokes a control and never autorebinds independent legacy hit regions.
        Call("bounds",hud,"legacy",new Vector4(1,1,2,2));Call("mesh",hud,"slider",points,new[]{0,1,2,0,2,3},Vector4.One);Check(!c.Controls.ContainsKey("drag")&&c.Bounds.ContainsKey("legacy"),"replacement revokes controls and preserves independent bounds");
        // World ray and numeric-to-angular mapping.
        long world=(long)Call("create-world",0,MatrixD.CreateTranslation(100,20,0));Call("mesh",world,"rotor",points,new[]{0,1,2,0,2,3},Vector4.One);Call("value",world,"angle",0d,0d,1d,0d);Call("control",world,"rotate","rotor",new Vector4(-2,-2,4,4));Call("bind-value",world,"rotate","angle");Call("constraint",world,"rotate","rotation",Vector3D.Zero,Vector3D.UnitZ,0d,Math.PI/2);Call("draggable",world,"rotate",true);
        Call("pointer-ray",world,new Vector3D(101,20,5),-Vector3D.UnitZ,true);Call("pointer-ray",world,new Vector3D(100,21,5),-Vector3D.UnitZ,true);Check(Math.Abs(((MyTuple<double,long>)Call("get-value",world,"angle")).Item1-1)<1e-6,"world rotation maps angular range into numeric value");Call("pointer-ray",world,new Vector3D(100,21,5),-Vector3D.UnitZ,false);
        Reject(()=>Call("pointer-ray",hud,Vector3D.Zero,Vector3D.UnitZ,false),"world ray unsupported on HUD");Reject(()=>Call("pointer-ray",world,new Vector3D(double.NaN),Vector3D.UnitZ,false),"nonfinite world ray");
        foreach(string id in new[]{"near","far"})
        {Call("mesh",world,id,points,new[]{0,1,2,0,2,3},Vector4.One);Call("transform",world,id,MatrixD.CreateTranslation(0,0,id=="near"?1:0));Call("value",world,id,0d,0d,1d);Call("control",world,id,id,new Vector4(-1,-1,2,2));Call("bind-value",world,id,id);Call("constraint",world,id,"line",Vector3D.Zero,Vector3D.UnitX);Call("draggable",world,id,true);}
        Call("pointer-ray",world,new Vector3D(100,20,5),-Vector3D.UnitZ,true);Check(owners["interaction-tests"].Contexts[world].Capture.Control.Id=="near","world ray selects nearest control rather than later far declaration");Call("pointer-cancel",world);
        Call("clear",world);Check(owners["interaction-tests"].Contexts[world].Values.Count==0&&owners["interaction-tests"].Contexts[world].Controls.Count==0,"clear revokes metadata");
        long oldRevision=((MyTuple<double,long>)Call("get-value",hud,"value")).Item2;Call("remove-value",hud,"value");Call("value",hud,"value",0d,0d,1d);Check(((MyTuple<double,long>)Call("get-value",hud,"value")).Item2>oldRevision,"value ID reuse revision monotonic");
        Call("control",hud,"fresh","slider",new Vector4(-1,-1,2,2));Call("bind-value",hud,"fresh","value");Call("constraint",hud,"fresh","path",new[]{Vector3D.Zero,new Vector3D(5,0,0),new Vector3D(5,5,0)});Call("draggable",hud,"fresh",true);
        Call("poll-value-events",hud);for(int i=0;i<63;i++){Call("value",hud,"v"+i,0d,0d,1d);Call("set-value",hud,"v"+i,1d);}
        Call("pointer",hud,0,0,true);for(int i=0;i<100;i++)Call("pointer",hud,i*.04,0,true);Call("pointer",hud,4,0,false);
        var bounded=(MyTuple<string,string,string,MyTuple<double,long,long>>[])Call("poll-value-events",hud);bool final=false;int changes=0;foreach(var e in bounded){final|=e.Item1=="end"&&e.Item2=="fresh";if(e.Item1=="change"&&e.Item2=="fresh")changes++;}Check(bounded.Length<=64&&final&&changes==1,"coalesced numeric progress reserves final gesture capacity");
        Call("pointer",hud,c.Items["slider"].Transform.Translation.X,0,true);Check(c.Capture!=null,"render-disable fixture");type.GetField("ClientRenderingEnabled",flags).SetValue(session,false);type.GetMethod("CancelModClientInteractions",flags).Invoke(session,new object[]{"render-disabled"});Check(c.Capture==null,"render-disable hook cancels retained drag");type.GetField("ClientRenderingEnabled",flags).SetValue(session,true);Call("pointer",hud,0,0,false);
        var sdk=(HdrModApi)RuntimeHelpers.GetUninitializedObject(typeof(HdrModApi));typeof(HdrModApi).GetField("endpoint",flags).SetValue(sdk,endpoint);typeof(HdrModApi).GetField("service",flags).SetValue(sdk,service);typeof(HdrModApi).GetField("<ConnectionGeneration>k__BackingField",flags).SetValue(sdk,1L);
        double external=.75;int callbacks=0;var binding=sdk.BindValue(hud,"value",()=>external,v=>{external=v;callbacks++;});sdk.UpdateBindings();Check(((MyTuple<double,long>)Call("get-value",hud,"value")).Item1==.75&&c.Items["slider"].Transform.Translation.Y==2.5,"real SDK callback source commits coupled path pose");
        Call("pointer",hud,5,2.5,true);Call("pointer",hud,5,4,true);sdk.UpdateBindings();Check(Math.Abs(external-.9)<1e-9&&callbacks==1,"real endpoint drag delivers canonical value through SDK setter");Call("pointer",hud,5,4,false);binding.Dispose();
        var old=endpoint;endpoint=(Func<string,object[],object>)service("open",new object[]{"interaction-tests"});Check(!(bool)old("valid",new object[0]),"owner replacement revokes stale endpoint");type.GetMethod("UnloadModClientApi",flags).Invoke(session,null);Check(!(bool)endpoint("valid",new object[0]),"world unload revokes capture service");
        return count;
    }
}
