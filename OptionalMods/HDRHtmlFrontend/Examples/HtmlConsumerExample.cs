using System;
using System.Globalization;
using Hdr.Mods;
using VRageMath;

namespace HdrHtmlExamples
{
    /// <summary>Call from the consumer's own session/GUI. This adapter never acquires game input.</summary>
    public sealed class HtmlConsumerExample : IDisposable
    {
        const string Markup="<main><h2>Panel</h2><p id='status'>Ready</p><input id='level' type='range' min='0' max='100' step='1' value='50' data-bind='level' data-action='level'/><button id='apply' data-action='apply'>Apply</button></main>";
        const string Css="main {padding:12px;width:300px;height:190px;background-color:#123442;color:#00d0cd;font-size:14px;} h2 {font-size:20px;} input {width:220px;height:24px;} button {width:80px;height:28px;background-color:#245a67;}";
        readonly HdrHtmlApi api=new HdrHtmlApi("example.html-panel");
        long document,generation;
        int tick;
        public double Level{get;private set;}
        public string LastError{get;private set;}
        public void Update()
        {
            if(++tick%60==0)api.Request();
            if(!api.Ready)return;
            try
            {
                if(generation!=api.ConnectionGeneration){generation=api.ConnectionGeneration;document=0;}
                if(document==0)document=api.CreateHud(Markup,Css,340,220,"vector");
                foreach(var item in api.PollEvents(document))
                {
                    if(item.Item1=="change"&&item.Item3=="level")
                    {Level=item.Item4.Item1;api.SetData(document,"level",Level.ToString("R",CultureInfo.InvariantCulture));}
                    if(item.Item1=="click"&&item.Item3=="apply")api.SetText(document,"status","Applied "+Level.ToString(CultureInfo.InvariantCulture));
                }
                LastError=api.Status(document).Item4.Item3;
            }
            catch(Exception error){LastError=error.Message;}
        }
        // The caller's GUI must already own/block this input. X/Y are HTML viewport pixels.
        public void OnOwnedUiPointer(double x,double y,bool held,bool guiOwnsFocus)
        {
            if(!api.Ready||generation!=api.ConnectionGeneration||document==0)return;
            if(guiOwnsFocus)api.Pointer(document,x,y,held);else api.CancelPointer(document);
        }
        public void Dispose(){api.Dispose();document=0;}
    }
}
