using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Hdr.Mods;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using Metrics = VRage.MyTuple<string, VRage.MyTuple<double,double,double>, VRage.MyTuple<double,double,double,double>, int, bool>;

internal static class CoreSeamTests
{
    static int checks;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool yes,string why) { if(!yes)throw new Exception("Core seam: "+why);checks++; }
    static void Reject(Action action,string why) { try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Expected rejection: "+why); }
    static bool Near(double a,double b) { return Math.Abs(a-b)<1e-8*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b))); }
    public static int Run()
    {
        ColdMetricQuery(); FontGeometryEquivalence(); EndpointPurity(); RetainedOrder(); GeometryQueries(); Sdk(); return checks;
    }
    static void ColdMetricQuery()
    {
        var cache=(IDictionary)typeof(VectorFont).GetField("Cache",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        cache.Clear();
        using(GeometryWork.Begin(1))
        {
            var metrics=VectorFont.MeasureText("Agj\t\u00a0\u03a9",2,1.8);
            Check(metrics.Item1==VectorFont.MetricProfile&&metrics.Item4==1,"explicit packaged cap-height profile");
            GeometryWork.Charge(1); Check(cache.Count==0,"cold metrics builds no glyph geometry and consumes no GeometryWork");
        }
        var blank=VectorFont.MeasureText(" \t\u00a0",3);
        Check(!blank.Item5&&blank.Item3.Equals(default(MyTuple<double,double,double,double>)),"space-only bounds distinct from layout advance");
        Check(Near(blank.Item2.Item1,.387*6*3),"tab means four spaces including trailing whitespace");
        var empty=VectorFont.MeasureText(""); Check(!empty.Item5&&empty.Item2.Item1==0&&empty.Item2.Item2==1&&empty.Item2.Item3==1.3,"empty advance/cap/line metrics");
        Reject(()=>VectorFont.MeasureText(null),"null text"); Reject(()=>VectorFont.MeasureText(new string('a',65)),"65 UTF16 units");
        foreach(double invalid in new[]{0d,-1,double.NaN,double.PositiveInfinity,1000000.1})Reject(()=>VectorFont.MeasureText("a",invalid),"invalid height");
        foreach(double invalid in new[]{.99,4.01,double.NaN,double.PositiveInfinity})Reject(()=>VectorFont.MeasureText("a",1,invalid),"invalid line height");
        Check(cache.Count==0,"invalid metrics cannot build glyph geometry");
        VectorFont.MeasureText(new string(' ',64));Check(cache.Count==0,"64 whitespace units accepted without geometry");
        for(int i=0;i<1000;i++)VectorFont.MeasureText("Agj");
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<125000;i++)VectorFont.MeasureText("Agj");
        long metricAllocation=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Check(metricAllocation==0,"warmed source metrics allocate zero per query (actual "+metricAllocation+" bytes; tuple value type "+typeof(Metrics).IsValueType+")");
    }
    static void FontGeometryEquivalence()
    {
        Check(VectorFont.MeasureText("H",1e-9).Item5&&VectorFont.Text("H",1e-9,"start").Triangles.Length==0,"source ink survives tiny-scale renderer degeneracy filtering");
        var outlines=(IDictionary)typeof(VectorFont).GetField("Outlines",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        foreach(int code in outlines.Keys)Compare(char.ConvertFromUtf32(code),2.5,1.3);
        foreach(string text in new[]{"A\r\nB\rC\nD","\nA\n\nB\n","gÁj ?\t\u00a0","\ud83d\ude00","\ud83d","\ude00","A\ud83d\ude00B",new string(' ',63)+"H"})
            foreach(double line in new[]{1d,1.3,4d})Compare(text,.25,line);
        // The core's chunk ceiling is UTF16; paired fallbacks occupy one glyph, split surrogates two.
        var one=VectorFont.MeasureText("\ud83d\ude00");var question=VectorFont.MeasureText("?");
        Check(one.Item2.Equals(question.Item2)&&one.Item3.Equals(question.Item3),"unsupported supplementary scalar is one fallback glyph");
        var split=VectorFont.MeasureText("\ud83d\n\ude00");Check(Near(split.Item2.Item1,question.Item2.Item1),"lone-surrogate lines each fallback");
        var first=VectorFont.MeasureText("A");var second=VectorFont.MeasureText("j");var whole=VectorFont.MeasureText("Aj");
        Check(Near(first.Item2.Item1+second.Item2.Item1,whole.Item2.Item1),"bounded chunk advances compose without kerning");
        Check(Near(Math.Min(first.Item3.Item1,first.Item2.Item1+second.Item3.Item1),whole.Item3.Item1)&&Near(Math.Max(first.Item3.Item3,first.Item2.Item1+second.Item3.Item3),whole.Item3.Item3),"bounded chunk ink unions compose");
    }
    static void Compare(string text,double height,double line)
    {
        var m=VectorFont.MeasureText(text,height,line);double width=0;
        foreach(string row in text.Replace("\r\n","\n").Replace('\r','\n').Split('\n'))width=Math.Max(width,VectorFont.Width(row));
        Check(Near(m.Item2.Item1,width*height)&&m.Item2.Item2==height&&Near(m.Item2.Item3,height*line),"advance equals exact renderer "+text);
        var glyphs=VectorFont.Layout(text,"start",line);bool has=false;Vector3D min=default,max=default;
        foreach(var glyph in glyphs)foreach(int vertex in glyph.Mesh.Triangles)
        {
            Vector3D point=(glyph.Mesh.Points[vertex]+glyph.Offset)*height;
            if(!has){min=max=point;has=true;}else{min=Vector3D.Min(min,point);max=Vector3D.Max(max,point);}
        }
        Check(m.Item5==has&&(!has||Near(m.Item3.Item1,min.X)&&Near(m.Item3.Item2,min.Y)&&Near(m.Item3.Item3,max.X)&&Near(m.Item3.Item4,max.Y)),"ink bounds equal referenced glyph triangles "+text);
        // Exercise actual Text output where current geometry admission permits the authored text.
        if(text.Length<12)
        {
            var geometry=VectorFont.Text(text,height,"start",line);
            if(has)
            {var a=geometry.Points[0];var b=a;foreach(int index in geometry.Triangles){a=Vector3D.Min(a,geometry.Points[index]);b=Vector3D.Max(b,geometry.Points[index]);}
                Check(Near(m.Item3.Item1,a.X)&&Near(m.Item3.Item2,a.Y)&&Near(m.Item3.Item3,b.X)&&Near(m.Item3.Item4,b.Y),"metrics equal actual compiled text geometry "+text);}
            else Check(geometry.Triangles.Length==0,"no-ink geometry has no triangles");
        }
    }
    static void EndpointPurity()
    {
        using var gateway=new ClientReplicationTests.GatewayScope{Server=true,Dedicated=true};
        var caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(method,args)=>method.Name switch
        {"get_EntityId"=>10L,"get_Closed"=>false,_=>throw new Exception("Metric query observed caller/target state: "+method.Name)});
        gateway.Install("Entities",(method,args)=>method.Name=="GetEntityById"&&(long)args[0]==10?caller:throw new Exception("Metric query tried to access target"));
        var session=new HoloMapSession();var draw=(Func<string,object[],object>)ClientReplicationTests.Call(session,"DrawEndpoint",caller);
        object context=((IDictionary)ClientReplicationTests.Field(session,"_drawContexts"))[10L];
        var scenes=(IDictionary)ClientReplicationTests.Field(session,"_scenes");
        var cache=(IDictionary)typeof(VectorFont).GetField("Cache",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);int count=cache.Count;
        ClientReplicationTests.SetField(session,"_dirty",false);
        var before=(Metrics)draw("measure-text",new object[]{"Omega Ω",2d,1.4d});
        Check(scenes.Count==0&&ClientReplicationTests.Field(context,"Target")==null&&!((bool)ClientReplicationTests.Field(session,"_dirty")),"dedicated metric query before selection claims no scene/target and dirties nothing");
        ClientReplicationTests.SetField(context,"ScreenId","retired-selected-screen");
        var after=(Metrics)draw("measure-text",new object[]{"Omega Ω",2d,1.4d});
        Check(before.Equals(after)&&scenes.Count==0&&ClientReplicationTests.Field(context,"ScreenId").Equals("retired-selected-screen"),"child screen query bypasses lookup unchanged and keeps selection");
        Check(cache.Count==count,"Draw metric query does not mutate renderer glyph cache");
        foreach(object[] invalid in new[]{new object[]{1},new object[]{"a","1"},new object[]{"a",1d,1.3d,0}})Reject(()=>draw("measure-text",invalid),"strict query argument types/arity");
        ClientReplicationTests.SetField(session,"_modClientActive",true);
        var service=(Func<string,object[],object>)Delegate.CreateDelegate(typeof(Func<string,object[],object>),session,typeof(HoloMapSession).GetMethod("ModClientService",Private));
        var owners=(IDictionary)ClientReplicationTests.Field(session,"_modClientOwners");
        Check((bool)service("rendering-enabled",new object[0])&&owners.Count==0,"service rendering query does not claim a consumer/context");
        ClientReplicationTests.SetField(session,"ClientRenderingEnabled",false);Check(!(bool)service("rendering-enabled",new object[0])&&owners.Count==0,"disabled service query retains no ownership");
        ClientReplicationTests.SetField(session,"ClientRenderingEnabled",true);
        Check(((Metrics)service("measure-text",new object[]{"A"})).Equals(VectorFont.MeasureText("A"))&&owners.Count==0,"service query does not claim a consumer/context");
        var endpoint=(Func<string,object[],object>)service("open",new object[]{"metric-owner"});var owner=(ModClientOwner)owners["metric-owner"];
        ClientReplicationTests.SetField(session,"_modClientDrawing",true);
        Check((bool)endpoint("rendering-enabled",new object[0])&&owner.Contexts.Count==0&&owner.DeclarationRevision==0,"owner rendering query harmless during Draw");
        Check(((Metrics)endpoint("measure-text",new object[]{"A"})).Equals(VectorFont.MeasureText("A"))&&owner.Contexts.Count==0&&owner.DeclarationRevision==0,"owner query valid without a context and harmless during Draw");
        ClientReplicationTests.SetField(session,"_modClientDrawing",false);endpoint("release",new object[0]);
        Reject(()=>endpoint("measure-text",new object[]{"A"}),"revoked owner cannot query");
        Check(!((bool)ClientReplicationTests.Field(session,"_dirty"))&&gateway.Sent.Count==0&&gateway.Requests==0,"queries never publish/network/GPU");
    }
    static void RetainedOrder()
    {
        using var gateway=new ClientReplicationTests.GatewayScope();
        var viewport=new Vector2(1920,1080);var projection=MatrixD.CreatePerspectiveFieldOfView(1.1,viewport.X/viewport.Y,.1,1000);
        var sessionType=typeof(MyAPIGateway).GetProperty("Session").PropertyType;
        var cameraType=sessionType.GetProperty("Camera").PropertyType;
        var camera=DrawTestProxy.Make(cameraType,(method,args)=>method.Name switch
        {"get_ViewportSize"=>viewport,"get_NearPlaneDistance"=>.1f,"get_ProjectionMatrix"=>projection,"get_ViewMatrix"=>MatrixD.Identity,"get_Position"=>Vector3D.Zero,_=>throw new Exception(method.Name)});
        gateway.Install("Session",(method,args)=>method.Name=="get_Camera"?camera:method.Name=="get_ElapsedPlayTime"?TimeSpan.Zero:throw new Exception(method.Name));
        var session=new HoloMapSession();ClientReplicationTests.SetField(session,"_modClientActive",true);
        var service=(Func<string,object[],object>)Delegate.CreateDelegate(typeof(Func<string,object[],object>),session,typeof(HoloMapSession).GetMethod("ModClientService",Private));
        var endpoint=(Func<string,object[],object>)service("open",new object[]{"paint-owner"});
        object Call(string op,params object[] values)=>endpoint(op,values);
        long handle=(long)Call("create-hud",0);var owner=((Dictionary<string,ModClientOwner>)ClientReplicationTests.Field(session,"_modClientOwners"))["paint-owner"];var context=owner.Contexts[handle];
        var points=new[]{new Vector3D(0,0,0),new Vector3D(10,0,0),new Vector3D(0,10,0)};
        void Mesh(string id,Vector4 color)=>Call("mesh",handle,id,points,new[]{0,1,2},color);
        Mesh("background",new Vector4(1,0,0,1));Call("text",handle,"caption","H",new Vector3D(1,5,0),2d,Vector4.One,"start");
        Mesh("control",new Vector4(0,1,0,1));Call("value",handle,"amount",0d,0d,10d,1d);Call("control",handle,"slider","control",new Vector4(0,0,10,10));Call("bind-value",handle,"slider","amount");Call("constraint",handle,"slider","line",Vector3D.Zero,new Vector3D(10,0,0));Call("draggable",handle,"slider",true);
        Call("pointer",handle,1d,1d,true);Check(context.Capture!=null,"live control capture fixture");
        var control=context.Controls["slider"];var capture=context.Capture;var controlItem=context.Items["control"];var controlMesh=controlItem.Geometry;var caption=context.Items["caption"];var captionMesh=caption.Geometry;long revision=context.DeclarationRevision;
        Call("item-order",handle,"control",2);Call("item-order",handle,"caption",1);Call("item-order",handle,"background",0);
        Check(context.DeclarationRevision==revision&&ReferenceEquals(control,context.Controls["slider"])&&ReferenceEquals(capture,context.Capture)&&ReferenceEquals(controlMesh,controlItem.Geometry)&&ReferenceEquals(captionMesh,caption.Geometry),"paint-order updates preserve declaration, capture, controls and geometry cache");
        Mesh("background",new Vector4(0,0,1,1));Check(context.Items["background"].Order>caption.Order,"default replacement still appends paint order");
        Call("item-order",handle,"background",0);Check(ReferenceEquals(capture,context.Capture)&&ReferenceEquals(control,context.Controls["slider"]),"updating an unrelated background preserves controls");
        var submitted=new List<ModClientTriangleSubmission>();ClientReplicationTests.SetField(session,"_modClientOfflineSubmit",new Action<ModClientTriangleSubmission>(s=>submitted.Add(s)));
        void Render(){object[] args={1000};typeof(HoloMapSession).GetMethod("DrawModClientApi",Private).Invoke(session,args);}
        Render();Check(submitted.Count>2&&submitted[0].Color.Z==1&&submitted[0].Color.X==0&&submitted.Last().Color.Y==1&&submitted.Last().Color.X==0,"actual retained renderer paints updated background before caption then controls");
        Check(context.DrawItems[0].Id=="background"&&context.DrawItems[1].Id=="caption"&&context.DrawItems[2].Id=="control","actual renderer stable explicit ordering");
        Call("item-order",handle,"control",1);submitted.Clear();Render();Check(context.DrawItems[1].Id=="caption"&&context.DrawItems[2].Id=="control","equal explicit orders deterministic by ordinal item ID");
        long priorOrder=controlItem.Order;Reject(()=>Call("item-order",handle,"control",1.5),"fractional order");Reject(()=>Call("item-order",handle,"control",double.PositiveInfinity),"nonfinite order");Reject(()=>Call("item-order",handle+1,"control",0),"foreign context");Reject(()=>Call("item-order",handle,"missing",0),"unknown artwork");Reject(()=>Call("item-order",handle,"control",0,1),"extra order argument");
        Check(controlItem.Order==priorOrder&&ReferenceEquals(context.Capture,capture)&&ReferenceEquals(controlItem.Geometry,controlMesh),"rejected order leaves all retained state unchanged");
        Call("item-order",handle,"control",int.MinValue);Call("item-order",handle,"control",int.MaxValue);Check(controlItem.Order==int.MaxValue,"full int range accepted");
        Call("pointer-cancel",handle);
    }
    static void GeometryQueries()
    {
        var session=new HoloMapSession();ClientReplicationTests.SetField(session,"_modClientActive",true);
        var service=(Func<string,object[],object>)Delegate.CreateDelegate(typeof(Func<string,object[],object>),session,typeof(HoloMapSession).GetMethod("ModClientService",Private));
        var owners=(Dictionary<string,ModClientOwner>)ClientReplicationTests.Field(session,"_modClientOwners");
        const string svg="<svg viewBox='0 0 100 50'><rect x='0' y='0' width='100' height='50' fill='#fff'/><text x='3' y='20' font-size='8'>Hello</text></svg>";
        var textCost=(MyTuple<int,int>)service("geometry-cost",new object[]{"text","Hello",3d,12});
        var compiledText=VectorFont.Text("Hello",3,"start");
        Check(textCost.Item1==compiledText.Points.Length&&textCost.Item2==compiledText.Triangles.Length/3,"text cost is actual compiled geometry");
        var svgCost=(MyTuple<int,int>)service("geometry-cost",new object[]{"svg",svg,1d,12});var compiledSvg=Svg.Parse(svg,12).Geometry;
        Check(svgCost.Item1==compiledSvg.Points.Length&&svgCost.Item2==compiledSvg.Triangles.Length/3+compiledSvg.Edges.Length/2,"SVG cost is actual bounded compiler result");
        Check(owners.Count==0&&((IDictionary)ClientReplicationTests.Field(session,"_scenes")).Count==0&&!((bool)ClientReplicationTests.Field(session,"_dirty")),"service costs retain no owner/context/scene state");
        var endpoint=(Func<string,object[],object>)service("open",new object[]{"geometry-owner"});
        object Call(string op,params object[] values)=>endpoint(op,values);
        Check(((MyTuple<int,int>)Call("geometry-usage")).Equals(new MyTuple<int,int>(0,0)),"empty owner geometry usage");
        long first=(long)Call("create-hud",0);long second=(long)Call("create-world",0,MatrixD.Identity);
        Check((bool)Call("context-valid",first)&&(bool)Call("context-valid",second)&&!(bool)Call("context-valid",-1L),"owner context validity lookup is exact");
        var points=new[]{Vector3D.Zero,Vector3D.UnitX,Vector3D.UnitY};Call("mesh",first,"mesh",points,new[]{0,1,2},Vector4.One);Call("text",second,"text","Hello",Vector3D.Zero,3d,Vector4.One,"start");
        Call("wires",first,"wires",points,new[]{new Vector2I(0,1),new Vector2I(1,2)},Vector4.One,1d);
        var owner=owners["geometry-owner"];long revision=owner.DeclarationRevision;var saved=owner.Contexts[second].Items["text"].Geometry;
        ClientReplicationTests.SetField(session,"ClientRenderingEnabled",false);
        Check(!(bool)Call("rendering-enabled")&&(bool)Call("context-valid",first)&&ReferenceEquals(saved,owner.Contexts[second].Items["text"].Geometry),"render-disabled remains distinct from context retirement");
        ClientReplicationTests.SetField(session,"ClientRenderingEnabled",true);Check((bool)Call("rendering-enabled"),"render query reports restored global switch");
        Reject(()=>Call("rendering-enabled",true),"render query takes no setter argument");
        var usage=(MyTuple<int,int>)Call("geometry-usage");
        Check(usage.Item1==6+textCost.Item1&&usage.Item2==3+textCost.Item2,"usage sums retained points and all triangles/edges across contexts");
        Call("context-visible",first,false);Check(((MyTuple<int,int>)Call("geometry-usage")).Equals(usage),"hidden contexts still occupy retained geometry budget");
        Check(((MyTuple<int,int>)Call("geometry-cost","svg",svg)).Equals(svgCost)&&owner.DeclarationRevision==revision&&ReferenceEquals(saved,owner.Contexts[second].Items["text"].Geometry),"owner cost queries preserve retained geometry and declarations");
        Reject(()=>Call("geometry-cost","html",svg),"unsupported kind");Reject(()=>Call("geometry-cost","text",new string('A',65)),"bounded text source");Reject(()=>Call("geometry-cost","svg",svg,1d,33),"bounded SVG segments");Reject(()=>Call("geometry-cost","svg",new string('x',65537)),"bounded SVG source");Reject(()=>Call("geometry-cost","text","A",double.NaN),"finite height");Reject(()=>Call("geometry-cost","text","A",1d,12,1),"cost argument arity");Reject(()=>Call("geometry-usage",first),"usage has no context argument");Reject(()=>Call("context-valid",1),"context validity requires long handle");
        using(GeometryWork.Begin(1))Reject(()=>Call("geometry-cost","text","H"),"cost query deliberately charges compiler allowance");
        Check(owner.DeclarationRevision==revision&&ReferenceEquals(saved,owner.Contexts[second].Items["text"].Geometry)&&((MyTuple<int,int>)Call("geometry-usage")).Equals(usage),"failed cost query preserves entire retained owner geometry");
        var other=(Func<string,object[],object>)service("open",new object[]{"other-owner"});
        for(int i=0;i<3;i++)other("create-hud",new object[]{0});
        Check(!(bool)Call("context-valid",3L)&&((MyTuple<int,int>)other("geometry-usage",new object[0])).Item1==0,"queries cannot inspect another owner context or aggregate");
        Call("destroy",first);Check(!(bool)Call("context-valid",first)&&((MyTuple<int,int>)Call("geometry-usage")).Item1==textCost.Item1,"context retirement immediately reflected in pure queries");
        ClientReplicationTests.SetField(session,"_modClientDrawing",true);Reject(()=>Call("geometry-cost","text","A"),"cost compilation only outside Draw");Reject(()=>service("geometry-cost",new object[]{"text","A"}),"service cost compilation only outside Draw");ClientReplicationTests.SetField(session,"_modClientDrawing",false);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<100;i++)Call("geometry-cost","svg",svg,1d,12);watch.Stop();double costMs=watch.Elapsed.TotalMilliseconds;
        watch.Restart();for(int i=0;i<100;i++)Svg.Parse(svg,12);watch.Stop();
        Console.WriteLine("BENCH 100 cost-query SVG compiles "+costMs.ToString("F3")+" ms; 100 extra upsert-equivalent compiles "+watch.Elapsed.TotalMilliseconds.ToString("F3")+" ms. Double compilation is explicit.");
        Call("release");Reject(()=>Call("geometry-usage"),"revoked usage query");Reject(()=>Call("context-valid",second),"revoked context query");
    }
    static void Sdk()
    {
        var api=(HdrModApi)RuntimeHelpers.GetUninitializedObject(typeof(HdrModApi));
        string op=null;object[] values=null;
        Func<string,object[],object> endpoint=(command,args)=>{if(command=="valid")return true;op=command;values=args;if(command=="measure-text")return VectorFont.MeasureText((string)args[0],(double)args[1],(double)args[2]);if(command=="geometry-cost"||command=="geometry-usage")return new MyTuple<int,int>(3,1);return true;};
        typeof(HdrModApi).GetField("endpoint",Private).SetValue(api,endpoint);
        var metric=api.MeasureText("A",2,1.4);Check(op=="measure-text"&&values.Length==3&&metric.Item2.Item2==2,"typed SDK metrics delegates exact profile");
        api.ItemOrder(9,"caption",3);Check(op=="item-order"&&values.Length==3&&(long)values[0]==9&&(string)values[1]=="caption"&&(int)values[2]==3,"typed SDK paint order contract");
        var cost=api.GeometryCost("svg","<svg/>");Check(op=="geometry-cost"&&values.Length==4&&(double)values[2]==1&&(int)values[3]==12&&cost.Item1==3,"typed SDK geometry cost contract");
        Check(api.ContextValid(9)&&op=="context-valid"&&values.Length==1&&(long)values[0]==9,"typed SDK context validity contract");
        Check(api.GeometryUsage().Item2==1&&op=="geometry-usage"&&values.Length==0,"typed SDK aggregate usage contract");
        Check(api.RenderingEnabled&&op=="rendering-enabled"&&values.Length==0,"typed SDK global rendering query contract");
        var pb=new HdrIngameApi();typeof(HdrIngameApi).GetField("_draw",Private).SetValue(pb,endpoint);
        Metrics measured;string reason;Check(pb.TryMeasureText("H",3,1.7,out measured,out reason)&&measured.Item2.Item2==3,"PB helper exact typed metric contract");
        Check(!pb.TryMeasureText(new string('x',65),1,1.3,out measured,out reason)&&reason.Contains("64"),"PB helper expected validation returns reason");
        var absent=new HdrIngameApi();Check(!absent.TryMeasureText("A",1,1.3,out measured,out reason)&&reason=="Requires mod: HDR API.","PB helper missing mod is clean result");
    }
}
