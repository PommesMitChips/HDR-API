using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace HoloMap
{
    // Pure bounded adapter. Call Compile from the viewer's geometry cache, never per server tick.
    // Input uses a top-left pixel-like canvas; output is centered normalized XY, with Y up.
    public static class ProjectedSprites
    {
        public const int MaxSprites=128,MaxTextCharacters=256,MaxClipDepth=8;
        const int CircleSegments=48;
        struct Rect {public double Left,Top,Right,Bottom;}
        sealed class WorkBudget {int remaining=100000;public void Charge(int amount=1){if(amount<0||amount>remaining)throw new ArgumentException("Projected sprite compilation work budget exceeded.");remaining-=amount;}}
        public static void Validate(MySprite[] sprites,double canvasWidth,double canvasHeight)
        {
            if(sprites==null||sprites.Length>MaxSprites)throw new ArgumentException("Projected sprites require at most 128 entries.");
            if(!Geometry.Finite(canvasWidth)||!Geometry.Finite(canvasHeight)||canvasWidth<1||canvasHeight<1||canvasWidth>16384||canvasHeight>16384)throw new ArgumentException("Sprite canvas dimensions must be 1–16384 units.");
            int characters=0,depth=0;
            foreach(var s in sprites)
            {
                if(s.Data!=null&&s.Data.Length>64||s.FontId!=null&&s.FontId.Length>32)throw new ArgumentException("Sprite identifiers/text exceed their character limits.");
                if(s.Position.HasValue)Finite(s.Position.Value,"position",false);
                if(s.Size.HasValue)Finite(s.Size.Value,"size",true);
                if(!Geometry.Finite(s.RotationOrScale)||Math.Abs(s.RotationOrScale)>1000000)throw new ArgumentException("Sprite rotation/scale must be finite and bounded.");
                if(s.Type==SpriteType.CLIP_RECT){if(!s.Position.HasValue||!s.Size.HasValue){if(depth>0)depth--;}else if(++depth>MaxClipDepth)throw new ArgumentException("Projected sprites support eight nested clips.");continue;}
                if(s.Alignment!=TextAlignment.LEFT&&s.Alignment!=TextAlignment.CENTER&&s.Alignment!=TextAlignment.RIGHT)throw new ArgumentException("Unsupported sprite alignment.");
                if(s.Type==SpriteType.TEXT)
                {
                    if(s.FontId!=null&&s.FontId!=""&&s.FontId!="Debug")throw new ArgumentException("Projected sprite text supports the Debug vector font only.");
                    if(s.Data==null||s.Data.Length>64||(characters+=s.Data.Length)>MaxTextCharacters)throw new ArgumentException("Sprite text is limited to 64 characters per entry and 256 per frame.");
                    if(s.RotationOrScale<=0||s.RotationOrScale>8)throw new ArgumentException("Text scale must be positive and at most eight.");
                    for(int i=0;i<s.Data.Length;i++){int code=s.Data[i];if(char.IsHighSurrogate(s.Data[i])){if(i+1>=s.Data.Length||!char.IsLowSurrogate(s.Data[i+1]))throw new ArgumentException("Malformed sprite text surrogate.");code=char.ConvertToUtf32(s.Data[i],s.Data[++i]);}else if(char.IsLowSurrogate(s.Data[i]))throw new ArgumentException("Malformed sprite text surrogate.");if(code=='\r'||code=='\n'||code=='\t')continue;if(!VectorFont.Supports(code))throw new ArgumentException("Unsupported projected text character: "+code);}
                }
                else if(s.Type==SpriteType.TEXTURE)
                {
                    if(s.Data!="SquareSimple"&&s.Data!="SquareHollow"&&s.Data!="Circle"&&s.Data!="CircleHollow"&&s.Data!="Triangle"&&s.Data!="RightTriangle"&&s.Data!="Line")throw new ArgumentException("Unsupported projected sprite texture: "+s.Data+". Use the HDR image API for registered images.");
                }
                else throw new ArgumentException("Unsupported projected sprite type.");
            }
        }
        static void Finite(Vector2 p,string name,bool positive)
        {if(!Geometry.Finite(p.X)||!Geometry.Finite(p.Y)||Math.Abs(p.X)>65536||Math.Abs(p.Y)>65536||positive&&(p.X<=0||p.Y<=0))throw new ArgumentException("Sprite "+name+" must be finite, bounded and sizes positive.");}
        sealed class RasterRect {public int X1,X2,Y1,Y2;public uint Color;}
        // Compatibility backend: ordered CPU alpha composition avoids relying on world OIT painter order.
        public static SurfaceMesh Composite(MySprite[] sprites,double canvasWidth,double canvasHeight,int width=128,int height=72)
        {
            if(width<1||height<1||width>256||height>144)throw new ArgumentException("Projected sprite raster resolution must be 1–256 by 1–144.");
            var source=Compile(sprites,canvasWidth,canvasHeight);var pixels=new Vector4[width*height];int work=1000000;
            var geometry=source.Geometry;
            for(int i=0;i<geometry.Triangles.Length;i+=3)
            {
                var a=RasterPoint(geometry.Points[geometry.Triangles[i]],width,height);var b=RasterPoint(geometry.Points[geometry.Triangles[i+1]],width,height);var c=RasterPoint(geometry.Points[geometry.Triangles[i+2]],width,height);
                double area=Cross(b-a,c-a);if(Math.Abs(area)<1e-12)continue;if(area<0){var swap=b;b=c;c=swap;}
                int minX=Math.Max(0,(int)Math.Ceiling(Math.Min(a.X,Math.Min(b.X,c.X))-.5)),maxX=Math.Min(width-1,(int)Math.Floor(Math.Max(a.X,Math.Max(b.X,c.X))-.5));
                int minY=Math.Max(0,(int)Math.Ceiling(Math.Min(a.Y,Math.Min(b.Y,c.Y))-.5)),maxY=Math.Min(height-1,(int)Math.Floor(Math.Max(a.Y,Math.Max(b.Y,c.Y))-.5));
                var color=source.Colors[i/3];float alpha=color.W;var premultiplied=new Vector4(color.X*alpha,color.Y*alpha,color.Z*alpha,alpha);
                for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
                {
                    if(--work<0)throw new ArgumentException("Projected sprite raster work budget exceeded; simplify the frame or lower resolution.");
                    var p=new Vector2D(x+.5,y+.5);if(!Covered(a,b,p)||!Covered(b,c,p)||!Covered(c,a,p))continue;
                    int index=y*width+x;pixels[index]=premultiplied+pixels[index]*(1-alpha);
                }
            }
            var packed=new uint[pixels.Length];for(int i=0;i<packed.Length;i++)packed[i]=Pack(pixels[i]);
            var rectangles=new List<RasterRect>();var active=new Dictionary<ulong,RasterRect>();
            for(int y=0;y<height;y++)
            {
                var next=new Dictionary<ulong,RasterRect>();
                for(int x=0;x<width;)
                {
                    uint color=packed[y*width+x];int end=x+1;while(end<width&&packed[y*width+end]==color)end++;
                    if((color>>24)!=0)
                    {
                        ulong key=((ulong)color<<18)|((ulong)x<<9)|(uint)end;RasterRect rect;
                        if(active.TryGetValue(key,out rect))rect.Y2=y+1;else rect=new RasterRect{X1=x,X2=end,Y1=y,Y2=y+1,Color=color};next.Add(key,rect);
                    }
                    x=end;
                }
                foreach(var pair in active)if(!next.ContainsKey(pair.Key))rectangles.Add(pair.Value);active=next;
            }
            foreach(var rect in active.Values)rectangles.Add(rect);
            using(GeometryWork.Begin())
            {
                var builder=new MeshEffects.Builder();
                foreach(var rect in rectangles)
                {
                    var a=RasterVertex(rect.X1,rect.Y1,width,height);var b=RasterVertex(rect.X2,rect.Y1,width,height);var c=RasterVertex(rect.X2,rect.Y2,width,height);var d=RasterVertex(rect.X1,rect.Y2,width,height);var color=Unpack(rect.Color);
                    builder.Triangle(a,b,c,color);builder.Triangle(a,c,d,color);
                }
                return builder.Finish();
            }
        }
        static Vector2D RasterPoint(Vector3D p,int width,int height){return new Vector2D((p.X+.5)*width,(.5-p.Y)*height);}
        static double Cross(Vector2D a,Vector2D b){return a.X*b.Y-a.Y*b.X;}
        static bool Covered(Vector2D a,Vector2D b,Vector2D p)
        {double edge=Cross(b-a,p-a);if(edge>1e-10)return true;if(edge < -1e-10)return false;var delta=b-a;return delta.Y<0||delta.Y==0&&delta.X>0;}
        static uint Channel(float value){return (uint)Math.Round(Math.Max(0,Math.Min(1,value))*255);}
        static uint Pack(Vector4 p)
        {uint a=Channel(p.W);if(a==0)return 0;return Channel(p.X/p.W)|(Channel(p.Y/p.W)<<8)|(Channel(p.Z/p.W)<<16)|(a<<24);}
        static Vector4 Unpack(uint p){return new Vector4((p&255)/255f,((p>>8)&255)/255f,((p>>16)&255)/255f,(p>>24)/255f);}
        static MeshEffects.Vertex RasterVertex(int x,int y,int width,int height){return MeshEffects.V(new Vector3D(x/(double)width-.5,.5-y/(double)height,0));}
        public static SurfaceMesh Compile(MySprite[] sprites,double canvasWidth,double canvasHeight)
        {
            Validate(sprites,canvasWidth,canvasHeight);
            using(GeometryWork.Begin(100000))
            {
                var work=new WorkBudget();var builder=new MeshEffects.Builder();var clips=new List<Rect>();var canvas=new Rect{Right=canvasWidth,Bottom=canvasHeight};var clip=canvas;
                foreach(var s in sprites)
                {
                    GeometryWork.Charge();work.Charge();
                    if(s.Type==SpriteType.CLIP_RECT)
                    {
                        if(!s.Position.HasValue||!s.Size.HasValue){if(clips.Count>0)clips.RemoveAt(clips.Count-1);clip=clips.Count==0?canvas:clips[clips.Count-1];}
                        else{var p=s.Position.Value;var size=s.Size.Value;clip=new Rect{Left=Math.Max(clip.Left,p.X),Top=Math.Max(clip.Top,p.Y),Right=Math.Min(clip.Right,p.X+size.X),Bottom=Math.Min(clip.Bottom,p.Y+size.Y)};clips.Add(clip);}continue;
                    }
                    if(clip.Left>=clip.Right||clip.Top>=clip.Bottom)continue;
                    var color=s.Color.HasValue?s.Color.Value.ToVector4():Vector4.One;if(color.W<=0)continue;
                    var position=s.Position??new Vector2((float)(canvasWidth/2),(float)(canvasHeight/2));
                    if(s.Type==SpriteType.TEXT)
                    {
                        double height=28*s.RotationOrScale;string anchor=s.Alignment==TextAlignment.LEFT?"start":s.Alignment==TextAlignment.RIGHT?"end":"middle";
                        var glyphs=VectorFont.Layout(s.Data,anchor);
                        foreach(var glyph in glyphs){var text=glyph.Mesh;work.Charge(text.Points.Length+text.Triangles.Length);for(int t=0;t<text.Triangles.Length;t+=3){var points=new Vector2[3];for(int j=0;j<3;j++){var p=(text.Points[text.Triangles[t+j]]+glyph.Offset)*height;points[j]=new Vector2(position.X+(float)p.X,position.Y+(float)(height*.5-p.Y));}Triangle(builder,points[0],points[1],points[2],color,clip,canvasWidth,canvasHeight,work);}}continue;
                    }
                    var dimensions=s.Size??new Vector2((float)canvasWidth,(float)canvasHeight);if(s.Alignment==TextAlignment.LEFT)position.X+=dimensions.X/2;else if(s.Alignment==TextAlignment.RIGHT)position.X-=dimensions.X/2;
                    double cosine=Math.Cos(s.RotationOrScale),sine=Math.Sin(s.RotationOrScale);var pointsList=new List<Vector2>();
                    if(s.Data=="Circle"||s.Data=="CircleHollow")for(int i=0;i<CircleSegments;i++){double angle=i*2*Math.PI/CircleSegments;pointsList.Add(new Vector2((float)(Math.Cos(angle)*dimensions.X/2),(float)(Math.Sin(angle)*dimensions.Y/2)));}
                    else if(s.Data=="Triangle"){pointsList.Add(new Vector2(0,-dimensions.Y/2));pointsList.Add(dimensions/2);pointsList.Add(new Vector2(-dimensions.X/2,dimensions.Y/2));}
                    else if(s.Data=="RightTriangle"){pointsList.Add(-dimensions/2);pointsList.Add(new Vector2(dimensions.X/2,-dimensions.Y/2));pointsList.Add(new Vector2(-dimensions.X/2,dimensions.Y/2));}
                    else{pointsList.Add(-dimensions/2);pointsList.Add(new Vector2(dimensions.X/2,-dimensions.Y/2));pointsList.Add(dimensions/2);pointsList.Add(new Vector2(-dimensions.X/2,dimensions.Y/2));}
                    var outside=new Vector2[pointsList.Count];for(int i=0;i<outside.Length;i++)outside[i]=Rotate(pointsList[i],position,cosine,sine);
                    if(s.Data=="CircleHollow"||s.Data=="SquareHollow")
                    {
                        // Texture-independent strokes: a ten-percent inset, triangulated without overlap.
                        var inside=new Vector2[outside.Length];for(int i=0;i<inside.Length;i++)inside[i]=Rotate(pointsList[i]*.9f,position,cosine,sine);
                        for(int i=0;i<outside.Length;i++){int next=(i+1)%outside.Length;Triangle(builder,outside[i],outside[next],inside[next],color,clip,canvasWidth,canvasHeight,work);Triangle(builder,outside[i],inside[next],inside[i],color,clip,canvasWidth,canvasHeight,work);}
                    }
                    else for(int i=1;i+1<outside.Length;i++)Triangle(builder,outside[0],outside[i],outside[i+1],color,clip,canvasWidth,canvasHeight,work);
                }
                return builder.Finish();
            }
        }
        static Vector2 Rotate(Vector2 p,Vector2 center,double cosine,double sine)
        {return center+new Vector2((float)(p.X*cosine-p.Y*sine),(float)(p.X*sine+p.Y*cosine));}
        static void Triangle(MeshEffects.Builder builder,Vector2 a,Vector2 b,Vector2 c,Vector4 color,Rect clip,double width,double height,WorkBudget work)
        {
            GeometryWork.Charge();work.Charge();var polygon=new List<MeshEffects.Vertex>{Point(a,width,height),Point(b,width,height),Point(c,width,height)};
            var planes=new[]{new Vector4(1,0,0,(float)(.5-clip.Left/width)),new Vector4(-1,0,0,(float)(clip.Right/width-.5)),new Vector4(0,-1,0,(float)(.5-clip.Top/height)),new Vector4(0,1,0,(float)(clip.Bottom/height-.5))};
            foreach(var plane in planes){work.Charge(polygon.Count+1);polygon=MeshEffects.ClipPolygon(polygon,plane);if(polygon.Count==0)return;}
            for(int i=1;i+1<polygon.Count;i++){work.Charge();builder.Triangle(polygon[0],polygon[i],polygon[i+1],color);}
        }
        static MeshEffects.Vertex Point(Vector2 p,double width,double height){return MeshEffects.V(new Vector3D(p.X/width-.5,.5-p.Y/height,0));}
    }
}
