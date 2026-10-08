using System;
using System.Globalization;
using Sandbox.ModAPI;
using VRageMath;

namespace Hdr.Html
{
    public sealed partial class HtmlFrontendSession
    {
        const string DemoOwner="prototype-demo";
        internal const string DemoMarkup="<main id='panel'><h2>HDR HTML</h2><p>Local HTML/CSS - no browser plugin</p><p>Tick <span>{{clock}}</span></p><label>Level</label><input id='level' type='range' min='0' max='100' step='1' value='60' data-bind='level' data-action='level'/><button id='reset' data-action='reset'>Reset</button><p id='note'>Input supplied by an owned GUI.</p></main>";
        internal const string DemoStylesheet="main {width:440px;height:250px;padding:12px;background-color:#123442;color:#00d0cd;font-size:14px;line-height:1.3;} h2 {font-size:20px;margin-bottom:8px;} p {margin-bottom:8px;} input {width:220px;height:24px;} button {width:100px;height:28px;padding:4px;background-color:#245a67;margin-top:6px;}";
        HtmlFrontendOwner demoOwner;
        long demoDocument;
        int demoCount;
        void DemoChat(string message,ref bool sendToOthers)
        {
            if(message==null||!message.StartsWith("/hdrhtml",StringComparison.OrdinalIgnoreCase))return;
            if(message.Length>8&&!char.IsWhiteSpace(message[8]))return;
            sendToOthers=false;
            try
            {
                string[] words=message.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                string command=words.Length>1?words[1].ToLowerInvariant():"status";
                if(command=="clear")
                {
                    if(demoOwner!=null&&Current(demoOwner))Command(demoOwner,"release",new object[0]);
                    ClearDemoReferences();Notice("Demo cleared; other mods' documents are unaffected.");return;
                }
                if(command=="status")
                {
                    if(demoOwner==null||!Current(demoOwner)||demoDocument==0){Notice("No demo. Commands: /hdrhtml hud [vector|svg], world [vector|svg], status, clear. Profile1; no JavaScript. Consumer owns pointer capture.");return;}
                    HtmlFrontendDocument doc; if(!demoOwner.Documents.TryGetValue(demoDocument,out doc)){Notice("Demo document was retired.");return;}
                    var state=doc.Status(demoOwner.Core.Ready);Notice(state.Item1+"; desired/visible "+state.Item2+"/"+state.Item3+"; layout builds "+state.Item5+(state.Item4.Item3.Length==0?"":"; "+state.Item4.Item3));return;
                }
                if(command!="hud"&&command!="world")throw new ArgumentException("Commands: hud, world, clear, status.");
                string backend=words.Length>2?words[2].ToLowerInvariant():"vector";
                Service("open",new object[]{DemoOwner});demoOwner=owners[DemoOwner];demoCount=0;
                if(command=="hud")demoDocument=(long)Command(demoOwner,"create-hud",new object[]{DemoMarkup,DemoStylesheet,480.0,280.0,backend,0});
                else
                {
                    var camera=MyAPIGateway.Session==null?null:MyAPIGateway.Session.Camera;
                    if(camera==null)throw new ArgumentException("A local world camera is required.");
                    MatrixD view=camera.WorldMatrix;double scale=.005;
                    Vector3D topLeft=view.Translation+view.Forward*3-view.Right*240*scale+view.Up*140*scale;
                    MatrixD pose=MatrixD.CreateWorld(topLeft,view.Forward,view.Up);
                    demoDocument=(long)Command(demoOwner,"create-world",new object[]{DemoMarkup,DemoStylesheet,480.0,280.0,pose,scale,backend,0});
                }
                Notice("Profile1 "+command+" demo created using "+backend+". No game controls are captured. Clear with /hdrhtml clear.");
            }
            catch(Exception error){Notice(error.Message);}
        }
        void UpdateDemo()
        {
            if(demoOwner==null||!Current(demoOwner)||demoDocument==0)return;
            HtmlFrontendDocument doc;if(!demoOwner.Documents.TryGetValue(demoDocument,out doc))return;
            if(tick%60==0)doc.SetData("clock",(++demoCount).ToString(CultureInfo.InvariantCulture));
            foreach(var item in doc.PollEvents())
            {
                if(item.Item1=="click"&&item.Item3=="reset"){demoCount=0;doc.SetData("clock","0");}
                if(item.Item1=="change"&&item.Item3=="level")doc.SetData("level",item.Item4.Item1.ToString("R",CultureInfo.InvariantCulture));
            }
        }
        void ClearDemoReferences(){demoOwner=null;demoDocument=0;demoCount=0;}
    }
}
