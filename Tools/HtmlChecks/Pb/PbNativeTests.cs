using System.Collections;
using System.Reflection;
using Hdr.Html;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;

internal static partial class PbTests
{
    static HtmlPbSession NativeSession(PbFixture f)
    { f.CaptureHtmlProperty(); var session=new HtmlPbSession();session.AfterLoadData();session.BeforeStart();return session; }
    static long NativeBind(Func<string,object[],object> endpoint,PbFixture f,bool relay=false,string html=Markup,string css=Css,MatrixD? pose=null,double units=.005,int index=0,bool owns=true)
    { return (long)endpoint("bind-sprites",new object[]{relay?f.Target:(IMyTerminalBlock)f.NativeSource,f.NativeSource,html,css,pose??MatrixD.Identity,units,index,owns}); }
    static HtmlPbDocument ActualDoc(HtmlPbSession session,long handle)
    {
        var owners=(IDictionary)ClientReplicationTests.Field(session,"owners");
        return (HtmlPbDocument)((IDictionary)ClientReplicationTests.Field(owners[10L],"Documents"))[handle];
    }
    static MyTuple<string,string,string,MyTuple<double,long,long>>[] NativeEvents(Func<string,object[],object> endpoint,long handle)
    { return (MyTuple<string,string,string,MyTuple<double,long,long>>[])endpoint("poll-events",new object[]{handle}); }
    static void NativePointer(Func<string,object[],object> endpoint,long handle,HtmlHitRegion hit,bool held,double proportion=.5)
    { endpoint("pointer",new object[]{handle,hit.Bounds.X+hit.Bounds.Width*proportion,hit.Bounds.Y+hit.Bounds.Height*.5,held}); }
    static void NativeDirectPublication()
    {
        using var f=new PbFixture();var session=NativeSession(f);var endpoint=f.HtmlEndpoint();
        try
        {
            long handle=NativeBind(endpoint,f);var doc=ActualDoc(session,handle);var backend=(MyTuple<string,string,string,bool>)endpoint("backend",new object[]{handle});
            Equal("sprites-lcd",backend.Item2,"Physical source anchor uses native LCD backend");Equal("Debug:Lcd",backend.Item3,"Native uses measured Debug profile");Check(!backend.Item4,"Physical native LCD needs no plugin");
            Equal(512d,doc.PublishedFrame.Width,"Native logical viewport uses SurfaceSize width");Equal(256d,doc.PublishedFrame.Height,"Native logical viewport uses SurfaceSize height");
            Equal(0,f.Screens.Count,"Physical route declares no Core child screen");Equal(0,f.SvgAttempts,"Physical route emits no outline SVG");Equal(0,f.CostCalls,"Native text uses no Inter geometry cost");Check(f.NativeMeasures>0&&f.NativeCommits==1,"Actual native API measured Debug and committed one MySprite frame");
            var op=doc.PublishedFrame.Operations.First(o=>o.Kind=="text"&&o.Text=="Start");var sprite=f.NativeSprites.Single(s=>s.Type==SpriteType.TEXT&&s.Data=="Start");
            Equal("Debug",sprite.FontId,"Native glyph font identity");Near(op.Bounds.X,sprite.Position.Value.X,"Text X is native logical pixel");Near(op.Bounds.Y+128,sprite.Position.Value.Y,"Wide LCD letterbox texture origin is (0,128)");Check(Math.Abs(op.FontSize/28-sprite.RotationOrScale)<1e-6,"Native float glyph scale matches measured Debug line pixels");
            Equal(ContentType.SCRIPT,f.SourceContent,"Surface mode preserved");Equal("",f.SourceScript,"Surface script NONE preserved");Equal(0,f.SurfaceSettingWrites,"Native painter never writes content/script/background settings");
            int commits=f.NativeCommits;for(int i=0;i<10;i++)session.UpdateAfterSimulation();Equal(commits,f.NativeCommits,"Native idle ticks do not repaint");
            long builds=doc.Controller.LayoutBuildCount;endpoint("data",new object[]{handle,"x","one"});endpoint("data",new object[]{handle,"x","two"});endpoint("text",new object[]{handle,"caption","Final"});session.UpdateAfterSimulation();Equal(builds+1,doc.Controller.LayoutBuildCount,"Native data/text changes coalesce once");Check(f.NativeSprites.Any(s=>s.Type==SpriteType.TEXT&&s.Data=="Final"),"Native committed final text");
            commits=f.NativeCommits;endpoint("destroy",new object[]{handle});Equal(commits+1,f.NativeCommits,"Owned destroy commits one empty cleanup frame before releasing claim");Equal(0,f.NativeSprites.Count,"Owned cleanup clears only source publication");Check(!HtmlPbNativeSurfaceClaims.IsClaimed(f.NativeSource),"Destroy releases actual source claim");
            session.UnloadDataConditional();Equal(commits+1,f.NativeCommits,"Unload does not repeat cleanup of destroyed surface");
        }
        finally{session.UnloadDataConditional();}
    }
    static void NativeRelay(string kind)
    {
        using var f=new PbFixture();if(kind=="console")f.TargetSubtype="LargeConsole";var session=NativeSession(f);var endpoint=f.HtmlEndpoint();
        try
        {
            f.Draw("line","foreign",Vector3D.Zero,Vector3D.UnitX,"white");f.Draw("screen","foreign",MatrixD.Identity,1d,1d,1d,1d);
            var pose=MatrixD.CreateRotationZ(.2)*MatrixD.CreateTranslation(.3,.4,.5);long handle=NativeBind(endpoint,f,true,pose:pose);var doc=ActualDoc(session,handle);
            var backend=(MyTuple<string,string,string,bool>)endpoint("backend",new object[]{handle});Equal(kind,backend.Item1,"Relay target capability");Equal("sprites-relay",backend.Item2,"World relay backend");Check(backend.Item4,"World relay exposes per-viewer existing-plugin requirement");
            string id="h"+handle.ToString("x");Check(id.Length<=12&&id.All(c=>c>='a'&&c<='z'||c>='0'&&c<='9'),"Owned native child ID obeys actual Core domain");
            var data=(HoloProjectedScreenData)ClientReplicationTests.Field(f.Screens["10:"+id],"Data");Equal("lcd-texture",data.SourceProvider,"Actual Core screen-source uses existing native texture provider");Equal("30:0",data.SourceId,"Actual Core provider source identifies physical LCD surface0");
            Near(2.56,data.Width,"World width uses complete TextureSize metres");Near(2.56,data.Height,"World height preserves texture aspect");Near(2.56,data.CanvasWidth,"Core canvas width is metres");Near(2.56,data.CanvasHeight,"Core canvas height is metres");Equal(512,data.UiRasterWidth,"Requested texture resolution width stays actual pixels");Equal(512,data.UiRasterHeight,"Requested texture resolution height stays actual pixels");Equal("stretch",data.Aspect,"Square native texture maps to square world plane without squeezing glyphs");Near(60,data.RefreshHz,"Relay refresh request");Check(data.Sprites==null&&data.RasterWidth==64&&data.RasterHeight==36,"Relay declares native source without ProjectedSprites compatibility pixels");
            var center=Vector3D.Transform(new Vector3D(512*.5*.005,-256*.5*.005,0),pose);Near(center.X,data.Pose[12],"World center follows source SURFACE half X");Near(center.Y,data.Pose[13],"World center follows source SURFACE half Y");Near(center.Z,data.Pose[14],"World center follows model pose");
            var top=doc.PublishedFrame.Operations.First(o=>o.Kind=="text"&&o.Text=="Start");var sprite=f.NativeSprites.Single(s=>s.Type==SpriteType.TEXT&&s.Data=="Start");Near(top.Bounds.Y+128,sprite.Position.Value.Y,"Source native texture carries letterbox origin");Near(.005,data.Width/512,"World glyph pixel scale remains metres per pixel");
            Check(f.Commands.Contains("draw:screen-source")&&f.Commands.Contains("draw:screen-resolution"),"Relay exercised actual public Core command dispatch");
            var missing=(MyTuple<bool,bool,string>)f.CoreDraw("plugin-status",new object[]{"lcd-texture"});Check(missing.Item1&&!missing.Item2&&missing.Item3.Contains("Requires plugin"),"Missing provider is conservatively reported while server declarations remain valid");
            Equal(0,f.SurfaceSettingWrites,"Relay preserves RGB source background settings");Check(f.Items.Contains("10:foreign"),"Relay leaves unrelated artwork intact");
            endpoint("destroy",new object[]{handle});Check(f.Screens.Count==1&&f.Screens.Contains("10:foreign")&&f.Items.Contains("10:foreign"),"Retirement removes precisely its selected child, preserving foreign root content/screen");
        }
        finally{session.UnloadDataConditional();}
    }
    static void NativeAdmissionRejections()
    {
        using var f=new PbFixture();var session=NativeSession(f);var endpoint=f.HtmlEndpoint();
        try
        {
            Reject(()=>NativeBind(endpoint,f,owns:false),"Explicit native surface ownership required");Reject(()=>NativeBind(endpoint,f,index:1),"Only real physical LCD surface0");
            f.SourceScript="HDR API";Reject(()=>NativeBind(endpoint,f),"Selected script is foreign to native NONE painter");f.SourceScript="";f.SourceContent=ContentType.TEXT_AND_IMAGE;Reject(()=>NativeBind(endpoint,f),"Native source mode must be manual SCRIPT");f.SourceContent=ContentType.SCRIPT;
            f.SourceAccess=false;Reject(()=>NativeBind(endpoint,f),"Source access checked before reads/writes");f.SourceAccess=true;f.SourceSame=false;Reject(()=>NativeBind(endpoint,f),"Source construct checked before reads/writes");f.SourceSame=true;
            Reject(()=>endpoint("bind-sprites",new object[]{null,f.NativeSource,Markup,Css,MatrixD.Identity,.005,0,true}),"Missing actual anchor");
            Equal(0,f.NativeDrawAttempts,"All admission failures occur before native write");Equal(0,f.NativeMeasures,"All authority failures occur before native metric read");Equal(0,f.SurfaceSettingWrites,"Rejected binds never change surface settings");Check(!HtmlPbNativeSurfaceClaims.IsClaimed(f.NativeSource),"Rejected admission leaves no orphan source lease");
            long handle=NativeBind(endpoint,f);int commits=f.NativeCommits;Reject(()=>NativeBind(endpoint,f),"Duplicate actual source claim is atomic");Equal(commits,f.NativeCommits,"Duplicate bind does not blank first writer");endpoint("destroy",new object[]{handle});
        }
        finally{session.UnloadDataConditional();}
    }
    static void NativeRelayPreflight()
    {
        using var f=new PbFixture();var session=NativeSession(f);var endpoint=f.HtmlEndpoint();
        try
        {
            foreach(var bad in new[]{new Vector2(512.5f,512),new Vector2(31,512),new Vector2(4097,512)}){f.SourceTexture=bad;Reject(()=>NativeBind(endpoint,f,true),"Integer bounded native texture resolution");}
            f.SourceTexture=new Vector2(512,512);Reject(()=>NativeBind(endpoint,f,true,units:.1),"Physical/canvas metres limited before source writes");Reject(()=>NativeBind(endpoint,f,true,pose:MatrixD.CreateTranslation(26,0,0)),"Anchor range preflight");Reject(()=>NativeBind(endpoint,f,true,pose:MatrixD.CreateScale(2)),"Relay pose must preserve glyph pixel aspect");
            var port=new HtmlPbBridge(f.Caller,f.Target,true);Reject(()=>new HtmlPbNativePainter(0x100000000000L,port,f.Caller,f.Target,f.NativeSource,MatrixD.Identity,.005,true),"Child identifier cannot exceed actual Core twelve-character bound");
            Equal(0,f.NativeDrawAttempts,"Invalid relay geometry/id rejects before source DrawFrame");Equal(0,f.Screens.Count,"Invalid relay preflight declares no child");Check(!HtmlPbNativeSurfaceClaims.IsClaimed(f.NativeSource),"Invalid relay preflight claims no source");
        }
        finally{session.UnloadDataConditional();}
    }
    static void NativeCooperativeEvents()
    {
        using var f=new PbFixture();var session=NativeSession(f);var endpoint=f.HtmlEndpoint();
        try
        {
            long handle=NativeBind(endpoint,f);var doc=ActualDoc(session,handle);var button=doc.PublishedFrame.Hits.Single(h=>h.Kind=="button");var range=doc.PublishedFrame.Hits.Single(h=>h.Kind=="range");long published=Status(endpoint,handle).Item3;
            NativePointer(endpoint,handle,button,true);NativePointer(endpoint,handle,button,false);var events=NativeEvents(endpoint,handle);Equal(1,events.Length,"Cooperative supplied click uses visible authored hits");Equal("click",events[0].Item1,"Native click kind");Equal("go",events[0].Item2,"Native node identity");Equal("launch",events[0].Item3,"Native action metadata");Equal(published,events[0].Item4.Item2,"Native events carry published DOCUMENT revision");Equal(0L,events[0].Item4.Item3,"Cooperative native input invents no player identity");
            NativePointer(endpoint,handle,button,true);endpoint("pointer-cancel",new object[]{handle});NativePointer(endpoint,handle,button,false);Equal(0,NativeEvents(endpoint,handle).Length,"Pointer-cancel suppresses held-release click");
            NativePointer(endpoint,handle,range,true,.6);events=NativeEvents(endpoint,handle);Check(events.Any(e=>e.Item1=="change"&&e.Item2=="gain"&&e.Item4.Item1==60&&e.Item4.Item2==published&&e.Item4.Item3==0),"Native range event is canonical and uses native revision semantics");endpoint("data",new object[]{handle,"gain","60"});session.UpdateAfterSimulation();Check(doc.PublishedFrame.Revision>published,"Consumer-selected native range data repaints through actual document");
            f.SourceSize=new Vector2(512,240);NativePointer(endpoint,handle,button,true);NativePointer(endpoint,handle,button,false);Equal(0,NativeEvents(endpoint,handle).Length,"Changed dimensions suppress old published hit coordinates before repaint");session.UpdateAfterSimulation();Equal(240d,doc.PublishedFrame.Height,"Native viewport auto-resizes to actual source");
            Equal(0,f.TryRuns,"Native actions never call PB TryRun");Equal(0,f.UiCalls-1,"Native documents create no Core persistent mouse/UI declaration (one discovery UI version query only)");
        }
        finally{session.UnloadDataConditional();}
    }
    static void NativeFailureRetiresInput()
    {
        using var f=new PbFixture();var session=NativeSession(f);var endpoint=f.HtmlEndpoint();
        try
        {
            long handle=NativeBind(endpoint,f);var doc=ActualDoc(session,handle);var hit=doc.PublishedFrame.Hits.Single(h=>h.Kind=="button");NativePointer(endpoint,handle,hit,true);endpoint("text",new object[]{handle,"caption","Uncertain"});f.FailNativeDraw=true;session.UpdateAfterSimulation();
            Equal(0L,Status(endpoint,handle).Item3,"Uncertain native frame retires published revision");Check(Status(endpoint,handle).Item4.Item2,"Failed native paint remains pending");NativePointer(endpoint,handle,hit,false);Equal(0,NativeEvents(endpoint,handle).Length,"Uncertain native frame cannot emit old held release");
            int attempts=f.NativeDrawAttempts;for(int i=0;i<59;i++)session.UpdateAfterSimulation();Equal(attempts,f.NativeDrawAttempts,"Native failure cooldown prevents per-tick retries");f.FailNativeDraw=false;session.UpdateAfterSimulation();Check(Status(endpoint,handle).Item3>0,"Successful bounded retry republishes native input");
        }
        finally{session.UnloadDataConditional();}
    }
    static void NativeLifetimes()
    {
        foreach(string condition in new[]{"PB off","program changed","anchor off","source off","caller replaced","anchor replaced","source replaced","source access","source construct"})
        {
            using var f=new PbFixture();var session=NativeSession(f);var endpoint=f.HtmlEndpoint();long handle=NativeBind(endpoint,f,true);endpoint("text",new object[]{handle,"caption","Pending"});int measures=f.NativeMeasures,frames=f.NativeFrameSizes.Count;
            switch(condition){case "PB off":f.Working=false;break;case "program changed":f.Program="new-program";break;case "anchor off":f.TargetWorking=false;break;case "source off":f.SourceWorking=false;break;case "caller replaced":f.ReplaceCaller();break;case "anchor replaced":f.ReplaceTarget();break;case "source replaced":f.ReplaceSource();break;case "source access":f.SourceAccess=false;break;case "source construct":f.SourceSame=false;break;}
            session.UpdateAfterSimulation();Equal(measures,f.NativeMeasures,condition+" fences native metric reads");Check(f.NativeFrameSizes.Skip(frames).All(count=>count==0),condition+" permits only still-owned empty cleanup, never pending native paint");Reject(()=>endpoint("status",new object[]{handle}),condition+" invalidates native handle");Check(!HtmlPbNativeSurfaceClaims.IsClaimed(f.NativeSource),condition+" releases native source lease");session.UnloadDataConditional();
        }
    }
    static void NativeForeignModeCleanup()
    {
        foreach(bool replacement in new[]{false,true})
        {
            using var f=new PbFixture();var session=NativeSession(f);var endpoint=f.HtmlEndpoint();long handle=NativeBind(endpoint,f);int attempts=f.NativeDrawAttempts,commits=f.NativeCommits;
            if(replacement)f.ReplaceSource();else f.SourceScript="ForeignScript";
            session.UpdateAfterSimulation();Equal(attempts,f.NativeDrawAttempts,"Retirement does not blank foreign mode/replacement source");Equal(commits,f.NativeCommits,"Foreign source publication remains untouched");Reject(()=>endpoint("status",new object[]{handle}),"Foreign source handle retires");Check(!HtmlPbNativeSurfaceClaims.IsClaimed(f.NativeSource),"Foreign source retirement releases old lease only");session.UnloadDataConditional();
        }
    }
    static void NativeSharedLocalClaims()
    {
        using var f=new PbFixture();f.LocalHostUtilities();var local=new HtmlFrontendSession();local.AfterLoadData();local.BeforeStart();var localEndpoint=(Func<string,object[],object>)ClientReplicationTests.Call(local,"Service","open",new object[]{"native-claims"});var session=NativeSession(f);var endpoint=f.HtmlEndpoint();
        try
        {
            long localHandle=(long)localEndpoint("create-lcd",new object[]{Markup,Css,(Sandbox.ModAPI.Ingame.IMyTextSurface)f.NativeSource,true});int commits=f.NativeCommits;Reject(()=>NativeBind(endpoint,f),"PB document cannot steal local-client host surface");Equal(commits,f.NativeCommits,"Duplicate shared claim performs no native write");localEndpoint("destroy",new object[]{localHandle});
            long handle=NativeBind(endpoint,f);commits=f.NativeCommits;Reject(()=>localEndpoint("create-lcd",new object[]{Markup,Css,(Sandbox.ModAPI.Ingame.IMyTextSurface)f.NativeSource,true}),"Local host cannot steal PB surface");Equal(commits,f.NativeCommits,"Inverse duplicate claim preserves PB frame");endpoint("destroy",new object[]{handle});
            localHandle=(long)localEndpoint("create-lcd",new object[]{Markup,Css,(Sandbox.ModAPI.Ingame.IMyTextSurface)f.NativeSource,true});Check(localHandle>0,"Cleanup-before-release enables subsequent owner");localEndpoint("destroy",new object[]{localHandle});Check(!HtmlPbNativeSurfaceClaims.IsClaimed(f.NativeSource),"Both hosts release shared source lease");
        }
        finally{session.UnloadDataConditional();local.UnloadDataConditional();}
    }
    static void NativeCanonicalDemo()
    {
        string path=typeof(PbTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a=>a.Key=="HtmlPbSpritesDemoPath").Value;var syntax=Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("class Program {"+File.ReadAllText(path)+"}").GetRoot();
        string Literal(string name){var v=syntax.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>().Single(n=>n.Identifier.ValueText==name);return ((Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax)v.Initializer.Value).Token.ValueText;}
        foreach(string kind in new[]{"lcd","projector","console"})
        {
            using var f=new PbFixture();if(kind=="console")f.TargetSubtype="LargeConsole";var session=NativeSession(f);var endpoint=f.HtmlEndpoint();
            try{long handle=NativeBind(endpoint,f,kind!="lcd",Literal("Markup"),Literal("Stylesheet"));var doc=ActualDoc(session,handle);Check(doc.PublishedFrame.Hits.Count==2&&doc.PublishedFrame.FontProfile=="Debug:Lcd","Canonical native demo admits measured Debug and both controls on "+kind);var button=doc.PublishedFrame.Hits.Single(h=>h.Kind=="button");NativePointer(endpoint,handle,button,true);NativePointer(endpoint,handle,button,false);var events=NativeEvents(endpoint,handle);Check(events.Length==1&&events[0].Item2=="reset"&&events[0].Item3=="reset"&&events[0].Item4.Item2==doc.PublishedFrame.Revision&&events[0].Item4.Item3==0,"Canonical native reset is a polled document/player0 event on "+kind);}
            finally{session.UnloadDataConditional();}
        }
    }
    static void RunNative()
    {
        Run("native direct LCD real sprites, Debug pixels and owned cleanup",NativeDirectPublication);
        Run("native Projector actual Core texture relay and letterbox geometry",()=>NativeRelay("projector"));
        Run("native Console actual Core texture relay and letterbox geometry",()=>NativeRelay("console"));
        Run("native source authority and duplicate admission are atomic",NativeAdmissionRejections);
        Run("native relay id, dimension and pose preflight precedes source writes",NativeRelayPreflight);
        Run("native cooperative visible-frame pointer and revision semantics",NativeCooperativeEvents);
        Run("native frame uncertainty retires input with bounded retry",NativeFailureRetiresInput);
        Run("native caller, anchor and source lifetime fence reads and paints",NativeLifetimes);
        Run("native retirement preserves foreign mode and replacement owner",NativeForeignModeCleanup);
        Run("native shared claims isolate PB and existing local-client host",NativeSharedLocalClaims);
        Run("canonical sprites PB demo admits direct and actual Core relay targets",NativeCanonicalDemo);
    }
}
