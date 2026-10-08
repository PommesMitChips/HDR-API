using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
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
        sealed class Owner
        {
            internal IMyProgrammableBlock Caller;
            internal string Program;
            internal bool Retired;
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
            if(op=="capabilities"){a.End();return new[]{HtmlDocument.Profile,"server-layout","core-replicated-svg","lcd-console-projector","native-lcd-sprites","native-sprites-texture-relay","native-cooperative-pointer","owned-prefix-cleanup","filtered-ui-events","input-requires-client-renderer-0.9.13","relay-requires-client-renderer-0.9.13","no-javascript","no-hud","no-curved-pb-html"};}
            if(op=="clear-owned"){a.End();foreach(var doc in owner.Documents.Values)Cleanup(doc);owner.Documents.Clear();owner.Targets.Clear();return true;}
            if(op=="bind-sprites")
            {
                var anchor=a.Target();var source=a.Target() as IMyTextPanel;string html=a.Text(),css=a.Text();MatrixD pose=a.Pose();double scale=a.Has?a.Number():.005;int index=a.Has?a.Integer():0;bool owns=a.Has?a.Flag():false;a.End();
                if(index!=0)throw new ArgumentException("PB native sprite sources are physical LCD surface 0 only.");
                if(owner.Documents.Count>=4)throw new ArgumentException("PB HTML document budget reached (4 per PB).");
                int total=0;foreach(var value in owners.Values)total+=value.Documents.Count;if(total>=16)throw new ArgumentException("PB HTML shared document budget reached (16).");
                var port=new HtmlPbBridge(owner.Caller,anchor,true);
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
            if(op=="bind")
            {
                var target=a.Target();string html=a.Text(),css=a.Text();double width=a.Number(),height=a.Number();MatrixD pose=a.Pose();double scale=a.Has?a.Number():.005;a.End();
                if(owner.Documents.Count>=4)throw new ArgumentException("PB HTML document budget reached (4 per PB).");
                int total=0;foreach(var value in owners.Values)total+=value.Documents.Count;if(total>=16)throw new ArgumentException("PB HTML shared document budget reached (16).");
                var port=new HtmlPbBridge(owner.Caller,target);
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
            if(!document.Ready){Cleanup(document);owner.Documents.Remove(handle);owner.Targets.Remove(handle);throw new ArgumentException("PB HTML display/source lifecycle ended; explicitly bind again.");}
            if(op=="destroy"){a.End();Cleanup(document);owner.Documents.Remove(handle);owner.Targets.Remove(handle);return true;}
            if(op=="status"){a.End();return document.Status();}
            if(op=="backend"){a.End();return document.Backend();}
            if(op=="pointer"){double x=a.Number(),y=a.Number();bool held=a.Flag();a.End();document.Pointer(x,y,held);return true;}
            if(op=="pointer-cancel"){a.End();document.CancelPointer();return true;}
            if(op=="poll-events"){a.End();return document.PollEvents();}
            if(op=="replace"){string html=a.Text(),css=a.Text();double w=a.Number(),h=a.Number();a.End();return document.TryLoad(html,css,w,h);}
            if(op=="data"){string key=a.Text(),text=a.Text();a.End();return document.SetData(key,text);}
            if(op=="text"){string node=a.Text(),text=a.Text();a.End();return document.SetText(node,text);}
            if(op=="resize"){double w=a.Number(),h=a.Number();a.End();return document.Resize(w,h);}
            throw new ArgumentException("Unknown PB HTML command: "+op);
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
                    var lost=new List<long>();foreach(var doc in owner.Documents.Values)
                    {if(!doc.Ready){Cleanup(doc);lost.Add(doc.Handle);}else doc.Update(tick);}
                    foreach(long handle in lost){owner.Documents.Remove(handle);owner.Targets.Remove(handle);}
                }
                foreach(long id in retired)owners.Remove(id);
            }
            finally{busy=false;}
        }
        static void Cleanup(HtmlPbDocument doc)
        {var port=doc.Bridge as HtmlPbBridge;if(port!=null)port.RetireOwned(doc.Dispose);else doc.Dispose();}
        static void Retire(Owner owner){owner.Retired=true;foreach(var doc in owner.Documents.Values)Cleanup(doc);owner.Documents.Clear();owner.Targets.Clear();}
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
            internal IMyTerminalBlock Target(){var target=Next() as IMyTerminalBlock;if(target==null)throw new ArgumentException("PB HTML requires an actual LCD, Console or Projector block.");return target;}
            internal void End(){if(Has)throw new ArgumentException("Unexpected PB HTML argument.");}
        }
    }
}
