// Paste this class inside a PB script. Standard PB imports plus Sandbox.ModAPI.Interfaces are sufficient.
public sealed class HdrHtmlIngameApi
{
    Func<string,object[],object> endpoint;
    public bool IsActive{get{return endpoint!=null;}}
    public bool Activate(IMyTerminalBlock programmableBlock)
    {
        endpoint=null;if(programmableBlock==null)return false;
        var property=programmableBlock.GetProperty("HDR.Html");if(property==null)return false;
        var typed=property.As<Func<string,object[],object>>();if(typed==null)return false;
        var candidate=typed.GetValue(programmableBlock);
        if(candidate==null||(string)candidate("version",new object[0])!="HDR.HtmlPB/0.1")return false;
        endpoint=candidate;return true;
    }
    public object Call(string command,params object[] arguments)
    {if(endpoint==null)throw new InvalidOperationException("Requires mod: HDR HTML Frontend PB adapter. Activate(Me) first.");return endpoint(command,arguments);}
    public bool TryCall(string command,object[] arguments,out object result,out string reason)
    {result=null;reason="";try{result=Call(command,arguments);return true;}catch(ArgumentException error){reason=error.Message;return false;}catch(InvalidOperationException error){reason=error.Message;return false;}}
    public bool Valid(){return endpoint!=null&&(bool)Call("valid");}
    public string[] Capabilities(){return (string[])Call("capabilities");}
    public long Bind(IMyTerminalBlock target,string html,string css,double width,double height,VRageMath.MatrixD modelPose,double unitsPerPixel=.005)
    {return (long)Call("bind",target,html,css,width,height,modelPose,unitsPerPixel);}
    // Canvas pose is the document center in selected-screen canvas metres; +X right, -Y down.
    // The owned screen keeps its shape, surface mapping and other content.
    public long BindScreen(IMyTerminalBlock target,string screenId,string html,string css,double width,double height,VRageMath.MatrixD canvasPose,double unitsPerPixel=.005)
    {return (long)Call("bind-screen",target,screenId,html,css,width,height,canvasPose,unitsPerPixel);}
    // Source LCD must be manually SCRIPT/NONE. Real sprites on that LCD; world relay needs existing renderer.
    public long BindSprites(IMyTerminalBlock anchor,IMyTerminalBlock sourceLcd,string html,string css,VRageMath.MatrixD modelPose,double metresPerPixel,int surfaceIndex,bool callerOwnsSurface)
    {return (long)Call("bind-sprites",anchor,sourceLcd,html,css,modelPose,metresPerPixel,surfaceIndex,callerOwnsSurface);}
    public long BindSpritesScreen(IMyTerminalBlock anchor,string screenId,IMyTerminalBlock sourceLcd,string html,string css,VRageMath.MatrixD canvasPose,double metresPerPixel,int surfaceIndex,bool callerOwnsSurface)
    {return (long)Call("bind-sprites-screen",anchor,screenId,sourceLcd,html,css,canvasPose,metresPerPixel,surfaceIndex,callerOwnsSurface);}
    public bool AttachSource(long handle,string nodeId,string provider,string sourceId,VRage.MyTuple<string,object[]>[] settings=null)
    {return (bool)(settings==null?Call("attach-source",handle,nodeId,provider,sourceId):Call("attach-source",handle,nodeId,provider,sourceId,settings));}
    public bool DetachSource(long handle,string nodeId){return (bool)Call("detach-source",handle,nodeId);}
    public VRage.MyTuple<bool,string> SourceStatus(long handle,string nodeId)
    {return (VRage.MyTuple<bool,string>)Call("source-status",handle,nodeId);}
    public string[] SourceCapabilities(long handle){return (string[])Call("source-capabilities",handle);}
    public VRage.MyTuple<string,string,string,bool> Backend(long handle)
    {return (VRage.MyTuple<string,string,string,bool>)Call("backend",handle);}
    // Coordinates must come from a consumer-owned touch/eye/GUI provider, not unmanaged game input.
    public void Pointer(long handle,double x,double y,bool held){Call("pointer",handle,x,y,held);}
    public void CancelPointer(long handle){Call("pointer-cancel",handle);}
    public bool Replace(long handle,string html,string css,double width,double height){return (bool)Call("replace",handle,html,css,width,height);}
    public bool SetData(long handle,string key,string text){return (bool)Call("data",handle,key,text);}
    public bool SetText(long handle,string node,string text){return (bool)Call("text",handle,node,text);}
    public bool Resize(long handle,double width,double height){return (bool)Call("resize",handle,width,height);}
    public VRage.MyTuple<string,long,long,VRage.MyTuple<bool,bool,string>,long> Status(long handle)
    {return (VRage.MyTuple<string,long,long,VRage.MyTuple<bool,bool,string>,long>)Call("status",handle);}
    public VRage.MyTuple<string,string,string,VRage.MyTuple<double,long,long>>[] PollEvents(long handle)
    {return (VRage.MyTuple<string,string,string,VRage.MyTuple<double,long,long>>[])Call("poll-events",handle);}
    public void Destroy(long handle){Call("destroy",handle);}
    public void ClearOwned(){Call("clear-owned");}
}
