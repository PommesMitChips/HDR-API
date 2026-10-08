using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Hdr.Html;
using HoloMap;
using VRage;
using VRageMath;

internal static class RendererChecks
{
    static int checks;
    static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
    sealed class Endpoint : IHtmlDrawApi
    {
        HoloMapSession session;Func<string,object[],object> endpoint;public bool Ready {get;set;} public long Generation {get;private set;}
        public int Calls,Mutations,CostCalls;public string FailCommand;public ModClientOwner Owner;
        public Endpoint(){Reconnect();}
        public void Reconnect()
        {
            session=new HoloMapSession();typeof(HoloMapSession).GetField("_modClientActive",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(session,true);
            var service=typeof(HoloMapSession).GetMethod("ModClientService",BindingFlags.Instance|BindingFlags.NonPublic);
            endpoint=(Func<string,object[],object>)service.Invoke(session,new object[]{"open",new object[]{"html-renderer-test"}});
            var owners=(Dictionary<string,ModClientOwner>)typeof(HoloMapSession).GetField("_modClientOwners",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
            Owner=owners["html-renderer-test"];Generation++;Ready=true;
        }
        public object Call(string command,params object[] arguments)
        {
            Calls++;if(command=="geometry-cost")CostCalls++;
            if(command!="context-valid"&&command!="geometry-usage"&&command!="geometry-cost"&&command!="measure-text"&&command!="geometry-limit-settings")Mutations++;
            if(command==FailCommand){FailCommand=null;throw new ArgumentException("injected transient "+command);}
            return endpoint(command,arguments);
        }
    }
    static HtmlPaintFrame Frame(Endpoint api,int rows)
    {
        var f=new HtmlPaintFrame{Width=800,Height=450,FontProfile=HtmlHdrTextMetrics.MetricSchema,Revision=1};
        f.Operations.Add(new HtmlPaintOperation{Key="panel",Kind="rect",Order=0,Bounds=new HtmlRect(5,5,780,435),Radius=10,Color=new HtmlColor(.06,.1,.16,.95),HasClip=true,Clip=new HtmlRect(0,0,800,450)});
        var metrics=new HtmlHdrTextMetrics(api);
        for(int i=0;i<rows;i++)
        {
            string text=i==0?"HDR HTML":"Row "+i;var m=metrics.Measure(text,12,1.3);
            f.Operations.Add(new HtmlPaintOperation{Key="text-"+i,Kind="text",Text=text,Order=f.Operations.Count,Bounds=new HtmlRect(20,20+i*14,m.Advance,12),FontSize=12,LineHeight=1.3,Color=new HtmlColor(.1,.8,.9,1),HasClip=true,Clip=new HtmlRect(8,8,760,428)});
        }
        return f;
    }
    static string GeometryPreview(Endpoint api,long context,bool hud,double s)
    {
        var items=new List<ModClientItem>(api.Owner.Contexts[context].Items.Values);items.Sort((a,b)=>a.Order.CompareTo(b.Order));
        var result=new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 800 450\" role=\"img\"><title>Actual compiled HDR paint triangles, Inter outline font</title>");
        foreach(var item in items)for(int i=0;i<item.Geometry.Triangles.Length;i+=3)
        {
            var color=item.Colors==null?item.Color:item.Colors[i/3];result.Append("<polygon fill=\"rgb(").Append(N(color.X*255)).Append(",").Append(N(color.Y*255)).Append(",").Append(N(color.Z*255)).Append(")\" fill-opacity=\"").Append(N(color.W)).Append("\" points=\"");
            for(int j=0;j<3;j++){var p=Vector3D.Transform(item.Geometry.Points[item.Geometry.Triangles[i+j]],item.Transform);result.Append(N(p.X/s)).Append(",").Append(N((hud?1:-1)*p.Y/s)).Append(" ");}result.Append("\"/>");
        }
        result.Append("</svg>");return result.ToString();
    }
    static string N(double x){return x.ToString("R",CultureInfo.InvariantCulture);}
    static void Coordinates(Endpoint api,HtmlRetainedPainter painter)
    {
        foreach(var item in api.Owner.Contexts[painter.Context].Items.Values)foreach(var point in item.Geometry.Points)
        {
            var p=Vector3D.Transform(point,item.Transform);double x=p.X/painter.UnitsPerPixel,y=(painter.IsHud?1:-1)*p.Y/painter.UnitsPerPixel;
            Check(x>=4.999&&x<=785.001&&y>=4.999&&y<=440.001,"logical coordinates agree across HUD/world");
        }
    }
    static void Run(string output)
    {
        bool limitsRejected=false;try{new HtmlVectorPainter(new Endpoint(),true,MatrixD.Identity,limits:new HtmlPainterLimits {MaxOperations=0});}catch(ArgumentException){limitsRejected=true;}Check(limitsRejected,"zero operation grant rejected at construction");
        UnlimitedGeometry();
        Directory.CreateDirectory(output);var api=new Endpoint();var frame=Frame(api,5);HtmlPaintReport report;
        using(var hud=new HtmlVectorPainter(api,true,MatrixD.Identity))using(var world=new HtmlVectorPainter(api,false,MatrixD.Identity,.01))
        {
            Check(hud.TryPaint(frame,out report),"HUD initial: "+report.Error);Check(report.Changed&&report.PreparedOperations==6,"initial retained items");Coordinates(api,hud);
            Check(world.TryPaint(frame,out report),"world initial: "+report.Error);Coordinates(api,world);
            File.WriteAllText(Path.Combine(output,"actual-hud.svg"),GeometryPreview(api,hud.Context,true,1));File.WriteAllText(Path.Combine(output,"actual-world.svg"),GeometryPreview(api,world.Context,false,.01));
            int count=api.Mutations,cost=api.CostCalls;Check(hud.TryPaint(frame,out report)&&!report.Changed&&report.MutatingCalls==0,"unchanged no mutations");Check(api.Mutations==count&&api.CostCalls==cost,"unchanged avoids compilation");
            frame.Operations[0].Color=new HtmlColor(.12,.16,.22,.95);cost=api.CostCalls;Check(hud.TryPaint(frame,out report),"background dirty update");Check(api.CostCalls-cost==1,"background update reuses unchanged text compiled costs");
            var c=api.Owner.Contexts[hud.Context];foreach(var item in c.Items.Values)if(item.Id!=HtmlRetainedPainter.ItemId("panel"))Check(item.Order>0,"dirty background retains paint order");
            frame.Operations[1].Text="NEXT";api.FailCommand="item-order";Check(!hud.TryPaint(frame,out report)&&report.RestoredPrevious,"partial failure restores owned prior paint");Check(hud.TryPaint(frame,out report),"retry after rollback");
            long old=hud.Context;api.Call("destroy",old);Check(hud.TryPaint(frame,out report)&&hud.Context!=old&&report.Changed,"context loss rebuilds identical frame");
            api.Reconnect();Check(hud.TryPaint(frame,out report)&&report.Changed,"endpoint generation rebuilds identical frame");
        }
        api=new Endpoint();frame=Frame(api,5);
        var mutableLimits=new HtmlPainterLimits();using(var fixedPainter=new HtmlVectorPainter(api,true,MatrixD.Identity,limits:mutableLimits)){Check(fixedPainter.TryPaint(frame,out report),"detached limits initial");mutableLimits.MaxItems=1;mutableLimits.CurveSegments=32;Check(fixedPainter.TryPaint(frame,out report)&&!report.Changed,"caller limits mutation does not alter fixed painter config");}
        using(var grouped=new HtmlSvgPainter(api,true,MatrixD.Identity))
        {
            Check(grouped.TryPaint(frame,out report),"grouped SVG: "+report.Error);Check(report.PreparedOperations<frame.Operations.Count,"grouped lowers item count");Coordinates(api,grouped);
            File.WriteAllText(Path.Combine(output,"actual-grouped.svg"),GeometryPreview(api,grouped.Context,true,1));
            int before=api.Mutations;Check(grouped.TryPaint(frame,out report)&&report.MutatingCalls==0&&api.Mutations==before,"grouped cached no-op");
        }
        api=new Endpoint();var clipped=new HtmlPaintFrame{Width=100,Height=100,FontProfile=HtmlHdrTextMetrics.MetricSchema};clipped.Operations.Add(new HtmlPaintOperation{Key="clipped",Kind="text",Text="HH",Order=0,Bounds=new HtmlRect(10,10,40,20),FontSize=20,LineHeight=1.3,Color=new HtmlColor(1,1,1,1),HasClip=true,Clip=new HtmlRect(15,10,10,20)});
        using(var painter=new HtmlVectorPainter(api,true,MatrixD.Identity)){Check(painter.TryPaint(clipped,out report),"real partial glyph clip: "+report.Error);var item=new List<ModClientItem>(api.Owner.Contexts[painter.Context].Items.Values)[0];Check(item.Colors!=null,"clipped glyph uses SVG triangles");foreach(var point in item.Geometry.Points){var p=Vector3D.Transform(point,item.Transform);Check(p.X>=14.999&&p.X<=25.001,"actual glyph geometry clipped");}}
        api=new Endpoint();var excessive=Frame(api,0);for(int i=0;i<65;i++)excessive.Operations.Add(new HtmlPaintOperation {Key="many-"+i,Kind="rect",Order=excessive.Operations.Count,Bounds=new HtmlRect(10,10,10,10),Color=new HtmlColor(1,1,1,1)});using(var painter=new HtmlVectorPainter(api,true,MatrixD.Identity)){int before=api.Mutations;Check(!painter.TryPaint(excessive,out report)&&api.Mutations==before,"oversized frame rejected before mutations");}
        api=new Endpoint();var retired=new HtmlVectorPainter(api,true,MatrixD.Identity);retired.Dispose();int beforeDisposed=api.Mutations;Check(!retired.TryPaint(Frame(api,1),out report)&&report.MutatingCalls==0&&api.Mutations==beforeDisposed,"retained disposed guard fails closed without renderer mutation");Check(report.Error.EndsWith("has been disposed.",StringComparison.Ordinal),"retained disposed diagnostic remains explicit with whitelisted exception");
        var stroke=new HtmlPaintFrame{Width=100,Height=100,FontProfile=HtmlHdrTextMetrics.MetricSchema};stroke.Operations.Add(new HtmlPaintOperation{Key="stroke-only",Kind="rect",Order=0,Bounds=new HtmlRect(20,20,40,40),Color=new HtmlColor(0,0,0,0),Stroke=new HtmlColor(1,0,0,1),StrokeWidth=4,HasClip=true,Clip=new HtmlRect(18,25,1,20)});
        api=new Endpoint();using(var painter=new HtmlSvgPainter(api,true,MatrixD.Identity)){Check(painter.TryPaint(stroke,out report)&&report.EstimatedTriangles>0,"stroke-only geometry outside fill bounds survives clipping");stroke.Operations[0].Stroke=new HtmlColor(double.NaN,0,0,1);int before=api.Mutations;Check(!painter.TryPaint(stroke,out report)&&api.Mutations==before,"invalid stroke rejected before mutation");}
        var stats=new StringBuilder("# CPU preparation and retained update comparison\n\nReal HDR packaged glyph and SVG compilers run against installed game references. No GPU, live game, or native draw-thread timings are claimed. Cost preflight compiles text/SVG; a successful source upsert compiles again.\n\n|Frame|Backend|Initial ms|Prepared items|Actual points|Actual triangles|Mutating calls|1000 no-op ms|No-op mutations|Dirty calls|\n|---|---|---:|---:|---:|---:|---:|---:|---:|---:|\n");
        Benchmark(stats,5,false);Benchmark(stats,5,true);Benchmark(stats,20,false);Benchmark(stats,20,true);File.WriteAllText(Path.Combine(output,"BENCHMARK.md"),stats.ToString());
        Console.WriteLine("Renderer assertions: "+checks);
    }
    static HtmlPaintFrame LargeGeometryFrame(Endpoint api)
    {
        var frame=new HtmlPaintFrame{Width=800,Height=800,FontProfile=HtmlHdrTextMetrics.MetricSchema,Revision=1};var metrics=new HtmlHdrTextMetrics(api);string text=new string('A',64);var measured=metrics.Measure(text,8,1.3);
        for(int i=0;i<32;i++)frame.Operations.Add(new HtmlPaintOperation{Key="dense-text-"+i,Order=i,Kind="text",Text=text,Bounds=new HtmlRect(5,10+i*20,measured.Advance,8),FontSize=8,LineHeight=1.3,Color=new HtmlColor(1,1,1,1)});
        return frame;
    }
    static void UnlimitedGeometry()
    {
        var defaults=new HtmlPainterLimits();Check(defaults.MaxPoints==0&&defaults.MaxPrimitives==0,"aggregate painter geometry defaults are explicitly unlimited");
        var api=new Endpoint();var frame=LargeGeometryFrame(api);HtmlPaintReport report;int points,primitives;
        using(var painter=new HtmlVectorPainter(api,true,MatrixD.Identity))
        {
            Check(painter.TryPaint(frame,out report),"unlimited painter plus actual Core accepts dense real font geometry: "+report.Error);
            points=report.EstimatedPoints;primitives=report.EstimatedTriangles;Check(points>8192&&primitives>8192,"actual retained geometry exceeds both former aggregate allowances");
            var usage=(MyTuple<int,int>)api.Call("geometry-usage");Check(usage.Item1==points&&usage.Item2==primitives,"actual Core owns every accepted point and primitive");
        }
        api=new Endpoint();frame=LargeGeometryFrame(api);api.Call("geometry-limit",8192,8192);
        using(var painter=new HtmlVectorPainter(api,true,MatrixD.Identity))
        {int before=api.Mutations;Check(!painter.TryPaint(frame,out report)&&api.Mutations==before&&report.MutatingCalls==0,"explicit finite owner allowance rejects before retained mutation");Check(painter.Context==0,"finite owner rejection allocates no hidden context");}
        api=new Endpoint();frame=LargeGeometryFrame(api);
        using(var painter=new HtmlVectorPainter(api,true,MatrixD.Identity,limits:new HtmlPainterLimits{MaxPoints=8192,MaxPrimitives=8192}))
        {int before=api.Mutations;Check(!painter.TryPaint(frame,out report)&&api.Mutations==before&&report.MutatingCalls==0,"explicit finite painter allowance preserves the earlier overflow gate");}
        api=new Endpoint();frame=LargeGeometryFrame(api);api.Call("geometry-limit",points+1,primitives+1);
        using(var painter=new HtmlVectorPainter(api,true,MatrixD.Identity,limits:new HtmlPainterLimits{MaxPoints=points+1,MaxPrimitives=primitives+1}))
        {Check(painter.TryPaint(frame,out report),"finite allowances above the former maximum are configurable: "+report.Error);}
        api=new Endpoint();frame=LargeGeometryFrame(api);
        using(var painter=new HtmlVectorPainter(api,true,MatrixD.Identity,limits:new HtmlPainterLimits{MaxPoints=0,MaxPrimitives=primitives+1}))
        {Check(painter.TryPaint(frame,out report),"zero point allowance remains unlimited beside a finite primitive allowance: "+report.Error);}
        bool rejected=false;try{new HtmlVectorPainter(api,true,MatrixD.Identity,limits:new HtmlPainterLimits{MaxPoints=-1});}catch(ArgumentException){rejected=true;}Check(rejected,"negative geometry allowances are rejected rather than normalized to unlimited");
    }
    static void Benchmark(StringBuilder stats,int rows,bool svg)
    {
        var api=new Endpoint();var frame=Frame(api,rows);HtmlPaintReport report;
        using(HtmlRetainedPainter painter=svg?(HtmlRetainedPainter)new HtmlSvgPainter(api,true,MatrixD.Identity):new HtmlVectorPainter(api,true,MatrixD.Identity))
        {
            var clock=Stopwatch.StartNew();bool ok=painter.TryPaint(frame,out report);clock.Stop();Check(ok,"benchmark admission: "+report.Error);double initial=clock.Elapsed.TotalMilliseconds;int items=report.PreparedOperations,points=report.EstimatedPoints,triangles=report.EstimatedTriangles,mutations=report.MutatingCalls;
            int before=api.Mutations;clock.Restart();for(int i=0;i<1000;i++)Check(painter.TryPaint(frame,out report)&&!report.Changed,"benchmark no-op");clock.Stop();int noop=api.Mutations-before;
            frame.Operations[1].Text="Updated";Check(painter.TryPaint(frame,out report),"benchmark dirty");
            stats.Append(rows).Append(" rows|").Append(svg?"Grouped SVG":"Retained elements").Append('|').Append(N(initial)).Append('|').Append(items).Append('|').Append(points).Append('|').Append(triangles).Append('|').Append(mutations).Append('|').Append(N(clock.Elapsed.TotalMilliseconds)).Append('|').Append(noop).Append('|').Append(report.MutatingCalls).Append("|\n");
        }
    }
    static void Main(string[] args){Run(args.Length==0?Directory.GetCurrentDirectory():args[0]);}
}
