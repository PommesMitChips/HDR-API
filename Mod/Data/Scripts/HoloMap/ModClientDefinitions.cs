using System;
using System.Collections.Generic;
using VRage;
using VRageMath;
using HDR.Interactions;

namespace HoloMap
{
    // These implementation types never cross the public delegate boundary.
    public sealed class ModClientItem
    {
        public string Id, Material;
        public Geometry Geometry;
        public Vector4[] Colors;
        public Vector2[] UV;
        public Vector4 Color;
        public double Thickness;
        public MatrixD Transform = MatrixD.Identity;
        public bool Visible = true;
        public long Order;
        public HologramEffectSettings Effects;
        public double EffectStart;
        public bool EffectEntering = true, EffectBaseComplete;
        public bool EffectBeamFan;
        public Vector3D EffectMin, EffectMax;
        public int EffectBaseCost;
        // Fixed clipping scratch belongs to the retained item, never to its immutable geometry.
        public readonly ModClientEffectVertex[] EffectClipA = new ModClientEffectVertex[8];
        public readonly ModClientEffectVertex[] EffectClipB = new ModClientEffectVertex[8];
    }
    public sealed class ModClientContext
    {
        public long Handle;
        public bool Hud, Visible = true, Pressed;
        public int Order;
        public MatrixD Pose = MatrixD.Identity;
        public readonly Dictionary<string, ModClientItem> Items = new Dictionary<string, ModClientItem>();
        public readonly Dictionary<string, Vector4> Bounds = new Dictionary<string, Vector4>();
        public readonly List<string> BoundsOrder = new List<string>();
        public readonly Queue<MyTuple<string,string>> Events = new Queue<MyTuple<string,string>>();
        public string Hover;
        public readonly List<ModClientItem> DrawItems = new List<ModClientItem>();
        public long DeclarationRevision;
        public readonly Dictionary<string,ModClientValue> Values=new Dictionary<string,ModClientValue>();
        public readonly Dictionary<string,ModClientControl> Controls=new Dictionary<string,ModClientControl>();
        public readonly List<string> ControlOrder=new List<string>();
        public readonly List<MyTuple<string,string,string,MyTuple<double,long,long>>> ValueEvents=new List<MyTuple<string,string,string,MyTuple<double,long,long>>>();
        public readonly List<ModClientPoseUpdate> PendingPoses=new List<ModClientPoseUpdate>();
        public ModClientCapture Capture;
        public bool PointerBlockedUntilRelease;
    }
    public sealed class ModClientOwner
    {
        public string Id;
        public long Generation, NextHandle, NextOrder;
        public long ValueRevision, DeclarationRevision;
        public bool Released;
        public int DrawLimit = 4096;
        public readonly Dictionary<long, ModClientContext> Contexts = new Dictionary<long, ModClientContext>();
        public readonly List<ModClientContext> DrawContexts = new List<ModClientContext>();
    }
    public sealed class ModClientValue
    {
        public string Id;
        public NumericRange Range;
        public double Current;
        public long Revision;
        public ModClientControl Lease;
    }
    public sealed class ModClientControl
    {
        public string Id,Artwork,Value;
        public Vector4 Rect;
        public bool Draggable;
        public int Kind;
        public long Revision;
        public MatrixD ReferencePose;
        public double ReferenceValue,AngleMin,AngleMax;
        public PathConstraint Path;
        public RotationConstraint Rotation;
    }
    public sealed class ModClientCapture
    {
        public ModClientControl Control;
        public ModClientValue Value;
        public ModClientItem Item;
        public PathDragState Path;
        public RotationDragState Rotation;
    }
    public struct ModClientPoseUpdate
    {
        public ModClientItem Item;
        public MatrixD Pose;
    }
    public struct ModClientEffectVertex
    {
        public Vector3D Point;
        public double Coordinate;
    }
    public sealed class ModClientTriangleSubmission
    {
        public Vector3D A,B,C;
        public Vector2 UVA,UVB,UVC;
        public Vector3 Normal;
        public Vector4 Color;
        public string Material;
        public bool Hud;
    }
    public static class ModClientRules
    {
        public const int MaxOwners = 16, MaxContexts = 16, MaxItems = 64;
        public const int MaxOwnerPoints = 8192, MaxOwnerPrimitives = 8192, MaxEvents = 64;
        public static string Id(string value)
        {
            if(string.IsNullOrWhiteSpace(value)||value.Length>64)throw new ArgumentException("IDs require 1–64 characters.");
            foreach(char c in value)if(char.IsControl(c))throw new ArgumentException("IDs cannot contain control characters.");
            return value;
        }
        public static void Color(Vector4 c)
        {if(!Geometry.Finite(c.X)||!Geometry.Finite(c.Y)||!Geometry.Finite(c.Z)||!Geometry.Finite(c.W)||c.X<0||c.Y<0||c.Z<0||c.W<0||c.X>1||c.Y>1||c.Z>1||c.W>1)throw new ArgumentException("RGBA must be finite and within 0–1.");}
        public static void Material(string name)
        {Id(name);if(name.StartsWith("HDR_Client",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("HDR_Client materials are private renderer resources; register a consumer-owned material.");}
        public static bool SafePoint(Vector3D p,double bound=1e12)
        {return Geometry.Finite(p.X)&&Geometry.Finite(p.Y)&&Geometry.Finite(p.Z)&&Math.Abs(p.X)<=bound&&Math.Abs(p.Y)<=bound&&Math.Abs(p.Z)<=bound;}
        public static void Transform(MatrixD m)
        {
            if(!SafePoint(m.Translation))throw new ArgumentException("Transform translation exceeds client world bounds.");
            foreach(var value in new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M44})
                if(!Geometry.Finite(value)||Math.Abs(value)>1000000)throw new ArgumentException("Transform coefficients must be finite and bounded.");
            if(!Geometry.Finite(m.Determinant())||Math.Abs(m.Determinant())<1e-15||Math.Abs(m.M14)>1e-12||Math.Abs(m.M24)>1e-12||Math.Abs(m.M34)>1e-12||Math.Abs(m.M44-1)>1e-12)throw new ArgumentException("Transform must be finite nonsingular affine.");
        }
        public static void Placement(Geometry geometry,MatrixD transform,MatrixD pose,bool hud)
        {
            Transform(transform);Transform(pose);
            foreach(var point in geometry.Points)
            {
                var local=Vector3D.Transform(point,transform);
                if(!SafePoint(local)||hud&&Math.Abs(local.Z)>1e-6||!SafePoint(Vector3D.Transform(local,pose)))throw new ArgumentException("Geometry placement must remain finite and bounded; HUD Z must be zero.");
            }
        }
        public static bool Normal(Vector3D a,Vector3D b,Vector3D c,Vector3D eye,out Vector3D normal)
        {
            normal=Vector3D.Zero;if(!SafePoint(eye)||!SafePoint(a)||!SafePoint(b)||!SafePoint(c)||!SafePoint(a-eye,1000000)||!SafePoint(b-eye,1000000)||!SafePoint(c-eye,1000000))return false;
            normal=Vector3D.Cross(b-a,c-a);double length=normal.LengthSquared();if(!Geometry.Finite(length)||length<1e-40)return false;
            normal/=Math.Sqrt(length);return SafePoint(normal,1.000001);
        }
        public static ModClientItem Mesh(string id, Vector3D[] points, int[] triangles, Vector4 color, Vector2[] uv=null, string material=null)
        {
            Id(id);Color(color);Geometry.ValidatePoints(points);
            if(triangles==null||triangles.Length==0||triangles.Length%3!=0||triangles.Length/3>Geometry.MaxPrimitives)throw new ArgumentException("Supply bounded triangle indices.");
            var p=(Vector3D[])points.Clone();var t=(int[])triangles.Clone();
            foreach(int i in t)if(i<0||i>=p.Length)throw new ArgumentException("Triangle index outside points.");
            if((uv==null)!=(material==null))throw new ArgumentException("Supply material and UVs together.");
            Vector2[] copy=null;
            if(uv!=null){Material(material);if(uv.Length!=p.Length)throw new ArgumentException("UV count must match points.");copy=(Vector2[])uv.Clone();foreach(var u in copy)if(!Geometry.Finite(u.X)||!Geometry.Finite(u.Y)||u.X<0||u.X>1||u.Y<0||u.Y>1)throw new ArgumentException("UVs must be within 0–1.");}
            return new ModClientItem{Id=id,Geometry=new Geometry(p,new int[0],t),Color=color,UV=copy,Material=material};
        }
        public static void Admit(ModClientOwner owner, ModClientContext context, ModClientItem item)
        {
            if(!context.Items.ContainsKey(item.Id)&&context.Items.Count>=MaxItems)throw new ArgumentException("Context item limit reached.");
            int points=item.Geometry.Points.Length, primitives=item.Geometry.Triangles.Length/3+item.Geometry.Edges.Length/2;
            foreach(var c in owner.Contexts.Values)foreach(var i in c.Items.Values)
            {if(ReferenceEquals(c,context)&&i.Id==item.Id)continue;points+=i.Geometry.Points.Length;primitives+=i.Geometry.Triangles.Length/3+i.Geometry.Edges.Length/2;}
            if(points>MaxOwnerPoints||primitives>MaxOwnerPrimitives)throw new ArgumentException("Consumer retained geometry limit reached.");
        }
        public static void Bound(Vector4 rect)
        {if(!Geometry.Finite(rect.X)||!Geometry.Finite(rect.Y)||!Geometry.Finite(rect.Z)||!Geometry.Finite(rect.W)||rect.Z<=0||rect.W<=0||Math.Abs(rect.X)>1000000||Math.Abs(rect.Y)>1000000||rect.Z>1000000||rect.W>1000000)throw new ArgumentException("Bounds require finite x/y and positive width/height.");}
        public static string Hit(ModClientContext context, double x, double y)
        {
            if(!context.Visible||!Geometry.Finite(x)||!Geometry.Finite(y))return null;
            for(int i=context.BoundsOrder.Count-1;i>=0;i--){var id=context.BoundsOrder[i];var b=context.Bounds[id];if(x>=b.X&&y>=b.Y&&x<=b.X+b.Z&&y<=b.Y+b.W)return id;}
            return null;
        }
        static void Event(ModClientContext context,string kind,string id)
        {if(context.Events.Count>=MaxEvents)context.Events.Dequeue();context.Events.Enqueue(new MyTuple<string,string>(kind,id));}
        public static void Pointer(ModClientContext context,double x,double y,bool pressed)
        {
            if(!Geometry.Finite(x)||!Geometry.Finite(y))throw new ArgumentException("Pointer coordinates must be finite.");
            if(!context.Visible){context.Hover=null;context.Pressed=false;return;}
            PointerHit(context,Hit(context,x,y),pressed);
        }
        public static void PointerHit(ModClientContext context,string hit,bool pressed)
        {
            if(!context.Visible){context.Hover=null;context.Pressed=false;return;}
            if(hit!=context.Hover){if(context.Hover!=null)Event(context,"leave",context.Hover);if(hit!=null)Event(context,"enter",hit);context.Hover=hit;}
            if(pressed&&!context.Pressed&&hit!=null)Event(context,"click",hit);context.Pressed=pressed;
        }
        public static void PointerEvent(ModClientContext context,string kind,string id){Event(context,kind,id);}
        // Screen pixel coordinates: origin top left, +X right, +Y down. HUD Z is zero.
        public static Vector3D PixelWorld(Vector3D pixel,Vector2 viewport,MatrixD inverseProjection,MatrixD cameraWorld,double planeDistance=1)
        {
            if(viewport.X<1||viewport.Y<1)throw new ArgumentException("Viewport is empty.");
            // Mid-depth works for both normal and reverse-Z / infinite-far perspective matrices.
            var v=Vector4D.Transform(new Vector4D(pixel.X/viewport.X*2-1,1-pixel.Y/viewport.Y*2,.5,1),inverseProjection);
            if(!Geometry.Finite(v.W)||Math.Abs(v.W)<1e-15)throw new ArgumentException("Projection cannot unproject HUD.");
            if(!Geometry.Finite(planeDistance)||planeDistance<=0||planeDistance>100)throw new ArgumentException("HUD plane distance must be finite and positive.");
            // Preserve off-axis perspective including projection shear; production uses just beyond near clip.
            var p=new Vector3D(v.X/v.W,v.Y/v.W,v.Z/v.W);if(p.Z>=-1e-9)throw new ArgumentException("HUD requires a perspective camera.");
            return Vector3D.Transform(p*(planeDistance/(-p.Z)),cameraWorld);
        }
    }
}
