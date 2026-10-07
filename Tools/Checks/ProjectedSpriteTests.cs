using HoloMap;
using VRage.Game.GUI.TextPanel;
using VRageMath;

internal static class ProjectedSpriteTests
{
    static int _checks;
    static void Check(bool value,string message){if(!value)throw new Exception("Projected sprites: "+message);_checks++;}
    static void Reject(Action action,string message){try{action();}catch(ArgumentException){_checks++;return;}throw new Exception("Expected rejection: "+message);}
    static MySprite Texture(string name,Vector2 position,Vector2 size,Color? color=null,float rotation=0)
    {return new MySprite{Type=SpriteType.TEXTURE,Data=name,Position=position,Size=size,Color=color??Color.White,RotationOrScale=rotation,Alignment=TextAlignment.CENTER};}
    static double Area(SurfaceMesh mesh,Func<Vector4,bool> filter=null)
    {double area=0;var g=mesh.Geometry;for(int i=0;i<g.Triangles.Length;i+=3){if(filter!=null&&!filter(mesh.Colors[i/3]))continue;var a=g.Points[g.Triangles[i]];var b=g.Points[g.Triangles[i+1]];var c=g.Points[g.Triangles[i+2]];area+=Vector3D.Cross(b-a,c-a).Length()/2;}return area;}
    static Vector3D Extent(SurfaceMesh mesh){var box=BoundingBoxD.CreateInvalid();foreach(var p in mesh.Geometry.Points)box.Include(p);return box.Size;}
    static bool Covers(SurfaceMesh mesh,Vector3D p)
    {
        var g=mesh.Geometry;for(int i=0;i<g.Triangles.Length;i+=3){var a=g.Points[g.Triangles[i]];var b=g.Points[g.Triangles[i+1]];var c=g.Points[g.Triangles[i+2]];double x=Vector3D.Cross(b-a,p-a).Z,y=Vector3D.Cross(c-b,p-b).Z,z=Vector3D.Cross(a-c,p-c).Z;if(x>=0&&y>=0&&z>=0||x<=0&&y<=0&&z<=0)return true;}return false;
    }
    public static int Run()
    {
        _checks=0;var box=ProjectedSprites.Compile(new[]{Texture("SquareSimple",new Vector2(50),new Vector2(100))},100,100);
        Check(box.Geometry.Points.All(p=>Math.Abs(p.Z)<1e-12&&Math.Abs(p.X)<=.500001&&Math.Abs(p.Y)<=.500001)&&Math.Abs(Area(box)-1)<1e-6,"canvas becomes centered normalized flat XY");
        var topLeft=ProjectedSprites.Compile(new[]{Texture("SquareSimple",new Vector2(10,20),new Vector2(10))},100,100);
        Check(topLeft.Geometry.Points.All(p=>p.X<0&&p.Y>0),"top-left source pixels map to upper-left normalized coordinates");
        var rotated=ProjectedSprites.Compile(new[]{Texture("SquareSimple",new Vector2(50),new Vector2(20,40),rotation:(float)Math.PI/2)},100,100);var extent=Extent(rotated);
        Check(Math.Abs(extent.X-.4)<1e-5&&Math.Abs(extent.Y-.2)<1e-5,"native clockwise rotation preserves dimensions and area");
        var tinted=ProjectedSprites.Compile(new[]{Texture("Triangle",new Vector2(50),new Vector2(50),new Color(255,128,0,64))},100,100);
        Check(tinted.Colors.All(c=>c.X==1&&Math.Abs(c.Y-128/255f)<1e-6&&c.Z==0&&Math.Abs(c.W-64/255f)<1e-6),"RGBA buffers preserve unpremultiplied color and transparency");
        var clips=new[]{new MySprite{Type=SpriteType.CLIP_RECT,Position=new Vector2(10,20),Size=new Vector2(60)},new MySprite{Type=SpriteType.CLIP_RECT,Position=new Vector2(20,30),Size=new Vector2(20)},Texture("SquareSimple",new Vector2(50),new Vector2(100),Color.Red),new MySprite{Type=SpriteType.CLIP_RECT},Texture("SquareSimple",new Vector2(50),new Vector2(100),Color.Green)};
        var clipped=ProjectedSprites.Compile(clips,100,100);
        Check(Math.Abs(Area(clipped,c=>c.X>.9)-.04)<1e-5&&Math.Abs(Area(clipped,c=>c.Y>.1&&c.X<.1)-.36)<1e-5,"nested clip intersection and pop restore the enclosing rectangle");
        var empty=ProjectedSprites.Compile(new[]{new MySprite{Type=SpriteType.CLIP_RECT,Position=new Vector2(200),Size=new Vector2(10)},Texture("Circle",new Vector2(50),new Vector2(50))},100,100);
        Check(empty.Geometry.Triangles.Length==0,"empty clip intersection produces no output");
        var hollow=ProjectedSprites.Compile(new[]{Texture("CircleHollow",new Vector2(50),new Vector2(80))},100,100);
        Check(Area(hollow)>0&&Area(hollow)<.15&&hollow.Colors.Length==96,"hollow circle is a bounded ring of filled triangles");
        double[] lefts=new double[3];var alignments=new[]{TextAlignment.LEFT,TextAlignment.CENTER,TextAlignment.RIGHT};
        for(int i=0;i<3;i++){var text=MySprite.CreateText("Aa","Debug",Color.White,.5f,alignments[i]);text.Position=new Vector2(50,30);var mesh=ProjectedSprites.Compile(new[]{text},100,100);lefts[i]=mesh.Geometry.Points.Min(p=>p.X);Check(mesh.Geometry.Triangles.Length>0,"mixed-case text has vector outlines");}
        Check(lefts[0]>lefts[1]&&lefts[1]>lefts[2],"native text alignment anchors change horizontal placement");
        var multiline=MySprite.CreateText("A\nA","Debug",Color.White,.5f,TextAlignment.LEFT);multiline.Position=new Vector2(20);var lines=ProjectedSprites.Compile(new[]{multiline},100,100);
        Check(Extent(lines).Y>.25,"multiline text advances downward in source coordinates");
        foreach(string name in new[]{"SquareHollow","Circle","Triangle","RightTriangle","Line"})Check(ProjectedSprites.Compile(new[]{Texture(name,new Vector2(50),new Vector2(40))},100,100).Geometry.Triangles.Length>0,"approved primitive "+name);
        Reject(()=>ProjectedSprites.Compile(new[]{Texture("UnregisteredImage",new Vector2(50),new Vector2(10))},100,100),"unapproved texture");
        var wrongFont=MySprite.CreateText("A","Unregistered",Color.White);Reject(()=>ProjectedSprites.Compile(new[]{wrongFont},100,100),"unapproved font");
        Reject(()=>ProjectedSprites.Compile(new[]{Texture("SquareSimple",new Vector2(float.NaN,0),new Vector2(10))},100,100),"nonfinite position");
        Reject(()=>ProjectedSprites.Compile(new[]{Texture("SquareSimple",Vector2.Zero,new Vector2(-1,10))},100,100),"negative size");
        Reject(()=>ProjectedSprites.Compile(new MySprite[129],100,100),"sprite input quota");
        Reject(()=>ProjectedSprites.Compile(new MySprite[0],double.PositiveInfinity,100),"canvas dimensions");
        var deep=Enumerable.Range(0,9).Select(i=>new MySprite{Type=SpriteType.CLIP_RECT,Position=Vector2.Zero,Size=new Vector2(100)}).ToArray();Reject(()=>ProjectedSprites.Compile(deep,100,100),"nested clip quota");
        var textFrames=Enumerable.Range(0,5).Select(i=>MySprite.CreateText(new string('A',64),"Debug",Color.White)).ToArray();Reject(()=>ProjectedSprites.Validate(textFrames,512,512),"total text quota without heavy compilation");
        var crowded=Enumerable.Range(0,128).Select(i=>Texture("CircleHollow",new Vector2(10+(i%16)*30,10+(i/16)*50),new Vector2(10))).ToArray();Reject(()=>ProjectedSprites.Compile(crowded,512,512),"bounded mesh/work quota");
        Check(ProjectedSprites.Compile(new MySprite[0],100,100).Geometry.Triangles.Length==0,"empty frame clears safely");
        var redBack=Texture("SquareSimple",new Vector2(50),new Vector2(100),Color.Red);
        var blueFront=Texture("SquareSimple",new Vector2(50),new Vector2(50),new Color(0,0,255,128));
        var composite=ProjectedSprites.Composite(new[]{redBack,blueFront},100,100,16,16);
        Check(composite.Colors.Any(c=>Math.Abs(c.X-127/255f)<1e-6&&Math.Abs(c.Z-128/255f)<1e-6&&c.W==1),"painter order composites translucent blue over opaque red");
        Check(Math.Abs(Area(composite)-1)<1e-6&&composite.Geometry.Points.All(p=>Math.Abs(p.X)<=.5&&Math.Abs(p.Y)<=.5),"composited rectangles partition the canvas without alpha overlap or escaped bounds");
        var reversed=ProjectedSprites.Composite(new[]{blueFront,redBack},100,100,16,16);
        Check(reversed.Colors.All(c=>c.X==1&&c.Z==0&&c.W==1),"reversing order makes the opaque background overwrite the prior blue square");
        var half=Texture("SquareSimple",new Vector2(50),new Vector2(100),new Color(10,20,30,64));
        var alphaComposite=ProjectedSprites.Composite(new[]{half},100,100,16,16);
        Check(alphaComposite.Geometry.Triangles.Length==6&&alphaComposite.Colors.All(c=>Math.Abs(c.W-64/255f)<1e-6),"top-left shared-edge coverage prevents a quad's diagonal from doubling alpha");
        var layered=ProjectedSprites.Composite(new[]{half,half},100,100,16,16);float expectedAlpha=(float)Math.Round((1-Math.Pow(1-64/255.0,2))*255)/255;
        Check(layered.Colors.All(c=>Math.Abs(c.W-expectedAlpha)<1e-6),"separate translucent sprites blend once each using premultiplied alpha-over");
        var ring=ProjectedSprites.Composite(new[]{Texture("CircleHollow",new Vector2(50),new Vector2(80))},100,100,64,64);
        Check(Area(ring)>0&&Area(ring)<.15&&ring.Geometry.Points.All(p=>Math.Abs(p.X)<=.5&&Math.Abs(p.Y)<=.5),"hollow sprite retains its transparent center in the bounded raster mesh");
        Check(!Covers(ring,new Vector3D(.0001,.0002,0)),"composited ring leaves its center uncovered");
        Check(ProjectedSprites.Composite(new MySprite[0],100,100).Geometry.Triangles.Length==0,"empty composite has no opaque rectangle");
        Reject(()=>ProjectedSprites.Composite(new[]{redBack},100,100,257,72),"raster width quota");
        Reject(()=>ProjectedSprites.Composite(new[]{redBack},100,100,128,145),"raster height quota");
        var costly=Enumerable.Range(0,128).Select(i=>redBack).ToArray();Reject(()=>ProjectedSprites.Composite(costly,100,100),"independent pixel coverage work quota");
        return _checks;
    }
}
