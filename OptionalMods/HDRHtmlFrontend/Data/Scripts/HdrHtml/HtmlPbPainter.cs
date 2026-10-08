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
        readonly string prefix,bundle,layerPrefix;
        readonly MatrixD modelPose;
        readonly double units;
        readonly bool centeredCanvas;
        HtmlPbPlan accepted;
        internal string LastError;
        internal bool Retired;
        internal HtmlPbPlan Accepted{get{return accepted;}}
        internal HtmlPbPainter(long handle,IHtmlPbBridge bridge,MatrixD modelPose,double units,bool centeredCanvas=false)
        {
            if(bridge==null||!Finite(units)||units<1e-6||units>1000)throw new ArgumentException("PB HTML needs a renderer and units per pixel in 0.000001..1000.");
            double determinant=modelPose.Determinant();if(!Finite(determinant)||Math.Abs(determinant)<1e-12)throw new ArgumentException("PB HTML pose must be finite nonsingular affine.");
            double[] numbers={modelPose.M11,modelPose.M12,modelPose.M13,modelPose.M14,modelPose.M21,modelPose.M22,modelPose.M23,modelPose.M24,modelPose.M31,modelPose.M32,modelPose.M33,modelPose.M34,modelPose.M41,modelPose.M42,modelPose.M43,modelPose.M44};
            foreach(double n in numbers)if(!Finite(n)||Math.Abs(n)>1e12)throw new ArgumentException("PB HTML pose must be finite and bounded.");
            if(Math.Abs(modelPose.M14)>1e-9||Math.Abs(modelPose.M24)>1e-9||Math.Abs(modelPose.M34)>1e-9||Math.Abs(modelPose.M44-1)>1e-9)throw new ArgumentException("PB HTML pose must be affine.");
            if(handle<1||handle>uint.MaxValue)throw new ArgumentException("PB HTML handle exceeds its collision-free short layer identity domain.");
            this.bridge=bridge;this.modelPose=modelPose;this.units=units;this.centeredCanvas=centeredCanvas;string root="h"+handle.ToString("x",CultureInfo.InvariantCulture);prefix=root+"_";bundle=root+"b";layerPrefix=root+"l";
        }
        internal bool Paint(HtmlPaintFrame frame,HtmlDocument document,bool forceControls=false,ICollection<HtmlPbSourceAttachment> sources=null)
        {
            HtmlPbPlan next;
            try{next=Prepare(frame,document,forceControls,sources);}catch(Exception error){LastError=error.Message;return false;}
            bool controlsChanged=forceControls||!SameControls(accepted,next);
            try
            {
                if(accepted==null)bridge.Ui("bundle",bundle,true);
                if(controlsChanged)RemoveControls(accepted);
                if(accepted!=null)foreach(var old in accepted.Items)if(!next.Items.Exists(i=>i.Id==old.Id))bridge.Draw("remove",old.Id);
                foreach(var item in next.Items)
                {
                    var old=accepted==null?null:accepted.Items.Find(i=>i.Id==item.Id);
                    if(old==null||old.Source!=item.Source||old.Pose!=item.Pose)bridge.Draw("svg",item.Id,item.Source,item.Pose,12);
                    if(old==null||old.Layer!=item.Layer)bridge.Draw("layer",item.Id,item.Layer);
                    if(old==null||old.Order!=item.Order||old.Layer!=item.Layer)bridge.Draw("layer-order",item.Layer,item.Order);
                }
                ApplySources(next,accepted);
                if(controlsChanged)AttachControls(next);
                RemoveUnusedLayers(accepted,next);
                accepted=next;LastError=null;Retired=false;return true;
            }
            catch(Exception error)
            {
                LastError=error.Message;
                try
                {
                    if(controlsChanged)TryRemoveControls(next);
                    foreach(var item in next.Items)if(accepted==null||!accepted.Items.Exists(i=>i.Id==item.Id))bridge.Draw("remove",item.Id);
                    if(accepted!=null)
                    {
                        if(controlsChanged)bridge.Ui("bundle",bundle,true);
                        foreach(var item in accepted.Items)
                        {
                            var candidate=next.Items.Find(i=>i.Id==item.Id);
                            if(candidate==null||candidate.Source!=item.Source||candidate.Pose!=item.Pose)bridge.Draw("svg",item.Id,item.Source,item.Pose,12);
                            if(candidate==null||candidate.Layer!=item.Layer)bridge.Draw("layer",item.Id,item.Layer);
                            if(candidate==null||candidate.Layer!=item.Layer||candidate.Order!=item.Order)bridge.Draw("layer-order",item.Layer,item.Order);
                        }
                        ApplySources(accepted,next);
                        if(controlsChanged)AttachControls(accepted);
                    }
                    else{RemoveSources(next);try{bridge.Ui("remove",bundle);}catch{}}
                    RemoveUnusedLayers(next,accepted);
                }
                catch{Retired=true;LastError+=" Previous owned declarations could not be restored.";Cleanup(next);Cleanup(accepted);accepted=null;}
                return false;
            }
        }
        HtmlPbPlan Prepare(HtmlPaintFrame frame,HtmlDocument document,bool forceControls,ICollection<HtmlPbSourceAttachment> sources)
        {
            HtmlPaintValidation.Validate(frame,new HtmlLimits{MaxHitRegions=32});
            if(frame.FontProfile!=HtmlHdrTextMetrics.MetricSchema)throw new ArgumentException("PB HTML requires exact HDR Inter metrics.");
            var plan=new HtmlPbPlan{Frame=HtmlPaintDiff.Snapshot(frame)};
            var ancestry=new Dictionary<int,string>();MapButtons(document.Root,null,ancestry);
            var groups=new Dictionary<string,List<HtmlPaintOperation>>(StringComparer.Ordinal);
            foreach(var hit in frame.Hits)groups[hit.NodeId]=new List<HtmlPaintOperation>();
            foreach(var op in frame.Operations)
            {
                if(op.Kind!="rect"&&op.Kind!="text")throw new ArgumentException("PB HTML Profile1 paints rectangles/text; assets are unsupported.");
                string button;int ordinal=Ordinal(op.Key);
                if(ancestry.TryGetValue(ordinal,out button)&&button!=null&&groups.ContainsKey(button)){groups[button].Add(op);continue;}
                if(op.Key.EndsWith(":range-handle",StringComparison.Ordinal)&&groups.ContainsKey(op.NodeId)){groups[op.NodeId].Add(op);continue;}
            }
            MatrixD pose=MatrixD.CreateScale(units,units,1)*(centeredCanvas?MatrixD.Identity:MatrixD.CreateTranslation(frame.Width*.5*units,-frame.Height*.5*units,0))*modelPose;
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
                plan.Controls.Add(c);
            }
            var insertions=new Dictionary<int,List<HtmlPbSourceSlot>>();
            if(sources!=null)foreach(var region in frame.SourceRegions)
            {
                HtmlPbSourceAttachment attachment=null;foreach(var candidate in sources)if(candidate.Node==region.NodeId){attachment=candidate;break;}
                if(attachment==null||!region.Visible||region.Bounds.Empty||region.Clip.Empty||region.Opacity<=0)continue;
                var slot=Slot(attachment,region,frame);
                List<HtmlPbSourceSlot> list;if(!insertions.TryGetValue(region.Order,out list)){list=new List<HtmlPbSourceSlot>();insertions.Add(region.Order,list);}list.Add(slot);
            }
            var run=new List<HtmlPaintOperation>();string runOwner=null;int order=0,batch=0;
            var usedControls=new HashSet<string>(StringComparer.Ordinal);
            for(int position=0;position<=frame.Operations.Count;position++)
            {
                List<HtmlPbSourceSlot> slots;
                if(insertions.TryGetValue(position,out slots))
                {
                    FlushRun(plan,frame,run,runOwner,pose,forceControls,usedControls,ref batch,ref order);runOwner=null;
                    foreach(var slot in slots){slot.Order=order++;plan.Sources.Add(slot);}
                }
                if(position==frame.Operations.Count)break;
                var op=frame.Operations[position];string owner=null,button;
                if(ancestry.TryGetValue(Ordinal(op.Key),out button)&&button!=null&&groups.ContainsKey(button))owner=button;
                if(op.Key.EndsWith(":range-handle",StringComparison.Ordinal)&&groups.ContainsKey(op.NodeId))owner=op.NodeId;
                if(run.Count>0&&runOwner!=owner)FlushRun(plan,frame,run,runOwner,pose,forceControls,usedControls,ref batch,ref order);
                runOwner=owner;run.Add(op);
            }
            FlushRun(plan,frame,run,runOwner,pose,forceControls,usedControls,ref batch,ref order);
            if(plan.Items.Count>32)throw new ArgumentException("PB HTML requires more than 32 ordered artwork layers; simplify the document.");
            foreach(var control in plan.Controls)if(!plan.Items.Exists(i=>i.Id==control.Artwork&&i.Points>0))throw new ArgumentException("PB control artwork has no visible geometry.");
            int points=0,primitives=0;foreach(var item in plan.Items){points=checked(points+item.Points);primitives=checked(primitives+item.Primitives);}
            var budget=(MyTuple<int,int,int>)bridge.Draw("budget-settings");
            if(budget.Item1<0||budget.Item2<0)throw new ArgumentException("HDR returned an invalid selected display geometry allowance.");
            if(budget.Item1>0&&points>budget.Item1||budget.Item2>0&&primitives>budget.Item2)throw new ArgumentException("PB HTML compiled geometry exceeds the selected display budget; simplify the document or explicitly configure HDR's budget.");
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
        void FlushRun(HtmlPbPlan plan,HtmlPaintFrame frame,List<HtmlPaintOperation> run,string owner,MatrixD pose,bool forceControls,HashSet<string> usedControls,ref int batch,ref int order)
        {
            if(run.Count==0)return;
            var control=owner==null?null:plan.Controls.Find(c=>c.Node==owner);
            bool first=control!=null&&usedControls.Add(owner);
            string id=first?control.Artwork:prefix+"p"+batch.ToString("x2",CultureInfo.InvariantCulture);
            var oldControl=accepted==null||control==null?null:accepted.Controls.Find(c=>c.Id==control.Id);
            HtmlPbItem item;
            if(first&&!forceControls&&control.Kind=="range"&&control.Same(oldControl))
            {
                var prior=accepted.Items.Find(i=>i.Id==id);
                item=prior==null?null:new HtmlPbItem{Id=prior.Id,Source=prior.Source,Pose=prior.Pose,Points=prior.Points,Primitives=prior.Primitives};
            }
            else item=Item(id,frame,run,pose);
            if(item==null)throw new ArgumentException("Missing retained control artwork.");
            item.Layer=layerPrefix+batch.ToString("x2",CultureInfo.InvariantCulture);item.Order=order++;
            plan.Items.Add(item);batch++;run.Clear();
        }
        HtmlPbSourceSlot Slot(HtmlPbSourceAttachment attachment,HtmlSourceRegion region,HtmlPaintFrame frame)
        {
            var b=region.Bounds;var c=region.Clip;
            return new HtmlPbSourceSlot{Attachment=attachment,Rect=CanvasRect(b,frame),Clip=CanvasRect(c,frame),UV=new Vector4(0,0,1,1),Pose=modelPose,Opacity=region.Opacity*HtmlPbSourceOptions.Opacity(attachment.Settings)};
        }
        Vector4 CanvasRect(HtmlRect r,HtmlPaintFrame frame)
        {
            return new Vector4((float)((r.X-frame.Width*.5)*units),(float)((frame.Height*.5-r.Y-r.Height)*units),(float)(r.Width*units),(float)(r.Height*units));
        }
        void ApplySources(HtmlPbPlan next,HtmlPbPlan previous)
        {
            if(previous!=null)foreach(var old in previous.Sources)if(!next.Sources.Exists(s=>s.Attachment.Slot==old.Attachment.Slot))bridge.Draw("screen-slot-remove",old.Attachment.Slot);
            foreach(var slot in next.Sources)
            {
                var old=previous==null?null:previous.Sources.Find(s=>s.Attachment.Slot==slot.Attachment.Slot);
                if(slot.Same(old))continue;
                bridge.Draw("screen-slot",slot.Attachment.Slot,slot.Attachment.Provider,slot.Attachment.Source,slot.Rect,slot.Clip,slot.UV,slot.Order,slot.Opacity);
                bridge.Draw("screen-slot-pose",slot.Attachment.Slot,slot.Pose);
                HtmlPbSourceOptions.Apply(bridge,slot.Attachment);
            }
        }
        void RemoveSources(HtmlPbPlan plan)
        {if(plan!=null)foreach(var slot in plan.Sources)try{bridge.Draw("screen-slot-remove",slot.Attachment.Slot);}catch{}}
        void RemoveUnusedLayers(HtmlPbPlan previous,HtmlPbPlan next)
        {if(previous!=null)foreach(var item in previous.Items)if(next==null||!next.Items.Exists(i=>i.Layer==item.Layer))bridge.Draw("layer-remove",item.Layer);}
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
            RemoveSources(plan);
            foreach(var item in plan.Items)try{bridge.Draw("layer-remove",item.Layer);}catch{}
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
