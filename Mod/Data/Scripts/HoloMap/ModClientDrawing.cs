using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        int _modClientDrawBudget=20000;
        // Offline test interception only; never exposed by the mod delegate service.
        Action<ModClientTriangleSubmission> _modClientOfflineSubmit;
        double _modClientHudDistance=1;
        readonly List<ModClientOwner> _modClientDrawOwners=new List<ModClientOwner>();
        // Test interception is private and cannot be supplied through the public delegate boundary.
        double? _modClientEffectOfflineTime;
        double _modClientEffectNow, _modClientEffectLastTime;
        double ModClientEffectTime()
        {
            double value=_modClientEffectOfflineTime.HasValue?_modClientEffectOfflineTime.Value:MyAPIGateway.Session==null?0:MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;
            if(!Geometry.Finite(value)||value<0||value>1000000000)return _modClientEffectLastTime;
            // Repeated Draw calls and paused simulation sample one retained clock, rather than advancing RNG.
            _modClientEffectLastTime=Math.Max(_modClientEffectLastTime,value);return _modClientEffectLastTime;
        }
        static void ModClientEffectBounds(ModClientItem item)
        {
            var min=item.Geometry.Points[0];var max=min;
            foreach(var p in item.Geometry.Points){min=Vector3D.Min(min,p);max=Vector3D.Max(max,p);}
            item.EffectMin=min;item.EffectMax=max;
            item.EffectBaseCost=item.Geometry.Triangles.Length/3+item.Geometry.Edges.Length;
        }
        void DrawModClientApi(ref int sharedBudget)
        {
            ModClientMaintainSourceSlots();
            if(!_modClientActive||_modClientDrawing||MyAPIGateway.Session==null||MyAPIGateway.Session.Camera==null)return;
            var camera=MyAPIGateway.Session.Camera;var viewport=camera.ViewportSize;
            if(viewport.X<1||viewport.Y<1)return;
            _modClientHudDistance=Math.Max(.0001,camera.NearPlaneDistance*1.001);
            var inverseProjection=MatrixD.Invert(camera.ProjectionMatrix);var cameraWorld=MatrixD.Invert(camera.ViewMatrix);
            _modClientEffectNow=ModClientEffectTime();
            var owners=_modClientDrawOwners;owners.Clear();foreach(var owner in _modClientOwners.Values)if(ModClientHasVisibleItems(owner))owners.Add(owner);owners.Sort((a,b)=>a.Generation.CompareTo(b.Generation));
            if(owners.Count==0||sharedBudget<=0)return;int available=Math.Min(sharedBudget,_modClientDrawBudget);int fair=available/owners.Count;
            _modClientDrawing=true;
            try
            {
                foreach(var owner in owners)
                {
                    if(!ModClientCurrent(owner))continue;int ownerBudget=Math.Min(owner.DrawLimit,fair);
                    var contexts=owner.DrawContexts;contexts.Clear();foreach(var context in owner.Contexts.Values)if(ModClientHasVisibleItems(context))contexts.Add(context);contexts.Sort((a,b)=>a.Order!=b.Order?a.Order.CompareTo(b.Order):a.Handle.CompareTo(b.Handle));
                    int contextShare=contexts.Count==0?0:ownerBudget/contexts.Count;
                    foreach(var context in contexts)
                    {
                        int budget=contextShare;if(!context.Visible||budget<=0)continue;
                        var items=context.DrawItems;items.Clear();foreach(var item in context.Items.Values)items.Add(item);ModClientCollectSourceItems(owner,context,items);items.Sort(ModClientCompositionCompare);
                        foreach(var item in items)
                        {
                            if(!item.Visible||budget<=0||!ModClientSourceItemCurrent(owner,context,item))continue;
                            // One malformed render item cannot disable another consumer or HDR's PB scenes.
                            try{DrawModClientItem(context,item,viewport,inverseProjection,cameraWorld,camera.Position,ref budget);}catch{}
                        }
                        // All retained source content gets the first opportunity at this context's grant.
                        foreach(var item in items)
                        {if(!item.Visible||item.Effects==null||!item.EffectBaseComplete||budget<=0)continue;try{DrawModClientEffects(context,item,viewport,inverseProjection,cameraWorld,camera.Position,ref budget);}catch{}}
                        sharedBudget-=contextShare-budget;
                    }
                }
            }
            finally{_modClientDrawing=false;}
        }
        bool ModClientHasVisibleItems(ModClientOwner owner)
        {foreach(var context in owner.Contexts.Values)if(ModClientHasVisibleItems(context))return true;return false;}
        bool ModClientHasVisibleItems(ModClientContext context)
        {if(!context.Visible)return false;foreach(var item in context.Items.Values)if(item.Visible)return true;return ModClientHasSourceSlots(context);}
        void DrawModClientItem(ModClientContext context,ModClientItem item,Vector2 viewport,MatrixD inverseProjection,MatrixD cameraWorld,Vector3D eye,ref int budget)
        {
            if(context.Surface!=null){DrawModClientSurfaceItem(context,item,eye,ref budget);return;}
            item.EffectBaseComplete=false;
            var g=item.Geometry;var points=g.WorldPoints;
            for(int i=0;i<points.Length;i++)
            {
                var p=Vector3D.Transform(g.Points[i],item.Transform);
                points[i]=context.Hud?ModClientRules.PixelWorld(p,viewport,inverseProjection,cameraWorld,_modClientHudDistance):Vector3D.Transform(p,context.Pose);
                if(!Geometry.Finite(points[i].X)||!Geometry.Finite(points[i].Y)||!Geometry.Finite(points[i].Z))return;
            }
            var effects=item.Effects;var frame=effects==null?default(HologramEffectFrame):HologramEffectKernel.Evaluate(effects,_modClientEffectNow,item.EffectStart,item.EffectEntering);
            int triangleIndex=0,edgeIndex=0;
            for(;triangleIndex<g.Triangles.Length&&budget>0;triangleIndex+=3)
            {
                int i=triangleIndex;
                int ia=g.Triangles[i],ib=g.Triangles[i+1],ic=g.Triangles[i+2];
                var color=item.Colors==null?item.Color:item.Colors[i/3];
                if(effects!=null)color=HologramEffectKernel.Color(effects,frame,color,HologramEffectKernel.Coordinate(effects,(g.Points[ia]+g.Points[ib]+g.Points[ic])/3,item.EffectMin,item.EffectMax),i/3);
                ModClientTriangle(points[ia],points[ib],points[ic],item.UV==null?Vector2.Zero:item.UV[ia],item.UV==null?Vector2.UnitX:item.UV[ib],item.UV==null?Vector2.UnitY:item.UV[ic],color,item.Material,context.Hud,eye,ref budget);
            }
            for(;edgeIndex<g.Edges.Length&&budget>=2;edgeIndex+=2)
            {
                int i=edgeIndex;
                var a=points[g.Edges[i]];var b=points[g.Edges[i+1]];Vector3D offset;
                if(context.Hud)
                {
                    var pa=Vector3D.Transform(g.Points[g.Edges[i]],item.Transform);var pb=Vector3D.Transform(g.Points[g.Edges[i+1]],item.Transform);
                    var d=pb-pa;double length=Math.Sqrt(d.X*d.X+d.Y*d.Y);if(length<1e-9)continue;
                    var pixelOffset=new Vector3D(-d.Y,d.X,0)*(item.Thickness*.5/length);
                    // Unproject a pixel offset so width is exact under off-axis and aspect changes.
                    offset=ModClientRules.PixelWorld(pa+pixelOffset,viewport,inverseProjection,cameraWorld,_modClientHudDistance)-a;
                }
                else
                {var d=b-a;offset=Vector3D.Cross(d,eye-(a+b)*.5);if(offset.LengthSquared()<1e-20)continue;offset=Vector3D.Normalize(offset)*(item.Thickness*.5);}
                var color=item.Color;if(effects!=null)color=HologramEffectKernel.Color(effects,frame,color,HologramEffectKernel.Coordinate(effects,(g.Points[g.Edges[i]]+g.Points[g.Edges[i+1]])*.5,item.EffectMin,item.EffectMax),g.Triangles.Length/3+i/2);
                ModClientTriangle(a-offset,b-offset,b+offset,Vector2.Zero,Vector2.UnitX,Vector2.One,color,null,context.Hud,eye,ref budget);
                ModClientTriangle(a-offset,b+offset,a+offset,Vector2.Zero,Vector2.One,Vector2.UnitY,color,null,context.Hud,eye,ref budget);
            }
            item.EffectBaseComplete=triangleIndex==g.Triangles.Length&&edgeIndex==g.Edges.Length;
        }
        Vector3D ModClientEffectPoint(ModClientContext context,ModClientItem item,Vector3D point,Vector2 viewport,MatrixD inverseProjection,MatrixD cameraWorld)
        {
            if(context.Hud)point.Z=0;
            var local=Vector3D.Transform(point,item.Transform);
            if(context.Surface!=null){var p=SurfaceMapping.MapPoint(local,context.Surface.CanvasWidth,context.Surface.CanvasHeight,ModClientSurfaceStyle(context.Surface,context.SurfaceSourceAspect));if(context.Surface.SurfaceKind!=0&&context.Surface.SurfaceSide==0&&context.Surface.SurfaceKind!=4)p.X=-p.X;return Vector3D.Transform(p,context.Pose);}
            return context.Hud?ModClientRules.PixelWorld(local,viewport,inverseProjection,cameraWorld,_modClientHudDistance):Vector3D.Transform(local,context.Pose);
        }
        void DrawModClientEffects(ModClientContext context,ModClientItem item,Vector2 viewport,MatrixD inverseProjection,MatrixD cameraWorld,Vector3D eye,ref int sharedBudget)
        {
            var s=item.Effects;if(s==null||!item.EffectBaseComplete)return;
            if(context.Surface!=null){DrawModClientSurfaceEffects(context,item,cameraWorld,eye,ref sharedBudget);return;}
            var frame=HologramEffectKernel.Evaluate(s,_modClientEffectNow,item.EffectStart,item.EffectEntering);
            var plan=HologramEffectKernel.Plan(s,item.EffectBaseCost,sharedBudget+item.EffectBaseCost);
            if(!plan.BaseAccepted)return;
            int budget=Math.Min(sharedBudget,Math.Max(0,s.PrimitiveLimit-item.EffectBaseCost)),initial=budget;
            try
            {
            var g=item.Geometry;var tint=item.Colors==null||item.Colors.Length==0?item.Color:item.Colors[0];
            // Ghost shells use the same immutable UVs and source colours, with depth in model units.
            if(!context.Hud)for(int layer=1;layer<=plan.DepthLayers&&budget>0;layer++)
            {
                double offset=HologramEffectKernel.LayerOffset(s,layer),opacity=HologramEffectKernel.LayerOpacity(s,layer);
                var displacement=Vector3D.TransformNormal(new Vector3D(0,0,offset),item.Transform*context.Pose);
                for(int i=0;i<g.Triangles.Length&&budget>0;i+=3)
                {
                    int ia=g.Triangles[i],ib=g.Triangles[i+1],ic=g.Triangles[i+2];
                    var color=HologramEffectKernel.Color(s,frame,item.Colors==null?item.Color:item.Colors[i/3],HologramEffectKernel.Coordinate(s,(g.Points[ia]+g.Points[ib]+g.Points[ic])/3,item.EffectMin,item.EffectMax),i/3);color.W*=(float)opacity;
                    ModClientTriangle(g.WorldPoints[ia]+displacement,g.WorldPoints[ib]+displacement,g.WorldPoints[ic]+displacement,item.UV==null?Vector2.Zero:item.UV[ia],item.UV==null?Vector2.UnitX:item.UV[ib],item.UV==null?Vector2.UnitY:item.UV[ic],color,item.Material,false,eye,ref budget);
                }
                for(int i=0;i<g.Edges.Length&&budget>=2;i+=2)
                {
                    var a=g.WorldPoints[g.Edges[i]]+displacement;var b=g.WorldPoints[g.Edges[i+1]]+displacement;
                    var color=HologramEffectKernel.Color(s,frame,item.Color,HologramEffectKernel.Coordinate(s,(g.Points[g.Edges[i]]+g.Points[g.Edges[i+1]])*.5,item.EffectMin,item.EffectMax),g.Triangles.Length/3+i/2);color.W*=(float)opacity;
                    ModClientEffectStrip(a,b,item.Thickness,color,eye,ref budget);
                }
            }
            for(int slot=0;slot<plan.Particles&&budget>=2;slot++)
            {
                HologramEffectParticle particle;if(!HologramEffectKernel.Particle(s,frame,slot,item.EffectMin,item.EffectMax,out particle))continue;
                Vector3D a,b,c,d;
                if(context.Hud)
                {
                    var p=particle.Position;p.Z=0;var x=Vector3D.UnitX*(particle.Size*.5);var y=Vector3D.UnitY*(particle.Size*.5);
                    a=ModClientEffectPoint(context,item,p-x-y,viewport,inverseProjection,cameraWorld);b=ModClientEffectPoint(context,item,p+x-y,viewport,inverseProjection,cameraWorld);c=ModClientEffectPoint(context,item,p+x+y,viewport,inverseProjection,cameraWorld);d=ModClientEffectPoint(context,item,p-x+y,viewport,inverseProjection,cameraWorld);
                }
                else
                {
                    var p=ModClientEffectPoint(context,item,particle.Position,viewport,inverseProjection,cameraWorld);
                    double eyeDistance=(p-eye).LengthSquared();if(!ModClientRules.SafePoint(p-eye,1000000)||!Geometry.Finite(eyeDistance)||eyeDistance<1e-12)continue;
                    double scale=Math.Sqrt(Vector3D.TransformNormal(Vector3D.UnitX,item.Transform*context.Pose).Length()*Vector3D.TransformNormal(Vector3D.UnitY,item.Transform*context.Pose).Length());
                    var x=cameraWorld.Right*(particle.Size*.5*scale);var y=cameraWorld.Up*(particle.Size*.5*scale);a=p-x-y;b=p+x-y;c=p+x+y;d=p-x+y;
                }
                var color=HologramEffectKernel.Color(s,frame,tint,HologramEffectKernel.Coordinate(s,particle.Position,item.EffectMin,item.EffectMax),g.Triangles.Length/3+g.Edges.Length/2+slot);color.W*=(float)particle.Opacity;
                ModClientTriangle(a,b,c,Vector2.Zero,Vector2.UnitX,Vector2.One,color,null,context.Hud,eye,ref budget);ModClientTriangle(a,c,d,Vector2.Zero,Vector2.One,Vector2.UnitY,color,null,context.Hud,eye,ref budget);
            }
            if(!context.Hud)for(int slot=0;slot<plan.Beams&&budget>=2;slot++)
            {
                HologramEffectBeam beam;int target=(int)((long)slot*g.Points.Length/Math.Max(1,s.Beams));if(!HologramEffectKernel.Beam(s,frame,slot,g.Points[target],out beam))continue;
                var a=ModClientEffectPoint(context,item,beam.Start,viewport,inverseProjection,cameraWorld);var b=ModClientEffectPoint(context,item,beam.End,viewport,inverseProjection,cameraWorld);var color=HologramEffectKernel.Color(s,frame,tint,HologramEffectKernel.Coordinate(s,g.Points[target],item.EffectMin,item.EffectMax),g.Triangles.Length/3+g.Edges.Length/2+s.Particles+slot);color.W*=(float)beam.Opacity;
                if(item.EffectBeamFan)
                {
                    var side=ModClientEffectPoint(context,item,g.Points[(target+1)%g.Points.Length],viewport,inverseProjection,cameraWorld)-b;
                    double length=side.LengthSquared();if(!Geometry.Finite(length)||length<1e-20)continue;
                    side*=beam.Width*.5*Vector3D.TransformNormal(Vector3D.UnitX,item.Transform*context.Pose).Length()/Math.Sqrt(length);
                    ModClientTriangle(a,b-side,b+side,Vector2.Zero,Vector2.UnitX,Vector2.UnitY,color,null,false,eye,ref budget);
                    color.W*=.5f;
                    ModClientTriangle(a,b-side*.25,b+side*.25,Vector2.Zero,Vector2.UnitX,Vector2.UnitY,color,null,false,eye,ref budget);
                }
                else
                {double scale=Vector3D.TransformNormal(Vector3D.UnitX,item.Transform*context.Pose).Length();ModClientEffectStrip(a,b,beam.Width*scale,color,eye,ref budget);}
            }
            // A moving band is clipped to source faces, rather than modifying/rebuilding their cached mesh.
            if((s.ScanStrength>0||s.ScanBoost>0)&&budget>0)
            {
                double half=s.ScanWidth*.5;
                ModClientScanBand(context,item,frame,frame.ScanPhase-half,frame.ScanPhase+half,viewport,inverseProjection,cameraWorld,eye,ref budget);
                if(frame.ScanPhase-half<0)ModClientScanBand(context,item,frame,frame.ScanPhase-half+1,1,viewport,inverseProjection,cameraWorld,eye,ref budget);
                if(frame.ScanPhase+half>1)ModClientScanBand(context,item,frame,0,frame.ScanPhase+half-1,viewport,inverseProjection,cameraWorld,eye,ref budget);
            }
            }
            finally{sharedBudget-=initial-budget;}
        }
        void ModClientEffectStrip(Vector3D a,Vector3D b,double width,Vector4 color,Vector3D eye,ref int budget)
        {
            var side=Vector3D.Cross(b-a,eye-(a+b)*.5);double length=side.LengthSquared();
            if(budget<2||!Geometry.Finite(width)||!Geometry.Finite(length)||length<1e-20)return;
            side*=width*.5/Math.Sqrt(length);
            ModClientTriangle(a-side,b-side,b+side,Vector2.Zero,Vector2.UnitX,Vector2.One,color,null,false,eye,ref budget);
            ModClientTriangle(a-side,b+side,a+side,Vector2.Zero,Vector2.One,Vector2.UnitY,color,null,false,eye,ref budget);
        }
        void ModClientScanBand(ModClientContext context,ModClientItem item,HologramEffectFrame frame,double low,double high,Vector2 viewport,MatrixD inverseProjection,MatrixD cameraWorld,Vector3D eye,ref int budget)
        {
            var s=item.Effects;var g=item.Geometry;var a=item.EffectClipA;var b=item.EffectClipB;
            low=Math.Max(0,low);high=Math.Min(1,high);if(low>=high)return;
            for(int i=0;i<g.Triangles.Length&&budget>0;i+=3)
            {
                for(int k=0;k<3;k++){var p=g.Points[g.Triangles[i+k]];a[k]=new ModClientEffectVertex{Point=p,Coordinate=HologramEffectKernel.Coordinate(s,p,item.EffectMin,item.EffectMax)};}
                int count=ModClientClipBand(a,3,b,low,true);count=ModClientClipBand(b,count,a,high,false);
                if(count<3)continue;var color=HologramEffectKernel.Color(s,frame,item.Colors==null?item.Color:item.Colors[i/3],frame.ScanPhase,i/3);color.W*=(float)Math.Max(s.ScanStrength,Math.Min(1,s.ScanBoost));
                var start=ModClientEffectPoint(context,item,a[0].Point,viewport,inverseProjection,cameraWorld);
                for(int k=1;k<count-1&&budget>0;k++)ModClientTriangle(start,ModClientEffectPoint(context,item,a[k].Point,viewport,inverseProjection,cameraWorld),ModClientEffectPoint(context,item,a[k+1].Point,viewport,inverseProjection,cameraWorld),Vector2.Zero,Vector2.UnitX,Vector2.UnitY,color,null,context.Hud,eye,ref budget);
            }
        }
        static int ModClientClipBand(ModClientEffectVertex[] source,int count,ModClientEffectVertex[] target,double bound,bool greater)
        {
            int n=0;if(count==0)return 0;
            var previous=source[count-1];bool was=greater?previous.Coordinate>=bound:previous.Coordinate<=bound;
            for(int i=0;i<count;i++)
            {
                var current=source[i];bool inside=greater?current.Coordinate>=bound:current.Coordinate<=bound;
                if(inside!=was){double t=(bound-previous.Coordinate)/(current.Coordinate-previous.Coordinate);target[n++]=new ModClientEffectVertex{Point=previous.Point+(current.Point-previous.Point)*t,Coordinate=bound};}
                if(inside)target[n++]=current;previous=current;was=inside;
            }
            return n;
        }
        void ModClientTriangle(Vector3D a,Vector3D b,Vector3D c,Vector2 uvA,Vector2 uvB,Vector2 uvC,Vector4 color,string material,bool hud,Vector3D eye,ref int budget)
        {
            if(budget<=0||color.W<=0)return;Vector3D normal;if(!ModClientRules.Normal(a,b,c,eye,out normal))return;
            if(Vector3D.Dot(normal,eye-a)<0){var p=b;b=c;c=p;var uv=uvB;uvB=uvC;uvC=uv;normal=-normal;}
            var n=(Vector3)normal;budget--;
            if(_modClientOfflineSubmit!=null){_modClientOfflineSubmit(new ModClientTriangleSubmission{A=a,B=b,C=c,UVA=uvA,UVB=uvB,UVC=uvC,Normal=n,Color=Premultiply(color,1),Material=material,Hud=hud});return;}
            MyTransparentGeometry.AddTriangleBillboard(a,b,c,n,n,n,uvA,uvB,uvC,material==null?FillMaterial:MyStringId.GetOrCompute(material),uint.MaxValue,a,Premultiply(color,1),hud?MyBillboard.BlendTypeEnum.PostPP:MyBillboard.BlendTypeEnum.Standard);
        }
    }
}
