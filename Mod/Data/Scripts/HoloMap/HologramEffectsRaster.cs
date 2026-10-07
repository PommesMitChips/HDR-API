using System;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace HoloMap
{
    // Display-only sampling. Source geometry and its compiled/mapped cache identity
    // remain unchanged; the CPU compositor evaluates coverage before source-over.
    public static class HologramRasterEffects
    {
        public static void Bounds(Geometry geometry, out Vector3D min, out Vector3D max)
        {
            min = max = Vector3D.Zero;
            if (geometry == null || geometry.Points == null || geometry.Points.Length == 0) return;
            min = max = geometry.Points[0];
            foreach (var point in geometry.Points)
            {
                min = Vector3D.Min(min, point); max = Vector3D.Max(max, point);
            }
        }
        public static double[] Coordinates(Geometry geometry, HologramEffectSettings settings)
        {
            Vector3D min, max; Bounds(geometry, out min, out max);
            var result = new double[geometry.Points.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = HologramEffectKernel.Coordinate(settings, geometry.Points[i], min, max);
            return result;
        }
        public static Vector4 Color(HologramEffectSettings settings, HologramEffectFrame frame,
            Vector4 color, double coordinate, int primitive)
        { return Color(HologramEffectKernel.Prepare(settings, frame), color, coordinate, primitive); }
        public static Vector4 Color(HologramEffectSampler sampler, Vector4 color, double coordinate, int primitive)
        {
            var sampled = sampler.Color(color, Math.Max(0, Math.Min(1, coordinate)), primitive);
            sampled.X = Math.Min(1, sampled.X); sampled.Y = Math.Min(1, sampled.Y); sampled.Z = Math.Min(1, sampled.Z);
            return sampled;
        }
        internal static int ClipBand(Vector3D[] input,int count,Vector3D[] output,HologramEffectSettings settings,
            Vector3D min,Vector3D max,double threshold,bool below)
        {
            int written=0;if(count==0)return 0;var prior=input[count-1];
            double p=HologramEffectKernel.Coordinate(settings,prior,min,max)-threshold;
            for(int i=0;i<count;i++)
            {
                var current=input[i];double q=HologramEffectKernel.Coordinate(settings,current,min,max)-threshold;
                bool a=below?p<=0:p>=0,b=below?q<=0:q>=0;
                if(a!=b)output[written++]=prior+(current-prior)*(p/(p-q));if(b)output[written++]=current;
                prior=current;p=q;
            }
            return written;
        }
        public static bool TryParticleSprite(Vector2 center,double width,double height,out Vector2 extent)
        {
            extent=Vector2.Zero;
            // An accepted source can be degenerate along a huge transform axis.
            // Particle size still uses that axis, so prove its complete sprite bounds
            // before casting doubles or handing vertices to the game's draw frame.
            if(!Geometry.Finite(center.X)||!Geometry.Finite(center.Y)||!Geometry.Finite(width)||!Geometry.Finite(height)||
                width<=0||height<=0||width>float.MaxValue||height>float.MaxValue||
                Math.Abs((double)center.X)+width*.5>float.MaxValue||Math.Abs((double)center.Y)+height*.5>float.MaxValue)return false;
            extent=new Vector2((float)width,(float)height);
            if(extent.X<=0||extent.Y<=0){extent=Vector2.Zero;return false;}
            return true;
        }
    }
    public sealed partial class HoloMapSession
    {
        static readonly SurfaceMesh RasterEffectParticleQuad = new SurfaceMesh
        {
            Geometry = new Geometry(new[] { new Vector3D(-.5,-.5,0), new Vector3D(.5,-.5,0),
                new Vector3D(.5,.5,0), new Vector3D(-.5,.5,0) }, new int[0], new[] { 0,1,2,0,2,3 })
        };
        void RasterEffectParticles(RasterCanvas compositor, Item item, MatrixD view, float opacity)
        {
            var settings = item.Effects;
            if (settings == null || settings.Particles == 0 || settings.ParticleOpacity == 0 || opacity <= 0) return;
            var frame = HologramEffectKernel.Evaluate(settings, HologramEffectNow, item.EffectStart, item.EffectEntering);
            var sampler = HologramEffectKernel.Prepare(settings,frame);
            Vector3D min,max; HologramRasterEffects.Bounds(item.Geometry, out min, out max);
            int primitives = item.Geometry.Triangles.Length / 3 + item.Geometry.Edges.Length / 2;
            int count = Math.Min(settings.Particles, Math.Max(0, settings.PrimitiveLimit - primitives) / 2);
            var tint = item.FillColor.W > 0 ? item.FillColor : item.LineColor;
            for (int i = 0; i < count; i++)
            {
                HologramEffectParticle particle;
                if (!HologramEffectKernel.Particle(settings, frame, i, min, max, out particle)) continue;
                var point = particle.Position; point.Z = 0;
                var transform = MatrixD.CreateScale(particle.Size) * MatrixD.CreateTranslation(point) * item.Transform * view;
                var color = HologramRasterEffects.Color(sampler,tint,HologramEffectKernel.Coordinate(settings,point,min,max),primitives+i);
                // Optional embellishments yield when the already accepted content used
                // the bounded sample allowance. Add's precharge keeps prior pixels intact.
                try { compositor.Add(RasterEffectParticleQuad, transform, color, Vector4.Zero, 0, opacity * (float)particle.Opacity); }
                catch (ArgumentException) { break; }
            }
        }
        struct LcdEffectSample
        {
            public HologramEffectSettings Settings;
            public HologramEffectFrame Frame;
            public HologramEffectSampler Sampler;
            public Vector3D Min,Max;
        }
        LcdEffectSample SampleLcdEffect(Item item)
        {
            var result = new LcdEffectSample { Settings = item.Effects };
            if (result.Settings == null) return result;
            HologramRasterEffects.Bounds(item.Geometry, out result.Min, out result.Max);
            result.Frame = HologramEffectKernel.Evaluate(result.Settings, HologramEffectNow, item.EffectStart, item.EffectEntering);
            result.Sampler = HologramEffectKernel.Prepare(result.Settings,result.Frame);
            return result;
        }
        static Vector4 LcdEffectColor(LcdEffectSample sample, Vector4 color, Vector3D point, int primitive)
        {
            if (sample.Settings == null) return color;
            return HologramRasterEffects.Color(sample.Sampler, color,
                HologramEffectKernel.Coordinate(sample.Settings, point, sample.Min, sample.Max), primitive);
        }
        static int LcdEffectParticleCount(Item item, int available, int cost)
        {
            if (item.Effects == null) return 0;
            int primitives = item.Geometry.Triangles.Length / 3 + item.Geometry.Edges.Length / 2;
            return Math.Min(item.Effects.Particles, Math.Min(Math.Max(0, available) / cost,
                Math.Max(0, item.Effects.PrimitiveLimit - primitives) / 2));
        }
        readonly Vector3D[] _lcdEffectPolygonA=new Vector3D[8],_lcdEffectPolygonB=new Vector3D[8];
        static int ClipLcdEffectBand(Vector3D[] input,int count,Vector3D[] output,LcdEffectSample effect,double threshold,bool below)
        {return HologramRasterEffects.ClipBand(input,count,output,effect.Settings,effect.Min,effect.Max,threshold,below);}
        int LcdEffectRefreshPolygon(Item item,LcdEffectSample effect,int triangle)
        {
            var g=item.Geometry;
            _lcdEffectPolygonA[0]=g.Points[g.Triangles[triangle]];_lcdEffectPolygonA[1]=g.Points[g.Triangles[triangle+1]];_lcdEffectPolygonA[2]=g.Points[g.Triangles[triangle+2]];
            double low=Math.Max(0,effect.Frame.ScanPhase-effect.Settings.ScanWidth*.5),high=Math.Min(1,effect.Frame.ScanPhase+effect.Settings.ScanWidth*.5);
            int count=ClipLcdEffectBand(_lcdEffectPolygonA,3,_lcdEffectPolygonB,effect,low,false);
            return ClipLcdEffectBand(_lcdEffectPolygonB,count,_lcdEffectPolygonA,effect,high,true);
        }
        static int LcdEffectExtraAllowance(Item item,int remaining)
        {return Math.Min(remaining,Math.Max(0,item.Effects.PrimitiveLimit-item.Geometry.Triangles.Length/3-item.Geometry.Edges.Length/2));}
        void DrawLcdEffectExtras(Scene tile,Item item,LcdEffectSample effect,MySpriteDrawFrame frame,Vector2 origin,Vector2 size,MatrixD matrix,float alpha,ref int budget)
        {
            if(effect.Settings==null||alpha<=0)return;int prior=budget;budget=LcdEffectExtraAllowance(item,budget);
            try
            {
                if(item.Material==null&&(effect.Settings.ScanStrength>0||effect.Settings.ScanBoost>0))
                for(int t=0;t<item.Geometry.Triangles.Length&&budget>0;t+=3)
                {
                    int count=LcdEffectRefreshPolygon(item,effect,t);if(count<3)continue;
                    var color=item.TriangleColors==null?item.FillColor:item.TriangleColors[t/3];color=LcdEffectColor(effect,color,(_lcdEffectPolygonA[0]+_lcdEffectPolygonA[1]+_lcdEffectPolygonA[2])/3,t/3);color.W*=alpha*(float)Math.Max(effect.Settings.ScanStrength,.15);
                    var a=origin+LcdProjection.Point(Vector3D.Transform(_lcdEffectPolygonA[0],matrix),tile.LcdWidth,tile.LcdHeight,size,tile.LcdColumns,tile.LcdRows,tile.LcdColumn,tile.LcdRow);
                    for(int i=1;i+1<count&&budget>0;i++)
                    {
                        var b=origin+LcdProjection.Point(Vector3D.Transform(_lcdEffectPolygonA[i],matrix),tile.LcdWidth,tile.LcdHeight,size,tile.LcdColumns,tile.LcdRows,tile.LcdColumn,tile.LcdRow);
                        var c=origin+LcdProjection.Point(Vector3D.Transform(_lcdEffectPolygonA[i+1],matrix),tile.LcdWidth,tile.LcdHeight,size,tile.LcdColumns,tile.LcdRows,tile.LcdColumn,tile.LcdRow);
                        LcdTriangle(frame,a,b,c,LcdColor(color,item.Emission),origin,size,ref budget);
                    }
                }
                DrawLcdEffectParticles(tile,item,effect,frame,origin,size,matrix,alpha,ref budget);
            }
            finally{budget=prior-LcdEffectExtraAllowance(item,prior)+budget;}
        }
        void DrawPinnedLcdEffectExtras(Item item,LcdEffectSample effect,MatrixD transform,DisplayVolume volume,float alpha,MatrixD world,MatrixD inverse,Vector3D camera,uint parent,ref int budget)
        {
            if(effect.Settings==null||alpha<=0)return;int prior=budget;budget=LcdEffectExtraAllowance(item,budget);
            try
            {
                if(item.Material==null&&(effect.Settings.ScanStrength>0||effect.Settings.ScanBoost>0))
                for(int t=0;t<item.Geometry.Triangles.Length&&budget>=2;t+=3)
                {
                    if(_volumeClipWork<8)break;_volumeClipWork-=8;
                    int count=LcdEffectRefreshPolygon(item,effect,t);if(count<3)continue;
                    var color=item.TriangleColors==null?item.FillColor:item.TriangleColors[t/3];color=LcdEffectColor(effect,color,(_lcdEffectPolygonA[0]+_lcdEffectPolygonA[1]+_lcdEffectPolygonA[2])/3,t/3);color.W*=alpha*(float)Math.Max(effect.Settings.ScanStrength,.15);
                    var offset=Vector3D.TransformNormal(new Vector3D(0,0,.0000001),transform);var a=Vector3D.Transform(_lcdEffectPolygonA[0],transform)+offset;
                    for(int i=1;i+1<count&&budget>=2;i++)DrawVolumeTriangle(volume,a,Vector3D.Transform(_lcdEffectPolygonA[i],transform)+offset,Vector3D.Transform(_lcdEffectPolygonA[i+1],transform)+offset,Vector2.Zero,Vector2.Zero,Vector2.Zero,color,item.Emission,false,null,world,inverse,camera,parent,ref budget);
                }
                DrawPinnedLcdEffectParticles(item,effect,transform,volume,alpha,world,inverse,camera,parent,ref budget);
            }
            finally{budget=prior-LcdEffectExtraAllowance(item,prior)+budget;}
        }
        void DrawLcdEffectParticles(Scene tile, Item item, LcdEffectSample effect, MySpriteDrawFrame frame,
            Vector2 origin, Vector2 size, MatrixD matrix, float alpha, ref int budget)
        {
            if (effect.Settings == null || alpha <= 0) return;
            int count = LcdEffectParticleCount(item, budget, 1);
            var tint = item.FillColor.W > 0 ? item.FillColor : item.LineColor;
            for (int i = 0; i < count && budget > 0; i++)
            {
                HologramEffectParticle particle;
                if (!HologramEffectKernel.Particle(effect.Settings, effect.Frame, i, effect.Min, effect.Max, out particle)) continue;
                var point = particle.Position; point.Z = 0;
                var center = origin + LcdProjection.Point(Vector3D.Transform(point, matrix), tile.LcdWidth, tile.LcdHeight,
                    size, tile.LcdColumns, tile.LcdRows, tile.LcdColumn, tile.LcdRow);
                double sx = Vector3D.TransformNormal(Vector3D.UnitX, matrix).Length(), sy = Vector3D.TransformNormal(Vector3D.UnitY, matrix).Length();
                Vector2 extent;
                if(!HologramRasterEffects.TryParticleSprite(center,particle.Size*sx*size.X*tile.LcdColumns/tile.LcdWidth,
                    particle.Size*sy*size.Y*tile.LcdRows/tile.LcdHeight,out extent))continue;
                if (center.X+extent.X*.5<origin.X||center.X-extent.X*.5>origin.X+size.X||
                    center.Y+extent.Y*.5<origin.Y||center.Y-extent.Y*.5>origin.Y+size.Y) continue;
                var color=LcdEffectColor(effect,tint,point,item.Geometry.Triangles.Length/3+item.Geometry.Edges.Length/2+i);color.W*=alpha*(float)particle.Opacity;
                var sprite = MySprite.CreateSprite("Circle", center, extent); sprite.Color = LcdColor(color,item.Emission);
                frame.Add(sprite); budget--;
            }
        }
        void DrawPinnedLcdEffectParticles(Item item, LcdEffectSample effect, MatrixD transform, DisplayVolume volume,
            float alpha, MatrixD world, MatrixD inverse, Vector3D camera, uint parent, ref int budget)
        {
            if (effect.Settings == null || alpha <= 0 || !HologramEffectKernel.MatrixValid(item.Transform)) return;
            int count = LcdEffectParticleCount(item, budget, 4);
            var tint = item.FillColor.W > 0 ? item.FillColor : item.LineColor;
            for (int i = 0; i < count && budget >= 4; i++)
            {
                HologramEffectParticle particle;
                if (!HologramEffectKernel.Particle(effect.Settings, effect.Frame, i, effect.Min, effect.Max, out particle)) continue;
                var point = particle.Position; point.Z = 0; double half = particle.Size*.5;
                var a=Vector3D.Transform(point+new Vector3D(-half,-half,0),transform);
                var b=Vector3D.Transform(point+new Vector3D(half,-half,0),transform);
                var c=Vector3D.Transform(point+new Vector3D(half,half,0),transform);
                var d=Vector3D.Transform(point+new Vector3D(-half,half,0),transform);
                if(!HologramEffectKernel.PointValid(a-world.Translation)||!HologramEffectKernel.PointValid(b-world.Translation)||
                    !HologramEffectKernel.PointValid(c-world.Translation)||!HologramEffectKernel.PointValid(d-world.Translation))continue;
                var color=LcdEffectColor(effect,tint,point,item.Geometry.Triangles.Length/3+item.Geometry.Edges.Length/2+i);color.W*=alpha*(float)particle.Opacity;
                DrawVolumeTriangle(volume,a,b,c,Vector2.Zero,Vector2.Zero,Vector2.Zero,color,item.Emission,false,null,world,inverse,camera,parent,ref budget);
                if(budget>=2)DrawVolumeTriangle(volume,a,c,d,Vector2.Zero,Vector2.Zero,Vector2.Zero,color,item.Emission,false,null,world,inverse,camera,parent,ref budget);
            }
        }
    }
}
