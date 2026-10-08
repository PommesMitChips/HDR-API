using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        const string UiPropertyId="HDR.UI";
        Dictionary<string,UiDisplay> _uiDisplays=new Dictionary<string,UiDisplay>();
        long _uiRevision;
        void RegisterUiApi()
        {
            var property=MyAPIGateway.TerminalControls.CreateProperty<Func<string,object[],object>,IMyProgrammableBlock>(UiPropertyId);
            property.Getter=block=>UiEndpoint(block as IMyProgrammableBlock);MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(property);
        }
        Func<string,object[],object> UiEndpoint(IMyProgrammableBlock caller)
        {
            DrawEndpoint(caller);var context=_drawContexts[caller.EntityId];return (command,args)=>UiCommand(context,command,args);
        }
        static string UiKey(long caller,long target){return Key(caller,target.ToString(CultureInfo.InvariantCulture));}
        UiDisplay GetUiDisplay(long caller,long target)
        {UiDisplay d;return _uiDisplays.TryGetValue(UiKey(caller,target),out d)?d:null;}
        UiWidget GetUiWidget(long caller,long target,string id,bool requireVisible=true)
        {var d=GetUiDisplay(caller,target);if(d==null)return null;foreach(var w in d.Widgets)if(w.Id==id&&(!requireVisible||HasVisibleUiWidget(d,w)))return w;return null;}
        bool HasVisibleUiWidget(UiDisplay d,UiWidget w,bool allowHiddenBundle=false)
        {
            if(d==null||w==null||!w.Visible)return false;bool found=false,visible=false;foreach(var b in d.Bundles)if(b.Id==w.Bundle){found=true;visible=b.Visible;break;}if(!found||!visible&&!allowHiddenBundle)return false;
            Scene scene;if(!_scenes.TryGetValue(d.TargetId,out scene))return false;Layer layer;if(scene.Layers.TryGetValue(Key(d.CallerId,"ui-"+w.Bundle),out layer)&&(layer.Opacity<=0||!layer.Visible&&!allowHiddenBundle))return false;
            Item item;if(scene.Items.TryGetValue(Key(d.CallerId,UiObjectId(w.Bundle)),out item)&&(!item.Visible||item.Opacity<=0))return false;return true;
        }
        List<UiDisplay> CaptureUiDisplays()
        {var result=new List<UiDisplay>();foreach(var d in _uiDisplays.Values)result.Add(UiRules.Copy(d));return result;}
        static Dictionary<string,UiDisplay> ValidateUiDisplays(List<UiDisplay> displays,Dictionary<long,Scene> scenes)
        {
            if(displays==null)displays=new List<UiDisplay>();if(displays.Count>UiRules.MaxDisplays)throw new ArgumentException("UI display budget exceeded.");
            var checkedDisplays=new Dictionary<string,UiDisplay>();int count=0,valueCount=0;foreach(var d in displays){UiRules.Display(d);if(scenes!=null&&!scenes.ContainsKey(d.TargetId))throw new ArgumentException("UI target scene is missing.");count+=d.Widgets.Count;valueCount+=d.Values.Count;if(valueCount>UiValueRules.MaxTotalValues)throw new ArgumentException("UI value budget exceeded.");if(count>UiRules.MaxTotalWidgets)throw new ArgumentException("UI widget budget exceeded.");var key=UiKey(d.CallerId,d.TargetId);if(checkedDisplays.ContainsKey(key))throw new ArgumentException("Duplicate UI display.");checkedDisplays.Add(key,UiRules.Copy(d));}if(displays.Count>0){if(MyAPIGateway.Utilities==null)throw new ArgumentException("UI metadata validation requires the serialization service.");if(MyAPIGateway.Utilities.SerializeToBinary(displays).Length>UiRules.ReplicationReserve)throw new ArgumentException("UI interaction metadata exceeds the shared network reserve.");}return checkedDisplays;
        }
        void ApplyValidatedUiDisplays(Dictionary<string,UiDisplay> validated){_uiDisplays=validated;}
        void ApplyUiDisplays(List<UiDisplay> displays){ApplyValidatedUiDisplays(ValidateUiDisplays(displays,_scenes));}
        void ClearUiState(){ClearUiValueRuntime();_uiDisplays.Clear();_uiRevision=0;}
        void ClearUiDisplay(long caller,long target)
        {ClearUiValueDisplay(caller,target);if(_uiDisplays.Remove(UiKey(caller,target)))_dirty=true;}
        void TickUiState()
        {
            if(!MyAPIGateway.Multiplayer.IsServer||_ticks%60!=0)return;var removed=new List<string>();
            foreach(var p in _uiDisplays){var caller=MyAPIGateway.Entities.GetEntityById(p.Value.CallerId) as IMyProgrammableBlock;var target=MyAPIGateway.Entities.GetEntityById(p.Value.TargetId) as IMyTerminalBlock;if(caller==null||target==null||caller.Closed||target.Closed||!_scenes.ContainsKey(p.Value.TargetId)){removed.Add(p.Key);continue;}try{AuthorizeAccess(caller,target);}catch(ArgumentException){removed.Add(p.Key);}}
            foreach(string key in removed)_uiDisplays.Remove(key);if(removed.Count>0)_dirty=true;
        }
        static string UiObjectId(string bundle){return "ui_"+bundle;}
        static string UiCaptionId(string widget){return "ui_b_"+widget;}
        object UiCommand(DrawContext context,string command,object[] values)
        {
            if(!MyAPIGateway.Multiplayer.IsServer||context.Caller==null||context.Caller.Closed||!ReferenceEquals(MyAPIGateway.Entities.GetEntityById(context.Caller.EntityId),context.Caller))throw new ArgumentException("UI commands require a live server PB.");
            if(string.IsNullOrWhiteSpace(command)||command.Length>64)throw new ArgumentException("UI command requires 1–64 characters.");
            string op=command.ToLowerInvariant();var a=new DrawArgs(values);
            if(op=="version"){a.End();return "HDR.UI/1";}
            if(op=="capabilities"){a.End();return "native-use;pb;toggle;menu;focus=look-and-use;values=1;constraints=line,path,rotation;mouse=client-provider;viewer=persistent-bundle;pointer=HDR.Pointer/1";}
            if(op=="target")return DrawCommand(context,command,values);
            if(context.Target==null)throw new ArgumentException("Select a UI target first.");
            var target=op=="get-value"?AuthorizeAccess(context.Caller,context.Target):Authorize(context.Caller,context.Target);object valueResult;if(TryUiValueCommand(context,op,a,out valueResult))return valueResult;long caller=context.Caller.EntityId;var old=GetUiDisplay(caller,target.EntityId);var next=old==null?new UiDisplay{CallerId=caller,TargetId=target.EntityId}:UiRules.Copy(old);
            string changedBundle=null,removedBundle=null;bool changed=false;
            switch(op)
            {
                case "bundle":case "menu":
                {
                    string id=UiRules.Id(a.Text());bool visible=a.Flag(true);a.End();var b=next.Bundles.Find(v=>v.Id==id);if(b==null){if(next.Bundles.Count>=UiRules.MaxBundles)throw new ArgumentException("UI bundle budget reached.");b=new UiBundle{Id=id};next.Bundles.Add(b);}b.Visible=visible;changedBundle=id;changed=true;break;
                }
                case "button":case "hit":
                {
                    var w=new UiWidget{Id=UiRules.Id(a.Text()),Bundle=UiRules.Id(a.Text()),X=a.Number(),Y=a.Number(),Width=a.Number(),Height=a.Number()};w.Label=op=="button"?a.Text():null;w.ActionKind=a.Text();w.Argument=a.Text("");a.End();UiRules.Widget(w);
                    if(!next.Bundles.Exists(v=>v.Id==w.Bundle))throw new ArgumentException("Declare the UI bundle first.");var existing=next.Widgets.Find(v=>v.Id==w.Id);if(existing!=null&&existing.Bundle!=w.Bundle)throw new ArgumentException("Remove a widget before moving it to another bundle.");if(existing!=null)next.Widgets.Remove(existing);next.Widgets.Add(w);changedBundle=w.Bundle;changed=true;break;
                }
                case "bind":case "action":
                {
                    string id=UiRules.Id(a.Text()),kind=a.Text(),argument=a.Text("");a.End();UiRules.Action(kind,argument);var w=next.Widgets.Find(v=>v.Id==id);if(w==null)throw new ArgumentException("UI widget does not exist.");w.ActionKind=kind;w.Argument=argument;changed=true;break;
                }
                case "visible":
                {
                    string id=UiRules.Id(a.Text());bool visible=a.Flag();a.End();var b=next.Bundles.Find(v=>v.Id==id);var w=next.Widgets.Find(v=>v.Id==id);if(b!=null){b.Visible=visible;changedBundle=id;}else if(w!=null){w.Visible=visible;changedBundle=w.Bundle;}else throw new ArgumentException("UI bundle or widget does not exist.");changed=true;break;
                }
                case "remove":
                {
                    string id=UiRules.Id(a.Text());a.End();var b=next.Bundles.Find(v=>v.Id==id);if(b!=null){foreach(var w in next.Widgets)if(w.Bundle!=id&&(w.ActionKind=="menu"||w.ActionKind=="focus")&&w.Argument==id)throw new ArgumentException("Another UI widget references this bundle.");next.Bundles.Remove(b);next.Widgets.RemoveAll(w=>w.Bundle==id);removedBundle=id;}else{var w=next.Widgets.Find(v=>v.Id==id);if(w==null)throw new ArgumentException("UI widget does not exist.");next.Widgets.Remove(w);changedBundle=w.Bundle;}changed=true;break;
                }
                case "clear":
                {
                    a.End();foreach(var b in next.Bundles)RemoveUiRendering(context,b.Id);ClearUiValueDisplay(caller,target.EntityId);_uiDisplays.Remove(UiKey(caller,target.EntityId));_dirty=true;return true;
                }
                default:throw new ArgumentException("Unknown UI command: "+command);
            }
            if(changed)
            {
                if(_uiRevision==long.MaxValue)throw new ArgumentException("UI revision budget exhausted.");next.Revision=_uiRevision+1;UiRules.Display(next);int total=next.Widgets.Count;foreach(var d in _uiDisplays.Values)if(d.CallerId!=caller||d.TargetId!=target.EntityId)total+=d.Widgets.Count;
                if(total>UiRules.MaxTotalWidgets||old==null&&_uiDisplays.Count>=UiRules.MaxDisplays)throw new ArgumentException("Global UI budget reached.");
                UiAdmitMetadata(next);
                if(changedBundle!=null)RenderUiBundle(context,next,changedBundle);if(removedBundle!=null)RemoveUiRendering(context,removedBundle);
                _uiRevision=next.Revision;_uiDisplays[UiKey(caller,target.EntityId)]=next;ReconcileUiValueDefinitions(old,next);_dirty=true;
            }
            return true;
        }
        void RemoveUiRendering(DrawContext c,string bundle)
        {
            Scene s;if(!_scenes.TryGetValue(c.Target.EntityId,out s))return;if(s.Items.ContainsKey(Key(c.Caller.EntityId,UiObjectId(bundle))))DrawCheck(Remove(c.Caller,c.Target,UiObjectId(bundle)));
            var old=GetUiDisplay(c.Caller.EntityId,c.Target.EntityId);if(old!=null)foreach(var widget in old.Widgets)if(widget.Bundle==bundle)s.Labels.Remove(Key(c.Caller.EntityId,UiCaptionId(widget.Id)));
            string layer="ui-"+bundle;bool used=false;foreach(var item in s.Items.Values)if(item.CallerId==c.Caller.EntityId&&item.Layer==layer){used=true;break;}if(!used)foreach(var label in s.Labels.Values)if(label.CallerId==c.Caller.EntityId&&label.Layer==layer){used=true;break;}if(!used)s.Layers.Remove(Key(c.Caller.EntityId,layer));
        }
        void RenderUiBundle(DrawContext c,UiDisplay d,string bundle)
        {
            var b=d.Bundles.Find(v=>v.Id==bundle);if(b==null)return;var scene=GetScene(c.Target.EntityId);if(!scene.Layers.ContainsKey(Key(c.Caller.EntityId,"ui-"+bundle))&&scene.Layers.Count>=32)throw new ArgumentException("UI layer budget reached.");
            var captionKeys=new HashSet<string>();var prior=GetUiDisplay(c.Caller.EntityId,c.Target.EntityId);if(prior!=null)foreach(var w in prior.Widgets)if(w.Bundle==bundle)captionKeys.Add(Key(c.Caller.EntityId,UiCaptionId(w.Id)));
            int labels=0,characters=0;foreach(var pair in scene.Labels)if(!captionKeys.Contains(pair.Key)){labels++;characters+=pair.Value.Text.Length;}
            foreach(var w in d.Widgets)if(w.Bundle==bundle&&w.Visible&&!string.IsNullOrEmpty(w.Label)){string key=Key(c.Caller.EntityId,UiCaptionId(w.Id));if(scene.Labels.ContainsKey(key)&&!captionKeys.Contains(key))throw new ArgumentException("UI caption id conflicts with existing artwork.");labels++;characters+=w.Label.Length;UiCaptionHeight(w);}
            if(labels>16||characters>256)throw new ArgumentException("UI captions share the display budget of 16 labels and 256 characters; use hit areas for larger custom menus.");
            long savedLcdCaller=scene.LcdCallerId,savedLcdSource=scene.LcdSourceId;var savedItems=new Dictionary<string,Item>(scene.Items);var savedLabels=new Dictionary<string,Label>(scene.Labels);var savedLayers=new Dictionary<string,Layer>();foreach(var pair in scene.Layers){var l=pair.Value;savedLayers.Add(pair.Key,new Layer{CallerId=l.CallerId,Name=l.Name,Visible=l.Visible,Opacity=l.Opacity,Order=l.Order});}
            var svg=new StringBuilder("<svg viewBox='-2500 -2500 5000 5000' xmlns='http://www.w3.org/2000/svg'>");bool any=false;
            foreach(var w in d.Widgets)if(w.Bundle==bundle&&w.Visible&&w.Label!=null)
            {
                any=true;double x=(w.X-w.Width/2)*100,y=(-w.Y-w.Height/2)*100;
                svg.Append("<rect x='").Append(UiNumber(x)).Append("' y='").Append(UiNumber(y)).Append("' width='").Append(UiNumber(w.Width*100)).Append("' height='").Append(UiNumber(w.Height*100)).Append("' fill='#12303c' stroke='#44ddee' stroke-width='0.8'/>");
            }
            svg.Append("</svg>");
            try
            {
                foreach(string key in captionKeys)scene.Labels.Remove(key);
                if(any){DrawCheck(PutSvg(c.Caller,c.Target,UiObjectId(bundle),svg.ToString(),new MyTuple<MatrixD,int>(MatrixD.CreateScale(0.01),4)));DrawCheck(SetObjectLayer(c.Caller,c.Target,UiObjectId(bundle),"ui-"+bundle));}else RemoveUiRendering(c,bundle);
                foreach(var w in d.Widgets)if(w.Bundle==bundle&&w.Visible&&!string.IsNullOrEmpty(w.Label)){string id=UiCaptionId(w.Id);DrawCheck(PutLabel(c.Caller,c.Target,id,new Vector3D(w.X,w.Y,0),w.Label,Vector4.One,UiCaptionHeight(w)));DrawCheck(SetObjectLayer(c.Caller,c.Target,id,"ui-"+bundle));}
                DrawCheck(SetLayerVisible(c.Caller,c.Target,"ui-"+bundle,b.Visible));
            }
            catch
            {
                scene.Items.Clear();foreach(var pair in savedItems)scene.Items.Add(pair.Key,pair.Value);scene.Labels.Clear();foreach(var pair in savedLabels)scene.Labels.Add(pair.Key,pair.Value);scene.Layers.Clear();foreach(var pair in savedLayers)scene.Layers.Add(pair.Key,pair.Value);scene.LcdCallerId=savedLcdCaller;scene.LcdSourceId=savedLcdSource;throw;
            }
        }
        static float UiCaptionHeight(UiWidget w)
        {double value=Math.Min(0.5,Math.Min(w.Height*0.28,w.Width/(Math.Max(1,w.Label.Length)*1.5)));if(value<0.01)throw new ArgumentException("UI caption is too small for readable text.");return (float)value;}
        static string UiNumber(double value){return value.ToString("0.######",CultureInfo.InvariantCulture);}
        static string UiEscape(string value){return value.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;").Replace("'","&apos;");}
    }
}
