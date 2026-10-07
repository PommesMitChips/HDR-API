using System;
using VRageMath;
using VRage.Game;
using VRageRender;
namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        struct HologramEffectState
        {
            public HologramEffectSettings Settings;public HologramEffectFrame Frame;public Vector3D Min,Max;
        }
        static void HologramEffectBounds(Geometry geometry,out Vector3D min,out Vector3D max)
        {
            min=max=geometry.Points.Length==0?Vector3D.Zero:geometry.Points[0];
            foreach(var p in geometry.Points){min=Vector3D.Min(min,p);max=Vector3D.Max(max,p);}
        }
        HologramEffectState HologramEffectsFor(Item item,Geometry geometry)
        {
            var state=new HologramEffectState{Settings=item==null?null:item.Effects};
            if(state.Settings!=null){state.Frame=HologramEffectKernel.Evaluate(state.Settings,HologramEffectNow,item.EffectStart,item.EffectEntering);HologramEffectBounds(geometry,out state.Min,out state.Max);}
            return state;
        }
        static Vector4 HologramEffectColor(HologramEffectState state,Vector4 color,Vector3D point,int primitive)
        {return state.Settings==null?color:HologramEffectKernel.Color(state.Settings,state.Frame,color,HologramEffectKernel.Coordinate(state.Settings,point,state.Min,state.Max),primitive);}
        static HologramEffectPlan HologramEffectDrawPlan(Item item,int remaining)
        {
            int count=item.Geometry.Triangles.Length/3*2+item.Geometry.Edges.Length/2;
            // Effects cannot make the canonical content disappear when their own allowance is small.
            return HologramEffectKernel.Plan(item.Effects,count,remaining>int.MaxValue-count?int.MaxValue:remaining+count,4,4);
        }
        readonly Vector3D[] _effectPolygonA=new Vector3D[8],_effectPolygonB=new Vector3D[8];
        int ClipHologramBand(Vector3D[] source,int count,Vector3D[] output,HologramEffectState state,double threshold,bool upper)
        {
            int written=0;if(count==0)return 0;var previous=source[count-1];double p=HologramEffectKernel.Coordinate(state.Settings,previous,state.Min,state.Max)-threshold;
            for(int i=0;i<count;i++)
            {var current=source[i];double q=HologramEffectKernel.Coordinate(state.Settings,current,state.Min,state.Max)-threshold;bool a=upper?p<=0:p>=0,b=upper?q<=0:q>=0;if(a!=b)output[written++]=previous+(current-previous)*(p/(p-q));if(b)output[written++]=current;previous=current;p=q;}
            return written;
        }
        void DrawHologramRefreshBar(Item item,HologramEffectState state,MatrixD matrix,DisplayVolume volume,float opacity,MatrixD world,MatrixD inverse,Vector3D camera,uint parent,ref int budget)
        {
            if(state.Settings.ScanStrength==0&&state.Settings.ScanBoost==0||item.Material!=null)return;
            double low=Math.Max(0,state.Frame.ScanPhase-state.Settings.ScanWidth*.5),high=Math.Min(1,state.Frame.ScanPhase+state.Settings.ScanWidth*.5);var g=item.Geometry;
            for(int i=0;i<g.Triangles.Length&&budget>0;i+=3)
            {
                if(_volumeClipWork<8)break;_volumeClipWork-=8;
                _effectPolygonA[0]=g.Points[g.Triangles[i]];_effectPolygonA[1]=g.Points[g.Triangles[i+1]];_effectPolygonA[2]=g.Points[g.Triangles[i+2]];
                int count=ClipHologramBand(_effectPolygonA,3,_effectPolygonB,state,low,false);count=ClipHologramBand(_effectPolygonB,count,_effectPolygonA,state,high,true);if(count<3)continue;
                var color=item.TriangleColors==null?item.FillColor:item.TriangleColors[i/3];color=HologramEffectColor(state,color,(_effectPolygonA[0]+_effectPolygonA[1]+_effectPolygonA[2])/3,i/3);color.W*=(float)(opacity*Math.Max(state.Settings.ScanStrength,.15));
                var offset=Vector3D.TransformNormal(new Vector3D(0,0,.0002),matrix);var a=Vector3D.Transform(_effectPolygonA[0],matrix)+offset;
                for(int j=1;j+1<count&&budget>0;j++)DrawVolumeTriangle(volume,a,Vector3D.Transform(_effectPolygonA[j],matrix)+offset,Vector3D.Transform(_effectPolygonA[j+1],matrix)+offset,Vector2.Zero,Vector2.UnitX,Vector2.UnitY,color,item.Emission,false,null,world,inverse,camera,parent,ref budget);
            }
        }
        void DrawHologramItem(Item item,MatrixD matrix,DisplayVolume volume,float opacity,MatrixD world,MatrixD inverse,Vector3D camera,uint parent,ref int budget)
        {
            var g=item.Geometry;var effects=HologramEffectsFor(item,g);
            for(int i=0;i<g.Points.Length;i++)g.WorldPoints[i]=Vector3D.Transform(g.Points[i],matrix);
            for(int i=0;i<g.Triangles.Length&&budget>0;i+=3)
            {
                int ia=g.Triangles[i],ib=g.Triangles[i+1],ic=g.Triangles[i+2];var color=item.TriangleColors==null?item.FillColor:item.TriangleColors[i/3];
                color=HologramEffectColor(effects,color,(g.Points[ia]+g.Points[ib]+g.Points[ic])/3,i/3);color.W*=opacity;if(color.W<=0)continue;
                DrawVolumeTriangle(volume,g.WorldPoints[ia],g.WorldPoints[ib],g.WorldPoints[ic],item.UV==null?Vector2.Zero:item.UV[ia],item.UV==null?Vector2.UnitX:item.UV[ib],item.UV==null?Vector2.UnitY:item.UV[ic],color,item.Emission,item.Shaded,item.Material,world,inverse,camera,parent,ref budget);
            }
            for(int i=0;i<g.Edges.Length&&budget>0;i+=2)
            {
                int ia=g.Edges[i],ib=g.Edges[i+1];var color=item.EdgeColors==null?item.LineColor:item.EdgeColors[i/2];
                color=HologramEffectColor(effects,color,(g.Points[ia]+g.Points[ib])/2,g.Triangles.Length/3+i/2);color.W*=opacity;if(color.W<=0)continue;
                var a=g.WorldPoints[ia];var b=g.WorldPoints[ib];if(volume!=null){if(_volumeClipWork<volume.Planes.Length)continue;_volumeClipWork-=volume.Planes.Length;if(!volume.ClipLine(ref a,ref b))continue;}
                Vector3 direction;float length;if(!HologramSafeLine(a,b,camera,out direction,out length))continue;var lineColor=Premultiply(color,1+item.Emission);if(!Geometry.Finite(lineColor.X)||!Geometry.Finite(lineColor.Y)||!Geometry.Finite(lineColor.Z)||!Geometry.Finite(lineColor.W))continue;budget--;
                MyTransparentGeometry.AddLineBillboard(LineMaterial,lineColor,a,parent,ref inverse,direction,length,item.Thickness,MyBillboard.BlendTypeEnum.Standard);
            }
        }
        void DrawHologramExtras(Item item,MatrixD matrix,DisplayVolume volume,float opacity,MatrixD world,MatrixD inverse,Vector3D camera,uint parent,ref int budget)
        {
            if(item.Effects==null||budget<=0||opacity<=0)return;var state=HologramEffectsFor(item,item.Geometry);var plan=HologramEffectDrawPlan(item,budget);if(!plan.BaseAccepted)return;
            int allowance=Math.Min(budget,Math.Max(0,item.Effects.PrimitiveLimit-(item.Geometry.Triangles.Length/3*2+item.Geometry.Edges.Length/2)));
            int original=budget;budget=allowance;try
            {
                DrawHologramRefreshBar(item,state,matrix,volume,opacity,world,inverse,camera,parent,ref budget);
                for(int layer=1;layer<=plan.DepthLayers&&budget>0;layer++)
                {var ghost=matrix;ghost.Translation+=Vector3D.TransformNormal(new Vector3D(0,0,HologramEffectKernel.LayerOffset(item.Effects,layer)),matrix);DrawHologramItem(item,ghost,volume,opacity*(float)HologramEffectKernel.LayerOpacity(item.Effects,layer),world,inverse,camera,parent,ref budget);}
                var color=item.FillColor.W>0?item.FillColor:item.LineColor; if(color.W<=0)color=new Vector4(0,.8f,1,1);
                for(int slot=0;slot<plan.Particles&&budget>=4;slot++)
                {
                    HologramEffectParticle particle;if(!HologramEffectKernel.Particle(item.Effects,state.Frame,slot,state.Min,state.Max,out particle))continue;
                    var center=Vector3D.Transform(particle.Position,matrix);Vector3D a,b,vertexC,d;if(!HologramParticleQuad(camera,center,particle.Size*matrix.Right.Length(),out a,out b,out vertexC,out d))continue;var c=HologramEffectColor(state,color,particle.Position,slot);c.W*=(float)(opacity*particle.Opacity);
                    DrawVolumeTriangle(volume,a,b,vertexC,Vector2.Zero,Vector2.UnitX,Vector2.One,c,item.Emission,false,null,world,inverse,camera,parent,ref budget);
                    if(budget>=2)DrawVolumeTriangle(volume,a,vertexC,d,Vector2.Zero,Vector2.One,Vector2.UnitY,c,item.Emission,false,null,world,inverse,camera,parent,ref budget);
                }
                for(int slot=0;slot<plan.Beams&&budget>=4;slot++)
                {
                    var points=item.Geometry.Points;if(points.Length==0)break;var target=HologramBeamTarget(item,slot);HologramEffectBeam beam;if(!HologramEffectKernel.Beam(item.Effects,state.Frame,slot,target,out beam))continue;
                    var start=Vector3D.Transform(beam.Start,matrix);var end=Vector3D.Transform(beam.End,matrix);var axis=end-start;var side=Vector3D.Cross(axis,camera-(start+end)*.5);if(side.LengthSquared()<1e-20)continue;side.Normalize();side*=beam.Width*.5*matrix.Right.Length();var c=HologramEffectColor(state,color,target,slot);c.W*=(float)(opacity*beam.Opacity);
                    DrawVolumeTriangle(volume,start-side*.1,end-side,end+side,Vector2.Zero,Vector2.UnitX,Vector2.One,c,item.Emission,false,null,world,inverse,camera,parent,ref budget);
                    if(budget>=2)DrawVolumeTriangle(volume,start-side*.1,end+side,start+side*.1,Vector2.Zero,Vector2.One,Vector2.UnitY,c,item.Emission,false,null,world,inverse,camera,parent,ref budget);
                }
            }finally{budget=original-(allowance-budget);}
        }
        static bool HologramParticleQuad(Vector3D camera,Vector3D center,double size,out Vector3D a,out Vector3D b,out Vector3D c,out Vector3D d)
        {
            a=b=c=d=Vector3D.Zero;if(!FiniteDisplayPoint(camera)||!FiniteDisplayPoint(center)||!Geometry.Finite(size)||size<=0||size>1e9)return false;
            var look=center-camera;double length=look.LengthSquared();if(!Geometry.Finite(length)||length<1e-20)return false;look.Normalize();
            var right=Vector3D.Cross(look,Math.Abs(look.Y)<.9?Vector3D.UnitY:Vector3D.UnitX);right.Normalize();var dx=right*(size*.5);var dy=Vector3D.Cross(look,dx);
            a=center-dx-dy;b=center+dx-dy;c=center+dx+dy;d=center-dx+dy;return FiniteDisplayPoint(a)&&FiniteDisplayPoint(b)&&FiniteDisplayPoint(c)&&FiniteDisplayPoint(d);
        }
        static Vector3D HologramBeamTarget(Item item,int slot)
        {return item.Geometry.Points[(int)((long)slot*item.Geometry.Points.Length/Math.Max(1,item.Effects.Beams))];}
        static bool HologramSafeLine(Vector3D a,Vector3D b,Vector3D camera,out Vector3 direction,out float length)
        {
            direction=Vector3.Zero;length=0;if(!ModClientRules.SafePoint(camera)||!ModClientRules.SafePoint(a)||!ModClientRules.SafePoint(b)||!ModClientRules.SafePoint(a-camera,1000000)||!ModClientRules.SafePoint(b-camera,1000000))return false;
            var delta=b-a;double distance=delta.Length();if(!Geometry.Finite(distance)||distance<1e-10||distance>2000000)return false;direction=(Vector3)(delta/distance);length=(float)distance;return true;
        }
        static MatrixD HologramProjectedExtrusion(MatrixD content,HoloProjectedScreenData data,MatrixD world,double depth)
        {
            var canvas=ProjectedCanvas(data,depth,data.CanvasWidth,data.CanvasHeight);var pose=ReadMatrix(data.Pose);
            double x=canvas.Right.Length(),y=canvas.Up.Length();var normal=Vector3D.Cross(canvas.Right,canvas.Up);double length=normal.LengthSquared();
            if(!Geometry.Finite(x)||!Geometry.Finite(y)||x<=0||y<=0||!Geometry.Finite(length)||length<1e-20)throw new ArgumentException("Flat effect projection requires a finite, nondegenerate screen plane.");
            normal/=Math.Sqrt(length);if(Vector3D.Dot(normal,pose.Backward)<0)normal=-normal;
            canvas.Backward=normal*Math.Sqrt(x*y);return content*canvas*world;
        }
    }
}
