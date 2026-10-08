using System;
using System.Collections.Generic;
using Hdr.Mods;
using Sandbox.ModAPI;
using VRage.Game.Components;

namespace Hdr.Html
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed partial class HtmlFrontendSession : MySessionComponentBase
    {
        internal const long DiscoveryChannel=481770150,RequestChannel=481770151;
        internal const string Protocol="HDR.Html/0.1";
        const int MaxOwners=8,MaxDocuments=4;
        const string NativeAuthorityError="Native LCD content mode or surface dimensions changed; input is inactive.";
        readonly Dictionary<string,HtmlFrontendOwner> owners=new Dictionary<string,HtmlFrontendOwner>(StringComparer.Ordinal);
        readonly List<HtmlNativeLease> nativeLeases=new List<HtmlNativeLease>();
        Func<string,object[],object> service;
        bool active,busy;
        long generation,nextDocument;
        int tick;
        internal sealed class HtmlFrontendOwner : IDisposable
        {
            internal string Id;
            internal long Generation,CoreGeneration=-1;
            internal bool Released;
            internal HdrModApi Core;
            internal readonly Dictionary<long,HtmlFrontendDocument> Documents=new Dictionary<long,HtmlFrontendDocument>();
            public void Dispose(){Released=true;foreach(var doc in Documents.Values)doc.Dispose();Documents.Clear();if(Core!=null)Core.Dispose();Core=null;}
        }
        sealed class HtmlNativeLease
        {internal Sandbox.ModAPI.Ingame.IMyTextSurface Surface;internal HtmlFrontendOwner Owner;internal long Document;internal object Token;}
        void RetireNativeLeases(HtmlFrontendOwner owner,long document=0)
        {for(int i=nativeLeases.Count-1;i>=0;i--)if(ReferenceEquals(nativeLeases[i].Owner,owner)&&(document==0||nativeLeases[i].Document==document)){var lease=nativeLeases[i];HtmlPbNativeSurfaceClaims.Release(lease.Surface,lease.Token);nativeLeases.RemoveAt(i);}}
        public override void BeforeStart()
        {
            if(active||MyAPIGateway.Utilities==null||MyAPIGateway.Utilities.IsDedicated)return;
            active=true;service=Service;
            MyAPIGateway.Utilities.RegisterMessageHandler(RequestChannel,ReceiveRequest);
            MyAPIGateway.Utilities.MessageEntered+=DemoChat;
            try{MyAPIGateway.Utilities.SendModMessage(DiscoveryChannel,service);}catch{}
        }
        void ReceiveRequest(object value)
        {var callback=value as Action<Func<string,object[],object>>;if(!active||callback==null)return;try{callback(service);}catch{}}
        public override void UpdateAfterSimulation()
        {
            if(!active||busy)return;tick++;busy=true;
            try
            {
                foreach(var owner in owners.Values)
                {
                    if(owner.Released)continue;
                    bool ready=false;
                    try
                    {
                        if(tick%60==0)owner.Core.Request();ready=owner.Core.Ready;
                        if(ready&&owner.CoreGeneration!=owner.Core.ConnectionGeneration)
                        {
                            owner.CoreGeneration=owner.Core.ConnectionGeneration;
                            foreach(var document in owner.Documents.Values)if(document.RequiresHdr){document.Suspend("HDR endpoint replaced; rebuilding owned document.");document.RebuildForRenderer();}
                        }
                    }
                    catch{ready=false;}
                    bool enabled=ready&&CoreRenderingEnabled(owner);
                    foreach(var document in owner.Documents.Values)
                    {
                        bool input=document.RequiresHdr?enabled:NativeSurfaceCurrent(owner,document);
                        document.SetInputEnabled(input);
                        if(!document.RequiresHdr&&!input&&!NativeAuthorityCurrent(owner,document))document.LastError=NativeAuthorityError;
                        else if(!document.RequiresHdr&&input&&document.LastError==NativeAuthorityError)document.LastError=document.Controller.LastError;
                        document.Update(tick,ready);
                    }
                }
                UpdateDemo();
            }
            catch(Exception error){Notice("Frontend update: "+error.Message);}
            finally{busy=false;}
        }
        protected override void UnloadData()
        {
            active=false;service=null;
            if(MyAPIGateway.Utilities!=null)
            {
                try{MyAPIGateway.Utilities.UnregisterMessageHandler(RequestChannel,ReceiveRequest);}catch{}
                try{MyAPIGateway.Utilities.MessageEntered-=DemoChat;}catch{}
            }
            foreach(var owner in owners.Values)try{owner.Dispose();}catch{}
            foreach(var lease in nativeLeases)HtmlPbNativeSurfaceClaims.Release(lease.Surface,lease.Token);
            owners.Clear();nativeLeases.Clear();ClearDemoReferences();
        }
        bool Current(HtmlFrontendOwner owner)
        {HtmlFrontendOwner current;return active&&!owner.Released&&owners.TryGetValue(owner.Id,out current)&&ReferenceEquals(current,owner)&&current.Generation==owner.Generation;}
        void RequireIdle(){if(busy)throw new ArgumentException("Call HDR HTML on the client simulation thread outside its update/render callback.");}
        static bool CoreRenderingEnabled(HtmlFrontendOwner owner)
        {try{return owner.Core.Ready&&(bool)owner.Core.Call("rendering-enabled");}catch{return false;}}
        bool NativeSurfaceCurrent(HtmlFrontendOwner owner,HtmlFrontendDocument doc)
        {return doc.VisibleFrame!=null&&NativeAuthorityCurrent(owner,doc);}
        bool NativeAuthorityCurrent(HtmlFrontendOwner owner,HtmlFrontendDocument doc)
        {
            foreach(var lease in nativeLeases)if(ReferenceEquals(lease.Owner,owner)&&lease.Document==doc.Handle)
            {
                try
                {
                    var surface=lease.Surface;var frame=doc.VisibleFrame??doc.Controller.Frame;
                    return frame!=null&&surface.ContentType==VRage.Game.GUI.TextPanel.ContentType.SCRIPT&&string.IsNullOrEmpty(surface.Script)&&Math.Abs(surface.SurfaceSize.X-frame.Width)<=.001&&Math.Abs(surface.SurfaceSize.Y-frame.Height)<=.001;
                }
                catch{return false;}
            }
            return false;
        }
        bool InputCurrent(HtmlFrontendOwner owner,HtmlFrontendDocument doc)
        {return doc.RequiresHdr?CoreRenderingEnabled(owner):NativeSurfaceCurrent(owner,doc);}
        static void Notice(string text){try{MyAPIGateway.Utilities.ShowMessage("HDR HTML",text);}catch{}}
    }
}
