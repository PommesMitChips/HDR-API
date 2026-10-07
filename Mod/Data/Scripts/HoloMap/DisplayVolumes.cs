using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using VRageRender;
using PbBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace HoloMap
{
    // Convex display envelope. Points and UVs are clipped together on the viewer.
    public sealed class DisplayVolume
    {
        public readonly MatrixD Inverse;
        public readonly Vector4D[] Planes;
        readonly List<Vertex> _clipA = new List<Vertex>(32), _clipB = new List<Vertex>(32);
        static readonly Vector4D[] UnitRangePlanes = BuildRange(1);
        public DisplayVolume(MatrixD world, Vector4D[] planes) { Inverse=MatrixD.Invert(world); Planes=planes; }
        double Distance(Vector3D point, Vector4D p) { return point.X*p.X+point.Y*p.Y+point.Z*p.Z-p.W; }
        public bool Contains(Vector3D world)
        {
            var p=Vector3D.Transform(world,Inverse);
            foreach(var plane in Planes)if(Distance(p,plane)>1e-8)return false;
            return true;
        }
        public bool ContainsBox(BoundingBoxD world)
        {
            foreach(var p in world.GetCorners())if(!Contains(p))return false;
            return true;
        }
        public bool ClipLine(ref Vector3D a,ref Vector3D b)
        {
            var pa=Vector3D.Transform(a,Inverse);var pb=Vector3D.Transform(b,Inverse);
            double lo=0,hi=1;
            foreach(var plane in Planes)
            {
                double da=Distance(pa,plane),db=Distance(pb,plane);
                if(da>0&&db>0)return false;
                if(da<=0&&db<=0)continue;
                double t=da/(da-db);
                if(da>0)lo=Math.Max(lo,t);else hi=Math.Min(hi,t);
                if(lo>hi)return false;
            }
            var delta=b-a;b=a+delta*hi;a+=delta*lo;return true;
        }
        public struct Vertex
        {
            public Vector3D Position;public Vector2 UV;
            public Vertex(Vector3D p,Vector2 uv){Position=p;UV=uv;}
        }
        public List<Vertex> ClipTriangle(Vertex a,Vertex b,Vertex c)
        { int work=10000;return ClipTriangle(a,b,c,ref work); }
        public List<Vertex> ClipTriangle(Vertex a,Vertex b,Vertex c,ref int work)
        {
            var polygon=_clipA;var output=_clipB;polygon.Clear();output.Clear();polygon.Add(a);polygon.Add(b);polygon.Add(c);
            foreach(var plane in Planes)
            {
                output.Clear();if(polygon.Count==0)break;
                var prior=polygon[polygon.Count-1];double dp=Distance(Vector3D.Transform(prior.Position,Inverse),plane);
                foreach(var next in polygon)
                {
                    if(work--<=0){polygon.Clear();return polygon;}
                    double dn=Distance(Vector3D.Transform(next.Position,Inverse),plane);
                    if((dp<=0)!=(dn<=0))
                    {
                        double t=dp/(dp-dn);
                        output.Add(new Vertex(prior.Position+(next.Position-prior.Position)*t,prior.UV+(next.UV-prior.UV)*(float)t));
                    }
                    if(dn<=0)output.Add(next);
                    prior=next;dp=dn;
                }
                var swap=polygon;polygon=output;output=swap;
            }
            return polygon;
        }
        public static Vector4D[] Frustum(double bottom,double height,double lowerX,double lowerZ,double upperX,double upperZ)
        {
            double sx=(upperX-lowerX)/height,sz=(upperZ-lowerZ)/height;
            return new[]{new Vector4D(0,-1,0,-bottom),new Vector4D(0,1,0,bottom+height),
                new Vector4D(1,-sx,0,lowerX-sx*bottom),new Vector4D(-1,-sx,0,lowerX-sx*bottom),
                new Vector4D(0,-sz,1,lowerZ-sz*bottom),new Vector4D(0,-sz,-1,lowerZ-sz*bottom)};
        }
        // An inscribed icosahedron keeps every drawn point inside the configured
        // spherical range; clipping cannot introduce geometry outside the radius.
        public static Vector4D[] Range(double radius)
        {
            var planes=new Vector4D[UnitRangePlanes.Length];for(int i=0;i<planes.Length;i++){var p=UnitRangePlanes[i];p.W*=radius;planes[i]=p;}return planes;
        }
        static Vector4D[] BuildRange(double radius)
        {
            double phi=(1+Math.Sqrt(5))/2;var vertices=new List<Vector3D>();
            foreach(int a in new[]{-1,1})foreach(int b in new[]{-1,1})
            {vertices.Add(Vector3D.Normalize(new Vector3D(0,a,b*phi))*radius);vertices.Add(Vector3D.Normalize(new Vector3D(a,b*phi,0))*radius);vertices.Add(Vector3D.Normalize(new Vector3D(b*phi,0,a))*radius);}
            var planes=new List<Vector4D>();
            for(int a=0;a<vertices.Count;a++)for(int b=a+1;b<vertices.Count;b++)for(int c=b+1;c<vertices.Count;c++)
            {
                var normal=Vector3D.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);if(normal.LengthSquared()<1e-16)continue;
                normal.Normalize();double d=Vector3D.Dot(normal,vertices[a]);if(d<0){normal=-normal;d=-d;}
                bool face=true;foreach(var v in vertices)if(Vector3D.Dot(normal,v)>d+1e-8){face=false;break;}
                if(face)planes.Add(new Vector4D(normal.X,normal.Y,normal.Z,d));
            }
            return planes.ToArray();
        }
    }
    public sealed partial class HoloMapSession
    {
        int _volumeClipWork;
        MyTuple<bool,string> SetDisplayRange(PbBlock caller,PbBlock target,double range)
        {
            return Guard(()=>{var block=Authorize(caller,target);if(!(block is IMyProjector))throw new ArgumentException("Range limits require a Console or Projector.");if(!Geometry.Finite(range)||range<0||range>25)throw new ArgumentException("Range must be 0 (disabled) through 25 meters.");GetScene(block.EntityId).VolumeRange=range;});
        }
        MyTuple<bool,string> SetTableVolume(PbBlock caller,PbBlock target,bool enabled,double height,double lowerX,double lowerZ,double upperX,double upperZ,double bottom)
        {
            return Guard(()=>{var block=Authorize(caller,target);if(!(block is IMyProjector))throw new ArgumentException("Table volumes require a Console/Holo table.");if((block.BlockDefinition.SubtypeName??"").IndexOf("Console",StringComparison.OrdinalIgnoreCase)<0){if(enabled)throw new ArgumentException("Table volumes require a Console/Holo table.");GetScene(block.EntityId).TableVolumeEnabled=false;return;}foreach(var value in new[]{height,lowerX,lowerZ,upperX,upperZ,bottom})if(!Geometry.Finite(value)||Math.Abs(value)>25)throw new ArgumentException("Volume dimensions must be finite and at most 25 meters.");
                if(height<=0||lowerX<=0||lowerZ<=0||upperX<lowerX||upperZ<lowerZ)throw new ArgumentException("Volume height and lower half widths must be positive; upper half widths must be at least the lower widths.");
                var scene=GetScene(block.EntityId);scene.TableVolumeEnabled=enabled;scene.TableVolume=new[]{bottom,height,lowerX,lowerZ,upperX,upperZ};});
        }
        DisplayVolume GetDisplayVolume(Scene scene,IMyTerminalBlock block)
        {
            if(!(block is IMyProjector))return null;
            string subtype=block.BlockDefinition.SubtypeName??"";
            if(subtype.IndexOf("Console",StringComparison.OrdinalIgnoreCase)>=0)
            {
                if(!scene.TableVolumeEnabled)return scene.VolumeRange<=0?null:new DisplayVolume(MatrixD.CreateTranslation(scene.Offset)*block.WorldMatrix,DisplayVolume.Range(scene.VolumeRange));
                var v=scene.TableVolume;
                if(v==null)
                {
                    var box=block.LocalAABB;double x=Math.Max(.25,(box.Max.X-box.Min.X)*.45),z=Math.Max(.25,(box.Max.Z-box.Min.Z)*.45);
                    v=new[]{0.1,2.0,x,z,x*1.8,z*1.8};
                }
                var planes=new List<Vector4D>(DisplayVolume.Frustum(v[0],v[1],v[2],v[3],v[4],v[5]));
                if(scene.VolumeRange>0)foreach(var plane in DisplayVolume.Range(scene.VolumeRange)){var centered=plane;centered.W+=Vector3D.Dot(new Vector3D(plane.X,plane.Y,plane.Z),scene.Offset);planes.Add(centered);}
                return new DisplayVolume(block.WorldMatrix,planes.ToArray());
            }
            return scene.VolumeRange<=0?null:new DisplayVolume(MatrixD.CreateTranslation(scene.Offset)*block.WorldMatrix,DisplayVolume.Range(scene.VolumeRange));
        }
        void DrawVolumeTriangle(DisplayVolume volume,Vector3D a,Vector3D b,Vector3D c,Vector2 uvA,Vector2 uvB,Vector2 uvC,Vector4 fillColor,float emission,bool shaded,string material,MatrixD consoleWorld,MatrixD inverse,Vector3D camera,uint anchorId,ref int budget)
        {
            if(budget<=0)return;
            if(!FiniteDisplayPoint(a)||!FiniteDisplayPoint(b)||!FiniteDisplayPoint(c))return;
            Vector3D entryNormal;if(!ModClientRules.Normal(a,b,c,camera,out entryNormal))return;
            budget--;
            var polygon=volume==null?null:volume.ClipTriangle(new DisplayVolume.Vertex(a,uvA),new DisplayVolume.Vertex(b,uvB),new DisplayVolume.Vertex(c,uvC),ref _volumeClipWork);
            for(int i=1;i+1<(polygon==null?3:polygon.Count)&&budget>0;i++)
            {
                if(polygon!=null){a=polygon[0].Position;b=polygon[i].Position;c=polygon[i+1].Position;uvA=polygon[0].UV;uvB=polygon[i].UV;uvC=polygon[i+1].UV;}
                var n=Vector3D.Cross(b-a,c-a);double normalLength=n.LengthSquared();if(!Geometry.Finite(normalLength)||normalLength<1e-24)continue;n.Normalize();
                if(Vector3D.Dot(n,camera-a)<0){var swap=b;b=c;c=swap;var uv=uvB;uvB=uvC;uvC=uv;n=-n;}
                var light=Vector3D.Normalize(Vector3D.TransformNormal(new Vector3D(.4,.8,.3),consoleWorld));
                float brightness=shaded?(float)(.25+.75*Math.Abs(Vector3D.Dot(n,light))):1;
                var color=Premultiply(fillColor,brightness+emission);var origin=a;
                if(anchorId!=uint.MaxValue){a=Vector3D.Transform(a,inverse);b=Vector3D.Transform(b,inverse);c=Vector3D.Transform(c,inverse);n=Vector3D.TransformNormal(n,inverse);}
                if(!ModClientRules.SafePoint(a,anchorId==uint.MaxValue?1e12:1000000)||!ModClientRules.SafePoint(b,anchorId==uint.MaxValue?1e12:1000000)||!ModClientRules.SafePoint(c,anchorId==uint.MaxValue?1e12:1000000)||!ModClientRules.SafePoint(n,1000000)||!Geometry.Finite(color.X)||!Geometry.Finite(color.Y)||!Geometry.Finite(color.Z)||!Geometry.Finite(color.W)||!Geometry.Finite(uvA.X)||!Geometry.Finite(uvA.Y)||!Geometry.Finite(uvB.X)||!Geometry.Finite(uvB.Y)||!Geometry.Finite(uvC.X)||!Geometry.Finite(uvC.Y))continue;
                budget--;
                var normal=(Vector3)n;MyTransparentGeometry.AddTriangleBillboard(a,b,c,normal,normal,normal,uvA,uvB,uvC,material==null?FillMaterial:MyStringId.GetOrCompute(material),anchorId,origin,color,MyBillboard.BlendTypeEnum.Standard);
            }
        }
        static bool FiniteDisplayPoint(Vector3D point)
        {return Geometry.Finite(point.X)&&Geometry.Finite(point.Y)&&Geometry.Finite(point.Z);}
    }
}
