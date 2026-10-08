using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace Hdr.Mods
{
    /// <summary>Copy this file into a consumer Workshop mod. All calls run on the client simulation thread.</summary>
    public sealed class HdrModApi : IDisposable
    {
        public const long DiscoveryChannel=481770130, RequestChannel=481770131;
        readonly string ownerId;
        Func<string,object[],object> service, endpoint;
        bool disposed;
        Dictionary<MyTuple<long,string>,ValueBinding> valueBindings;
        bool updatingBindings;
        public long ConnectionGeneration { get; private set; }
        /// <summary>The most recent local binding failure. Only that binding is detached; other bindings continue.</summary>
        public string LastBindingError { get; private set; }
        public HdrModApi(string uniqueOwnerId)
        {
            ownerId=uniqueOwnerId;
            MyAPIGateway.Utilities.RegisterMessageHandler(DiscoveryChannel,Receive);
            Request();
        }
        public bool Ready
        {
            get
            {
                if(disposed||endpoint==null)return false;
                var prior=endpoint;long generation=ConnectionGeneration;bool valid=EndpointValid();
                // A validity probe is an external call too. Its result belongs only to the queried generation.
                if(disposed||!ReferenceEquals(endpoint,prior)||ConnectionGeneration!=generation)return false;
                if(valid)return true;Invalidate();return false;
            }
        }
        bool EndpointValid(){if(endpoint==null)return false;try{var valid=endpoint("valid",new object[0]);return valid is bool&&(bool)valid;}catch{return false;}}
        void Invalidate(){endpoint=null;RevokeBindings();}
        void Receive(object value)
        {
            var candidate=value as Func<string,object[],object>;
            if(disposed||candidate==null||ReferenceEquals(candidate,service)&&Ready)return;
            try
            {
                if((string)candidate("version",new object[0])!="HDR.ModClient/1")return;
                var acquired=candidate("open",new object[]{ownerId}) as Func<string,object[],object>;
                if(acquired==null)return;ReleaseEndpoint();service=candidate;endpoint=acquired;ConnectionGeneration++;
            }
            catch{ /* Absent/stopped/mismatched renderer does not stop the consumer mod. */ }
        }
        public void Request()
        {
            if(disposed)return;
            try{MyAPIGateway.Utilities.SendModMessage(RequestChannel,new Action<Func<string,object[],object>>(value=>Receive(value)));}catch{}
        }
        public object Call(string command,params object[] args)
        {
            if(!Ready)throw new InvalidOperationException("Requires mod: HDR API (client API unavailable).");
            var prior=endpoint;long generation=ConnectionGeneration;
            try
            {
                var result=prior(command,args);
                // Rejected lifecycle calls retain their context/value and local adapter. Successful calls revoke
                // before returning to a callback, but may never revoke bindings admitted by a reentrant reconnect.
                if(ReferenceEquals(endpoint,prior)&&ConnectionGeneration==generation)
                {
                    if(command=="release")Invalidate();
                    else if((command=="clear"||command=="destroy")&&args!=null&&args.Length>0&&args[0] is long)RevokeBindings((long)args[0]);
                    else if(command=="remove-value"&&args!=null&&args.Length>1&&args[0] is long&&args[1] is string)UnbindValue((long)args[0],(string)args[1]);
                }
                return result;
            }
            catch
            {
                if(ReferenceEquals(endpoint,prior)&&ConnectionGeneration==generation)
                {
                    bool valid=EndpointValid();
                    if(!valid&&ReferenceEquals(endpoint,prior)&&ConnectionGeneration==generation)Invalidate();
                }
                throw;
            }
        }
        public bool TryCall(string command,out object result,out string reason,params object[] args)
        {
            result=null;reason=null;if(!Ready){reason="Requires mod: HDR API (client API unavailable).";return false;}
            try{result=Call(command,args);return true;}catch(Exception error){reason=error.Message;return false;}
        }
        public string[] Capabilities()
        {return Ready?(string[])service("capabilities",new object[0]):new string[0];}
        public MyTuple<bool,bool,string> PluginStatus(string feature)
        {if(!Ready)return new MyTuple<bool,bool,string>(false,false,"Requires mod: HDR API (client API unavailable).");return (MyTuple<bool,bool,string>)Call("plugin-status",feature);}
        public void SharedDrawBudget(int triangles){if(!Ready)throw new InvalidOperationException("HDR client API is unavailable.");service("draw-budget",new object[]{triangles});}
        public long CreateHud(int order=0){return (long)Call("create-hud",order);}
        public long CreateWorld(MatrixD pose,int order=0){return (long)Call("create-world",order,pose);}
        public Vector2 Viewport { get { return (Vector2)Call("viewport"); } }
        /// <summary>Exact packaged Inter cap-height metrics; source Y-up, start anchor. No target/context or geometry build.</summary>
        public MyTuple<string,MyTuple<double,double,double>,MyTuple<double,double,double,double>,int,bool> MeasureText(string text,double height=1,double lineHeight=1.3)
        {return (MyTuple<string,MyTuple<double,double,double>,MyTuple<double,double,double,double>,int,bool>)Call("measure-text",text,height,lineHeight);}
        public void DrawLimit(int triangles){Call("draw-limit",triangles);}
        public void Destroy(long context){Call("destroy",context);}
        public void Clear(long context){Call("clear",context);}
        public void ContextVisible(long context,bool visible){Call("context-visible",context,visible);}
        public void ContextPose(long context,MatrixD pose){Call("context-pose",context,pose);}
        public void Mesh(long context,string id,Vector3D[] points,int[] triangles,Vector4 color,Vector2[] uv=null,string material=null)
        {Call("mesh",context,id,points,triangles,color,uv,material);}
        public void Wires(long context,string id,Vector3D[] points,Vector2I[] edges,Vector4 color,double width=1)
        {Call("wires",context,id,points,edges,color,width);}
        public void Text(long context,string id,string text,Vector3D position,double height,Vector4 color,string anchor="start")
        {Call("text",context,id,text,position,height,color,anchor);}
        public void Svg(long context,string id,string svg,MatrixD transform,int curveSegments=12)
        {Call("svg",context,id,svg,transform,curveSegments);}
        public void Rect(long context,string id,double x,double y,double width,double height,Vector4 color)
        {if(!Finite(x)||!Finite(y)||!Finite(width)||!Finite(height)||width<=0||height<=0)throw new ArgumentException("Rectangle must be finite and positive.");Mesh(context,id,Quad(x,y,width,height),new[]{0,1,2,0,2,3},color);}
        public void Image(long context,string id,string registeredMaterial,double x,double y,double width,double height,Vector4 tint)
        {if(!Finite(x)||!Finite(y)||!Finite(width)||!Finite(height)||width<=0||height<=0)throw new ArgumentException("Image rectangle must be finite and positive.");Mesh(context,id,Quad(x,y,width,height),new[]{0,1,2,0,2,3},tint,new[]{Vector2.Zero,Vector2.UnitX,Vector2.One,Vector2.UnitY},registeredMaterial);}
        static bool Finite(double n){return !double.IsNaN(n)&&!double.IsInfinity(n);}
        static Vector3D[] Quad(double x,double y,double w,double h){return new[]{new Vector3D(x,y,0),new Vector3D(x+w,y,0),new Vector3D(x+w,y+h,0),new Vector3D(x,y+h,0)};}
        public void Transform(long context,string id,MatrixD pose){Call("transform",context,id,pose);}
        public void Visible(long context,string id,bool visible){Call("visible",context,id,visible);}
        /// <summary>Set explicit retained paint order without replacing geometry or linked controls. Reapply after an artwork upsert.</summary>
        public void ItemOrder(long context,string id,int order){Call("item-order",context,id,order);}
        /// <summary>Compile bounded source once to obtain exact retained point/primitive counts. Artwork upsert compiles again.</summary>
        public MyTuple<int,int> GeometryCost(string kind,string source,double height=1,int segments=12)
        {return (MyTuple<int,int>)Call("geometry-cost",kind,source,height,segments);}
        public bool ContextValid(long context){return (bool)Call("context-valid",context);}
        /// <summary>Global local rendering switch, independent of context lifetime. Gate consumer-owned input when false.</summary>
        public bool RenderingEnabled {get{return (bool)Call("rendering-enabled");}}
        /// <summary>Total retained points/primitives across this owner's contexts; no geometry compilation or mutation.</summary>
        public MyTuple<int,int> GeometryUsage(){return (MyTuple<int,int>)Call("geometry-usage");}
        public void Remove(long context,string id){Call("remove",context,id);}
        /// <summary>Configure retained flicker, scan, volume, particles, beams, rays or budget. World distances are model units; HUD distances are pixels. No client plugin is required.</summary>
        public void Effect(long context,string id,string type,params object[] parameters)
        {
            var args=new object[3+(parameters==null?0:parameters.Length)];args[0]=context;args[1]=id;args[2]=type;
            if(parameters!=null)Array.Copy(parameters,0,args,3,parameters.Length);Call("effect",args);
        }
        /// <summary>Advanced numeric descriptor: 24 doubles and 8 integers. Arrays are cloned by HDR. Configuration calls do not advance animation.</summary>
        public void Effects(long context,string id,double[] values,int[] flags){Call("effect",context,id,"raw",new MyTuple<double[],int[]>(values,flags));}
        public void ClearEffects(long context,string id,string type=null){if(type==null)Call("effect-clear",context,id);else Call("effect-clear",context,id,type);}
        /// <summary>Start a retained fade, wipe or dissolve. Leaving transitions retain the item, hidden, until it is removed or transitioned in again.</summary>
        public void Transition(long context,string id,bool entering,string style="fade",double seconds=.5){Call("transition",context,id,entering?"in":"out",style,seconds);}
        public void Bounds(long context,string id,Vector4 rectangle){Call("bounds",context,id,rectangle);}
        public void RemoveBounds(long context,string id){Call("remove-bounds",context,id);}
        /// <summary>Define a client-local numeric value. HDR canonicalizes writes to the inclusive range and optional step.</summary>
        public void Value(long context,string id,double initial,double minimum,double maximum,double step=0)
        {Call("value",context,id,initial,minimum,maximum,step);}
        public MyTuple<double,long> GetValue(long context,string id){return (MyTuple<double,long>)Call("get-value",context,id);}
        /// <summary>A nonnegative expected revision requests compare-and-swap. A rejected write returns the current value and revision.</summary>
        public MyTuple<bool,double,long> SetValue(long context,string id,double requested,long expectedRevision=-1)
        {return (MyTuple<bool,double,long>)Call("set-value",context,id,requested,expectedRevision);}
        /// <summary>Retain a control for existing artwork, using local top-left X/Y and positive width/height.</summary>
        public void Control(long context,string controlId,string artworkId,Vector4 rectangle){Call("control",context,controlId,artworkId,rectangle);}
        public void BindControlValue(long context,string controlId,string valueId){Call("bind-value",context,controlId,valueId);}
        public void Draggable(long context,string controlId,bool enabled=true){Call("draggable",context,controlId,enabled);}
        public void ConstraintLine(long context,string controlId,Vector3D start,Vector3D end){Call("constraint",context,controlId,"line",start,end);}
        public void ConstraintPath(long context,string controlId,Vector3D[] path){Call("constraint",context,controlId,"path",path);}
        /// <summary>Rotation limits are radians and map linearly from the bound value's domain.</summary>
        public void ConstraintRotation(long context,string controlId,Vector3D pivot,Vector3D axis,double minimumAngle,double maximumAngle)
        {Call("constraint",context,controlId,"rotation",pivot,axis,minimumAngle,maximumAngle);}
        /// <summary>Caller supplied HUD pixels, or world-context local XY coordinates. Does not capture/block mouse input.</summary>
        public void Pointer(long context,double x,double y,bool pressed){Call("pointer",context,x,y,pressed);}
        /// <summary>Cooperative focus loss. The consumer still owns mouse/button capture and game-input policy.</summary>
        public void CancelPointer(long context){Call("pointer-cancel",context);}
        /// <summary>Caller supplied world ray; HDR does not read native input or capture the mouse.</summary>
        public void PointerRay(long context,Vector3D origin,Vector3D direction,bool pressed){Call("pointer-ray",context,origin,direction,pressed);}
        public MyTuple<string,string>[] PollEvents(long context){return (MyTuple<string,string>[])Call("poll-events",context);}
        public MyTuple<string,string,string,MyTuple<double,long,long>>[] PollValueEvents(long context)
        {return (MyTuple<string,string,string,MyTuple<double,long,long>>[])Call("poll-value-events",context);}

        /// <summary>
        /// Bind a value to consumer-owned callbacks. Registration invokes no callback. The first UpdateBindings treats
        /// the getter as authoritative, then delivers any canonical range/step correction through the setter.
        /// Bindings are local and are revoked on reconnect, release, destroy, clear or value removal; rebuild them for a new endpoint.
        /// </summary>
        public IDisposable BindValue(long context,string id,Func<double> getter,Action<double> setter)
        {
            if(getter==null||setter==null)throw new ArgumentNullException(getter==null?"getter":"setter");
            if(string.IsNullOrEmpty(id))throw new ArgumentException("Value ID is required.","id");
            GetValue(context,id); // Verify the retained value before admitting a callback adapter.
            if(!Ready)throw new InvalidOperationException("Requires mod: HDR API (client API unavailable).");
            var key=new MyTuple<long,string>(context,id);UnbindValue(context,id);
            if(valueBindings==null)valueBindings=new Dictionary<MyTuple<long,string>,ValueBinding>();
            var binding=new ValueBinding(this,key,endpoint,ConnectionGeneration,getter,setter);valueBindings.Add(key,binding);return binding;
        }
        public void UnbindValue(long context,string id)
        {ValueBinding binding;if(valueBindings!=null&&valueBindings.TryGetValue(new MyTuple<long,string>(context,id),out binding))Detach(binding);}

        /// <summary>
        /// Explicit client simulation-thread pump, outside HDR Draw. Never call it from a render/input worker.
        /// Changed consumer data writes against a freshly read revision and preempts a drag. Otherwise a newer HDR
        /// revision is delivered to the setter. Callbacks and local variables are neither reflected nor replicated.
        /// Nested pumps are ignored; bindings registered during a callback start on the next pump.
        /// </summary>
        public void UpdateBindings()
        {
            if(updatingBindings||!Ready||valueBindings==null||valueBindings.Count==0)return;
            updatingBindings=true;
            try
            {
                var snapshot=new List<ValueBinding>(valueBindings.Values);
                foreach(var binding in snapshot)
                {
                    if(!BindingCurrent(binding))continue;
                    try{UpdateBinding(binding);}
                    catch(Exception error){LastBindingError="Value binding '"+binding.Key.Item2+"' in context "+binding.Key.Item1+": "+error.Message;Detach(binding);}
                }
            }
            finally{updatingBindings=false;}
        }
        void UpdateBinding(ValueBinding binding)
        {
            if(!BindingCurrent(binding))return;
            // The endpoint refuses ordinary calls during HDR Draw. Validate that phase before running user code;
            // the later read is still needed because a getter may author a new HDR revision itself.
            GetValue(binding.Key.Item1,binding.Key.Item2);if(!BindingCurrent(binding))return;
            double external=binding.Getter();if(!BindingCurrent(binding))return;
            if(!Finite(external))throw new ArgumentException("Consumer getter must return a finite value.");
            var current=GetValue(binding.Key.Item1,binding.Key.Item2);if(!BindingCurrent(binding))return;
            if(!binding.Observed||binding.SourcePending||external!=binding.External)
            {
                var written=SetValue(binding.Key.Item1,binding.Key.Item2,external,current.Item2);if(!BindingCurrent(binding))return;
                if(!written.Item1)return; // Retry the source change next pump, against a newly read revision.
                if(written.Item2!=external){DeliverBinding(binding,written.Item2,written.Item3,external);return;}
                binding.External=external;binding.Revision=written.Item3;binding.Observed=true;binding.SourcePending=false;return;
            }
            if(current.Item2!=binding.Revision)DeliverBinding(binding,current.Item1,current.Item2,external);
        }
        void DeliverBinding(ValueBinding binding,double value,long revision,double precedingExternal)
        {
            if(!BindingCurrent(binding))return;binding.Setter(value);if(!BindingCurrent(binding))return;
            double observed=binding.Getter();if(!BindingCurrent(binding))return;
            if(!Finite(observed))throw new ArgumentException("Consumer getter must return a finite value after setter delivery.");
            // A setter may call HDR itself. Re-read for validity, but acknowledge only the revision just delivered,
            // so a value changed within the setter will still be delivered on the next pump.
            GetValue(binding.Key.Item1,binding.Key.Item2);if(!BindingCurrent(binding))return;
            // An unchanged/no-op setter is quiet, as is an exact canonical echo. A setter that deliberately authors
            // another source value is a new source change and must be written on the next pump.
            binding.SourcePending=observed!=precedingExternal&&observed!=value;
            binding.External=observed;binding.Revision=revision;binding.Observed=true;
        }
        bool BindingCurrent(ValueBinding binding)
        {
            ValueBinding current;
            if(binding.Revoked||disposed||!ReferenceEquals(endpoint,binding.Endpoint)||ConnectionGeneration!=binding.Generation
                ||valueBindings==null||!valueBindings.TryGetValue(binding.Key,out current)||!ReferenceEquals(current,binding)||!Ready)return false;
            return !binding.Revoked&&!disposed&&ReferenceEquals(endpoint,binding.Endpoint)&&ConnectionGeneration==binding.Generation
                &&valueBindings!=null&&valueBindings.TryGetValue(binding.Key,out current)&&ReferenceEquals(current,binding);
        }
        void Detach(ValueBinding binding)
        {binding.Revoked=true;ValueBinding current;if(valueBindings!=null&&valueBindings.TryGetValue(binding.Key,out current)&&ReferenceEquals(current,binding))valueBindings.Remove(binding.Key);}
        void RevokeBindings()
        {if(valueBindings==null)return;foreach(var binding in valueBindings.Values)binding.Revoked=true;valueBindings.Clear();}
        void RevokeBindings(long context)
        {if(valueBindings==null)return;var snapshot=new List<ValueBinding>(valueBindings.Values);foreach(var binding in snapshot)if(binding.Key.Item1==context)Detach(binding);}
        sealed class ValueBinding : IDisposable
        {
            readonly HdrModApi owner;
            internal readonly MyTuple<long,string> Key;
            internal readonly Func<string,object[],object> Endpoint;
            internal readonly long Generation;
            internal readonly Func<double> Getter;
            internal readonly Action<double> Setter;
            internal bool Revoked,Observed,SourcePending;internal double External;internal long Revision;
            internal ValueBinding(HdrModApi owner,MyTuple<long,string> key,Func<string,object[],object> endpoint,long generation,Func<double> getter,Action<double> setter)
            {this.owner=owner;Key=key;Endpoint=endpoint;Generation=generation;Getter=getter;Setter=setter;}
            public void Dispose(){owner.Detach(this);}
        }
        void ReleaseEndpoint(){RevokeBindings();var prior=endpoint;endpoint=null;if(prior!=null)try{prior("release",new object[0]);}catch{}}
        public void Dispose()
        {
            if(disposed)return;disposed=true;ReleaseEndpoint();service=null;
            if(MyAPIGateway.Utilities!=null)MyAPIGateway.Utilities.UnregisterMessageHandler(DiscoveryChannel,Receive);
        }
    }
}
