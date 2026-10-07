using System;
using System.Collections.Generic;
using ProtoBuf;
using VRageMath;

namespace HoloMap
{
    // Declarative display data only. No callbacks, file paths, runtime types or executable code.
    [ProtoContract]
    public sealed class HoloGeometryDeclaration
    {
        [ProtoMember(1)] public string SvgSource;
        [ProtoMember(2)] public int SvgSegments;
        [ProtoMember(3)] public float[] Clip;
        [ProtoMember(4)] public double[] Gradient;
        [ProtoMember(5)] public List<HoloFaceDeclaration> Faces;
        [ProtoMember(6)] public List<HoloContourDeclaration> Contours;
        [ProtoMember(7)] public bool EvenOdd;
        [ProtoMember(8)] public string Text;
        [ProtoMember(9)] public double TextHeight;
        [ProtoMember(10)] public string TextAnchor;
    }
    [ProtoContract] public sealed class HoloFaceDeclaration { [ProtoMember(1)] public int[] Indices; }
    [ProtoContract] public sealed class HoloContourDeclaration { [ProtoMember(1)] public double[] Points; }
    public sealed partial class HoloMapSession
    {
        static Geometry EmptyDisplayGeometry(){return new Geometry(new[]{Vector3D.Zero},new int[0],new int[0]);}
        static void ValidateSvgSource(string source,int segments)
        {
            if(string.IsNullOrWhiteSpace(source)||source.Length>Svg.MaxCharacters||segments<2||segments>32)
                throw new ArgumentException("SVG requires 1–65536 characters and 2–32 curve segments.");
        }
        static void ValidateText(string text,double height,string anchor)
        {
            if(text==null||text.Length>64||!Geometry.Finite(height)||height<=0||height>1000000)
                throw new ArgumentException("Text requires at most 64 characters and positive finite height.");
            if(anchor!="start"&&anchor!="middle"&&anchor!="end")throw new ArgumentException("Invalid text anchor.");
        }
        static int[][] ValidateFaces(Vector3D[] points,int[][] faces)
        {
            Geometry.ValidatePoints(points);
            if(faces==null||faces.Length==0||faces.Length>Geometry.MaxPrimitives)throw new ArgumentException("Invalid face count.");
            int total=0;var result=new int[faces.Length][];
            for(int f=0;f<faces.Length;f++)
            {
                var face=faces[f];if(face==null||face.Length<3||face.Length>128)throw new ArgumentException("Faces require 3–128 indices.");
                total+=face.Length;if(total>Geometry.MaxPrimitives*3||total-2*(f+1)>Geometry.MaxPrimitives)throw new ArgumentException("Face input work budget exceeded.");
                var unique=new HashSet<int>();foreach(int i in face)if(i<0||i>=points.Length||!unique.Add(i))throw new ArgumentException("Invalid or repeated face index.");
                result[f]=(int[])face.Clone();
            }
            return result;
        }
        static Vector3D[][] ValidateContours(Vector3D[][] contours)
        {
            if(contours==null||contours.Length==0||contours.Length>16)throw new ArgumentException("Contour count exceeds budget.");
            var result=new Vector3D[contours.Length][];int total=0;
            for(int i=0;i<contours.Length;i++)
            {
                var c=contours[i];if(c==null||c.Length<3||c.Length>128)throw new ArgumentException("Contours require 3–128 points.");
                Geometry.ValidatePoints(c);total+=c.Length;if(total>256)throw new ArgumentException("Contour input budget exceeded.");result[i]=(Vector3D[])c.Clone();
            }
            return result;
        }
        static void ValidateGradient(GradientStyle g)
        {
            if(g==null)return;Geometry.ValidatePoints(new[]{g.From,g.To});Color(g.Start);Color(g.End);
            if(g.Resolution<1||g.Resolution>8||(g.To-g.From).LengthSquared()<1e-16)throw new ArgumentException("Invalid gradient declaration.");
        }
        static HoloGeometryDeclaration ExportGeometryDeclaration(Item item)
        {
            if(item.SvgSource==null&&item.Faces==null&&item.Contours==null&&item.TextSource==null&&item.ClipPlanes==null&&item.Gradient==null)return null;
            var d=new HoloGeometryDeclaration{SvgSource=item.SvgSource,SvgSegments=item.SvgSegments,Faces=null,EvenOdd=item.EvenOdd,Text=item.TextSource,TextHeight=item.TextHeight,TextAnchor=item.TextAnchor};
            if(item.Faces!=null){d.Faces=new List<HoloFaceDeclaration>();foreach(var f in item.Faces)d.Faces.Add(new HoloFaceDeclaration{Indices=f});}
            if(item.ClipPlanes!=null){d.Clip=new float[item.ClipPlanes.Length*4];for(int i=0;i<item.ClipPlanes.Length;i++){var p=item.ClipPlanes[i];d.Clip[i*4]=p.X;d.Clip[i*4+1]=p.Y;d.Clip[i*4+2]=p.Z;d.Clip[i*4+3]=p.W;}}
            if(item.Gradient!=null){var g=item.Gradient;d.Gradient=new[]{g.From.X,g.From.Y,g.From.Z,g.To.X,g.To.Y,g.To.Z,(double)g.Start.X,g.Start.Y,g.Start.Z,g.Start.W,g.End.X,g.End.Y,g.End.Z,g.End.W,(double)g.Resolution,g.Radial?1d:0d};}
            if(item.Contours!=null){d.Contours=new List<HoloContourDeclaration>();for(int i=0;i<item.Contours.Length;i++){var data=new double[item.Contours[i].Length*3];for(int j=0;j<item.Contours[i].Length;j++){var p=item.Contours[i][j];data[j*3]=p.X;data[j*3+1]=p.Y;data[j*3+2]=p.Z;}d.Contours.Add(new HoloContourDeclaration{Points=data});}}
            return d;
        }
        static Vector4[] ReadClip(HoloGeometryDeclaration d)
        {
            if(d.Clip==null)return null;if(d.Clip.Length%4!=0||d.Clip.Length>64)throw new ArgumentException("Invalid clip declaration.");
            var p=new Vector4[d.Clip.Length/4];for(int i=0;i<p.Length;i++)p[i]=new Vector4(d.Clip[i*4],d.Clip[i*4+1],d.Clip[i*4+2],d.Clip[i*4+3]);MeshEffects.ValidatePlanes(p);return p;
        }
        static GradientStyle ReadGradient(HoloGeometryDeclaration d)
        {
            var a=d.Gradient;if(a==null)return null;if(a.Length!=16)throw new ArgumentException("Invalid gradient declaration.");foreach(double n in a)if(!Geometry.Finite(n))throw new ArgumentException("Nonfinite gradient.");
            if(a[14]!=(int)a[14]||(a[15]!=0&&a[15]!=1))throw new ArgumentException("Invalid gradient options.");
            var g=new GradientStyle{From=new Vector3D(a[0],a[1],a[2]),To=new Vector3D(a[3],a[4],a[5]),Start=new Vector4((float)a[6],(float)a[7],(float)a[8],(float)a[9]),End=new Vector4((float)a[10],(float)a[11],(float)a[12],(float)a[13]),Resolution=(int)a[14],Radial=a[15]==1};ValidateGradient(g);return g;
        }
        static Vector3D[][] ReadContours(HoloGeometryDeclaration d)
        {
            if(d.Contours==null)return null;if(d.Contours.Count==0||d.Contours.Count>16)throw new ArgumentException("Invalid contour declaration.");
            var result=new Vector3D[d.Contours.Count][];for(int i=0;i<result.Length;i++){var a=d.Contours[i]==null?null:d.Contours[i].Points;if(a==null||a.Length%3!=0||a.Length<9||a.Length>384)throw new ArgumentException("Invalid contour points.");result[i]=new Vector3D[a.Length/3];for(int j=0;j<result[i].Length;j++)result[i][j]=new Vector3D(a[j*3],a[j*3+1],a[j*3+2]);}return ValidateContours(result);
        }
        static void ValidateGeometryDeclaration(HoloGeometryDeclaration d)
        {
            if(d==null)return;int sources=(d.SvgSource==null?0:1)+(d.Faces==null?0:1)+(d.Contours==null?0:1)+(d.Text==null?0:1);
            if(sources>1)throw new ArgumentException("Conflicting geometry source declarations.");
            if(d.SvgSource!=null)ValidateSvgSource(d.SvgSource,d.SvgSegments);
            if(d.Text!=null)ValidateText(d.Text,d.TextHeight,d.TextAnchor);
            ReadClip(d);ReadGradient(d);ReadContours(d);
            if(d.Faces!=null){int total=0;if(d.Faces.Count==0||d.Faces.Count>Geometry.MaxPrimitives)throw new ArgumentException("Invalid faces.");foreach(var face in d.Faces){var f=face==null?null:face.Indices;if(f==null||f.Length<3||f.Length>128)throw new ArgumentException("Invalid face.");total+=f.Length;if(total>Geometry.MaxPrimitives*3)throw new ArgumentException("Face work budget exceeded.");foreach(int i in f)if(i<0||i>=Geometry.MaxPoints)throw new ArgumentException("Invalid face index.");}}
        }
        static void ImportGeometryDeclaration(Item item,HoloGeometryDeclaration d)
        {
            if(d==null)return;ValidateGeometryDeclaration(d);item.Declaration=d;item.SvgSource=d.SvgSource;item.SvgSegments=d.SvgSegments;if(d.Faces!=null){var faces=new int[d.Faces.Count][];for(int i=0;i<faces.Length;i++)faces[i]=d.Faces[i].Indices;item.Faces=ValidateFaces(item.Geometry.Points,faces);}item.Contours=ReadContours(d);item.EvenOdd=d.EvenOdd;item.TextSource=d.Text;item.TextHeight=d.TextHeight;item.TextAnchor=d.TextAnchor;item.ClipPlanes=ReadClip(d);item.Gradient=ReadGradient(d);
        }
        void ValidateSceneSources(Scene scene,Item[] replacing,HashSet<string> keys)
        {
            long count=0;foreach(var other in _scenes.Values)foreach(var i in other.Items.Values)if(other!=scene||!keys.Contains(Key(i.CallerId,i.Id)))count+=(i.SvgSource==null?0:i.SvgSource.Length)+(i.TextSource==null?0:i.TextSource.Length);
            foreach(var i in replacing)count+=(i.SvgSource==null?0:i.SvgSource.Length)+(i.TextSource==null?0:i.TextSource.Length);
            if(count>262144)throw new ArgumentException("Global artwork source budget exceeds 262144 characters.");
            long bytes=32768+UiRules.ReplicationReserve;foreach(var other in _scenes.Values)foreach(var i in other.Items.Values)if(other!=scene||!keys.Contains(Key(i.CallerId,i.Id)))bytes+=ReplicationItemBytes(i);foreach(var i in replacing)bytes+=ReplicationItemBytes(i);
            foreach(var other in _scenes.Values)bytes+=ProjectedSourceBytes(other);
            bytes+=AnimationReplicationBytes();
            if(bytes>1572864)throw new ArgumentException("Global display replication budget exceeded.");
        }
        static long ReplicationItemBytes(Item item)
        {long n=1024+item.Geometry.Points.Length*24L+(item.Geometry.Edges.Length+item.Geometry.Triangles.Length)*4L;if(item.TriangleColors!=null)n+=item.TriangleColors.Length*16L;if(item.EdgeColors!=null)n+=item.EdgeColors.Length*16L;if(item.UV!=null)n+=item.UV.Length*8L;if(item.SvgSource!=null)n+=item.SvgSource.Length*3L;if(item.TextSource!=null)n+=item.TextSource.Length*3L;if(item.Faces!=null)foreach(var face in item.Faces)n+=face.Length*4L;if(item.Contours!=null)foreach(var contour in item.Contours)n+=contour.Length*24L;return n;}
        sealed class CompiledDisplay
        {
            public string Error;public object ContentIdentity;public Item PendingItem;public Scene PendingScene;
            public HoloGeometryDeclaration Declaration;public Geometry SourceGeometry;public string Svg,Text;public int Segments;public double Height;public string Anchor;
            public int[][] Faces;public Vector3D[][] Contours;public bool EvenOdd;public Vector4[] Clip;public GradientStyle Gradient;
            public Vector4 Fill,Line;
            public SurfaceMesh Mesh;public int LastSeen;public long ConsoleId,CallerId;public string Id;
        }
        readonly Dictionary<string,CompiledDisplay> _clientGeometry=new Dictionary<string,CompiledDisplay>();
        void ClearClientGeometry(){_clientGeometry.Clear();_compileQueue.Clear();_queuedCompile.Clear();_negativeSvg.Clear();_negativeSvgCharacters=0;_lastCompileTick=-1;ClearProjectedCaches();}
        void PruneClientGeometry()
        {
            var dead=new List<string>();foreach(var e in _clientGeometry)if(_ticks-e.Value.LastSeen>600||!_scenes.ContainsKey(e.Value.ConsoleId)||!_scenes[e.Value.ConsoleId].Items.ContainsKey(Key(e.Value.CallerId,e.Value.Id)))dead.Add(e.Key);foreach(var key in dead){_clientGeometry.Remove(key);_queuedCompile.Remove(key);}
            if(dead.Count>0){foreach(var c in _clientGeometry.Values){c.ContentIdentity=null;c.Declaration=null;}var pending=new List<string>();foreach(var key in _compileQueue)if(_clientGeometry.ContainsKey(key))pending.Add(key);_compileQueue.Clear();_queuedCompile.Clear();foreach(var key in pending)if(_queuedCompile.Add(key))_compileQueue.Enqueue(key);}
            CompileQueuedDisplay();
        }
        static Item RenderClone(Item item,SurfaceMesh mesh)
        {return new Item{CallerId=item.CallerId,Id=item.Id,Effects=item.Effects,EffectStart=item.EffectStart,EffectEntering=item.EffectEntering,Geometry=mesh.Geometry,TriangleColors=mesh.Colors,EdgeColors=mesh.EdgeColors,UV=mesh.UV,Material=item.Material,Transform=item.Transform,Visible=item.Visible,Layer=item.Layer,Opacity=item.Opacity,Emission=item.Emission,LineColor=item.LineColor,FillColor=item.FillColor,Thickness=item.Thickness,Shaded=item.Shaded};}
        readonly Queue<string> _compileQueue=new Queue<string>();
        readonly HashSet<string> _queuedCompile=new HashSet<string>();
        readonly Dictionary<string,string> _negativeSvg=new Dictionary<string,string>();
        int _negativeSvgCharacters,_lastCompileTick=-1;
        static object ContentKey(Item i){return i.ContentIdentity??(object)i.Geometry.Points;}
        static bool SameCompilation(CompiledDisplay c,Item i)
        {
            bool source=c.ContentIdentity==ContentKey(i)&&c.Svg==i.SvgSource&&c.Segments==i.SvgSegments&&c.Text==i.TextSource&&c.Height==i.TextHeight&&c.Anchor==i.TextAnchor;
            bool effects=c.Declaration!=null&&c.Declaration==i.Declaration||c.Faces==i.Faces&&c.Contours==i.Contours&&c.EvenOdd==i.EvenOdd&&c.Clip==i.ClipPlanes&&c.Gradient==i.Gradient;
            return source&&effects&&c.Fill==i.FillColor&&c.Line==i.LineColor;
        }
        static string SvgFailureKey(Item item)
        {
            if(item.SvgSource==null)return null;
            string key=item.SvgSegments+":"+item.FillColor+":"+item.LineColor;
            if(item.ClipPlanes!=null)foreach(var p in item.ClipPlanes)key+=":"+p;
            if(item.Gradient!=null){var g=item.Gradient;key+=":"+g.From+":"+g.To+":"+g.Start+":"+g.End+":"+g.Resolution+":"+g.Radial;}
            return key+":"+item.SvgSource;
        }
        void RememberSvgFailure(string key)
        {
            if(key==null||key.Length>262144||_negativeSvg.ContainsKey(key))return;
            if(_negativeSvg.Count>=64||_negativeSvgCharacters+key.Length>262144){_negativeSvg.Clear();_negativeSvgCharacters=0;}
            _negativeSvg.Add(key,key);_negativeSvgCharacters+=key.Length;
        }
        void CompileQueuedDisplay()
        {
            if(!ClientRenderingEnabled||_lastCompileTick==_ticks)return;
            while(_compileQueue.Count>0)
            {
                string key=_compileQueue.Dequeue();_queuedCompile.Remove(key);CompiledDisplay cache;
                if(!_clientGeometry.TryGetValue(key,out cache)||cache.PendingItem==null||cache.PendingScene==null||_ticks-cache.LastSeen>2)continue;
                var item=cache.PendingItem;var scene=cache.PendingScene;cache.PendingItem=null;
                Scene currentScene;if(!_scenes.TryGetValue(scene.ConsoleId,out currentScene)||currentScene!=scene||!scene.Items.ContainsKey(Key(item.CallerId,item.Id)))continue;
                Item current;if(!scene.Items.TryGetValue(Key(item.CallerId,item.Id),out current)||!current.Visible||ClientLayerAlpha(scene,current.CallerId,current.Layer)<=0)continue;
                if(SameCompilation(cache,item))continue;
                _lastCompileTick=_ticks;cache.ContentIdentity=ContentKey(item);cache.Declaration=item.Declaration;cache.SourceGeometry=item.Geometry;cache.Svg=item.SvgSource;cache.Segments=item.SvgSegments;cache.Text=item.TextSource;cache.Height=item.TextHeight;cache.Anchor=item.TextAnchor;cache.Faces=item.Faces;cache.Contours=item.Contours;cache.EvenOdd=item.EvenOdd;cache.Clip=item.ClipPlanes;cache.Gradient=item.Gradient;cache.Fill=item.FillColor;cache.Line=item.LineColor;
                string failureKey=SvgFailureKey(item);if(failureKey!=null&&_negativeSvg.ContainsKey(failureKey))return;
                SurfaceMesh compiled;
                try
                {
                    using(GeometryWork.Begin(200000))
                    {
                        SurfaceMesh source;
                        if(item.SvgSource!=null){ValidateSvgSource(item.SvgSource,item.SvgSegments);var svg=Svg.Parse(item.SvgSource,item.SvgSegments);source=MeshEffects.Solid(svg.Geometry,Vector4.One,Vector4.Zero,svg.Colors);}
                        else{bool generated=item.Faces!=null||item.Contours!=null||item.TextSource!=null;var g=item.Faces!=null?Geometry.Polygons(item.Geometry.Points,item.Faces):item.Contours!=null?PlanarFill.Tessellate(item.Contours,item.EvenOdd):item.TextSource!=null?MeshEffects.Text(item.TextSource,item.TextHeight,item.TextAnchor):item.Geometry;source=MeshEffects.Solid(g,item.FillColor,item.LineColor,generated?null:item.TriangleColors,generated?null:item.EdgeColors,generated?null:item.UV);}
                        MeshEffects.ValidatePlanes(item.ClipPlanes);ValidateGradient(item.Gradient);compiled=StyledMesh(source,item.ClipPlanes,item.Gradient);MeshEffects.ValidateAttributes(compiled);
                    }
                }
                catch(Exception error){cache.Error=error.Message;RememberSvgFailure(failureKey);return;}
                int points,primitives;ActiveRenderCounts(scene,cache,null,false,false,out points,out primitives);ExternalBudgetCount(compiled,ref points,ref primitives);
                if(points>scene.PointBudget||primitives>scene.PrimitiveBudget)
                {ReclaimExternalViews(scene);ActiveRenderCounts(scene,cache,null,false,false,out points,out primitives);ExternalBudgetCount(compiled,ref points,ref primitives);}
                if(points<=scene.PointBudget&&primitives<=scene.PrimitiveBudget){cache.Mesh=compiled;cache.Error=null;}
                else cache.Error="Static geometry exceeds this anchor's configured render budget.";
                return;
            }
        }
        Item ClientDisplayItem(Scene scene,Item item,ref int budget)
        {
            if(!ClientRenderingEnabled)return null;
            string key=PackedKey(item.CallerId,scene.ConsoleId,item.Id);CompiledDisplay cache;
            if(!_clientGeometry.TryGetValue(key,out cache))
            {if(_clientGeometry.Count>=128)return null;cache=new CompiledDisplay{ConsoleId=scene.ConsoleId,CallerId=item.CallerId,Id=item.Id};_clientGeometry.Add(key,cache);}
            cache.LastSeen=_ticks;
            if(!SameCompilation(cache,item))
            {
                cache.PendingItem=item;cache.PendingScene=scene;
                if(_queuedCompile.Add(key))_compileQueue.Enqueue(key);
            }
            if(cache.Mesh==null)return null;
            try{FitsDisplay(cache.Mesh.Geometry,item.Transform,scene);}catch(ArgumentException){return null;}
            return RenderClone(item,cache.Mesh);
        }
    }
}
