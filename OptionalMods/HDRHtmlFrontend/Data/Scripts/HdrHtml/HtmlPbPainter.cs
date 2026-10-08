using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VRage;
using VRageMath;

namespace Hdr.Html
{
    internal sealed class HtmlPbPainter : IDisposable
    {
        readonly IHtmlPbBridge bridge;
        readonly string prefix,bundle,layer;
        readonly MatrixD modelPose;
        readonly double units;
        HtmlPbPlan accepted;
        internal string LastError;
        internal bool Retired;
        internal HtmlPbPlan Accepted{get{return accepted;}}
        internal HtmlPbPainter(long handle,IHtmlPbBridge bridge,MatrixD modelPose,double units)
        {
            if(bridge==null||!Finite(units)||units<1e-6||units>1000)throw new ArgumentException("PB HTML needs a renderer and units per pixel in 0.000001..1000.");
            double determinant=modelPose.Determinant();if(!Finite(determinant)||Math.Abs(determinant)<1e-12)throw new ArgumentException("PB HTML pose must be finite nonsingular affine.");
            double[] numbers={modelPose.M11,modelPose.M12,modelPose.M13,modelPose.M14,modelPose.M21,modelPose.M22,modelPose.M23,modelPose.M24,modelPose.M31,modelPose.M32,modelPose.M33,modelPose.M34,modelPose.M41,modelPose.M42,modelPose.M43,modelPose.M44};
            foreach(double n in numbers)if(!Finite(n)||Math.Abs(n)>1e12)throw new ArgumentException("PB HTML pose must be finite and bounded.");
            if(Math.Abs(modelPose.M14)>1e-9||Math.Abs(modelPose.M24)>1e-9||Math.Abs(modelPose.M34)>1e-9||Math.Abs(modelPose.M44-1)>1e-9)throw new ArgumentException("PB HTML pose must be affine.");
            this.bridge=bridge;this.modelPose=modelPose;this.units=units;prefix="hp"+handle.ToString("x",CultureInfo.InvariantCulture)+"_";bundle=prefix+"b";layer="ui-"+bundle;
        }
        internal bool Paint(HtmlPaintFrame frame,HtmlDocument document,bool forceControls=false)
        {
            HtmlPbPlan next;
            try{next=Prepare(frame,document,forceControls);}catch(Exception error){LastError=error.Message;return false;}
            bool controlsChanged=forceControls||!SameControls(accepted,next);
            try
            {
                if(accepted==null)bridge.Ui("bundle",bundle,true);
                if(controlsChanged)RemoveControls(accepted);
                if(accepted!=null)foreach(var old in accepted.Items)if(!next.Items.Exists(i=>i.Id==old.Id))bridge.Draw("remove",old.Id);
                foreach(var item in next.Items)
                {
                    var old=accepted==null?null:accepted.Items.Find(i=>i.Id==item.Id);
                    if(old==null||old.Source!=item.Source||old.Pose!=item.Pose){bridge.Draw("svg",item.Id,item.Source,item.Pose,12);bridge.Draw("layer",item.Id,layer);}
                }
                if(controlsChanged)AttachControls(next);
                accepted=next;LastError=null;Retired=false;return true;
            }
            catch(Exception error)
            {
                LastError=error.Message;
                try
                {
                    TryRemoveControls(next);
                    foreach(var item in next.Items)bridge.Draw("remove",item.Id);
                    if(accepted!=null)
                    {
                        bridge.Ui("bundle",bundle,true);
                        foreach(var item in accepted.Items){bridge.Draw("svg",item.Id,item.Source,item.Pose,12);bridge.Draw("layer",item.Id,layer);}
                        AttachControls(accepted);
                    }
                }
                catch{Retired=true;LastError+=" Previous owned declarations could not be restored.";Cleanup(next);Cleanup(accepted);accepted=null;}
                return false;
            }
        }
        HtmlPbPlan Prepare(HtmlPaintFrame frame,HtmlDocument document,bool forceControls)
        {
            HtmlPaintValidation.Validate(frame,new HtmlLimits{MaxHitRegions=32});
            if(frame.FontProfile!=HtmlHdrTextMetrics.MetricSchema)throw new ArgumentException("PB HTML requires exact HDR Inter metrics.");
            var plan=new HtmlPbPlan{Frame=HtmlPaintDiff.Snapshot(frame)};
            var ancestry=new Dictionary<int,string>();MapButtons(document.Root,null,ancestry);
            var groups=new Dictionary<string,List<HtmlPaintOperation>>(StringComparer.Ordinal);
            foreach(var hit in frame.Hits)groups[hit.NodeId]=new List<HtmlPaintOperation>();
            var body=new List<HtmlPaintOperation>();
            foreach(var op in frame.Operations)
            {
                if(op.Kind!="rect"&&op.Kind!="text")throw new ArgumentException("PB HTML Profile1 paints rectangles/text; assets are unsupported.");
                string button;int ordinal=Ordinal(op.Key);
                if(ancestry.TryGetValue(ordinal,out button)&&button!=null&&groups.ContainsKey(button)){groups[button].Add(op);continue;}
                if(op.Key.EndsWith(":range-handle",StringComparison.Ordinal)&&groups.ContainsKey(op.NodeId)){groups[op.NodeId].Add(op);continue;}
                body.Add(op);
            }
            MatrixD pose=MatrixD.CreateScale(units,units,1)*MatrixD.CreateTranslation(frame.Width*.5*units,-frame.Height*.5*units,0)*modelPose;
            if(body.Count>0)plan.Items.Add(Item(prefix+"p0000",frame,body,pose));
            for(int index=0;index<frame.Hits.Count;index++)
            {
                HtmlHitRegion hit=frame.Hits[index];var operations=groups[hit.NodeId];
                if(operations.Count==0)throw new ArgumentException("Interactive "+hit.NodeId+" needs visible authored artwork.");
                string suffix=(index+1).ToString("D4",CultureInfo.InvariantCulture);
                var c=new HtmlPbControl{Id=prefix+"c"+index.ToString("x",CultureInfo.InvariantCulture),ValueId=hit.Kind=="range"?prefix+"v"+index.ToString("x",CultureInfo.InvariantCulture):null,Artwork=prefix+"p"+suffix,Node=hit.NodeId,Kind=hit.Kind,Action=hit.Action??"",Binding=hit.Binding,Hit=hit.Bounds,Value=hit.Value,Min=hit.Minimum,Max=hit.Maximum,Step=hit.Step};
                if(hit.Kind=="range")
                {
                    var knob=operations.Find(o=>o.Key.EndsWith(":range-handle",StringComparison.Ordinal));if(knob==null||knob.Color.A<=0)throw new ArgumentException("PB ranges need a visible knob.");
                    c.Knob=knob.Bounds;double travel=hit.Bounds.Width-knob.Bounds.Width;
                    if(travel<=0||knob.Bounds.Y<hit.Bounds.Y||knob.Bounds.Y+knob.Bounds.Height>hit.Bounds.Y+hit.Bounds.Height)throw new ArgumentException("PB range requires an unclipped positive knob path.");
                    double left=knob.Bounds.X-travel*(hit.Value-hit.Minimum)/(hit.Maximum-hit.Minimum);
                    if(Math.Abs(left-hit.Bounds.X)>.001||left+travel+knob.Bounds.Width>hit.Bounds.X+hit.Bounds.Width+.001)throw new ArgumentException("Partly clipped range paths are unsupported by the PB adapter.");
                    c.Start=new Vector3D(left+knob.Bounds.Width*.5-frame.Width*.5,frame.Height*.5-knob.Bounds.Y-knob.Bounds.Height*.5,0);c.End=c.Start+new Vector3D(travel,0,0);
                }
                // Body-first, then independent control sources. Reject later body content that would need interleaving.
                int first=operations[0].Order;
                foreach(var op in body)if(op.Order>first&&Overlap(op.Bounds,hit.Bounds))throw new ArgumentException("PB HTML does not support body/control overlap requiring interleaved paint order.");
                HtmlPbItem item;
                var old=accepted==null?null:accepted.Controls.Find(o=>o.Id==c.Id);
                if(!forceControls&&c.Kind=="range"&&c.Same(old))
                {
                    // The core moves this original knob via its constraint; ordinary value paint must not rebase its grab.
                    item=accepted.Items.Find(i=>i.Id==c.Artwork);
                }
                else item=Item(c.Artwork,frame,operations,pose);
                if(item==null||item.Points==0)throw new ArgumentException("PB control artwork has no visible geometry.");
                plan.Controls.Add(c);plan.Items.Add(item);
            }
            int points=0,primitives=0;foreach(var item in plan.Items){points=checked(points+item.Points);primitives=checked(primitives+item.Primitives);}
            var budget=(MyTuple<int,int,int>)bridge.Draw("budget-settings");
            if(points>budget.Item1||primitives>budget.Item2)throw new ArgumentException("PB HTML compiled geometry exceeds the selected display budget; simplify the document or explicitly configure HDR's budget.");
            return plan;
        }
        HtmlPbItem Item(string id,HtmlPaintFrame frame,List<HtmlPaintOperation> operations,MatrixD pose)
        {
            var svg=new StringBuilder(HtmlSvgEncoding.Begin(frame));foreach(var op in operations)svg.Append(HtmlSvgEncoding.Element(op,op.Order));svg.Append("</svg>");
            if(svg.Length>65536)throw new ArgumentException("PB HTML SVG item exceeds 65536 characters.");
            string source=svg.ToString();var prior=accepted==null?null:accepted.Items.Find(i=>i.Id==id&&i.Source==source);
            var cost=prior==null?(MyTuple<int,int>)bridge.Draw("geometry-cost","svg",source,1.0,12):new MyTuple<int,int>(prior.Points,prior.Primitives);
            if(cost.Item1<0||cost.Item2<0)throw new ArgumentException("Invalid core geometry cost.");
            return new HtmlPbItem{Id=id,Source=source,Pose=pose,Points=cost.Item1,Primitives=cost.Item2};
        }
        void AttachControls(HtmlPbPlan plan)
        {
            foreach(var c in plan.Controls)
            {
                var frame=plan.Frame;double x=c.Hit.X+c.Hit.Width*.5-frame.Width*.5,y=frame.Height*.5-c.Hit.Y-c.Hit.Height*.5,w=c.Hit.Width,h=c.Hit.Height;
                if(c.Kind=="range"){x=c.Knob.X+c.Knob.Width*.5-frame.Width*.5;y=frame.Height*.5-c.Knob.Y-c.Knob.Height*.5;w=c.Knob.Width;h=c.Knob.Height;bridge.Ui("value",c.ValueId,c.Value,c.Min,c.Max,c.Step);}
                bridge.Ui("control",c.Id,bundle,c.Artwork,x,y,w,h);
                if(c.Kind=="range"){bridge.Ui("bind-value",c.Id,c.ValueId);bridge.Ui("constraint",c.Id,"line",c.Start,c.End);bridge.Ui("draggable",c.Id,true);}
            }
        }
        void RemoveControls(HtmlPbPlan plan)
        {
            if(plan==null)return;
            foreach(var c in plan.Controls)bridge.Ui("remove",c.Id);
            foreach(var c in plan.Controls)if(c.ValueId!=null)bridge.Ui("remove-value",c.ValueId);
        }
        void TryRemoveControls(HtmlPbPlan plan)
        {if(plan==null)return;foreach(var c in plan.Controls)try{bridge.Ui("remove",c.Id);}catch{}foreach(var c in plan.Controls)if(c.ValueId!=null)try{bridge.Ui("remove-value",c.ValueId);}catch{}}
        internal MyTuple<string,string,string,MyTuple<double,long,long>>[] Poll()
        {
            if(accepted==null||accepted.Controls.Count==0)return new MyTuple<string,string,string,MyTuple<double,long,long>>[0];
            var raw=(MyTuple<string,string,string,MyTuple<double,long,long>>[])bridge.Ui("poll-value-events",prefix);
            var result=new List<MyTuple<string,string,string,MyTuple<double,long,long>>>();
            foreach(var item in raw){var c=accepted.Controls.Find(v=>v.Id==item.Item2||v.ValueId!=null&&v.ValueId==item.Item3);if(c!=null)result.Add(new MyTuple<string,string,string,MyTuple<double,long,long>>(item.Item1,c.Node,c.Action,item.Item4));}
            return result.ToArray();
        }
        internal void SyncRanges(HtmlDocumentController controller)
        {
            if(accepted==null)return;foreach(var c in accepted.Controls)if(c.ValueId!=null&&!string.IsNullOrEmpty(c.Binding))
            {var value=(MyTuple<double,long>)bridge.Ui("get-value",c.ValueId);controller.SetData(c.Binding,value.Item1.ToString("R",CultureInfo.InvariantCulture));}
        }
        internal void WriteBinding(string key,string text)
        {
            if(accepted==null)return;
            foreach(var c in accepted.Controls)if(c.ValueId!=null&&c.Binding==key)
            {double value;if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!Finite(value))throw new ArgumentException("Range binding requires a finite number.");bridge.Ui("set-value",c.ValueId,value);}
        }
        void Cleanup(HtmlPbPlan plan)
        {
            if(plan==null)return;
            foreach(var c in plan.Controls)try{bridge.Ui("remove",c.Id);}catch{}
            foreach(var c in plan.Controls)if(c.ValueId!=null)try{bridge.Ui("remove-value",c.ValueId);}catch{}
            foreach(var item in plan.Items)try{bridge.Draw("remove",item.Id);}catch{}
            try{bridge.Ui("remove",bundle);}catch{}
        }
        public void Dispose(){Cleanup(accepted);accepted=null;Retired=true;}
        static bool SameControls(HtmlPbPlan a,HtmlPbPlan b){if(a==null||a.Controls.Count!=b.Controls.Count||a.Frame.Width!=b.Frame.Width||a.Frame.Height!=b.Frame.Height)return false;for(int i=0;i<a.Controls.Count;i++)if(!a.Controls[i].Same(b.Controls[i]))return false;return true;}
        static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
        static bool Overlap(HtmlRect a,HtmlRect b){return a.Width>0&&a.Height>0&&b.Width>0&&b.Height>0&&a.X<b.X+b.Width&&b.X<a.X+a.Width&&a.Y<b.Y+b.Height&&b.Y<a.Y+a.Height;}
        static int Ordinal(string key){int colon=key.IndexOf(':',5);int number;if(!key.StartsWith("html:",StringComparison.Ordinal)||colon<0||!int.TryParse(key.Substring(5,colon-5),out number))throw new ArgumentException("Invalid paint key.");return number;}
        static string Identity(HtmlNode node){return !string.IsNullOrEmpty(node.Id)?node.Id:"node-"+node.Ordinal.ToString(CultureInfo.InvariantCulture);}
        static void MapButtons(HtmlNode node,string parent,Dictionary<int,string> result)
        {if(node.Tag=="button"){if(parent!=null)throw new ArgumentException("Nested PB HTML buttons are unsupported.");parent=Identity(node);}if(node.Tag=="input"&&parent!=null)throw new ArgumentException("A PB range cannot be inside a button.");result[node.Ordinal]=parent;foreach(var child in node.Children)MapButtons(child,parent,result);}
    }
}
