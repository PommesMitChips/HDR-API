using System;
using Hdr.Mods;
using Sandbox.ModAPI.Ingame;
using IMyTextSurface=Sandbox.ModAPI.Ingame.IMyTextSurface;
using VRage;
using VRageMath;

namespace Hdr.Html
{
    public sealed partial class HtmlFrontendSession
    {
        object Service(string op,object[] values)
        {
            if(!active)throw new ArgumentException("HDR HTML frontend is stopped.");
            var a=new HtmlArguments(values);
            if(op=="version"){a.End();return Protocol;}
            if(op=="capabilities"){a.End();return new[]{HtmlDocument.Profile,"client-local","hud-vector","world-plane-vector","grouped-svg","owned-native-lcd","cooperative-pointer","click-change-events","no-javascript","no-network-fetch","no-pb-html-terminal-property"};}
            if(op!="open")throw new ArgumentException("Unknown HDR HTML service command: "+op);
            RequireIdle();string id=a.Text();a.End();ValidateOwner(id);
            HtmlFrontendOwner prior;
            if(!owners.TryGetValue(id,out prior)&&owners.Count>=MaxOwners)throw new ArgumentException("HTML owner limit reached (8).");
            busy=true;
            try
            {
                var owner=new HtmlFrontendOwner{Id=id,Generation=++generation};
                try{owner.Core=new HdrModApi("hdrhtml:"+id);}catch{owner.Dispose();throw;}
                if(owner.Core.Ready)owner.CoreGeneration=owner.Core.ConnectionGeneration;
                if(prior!=null){prior.Dispose();RetireNativeLeases(prior);}owners[id]=owner;
                return new Func<string,object[],object>((command,args)=>Command(owner,command,args));
            }
            finally{busy=false;}
        }
        object Command(HtmlFrontendOwner owner,string op,object[] values)
        {
            if(op=="valid"){var valid=new HtmlArguments(values);valid.End();return Current(owner);}
            RequireIdle();busy=true;
            try{return ExecuteCommand(owner,op,values);}finally{busy=false;}
        }
        object ExecuteCommand(HtmlFrontendOwner owner,string op,object[] values)
        {
            var a=new HtmlArguments(values);
            if(op=="valid"){a.End();return Current(owner);}
            if(!Current(owner))throw new ArgumentException("HDR HTML owner endpoint has been revoked.");
            if(op=="release"){a.End();owner.Dispose();RetireNativeLeases(owner);owners.Remove(owner.Id);return true;}
            if(op=="clear-owned"){a.End();foreach(var document in owner.Documents.Values)document.Dispose();owner.Documents.Clear();RetireNativeLeases(owner);return true;}
            if(op=="create-hud"||op=="create-world"||op=="create-lcd")return CreateDocument(owner,op,a);
            long handle=a.Long();HtmlFrontendDocument doc;
            if(!owner.Documents.TryGetValue(handle,out doc))throw new ArgumentException("Document does not belong to this HTML owner.");
            if(op=="destroy"){a.End();doc.Dispose();owner.Documents.Remove(handle);RetireNativeLeases(owner,handle);return true;}
            if(op=="status"){a.End();return doc.Status(owner.Core.Ready);}
            if(op=="poll-events"){a.End();doc.SetInputEnabled(InputCurrent(owner,doc));return doc.PollEvents();}
            if(op=="pointer"){double x=a.Number(),y=a.Number();bool pressed=a.Flag();a.End();doc.SetInputEnabled(InputCurrent(owner,doc));doc.Pointer(x,y,pressed);return true;}
            if(op=="pointer-cancel"){a.End();doc.CancelPointer();return true;}
            if(op=="replace"){string html=a.Text(),css=a.Text();double w=a.Number(),h=a.Number();a.End();return doc.Load(html,css,w,h);}
            if(op=="data"){string key=a.Text(),value=a.Text();a.End();return doc.SetData(key,value);}
            if(op=="text")
            {
                string node=a.Text(),text=a.Text();a.End();return doc.SetText(node,text);
            }
            if(op=="resize"){double width=a.Number(),height=a.Number();a.End();return doc.Resize(width,height);}
            throw new ArgumentException("Unknown HDR HTML document command: "+op);
        }
        object CreateDocument(HtmlFrontendOwner owner,string op,HtmlArguments a)
        {
            if(owner.Documents.Count>=MaxDocuments)throw new ArgumentException("HTML documents per owner limit reached (4).");
            string html=a.Text(),css=a.Text(),backend;double width,height,scale=1;int order=0;
            bool hdr=op!="create-lcd",hud=op=="create-hud";MatrixD pose=MatrixD.Identity;
            IMyTextSurface surface=null;
            if(!hdr)
            {
                surface=a.Surface();bool owns=a.Flag();a.End();
                if(!owns)throw new ArgumentException("Native LCD requires the consumer to explicitly own its supplied surface.");
                if(HtmlPbNativeSurfaceClaims.IsClaimed(surface))throw new ArgumentException("Native LCD surface already belongs to another frontend document.");
                foreach(var lease in nativeLeases)if(ReferenceEquals(lease.Surface,surface))throw new ArgumentException("This native LCD surface already belongs to another live frontend document; release it first.");
                if(surface.ContentType!=VRage.Game.GUI.TextPanel.ContentType.SCRIPT||!string.IsNullOrEmpty(surface.Script))throw new ArgumentException("Set the owned surface to SCRIPT with no selected text-surface script before creating HTML; HDR HTML will not change its content mode.");
                width=surface.SurfaceSize.X;height=surface.SurfaceSize.Y;backend="native-lcd";
            }
            else
            {
                width=a.Number();height=a.Number();
                if(!hud){pose=a.Pose();scale=a.Number();ValidatePose(pose);}
                backend=a.Text();if(a.Has)order=a.Integer();a.End();
                if(backend!="vector"&&backend!="svg")throw new ArgumentException("HTML backend requires vector or svg; raster is not implemented in this profile.");
                if(!owner.Core.Ready)throw new ArgumentException("Requires mod: HDR API with HTML text metrics and geometry queries.");
                try
                {
                    object enabled=owner.Core.Call("rendering-enabled"),usage=owner.Core.Call("geometry-usage");
                    if(!(enabled is bool)||!(usage is MyTuple<int,int>))throw new ArgumentException("Unsupported core query schema.");
                }
                catch(Exception error){throw new ArgumentException("Requires mod: HDR API with HTML frontend query support. "+error.Message);}
            }
            IHtmlTextMetrics metrics=hdr?(IHtmlTextMetrics)new HtmlHdrTextMetrics(owner.Core):new HtmlLcdTextMetrics(surface);
            var controller=new HtmlDocumentController(metrics,new HtmlLimits());
            if(!controller.TryLoad(html,css,width,height))throw new ArgumentException(controller.LastError);
            object nativeToken=hdr?null:new object();
            if(!hdr&&!HtmlPbNativeSurfaceClaims.TryClaim(surface,nativeToken))throw new ArgumentException("The source LCD surface was claimed during HTML admission.");
            IHtmlPainter painter;
            try{painter=hdr?(backend=="vector"?(IHtmlPainter)new HtmlVectorPainter(owner.Core,hud,pose,scale,order):new HtmlSvgPainter(owner.Core,hud,pose,scale,order)):new HtmlNativeLcdPainter(surface,true,null,()=>HtmlPbNativeSurfaceClaims.Owns(surface,nativeToken));}
            catch{if(!hdr)HtmlPbNativeSurfaceClaims.Release(surface,nativeToken);throw;}
            var document=new HtmlFrontendDocument(++nextDocument,backend,controller,painter,hdr){Markup=html,Stylesheet=css,PendingPaint=true};
            // Initial paint is part of admission; failed documents never leave invisible handles or input behind.
            document.Update(tick,!hdr||owner.Core.Ready);
            if(document.VisibleFrame==null){string reason=document.LastError;document.Dispose();if(!hdr)HtmlPbNativeSurfaceClaims.Release(surface,nativeToken);throw new ArgumentException(reason??"Initial HTML paint failed.");}
            owner.Documents.Add(document.Handle,document);
            if(!hdr)nativeLeases.Add(new HtmlNativeLease{Surface=surface,Owner=owner,Document=document.Handle,Token=nativeToken});
            return document.Handle;
        }
        static void ValidateOwner(string id)
        {
            if(string.IsNullOrEmpty(id)||id.Length>48)throw new ArgumentException("HTML owner ID requires 1..48 characters.");
            foreach(char c in id)if(!(char.IsLetterOrDigit(c)||c=='-'||c=='_'||c=='.'))throw new ArgumentException("HTML owner ID requires letters, digits, period, underscore or hyphen.");
        }
        static void ValidatePose(MatrixD p)
        {
            double[] values={p.M11,p.M12,p.M13,p.M14,p.M21,p.M22,p.M23,p.M24,p.M31,p.M32,p.M33,p.M34,p.M41,p.M42,p.M43,p.M44};
            foreach(double n in values)if(!HtmlFrontendDocument.Finite(n)||Math.Abs(n)>1e12)throw new ArgumentException("World pose must be finite and bounded.");
            double determinant=p.Determinant();if(!HtmlFrontendDocument.Finite(determinant)||Math.Abs(determinant)<1e-12||Math.Abs(p.M14)>1e-9||Math.Abs(p.M24)>1e-9||Math.Abs(p.M34)>1e-9||Math.Abs(p.M44-1)>1e-9)throw new ArgumentException("World pose must be a nondegenerate affine transform.");
        }
        internal sealed class HtmlArguments
        {
            readonly object[] args;int index;
            internal HtmlArguments(object[] values){args=values??new object[0];if(args.Length>12)throw new ArgumentException("HTML command has too many arguments.");}
            internal bool Has{get{return index<args.Length;}}
            object Next(){if(!Has)throw new ArgumentException("Missing HTML command argument.");return args[index++];}
            internal string Text(){var result=Next() as string;if(result==null)throw new ArgumentException("HTML argument requires a string.");return result;}
            internal long Long(){var value=Next();if(!(value is long))throw new ArgumentException("Document handle requires Int64.");return (long)value;}
            internal int Integer(){var value=Next();if(!(value is int))throw new ArgumentException("Order requires Int32.");return (int)value;}
            internal bool Flag(){var value=Next();if(!(value is bool))throw new ArgumentException("HTML argument requires Boolean.");return (bool)value;}
            internal double Number(){var value=Next();double result;if(value is double)result=(double)value;else if(value is int)result=(int)value;else if(value is float)result=(float)value;else throw new ArgumentException("HTML argument requires a finite number.");if(!HtmlFrontendDocument.Finite(result))throw new ArgumentException("HTML argument requires a finite number.");return result;}
            internal MatrixD Pose(){var value=Next();if(!(value is MatrixD))throw new ArgumentException("World pose requires MatrixD.");return (MatrixD)value;}
            internal IMyTextSurface Surface(){var value=Next() as IMyTextSurface;if(value==null)throw new ArgumentException("Native LCD requires an explicit IMyTextSurface.");return value;}
            internal void End(){if(Has)throw new ArgumentException("Unexpected HTML command argument.");}
        }
    }
}
