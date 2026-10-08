using System.Collections;
using System.Reflection;
using Hdr.Html;
using VRageMath;

internal static partial class PbTests
{
    const string AuthoredHtml="<!DOCTYPE html><html><head><style>main {width:440px;height:240px;padding:12px;background:#123442;color:#00d0cd;} p {height:24px;} input {width:220px;height:24px;} button {width:100px;height:28px;}</style></head><body><main><p id='caption'>Level {{level}}</p><input id='level' type='range' min='0' max='100' step='1' value='60' data-bind='level'/><button id='go' data-action='go'>Go</button></main></body></html>";
    static string OverfullControls(){return "<style>button {height:4px;width:40px;padding:0;margin:0;font-size:1px;line-height:1;}</style>"+string.Concat(Enumerable.Range(0,33).Select(i=>"<button id='bad-"+i+"'>Button</button>"));}
    static string Author(Func<string,object[],object> api,string command="",string name="HTML Display")
    {return (string)api("run",new object[]{name,command});}
    static IDictionary AuthorDocuments(HtmlPbSession session)
    {var owners=(IDictionary)ClientReplicationTests.Field(session,"owners");return (IDictionary)ClientReplicationTests.Field(owners[10L],"Documents");}
    static long AuthorHandle(HtmlPbSession session)
    {var owners=(IDictionary)ClientReplicationTests.Field(session,"owners");var record=ClientReplicationTests.Field(owners[10L],"Authoring");return record==null?0L:(long)ClientReplicationTests.Field(record,"Handle");}
    static HtmlPbDocument AuthorDoc(HtmlPbSession session)
    {long handle=AuthorHandle(session);return handle==0?null:(HtmlPbDocument)AuthorDocuments(session)[handle];}
    static void Advance(HtmlPbSession session,int ticks)
    {for(int i=0;i<ticks;i++)session.UpdateAfterSimulation();}
    static void RunAuthoring()
    {
        Run("plain CustomData HTML mounts via one public call on each supported display", AuthoringMountAdmission);
        Run("mod-owned authoring refresh coalesces edits and retains invalid source diagnostics", AuthoringRefreshAndRecovery);
        Run("authoring idempotence, bounded document count and scoped clear", AuthoringScopeAndBudgets);
        Run("authoring exact discovery/access/content guards precede publication", AuthoringDiscoveryGuards);
        Run("authoring target loss and PB lifetime do not auto rebind", AuthoringLifetime);
    }
    static void AuthoringMountAdmission()
    {
        string samplePath=typeof(PbTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a=>a.Key=="PlainHtmlPbDemoPath").Value;
        string plainSample=File.ReadAllText(samplePath);
        foreach(string kind in new[]{"projector","console","lcd"})
        {
            using var f=new PbFixture(kind=="lcd");if(kind=="console")f.TargetSubtype="LargeConsole";
            f.CustomData=plainSample;f.TargetSurfaceSize=new Vector2(512,256);f.CaptureHtmlProperty();var session=new HtmlPbSession();session.BeforeStart();var api=f.HtmlEndpoint();
            try
            {
                string status=Author(api);Check(status.Contains("HTML Display")&&status.Contains(kind),"Printable public status identifies actual "+kind+" target: "+status);
                var doc=AuthorDoc(session);Check(doc!=null&&doc.PublishedFrame!=null,"One call publishes real HTML document without PB update loop on "+kind);
                Near(480,doc.PublishedFrame.Width,"Mod owns default viewport width");Near(280,doc.PublishedFrame.Height,"Mod owns default viewport height");
                Check(doc.PublishedFrame.Operations.Any(op=>op.Kind=="text"&&op.Text=="60"),"Actual shipped plain HTML range value populates {{level}} before first publication on "+kind);
                var range=Range(f);long lease=f.Begin(range);f.Move(lease,74);session.UpdateAfterSimulation();
                Check(doc.PublishedFrame.Operations.Any(op=>op.Kind=="text"&&op.Text=="74"),"Actual shipped plain HTML range updates its data binding modside on "+kind);
                Equal("html-pb-fixture",f.Program,"Mount never rewrites PB program");Equal(0,f.TryRuns,"HTML does not execute PB actions");
                if(kind=="lcd")
                {
                    var scenes=(IDictionary)ClientReplicationTests.Field(f.Core,"_scenes");var scene=scenes[20L];
                    Near(2.8,(double)ClientReplicationTests.Field(scene,"LcdWidth"),"Wide LCD canvas aspect preserves authored viewport without stretching");
                    Near(1.4,(double)ClientReplicationTests.Field(scene,"LcdHeight"),"Wide LCD canvas leaves letterbox for480x280");
                    Equal("HDR API",f.LcdScript,"Mount retains manually selected physical LCD renderer");
                }
            }
            finally{session.UnloadDataConditional();}
        }
    }
    static void AuthoringRefreshAndRecovery()
    {
        using var f=new PbFixture();f.CustomData=AuthoredHtml;f.CaptureHtmlProperty();var session=new HtmlPbSession();session.BeforeStart();var api=f.HtmlEndpoint();
        try
        {
            Author(api);var doc=AuthorDoc(session);long layout=doc.Controller.LayoutBuildCount;int reads=f.CustomDataReads,cost=f.CostCalls,mutations=f.Mutations;
            Advance(session,120);Equal(layout,doc.Controller.LayoutBuildCount,"Unchanged mod polls never rebuild layout");Equal(cost,f.CostCalls,"Unchanged polls never compile geometry");Equal(mutations,f.Mutations,"Unchanged polls never publish artwork");Equal(reads+4,f.CustomDataReads,"CustomData read coalesces to one per30mod ticks");
            f.CustomData=AuthoredHtml.Replace("Level {{level}}","Intermediate {{level}}");Advance(session,10);
            f.CustomData=AuthoredHtml.Replace("Level {{level}}","Final {{level}}");Advance(session,20);
            Equal(layout+1,doc.Controller.LayoutBuildCount,"Multiple edits coalesce into final document once");Check(f.Sources().Values.Any(v=>v!=null&&v.Contains("Final 60",StringComparison.Ordinal)),"Watcher publishes latest authored document, no manualPB execution");
            var retained=f.Sources();long published=doc.PublishedFrame.Revision,desired=doc.Controller.Revision;layout=doc.Controller.LayoutBuildCount;cost=f.CostCalls;
            f.CustomData="<script>bad()</script>";Advance(session,30);string error=Author(api,"status");Check(error.Contains("script",StringComparison.OrdinalIgnoreCase)&&error.Contains("retained"),"Rejected CustomData returns persistent actionable status");SourcesEqual(retained,f.Sources(),"Rejected source retains last real Core publication");Equal(published,doc.PublishedFrame.Revision,"Rejected source keeps committed visible revision");Equal(desired,doc.Controller.Revision,"Parser rejection retains last desired document");
            Advance(session,120);Equal(layout,doc.Controller.LayoutBuildCount,"Same rejected source is not parsed/layout repeatedly");Equal(cost,f.CostCalls,"Same rejected source produces no geometry work");
            long lease=f.Begin(Range(f));f.Move(lease,80);session.UpdateAfterSimulation();Check(Author(api,"status").Contains("script",StringComparison.OrdinalIgnoreCase),"Successful range refresh does not erase authoring-source error");
            retained=f.Sources();published=doc.PublishedFrame.Revision;desired=doc.Controller.Revision;layout=doc.Controller.LayoutBuildCount;
            f.CustomData=OverfullControls();Advance(session,30);
            string layoutError=Author(api,"status");Check(layoutError.Contains("retained")&&layoutError.Contains("limit",StringComparison.OrdinalIgnoreCase),"Layout rejection is reported separately from successful range refresh: "+layoutError);Equal(desired,doc.Controller.Revision,"Rejected layout retains last desired document");Equal(published,doc.PublishedFrame.Revision,"Rejected layout retains last published frame");SourcesEqual(retained,f.Sources(),"Layout failure leaves actual Core sources unchanged");Equal(layout+1,doc.Controller.LayoutBuildCount,"One changed valid parse attempts one failing layout");
            Advance(session,120);Equal(layout+1,doc.Controller.LayoutBuildCount,"Unchanged layout-failing source is not retried every poll");
            f.CustomData=AuthoredHtml.Replace("Level {{level}}","Recovered {{level}}");Advance(session,30);string recovered=Author(api,"status");Check(!recovered.Contains("script",StringComparison.OrdinalIgnoreCase)&&!recovered.Contains("retained"),"Fixed source clears rejection diagnostic");Check(f.Sources().Values.Any(v=>v!=null&&v.Contains("Recovered 80",StringComparison.Ordinal)),"Recovery preserves runtime range state through valid reload");
            long revision=doc.PublishedFrame.Revision;f.CustomData=new string('x',131073);Advance(session,30);Check(Author(api,"status").Contains("131072"),"Overlong CustomData rejected with bounded-source reason");Equal(revision,doc.PublishedFrame.Revision,"Overlong source cannot erase last display");
            f.CustomData=AuthoredHtml;Author(api,"reload");Check(!Author(api,"status").Contains("131072"),"Explicit reload recovers without waiting for watcher");
        }
        finally{session.UnloadDataConditional();}
    }
    static void AuthoringScopeAndBudgets()
    {
        using var f=new PbFixture();f.CustomData=AuthoredHtml;f.CaptureHtmlProperty();var session=new HtmlPbSession();session.BeforeStart();var api=f.HtmlEndpoint();
        try
        {
            long manual=Bind(api,f,"<p>Independent document</p>","p {height:24px;}");var manualSources=f.Sources();Author(api);long handle=AuthorHandle(session);
            for(int i=0;i<50;i++)Author(api);Equal(handle,AuthorHandle(session),"Repeated short call owns same authoring handle");Equal(2,AuthorDocuments(session).Count,"Repeated calls allocate exactly one authoring document besides manual document");
            for(int i=0;i<5;i++){f.CustomData=AuthoredHtml.Replace("Level {{level}}","Revision"+i+" {{level}}");Author(api,"reload");}Equal(2,AuthorDocuments(session).Count,"Repeated valid reloads do not consume another document allowance");
            var manualBefore=Status(api,manual);Author(api,"clear");Equal(1,AuthorDocuments(session).Count,"Short clear only removes authoring document");Equal(manualBefore.Item3,Status(api,manual).Item3,"Manual low-level document retained after authoring clear");SourcesEqual(manualSources,f.Sources(),"Manual artwork survives authoring cleanup");
            Author(api);handle=AuthorHandle(session);api("destroy",new object[]{handle});Check(AuthorDoc(session)==null,"Manual destroy clears corresponding authoring record");Advance(session,60);Equal(1,AuthorDocuments(session).Count,"Watcher cannot resurrect explicitly destroyed authoring document");
            Bind(api,f,"<p>Two</p>","");Bind(api,f,"<p>Three</p>","");Author(api);Equal(4,AuthorDocuments(session).Count,"Three manual plus one authoring doc obey existing budget");
            for(int i=0;i<10;i++)Author(api,"reload");Equal(4,AuthorDocuments(session).Count,"Reload at full per-PB budget preserves the existing authoring slot");
            Author(api,"clear");Bind(api,f,"<p>Four</p>","");string rejected=Author(api);Check(rejected.Contains("budget"),"New mount rejected when four manual documents fill existing allowance");Equal(4,AuthorDocuments(session).Count,"Budget failure preserves all existing documents");
            api("clear-owned",new object[0]);Check(AuthorDoc(session)==null&&AuthorDocuments(session).Count==0,"Low-level clear-owned invalidates authoring record");
        }
        finally{session.UnloadDataConditional();}
    }
    static void AuthoringDiscoveryGuards()
    {
        foreach(string failure in new[]{"missing","duplicate","different-name","access","construct","working","empty","oversize","lcd-mode","invalid-html","invalid-layout"})
        {
            using var f=new PbFixture(failure=="lcd-mode");f.CustomData=AuthoredHtml;
            switch(failure){case "missing":f.MatchingTargetCount=0;break;case "duplicate":f.MatchingTargetCount=2;break;case "different-name":f.TargetName="Other";break;case "access":f.Access=false;break;case "construct":f.SameConstruct=false;break;case "working":f.TargetWorking=false;break;case "empty":f.CustomData="";break;case "oversize":f.CustomData=new string('x',131073);break;case "lcd-mode":f.LcdScript="Foreign";break;case "invalid-html":f.CustomData="<script>x()</script>";break;case "invalid-layout":f.CustomData=OverfullControls();break;}
            f.CaptureHtmlProperty();var session=new HtmlPbSession();session.BeforeStart();var api=f.HtmlEndpoint();
            try{string status=Author(api);Check(status.Contains("not mounted"),"Guarded mount reports printable failure for"+failure+": "+status);Equal(0,AuthorDocuments(session).Count,"Rejected"+failure+" mounts create no document");Equal(0,f.Items.Count,"Rejected"+failure+" mounts emit no retained artwork");Check(!f.Commands.Contains("draw:lcd"),"Preflight"+failure+" failure does not change logical LCD canvas");Check(!f.Commands.Contains("draw:clear"),"Mount never clears unrelated Core objects");}
            finally{session.UnloadDataConditional();}
        }
        using var good=new PbFixture();good.CustomData=AuthoredHtml;good.CaptureHtmlProperty();var s=new HtmlPbSession();s.BeforeStart();var endpoint=good.HtmlEndpoint();
        try{Author(endpoint);int mutations=good.Mutations;string unknown=Author(endpoint,"launch arbitrary-action");Check(unknown.Contains("commands"),"Unknown authoring command lists explicit command set");Equal(mutations,good.Mutations,"Unknown commands never invoke gameplay or repaint");}
        finally{s.UnloadDataConditional();}
    }
    static void AuthoringLifetime()
    {
        foreach(string failure in new[]{"target-off","target-replaced","caller-replaced","PB-off","program-changed"})
        {
            using var f=new PbFixture();f.CustomData=AuthoredHtml;f.CaptureHtmlProperty();var session=new HtmlPbSession();session.BeforeStart();var api=f.HtmlEndpoint();Author(api);long handle=AuthorHandle(session);int costs=f.CostCalls;var before=f.Sources();
            try
            {
                f.CustomData=AuthoredHtml.Replace("Level", "Pending");
                switch(failure){case "target-off":f.TargetWorking=false;break;case "target-replaced":f.ReplaceTarget();break;case "caller-replaced":f.ReplaceCaller();break;case "PB-off":f.Enabled=false;break;case "program-changed":f.Program="New program";break;}
                Advance(session,60);Equal(costs,f.CostCalls,"Lifecycle"+failure+" retires before pending geometry work");
                if(failure=="target-replaced"||failure=="caller-replaced")SourcesEqual(before,f.Sources(),"Identity replacement cannot authorize writes or cleanup against replacement owner/target");
                else Equal(0,f.Items.Count,"Lifecycle"+failure+" cleans only its projected artwork");
                if(failure=="target-off"){f.TargetWorking=true;Advance(session,60);Equal(0,f.Items.Count,"Target recovery never auto reattaches old authoring display");Check(Author(api,"status").Contains("not mounted"),"Target loss clears authoring record");Author(api,"mount");Check(AuthorHandle(session)!=handle&&AuthorDoc(session).PublishedFrame!=null,"Explicit mount after correction grants fresh document");}
                else if(failure=="target-replaced"){Check(Author(api,"status").Contains("not mounted"),"Same-ID object replacement clears authoring record");}
                else{Check(!(bool)api("valid",new object[0]),"Caller lifetime change revokes captured authoring endpoint");Reject(()=>Author(api),"Stale caller cannot remount via retained delegate");}
            }
            finally{session.UnloadDataConditional();}
        }
    }
}
