// Whole PB script. Name an LCD "HDR UI" and select Content -> HDR API.
Func<string,object[],object> _draw,_ui;
object H(string op,params object[] a){if(_draw==null)_draw=Me.GetProperty("HDR.Draw").As<Func<string,object[],object>>().GetValue(Me);return _draw(op,a);}
object U(string op,params object[] a){if(_ui==null)_ui=Me.GetProperty("HDR.UI").As<Func<string,object[],object>>().GetValue(Me);return _ui(op,a);}
public void Main(string argument,UpdateType source)
{
    try
    {
        H("target","HDR UI");
        if(argument=="clear"){H("clear");return;}
        if(argument=="ui:status")
        {
            H("text","status","PB button worked!",0,-0.8,0,0.08,"cyan");
            Echo("Server-validated button invoked this PB.");return;
        }
        H("lcd",2,2);H("clear");H("view",0,0,0);
        H("circle","map",0,0,0,0.75,"#004750",64);H("layer","map","map");
        H("text","title","HDR UI",0,0.85,0,0.12,"white");
        U("bundle","main",true);U("bundle","context",false);
        U("button","run","main",0,0.45,1.3,0.22,"Run PB command","pb","ui:status");
        U("button","toggle","main",0,0.10,1.3,0.22,"Toggle map layer","toggle","map");
        U("button","menu","main",0,-0.25,1.3,0.22,"Open context menu","menu","context");
        U("button","context-run","context",0,0.45,1.5,0.26,"Context: run PB","pb","ui:status");
        U("button","context-map","context",0,0.10,1.5,0.26,"Context: toggle map","toggle","map");
        H("layer-order","ui-main",10);H("layer-order","ui-context",20);
        Echo("On foot, aim at a button and press Use (F). Escape closes the context menu.");
        Echo((string)U("capabilities"));
    }
    catch(Exception e){Echo("Enable HDR API, reload and select HDR API Content. "+e.Message);}
}
