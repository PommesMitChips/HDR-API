using System;
using Sandbox.ModAPI;
using IMyTextSurface=Sandbox.ModAPI.Ingame.IMyTextSurface;
using VRage;
using VRageMath;

namespace Hdr.Mods
{
    /// <summary>Copy into a consumer mod. Local HTML profile frontend; no browser, JavaScript or automatic input capture.</summary>
    public sealed class HdrHtmlApi : IDisposable
    {
        public const long DiscoveryChannel=481770150,RequestChannel=481770151;
        readonly string owner;
        Func<string,object[],object> service,endpoint;
        bool disposed;
        long acquisition;
        public long ConnectionGeneration{get;private set;}
        public HdrHtmlApi(string uniqueOwnerId)
        {owner=uniqueOwnerId;MyAPIGateway.Utilities.RegisterMessageHandler(DiscoveryChannel,Receive);Request();}
        public bool Ready
        {
            get
            {
                if(disposed||endpoint==null)return false;
                var prior=endpoint;long generation=ConnectionGeneration;
                try{bool valid=(bool)prior("valid",new object[0]);if(disposed||!ReferenceEquals(prior,endpoint)||generation!=ConnectionGeneration)return false;if(valid)return true;endpoint=null;}
                catch{if(ReferenceEquals(prior,endpoint)&&generation==ConnectionGeneration)endpoint=null;}
                return false;
            }
        }
        void Receive(object value)
        {
            var candidate=value as Func<string,object[],object>;
            if(disposed||candidate==null||ReferenceEquals(candidate,service)&&Ready)return;
            long ticket=++acquisition;
            try
            {
                if((string)candidate("version",new object[0])!="HDR.Html/0.1")return;
                var acquired=candidate("open",new object[]{owner}) as Func<string,object[],object>;
                if(acquired==null)return;
                if(disposed||ticket!=acquisition){try{acquired("release",new object[0]);}catch{}return;}
                // A reconnect always creates a new owner generation. Stale handles cannot target its successor.
                ReleaseEndpoint();
                if(disposed||ticket!=acquisition){try{acquired("release",new object[0]);}catch{}return;}
                service=candidate;endpoint=acquired;ConnectionGeneration++;
            }
            catch{}
        }
        public void Request(){if(!disposed)try{MyAPIGateway.Utilities.SendModMessage(RequestChannel,new Action<Func<string,object[],object>>(value=>Receive(value)));}catch{}}
        public object Call(string command,params object[] args)
        {
            if(!Ready)throw new InvalidOperationException("Requires mod: HDR HTML Frontend (client service unavailable).");
            var prior=endpoint;long generation=ConnectionGeneration;object result=prior(command,args);
            if(disposed||!ReferenceEquals(prior,endpoint)||generation!=ConnectionGeneration)throw new InvalidOperationException("HDR HTML endpoint changed during the command; rebuild owned documents.");
            if(command=="release")endpoint=null;return result;
        }
        public bool TryCall(string command,out object result,out string reason,params object[] args)
        {result=null;reason=null;try{result=Call(command,args);return true;}catch(Exception error){reason=error.Message;return false;}}
        public string[] Capabilities()
        {
            if(!Ready)return new string[0];var before=endpoint;long generation=ConnectionGeneration;
            var result=(string[])service("capabilities",new object[0]);
            if(disposed||!ReferenceEquals(before,endpoint)||generation!=ConnectionGeneration)throw new InvalidOperationException("HDR HTML endpoint changed during capabilities query.");
            return result;
        }
        public long CreateHud(string markup,string css,double width,double height,string backend="vector",int order=0)
        {return (long)Call("create-hud",markup,css,width,height,backend,order);}
        /// <summary>World pose is the document's top-left origin; local +X is right and local -Y is down.</summary>
        public long CreateWorld(string markup,string css,double width,double height,MatrixD pose,double metresPerPixel,string backend="vector",int order=0)
        {return (long)Call("create-world",markup,css,width,height,pose,metresPerPixel,backend,order);}
        /// <summary>Consumer must own this surface, select SCRIPT and clear its selected text-surface script first.</summary>
        public long CreateNativeLcd(string markup,string css,IMyTextSurface ownedSurface,bool callerOwnsSurface)
        {return (long)Call("create-lcd",markup,css,ownedSurface,callerOwnsSurface);}
        public bool Replace(long document,string markup,string css,double width,double height){return (bool)Call("replace",document,markup,css,width,height);}
        public bool SetData(long document,string key,string value){return (bool)Call("data",document,key,value);}
        public bool SetText(long document,string nodeId,string text){return (bool)Call("text",document,nodeId,text);}
        public bool Resize(long document,double width,double height){return (bool)Call("resize",document,width,height);}
        /// <summary>Call only from a consumer-owned GUI which already prevents game input from using this press.</summary>
        public void Pointer(long document,double x,double y,bool pressed){Call("pointer",document,x,y,pressed);}
        public void CancelPointer(long document){Call("pointer-cancel",document);}
        public MyTuple<string,string,string,MyTuple<double,long>>[] PollEvents(long document)
        {return (MyTuple<string,string,string,MyTuple<double,long>>[])Call("poll-events",document);}
        /// <summary>Backend, desired revision, visible revision, (renderer ready,pending paint,error), layout build count.</summary>
        public MyTuple<string,long,long,MyTuple<bool,bool,string>,long> Status(long document)
        {return (MyTuple<string,long,long,MyTuple<bool,bool,string>,long>)Call("status",document);}
        public void Destroy(long document){Call("destroy",document);}
        public void ClearOwned(){Call("clear-owned");}
        void ReleaseEndpoint(){var old=endpoint;endpoint=null;if(old!=null)try{old("release",new object[0]);}catch{}}
        public void Dispose(){if(disposed)return;disposed=true;acquisition++;if(MyAPIGateway.Utilities!=null)try{MyAPIGateway.Utilities.UnregisterMessageHandler(DiscoveryChannel,Receive);}catch{}ReleaseEndpoint();service=null;}
    }
}
