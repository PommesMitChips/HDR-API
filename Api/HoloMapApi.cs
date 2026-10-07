// Paste this class inside a programmable-block script. No mod assemblies are needed by the PB.
// Uses the standard PB imports: System, System.Collections.Generic, Sandbox.ModAPI.Ingame, VRageMath.
public sealed class HoloMapApi
{
    IMyTerminalBlock _caller;
    Func<string> _version, _apiVersion;
    Func<IMyTerminalBlock, IMyTerminalBlock, VRage.MyTuple<string, string, int>> _capabilities;
    public const int Supported = 1, NativeLcd = 2, CalibratedVectorLcd = 4, Floating3D = 8, TableVolume = 16, ProjectedSurfaces = 32, Ui = 64;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, Vector3D[], Vector2I[], Vector4, float, VRage.MyTuple<bool, string>> _wires;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, Vector3D[], int[][], VRage.MyTuple<Vector4, Vector4, float, bool>, VRage.MyTuple<bool, string>> _polygons;
    Func<IMyTerminalBlock, IMyTerminalBlock, Vector3D, Vector3D, double, VRage.MyTuple<bool, string>> _view;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, MatrixD, VRage.MyTuple<bool, string>> _transform;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, bool, VRage.MyTuple<bool, string>> _visible;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, VRage.MyTuple<bool, string>> _remove;
    Func<IMyTerminalBlock, IMyTerminalBlock, VRage.MyTuple<bool, string>> _clear;
    Func<IMyTerminalBlock, IMyTerminalBlock, bool, double, Vector3D, VRage.MyTuple<bool, string>> _track;
    Func<IMyTerminalBlock, IMyTerminalBlock, bool, double, Vector3D, VRage.MyTuple<bool, string>> _trackWorld;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, Vector3D, string, Vector4, float, VRage.MyTuple<bool, string>> _label;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, string, VRage.MyTuple<bool, string>> _objectLayer;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, VRage.MyTuple<bool, string>> _constructLayer;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, bool, VRage.MyTuple<bool, string>> _layerVisible;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, float, VRage.MyTuple<bool, string>> _layerOpacity;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, VRage.MyTuple<bool, string, bool, float>> _layerState;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, string, VRage.MyTuple<MatrixD, int>, VRage.MyTuple<bool, string>> _svg, _svgAsset;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, string, Vector2, Vector4, VRage.MyTuple<bool, string>> _image;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, float, VRage.MyTuple<bool, string>> _opacity;
    Func<IMyTerminalBlock, IMyTerminalBlock, string, string, double, VRage.MyTuple<MatrixD, int>, VRage.MyTuple<bool, string>> _packedFrame;
    Func<IMyTerminalBlock,IMyTerminalBlock,string,float,VRage.MyTuple<bool,string>> _emission;
    Func<IMyTerminalBlock,IMyTerminalBlock,string,Vector4[],VRage.MyTuple<bool,string>> _clip;
    Func<IMyTerminalBlock,IMyTerminalBlock,string,VRage.MyTuple<Vector3D,Vector3D,Vector4,Vector4>,VRage.MyTuple<int,bool>,VRage.MyTuple<bool,string>> _gradient;
    Func<IMyTerminalBlock,IMyTerminalBlock,string,Vector3D[][],VRage.MyTuple<Vector4,Vector4,float,bool>,bool,VRage.MyTuple<bool,string>> _contours;
    Func<IMyTerminalBlock,IMyTerminalBlock,string,string,Vector4,VRage.MyTuple<double,MatrixD,string>,VRage.MyTuple<bool,string>> _text;

    public bool IsActive { get { return _caller != null; } }
    public string Version { get { Ready(); return _version(); } }
    public string ApiVersion { get { Ready(); return _apiVersion == null ? "HDR.Api/1" : _apiVersion(); } }
    public bool Activate(IMyTerminalBlock caller)
    {
        _caller = null;
        if (caller == null) return false;
        var property = caller.GetProperty("HDR.Api");
        if (property == null) return false;
        var typed = property.As<IReadOnlyDictionary<string, Delegate>>();
        if (typed == null) return false;
        var methods = typed.GetValue(caller);
        if (methods == null) return false;
        Bind(ref _version, methods, "Version");
        Bind(ref _apiVersion, methods, "ApiVersion");
        Bind(ref _capabilities, methods, "GetDisplayCapabilities");
        Bind(ref _wires, methods, "PutWires");
        Bind(ref _polygons, methods, "PutPolygons");
        Bind(ref _view, methods, "SetView");
        Bind(ref _transform, methods, "SetTransform");
        Bind(ref _visible, methods, "SetVisible");
        Bind(ref _remove, methods, "Remove");
        Bind(ref _clear, methods, "Clear");
        Bind(ref _track, methods, "TrackConstruct");
        Bind(ref _trackWorld, methods, "TrackConstructWorld");
        Bind(ref _label, methods, "PutLabel");
        Bind(ref _objectLayer, methods, "SetObjectLayer");
        Bind(ref _constructLayer, methods, "SetConstructLayer");
        Bind(ref _layerVisible, methods, "SetLayerVisible");
        Bind(ref _layerOpacity, methods, "SetLayerOpacity");
        Bind(ref _layerState, methods, "GetLayerState");
        Bind(ref _svg, methods, "PutSvg");
        Bind(ref _svgAsset, methods, "PutSvgAsset");
        Bind(ref _image, methods, "PutImage");
        Bind(ref _opacity, methods, "SetOpacity");
        Bind(ref _packedFrame, methods, "PutPackedAnimationFrame");
        Bind(ref _emission, methods, "SetEmission");
        Bind(ref _clip, methods, "SetClip");
        Bind(ref _gradient, methods, "SetGradient");
        Bind(ref _contours, methods, "PutContours");
        Bind(ref _text, methods, "PutText");
        if (_version == null || _wires == null || _polygons == null || _view == null || _transform == null || _visible == null || _remove == null || _clear == null) return false;
        if (methods.ContainsKey("ApiVersion") && _apiVersion == null) return false;
        if (!ProtocolSupported(_apiVersion, _version)) return false;
        _caller = caller;
        return true;
    }
    // Compatibility follows the API protocol, independently of product releases.
    // Historical 0.1–0.9 dictionaries predate ApiVersion; required shapes are checked above.
    static bool ProtocolSupported(Func<string> protocol, Func<string> product)
    {
        try
        {
            if (protocol != null) return protocol() == "HDR.Api/1";
            string release = product();
            if (release == null) return false;
            string[] parts = release.Split('.'); int major, minor, patch;
            return parts.Length == 3 && int.TryParse(parts[0], out major) && major == 0
                && int.TryParse(parts[1], out minor) && minor >= 1 && minor <= 9
                && int.TryParse(parts[2], out patch) && patch >= 0;
        }
        catch { return false; }
    }
    // Detached structural result: no target selection, LCD claim or scene allocation.
    // Viewer plugin availability is a separate client-side concern.
    public VRage.MyTuple<string, string, int> Capabilities(IMyTerminalBlock display)
    {
        Ready();
        if (_capabilities == null) throw new InvalidOperationException("This HDR API release does not expose display capabilities.");
        var result = _capabilities(_caller, display);
        if (result.Item1 != "HDR.DisplayCapabilities/1" || result.Item3 < 0 || (result.Item3 & ~127) != 0)
            throw new InvalidOperationException("Unsupported display capability protocol.");
        return result;
    }
    static T Get<T>(IReadOnlyDictionary<string, Delegate> methods, string name) where T : class
    { Delegate method; return methods.TryGetValue(name, out method) ? method as T : null; }
    static void Bind<T>(ref T slot,IReadOnlyDictionary<string,Delegate> methods,string name) where T:class
    { slot=Get<T>(methods,name); }
    void Ready() { if (_caller == null) throw new InvalidOperationException("Call HoloMapApi.Activate(Me) first."); }
    static void Check(VRage.MyTuple<bool, string> result) { if (!result.Item1) throw new InvalidOperationException(result.Item2); }

    public List<IMyProjector> FindTargets(IMyGridTerminalSystem terminal,string name)
    {
        Ready();if(terminal==null)throw new ArgumentNullException("terminal");
        var targets=new List<IMyProjector>();terminal.GetBlocksOfType<IMyProjector>(targets,b=>b.CustomName==name&&b.IsSameConstructAs(_caller));return targets;
    }
    public IMyProjector FindTarget(IMyGridTerminalSystem terminal,string name)
    {
        IMyProjector nearest=null;double distance=double.MaxValue;
        foreach(var block in FindTargets(terminal,name))
        {
            double candidate=Vector3D.DistanceSquared(block.GetPosition(),_caller.GetPosition());
            if(nearest==null||candidate<distance||candidate==distance&&block.EntityId<nearest.EntityId){nearest=block;distance=candidate;}
        }
        return nearest;
    }
    public static MatrixD Pose(Vector3D position,double scale=1,Vector3D? rotationRadians=null)
    {
        var r=rotationRadians??Vector3D.Zero;
        return MatrixD.CreateScale(scale)*MatrixD.CreateFromYawPitchRoll(r.Y,r.X,r.Z)*MatrixD.CreateTranslation(position);
    }
    public static MatrixD Pose(double x=0,double y=0,double z=0,double scale=1,double pitch=0,double yaw=0,double roll=0)
    {return Pose(new Vector3D(x,y,z),scale,new Vector3D(pitch,yaw,roll));}
    public static Vector4 Rgba(float r,float g,float b,float a=1)
    {
        if(!Finite(r)||!Finite(g)||!Finite(b)||!Finite(a)||r<0||r>1||g<0||g>1||b<0||b>1||a<0||a>1)throw new ArgumentException("RGBA components must be finite, 0–1.");
        return new Vector4(r,g,b,a);
    }
    public static Vector4 Rgb(byte r,byte g,byte b,float alpha=1)
    {return Rgba(r/255f,g/255f,b/255f,alpha);}
    public void PutCircle(IMyTerminalBlock console,string id,Vector3D center,double radius,Vector4 color,
        int segments=64,float thickness=0.008f,Vector3D? normal=null)
    {
        var n=normal??Vector3D.UnitZ;
        if(!Finite(radius)||radius<=0||!Finite(n.X)||!Finite(n.Y)||!Finite(n.Z)||n.LengthSquared()<1e-20)throw new ArgumentException("Circle requires positive finite radius and a nonzero normal.");
        n.Normalize();var seed=Math.Abs(n.X)<0.8?Vector3D.UnitX:Vector3D.UnitY;var u=Vector3D.Normalize(Vector3D.Cross(n,seed));var v=Vector3D.Cross(n,u);
        PutCurve(console,id,t=>center+radius*(Math.Cos(t)*u+Math.Sin(t)*v),0,2*Math.PI,color,segments,thickness,true);
    }
    public bool ToggleLayer(IMyTerminalBlock console,string layer)
    {bool visible=!GetLayerState(console,layer).Item1;SetLayerVisible(console,layer,visible);return visible;}
    public void SoloLayer(IMyTerminalBlock console,string selected,string[] layers)
    {
        if(layers==null)throw new ArgumentNullException("layers");bool found=false;foreach(string name in layers)if(name==selected)found=true;
        if(!found)throw new ArgumentException("Selected layer must occur in the provided layer list.");
        foreach(string name in layers)SetLayerVisible(console,name,name==selected);
    }

    public void PutLine(IMyTerminalBlock console, string id, Vector3D from, Vector3D to, Vector4 rgba, float thickness = 0.008f)
    { PutWires(console, id, new[] { from, to }, new[] { new Vector2I(0, 1) }, rgba, thickness); }
    public void PutWires(IMyTerminalBlock console, string id, Vector3D[] points, Vector2I[] connections, Vector4 rgba, float thickness = 0.008f)
    { Ready(); Check(_wires(_caller, console, id, points, connections, rgba, thickness)); }
    public void PutPolygons(IMyTerminalBlock console, string id, Vector3D[] points, int[][] faces,
        Vector4 outlineRgba, Vector4 fillRgba, float thickness = 0.008f, bool shaded = true)
    { Ready(); Check(_polygons(_caller, console, id, points, faces, new VRage.MyTuple<Vector4, Vector4, float, bool>(outlineRgba, fillRgba, thickness, shaded))); }

    // Sample exactly once per call in the PB; no executable function is sent across the network.
    // For a closed curve, end is excluded to avoid a duplicate endpoint.
    public static Vector3D[] SampleCurve(Func<double, Vector3D> function,
        double start, double end, int segments = 128, bool closed = false)
    {
        if (function == null || !Finite(start) || !Finite(end) || start == end || !Finite(end - start)
            || segments < (closed ? 3 : 1) || segments > (closed ? 2048 : 2047))
            throw new ArgumentException("Curve requires a function, finite distinct bounds, and 1–2047 segments (3–2048 when closed).");
        var points = new Vector3D[closed ? segments : segments + 1];
        for (int i = 0; i < points.Length; i++)
        {
            var p = function(start + (end - start) * ((double)i / segments));
            if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) || p.LengthSquared() > 1e12)
                throw new ArgumentException("Curve produced a nonfinite or out-of-range point; split discontinuous curves into separate objects.");
            points[i] = p;
        }
        return points;
    }
    static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
    public void PutCurve(IMyTerminalBlock console, string id,
        Func<double, Vector3D> function, double start, double end, Vector4 rgba,
        int segments = 128, float thickness = 0.008f, bool closed = false)
    {
        Ready();
        var points = SampleCurve(function, start, end, segments, closed);
        var edges = new Vector2I[segments];
        for (int i = 0; i < segments; i++) edges[i] = new Vector2I(i, (i + 1) % points.Length);
        PutWires(console, id, points, edges, rgba, thickness);
    }
    public void SetView(IMyTerminalBlock console, Vector3D offset, Vector3D rotationRadians, double scale = 1)
    { Ready(); Check(_view(_caller, console, offset, rotationRadians, scale)); }
    public void SetTransform(IMyTerminalBlock console, string id, MatrixD transform)
    { Ready(); Check(_transform(_caller, console, id, transform)); Remember(console, id, transform); }
    public void SetVisible(IMyTerminalBlock console, string id, bool visible)
    { Ready(); Check(_visible(_caller, console, id, visible)); }
    public void Remove(IMyTerminalBlock console, string id)
    { Ready(); Check(_remove(_caller, console, id)); Forget(console, id); }
    public void Clear(IMyTerminalBlock console)
    { Ready(); Check(_clear(_caller, console)); ClearAnimations(console); }
    public void TrackConstruct(IMyTerminalBlock console, bool enabled = true,
        double displayRadius = 0.55, Vector3D? offset = null)
    {
        Ready();
        if (_track == null) throw new InvalidOperationException("Update the HoloMap mod to the live-construct build.");
        Check(_track(_caller, console, enabled, displayRadius, offset ?? Vector3D.Zero));
    }
    public void TrackConstructWorld(IMyTerminalBlock console, double metresPerUnit, Vector3D rootLocalOrigin)
    {
        Ready();
        if (_trackWorld == null) throw new InvalidOperationException("Update HoloMap mod to the terrain-scanner build.");
        Check(_trackWorld(_caller, console, true, metresPerUnit, rootLocalOrigin));
    }
    public void PutLabel(IMyTerminalBlock console, string id, Vector3D position,
        string text, Vector4 color, float height = 0.035f)
    {
        Ready();
        if (_label == null) throw new InvalidOperationException("Update HoloMap mod to the labels build.");
        Check(_label(_caller, console, id, position, text, color, height));
    }
    void LayersReady()
    { Ready(); if (_objectLayer == null || _constructLayer == null || _layerVisible == null || _layerOpacity == null || _layerState == null) throw new InvalidOperationException("Update HoloMap mod to the layers build."); }
    public void SetObjectLayer(IMyTerminalBlock console, string id, string layer)
    { LayersReady(); Check(_objectLayer(_caller, console, id, layer)); }
    public void SetConstructLayer(IMyTerminalBlock console, string layer)
    { LayersReady(); Check(_constructLayer(_caller, console, layer)); }
    public void SetLayerVisible(IMyTerminalBlock console, string layer, bool visible)
    { LayersReady(); Check(_layerVisible(_caller, console, layer, visible)); }
    public void SetLayerOpacity(IMyTerminalBlock console, string layer, float opacity)
    { LayersReady(); Check(_layerOpacity(_caller, console, layer, opacity)); }
    public VRage.MyTuple<bool, float> GetLayerState(IMyTerminalBlock console, string layer)
    {
        LayersReady(); var state = _layerState(_caller, console, layer);
        if (!state.Item1) throw new InvalidOperationException(state.Item2);
        return new VRage.MyTuple<bool, float>(state.Item3, state.Item4);
    }

    public void PutSvg(IMyTerminalBlock console, string id, string svg,
        Vector3D position, double scale = 0.01, int curveSegments = 12)
    { PutSvg(console, id, svg, Pose(position, scale), curveSegments); }
    public void PutSvg(IMyTerminalBlock console, string id, string svg, MatrixD transform, int curveSegments = 12)
    {
        Ready(); if (_svg == null) throw new InvalidOperationException("Update HoloMap to the SVG build.");
        Check(_svg(_caller, console, id, svg, new VRage.MyTuple<MatrixD, int>(transform, curveSegments))); Remember(console, id, transform);
    }
    public void PutSvgAsset(IMyTerminalBlock console, string id, string asset,
        Vector3D position, double scale = 0.01, int curveSegments = 12)
    {
        Ready(); if (_svgAsset == null) throw new InvalidOperationException("Update HoloMap to the SVG build.");
        var transform = Pose(position, scale);
        Check(_svgAsset(_caller, console, id, asset, new VRage.MyTuple<MatrixD, int>(transform, curveSegments))); Remember(console, id, transform);
    }
    public void PutImage(IMyTerminalBlock console, string id, string asset,
        Vector2 size, Vector4 tint)
    {
        Ready(); if (_image == null) throw new InvalidOperationException("Update HoloMap to the image build.");
        Check(_image(_caller, console, id, asset, size, tint));
    }
    public void SetOpacity(IMyTerminalBlock console, string id, float opacity)
    { Ready(); if (_opacity == null) throw new InvalidOperationException("Update HoloMap to the animation build."); Check(_opacity(_caller, console, id, opacity)); }
    public void PutPackedAnimationFrame(IMyTerminalBlock console,string id,string customData,double timeSeconds,
        Vector3D position,double scale=0.003,int curveSegments=3)
    {
        Ready();if(_packedFrame==null)throw new InvalidOperationException("Update HoloMap to the packed animation build.");
        var transform=Pose(position,scale);
        Check(_packedFrame(_caller,console,id,customData,timeSeconds,new VRage.MyTuple<MatrixD,int>(transform,curveSegments)));
    }

    sealed class Motion
    {
        public string Channel; public double From, To, Duration, Time;
        public bool Loop, PingPong;
    }
    // Appearance operations are implemented in the mod; PB retains only control data.
    public void SetTransparency(IMyTerminalBlock console,string id,float transparency)
    {if(!Finite(transparency)||transparency<0||transparency>1)throw new ArgumentException("Transparency must be 0–1.");SetOpacity(console,id,1-transparency);}
    public void SetEmission(IMyTerminalBlock console,string id,float strength)
    {Ready();if(_emission==null)throw new InvalidOperationException("Update HoloMap to the appearance build.");Check(_emission(_caller,console,id,strength));}
    public void SetClip(IMyTerminalBlock console,string id,Vector4[] planes)
    {Ready();if(_clip==null)throw new InvalidOperationException("Update HoloMap to the appearance build.");Check(_clip(_caller,console,id,planes));}
    public void SetClipBox(IMyTerminalBlock console,string id,Vector3D min,Vector3D max)
    {
        if(!Finite(min.X)||!Finite(min.Y)||!Finite(min.Z)||!Finite(max.X)||!Finite(max.Y)||!Finite(max.Z)||min.X>=max.X||min.Y>=max.Y||min.Z>=max.Z)throw new ArgumentException("Clip box needs finite ordered corners.");
        SetClip(console,id,new[]{new Vector4(1,0,0,(float)-min.X),new Vector4(-1,0,0,(float)max.X),new Vector4(0,1,0,(float)-min.Y),new Vector4(0,-1,0,(float)max.Y),new Vector4(0,0,1,(float)-min.Z),new Vector4(0,0,-1,(float)max.Z)});
    }
    public void SetClipCircle(IMyTerminalBlock console,string id,Vector2 center,double radius,int sides=12)
    {
        if(!Finite(center.X)||!Finite(center.Y)||!Finite(radius)||radius<=0||sides<3||sides>16)throw new ArgumentException("Circular XY clip requires positive finite radius and 3–16 sides.");
        var planes=new Vector4[sides];for(int i=0;i<sides;i++){double a=2*Math.PI*i/sides;double x=Math.Cos(a),y=Math.Sin(a);planes[i]=new Vector4((float)-x,(float)-y,0,(float)(radius+center.X*x+center.Y*y));}SetClip(console,id,planes);
    }
    public void SetGradient(IMyTerminalBlock console,string id,Vector3D from,Vector3D to,
        Vector4 start,Vector4 end,int resolution=3,bool radial=false)
    {Ready();if(_gradient==null)throw new InvalidOperationException("Update HoloMap to the appearance build.");Check(_gradient(_caller,console,id,new VRage.MyTuple<Vector3D,Vector3D,Vector4,Vector4>(from,to,start,end),new VRage.MyTuple<int,bool>(resolution,radial)));}
    public void ClearGradient(IMyTerminalBlock console,string id)
    {SetGradient(console,id,Vector3D.Zero,Vector3D.UnitX,Vector4.One,Vector4.One,0);}
    public void PutContours(IMyTerminalBlock console,string id,Vector3D[][] contours,
        Vector4 outline,Vector4 fill,float thickness=0.008f,bool shaded=false,bool evenOdd=true)
    {Ready();if(_contours==null)throw new InvalidOperationException("Update HoloMap to the appearance build.");Check(_contours(_caller,console,id,contours,new VRage.MyTuple<Vector4,Vector4,float,bool>(outline,fill,thickness,shaded),evenOdd));}
    public void PutText(IMyTerminalBlock console,string id,string text,Vector4 color,
        double height,MatrixD transform,string anchor="middle")
    {Ready();if(_text==null)throw new InvalidOperationException("Update HoloMap to the appearance build.");Check(_text(_caller,console,id,text,color,new VRage.MyTuple<double,MatrixD,string>(height,transform,anchor)));Remember(console,id,transform);}
    public void PutVertices(IMyTerminalBlock console,string id,Vector3D[] positions,Vector4 color,double radius=0.01,float thickness=0.004f)
    {
        if(positions==null||positions.Length<1||positions.Length>341||!Finite(radius)||radius<=0)throw new ArgumentException("Vertices require 1–341 positions and a positive finite marker radius.");
        var points=new Vector3D[positions.Length*6];var edges=new Vector2I[positions.Length*3];
        for(int i=0;i<positions.Length;i++)for(int axis=0;axis<3;axis++){var d=(axis==0?Vector3D.UnitX:axis==1?Vector3D.UnitY:Vector3D.UnitZ)*radius;int k=i*6+axis*2;points[k]=positions[i]-d;points[k+1]=positions[i]+d;edges[i*3+axis]=new Vector2I(k,k+1);}PutWires(console,id,points,edges,color,thickness);
    }
    sealed class AnimatedObject
    {
        public IMyTerminalBlock Console; public string Id;
        public MatrixD Base = MatrixD.Identity;
        public double X, Y, Z, Pitch, Yaw, Roll, Scale = 1;
        public string[] Frames; public double FrameRate, FrameTime; public int FrameIndex, CurveSegments;
        public bool FrameLoop;
        public readonly List<Motion> Motions = new List<Motion>();
    }
    readonly Dictionary<string, AnimatedObject> _animated = new Dictionary<string, AnimatedObject>();
    static string AnimationKey(IMyTerminalBlock console, string id) { return console.EntityId + ":" + id; }
    AnimatedObject AnimationObject(IMyTerminalBlock console, string id)
    {
        if (console == null || string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Animation requires a Console and object ID.");
        string key = AnimationKey(console,id); AnimatedObject state;
        if (!_animated.TryGetValue(key,out state))
        {
            if (_animated.Count >= 128) throw new InvalidOperationException("Animation helper object budget exceeded.");
            state = new AnimatedObject { Console=console, Id=id }; _animated.Add(key,state);
        }
        return state;
    }
    void Remember(IMyTerminalBlock console,string id,MatrixD transform)
    {
        var state=AnimationObject(console,id);state.Base=transform;state.Motions.Clear();state.Frames=null;
        state.X=state.Y=state.Z=state.Pitch=state.Yaw=state.Roll=0;state.Scale=1;
    }
    void Forget(IMyTerminalBlock console,string id) { _animated.Remove(AnimationKey(console,id)); }
    void ClearAnimations(IMyTerminalBlock console)
    {
        var keys=new List<string>();foreach(var pair in _animated)if(pair.Value.Console.EntityId==console.EntityId)keys.Add(pair.Key);
        foreach(string key in keys)_animated.Remove(key);
    }
    // PB owns animation time: call UpdateAnimations(Runtime.TimeSinceLastRun.TotalSeconds).
    // Transform tracks are offsets relative to the last SetTransform/PutSvg placement.
    public void Animate(IMyTerminalBlock console,string id,string channel,
        double from,double to,double seconds,bool loop=false,bool pingPong=false)
    {
        Ready();channel=(channel??"").ToLowerInvariant();
        if (channel!="x"&&channel!="y"&&channel!="z"&&channel!="pitch"&&channel!="yaw"&&channel!="rotation"&&channel!="scale"&&channel!="opacity")
            throw new ArgumentException("Animation channel: x/y/z, pitch/yaw/rotation (radians), scale (multiplier), opacity.");
        if(!Finite(from)||!Finite(to)||!Finite(seconds)||seconds<0.001||seconds>86400||Math.Abs(from)>1000000||Math.Abs(to)>1000000
            ||(channel=="scale"&&(from<0.000001||to<0.000001))||(channel=="opacity"&&(from<0||to<0||from>1||to>1)))
            throw new ArgumentException("Invalid animation bounds or duration.");
        var state=AnimationObject(console,id);
        for(int i=state.Motions.Count-1;i>=0;i--)if(state.Motions[i].Channel==channel)state.Motions.RemoveAt(i);
        state.Motions.Add(new Motion{Channel=channel,From=from,To=to,Duration=seconds,Loop=loop,PingPong=pingPong});
    }
    public static double AnimationValue(double from,double to,double elapsed,double seconds,bool loop,bool pingPong)
    {
        if(!Finite(from)||!Finite(to)||!Finite(to-from)||!Finite(elapsed)||elapsed<0||!Finite(seconds)||seconds<0.001||!Finite(elapsed/seconds))throw new ArgumentException("Invalid animation time.");
        double t=elapsed/seconds;
        if(loop){t%=pingPong?2:1;if(pingPong&&t>1)t=2-t;}else{t=Math.Min(t,pingPong?2:1);if(pingPong&&t>1)t=2-t;}
        return from+(to-from)*t;
    }
    public void UpdateAnimations(double elapsedSeconds)
    {
        Ready();if(!Finite(elapsedSeconds)||elapsedSeconds<0)throw new ArgumentException("Animation elapsed time must be finite and nonnegative.");
        foreach(var state in _animated.Values)
        {
            bool transform=false;
            for(int i=state.Motions.Count-1;i>=0;i--)
            {
                var motion=state.Motions[i];motion.Time=Math.Min(1e12,motion.Time+elapsedSeconds);
                double value=AnimationValue(motion.From,motion.To,motion.Time,motion.Duration,motion.Loop,motion.PingPong);
                if(motion.Channel=="opacity")SetOpacity(state.Console,state.Id,(float)value);
                else
                {
                    transform=true;
                    if(motion.Channel=="x")state.X=value;else if(motion.Channel=="y")state.Y=value;else if(motion.Channel=="z")state.Z=value;
                    else if(motion.Channel=="pitch")state.Pitch=value;else if(motion.Channel=="yaw")state.Yaw=value;else if(motion.Channel=="rotation")state.Roll=value;else state.Scale=value;
                }
                if(!motion.Loop&&motion.Time>=motion.Duration*(motion.PingPong?2:1))state.Motions.RemoveAt(i);
            }
            if(transform)Check(_transform(_caller,state.Console,state.Id,
                MatrixD.CreateScale(state.Scale)*MatrixD.CreateFromYawPitchRoll(state.Yaw,state.Pitch,state.Roll)*state.Base*MatrixD.CreateTranslation(state.X,state.Y,state.Z)));
            if(state.Frames!=null)
            {
                state.FrameTime=Math.Min(1e12,state.FrameTime+elapsedSeconds);
                int index=state.FrameLoop?(int)(Math.Floor(state.FrameTime*state.FrameRate)%state.Frames.Length)
                    :(int)Math.Min(state.Frames.Length-1,Math.Floor(state.FrameTime*state.FrameRate));
                if(index!=state.FrameIndex)
                {
                    var placement=MatrixD.CreateScale(state.Scale)*MatrixD.CreateFromYawPitchRoll(state.Yaw,state.Pitch,state.Roll)*state.Base*MatrixD.CreateTranslation(state.X,state.Y,state.Z);
                    Check(_svg(_caller,state.Console,state.Id,state.Frames[index],new VRage.MyTuple<MatrixD,int>(placement,state.CurveSegments)));
                    state.FrameIndex=index;
                }
                if(!state.FrameLoop&&index==state.Frames.Length-1)state.Frames=null;
            }
        }
    }
    public void PlaySvgFrames(IMyTerminalBlock console,string id,string[] frames,
        double framesPerSecond,Vector3D position,double scale=0.01,bool loop=true,int curveSegments=12)
    {
        if(frames==null||frames.Length<1||frames.Length>300||!Finite(framesPerSecond)||framesPerSecond<1||framesPerSecond>30)
            throw new ArgumentException("SVG animation requires 1–300 frames and 1–30 fps.");
        int characters=0;foreach(string frame in frames){if(string.IsNullOrEmpty(frame))throw new ArgumentException("Empty SVG frame.");characters+=frame.Length;if(characters>60000)throw new ArgumentException("SVG frame sequence exceeds 60k characters.");}
        PutSvg(console,id,frames[0],position,scale,curveSegments);
        var state=AnimationObject(console,id);state.Frames=(string[])frames.Clone();state.FrameRate=framesPerSecond;
        state.FrameTime=0;state.FrameIndex=0;state.FrameLoop=loop;state.CurveSegments=curveSegments;
    }
    public void StopAnimation(IMyTerminalBlock console,string id)
    { AnimatedObject state;if(_animated.TryGetValue(AnimationKey(console,id),out state)){state.Motions.Clear();state.Frames=null;} }
}
