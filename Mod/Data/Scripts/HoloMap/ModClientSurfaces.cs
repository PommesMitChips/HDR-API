using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using HDR.Interactions;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        bool ModClientSurfaceCommand(ModClientOwner owner,ModClientContext c,string op,DrawArgs a,out object result)
        {
            result=null;
            if(op=="context-anchor")
            {
                var anchor=a.Nullable<IMyTerminalBlock>();a.End();
                if(anchor!=null)
                {
                    if(!ModClientAnchorCurrent(anchor))throw new ArgumentException("Source anchor requires the actual live working block, accessible and in range of the current viewer.");
                }
                ModClientCancel(c,"context-anchor");c.SourceAnchor=anchor;c.SurfaceRevision++;ModClientInvalidateSourceSlots(c);result=true;return true;
            }
            if(op=="context-surface-clear")
            {a.End();ModClientCancel(c,"context-surface");c.Surface=null;c.SurfaceSourceAspect=0;c.SurfaceRevision++;foreach(var item in c.Items.Values)item.SurfaceMesh=null;ModClientClearSourceSlots(c);result=true;return true;}
            if(op!="context-surface"&&op!="context-mapping"&&op!="context-surface-error"&&op!="context-surface-sided")return false;
            if(c.Hud)throw new ArgumentException("Projected surfaces require a world context.");
            var d=c.Surface==null?new HoloProjectedScreenData{Id="mod",Pose=MatrixValues(MatrixD.Identity),SurfaceSide=1}:CloneScreen(c.Surface,true);
            double aspect=c.SurfaceSourceAspect;
            if(op=="context-surface")
            {
                string kind=a.Text();d.CanvasWidth=a.Number();d.CanvasHeight=a.Number();d.Width=d.CanvasWidth;d.Height=d.CanvasHeight;
                d.SurfaceRadii=null;d.SurfaceMeshPoints=null;d.SurfaceMeshTriangles=null;d.SurfaceMeshUV=null;
                d.SurfaceKind=kind=="plane"?0:kind=="cylinder"?1:kind=="sphere"?2:kind=="ellipsoid"?3:kind=="mesh"?4:-1;
                if(d.SurfaceKind<0)throw new ArgumentException("Surface kind requires plane, cylinder, sphere, ellipsoid or mesh.");
                if(d.SurfaceKind==3){var radii=a.Point();d.SurfaceRadii=new[]{radii.X,radii.Y,radii.Z};d.SurfaceRadius=Math.Max(radii.X,Math.Max(radii.Y,radii.Z));}
                else if(d.SurfaceKind==4)
                {
                    var p=a.Typed<Vector3D[]>();var t=a.Typed<int[]>();var uv=a.Typed<Vector2[]>();SurfaceMapping.ValidateAuthoredMesh(p,t,uv);
                    d.SurfaceMeshPoints=new double[p.Length*3];d.SurfaceMeshUV=new float[uv.Length*2];d.SurfaceMeshTriangles=(int[])t.Clone();d.SurfaceMapping=0;
                    for(int i=0;i<p.Length;i++){d.SurfaceMeshPoints[i*3]=p[i].X;d.SurfaceMeshPoints[i*3+1]=p[i].Y;d.SurfaceMeshPoints[i*3+2]=p[i].Z;d.SurfaceMeshUV[i*2]=uv[i].X;d.SurfaceMeshUV[i*2+1]=uv[i].Y;}
                }
                else if(d.SurfaceKind!=0)d.SurfaceRadius=a.Number(2);
                if(d.SurfaceKind>0&&d.SurfaceKind<4){d.SurfaceHorizontal=a.Number(Math.PI*2);d.SurfaceVertical=a.Number(Math.PI);}
                string side=a.Text(d.SurfaceKind==0||d.SurfaceKind==4?"outside":"inside");d.SurfaceSide=side=="inside"?0:side=="outside"?1:-1;a.End();
            }
            else if(op=="context-mapping")
            {
                if(c.Surface==null)throw new ArgumentException("Declare a context surface before its mapping.");
                string mode=a.Text();d.SurfaceMapping=mode=="angular"?0:mode=="geodesic"?1:mode=="pinhole"?2:-1;d.Camera[9]=a.Number(Math.PI/2);aspect=a.Number(0);a.End();
                if(!Geometry.Finite(aspect)||aspect<0||aspect>100)throw new ArgumentException("Source aspect must be 0 (canvas aspect) or finite in (0,100].");
            }
            else if(op=="context-surface-error"){d.SurfaceError=a.Number();a.End();}
            else{d.TwoSided=a.Flag();d.FrontOpacity=(float)a.Number(1);d.BackOpacity=(float)a.Number(1);a.End();LayerRules.Opacity(d.FrontOpacity);LayerRules.Opacity(d.BackOpacity);}
            double canvasLimit=d.SurfaceKind==0?2000000000000d:50;
            if(!Geometry.Finite(d.CanvasWidth)||!Geometry.Finite(d.CanvasHeight)||d.CanvasWidth<=0||d.CanvasHeight<=0||d.CanvasWidth>canvasLimit||d.CanvasHeight>canvasLimit||d.SurfaceSide<0)throw new ArgumentException("Canvas dimensions require positive finite metres within the selected surface's placement bounds and a supported side.");
            var style=ModClientSurfaceStyle(d,aspect);SurfaceMapping.ValidateStyle(style);
            if(d.SurfaceKind==1&&d.SurfaceMapping==1&&d.CanvasWidth/d.SurfaceRadius>Math.PI*2)throw new ArgumentException("Geodesic cylinder canvas covers at most one turn.");
            var prepared=new Dictionary<ModClientItem,SurfaceMesh>();int points=0,primitives=0;
            foreach(var context in owner.Contexts.Values)foreach(var item in context.Items.Values)
            {
                SurfaceMesh mesh=null;if(ReferenceEquals(context,c)){mesh=ModClientMapSurface(item,item.Transform,d,aspect,0);foreach(var p in mesh.Geometry.Points)if(!ModClientRules.SafePoint(Vector3D.Transform(p,c.Pose)))throw new ArgumentException("Mapped surface placement exceeds client world bounds.");prepared.Add(item,mesh);}
                else if(context.Surface!=null)mesh=ModClientPreparedSurface(context,item);
                var geometry=mesh==null?item.Geometry:mesh.Geometry;points+=geometry.Points.Length;primitives+=geometry.Triangles.Length/3+geometry.Edges.Length/2;
            }
            if(!ModClientRules.FitsAllowance(owner,points,primitives))throw new ArgumentException("Mapped surface exceeds the consumer geometry allowance.");
            foreach(var control in c.Controls.Values)
            {
                ModClientSurfaceControlPose(c.Items[control.Artwork].Transform);
                if(control.Path!=null)for(int i=0;i<control.Path.PointCount;i++){Vector3D p;if(!control.Path.TryGetPoint(i,out p)||Math.Abs(p.Z)>1e-6)throw new ArgumentException("Projected control paths require the XY source plane.");}
                if(control.Rotation!=null&&(Math.Abs(control.Rotation.Pivot.Z)>1e-6||Math.Abs(control.Rotation.Axis.X)>1e-6||Math.Abs(control.Rotation.Axis.Y)>1e-6))throw new ArgumentException("Projected control rotations require a Z axis and Z=0 pivot.");
            }
            ModClientCancel(c,"context-surface");c.Surface=d;c.SurfaceSourceAspect=aspect;c.SurfaceRevision++;
            foreach(var pair in prepared){pair.Key.SurfaceMesh=pair.Value;pair.Key.SurfaceTransform=pair.Key.Transform;pair.Key.SurfaceRevision=c.SurfaceRevision;}
            ModClientDeclare(owner,c);ModClientInvalidateSourceSlots(c);result=true;return true;
        }
        static SurfaceStyle ModClientSurfaceStyle(HoloProjectedScreenData d,double aspect)
        {var style=ScreenSurfaceStyle(d);style.SourceAspect=aspect;return style;}
        static void ModClientSurfaceControlPose(MatrixD pose)
        {
            ModClientRules.Transform(pose);double determinant=pose.M11*pose.M22-pose.M12*pose.M21;
            if(Math.Abs(pose.M13)>1e-8||Math.Abs(pose.M23)>1e-8||Math.Abs(pose.M43)>1e-8||!Geometry.Finite(determinant)||Math.Abs(determinant)<1e-12)throw new ArgumentException("Projected controls require an invertible affine XY source plane at Z=0.");
        }
        static SurfaceMesh ModClientMapSurface(ModClientItem item,MatrixD transform,HoloProjectedScreenData d,double aspect,double depth,Vector4[] colors=null,Vector4[] edgeColors=null,bool preserveDepth=false)
        {
            // A local plane is an affine client drawing frame. It inherits the existing
            // ModClient placement limits, rather than a projected-screen physical size cap.
            if(d.SurfaceKind==0)return ModClientMapPlane(item,transform,d,depth,colors,edgeColors,preserveDepth);
            var source=MeshEffects.Solid(item.Geometry,item.Color,item.Color,colors??item.Colors,edgeColors,item.UV);
            SurfaceMesh transformed;
            if(preserveDepth){var p=new Vector3D[source.Geometry.Points.Length];for(int i=0;i<p.Length;i++)p[i]=Vector3D.Transform(source.Geometry.Points[i],transform);transformed=new SurfaceMesh{Geometry=new Geometry(p,source.Geometry.Edges,source.Geometry.Triangles),Colors=source.Colors,EdgeColors=source.EdgeColors,UV=source.UV};}
            else transformed=TransformCanvasMesh(source,transform);
            var clipped=MeshEffects.Clip(transformed,new[]{new Vector4(1,0,0,(float)(d.CanvasWidth/2)),new Vector4(-1,0,0,(float)(d.CanvasWidth/2)),new Vector4(0,1,0,(float)(d.CanvasHeight/2)),new Vector4(0,-1,0,(float)(d.CanvasHeight/2))});
            var mesh=SurfaceMapping.Warp(clipped,d.CanvasWidth,d.CanvasHeight,ModClientSurfaceStyle(d,aspect),depth);
            if(d.SurfaceKind!=0&&d.SurfaceSide==0&&d.SurfaceKind!=4)for(int i=0;i<mesh.Geometry.Points.Length;i++)mesh.Geometry.Points[i].X=-mesh.Geometry.Points[i].X;
            return mesh;
        }
        sealed class ModClientPlaneBuilder
        {
            readonly List<Vector3D> points=new List<Vector3D>();readonly List<Vector2> uv=new List<Vector2>();readonly List<int> triangles=new List<int>(),edges=new List<int>();readonly List<Vector4> colors=new List<Vector4>(),edgeColors=new List<Vector4>();
            readonly Dictionary<MeshEffects.Vertex,int> indices=new Dictionary<MeshEffects.Vertex,int>();public bool Textured;
            int Point(MeshEffects.Vertex vertex)
            {int index;if(indices.TryGetValue(vertex,out index))return index;if(points.Count>=Geometry.MaxPoints||!ModClientRules.SafePoint(vertex.P))throw new ArgumentException("Local plane exceeds retained point or finite placement bounds.");index=points.Count;points.Add(vertex.P);uv.Add(vertex.UV);indices.Add(vertex,index);return index;}
            void Admit(){GeometryWork.Charge();if(triangles.Count/3+edges.Count/2>=Geometry.MaxPrimitives)throw new ArgumentException("Local plane exceeds retained primitive bounds.");}
            public void Triangle(MeshEffects.Vertex a,MeshEffects.Vertex b,MeshEffects.Vertex c,Vector4 color){Admit();triangles.Add(Point(a));triangles.Add(Point(b));triangles.Add(Point(c));colors.Add(color);}
            public void Line(MeshEffects.Vertex a,MeshEffects.Vertex b,Vector4 color){Admit();edges.Add(Point(a));edges.Add(Point(b));edgeColors.Add(color);}
            public SurfaceMesh Finish(){if(points.Count==0){points.Add(Vector3D.Zero);uv.Add(Vector2.Zero);}return new SurfaceMesh{Geometry=new Geometry(points.ToArray(),edges.ToArray(),triangles.ToArray()),Colors=colors.ToArray(),EdgeColors=edgeColors.ToArray(),UV=Textured?uv.ToArray():null};}
        }
        static double ModClientPlaneDistance(Vector3D point,int axis,double bound,bool greater)
        {double coordinate=axis==0?point.X:point.Y;return greater?coordinate-bound:bound-coordinate;}
        static List<MeshEffects.Vertex> ModClientPlaneClip(List<MeshEffects.Vertex> polygon,int axis,double bound,bool greater)
        {
            GeometryWork.Charge(polygon.Count+1);var output=new List<MeshEffects.Vertex>();if(polygon.Count==0)return output;var previous=polygon[polygon.Count-1];double before=ModClientPlaneDistance(previous.P,axis,bound,greater);
            foreach(var current in polygon){double now=ModClientPlaneDistance(current.P,axis,bound,greater);if((before>=0)!=(now>=0))output.Add(MeshEffects.Lerp(previous,current,before/(before-now)));if(now>=0)output.Add(current);previous=current;before=now;}return output;
        }
        static SurfaceMesh ModClientMapPlane(ModClientItem item,MatrixD transform,HoloProjectedScreenData d,double depth,Vector4[] faceColors,Vector4[] lineColors,bool preserveDepth)
        {
            if(!Geometry.Finite(depth)||Math.Abs(depth)>100000)throw new ArgumentException("Invalid local plane depth.");var g=item.Geometry;var vertices=new MeshEffects.Vertex[g.Points.Length];
            for(int i=0;i<vertices.Length;i++){var p=Vector3D.Transform(g.Points[i],transform);if(!preserveDepth)p.Z=0;p.Z+=depth;if(!ModClientRules.SafePoint(p))throw new ArgumentException("Local plane exceeds finite placement bounds.");vertices[i]=new MeshEffects.Vertex{P=p,UV=item.UV==null?Vector2.Zero:item.UV[i]};}
            var builder=new ModClientPlaneBuilder{Textured=item.UV!=null};var bounds=new[]{-d.CanvasWidth/2,d.CanvasWidth/2,-d.CanvasHeight/2,d.CanvasHeight/2};
            using(GeometryWork.Begin(200000))
            {
                for(int i=0;i<g.Triangles.Length;i+=3)
                {
                    var polygon=new List<MeshEffects.Vertex>{vertices[g.Triangles[i]],vertices[g.Triangles[i+1]],vertices[g.Triangles[i+2]]};for(int plane=0;plane<4;plane++)polygon=ModClientPlaneClip(polygon,plane/2,bounds[plane],plane%2==0);
                    var color=faceColors!=null?faceColors[i/3]:item.Colors==null?item.Color:item.Colors[i/3];for(int k=1;k+1<polygon.Count;k++)builder.Triangle(polygon[0],polygon[k],polygon[k+1],color);
                }
                for(int i=0;i<g.Edges.Length;i+=2)
                {
                    var a=vertices[g.Edges[i]];var b=vertices[g.Edges[i+1]];bool keep=true;
                    for(int plane=0;plane<4;plane++){GeometryWork.Charge();double da=ModClientPlaneDistance(a.P,plane/2,bounds[plane],plane%2==0),db=ModClientPlaneDistance(b.P,plane/2,bounds[plane],plane%2==0);if(da<0&&db<0){keep=false;break;}if(da<0)a=MeshEffects.Lerp(a,b,da/(da-db));else if(db<0)b=MeshEffects.Lerp(a,b,da/(da-db));}
                    if(keep)builder.Line(a,b,lineColors==null?item.Color:lineColors[i/2]);
                }
                return builder.Finish();
            }
        }
        static SurfaceMesh ModClientPreparedSurface(ModClientContext c,ModClientItem item)
        {
            if(item.SurfaceMesh==null||item.SurfaceRevision!=c.SurfaceRevision||item.SurfaceTransform!=item.Transform)
            {item.SurfaceMesh=ModClientMapSurface(item,item.Transform,c.Surface,c.SurfaceSourceAspect,0);item.SurfaceRevision=c.SurfaceRevision;item.SurfaceTransform=item.Transform;}
            return item.SurfaceMesh;
        }
        static void ModClientSurfacePlacement(ModClientOwner owner,ModClientContext c,ModClientItem item,MatrixD transform)
        {
            ModClientRules.Placement(item.Geometry,transform,c.Pose,c.Hud);if(c.Surface==null)return;
            foreach(var control in c.Controls.Values)if(control.Artwork==item.Id)ModClientSurfaceControlPose(transform);
            var mapped=ModClientMapSurface(item,transform,c.Surface,c.SurfaceSourceAspect,0);int points=mapped.Geometry.Points.Length,primitives=mapped.Geometry.Triangles.Length/3+mapped.Geometry.Edges.Length/2;
            foreach(var context in owner.Contexts.Values)foreach(var old in context.Items.Values)
            {if(ReferenceEquals(context,c)&&old.Id==item.Id)continue;var geometry=context.Surface==null?old.Geometry:ModClientPreparedSurface(context,old).Geometry;points+=geometry.Points.Length;primitives+=geometry.Triangles.Length/3+geometry.Edges.Length/2;}
            if(!ModClientRules.FitsAllowance(owner,points,primitives))throw new ArgumentException("Mapped surface exceeds the consumer geometry allowance.");
            foreach(var p in mapped.Geometry.Points)if(!ModClientRules.SafePoint(Vector3D.Transform(p,c.Pose)))throw new ArgumentException("Mapped surface placement exceeds client world bounds.");
            item.SurfaceMesh=mapped;item.SurfaceRevision=c.SurfaceRevision;item.SurfaceTransform=transform;
        }
        static bool ModClientSurfaceRay(ModClientContext c,LocalRay ray,out LocalRay source)
        {
            source=default(LocalRay);if(c.Surface==null){source=ray;return true;}Vector3D hit;Vector2 canvas;if(!ModClientSurfaceHit(c,ray,out hit,out canvas))return false;
            var point=c.Surface.SurfaceKind==0?hit:new Vector3D(canvas.X,canvas.Y,0);return LocalRay.TryFromDirection(new Vector3D(point.X,point.Y,1000),-Vector3D.UnitZ,out source);
        }
        static bool ModClientSurfaceHit(ModClientContext c,LocalRay ray,out Vector3D hit,out Vector2 canvas)
        {
            hit=Vector3D.Zero;canvas=Vector2.Zero;var d=c.Surface;if(d==null)return false;var origin=ray.Origin;var direction=ray.Direction;
            if(d.SurfaceKind==0)
            {
                if(Math.Abs(direction.Z)<1e-12||(direction.Z<0?d.FrontOpacity:d.TwoSided?d.BackOpacity:0)<=0)return false;double distance=-origin.Z/direction.Z;if(!Geometry.Finite(distance)||distance<0||distance>1000000)return false;var point=origin+direction*distance;
                if(Math.Abs(point.X)>d.CanvasWidth/2||Math.Abs(point.Y)>d.CanvasHeight/2||!ModClientSurfaceCrop(d,point))return false;hit=point;canvas=new Vector2((float)point.X,(float)point.Y);return true;
            }
            if(d.SurfaceKind!=0&&d.SurfaceSide==0&&d.SurfaceKind!=4){origin.X=-origin.X;direction.X=-direction.X;}
            Vector2 uv;if(!UiSurfaceHit.TryHit(origin,direction,ModClientSurfaceStyle(d,c.SurfaceSourceAspect),d.CanvasWidth,d.CanvasHeight,1000000,d.FrontOpacity,d.TwoSided?d.BackOpacity:0,out hit,out uv,p=>ModClientSurfaceCrop(d,p)))return false;
            canvas=new Vector2((float)((uv.X-.5)*d.CanvasWidth),(float)((.5-uv.Y)*d.CanvasHeight));
            if(d.SurfaceKind!=0&&d.SurfaceSide==0&&d.SurfaceKind!=4)hit.X=-hit.X;return true;
        }
        static bool ModClientSurfaceCrop(HoloProjectedScreenData d,Vector3D point)
        {
            if(d.SurfaceKind!=0&&d.SurfaceSide==0&&d.SurfaceKind!=4)point.X=-point.X;
            if(!ModClientRules.SafePoint(point,1000000))return false;var clip=d.SurfaceClip;if(clip==null)return true;if(clip.Length%4!=0||clip.Length>32)return false;
            for(int i=0;i<clip.Length;i+=4){double distance=point.X*clip[i]+point.Y*clip[i+1]+point.Z*clip[i+2]-clip[i+3];if(!Geometry.Finite(distance)||distance>1e-7)return false;}return true;
        }
        static void ModClientSurfacePointerMiss(ModClientContext c,bool pressed)
        {
            if(!pressed)c.PointerBlockedUntilRelease=false;
            ModClientRules.PointerHit(c,null,pressed);
            if(!pressed&&c.Capture!=null){var current=c.Capture;ModClientValueEvent(c,"end",current.Control,current.Value);ModClientRules.PointerEvent(c,"up",current.Control.Id);current.Value.Lease=null;c.Capture=null;}
        }
        static bool ModClientSurfacePendingFits(ModClientOwner owner,ModClientContext c)
        {
            int points=0,primitives=0;
            foreach(var context in owner.Contexts.Values)foreach(var item in context.Items.Values)
            {
                Geometry geometry=item.Geometry;
                if(context.Surface!=null)
                {
                    MatrixD pose=item.Transform;if(ReferenceEquals(context,c))foreach(var pending in c.PendingPoses)if(ReferenceEquals(pending.Item,item)){pose=pending.Pose;break;}
                    geometry=ModClientMapSurface(item,pose,context.Surface,context.SurfaceSourceAspect,0).Geometry;
                }
                points+=geometry.Points.Length;primitives+=geometry.Triangles.Length/3+geometry.Edges.Length/2;
            }
            return ModClientRules.FitsAllowance(owner,points,primitives);
        }
        static Vector4[] ModClientSurfaceFaceColors(ModClientItem item,HologramEffectFrame frame,bool edges,double opacity=1)
        {
            var g=item.Geometry;var indices=edges?g.Edges:g.Triangles;int stride=edges?2:3;var colors=new Vector4[indices.Length/stride];
            for(int i=0;i<colors.Length;i++){var point=Vector3D.Zero;for(int k=0;k<stride;k++)point+=g.Points[indices[i*stride+k]];point/=stride;var color=edges||item.Colors==null?item.Color:item.Colors[i];if(item.Effects!=null)color=HologramEffectKernel.Color(item.Effects,frame,color,HologramEffectKernel.Coordinate(item.Effects,point,item.EffectMin,item.EffectMax),edges?g.Triangles.Length/3+i:i);color.W*=(float)opacity;colors[i]=color;}return colors;
        }
        void DrawModClientSurfaceMesh(ModClientContext c,ModClientItem item,SurfaceMesh mesh,Vector3D eye,ref int budget)
        {
            var g=mesh.Geometry;var points=g.WorldPoints;for(int i=0;i<points.Length;i++)points[i]=Vector3D.Transform(g.Points[i],c.Pose);
            int ti=0,ei=0;for(;ti<g.Triangles.Length&&budget>0;ti+=3)
            {int a=g.Triangles[ti],b=g.Triangles[ti+1],z=g.Triangles[ti+2];var color=mesh.Colors[ti/3];bool front;float side=c.Surface.SurfaceKind<3?ScreenSideOpacity(c.Surface,c.Pose,eye):ScreenTriangleOpacity(c.Surface,g.Points[a],g.Points[b],g.Points[z],Vector3D.Transform(eye,MatrixD.Invert(c.Pose)),out front);color.W*=side;ModClientTriangle(points[a],points[b],points[z],mesh.UV==null?Vector2.Zero:mesh.UV[a],mesh.UV==null?Vector2.UnitX:mesh.UV[b],mesh.UV==null?Vector2.UnitY:mesh.UV[z],color,item.Material,false,eye,ref budget);}
            for(;ei<g.Edges.Length&&budget>=2;ei+=2){var color=mesh.EdgeColors[ei/2];color.W*=ScreenSideOpacity(c.Surface,c.Pose,eye);ModClientEffectStrip(points[g.Edges[ei]],points[g.Edges[ei+1]],item.Thickness,color,eye,ref budget);}
            item.EffectBaseComplete=ti==g.Triangles.Length&&ei==g.Edges.Length;
        }
        void DrawModClientSurfaceItem(ModClientContext c,ModClientItem item,Vector3D eye,ref int budget)
        {
            item.EffectBaseComplete=false;var mesh=ModClientPreparedSurface(c,item);
            if(item.Effects!=null){var frame=HologramEffectKernel.Evaluate(item.Effects,_modClientEffectNow,item.EffectStart,item.EffectEntering);mesh=ModClientMapSurface(item,item.Transform,c.Surface,c.SurfaceSourceAspect,0,ModClientSurfaceFaceColors(item,frame,false),ModClientSurfaceFaceColors(item,frame,true));}
            item.EffectBaseCost=mesh.Geometry.Triangles.Length/3+mesh.Geometry.Edges.Length;
            DrawModClientSurfaceMesh(c,item,mesh,eye,ref budget);
        }
        void DrawModClientSurfaceEffects(ModClientContext c,ModClientItem item,MatrixD cameraWorld,Vector3D eye,ref int sharedBudget)
        {
            var s=item.Effects;var frame=HologramEffectKernel.Evaluate(s,_modClientEffectNow,item.EffectStart,item.EffectEntering);var plan=HologramEffectKernel.Plan(s,item.EffectBaseCost,sharedBudget+item.EffectBaseCost);
            if(!plan.BaseAccepted)return;int budget=Math.Min(sharedBudget,Math.Max(0,s.PrimitiveLimit-item.EffectBaseCost)),initial=budget;
            try
            {
                for(int layer=1;layer<=plan.DepthLayers&&budget>0;layer++)
                {double opacity=HologramEffectKernel.LayerOpacity(s,layer);var mesh=ModClientMapSurface(item,item.Transform,c.Surface,c.SurfaceSourceAspect,HologramEffectKernel.LayerOffset(s,layer),ModClientSurfaceFaceColors(item,frame,false,opacity),ModClientSurfaceFaceColors(item,frame,true,opacity));DrawModClientSurfaceMesh(c,item,mesh,eye,ref budget);}
                var tint=item.Colors==null||item.Colors.Length==0?item.Color:item.Colors[0];
                for(int slot=0;slot<plan.Particles&&budget>=2;slot++)
                {
                    HologramEffectParticle particle;if(!HologramEffectKernel.Particle(s,frame,slot,item.EffectMin,item.EffectMax,out particle))continue;var p=particle.Position;var x=Vector3D.UnitX*(particle.Size*.5);var y=Vector3D.UnitY*(particle.Size*.5);var color=HologramEffectKernel.Color(s,frame,tint,HologramEffectKernel.Coordinate(s,p,item.EffectMin,item.EffectMax),slot);color.W*=(float)particle.Opacity;
                    var effect=new ModClientItem{Geometry=new Geometry(new[]{p-x-y,p+x-y,p+x+y,p-x+y},new int[0],new[]{0,1,2,0,2,3}),Color=color};DrawModClientSurfaceMesh(c,effect,ModClientMapSurface(effect,item.Transform,c.Surface,c.SurfaceSourceAspect,0,null,null,true),eye,ref budget);
                }
                for(int slot=0;slot<plan.Beams&&budget>=2;slot++)
                {
                    HologramEffectBeam beam;var g=item.Geometry;int target=(int)((long)slot*g.Points.Length/Math.Max(1,s.Beams));if(!HologramEffectKernel.Beam(s,frame,slot,g.Points[target],out beam))continue;var color=HologramEffectKernel.Color(s,frame,tint,HologramEffectKernel.Coordinate(s,g.Points[target],item.EffectMin,item.EffectMax),slot);color.W*=(float)beam.Opacity;
                    ModClientItem effect;
                    if(item.EffectBeamFan)
                    {var side=g.Points[(target+1)%g.Points.Length]-beam.End;double length=side.Length();if(length<1e-10)continue;side*=beam.Width*.5/length;effect=new ModClientItem{Geometry=new Geometry(new[]{beam.Start,beam.End-side,beam.End+side},new int[0],new[]{0,1,2}),Color=color};}
                    else effect=new ModClientItem{Geometry=Geometry.Wires(new[]{beam.Start,beam.End},new[]{new Vector2I(0,1)}),Color=color,Thickness=beam.Width};
                    DrawModClientSurfaceMesh(c,effect,ModClientMapSurface(effect,item.Transform,c.Surface,c.SurfaceSourceAspect,0,null,null,true),eye,ref budget);
                }
                if((s.ScanStrength>0||s.ScanBoost>0)&&budget>0)
                {
                    double half=s.ScanWidth*.5;ModClientSurfaceScanBand(c,item,frame,frame.ScanPhase-half,frame.ScanPhase+half,eye,ref budget);
                    if(frame.ScanPhase-half<0)ModClientSurfaceScanBand(c,item,frame,frame.ScanPhase-half+1,1,eye,ref budget);
                    if(frame.ScanPhase+half>1)ModClientSurfaceScanBand(c,item,frame,0,frame.ScanPhase+half-1,eye,ref budget);
                }
            }
            finally{sharedBudget-=initial-budget;item.EffectBaseComplete=true;}
        }
        void ModClientSurfaceScanBand(ModClientContext c,ModClientItem item,HologramEffectFrame frame,double low,double high,Vector3D eye,ref int budget)
        {
            low=Math.Max(0,low);high=Math.Min(1,high);if(low>=high)return;var s=item.Effects;var g=item.Geometry;var builder=new MeshEffects.Builder();var a=item.EffectClipA;var b=item.EffectClipB;
            for(int i=0;i<g.Triangles.Length;i+=3)
            {
                for(int k=0;k<3;k++){var p=g.Points[g.Triangles[i+k]];a[k]=new ModClientEffectVertex{Point=p,Coordinate=HologramEffectKernel.Coordinate(s,p,item.EffectMin,item.EffectMax)};}
                int count=ModClientClipBand(a,3,b,low,true);count=ModClientClipBand(b,count,a,high,false);if(count<3)continue;
                var color=HologramEffectKernel.Color(s,frame,item.Colors==null?item.Color:item.Colors[i/3],frame.ScanPhase,i/3);color.W*=(float)Math.Max(s.ScanStrength,Math.Min(1,s.ScanBoost));
                for(int k=1;k<count-1;k++)builder.Triangle(MeshEffects.V(a[0].Point),MeshEffects.V(a[k].Point),MeshEffects.V(a[k+1].Point),color);
            }
            var source=builder.Finish();if(source.Geometry.Triangles.Length==0)return;var effect=new ModClientItem{Geometry=source.Geometry,Colors=source.Colors,Color=Vector4.One};DrawModClientSurfaceMesh(c,effect,ModClientMapSurface(effect,item.Transform,c.Surface,c.SurfaceSourceAspect,0),eye,ref budget);
        }
    }
}
