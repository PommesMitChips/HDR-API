using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game.Components;
using VRageMath;
using PbBlock=Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace Hdr.Html
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class HtmlPbSession : MySessionComponentBase
    {
        const string Property="HDR.Html",Protocol="HDR.HtmlPB/0.1";
        readonly Dictionary<long,Owner> owners=new Dictionary<long,Owner>();
        IMyTerminalControl control;
        bool active,busy;
        long nextHandle;
        int tick;
        const double AuthoringWidth=480,AuthoringHeight=280,AuthoringUnits=.005;
        const int AuthoringCharacters=131072,AuthoringPollTicks=30;
        sealed class AuthoringMount
        {
            internal long Handle;
            internal string TargetName,LastAttempted,SourceError;
            internal bool OversizedAttempt;
        }
        sealed class Owner
        {
            internal IMyProgrammableBlock Caller;
            internal string Program;
            internal bool Retired;
            internal AuthoringMount Authoring;
            internal string AuthoringError;
            internal readonly Dictionary<long,HtmlPbDocument> Documents=new Dictionary<long,HtmlPbDocument>();
            internal readonly Dictionary<long,IMyTerminalBlock> Targets=new Dictionary<long,IMyTerminalBlock>();
        }
        public override void BeforeStart()
        {
            if(active||MyAPIGateway.Multiplayer==null||!MyAPIGateway.Multiplayer.IsServer)return;
            var property=MyAPIGateway.TerminalControls.CreateProperty<Func<string,object[],object>,IMyProgrammableBlock>(Property);
            property.Getter=block=>Endpoint(block as IMyProgrammableBlock);
            MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(property);control=property;active=true;
        }
        Func<string,object[],object> Endpoint(IMyProgrammableBlock caller)
        {
            if(!active||caller==null)throw new ArgumentException("Requires the server HDR HTML PB adapter.");
            if(caller.Closed||!caller.Enabled||!caller.IsWorking||caller.OwnerId==0||!ReferenceEquals(MyAPIGateway.Entities.GetEntityById(caller.EntityId),caller))throw new ArgumentException("PB HTML requires a registered working programmable block.");
            Owner owner;
            if(owners.TryGetValue(caller.EntityId,out owner)&&(!Current(owner)||!ReferenceEquals(owner.Caller,caller))){Retire(owner);owners.Remove(caller.EntityId);owner=null;}
            if(owner==null)
            {
                if(owners.Count>=16)throw new ArgumentException("PB HTML owner budget reached (16).");
                owner=new Owner{Caller=caller,Program=caller.ProgramData};owners.Add(caller.EntityId,owner);
            }
            var captured=owner;return (operation,args)=>Command(captured,operation,args);
        }
        bool Current(Owner owner)
        {
            if(!active||owner==null||owner.Retired||owner.Caller==null||MyAPIGateway.Multiplayer==null||!MyAPIGateway.Multiplayer.IsServer)return false;
            var caller=owner.Caller;Owner current;
            return !caller.Closed&&caller.Enabled&&caller.IsWorking&&caller.OwnerId!=0&&ReferenceEquals(MyAPIGateway.Entities.GetEntityById(caller.EntityId),caller)&&caller.ProgramData==owner.Program&&owners.TryGetValue(caller.EntityId,out current)&&ReferenceEquals(current,owner);
        }
        object Command(Owner owner,string op,object[] values)
        {
            var args=new Args(values);
            if(op=="valid"){args.End();return Current(owner);}
            if(!Current(owner))throw new ArgumentException("PB HTML endpoint retired; reactivate after script/power changes.");
            if(busy)throw new ArgumentException("PB HTML does not allow reentrant updates.");
            busy=true;try{return Execute(owner,op,args);}finally{busy=false;}
        }
        object Execute(Owner owner,string op,Args a)
        {
            if(op=="version"){a.End();return Protocol;}
            if(op=="capabilities"){a.End();return new[]{HtmlDocument.Profile,"server-layout","core-replicated-svg","owned-projected-screen-binding","generic-node-source-slots","ordered-source-artwork","lcd-console-projector","native-lcd-sprites","native-sprites-texture-relay","native-cooperative-pointer","owned-prefix-cleanup","filtered-ui-events","custom-data-authoring","mod-owned-authoring-refresh","input-requires-client-renderer-0.9.13","relay-requires-client-renderer-0.9.13","no-javascript","no-hud"};}
            if(op=="run"){string name=a.Text(),command=a.Has?a.Text():"";a.End();return RunAuthoring(owner,name,command);}
            if(op=="clear-owned"){a.End();foreach(var doc in owner.Documents.Values)Cleanup(doc);owner.Documents.Clear();owner.Targets.Clear();owner.Authoring=null;owner.AuthoringError=null;return true;}
            if(op=="bind-sprites"||op=="bind-sprites-screen")
            {
                var anchor=a.Target();string screenId=op=="bind-sprites-screen"?a.Text():null;var source=a.Target() as IMyTextPanel;string html=a.Text(),css=a.Text();MatrixD pose=a.Pose();double scale=a.Has?a.Number():.005;int index=a.Has?a.Integer():0;bool owns=a.Has?a.Flag():false;a.End();
                if(index!=0)throw new ArgumentException("PB native sprite sources are physical LCD surface 0 only.");
                if(owner.Documents.Count>=4)throw new ArgumentException("PB HTML document budget reached (4 per PB).");
                int total=0;foreach(var value in owners.Values)total+=value.Documents.Count;if(total>=16)throw new ArgumentException("PB HTML shared document budget reached (16).");
                var port=new HtmlPbBridge(owner.Caller,anchor,true,screenId);
                var capabilities=(MyTuple<string,string,int>)port.Draw("capabilities",(PbBlock)anchor);
                if(capabilities.Item1!="HDR.DisplayCapabilities/1"||(capabilities.Item3&1)==0)throw new ArgumentException("Native sprites require an authorized LCD, Console or Projector anchor.");
                long nativeHandle=++nextHandle;var painter=new HtmlPbNativePainter(nativeHandle,port,owner.Caller,anchor,source,pose,scale,owns);
                HtmlPbDocument doc=null;
                try
                {
                    doc=new HtmlPbDocument(nativeHandle,port,painter){TargetKind=capabilities.Item2};
                    var size=painter.Surface.SurfaceSize;
                    if(!doc.TryLoad(html,css,size.X,size.Y))throw new ArgumentException(doc.LastError);
                    doc.Update(tick);if(doc.PublishedFrame==null)throw new ArgumentException(doc.LastError??"Native sprite publication failed.");
                    owner.Documents.Add(nativeHandle,doc);owner.Targets.Add(nativeHandle,anchor);return nativeHandle;
                }
                catch{if(doc!=null)doc.Dispose();else painter.Dispose();throw;}
            }
            if(op=="bind"||op=="bind-screen")
            {
                var target=a.Target();string screenId=op=="bind-screen"?a.Text():null;string html=a.Text(),css=a.Text();double width=a.Number(),height=a.Number();MatrixD pose=a.Pose();double scale=a.Has?a.Number():.005;a.End();
                if(owner.Documents.Count>=4)throw new ArgumentException("PB HTML document budget reached (4 per PB).");
                int total=0;foreach(var value in owners.Values)total+=value.Documents.Count;if(total>=16)throw new ArgumentException("PB HTML shared document budget reached (16).");
                var port=new HtmlPbBridge(owner.Caller,target,false,screenId);
                var capabilities=(MyTuple<string,string,int>)port.Draw("capabilities",(PbBlock)target);
                if(capabilities.Item1!="HDR.DisplayCapabilities/1"||(capabilities.Item3&1)==0||(capabilities.Item3&64)==0)throw new ArgumentException("PB HTML needs an authorized LCD, Console or Projector UI target.");
                var doc=new HtmlPbDocument(++nextHandle,port,pose,scale){TargetKind=capabilities.Item2};
                if(!doc.TryLoad(html,css,width,height)){doc.Dispose();throw new ArgumentException(doc.LastError);}
                doc.Update(tick);
                if(doc.PublishedFrame==null){string error=doc.LastError;doc.Dispose();throw new ArgumentException(error??"PB HTML initial declaration failed.");}
                owner.Documents.Add(doc.Handle,doc);owner.Targets.Add(doc.Handle,target);return doc.Handle;
            }
            long handle=a.Handle();HtmlPbDocument document;
            if(!owner.Documents.TryGetValue(handle,out document))throw new ArgumentException("PB HTML document does not belong to this live PB endpoint.");
            if(!document.Ready){RemoveDocument(owner,document,"HTML display lifecycle ended; run mount explicitly to select a display again.");throw new ArgumentException("PB HTML display/source lifecycle ended; explicitly bind again.");}
            if(op=="destroy"){a.End();RemoveDocument(owner,document,null);return true;}
            if(op=="status"){a.End();return document.Status();}
            if(op=="backend"){a.End();return document.Backend();}
            if(op=="source-capabilities"){a.End();return document.SourceCapabilities();}
            if(op=="source-status"){string node=a.Text();a.End();return document.SourceStatus(node);}
            if(op=="attach-source"){string node=a.Text(),provider=a.Text(),source=a.Text();var settings=a.Has?a.SourceSettings():null;a.End();return document.AttachSource(node,provider,source,settings,tick);}
            if(op=="detach-source"){string node=a.Text();a.End();return document.DetachSource(node,tick);}
            if(op=="pointer"){double x=a.Number(),y=a.Number();bool held=a.Flag();a.End();document.Pointer(x,y,held);return true;}
            if(op=="pointer-cancel"){a.End();document.CancelPointer();return true;}
            if(op=="poll-events"){a.End();return document.PollEvents();}
            if(op=="replace"){string html=a.Text(),css=a.Text();double w=a.Number(),h=a.Number();a.End();return document.TryLoad(html,css,w,h);}
            if(op=="data"){string key=a.Text(),text=a.Text();a.End();return document.SetData(key,text);}
            if(op=="text"){string node=a.Text(),text=a.Text();a.End();return document.SetText(node,text);}
            if(op=="resize"){double w=a.Number(),h=a.Number();a.End();return document.Resize(w,h);}
            throw new ArgumentException("Unknown PB HTML command: "+op);
        }
        string RunAuthoring(Owner owner,string name,string command)
        {
            command=(command??"").Trim().ToLowerInvariant();
            if(command=="clear")
            {
                var mounted=owner.Authoring;HtmlPbDocument doc;
                if(mounted!=null&&owner.Documents.TryGetValue(mounted.Handle,out doc))RemoveDocument(owner,doc,null);
                owner.Authoring=null;owner.AuthoringError=null;return "HTML authoring document cleared.";
            }
            if(command=="status")return AuthoringStatus(owner);
            if(command!=""&&command!="demo"&&command!="mount"&&command!="reload")return "HTML commands: mount, reload, status, clear.";
            try
            {
                if(string.IsNullOrWhiteSpace(name)||name.Length>128)throw new ArgumentException("HTML display name requires 1..128 characters.");
                HtmlPbDocument current;var mounted=owner.Authoring;
                if(mounted!=null&&mounted.TargetName==name&&owner.Documents.TryGetValue(mounted.Handle,out current))
                {
                    if(!current.Ready){RemoveDocument(owner,current,"HTML display lifecycle ended; run mount explicitly to select a display again.");throw new ArgumentException(owner.AuthoringError);}
                    ReadAuthoring(owner,current,command=="reload");current.Update(tick);return AuthoringStatus(owner);
                }
                MountAuthoring(owner,name);return AuthoringStatus(owner);
            }
            catch(ArgumentException error){owner.AuthoringError=BoundError(error.Message);return AuthoringStatus(owner);}
            catch(InvalidOperationException error){owner.AuthoringError=BoundError(error.Message);return AuthoringStatus(owner);}
        }
        void MountAuthoring(Owner owner,string name)
        {
            string markup=owner.Caller.CustomData??"";
            ValidateAuthoringSource(markup);
            var drawProperty=owner.Caller.GetProperty("HDR.Draw");
            if(drawProperty==null)throw new ArgumentException("Requires mod: HDR API with HDR.Draw/1.");
            var draw=drawProperty.As<Func<string,object[],object>>().GetValue(owner.Caller);
            if(draw==null||(string)draw("version",new object[0])!="HDR.Draw/1")throw new ArgumentException("Requires compatible HDR.Draw/1.");
            var matches=draw("finddisplays",new object[]{name}) as List<PbBlock>;
            if(matches==null||matches.Count!=1)throw new ArgumentException("Name exactly one LCD, Console or Projector '"+name+"' on this construct; found "+(matches==null?0:matches.Count)+".");
            var target=matches[0] as IMyTerminalBlock;
            if(target==null||target.CustomName!=name||target.Closed||!target.IsWorking||!ReferenceEquals(MyAPIGateway.Entities.GetEntityById(target.EntityId),target)||!owner.Caller.IsSameConstructAs(target)||!target.HasPlayerAccess(owner.Caller.OwnerId))throw new ArgumentException("HTML target must be the actual working, accessible display on this PB construct.");
            int replacing=owner.Authoring==null?0:1,total=0;
            if(owner.Documents.Count-replacing>=4)throw new ArgumentException("PB HTML document budget reached (4 per PB).");
            foreach(var entry in owners.Values)total+=entry.Documents.Count;
            if(total-replacing>=16)throw new ArgumentException("PB HTML shared document budget reached (16).");
            var port=new HtmlPbBridge(owner.Caller,target);
            var capabilities=(MyTuple<string,string,int>)port.Draw("capabilities",(PbBlock)target);
            if(capabilities.Item1!="HDR.DisplayCapabilities/1"||(capabilities.Item3&1)==0||(capabilities.Item3&64)==0)throw new ArgumentException("PB HTML needs an authorized LCD, Console or Projector UI target.");
            if(capabilities.Item2!="lcd"&&(capabilities.Item3&8)==0)throw new ArgumentException("HTML authoring requires an LCD, Console or Projector.");
            var pose=MatrixD.CreateTranslation(-AuthoringWidth*AuthoringUnits*.5,AuthoringHeight*AuthoringUnits*.5,0);
            var candidate=new HtmlPbDocument(++nextHandle,port,pose,AuthoringUnits){TargetKind=capabilities.Item2};
            try
            {
                // Parse/layout completely before changing the LCD's logical canvas or publishing artwork.
                if(!candidate.TryLoad(markup,"",AuthoringWidth,AuthoringHeight))throw new ArgumentException(candidate.LastError);
                var seeded=new HashSet<string>(StringComparer.Ordinal);
                foreach(var hit in candidate.Controller.Frame.Hits)
                    if(hit.Kind=="range"&&!string.IsNullOrEmpty(hit.Binding)&&seeded.Add(hit.Binding))candidate.SetData(hit.Binding,hit.Value.ToString("R",CultureInfo.InvariantCulture));
                if(capabilities.Item2=="lcd")
                {
                    var panel=target as Sandbox.ModAPI.Ingame.IMyTextPanel;
                    if(panel==null)throw new ArgumentException("LCD target must provide its physical panel surface.");
                    var size=panel.SurfaceSize;
                    if(!HtmlFrontendDocument.Finite(size.X)||!HtmlFrontendDocument.Finite(size.Y)||size.X<=0||size.Y<=0)throw new ArgumentException("LCD SurfaceSize is invalid.");
                    double factor=Math.Max(AuthoringWidth/size.X,AuthoringHeight/size.Y);
                    port.Draw("lcd",size.X*factor*AuthoringUnits,size.Y*factor*AuthoringUnits);
                }
                candidate.Update(tick);
                if(candidate.PublishedFrame==null)throw new ArgumentException(candidate.LastError??"HTML initial publication failed.");
                var previous=owner.Authoring;HtmlPbDocument old;
                if(previous!=null&&owner.Documents.TryGetValue(previous.Handle,out old))RemoveDocument(owner,old,null);
                owner.Documents.Add(candidate.Handle,candidate);owner.Targets.Add(candidate.Handle,target);
                owner.Authoring=new AuthoringMount{Handle=candidate.Handle,TargetName=name,LastAttempted=markup};owner.AuthoringError=null;
            }
            catch{Cleanup(candidate);throw;}
        }
        void ReadAuthoring(Owner owner,HtmlPbDocument document,bool force)
        {
            var mounted=owner.Authoring;if(mounted==null||mounted.Handle!=document.Handle)return;
            string markup=owner.Caller.CustomData??"";
            // Never retain or compare an unbounded rejected CustomData string in the mod.
            if(markup.Length>AuthoringCharacters)
            {
                mounted.LastAttempted=null;mounted.OversizedAttempt=true;mounted.SourceError="HTML Custom Data exceeds the 131072-character authoring limit.";return;
            }
            if(!force&&!mounted.OversizedAttempt&&string.Equals(markup,mounted.LastAttempted,StringComparison.Ordinal))return;
            mounted.OversizedAttempt=false;
            mounted.LastAttempted=markup;
            try
            {
                ValidateAuthoringSource(markup);
                if(!document.TryLoad(markup,"",AuthoringWidth,AuthoringHeight))throw new ArgumentException(document.LastError);
                mounted.SourceError=null;owner.AuthoringError=null;
            }
            catch(ArgumentException error){mounted.SourceError=BoundError(error.Message);}
            catch(InvalidOperationException error){mounted.SourceError=BoundError(error.Message);}
        }
        static void ValidateAuthoringSource(string markup)
        {if(markup.Length>AuthoringCharacters)throw new ArgumentException("HTML Custom Data exceeds the 131072-character authoring limit.");if(string.IsNullOrWhiteSpace(markup))throw new ArgumentException("Put an HTML document, with optional inline <style>, in this PB's Custom Data.");}
        string AuthoringStatus(Owner owner)
        {
            var mounted=owner.Authoring;HtmlPbDocument doc;
            string error=owner.AuthoringError;
            if(mounted==null||!owner.Documents.TryGetValue(mounted.Handle,out doc))return "HTML authoring is not mounted."+(string.IsNullOrEmpty(error)?" Run mount after adding HTML to Custom Data.":"\n"+error);
            var status=doc.Status();
            string output="HTML / "+mounted.TargetName+" / "+status.Item1+" / Custom Data\nPublished revision "+status.Item3+"; pending "+status.Item4.Item2+". Mod refreshes changed HTML automatically.";
            if(!string.IsNullOrEmpty(mounted.SourceError))output+="\n"+mounted.SourceError+" Last published HTML is retained.";
            if(!string.IsNullOrEmpty(error))output+="\n"+error;
            if(!string.IsNullOrEmpty(status.Item4.Item3)&&status.Item4.Item3!=mounted.SourceError)output+="\n"+status.Item4.Item3;
            return output;
        }
        static string BoundError(string error){return string.IsNullOrEmpty(error)?"HTML authoring failed.":error.Length<=512?error:error.Substring(0,512);}
        static void RemoveDocument(Owner owner,HtmlPbDocument document,string reason)
        {
            Cleanup(document);owner.Documents.Remove(document.Handle);owner.Targets.Remove(document.Handle);
            if(owner.Authoring!=null&&owner.Authoring.Handle==document.Handle){owner.Authoring=null;owner.AuthoringError=reason;}
        }
        public override void UpdateAfterSimulation()
        {
            if(!active||busy)return;tick++;busy=true;
            try
            {
                var retired=new List<long>();
                foreach(var pair in owners)
                {
                    var owner=pair.Value;if(!Current(owner)){Retire(owner);retired.Add(pair.Key);continue;}
                    var lost=new List<HtmlPbDocument>();foreach(var doc in owner.Documents.Values)
                    {if(!doc.Ready)lost.Add(doc);else{if(tick%AuthoringPollTicks==0)ReadAuthoring(owner,doc,false);doc.Update(tick);}}
                    foreach(var doc in lost)RemoveDocument(owner,doc,"HTML display lifecycle ended; run mount explicitly to select a display again.");
                }
                foreach(long id in retired)owners.Remove(id);
            }
            finally{busy=false;}
        }
        static void Cleanup(HtmlPbDocument doc)
        {var port=doc.Bridge as HtmlPbBridge;if(port!=null)port.RetireOwned(doc.Dispose);else doc.Dispose();}
        static void Retire(Owner owner){owner.Retired=true;foreach(var doc in owner.Documents.Values)Cleanup(doc);owner.Documents.Clear();owner.Targets.Clear();owner.Authoring=null;owner.AuthoringError=null;}
        protected override void UnloadData()
        {
            foreach(var owner in owners.Values)Retire(owner);owners.Clear();active=false;
            if(control!=null&&MyAPIGateway.TerminalControls!=null)MyAPIGateway.TerminalControls.RemoveControl<IMyProgrammableBlock>(control);control=null;
        }
        sealed class Args
        {
            readonly object[] values;int index;
            internal Args(object[] values){this.values=values??new object[0];if(this.values.Length>9)throw new ArgumentException("Too many PB HTML arguments.");}
            internal bool Has{get{return index<values.Length;}}
            object Next(){if(!Has)throw new ArgumentException("Missing PB HTML argument.");return values[index++];}
            internal string Text(){var text=Next() as string;if(text==null)throw new ArgumentException("PB HTML requires string arguments.");return text;}
            internal long Handle(){object value=Next();if(!(value is long))throw new ArgumentException("PB HTML handle requires Int64.");return (long)value;}
            internal double Number(){object value=Next();double n;if(value is double)n=(double)value;else if(value is int)n=(int)value;else if(value is float)n=(float)value;else throw new ArgumentException("PB HTML requires a finite number.");if(double.IsNaN(n)||double.IsInfinity(n))throw new ArgumentException("PB HTML requires a finite number.");return n;}
            internal MatrixD Pose(){object value=Next();if(!(value is MatrixD))throw new ArgumentException("PB HTML pose requires MatrixD.");return (MatrixD)value;}
            internal int Integer(){object value=Next();if(!(value is int))throw new ArgumentException("Native sprite surface index requires Int32.");return (int)value;}
            internal bool Flag(){object value=Next();if(!(value is bool))throw new ArgumentException("Native sprite ownership/pointer arguments require Boolean.");return (bool)value;}
            internal MyTuple<string,object[]>[] SourceSettings(){var value=Next() as MyTuple<string,object[]>[];if(value==null)throw new ArgumentException("Source settings require MyTuple<string,object[]>[].");return value;}
            internal IMyTerminalBlock Target(){var target=Next() as IMyTerminalBlock;if(target==null)throw new ArgumentException("PB HTML requires an actual LCD, Console or Projector block.");return target;}
            internal void End(){if(Has)throw new ArgumentException("Unexpected PB HTML argument.");}
        }
    }
}
