using System;
using System.Collections.Generic;
using System.Globalization;
using VRage;
using VRageMath;

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
        readonly Dictionary<string,HtmlPbSourceAttachment> sources=new Dictionary<string,HtmlPbSourceAttachment>(StringComparer.Ordinal);
        int nextSource;
        object scriptToken;
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
        internal bool ClaimScript(object token)
        {if(disposed)return false;if(scriptToken!=null)return ReferenceEquals(scriptToken,token);scriptToken=token;return true;}
        internal bool ScriptValid(object token){return !disposed&&scriptToken!=null&&ReferenceEquals(scriptToken,token);}
        internal bool ReleaseScript(object token){if(!ScriptValid(token))return false;scriptToken=null;return true;}
        internal MyTuple<bool,string> SourceCheck(string provider,string source)
        {
            var surface=Painter as HtmlSurfacePainter;if(disposed||surface==null)return new MyTuple<bool,string>(false,"Unsupported: this document has no retained local source consumer context.");
            try{surface.ValidateSource(provider,source);return new MyTuple<bool,string>(true,"Supported");}
            catch(ArgumentException error){return new MyTuple<bool,string>(false,BoundReason(error.Message));}
            catch(InvalidOperationException error){return new MyTuple<bool,string>(false,BoundReason(error.Message));}
        }
        static string BoundReason(string reason){return string.IsNullOrEmpty(reason)?"Source capability check failed.":reason.Length<=512?reason:reason.Substring(0,512);}
        internal bool Mutate(MyTuple<string,object[]>[] changes,int tick)
        {
            var surface=Painter as HtmlSurfacePainter;
            if(disposed||surface==null||!RequiresHdr)throw new ArgumentException("Unsupported: atomic mutation batches require one retained local HUD/world/surface document; physical native LCD publication has no reversible context transaction.");
            if(changes==null||changes.Length>64)throw new ArgumentException("Mutation batch requires at most 64 standard tuple entries.");
            var captured=new MyTuple<string,object[]>[changes.Length];long capturedCharacters=0;
            for(int i=0;i<changes.Length;i++)
            {
                string operation=changes[i].Item1;var args=changes[i].Item2;
                if(operation!="text"&&operation!="data"&&operation!="attach-source"&&operation!="detach-source")throw new ArgumentException("Unsupported mutation operation.");
                if(args==null||args.Length>4)throw new ArgumentException("Mutation entry requires a bounded argument array (at most four values).");
                var copy=(object[])args.Clone();foreach(var value in copy)if(value is string)capturedCharacters+=((string)value).Length;
                if(capturedCharacters>131072)throw new ArgumentException("Mutation batch exceeds 131072 direct string characters.");
                if(operation=="attach-source"&&copy.Length==4){var hints=copy[3] as MyTuple<string,object[]>[];if(hints==null)throw new ArgumentException("Source hints require MyTuple<string,object[]>[].");copy[3]=HtmlPbSourceOptions.Copy(hints);}
                captured[i]=new MyTuple<string,object[]>(operation,copy);
            }
            surface.RequireMutationContext();
            object guardedScriptToken=scriptToken;
            var candidate=Controller.Fork();var desiredSources=new Dictionary<string,HtmlPbSourceAttachment>(sources,StringComparer.Ordinal);
            var desiredKeys=new HashSet<string>(BindingKeys,StringComparer.Ordinal);var textEdits=new Dictionary<string,string>(pendingText,StringComparer.Ordinal);
            var attachments=new HashSet<string>(StringComparer.Ordinal);int desiredNextSource=nextSource;long characters=0;
            foreach(var change in captured)
            {
                string operation=change.Item1;var args=change.Item2;
                if(args==null)throw new ArgumentException("Mutation arguments require an object array.");
                foreach(var value in args)if(value is string)characters+=((string)value).Length;
                if(characters>131072)throw new ArgumentException("Mutation batch exceeds 131072 direct string characters.");
                if(operation=="text"||operation=="data")
                {
                    if(args.Length!=2||!(args[0] is string)||!(args[1] is string))throw new ArgumentException("Text/data mutations require exactly two string arguments.");
                    string key=(string)args[0],value=(string)args[1];bool changed;
                    if(operation=="data")
                    {
                        if(desiredKeys.Count>=128&&!desiredKeys.Contains(key))throw new ArgumentException("Document binding-key limit reached (128).");
                        changed=candidate.SetData(key,value);if(changed)desiredKeys.Add(key);
                    }
                    else{changed=candidate.SetText(key,value);if(changed)textEdits[key]=value;}
                    if(candidate.LastError!=null)throw new ArgumentException(candidate.LastError);
                }
                else if(operation=="attach-source")
                {
                    if(args.Length<3||args.Length>4||!(args[0] is string)||!(args[1] is string)||!(args[2] is string))throw new ArgumentException("Source attachment mutation requires node, provider, source ID and optional named hints.");
                    string node=(string)args[0],provider=(string)args[1],source=(string)args[2];MyTuple<string,object[]>[] hints=null;
                    if(args.Length==4){hints=args[3] as MyTuple<string,object[]>[];if(hints==null)throw new ArgumentException("Source hints require MyTuple<string,object[]>[].");}
                    var options=HtmlPbSourceOptions.Copy(hints);surface.ValidateSource(provider,source);
                    HtmlPbSourceAttachment previous;desiredSources.TryGetValue(node,out previous);
                    if(previous==null&&desiredSources.Count>=16)throw new ArgumentException("HTML source attachment limit reached (16).");
                    string slot=previous==null?"hs"+(checked(desiredNextSource++)).ToString("x",CultureInfo.InvariantCulture):previous.Slot;
                    desiredSources[node]=new HtmlPbSourceAttachment{Node=node,Provider=provider,Source=source,Slot=slot,Settings=options};attachments.Add(node);
                }
                else if(operation=="detach-source")
                {if(args.Length!=1||!(args[0] is string))throw new ArgumentException("Source detach mutation requires exactly one node ID.");desiredSources.Remove((string)args[0]);attachments.Remove((string)args[0]);}
                else throw new ArgumentException("Unsupported mutation operation: "+operation);
            }
            if(candidate.IsDirty&&!candidate.Update())throw new ArgumentException(candidate.LastError??"Mutation candidate layout failed.");
            foreach(string node in attachments)if(candidate.Document==null||string.IsNullOrEmpty(node)||!candidate.Document.ById.ContainsKey(node))throw new ArgumentException("Source attachment mutation requires an explicit node ID in the final candidate document: "+node);
            if(guardedScriptToken!=null&&!ReferenceEquals(scriptToken,guardedScriptToken)){LastError="Mutation script claim was released before publication; desired state and prior frame were preserved.";return false;}
            surface.Sources=desiredSources.Values;HtmlPaintReport report;
            bool painted;
            try{painted=Painter.TryPaint(candidate.Frame,out report);}
            catch(Exception error)
            {surface.Sources=sources.Values;surface.Reset();RetireBatchPublication(tick);LastError="Mutation publication failed unexpectedly; prior renderer context and input were retired. "+error.Message;return false;}
            if(!painted)
            {
                surface.Sources=sources.Values;LastError=report==null?"Mutation painter failed without a report.":report.Error;
                if(report==null||(report.Changed||report.MutatingCalls>0)&&!report.RestoredPrevious)
                {surface.Reset();RetireBatchPublication(tick);LastError=(LastError??"Mutation publication failed.")+" Prior renderer context could not be restored; input is retired.";}
                return false;
            }
            if(guardedScriptToken!=null&&!ReferenceEquals(scriptToken,guardedScriptToken))
            {
                surface.Sources=sources.Values;
                try
                {HtmlPaintReport restored;if(VisibleFrame!=null&&Painter.TryPaint(VisibleFrame,out restored)){LastError="Mutation script claim was released during publication; prior frame was restored and desired state was preserved.";return false;}}
                catch{}
                surface.Reset();RetireBatchPublication(tick);LastError="Mutation script claim was released during publication; prior renderer context could not be restored and input is retired.";return false;
            }
            Controller.Commit(candidate);sources.Clear();foreach(var pair in desiredSources)sources.Add(pair.Key,pair.Value);surface.Sources=sources.Values;nextSource=desiredNextSource;
            BindingKeys.Clear();foreach(string key in desiredKeys)BindingKeys.Add(key);foreach(var pair in textEdits)TextEdits[pair.Key]=pair.Value;
            pendingText.Clear();pendingData.Clear();pendingResize=false;PendingPaint=false;NextAttempt=0;LastError=null;PublishVisibleFrame(Controller.Frame);return true;
        }
        void RetireBatchPublication(int tick)
        {VisibleFrame=null;CancelPointer();events.Clear();PendingPaint=true;NextAttempt=tick+60;}
        void PublishVisibleFrame(HtmlPaintFrame frame)
        {
            bool viewportChanged=VisibleFrame!=null&&(VisibleFrame.Width!=frame.Width||VisibleFrame.Height!=frame.Height);
            VisibleFrame=HtmlPaintDiff.Snapshot(frame);
            if(pointerHeld&&pressedRevision!=VisibleFrame.Revision)
            {
                HtmlHitRegion next=null;if(pressed!=null)foreach(var hit in VisibleFrame.Hits)if(hit.NodeId==pressed.NodeId){next=hit;break;}
                if(!viewportChanged&&SameGrab(pressed,next)){pressed=next;pressedRevision=VisibleFrame.Revision;}else CancelPointer();
            }
        }
        internal bool AttachSource(string node,string provider,string source,MyTuple<string,object[]>[] settings,int tick)
        {
            var surface=Painter as HtmlSurfacePainter;if(surface==null)throw new ArgumentException("Unsupported: physical native LCD cannot embed external engine textures. Local source attachments require a retained HUD/world/surface document with an actual source anchor.");
            if(Controller.Document==null||string.IsNullOrEmpty(node)||!Controller.Document.ById.ContainsKey(node))throw new ArgumentException("Source attachment requires an existing explicit HTML node ID.");
            var options=HtmlPbSourceOptions.Copy(settings);surface.ValidateSource(provider,source);
            HtmlPbSourceAttachment previous;sources.TryGetValue(node,out previous);
            if(previous==null&&sources.Count>=16)throw new ArgumentException("HTML source attachment limit reached (16).");
            string slot=previous==null?"hs"+(checked(nextSource++)).ToString("x",CultureInfo.InvariantCulture):previous.Slot;
            sources[node]=new HtmlPbSourceAttachment{Node=node,Provider=provider,Source=source,Slot=slot,Settings=options};surface.Sources=sources.Values;
            PendingPaint=true;NextAttempt=0;Update(tick,true);if(!PendingPaint)return true;
            if(previous==null)sources.Remove(node);else sources[node]=previous;return false;
        }
        internal bool DetachSource(string node,int tick)
        {
            HtmlPbSourceAttachment previous;if(!sources.TryGetValue(node,out previous))return false;
            sources.Remove(node);PendingPaint=true;NextAttempt=0;Update(tick,true);if(!PendingPaint)return true;sources[node]=previous;return false;
        }
        internal MyTuple<bool,string> SourceStatus(string node)
        {HtmlPbSourceAttachment source;if(!sources.TryGetValue(node,out source))return new MyTuple<bool,string>(false,"Detached");var surface=Painter as HtmlSurfacePainter;return surface==null?new MyTuple<bool,string>(false,"Unsupported renderer."):surface.SourceStatus(source);}
        internal string[] SourceCapabilities()
        {var surface=Painter as HtmlSurfacePainter;return surface==null?new[]{"Unsupported: this renderer has no local source consumer context. Use CreateSurface."}:surface.SourceCapabilities();}
        internal void PointerRay(Vector3D origin,Vector3D direction,bool pressed)
        {
            if(!Finite(origin.X)||!Finite(origin.Y)||!Finite(origin.Z)||!Finite(direction.X)||!Finite(direction.Y)||!Finite(direction.Z)||direction.LengthSquared()<1e-12||!Finite(direction.LengthSquared()))throw new ArgumentException("Pointer ray must use finite origin and nonzero finite direction.");
            var surface=Painter as HtmlSurfacePainter;if(surface==null)throw new ArgumentException("PointerRay requires a mapped CreateSurface document.");
            if(VisibleFrame==null||!InputEnabled){CancelPointer();Pointer(-1,-1,pressed);return;}
            var hit=surface.Ray(origin,direction);
            if(!hit.Item1){CancelPointer();Pointer(-1,-1,pressed);return;}
            Pointer(hit.Item2.X/surface.Units+VisibleFrame.Width*.5,VisibleFrame.Height*.5-hit.Item2.Y/surface.Units,pressed);
        }

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
                    PublishVisibleFrame(Controller.Frame);PendingPaint=false;LastError=Controller.LastError;
                    // A new layout invalidates a grab; it may move/rebind the original element.
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
        public void Dispose(){if(disposed)return;disposed=true;scriptToken=null;CancelPointer();events.Clear();VisibleFrame=null;try{Painter.Dispose();}catch{}TextEdits.Clear();BindingKeys.Clear();pendingText.Clear();pendingData.Clear();sources.Clear();}
    }
}
