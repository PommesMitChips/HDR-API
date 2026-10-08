using System;
using System.Collections.Generic;
using System.Globalization;
using VRage;

namespace Hdr.Html
{
    /// <summary>One retained document. Input always uses the successfully committed paint, never a failed candidate.</summary>
    internal sealed class HtmlFrontendDocument : IDisposable
    {
        internal readonly long Handle;
        internal readonly string Backend;
        internal readonly HtmlDocumentController Controller;
        internal readonly IHtmlPainter Painter;
        internal readonly bool RequiresHdr;
        internal HtmlPaintFrame VisibleFrame;
        internal string Markup, Stylesheet, LastError;
        internal bool PendingPaint;
        internal bool InputEnabled=true;
        internal int NextAttempt;
        internal readonly Dictionary<string, string> TextEdits = new Dictionary<string, string>(StringComparer.Ordinal);
        internal readonly HashSet<string> BindingKeys = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string,string> pendingText=new Dictionary<string,string>(StringComparer.Ordinal);
        readonly Dictionary<string,string> pendingData=new Dictionary<string,string>(StringComparer.Ordinal);
        bool pendingResize;double pendingWidth,pendingHeight;
        readonly List<MyTuple<string,string,string,MyTuple<double,long>>> events = new List<MyTuple<string,string,string,MyTuple<double,long>>>();
        HtmlHitRegion pressed;
        long pressedRevision;
        bool pointerHeld, awaitNeutral, disposed;
        double lastRange;
        const int MaxEvents = 128;

        internal HtmlFrontendDocument(long handle, string backend, HtmlDocumentController controller, IHtmlPainter painter, bool requiresHdr)
        { Handle=handle; Backend=backend; Controller=controller; Painter=painter; RequiresHdr=requiresHdr; }

        internal bool Load(string markup,string css,double width,double height)
        {
            if(!Controller.TryLoad(markup,css,width,height)){LastError=Controller.LastError;return false;}
            CancelPointer();events.Clear();Markup=markup;Stylesheet=css;TextEdits.Clear();pendingText.Clear();pendingData.Clear();pendingResize=false;PendingPaint=true;NextAttempt=0;LastError=null;return true;
        }
        internal bool SetData(string key,string value)
        {
            if(BindingKeys.Count>=128&&!BindingKeys.Contains(key))throw new ArgumentException("Document binding-key limit reached (128).");
            bool changed=Controller.SetData(key,value);if(changed){BindingKeys.Add(key);pendingData[key]=value;}else if(Controller.LastError!=null)LastError=Controller.LastError;return changed;
        }
        internal bool SetText(string node,string text)
        {bool changed=Controller.SetText(node,text);if(changed)pendingText[node]=text;else if(Controller.LastError!=null)LastError=Controller.LastError;return changed;}
        internal bool Resize(double width,double height)
        {bool changed=Controller.Resize(width,height);if(changed){pendingResize=true;pendingWidth=width;pendingHeight=height;}else if(Controller.LastError!=null)LastError=Controller.LastError;return changed;}

        internal void Update(int tick,bool rendererReady)
        {
            if(disposed)return;
            if(RequiresHdr&&!rendererReady){Suspend("Requires mod: HDR API with HTML text metrics and geometry queries.");return;}
            try
            {
                if(Controller.IsDirty)
                {
                    bool changed=Controller.Update();
                    if(changed){foreach(var pair in pendingText)TextEdits[pair.Key]=pair.Value;PendingPaint=true;NextAttempt=0;}
                    else if(Controller.LastError!=null)LastError=Controller.LastError;
                    pendingText.Clear();pendingData.Clear();pendingResize=false;
                }
                if(!PendingPaint||tick<NextAttempt)return;
                HtmlPaintReport report;
                if(Painter.TryPaint(Controller.Frame,out report))
                {
                    // Painter commits a snapshot. The controller frame is immutable between successful layouts.
                    bool viewportChanged=VisibleFrame!=null&&(VisibleFrame.Width!=Controller.Frame.Width||VisibleFrame.Height!=Controller.Frame.Height);
                    VisibleFrame=HtmlPaintDiff.Snapshot(Controller.Frame);PendingPaint=false;LastError=Controller.LastError;
                    // A new layout invalidates a grab; it may move/rebind the original element.
                    if(pointerHeld&&pressedRevision!=VisibleFrame.Revision)
                    {
                        HtmlHitRegion next=null;
                        if(pressed!=null)foreach(var hit in VisibleFrame.Hits)if(hit.NodeId==pressed.NodeId){next=hit;break;}
                        if(!viewportChanged&&SameGrab(pressed,next)){pressed=next;pressedRevision=VisibleFrame.Revision;}
                        else CancelPointer();
                    }
                }
                else
                {
                    LastError=report==null?"HTML painter failed without a report.":report.Error;
                    NextAttempt=tick+60;
                    // Prepare failures mutate no paint. A failed restore retires the context and all its input.
                    if(report!=null&&(report.Changed||report.MutatingCalls>0)&&!report.RestoredPrevious){VisibleFrame=null;CancelPointer();}
                }
            }
            catch(Exception error){LastError=error.Message;NextAttempt=tick+60;}
        }

        internal void Suspend(string reason)
        {CancelPointer();VisibleFrame=null;PendingPaint=true;LastError=reason;NextAttempt=0;}

        internal bool RebuildForRenderer()
        {
            if(Controller.Frame==null)return false;
            double width=Controller.Frame.Width,height=Controller.Frame.Height;
            if(!Controller.TryLoad(Markup,Stylesheet,width,height)){LastError=Controller.LastError;return false;}
            foreach(var pair in TextEdits)Controller.SetText(pair.Key,pair.Value);
            if(Controller.IsDirty&&!Controller.Update()){LastError=Controller.LastError;return false;}
            // First restore committed text. A bad queued edit must not erase that valid state after reconnect.
            foreach(var pair in pendingText)Controller.SetText(pair.Key,pair.Value);
            foreach(var pair in pendingData)Controller.SetData(pair.Key,pair.Value);
            if(pendingResize)Controller.Resize(pendingWidth,pendingHeight);
            bool accepted=!Controller.IsDirty||Controller.Update();
            if(accepted)foreach(var pair in pendingText)TextEdits[pair.Key]=pair.Value;
            else LastError=Controller.LastError;
            pendingText.Clear();pendingData.Clear();pendingResize=false;
            PendingPaint=true;NextAttempt=0;return accepted;
        }

        internal void Pointer(double x,double y,bool held)
        {
            if(!Finite(x)||!Finite(y)||Math.Abs(x)>1e6||Math.Abs(y)>1e6)throw new ArgumentException("Pointer coordinates must be finite authored viewport pixels.");
            if(disposed||VisibleFrame==null||!InputEnabled){CancelPointer();awaitNeutral|=held;if(!held)awaitNeutral=false;return;}
            if(awaitNeutral){if(!held)awaitNeutral=false;return;}
            if(!pointerHeld&&held)
            {
                pressed=Hit(VisibleFrame,x,y);pressedRevision=VisibleFrame.Revision;lastRange=pressed==null?0:pressed.Value;
            }
            if(held&&pressed!=null&&pressed.Kind=="range")Range(x);
            if(pointerHeld&&!held&&pressed!=null&&pressed.Kind=="button")
            {
                HtmlHitRegion release=Hit(VisibleFrame,x,y);
                if(pressedRevision==VisibleFrame.Revision&&release!=null&&release.NodeId==pressed.NodeId)Enqueue("click",pressed,0);
            }
            pointerHeld=held;if(!held){pressed=null;pressedRevision=0;}
        }
        void Range(double x)
        {
            if(pressedRevision!=VisibleFrame.Revision||pressed.Maximum<=pressed.Minimum||pressed.Bounds.Width<=0)return;
            double ratio=Math.Max(0,Math.Min(1,(x-pressed.Bounds.X)/pressed.Bounds.Width));
            double value=pressed.Minimum+(pressed.Maximum-pressed.Minimum)*ratio;
            if(pressed.Step>0)
            {
                double quotient=(value-pressed.Minimum)/pressed.Step;
                if(Finite(quotient))value=pressed.Minimum+Math.Floor(quotient+.5)*pressed.Step;
            }
            value=Math.Max(pressed.Minimum,Math.Min(pressed.Maximum,value));
            if(value==lastRange)return;lastRange=value;
            // The event lets the owner choose its script/mod value; bindings are plain text, never callbacks.
            Enqueue("change",pressed,value);
        }
        void Enqueue(string kind,HtmlHitRegion hit,double value)
        {
            var item=new MyTuple<string,string,string,MyTuple<double,long>>(kind,hit.NodeId,hit.Action??"",new MyTuple<double,long>(value,VisibleFrame.Revision));
            if(kind=="change")for(int i=events.Count-1;i>=0;i--)if(events[i].Item1==kind&&events[i].Item2==hit.NodeId){events[i]=item;return;}
            if(events.Count==MaxEvents)events.RemoveAt(0);events.Add(item);
        }
        internal static HtmlHitRegion Hit(HtmlPaintFrame frame,double x,double y)
        {
            HtmlHitRegion best=null;
            foreach(var hit in frame.Hits)
                if(x>=hit.Bounds.X&&y>=hit.Bounds.Y&&x<hit.Bounds.X+hit.Bounds.Width&&y<hit.Bounds.Y+hit.Bounds.Height&&(best==null||hit.Order>=best.Order))best=hit;
            return best;
        }
        internal void CancelPointer(){awaitNeutral|=pointerHeld;pointerHeld=false;pressed=null;pressedRevision=0;}
        internal void SetInputEnabled(bool enabled){InputEnabled=enabled;if(!enabled){CancelPointer();events.Clear();}}
        static bool SameGrab(HtmlHitRegion a,HtmlHitRegion b)
        {return a!=null&&b!=null&&a.NodeId==b.NodeId&&a.Kind==b.Kind&&a.Binding==b.Binding&&a.Action==b.Action&&a.Bounds.X==b.Bounds.X&&a.Bounds.Y==b.Bounds.Y&&a.Bounds.Width==b.Bounds.Width&&a.Bounds.Height==b.Bounds.Height&&a.Minimum==b.Minimum&&a.Maximum==b.Maximum&&a.Step==b.Step;}
        internal MyTuple<string,string,string,MyTuple<double,long>>[] PollEvents(){var result=events.ToArray();events.Clear();return result;}
        internal MyTuple<string,long,long,MyTuple<bool,bool,string>,long> Status(bool rendererReady)
        {return new MyTuple<string,long,long,MyTuple<bool,bool,string>,long>(Backend,Controller.Revision,VisibleFrame==null?0:VisibleFrame.Revision,new MyTuple<bool,bool,string>(!RequiresHdr||rendererReady,PendingPaint,LastError??""),Controller.LayoutBuildCount);}
        internal static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
        public void Dispose(){if(disposed)return;disposed=true;CancelPointer();events.Clear();VisibleFrame=null;try{Painter.Dispose();}catch{}TextEdits.Clear();BindingKeys.Clear();pendingText.Clear();pendingData.Clear();}
    }
}
