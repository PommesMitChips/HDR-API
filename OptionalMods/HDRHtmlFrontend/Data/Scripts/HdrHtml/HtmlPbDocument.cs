using System;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace Hdr.Html
{
    internal sealed class HtmlPbDocument : IDisposable
    {
        internal readonly long Handle;
        internal readonly IHtmlPbBridge Bridge;
        internal readonly HtmlDocumentController Controller;
        readonly HtmlPbPainter painter;
        readonly HtmlPbNativePainter nativePainter;
        readonly HtmlFrontendDocument nativeDocument;
        readonly HashSet<string> keys=new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string,HtmlPbSourceAttachment> sources=new Dictionary<string,HtmlPbSourceAttachment>(StringComparer.Ordinal);
        int nextSource;
        bool forceControls=true,disposed;
        int nextAttempt;
        bool pending;
        internal bool Pending{get{return nativeDocument==null?pending:nativeDocument.PendingPaint;}}
        internal string LastError{get;private set;}
        internal string TargetKind="display";
        internal HtmlPaintFrame PublishedFrame{get{return nativeDocument==null?(painter.Accepted==null?null:painter.Accepted.Frame):nativeDocument.VisibleFrame;}}
        internal bool Ready{get{return Bridge.Ready&&(nativePainter==null||nativePainter.Ready);}}
        internal HtmlPbDocument(long handle,IHtmlPbBridge bridge,MatrixD modelPose,double units)
        {Handle=handle;Bridge=bridge;Controller=new HtmlDocumentController(new HtmlPbMetrics(bridge),new HtmlLimits{MaxHitRegions=32});var port=bridge as HtmlPbBridge;painter=new HtmlPbPainter(handle,bridge,modelPose,units,port!=null&&port.ScreenId!=null);}
        internal HtmlPbDocument(long handle,HtmlPbBridge bridge,HtmlPbNativePainter painter)
        {
            Handle=handle;Bridge=bridge;nativePainter=painter;
            Controller=new HtmlDocumentController(new HtmlPbNativeMetrics(painter),new HtmlLimits{MaxHitRegions=32});
            nativeDocument=new HtmlFrontendDocument(handle,"native-sprites",Controller,painter,false);
        }
        internal bool TryLoad(string html,string css,double width,double height)
        {
            if(nativeDocument!=null){bool loaded=nativeDocument.Load(html,css,width,height);LastError=nativeDocument.LastError;return loaded;}
            if(!Controller.TryLoad(html,css,width,height)){LastError=Controller.LastError;return false;}forceControls=true;pending=true;nextAttempt=0;LastError=null;return true;
        }
        internal bool SetData(string key,string text)
        {
            if(string.IsNullOrEmpty(key)||key.Length>128)throw new ArgumentException("PB HTML binding keys require 1..128 characters.");
            if(keys.Count>=128&&!keys.Contains(key))throw new ArgumentException("PB HTML binding key budget reached.");
            if(nativeDocument!=null)return nativeDocument.SetData(key,text);
            painter.WriteBinding(key,text);
            bool changed=Controller.SetData(key,text);if(changed)keys.Add(key);else if(Controller.LastError!=null)LastError=Controller.LastError;return changed;
        }
        internal bool SetText(string node,string text){if(nativeDocument!=null)return nativeDocument.SetText(node,text);bool changed=Controller.SetText(node,text);if(!changed&&Controller.LastError!=null)LastError=Controller.LastError;return changed;}
        internal bool Resize(double width,double height){if(nativeDocument!=null)return nativeDocument.Resize(width,height);bool changed=Controller.Resize(width,height);if(!changed&&Controller.LastError!=null)LastError=Controller.LastError;return changed;}
        internal void Update(int tick)
        {
            if(disposed)return;
            if(!Ready){LastError="PB HTML caller/target/source or core endpoint is inactive.";return;}
            if(nativeDocument!=null)
            {
                var size=nativePainter.Surface.SurfaceSize;
                if(Controller.Frame!=null&&(Controller.Frame.Width!=size.X||Controller.Frame.Height!=size.Y))nativeDocument.Resize(size.X,size.Y);
                if(nativePainter.NeedsRefresh&&!nativeDocument.PendingPaint){nativeDocument.PendingPaint=true;nativeDocument.NextAttempt=0;}
                nativeDocument.SetInputEnabled(NativeInputReady());nativeDocument.Update(tick,true);LastError=nativeDocument.LastError;return;
            }
            try
            {
                if(!forceControls)painter.SyncRanges(Controller);
                if(Controller.IsDirty)
                {if(Controller.Update()){pending=true;nextAttempt=0;}else LastError=Controller.LastError;}
                if(!Pending||tick<nextAttempt)return;
                if(painter.Paint(Controller.Frame,Controller.Document,forceControls,sources.Values)){pending=false;forceControls=false;LastError=Controller.LastError;}
                else{LastError=painter.LastError;nextAttempt=tick+60;}
            }
            catch(Exception error){LastError=error.Message;nextAttempt=tick+60;}
        }
        bool NativeInputReady(){var frame=PublishedFrame;if(!Ready||frame==null)return false;var size=nativePainter.Surface.SurfaceSize;return Math.Abs(frame.Width-size.X)<=.001&&Math.Abs(frame.Height-size.Y)<=.001;}
        internal void Pointer(double x,double y,bool held){if(nativeDocument==null)throw new ArgumentException("Core SVG uses its existing authorized client pointer controls; supplied PB pointer is native-sprites only.");nativeDocument.SetInputEnabled(NativeInputReady());nativeDocument.Pointer(x,y,held);}
        internal void CancelPointer(){if(nativeDocument==null)throw new ArgumentException("PB pointer cancel is native-sprites only.");nativeDocument.CancelPointer();}
        internal MyTuple<string,string,string,MyTuple<double,long,long>>[] PollEvents()
        {
            if(nativeDocument==null)return painter.Poll();
            nativeDocument.SetInputEnabled(NativeInputReady());var raw=nativeDocument.PollEvents();var output=new MyTuple<string,string,string,MyTuple<double,long,long>>[raw.Length];
            for(int i=0;i<raw.Length;i++)output[i]=new MyTuple<string,string,string,MyTuple<double,long,long>>(raw[i].Item1,raw[i].Item2,raw[i].Item3,new MyTuple<double,long,long>(raw[i].Item4.Item1,raw[i].Item4.Item2,0));return output;
        }
        internal MyTuple<string,string,string,bool> Backend()
        {return new MyTuple<string,string,string,bool>(TargetKind,nativePainter==null?"core-svg":nativePainter.Relay?"sprites-relay":"sprites-lcd",Controller.Frame==null?"":Controller.Frame.FontProfile,nativePainter!=null&&nativePainter.Relay);}
        internal MyTuple<string,long,long,MyTuple<bool,bool,string>,long> Status()
        {return new MyTuple<string,long,long,MyTuple<bool,bool,string>,long>(TargetKind,Controller.Revision,PublishedFrame==null?0:PublishedFrame.Revision,new MyTuple<bool,bool,string>(Ready,Pending,nativeDocument==null?LastError??"":nativeDocument.LastError??""),Controller.LayoutBuildCount);}
        internal bool AttachSource(string node,string provider,string source,MyTuple<string,object[]>[] settings,int tick)
        {
            if(Controller.Document==null||string.IsNullOrEmpty(node)||!Controller.Document.ById.ContainsKey(node))throw new ArgumentException("Source attachment requires an existing explicit HTML node ID.");
            var port=Bridge as HtmlPbBridge;
            if(port==null||port.ScreenId==null)throw new ArgumentException("Unsupported: source attachments require bind-screen or bind-sprites-screen on an owned projected screen. A physical LCD renderer cannot embed engine camera textures.");
            var options=HtmlPbSourceOptions.Copy(settings);
            Bridge.Draw("screen-slot-validate",provider,source);
            HtmlPbSourceAttachment previous;sources.TryGetValue(node,out previous);
            if(previous==null&&sources.Count>=16)throw new ArgumentException("HTML source attachment limit reached (16).");
            if(previous==null&&nextSource>=256)throw new ArgumentException("HTML source attachment identity budget reached; rebind the document.");
            string slot=previous==null?"h"+Handle.ToString("x",System.Globalization.CultureInfo.InvariantCulture)+"s"+(nextSource++).ToString("x2",System.Globalization.CultureInfo.InvariantCulture):previous.Slot;
            sources[node]=new HtmlPbSourceAttachment{Node=node,Provider=provider,Source=source,Slot=slot,Settings=options};
            if(PaintSources(tick))return true;
            if(previous==null)sources.Remove(node);else sources[node]=previous;
            return false;
        }
        internal bool DetachSource(string node,int tick)
        {
            HtmlPbSourceAttachment previous;if(!sources.TryGetValue(node,out previous))return false;
            sources.Remove(node);if(PaintSources(tick))return true;sources[node]=previous;return false;
        }
        bool PaintSources(int tick)
        {
            if(nativeDocument!=null)
            {
                nativePainter.Sources=sources.Values;nativeDocument.PendingPaint=true;nativeDocument.NextAttempt=0;nativeDocument.Update(tick,true);LastError=nativeDocument.LastError;return !nativeDocument.PendingPaint;
            }
            if(Controller.IsDirty){if(!Controller.Update()){LastError=Controller.LastError;return false;}pending=true;}
            bool painted=painter.Paint(Controller.Frame,Controller.Document,forceControls,sources.Values);LastError=painted?Controller.LastError:painter.LastError;
            if(painted){pending=false;forceControls=false;}return painted;
        }
        internal MyTuple<bool,string> SourceStatus(string node)
        {
            HtmlPbSourceAttachment attachment;if(!sources.TryGetValue(node,out attachment))return new MyTuple<bool,string>(false,"Detached");
            if(nativePainter!=null)return nativePainter.SourceStatus(attachment);
            var committed=painter.Accepted==null?null:painter.Accepted.Sources.Find(s=>s.Attachment.Slot==attachment.Slot);
            if(committed==null)return new MyTuple<bool,string>(false,"Hidden: source node has no visible content region.");
            return (MyTuple<bool,string>)Bridge.Draw("screen-slot-status",attachment.Slot);
        }
        internal string[] SourceCapabilities()
        {var port=Bridge as HtmlPbBridge;return port!=null&&port.ScreenId!=null?nativePainter==null?new[]{"generic-source-slots","ordered-artwork","any-projected-surface","affine-canvas-source-pose"}:new[]{"generic-source-slots","any-projected-surface","native-lcd-rgb-texture","source-overlap-with-later-artwork-unsupported"}:new[]{"Unsupported: physical LCD textures cannot embed external engine textures; bind to an owned projected screen."};}
        public void Dispose(){if(disposed)return;disposed=true;if(nativeDocument!=null)nativeDocument.Dispose();else painter.Dispose();keys.Clear();sources.Clear();pending=false;}
    }
}
