using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Hdr.Mods;
using VRage;
using VRageMath;

// Compiles the actual copied SDK against real game/VRage references. No native input, game constructor or renderer.
internal static class SdkBindingTests
{
    static int count;
    static void Check(bool condition,string message){if(!condition)throw new Exception("SDK binding: "+message);count++;}
    static void Reject(Action action,string message){try{action();}catch(ArgumentException){count++;return;}throw new Exception("Expected SDK rejection: "+message);}
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static HdrModApi Attach(FakeEndpoint fake)
    {
        var api=(HdrModApi)RuntimeHelpers.GetUninitializedObject(typeof(HdrModApi));
        typeof(HdrModApi).GetField("endpoint",Private).SetValue(api,fake.Endpoint);
        typeof(HdrModApi).GetField("service",Private).SetValue(api,fake.Service);
        typeof(HdrModApi).GetField("<ConnectionGeneration>k__BackingField",Private).SetValue(api,1L);
        return api;
    }
    static void Reconnect(HdrModApi api,FakeEndpoint next)
    {typeof(HdrModApi).GetMethod("Receive",Private).Invoke(api,new object[]{next.Service});}
    static double Read(FakeEndpoint fake,long context,string id){return fake.Values[new MyTuple<long,string>(context,id)].Value;}
    public static int Run()
    {
        count=0;Wrappers();Authority();Lifecycle();Failures();return count;
    }
    static void Wrappers()
    {
        var f=new FakeEndpoint();var api=Attach(f);Check(api.Ready,"fake owned endpoint valid");
        api.Value(1,"number",2,0,10,.5);var v=api.GetValue(1,"number");Check(v.Item1==2&&v.Item2==0,"value/get exact typed tuple");
        var s=api.SetValue(1,"number",8.2,0);Check(s.Item1&&s.Item2==8&&s.Item3==1,"CAS range/step canonical tuple");
        s=api.SetValue(1,"number",99,0);Check(!s.Item1&&s.Item2==8&&s.Item3==1,"stale CAS current-state tuple");
        s=api.SetValue(1,"number",99);Check(s.Item1&&s.Item2==10&&s.Item3==2,"unconditional clamp");
        s=api.SetValue(1,"number",10,2);Check(s.Item1&&s.Item3==2,"accepted unchanged write preserves revision");
        api.Control(1,"slider","art",new Vector4(1,2,3,4));Check(f.Last.Command=="control"&&(long)f.Last.Args[0]==1&&(string)f.Last.Args[1]=="slider"&&(string)f.Last.Args[2]=="art"&&(Vector4)f.Last.Args[3]==new Vector4(1,2,3,4),"control raw tuple");
        api.BindControlValue(1,"slider","number");Check(f.Last.Command=="bind-value"&&f.Last.Args.Length==3,"control/value association");
        api.Draggable(1,"slider");Check(f.Last.Command=="draggable"&&(bool)f.Last.Args[2],"draggable default");
        api.ConstraintLine(1,"slider",Vector3D.Zero,Vector3D.UnitX);Check(f.Last.Command=="constraint"&&(string)f.Last.Args[2]=="line"&&f.Last.Args.Length==5,"line descriptor");
        var path=new[]{Vector3D.Zero,Vector3D.UnitY};api.ConstraintPath(1,"slider",path);Check((string)f.Last.Args[2]=="path"&&ReferenceEquals(f.Last.Args[3],path),"path raw array");
        api.ConstraintRotation(1,"slider",Vector3D.Zero,Vector3D.UnitZ,-Math.PI,Math.PI);Check((string)f.Last.Args[2]=="rotation"&&(double)f.Last.Args[5]==-Math.PI&&(double)f.Last.Args[6]==Math.PI,"rotation radian range");
        api.Pointer(1,2,3,true);Check(f.Last.Command=="pointer"&&(double)f.Last.Args[1]==2,"old cooperative pointer retained");
        api.CancelPointer(1);Check(f.Last.Command=="pointer-cancel"&&f.Last.Args.Length==1,"cooperative cancel");
        api.PointerRay(1,Vector3D.One,Vector3D.UnitZ,false);Check(f.Last.Command=="pointer-ray"&&(Vector3D)f.Last.Args[1]==Vector3D.One,"world ray pointer");
        f.Events=new[]{new MyTuple<string,string,string,MyTuple<double,long,long>>("change","slider","number",new MyTuple<double,long,long>(4,7,0))};
        var e=api.PollValueEvents(1);Check(e.Length==1&&e[0].Item1=="change"&&e[0].Item3=="number"&&e[0].Item4.Item1==4&&e[0].Item4.Item2==7&&e[0].Item4.Item3==0,"exact event payload");
        Check(api.PollValueEvents(1).Length==0,"poll consumes event queue");
        Reject(()=>api.BindValue(1,"number",null,x=>{}),"null getter");Reject(()=>api.BindValue(1,"number",()=>1,null),"null setter");
        api.Effect(1,"art","flicker",.2);Check(f.Last.Command=="effect"&&f.Last.Args.Length==4,"effects wrapper preserved");
        api.Transition(1,"art",false);Check(f.Last.Command=="transition"&&(string)f.Last.Args[2]=="out","transition wrapper preserved");
    }
    static void Authority()
    {
        var f=new FakeEndpoint();var api=Attach(f);api.Value(1,"a",0,0,10,1);double source=3.3;int gets=0,sets=0;
        var handle=api.BindValue(1,"a",()=>{gets++;return source;},v=>{sets++;source=v;});
        Check(gets==0&&sets==0,"registration invokes no callbacks");api.UpdateBindings();Check(Read(f,1,"a")==3&&source==3&&sets==1&&gets==2,"first pump source authority plus canonical delivery");
        int writes=f.SetCalls;api.UpdateBindings();Check(f.SetCalls==writes&&sets==1,"canonical correction does not echo-write");
        f.Ui(1,"a",6);api.UpdateBindings();Check(source==6&&sets==2&&f.SetCalls==writes,"new HDR revision updates source without echo");
        f.Ui(1,"a",8);source=4;api.UpdateBindings();Check(Read(f,1,"a")==4&&source==4&&sets==2,"changed source preempts pending drag");
        source=5;f.BeforeSet=()=>f.Ui(1,"a",9);api.UpdateBindings();Check(Read(f,1,"a")==9&&source==5&&sets==2,"stale CAS does not overwrite source or UI");
        api.UpdateBindings();Check(Read(f,1,"a")==5&&source==5,"source retries fresh revision next pump");
        api.UnbindValue(1,"a");api.BindValue(1,"a",()=>{api.SetValue(1,"a",9);return 2;},v=>{});api.UpdateBindings();Check(Read(f,1,"a")==2,"CAS snapshot is fresh after getter authoring HDR");
        int before=f.SetCalls;api.UnbindValue(1,"a");source=7;api.UpdateBindings();Check(f.SetCalls==before,"unbind detaches adapter");handle.Dispose();
        // A setter may update HDR again; acknowledge only what was delivered.
        api.BindValue(1,"a",()=>source,v=>{source=v;if(v==2)api.SetValue(1,"a",9);});api.UpdateBindings();f.Ui(1,"a",2);api.UpdateBindings();Check(source==2&&Read(f,1,"a")==9,"setter may author a newer HDR revision");api.UpdateBindings();Check(source==9,"setter-authored revision delivered next pump");
        // A no-op/rounding setter must not make an unchanged source repeatedly preempt UI.
        api.Clear(1);api.Value(1,"a",0,0,10,1);source=2.2;sets=0;api.BindValue(1,"a",()=>source,v=>sets++);api.UpdateBindings();before=f.SetCalls;f.Ui(1,"a",8);api.UpdateBindings();api.UpdateBindings();Check(f.SetCalls==before&&Read(f,1,"a")==8&&sets==2,"no-op setter does not echo-write");
        api.Clear(1);api.Value(1,"a",0,0,10);source=1;api.BindValue(1,"a",()=>source,v=>source=v==4?9:v);api.UpdateBindings();f.Ui(1,"a",4);api.UpdateBindings();Check(source==9&&Read(f,1,"a")==4,"setter can author a different source value");api.UpdateBindings();Check(Read(f,1,"a")==9,"setter-authored source change is delivered next pump");
        // Recursion is bounded; bindings added inside callbacks wait for the following pump.
        api.Clear(1);api.Value(1,"a",0,0,10);api.Value(1,"b",0,0,10);int first=0,second=0;
        api.BindValue(1,"a",()=>{first++;api.UpdateBindings();if(first==1)api.BindValue(1,"b",()=>{second++;return 2;},v=>{});return 1;},v=>{});
        api.UpdateBindings();Check(first==1&&second==0,"snapshot pump and nested suppression");api.UpdateBindings();Check(second==1,"new adapter starts next pump");
    }
    static void Lifecycle()
    {
        foreach(string action in new[]{"clear","destroy","release","dispose","unbind","reconnect","remove-value"})
        {
            var f=new FakeEndpoint();var next=new FakeEndpoint();var api=Attach(f);api.Value(1,"a",0,0,10);next.Define(1,"a",7,0,10,0);int setter=0,getter=0;long generation=api.ConnectionGeneration;
            api.BindValue(1,"a",()=>{getter++;if(action=="clear")api.Call("clear",1L);else if(action=="destroy")api.Call("destroy",1L);else if(action=="release")api.Call("release");else if(action=="dispose")api.Dispose();else if(action=="unbind")api.UnbindValue(1,"a");else if(action=="remove-value"){api.Call("remove-value",1L,"a");api.Value(1,"a",8,0,10);}else Reconnect(api,next);return 4;},v=>setter++);
            api.UpdateBindings();Check(getter==1&&setter==0&&f.SetCalls==0&&next.SetCalls==0,action+" in getter prevents stale writes");
            api.UpdateBindings();Check(getter==1&&next.SetCalls==0,action+" does not replay old adapters");
            if(action=="reconnect")Check(api.ConnectionGeneration==generation+1&&api.Ready,"reconnect changes endpoint generation");
        }
        foreach(string action in new[]{"clear","destroy","release","dispose","unbind","reconnect"})
        {
            var f=new FakeEndpoint();var next=new FakeEndpoint();var api=Attach(f);api.Value(1,"a",0,0,10);next.Define(1,"a",7,0,10,0);double source=1;int gets=0,sets=0;
            api.BindValue(1,"a",()=>{gets++;return source;},v=>{sets++;if(action=="clear")api.Clear(1);else if(action=="destroy")api.Destroy(1);else if(action=="release")api.Call("release");else if(action=="dispose")api.Dispose();else if(action=="unbind")api.UnbindValue(1,"a");else Reconnect(api,next);source=v;});api.UpdateBindings();f.Ui(1,"a",4);int before=gets;
            api.UpdateBindings();Check(sets==1&&gets==before+1&&next.SetCalls==0,action+" in setter blocks post-callback getter/write");api.UpdateBindings();Check(sets==1,action+" in setter revokes adapter");
        }
        // Old IDisposable cannot detach a same-key successor; destroying another context leaves its peer alive.
        var g=new FakeEndpoint();var sdk=Attach(g);sdk.Value(1,"a",0,0,10);sdk.Value(2,"a",0,0,10);int old=0,fresh=0,peer=0;
        long existingGeneration=sdk.ConnectionGeneration;
        typeof(HdrModApi).GetMethod("Receive",Private).Invoke(sdk,new object[]{new Func<string,object[],object>((op,a)=>"other-protocol")});
        Check(sdk.Ready&&sdk.ConnectionGeneration==existingGeneration,"mismatched discovery does not replace owned endpoint");
        typeof(HdrModApi).GetMethod("Receive",Private).Invoke(sdk,new object[]{new Func<string,object[],object>((op,a)=>{throw new InvalidOperationException("unavailable discovery");})});
        Check(sdk.Ready&&sdk.ConnectionGeneration==existingGeneration,"throwing discovery preserves prior connection");
        var token=sdk.BindValue(1,"a",()=>{old++;return 1;},v=>{});sdk.BindValue(1,"a",()=>{fresh++;return 2;},v=>{});token.Dispose();sdk.BindValue(2,"a",()=>{peer++;return 3;},v=>{});sdk.UpdateBindings();Check(old==0&&fresh==1&&peer==1,"old handle cannot revoke successor");sdk.Clear(1);sdk.UpdateBindings();Check(fresh==1&&peer==2,"context clear preserves unrelated binding");
        // A getter may revoke another snapshot member before it is called.
        sdk.Clear(2);sdk.Value(2,"a",0,0,10);sdk.Value(2,"b",0,0,10);int called=0;
        sdk.BindValue(2,"a",()=>{called++;sdk.UnbindValue(2,"b");return 1;},v=>{});sdk.BindValue(2,"b",()=>{called++;sdk.UnbindValue(2,"a");return 2;},v=>{});sdk.UpdateBindings();Check(called==1,"snapshot membership validated before each callback regardless of dictionary order");
        // Invalid endpoint without a generation increment still revokes the adapter.
        sdk.Clear(2);sdk.Value(2,"a",0,0,10);int invalid=0;sdk.BindValue(2,"a",()=>{invalid++;return 2;},v=>{});g.Valid=false;sdk.UpdateBindings();g.Valid=true;sdk.UpdateBindings();Check(invalid==0&&!sdk.Ready,"Ready invalidation revokes despite same numeric generation");
        object result;string reason;Check(!sdk.TryCall("get-value",out result,out reason,2L,"a")&&reason.StartsWith("Requires mod: HDR API"),"absent endpoint safe reason retained");
    }
    static void Failures()
    {
        foreach(bool priorProbeResult in new[]{false,true})
        {
            var readyPrior=new FakeEndpoint();var readyNext=new FakeEndpoint();var readiness=Attach(readyPrior);readiness.Value(1,"a",0,0,10);readyNext.Define(1,"a",0,0,10,0);int oldCalls=0,newCalls=0;
            readiness.BindValue(1,"a",()=>{oldCalls++;return 2;},v=>{});long oldGeneration=readiness.ConnectionGeneration;
            readyPrior.OnValid=()=>{Reconnect(readiness,readyNext);readiness.BindValue(1,"a",()=>{newCalls++;return 6;},v=>{});readyPrior.Valid=priorProbeResult;};
            Check(!readiness.Ready,"Ready refuses stale "+priorProbeResult+" result from prior generation");
            Check(readiness.ConnectionGeneration==oldGeneration+1&&readiness.Ready,"Ready probe preserves successor generation for prior "+priorProbeResult);
            readiness.UpdateBindings();Check(oldCalls==0&&newCalls==1&&Read(readyNext,1,"a")==6,"Ready probe preserves successor adapter for prior "+priorProbeResult);
        }
        foreach(string command in new[]{"clear","destroy","release","remove-value"})
        {
            var preserved=new FakeEndpoint();var retained=Attach(preserved);retained.Value(1,"a",0,0,10);double local=2;int callbacks=0;
            retained.BindValue(1,"a",()=>{callbacks++;return local;},v=>local=v);long beforeGeneration=retained.ConnectionGeneration;
            Reject(()=>{if(command=="release")retained.Call(command,"extra");else if(command=="remove-value")retained.Call(command,1L,"a","extra");else retained.Call(command,1L,"extra");},command+" rejects trailing argument");
            Check(retained.Ready&&retained.ConnectionGeneration==beforeGeneration&&callbacks==0,"failed "+command+" preserves current generation without callbacks");
            retained.UpdateBindings();Check(callbacks==1&&Read(preserved,1,"a")==2,"failed "+command+" retains value binding");
            local=7;retained.UpdateBindings();Check(callbacks==2&&Read(preserved,1,"a")==7,"failed "+command+" remains two-way on same generation");
        }
        // Successful old endpoint cleanup must not detach a successor registered during its call.
        var earlier=new FakeEndpoint();var successor=new FakeEndpoint();var moved=Attach(earlier);moved.Value(1,"a",0,0,10);successor.Define(1,"a",0,0,10,0);int oldCallbacks=0,newCallbacks=0;
        moved.BindValue(1,"a",()=>{oldCallbacks++;moved.Clear(1);return 3;},v=>{});
        earlier.AfterLifecycle=()=>{Reconnect(moved,successor);moved.BindValue(1,"a",()=>{newCallbacks++;return 8;},v=>{});};
        moved.UpdateBindings();Check(oldCallbacks==1&&newCallbacks==0&&successor.SetCalls==0,"successful reentrant clear cannot continue old callback into successor");
        moved.UpdateBindings();Check(newCallbacks==1&&Read(successor,1,"a")==8,"successful old lifecycle call preserves successor adapter");
        var rejected=new FakeEndpoint();var probeSuccessor=new FakeEndpoint();var probed=Attach(rejected);probed.Value(1,"a",0,0,10);probeSuccessor.Define(1,"a",0,0,10,0);int retiredCallbacks=0,probeCallbacks=0;
        probed.BindValue(1,"a",()=>{retiredCallbacks++;return 2;},v=>{});
        rejected.BeforeReject=()=>rejected.OnValid=()=>{Reconnect(probed,probeSuccessor);probed.BindValue(1,"a",()=>{probeCallbacks++;return 9;},v=>{});};
        Reject(()=>probed.Call("clear",1L,"extra"),"failed lifecycle probe can reconnect");
        Check(probed.Ready,"failing prior validity probe cannot invalidate reentrant successor");probed.UpdateBindings();
        Check(retiredCallbacks==0&&probeCallbacks==1&&Read(probeSuccessor,1,"a")==9,"exception cleanup preserves successor adapter admitted during validity probe");
        var f=new FakeEndpoint();var api=Attach(f);api.Value(1,"bad",0,0,10);api.Value(1,"good",0,0,10);int bad=0,good=0;
        api.BindValue(1,"bad",()=>{bad++;throw new Exception("getter exploded");},v=>{});api.BindValue(1,"good",()=>{good++;return 4;},v=>{});api.UpdateBindings();Check(good==1&&bad==1&&Read(f,1,"good")==4&&api.LastBindingError.Contains("getter exploded"),"getter exception isolated and recorded");api.UpdateBindings();Check(good==2&&bad==1,"bad getter permanently detached");
        api.BindValue(1,"bad",()=>2,v=>{throw new Exception("setter exploded");});api.UpdateBindings();f.Ui(1,"bad",5);api.UpdateBindings();Check(api.LastBindingError.Contains("setter exploded")&&good==4,"setter exception isolated");
        int replacement=0;api.BindValue(1,"bad",()=>{api.BindValue(1,"bad",()=>{replacement++;return 3;},v=>{});throw new Exception("old callback exploded");},v=>{});api.UpdateBindings();api.UpdateBindings();Check(replacement==1&&Read(f,1,"bad")==3,"throwing obsolete callback preserves replacement identity");
        api.BindValue(1,"bad",()=>double.NaN,v=>{});int before=f.SetCalls;api.UpdateBindings();Check(api.LastBindingError.Contains("finite")&&f.SetCalls==before,"nonfinite getter detached before endpoint write");
        int inDraw=0;api.BindValue(1,"bad",()=>{inDraw++;return 2;},v=>{});f.Drawing=true;api.UpdateBindings();f.Drawing=false;Check(inDraw==0&&api.LastBindingError.Contains("outside Draw"),"Draw preflight blocks user callback");
        // Post-setter getter reconnects: there must be no stale GetValue on the successor.
        api.UnbindValue(1,"good");var next=new FakeEndpoint();next.Define(1,"bad",8,0,10,0);double source=1;int get=0;
        api.BindValue(1,"bad",()=>{get++;if(get==3)Reconnect(api,next);return source;},v=>source=v);api.UpdateBindings();f.Ui(1,"bad",6);int reads=next.GetCalls;api.UpdateBindings();Check(next.GetCalls==reads&&next.SetCalls==0,"reconnect from post-setter getter cannot touch successor");
    }
    sealed class RecordedCommand {public string CommandName;public string Command {get{return CommandName;}}public object[] Args;}
    sealed class State {public double Value,Min,Max,Step;public long Revision;}
    sealed class FakeEndpoint
    {
        public readonly Dictionary<MyTuple<long,string>,State> Values=new Dictionary<MyTuple<long,string>,State>();
        public readonly Func<string,object[],object> Endpoint,Service;
        public bool Valid=true,Drawing;public int GetCalls,SetCalls;public Action BeforeSet,AfterLifecycle,BeforeReject,OnValid;public RecordedCommand Last;
        public MyTuple<string,string,string,MyTuple<double,long,long>>[] Events=new MyTuple<string,string,string,MyTuple<double,long,long>>[0];
        public FakeEndpoint(){Endpoint=Execute;Service=(op,a)=>op=="version"?(object)"HDR.ModClient/1":op=="open"?Endpoint:op=="capabilities"?new string[0]:(object)true;}
        public void Define(long ctx,string id,double value,double min,double max,double step){Values[new MyTuple<long,string>(ctx,id)]=new State{Value=value,Min=min,Max=max,Step=step};}
        public void Ui(long ctx,string id,double value){var state=Values[new MyTuple<long,string>(ctx,id)];state.Value=value;state.Revision++;}
        object Execute(string op,object[] a)
        {
            Last=new RecordedCommand{CommandName=op,Args=a};if(op=="valid"){var probe=OnValid;OnValid=null;if(probe!=null)probe();return Valid;}
            if(op=="release"){ValidateLength(a,0);Valid=false;LifecycleCompleted();return true;}if(!Valid)throw new ArgumentException("revoked fake endpoint");
            if(Drawing)throw new ArgumentException("Mutate contexts on client simulation thread outside Draw.");
            if(op=="value"){Define((long)a[0],(string)a[1],(double)a[2],(double)a[3],(double)a[4],(double)a[5]);return true;}
            if(op=="get-value"){GetCalls++;var s=Values[new MyTuple<long,string>((long)a[0],(string)a[1])];return new MyTuple<double,long>(s.Value,s.Revision);}
            if(op=="set-value")
            {
                SetCalls++;var before=BeforeSet;BeforeSet=null;if(before!=null)before();var s=Values[new MyTuple<long,string>((long)a[0],(string)a[1])];long expected=(long)a[3];
                if(expected>=0&&s.Revision!=expected)return new MyTuple<bool,double,long>(false,s.Value,s.Revision);
                double v=Math.Max(s.Min,Math.Min(s.Max,(double)a[2]));if(s.Step>0)v=Math.Max(s.Min,Math.Min(s.Max,s.Min+Math.Round((v-s.Min)/s.Step)*s.Step));if(v!=s.Value){s.Value=v;s.Revision++;}return new MyTuple<bool,double,long>(true,s.Value,s.Revision);
            }
            if(op=="clear"||op=="destroy"){ValidateLength(a,1);var keys=new List<MyTuple<long,string>>(Values.Keys);foreach(var k in keys)if(k.Item1==(long)a[0])Values.Remove(k);LifecycleCompleted();return true;}
            if(op=="remove-value"){ValidateLength(a,2);Values.Remove(new MyTuple<long,string>((long)a[0],(string)a[1]));LifecycleCompleted();return true;}
            if(op=="poll-value-events"){var e=Events;Events=new MyTuple<string,string,string,MyTuple<double,long,long>>[0];return e;}
            if(op=="poll-events")return new MyTuple<string,string>[0];return true;
        }
        void ValidateLength(object[] args,int length){if(args.Length==length)return;var rejecting=BeforeReject;BeforeReject=null;if(rejecting!=null)rejecting();throw new ArgumentException("Unexpected trailing argument.");}
        void LifecycleCompleted(){var callback=AfterLifecycle;AfterLifecycle=null;if(callback!=null)callback();}
    }
#if HDR_SDK_BINDING_TESTS
    public static int Main(){try{Console.WriteLine("SDK bindings: "+Run()+" assertions passed.");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
#endif
}
