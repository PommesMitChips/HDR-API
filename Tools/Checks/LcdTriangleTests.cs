using System.Reflection;
using HoloMap;
using VRageMath;
using VRage.Game.GUI.TextPanel;

internal static class LcdTriangleTests
{
    static int _checks;
    static void Check(bool condition,string message){if(!condition)throw new Exception("LCD triangle: "+message);_checks++;}
    static double Cross(Vector2 a,Vector2 b)=>a.X*(double)b.Y-a.Y*(double)b.X;
    static Vector2[] Vertices(LcdTrianglePiece p)
    {
        var u=new Vector2((float)Math.Cos(p.Rotation),(float)Math.Sin(p.Rotation));var n=new Vector2(-u.Y,u.X);
        var uv=p.Mirrored?new[]{Vector2.Zero,Vector2.UnitX,Vector2.One}:new[]{Vector2.Zero,Vector2.UnitX,Vector2.UnitY};
        return uv.Select(v=>p.Center+u*((v.X-.5f)*p.Size.X)+n*((v.Y-.5f)*p.Size.Y)).ToArray();
    }
    static bool Inside(Vector2 point,Vector2[] triangle,double tolerance)
    {
        var signs=new[]{Cross(triangle[1]-triangle[0],point-triangle[0]),Cross(triangle[2]-triangle[1],point-triangle[1]),Cross(triangle[0]-triangle[2],point-triangle[2])};
        return signs.All(s=>s>tolerance)||signs.All(s=>s < -tolerance);
    }
    public static int Run(string root)
    {
        _checks=0;
        var cases=new[]{new[]{new Vector2(0,0),new Vector2(100,0),new Vector2(20,80)},new[]{new Vector2(10,20),new Vector2(70,110),new Vector2(-60,80)},new[]{new Vector2(0,0),new Vector2(100,0),new Vector2(20,10)},new[]{new Vector2(-70,-40),new Vector2(-20,-35),new Vector2(-40,-90)}};
        foreach(var source in cases)foreach(bool reverse in new[]{false,true})
        {
            var a=source[0];var b=source[reverse?2:1];var c=source[reverse?1:2];var pieces=LcdTriangleGeometry.Decompose(a,b,c);
            Check(pieces.Length==2&&pieces.All(p=>p.Size.X>0&&p.Size.Y>0&&float.IsFinite(p.Rotation)),"generic triangle has two finite positive-size pieces");
            double area=Math.Abs(Cross(b-a,c-a))/2;double represented=pieces.Sum(p=>p.Size.X*(double)p.Size.Y/2);
            Check(Math.Abs(area-represented)<=Math.Max(.01,area*2e-6),"piece area equals original under both windings");
            var triangles=pieces.Select(Vertices).ToArray();
            Check(triangles.All(t=>Inside((t[0]+t[1]+t[2])/3,new[]{a,b,c},-1e-3)),"both rotated texture triangles lie in the original triangle");
            bool overlap=false;for(int y=-100;y<=120;y+=3)for(int x=-100;x<=120;x+=3)if(triangles.Count(t=>Inside(new Vector2(x+.31f,y+.27f),t,.01))>1)overlap=true;
            Check(!overlap,"pieces have disjoint interiors, preserving transparency without double blending");
        }
        Check(LcdTriangleGeometry.Decompose(Vector2.Zero,new Vector2(100,0),new Vector2(0,50)).Length==1,"right angle at A uses one native sprite");
        Check(LcdTriangleGeometry.Decompose(Vector2.Zero,new Vector2(100,0),new Vector2(100,50)).Length==1,"right angle at B uses one native sprite");
        Check(LcdTriangleGeometry.Decompose(Vector2.Zero,new Vector2(100,50),new Vector2(100,0)).Length==1,"right angle at C uses one native sprite");
        Check(LcdTriangleGeometry.Decompose(Vector2.Zero,Vector2.One,Vector2.One*2).Length==0,"degenerate triangle produces no pieces");
        LcdRectanglePiece rectangle;
        var ra=Vector2.Zero;var rb=new Vector2(200,0);var rc=new Vector2(200,40);var rd=new Vector2(0,40);
        Check(LcdTriangleGeometry.TryRectangle(ra,rb,rc,ra,rc,rd,out rectangle)&&rectangle.Center==new Vector2(100,20)&&Math.Abs(rectangle.Size.X*rectangle.Size.Y-8000)<.01,"two rectangle triangles merge without expansion or area change");
        Check(LcdTriangleGeometry.TryRectangle(rd,rb,ra,rd,rc,rb,out rectangle),"rectangle merge accepts reversed winding and either diagonal");
        Func<Vector2,Vector2> rotate=p=>new Vector2(p.X*.6f-p.Y*.8f+123,p.X*.8f+p.Y*.6f-17);
        Check(LcdTriangleGeometry.TryRectangle(rotate(ra),rotate(rb),rotate(rc),rotate(ra),rotate(rc),rotate(rd),out rectangle)&&Math.Abs(rectangle.Size.X*rectangle.Size.Y-8000)<.1,"rotated/transformed rectangle retains its exact area");
        Check(!LcdTriangleGeometry.TryRectangle(ra,rb,rc,ra,rb,rd,out rectangle),"shared side and overlapping triangles are not mistaken for a rectangle diagonal");
        Check(!LcdTriangleGeometry.TryRectangle(ra,rb,rc,ra,rc,new Vector2(20,40),out rectangle),"skewed quadrilateral remains on the triangle path");
        Check(!LcdTriangleGeometry.TryRectangle(ra,rb,rc,ra,rb,rc,out rectangle),"duplicate triangles do not lose their intended overlapping opacity");
        Check(!LcdTriangleGeometry.TryRectangle(ra,rb,rc,ra,rc,new Vector2(float.NaN,0),out rectangle),"nonfinite rectangle input is rejected");
        try{LcdTriangleGeometry.Decompose(new Vector2(float.NaN,0),Vector2.One,Vector2.Zero);throw new Exception("Expected finite rejection");}catch(ArgumentException){_checks++;}
        var method=typeof(HoloMapSession).GetMethod("LcdTriangle",BindingFlags.NonPublic|BindingFlags.Static);var sprites=new List<MySprite>();
        using(var frame=new MySpriteDrawFrame(f=>f.AddToList(sprites))){var args=new object[]{frame,new Vector2(0,0),new Vector2(100,0),new Vector2(20,80),new Color(10,20,30,40),Vector2.Zero,new Vector2(512),7};method.Invoke(null,args);Check((int)args[7]==5,"budget charges exactly two emitted sprites");}
        Check(sprites.Count==2&&sprites.All(s=>s.Data.StartsWith("HDRAPI_Triangle")&&s.Color.Value.A==40),"native texture sprites preserve supplied alpha");
        sprites.Clear();using(var frame=new MySpriteDrawFrame(f=>f.AddToList(sprites))){var args=new object[]{frame,new Vector2(0,0),new Vector2(100,0),new Vector2(20,80),Color.White,Vector2.Zero,new Vector2(512),1};method.Invoke(null,args);Check((int)args[7]==1,"insufficient budget leaves triangle atomic");}Check(sprites.Count==0,"no incomplete half triangle");
        sprites.Clear();using(var frame=new MySpriteDrawFrame(f=>f.AddToList(sprites))){var args=new object[]{frame,new Vector2(1000,1000),new Vector2(1100,1000),new Vector2(1020,1080),Color.White,Vector2.Zero,new Vector2(512),7};method.Invoke(null,args);}Check(sprites.Count==0,"fully offscreen triangles are culled before decomposition");
        var drawRectangle=typeof(HoloMapSession).GetMethod("LcdRectangle",BindingFlags.NonPublic|BindingFlags.Static);
        sprites.Clear();using(var frame=new MySpriteDrawFrame(f=>f.AddToList(sprites)))
        {
            var args=new object[]{frame,new LcdRectanglePiece{Center=new Vector2(1000),Size=new Vector2(200,40),Rotation=.7f},new Color(10,20,30,40),Vector2.Zero,new Vector2(512),1};
            drawRectangle.Invoke(null,args);Check((int)args[5]==1,"off-tile merged rectangles preserve the budget for later visible UI");
            args[1]=new LcdRectanglePiece{Center=new Vector2(256),Size=new Vector2(200,40),Rotation=.7f};
            drawRectangle.Invoke(null,args);Check((int)args[5]==0,"later visible merged rectangle consumes exactly one sprite");
        }
        Check(sprites.Count==1&&sprites[0].Data=="SquareSimple"&&sprites[0].Color.Value.A==40,"culling preserves subsequent rotated translucent rectangles");
        var left=File.ReadAllBytes(Path.Combine(root,"Mod","Textures","HDRAPI","TriangleLeft.dds"));var right=File.ReadAllBytes(Path.Combine(root,"Mod","Textures","HDRAPI","TriangleRight.dds"));
        Check(left.Length==128+256*256*4&&right.Length==left.Length&&BitConverter.ToUInt32(left,0)==0x20534444,"packaged RGBA triangle DDS dimensions and header");
        bool mirrored=true,white=true;long areaAlpha=0;for(int y=0;y<256;y++)for(int x=0;x<256;x++){int i=128+(y*256+x)*4,j=128+(y*256+255-x)*4;mirrored&=left[i+3]==right[j+3];white&=left[i]==255&&left[i+1]==255&&left[i+2]==255&&right[i]==255&&right[i+1]==255&&right[i+2]==255;areaAlpha+=right[i+3];}
        Check(mirrored&&white,"templates are exact horizontal mirrors with white tintable RGB");Check(Math.Abs(areaAlpha/(256.0*256*255)-.5)<.01,"alpha mask covers half its rectangle");
        return _checks;
    }
}
