using System;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRageMath;
using PbBlock=Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace Hdr.Html
{
    internal sealed class HtmlPbBridge : IHtmlPbBridge
    {
        readonly IMyProgrammableBlock caller;
        readonly IMyTerminalBlock target;
        readonly string program;
        bool cleanup;
        readonly bool sprites;
        Func<string,object[],object> draw,ui;
        internal HtmlPbBridge(IMyProgrammableBlock caller,IMyTerminalBlock target,bool sprites=false)
        {this.caller=caller;this.target=target;this.sprites=sprites;program=caller==null?null:caller.ProgramData;if(!ReadyIdentity())throw new ArgumentException("PB HTML requires a live working authorized PB and display with the requested manual content mode.");Acquire();}
        internal void Acquire()
        {
            var d=caller.GetProperty("HDR.Draw");var u=caller.GetProperty("HDR.UI");
            if(d==null||u==null)throw new ArgumentException("Requires mod: HDR API with the PB HTML query/event seams.");
            draw=d.As<Func<string,object[],object>>().GetValue(caller);ui=u.As<Func<string,object[],object>>().GetValue(caller);
            if(draw==null||ui==null||(string)draw("version",new object[0])!="HDR.Draw/1"||(string)ui("version",new object[0])!="HDR.UI/1")throw new ArgumentException("Requires compatible HDR.Draw/1 and HDR.UI/1.");
        }
        public bool Ready
        {
            get{return ReadyIdentity()&&draw!=null&&ui!=null;}
        }
        bool ReadyIdentity()
        {
            try{return caller!=null&&target!=null&&!caller.Closed&&!target.Closed&&ReferenceEquals(MyAPIGateway.Entities.GetEntityById(caller.EntityId),caller)&&ReferenceEquals(MyAPIGateway.Entities.GetEntityById(target.EntityId),target)&&caller.OwnerId!=0&&caller.IsSameConstructAs(target)&&target.HasPlayerAccess(caller.OwnerId)&&(cleanup||caller.Enabled&&caller.IsWorking&&target.IsWorking&&caller.ProgramData==program&&LcdModeCurrent());}
            catch{return false;}
        }
        bool LcdModeCurrent()
        {var panel=target as Sandbox.ModAPI.Ingame.IMyTextPanel;return panel==null||panel.ContentType==VRage.Game.GUI.TextPanel.ContentType.SCRIPT&&(sprites?string.IsNullOrEmpty(panel.Script):panel.Script=="HDR API");}
        internal void RetireOwned(Action action)
        {cleanup=true;try{action();}finally{cleanup=false;}}
        public object Draw(string operation,params object[] args)
        {
            if(!Ready)throw new ArgumentException("PB HTML caller or target was retired.");
            if(operation!="version"&&operation!="measure-text"&&operation!="geometry-cost"&&operation!="capabilities")draw("target",new object[]{(PbBlock)target});
            return draw(operation,args);
        }
        public object Ui(string operation,params object[] args)
        {if(!Ready)throw new ArgumentException("PB HTML caller or target was retired.");draw("target",new object[]{(PbBlock)target});return ui(operation,args);}
        internal object DrawSelectedScreen(string screen,string operation,params object[] args)
        {if(!Ready)throw new ArgumentException("PB HTML caller or target was retired.");draw("target",new object[]{(PbBlock)target});draw("screen",new object[]{screen});return draw(operation,args);}
    }
}
