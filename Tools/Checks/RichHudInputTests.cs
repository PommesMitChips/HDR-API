using System;
using System.Collections.Generic;
using HoloMap;
using VRage;
using VRageMath;
using Accessor=System.Func<object,int,object>;
using Space=System.Func<VRage.MyTuple<bool,float,VRageMath.MatrixD>>;
using RichString=VRage.MyTuple<System.Text.StringBuilder,VRage.MyTuple<byte,float,VRageMath.Vector2I,VRageMath.Color>>;
using TextBuilder=VRage.MyTuple<VRage.MyTuple<System.Func<int,int,object>,System.Func<int>>,System.Func<VRageMath.Vector2I,int,object>,System.Func<object,int,object>,System.Action<System.Collections.Generic.IList<VRage.MyTuple<System.Text.StringBuilder,VRage.MyTuple<byte,float,VRageMath.Vector2I,VRageMath.Color>>>,VRageMath.Vector2I>,System.Action<System.Collections.Generic.IList<VRage.MyTuple<System.Text.StringBuilder,VRage.MyTuple<byte,float,VRageMath.Vector2I,VRageMath.Color>>>>,System.Action>;

static class RichHudInputTests
{
    enum ProviderModes:int { None=0,MouseAndCam=5,Full=7 }
    sealed class Provider
    {
        public ProviderModes OwnMode;
        public int Mode5Requests,Unregistered,CallbacksInstalled;
        public bool CursorEnabled,ThrowBindSet;
        public Action Callback;
        public bool Hardened,ApplyImmediately=true,AppliedReady=true;
        public int ExtensionVersion=1,AppliedMask,ReleaseAppliedCount;
        public bool QueueBeforeReply,ThrowRequestAfterQueue;
        public int QueuedMask,CancelledEpoch;
        Accessor owner;
        object Bind(object value,int member)
        {
            if(member!=7)return null;
            if(value==null)return OwnMode;
            if(ThrowBindSet)throw new InvalidOperationException("Provider gone");
            OwnMode=(ProviderModes)value;
            if(OwnMode==ProviderModes.MouseAndCam)Mode5Requests++;
            return null;
        }
        object Hud(object value,int member)
        {
            if(member==1)return 1000f;
            if(member==2)return 500f;
            if(member==18)return CursorEnabled?1:0;
            if(member==10){CursorEnabled=(bool)value;return null;}
            if(member==22){Callback=value as Action;if(Callback!=null)CallbacksInstalled++;return null;}
            return null;
        }
        object Cursor(object value,int member){return member==2?(object)new Vector2(250,125):member==1?(object)(owner!=null):null;}
        object Module(int module)
        {
            if(module==6)return Hardened?(object)new MyTuple<int,Func<int,bool>,Func<int>,Action,Func<bool>>
                (ExtensionVersion,mask=>{if(QueueBeforeReply){QueuedMask=mask;OwnMode=ProviderModes.Full;if(ThrowRequestAfterQueue)throw new InvalidOperationException("queue then failure");}if(mask!=7||!ApplyImmediately||!AppliedReady)return false;OwnMode=ProviderModes.Full;AppliedMask=7;return true;},()=>AppliedMask,
                    ()=>{ReleaseAppliedCount++;CancelledEpoch++;QueuedMask=0;OwnMode=ProviderModes.None;AppliedMask=0;},()=>AppliedReady):null;
            if(module!=1&&module!=2)return null;
            if(module==1)return new MyTuple<Accessor,MyTuple<Func<int,object,int,object>,Func<int>>,MyTuple<Func<Vector2I,object,int,object>,Func<int,int>>,Func<Vector2I,int,bool>,MyTuple<Func<int,int,object>,Func<int>>,Action>
                (Bind,default,default,null,default,()=>{});
            return new MyTuple<MyTuple<Func<Space,bool>,Func<float,Space,bool>,Func<Accessor,bool>,Func<Accessor,bool>,Func<Accessor,bool>,Accessor>,Func<MyTuple<TextBuilder,MyTuple<Func<float>,Action<float>>,Func<Vector2>,Func<Vector2>,MyTuple<Func<Vector2>,Action<Vector2>>,Action<BoundingBox2,BoundingBox2,MatrixD[]>>>,Accessor,Action>
                (new MyTuple<Func<Space,bool>,Func<float,Space,bool>,Func<Accessor,bool>,Func<Accessor,bool>,Func<Accessor,bool>,Accessor>
                    (null,null,a=>a==owner,a=>{if(owner!=null)return false;owner=a;return true;},a=>{if(owner!=a)return false;owner=null;return true;},Cursor),null,Hud,()=>{});
        }
        public void Register(RichHudInputBridge bridge,int version=13)
        { bridge.Receive(2,new MyTuple<Action,Func<int,object>,int>(()=>Unregistered++,Module,version)); }
    }
    public static int Run()
    {
        int n=0;
        Action<bool,string> check=(condition,message)=>{if(!condition)throw new Exception("RHF: "+message);n++;};
        var bridge=new RichHudInputBridge(()=>{});
        check(!bridge.Ready&&!bridge.Request(),"missing provider cannot capture");
        check(bridge.Registration() is MyTuple<string,Action<int,object>,Action,int>,"official client registration tuple");
        var provider=new Provider();provider.Register(bridge);
        check(bridge.Ready&&provider.CallbacksInstalled==1,"API13 handshake installs owned callback");
        check(!bridge.SupportsAcknowledgedCapture&&!bridge.Request(),"unacknowledged blacklist cannot grant capture");
        check(provider.Mode5Requests==0&&!provider.CursorEnabled,"unsupported capture changes no game input");
        for(int i=0;i<100;i++)provider.Callback();
        check(!bridge.Requested&&bridge.ObservedFrames==0,"frame delay cannot manufacture engine acknowledgement");
        var cursor=bridge.Cursor();
        check(Math.Abs(cursor.X-.75f)<1e-6&&Math.Abs(cursor.Y-.25f)<1e-6,"installed centered RHF cursor normalizes correctly");
        bridge.Close();
        check(!bridge.Ready&&provider.Unregistered==1&&provider.Callback==null&&!provider.CursorEnabled&&provider.OwnMode==ProviderModes.None,"close releases only own state and unregisters");
        bridge.Close();check(provider.Unregistered==1,"close idempotent");
        var mismatch=new RichHudInputBridge(()=>{});var wrong=new Provider();wrong.Register(mismatch,14);
        check(!mismatch.Ready&&wrong.Unregistered==1&&wrong.Mode5Requests==0,"unsupported protocol rejects and unregisters");
        var partial=new RichHudInputBridge(()=>{});var broken=new Provider{ThrowBindSet=true};broken.Register(partial);
        check(!partial.Ready&&broken.Unregistered==1&&!broken.CursorEnabled,"partial handshake cleans up despite accessor failure");
        var shutdown=new RichHudInputBridge(()=>{});var closing=new Provider();closing.Register(shutdown);
        var request=(MyTuple<string,Action<int,object>,Action,int>)shutdown.Registration();request.Item3();
        check(!shutdown.Ready&&!shutdown.Requested&&!closing.CursorEnabled,"provider shutdown clears capability and ownership");
        int callbacks=0;
        var acknowledged=new RichHudInputBridge(()=>callbacks++);var local=new Provider{Hardened=true};local.Register(acknowledged);
        check(acknowledged.Ready&&acknowledged.SupportsAcknowledgedCapture,"versioned synchronous extension recognized");
        check(acknowledged.Request()&&local.AppliedMask==7&&local.OwnMode==ProviderModes.Full&&local.CursorEnabled,"request requires synchronously applied full owned mask");
        check(!acknowledged.Owns&&acknowledged.ObservedFrames==0,"applied mask alone cannot skip cursor ownership");
        local.Callback();local.Callback();
        check(acknowledged.Owns&&acknowledged.ObservedFrames==2&&callbacks==2,"provider callbacks observe applied mask and cooperative cursor ownership");
        local.AppliedMask=0;
        local.Callback();
        check(!acknowledged.Requested&&!acknowledged.Owns&&!local.CursorEnabled&&local.OwnMode==ProviderModes.None,"revoked applied mask releases immediately");
        var pending=new RichHudInputBridge(()=>{});var delayed=new Provider{Hardened=true,ApplyImmediately=false};delayed.Register(pending);
        check(!pending.Request()&&!pending.Requested&&!delayed.CursorEnabled,"deferred engine apply does not grant a capture lease");
        var unknown=new RichHudInputBridge(()=>{});var newer=new Provider{Hardened=true,ExtensionVersion=2};newer.Register(unknown);
        check(unknown.Ready&&!unknown.SupportsAcknowledgedCapture&&!unknown.Request(),"unknown extension version retains native fallback");
        var exceptional=new RichHudInputBridge(()=>{throw new Exception("context lost");});var failed=new Provider{Hardened=true};failed.Register(exceptional);exceptional.Request();failed.Callback();
        check(!exceptional.Requested&&failed.ReleaseAppliedCount>0&&!failed.CursorEnabled&&failed.OwnMode==ProviderModes.None,"callback failure releases acknowledged provider lease");
        var queued=new RichHudInputBridge(()=>{});var queueThenReject=new Provider{Hardened=true,QueueBeforeReply=true,ApplyImmediately=false};queueThenReject.Register(queued);
        check(!queued.Request()&&!queued.Requested&&queueThenReject.QueuedMask==0&&queueThenReject.CancelledEpoch==1&&queueThenReject.OwnMode==ProviderModes.None,"false request cancels pending provider work before returning");
        var throwing=new RichHudInputBridge(()=>{});var queueThenThrow=new Provider{Hardened=true,QueueBeforeReply=true,ThrowRequestAfterQueue=true};queueThenThrow.Register(throwing);
        check(!throwing.Request()&&!throwing.Requested&&queueThenThrow.QueuedMask==0&&queueThenThrow.CancelledEpoch==1&&!queueThenThrow.CursorEnabled,"request exception cancels queued work and releases independently");
        var dormant=new RichHudInputBridge(()=>{});var noProof=new Provider{Hardened=true,ExtensionVersion=0};noProof.Register(dormant);
        check(dormant.Ready&&!dormant.SupportsAcknowledgedCapture&&!dormant.Request()&&noProof.QueuedMask==0&&!noProof.CursorEnabled,"current provider explicitly advertises unsupported capture and changes no input");
        return n;
    }
}
