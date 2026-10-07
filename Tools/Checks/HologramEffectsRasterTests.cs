using System;
using HoloMap;
using VRageMath;

internal static class HologramEffectsRasterTests
{
    static int _checks;
    static void Check(bool condition,string message)
    { if(!condition)throw new Exception("Hologram raster effects: "+message);_checks++; }
    static SurfaceMesh Quad(bool clockwise=false)
    { return new SurfaceMesh{Geometry=new Geometry(new[]{new Vector3D(-1,-1,0),new Vector3D(1,-1,0),new Vector3D(1,1,0),new Vector3D(-1,1,0)},new int[0],clockwise?new[]{0,2,1,0,3,2}:new[]{0,1,2,0,2,3})}; }
    static RasterCanvas Canvas(long work=RasterCanvas.DefaultMaxWork)
    { var canvas=new RasterCanvas(32,32,2,2,work,4);canvas.Clear(Vector4.Zero);return canvas; }
    static HologramEffectSettings Settings(Action<double[],int[]> change)
    {var v=HologramEffectSettings.DefaultValues();var f=HologramEffectSettings.DefaultFlags();change(v,f);return HologramEffectSettings.Create(v,f);}
    static byte[] Draw(SurfaceMesh mesh,HologramEffectSettings settings,double time,bool entering=true,MatrixD? transform=null)
    {var canvas=Canvas();canvas.Add(mesh,transform??MatrixD.Identity,new Vector4(.25f,.5f,.75f,.5f),Vector4.Zero,0,1,settings,HologramEffectKernel.Evaluate(settings,time,0,entering));return canvas.Finish();}
    static int Alpha(byte[] bytes,int x,int y){return bytes[(y*32+x)*4+3];}
    static void DefaultsPreserveContent()
    {
        var mesh=Quad();for(int i=0;i<mesh.Geometry.WorldPoints.Length;i++)mesh.Geometry.WorldPoints[i]=new Vector3D(77,88,99);
        var regular=Canvas();regular.Add(mesh,MatrixD.Identity,new Vector4(.25f,.5f,.75f,.5f),Vector4.Zero,0,1);
        var effect=Canvas();var settings=Settings((v,f)=>{});effect.Add(mesh,MatrixD.Identity,new Vector4(.25f,.5f,.75f,.5f),Vector4.Zero,0,1,settings,HologramEffectKernel.Evaluate(settings,5,0,true));
        var a=regular.Finish();var b=effect.Finish();for(int i=0;i<a.Length;i++)Check(a[i]==b[i],"neutral settings preserve every RGBA channel");
        Check(regular.WorkUsed==effect.WorkUsed,"sampling shares existing bounded work accounting");
        foreach(var p in mesh.Geometry.WorldPoints)Check(p==new Vector3D(77,88,99),"effect never writes source renderer scratch points");
    }
    static void PerSampleWipeAndRotation()
    {
        var settings=Settings((v,f)=>{v[16]=1;v[17]=0;f[5]=2;f[6]=0;});
        var image=Draw(Quad(),settings,.5);var reverse=Draw(Quad(true),settings,.5);
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)
        {
            Check(Alpha(image,x,y)==(y<16?0:128),"wipe clips coverage inside each triangle without diagonal alpha seams");
            Check(Alpha(image,x,y)==Alpha(reverse,x,y),"barycentric effect coordinates survive opposite winding");
        }
        var rotated=Draw(Quad(),settings,.5,true,MatrixD.CreateRotationZ(Math.PI/2));
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)Check(Alpha(rotated,x,y)==(x<16?0:128),"spatial effect follows source orientation through transform");
        var exit=Draw(Quad(),settings,.5,false);for(int y=0;y<32;y++)for(int x=0;x<32;x++)Check(Alpha(image,x,y)+Alpha(exit,x,y)==128,"outgoing wipe complements incoming coverage");
    }
    static void ScanAndFade()
    {
        var settings=Settings((v,f)=>{v[2]=.8;v[3]=.31;v[4]=.15;v[5]=3;v[0]=.6;});
        double time=2.345;var frame=HologramEffectKernel.Evaluate(settings,time,0,true);var sample=HologramEffectKernel.Prepare(settings,frame);var image=Draw(Quad(),settings,time);
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)
        {
            double alpha=0;
            for(int s=0;s<4;s++)alpha+=sample.Alpha(1-(y+(s<2?.25:.75))/32,0)*.5;
            int expected=(int)Math.Floor(alpha/4*255+.5);
            Check(Math.Abs(Alpha(image,x,y)-expected)<=1,"moving scan band and frozen flicker sampled at coverage positions");
        }
        var fade=Settings((v,f)=>{v[16]=1;f[5]=1;f[6]=0;});var faded=Draw(Quad(),fade,.25);
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)Check(Alpha(faded,x,y)==32,"fade opacity applies once across shared quad diagonal");
    }
    static void AtomicFailureAndBounds()
    {
        var mesh=Quad();var settings=Settings((v,f)=>{v[2]=.5;});var canvas=Canvas(1);var before=canvas.Finish();
        bool rejected=false;try{canvas.Add(mesh,MatrixD.Identity,Vector4.One,Vector4.Zero,0,1,settings,HologramEffectKernel.Evaluate(settings,2,0,true));}catch(ArgumentException){rejected=true;}
        Check(rejected,"effect work still rejects beyond compositor budget");Check(canvas.WorkUsed==0,"failed effect Add leaves sample charge untouched");Check(Object.ReferenceEquals(before,canvas.Finish()),"failed effect Add leaves prior pixel publication untouched");
        var coordinates=HologramRasterEffects.Coordinates(mesh.Geometry,settings);Check(coordinates[0]==0&&coordinates[1]==0&&coordinates[2]==1&&coordinates[3]==1,"normalized local bounds independent of render pose");
    }
    static void PhysicalLcdBandClipping()
    {
        var mesh=Quad();var settings=Settings((v,f)=>{v[2]=.5;});Vector3D min,max;HologramRasterEffects.Bounds(mesh.Geometry,out min,out max);
        var input=new Vector3D[8];var output=new Vector3D[8];double area=0;
        for(int t=0;t<6;t+=3)
        {
            for(int i=0;i<3;i++)input[i]=mesh.Geometry.Points[mesh.Geometry.Triangles[t+i]];
            int count=HologramRasterEffects.ClipBand(input,3,output,settings,min,max,.3,false);
            count=HologramRasterEffects.ClipBand(output,count,input,settings,min,max,.4,true);
            Check(count>=3&&count<=5,"a narrow bar creates a bounded transient convex polygon inside a large triangle");
            for(int i=0;i<count;i++){double c=HologramEffectKernel.Coordinate(settings,input[i],min,max);Check(c>=.3-1e-12&&c<=.4+1e-12,"LCD band clips its actual refresh bar boundaries");}
            for(int i=1;i+1<count;i++)area+=Vector3D.Cross(input[i]-input[0],input[i+1]-input[0]).Length()*.5;
        }
        Check(Math.Abs(area-.4)<1e-12,"LCD refresh overlay covers a narrow strip across both quad halves without overlap");
    }
    static void StrokesAndInvalidFrame()
    {
        var settings=Settings((v,f)=>{v[16]=1;v[17]=0;v[18]=1;v[19]=0;f[5]=2;f[6]=0;});
        var mesh=new SurfaceMesh{Geometry=new Geometry(new[]{new Vector3D(-1,0,0),new Vector3D(1,0,0)},new[]{0,1},new int[0])};
        var canvas=Canvas();canvas.Add(mesh,MatrixD.Identity,Vector4.Zero,new Vector4(1,0,0,.5f),.125f,1,settings,HologramEffectKernel.Evaluate(settings,.5,0,true));var pixels=canvas.Finish();
        for(int x=0;x<32;x++)Check(Alpha(pixels,x,16)==(x<16?128:0),"stroke samples interpolate source-coordinate reveal along the segment");
        var safe=Canvas();var original=safe.Finish();bool rejected=false;
        try{safe.Add(Quad(),MatrixD.Identity,Vector4.One,Vector4.Zero,0,1,settings,new HologramEffectFrame(double.NaN,1,1,0,1,true));}catch(ArgumentException){rejected=true;}
        Check(rejected&&safe.WorkUsed==0&&Object.ReferenceEquals(original,safe.Finish()),"invalid shared frame is rejected before work or pixels mutate");
    }
    static void HostileParticleSprite()
    {
        var matrix=MatrixD.Identity;matrix.M11=1e308;
        var position=Vector3D.Transform(new Vector3D(0,.25,0),matrix);
        Check(HologramEffectKernel.PointValid(position),"degenerate vertical source stays finite despite huge unused scale");
        double scale=Vector3D.TransformNormal(Vector3D.UnitX,matrix).Length();Vector2 extent;
        Check(!HologramRasterEffects.TryParticleSprite(new Vector2(16,16),.02*scale*32,1,out extent)&&extent==Vector2.Zero,"overflowed particle scale rejected before sprite publication");
        Check(!HologramRasterEffects.TryParticleSprite(new Vector2(float.PositiveInfinity,0),1,1,out extent),"nonfinite particle center rejected");
        Check(!HologramRasterEffects.TryParticleSprite(Vector2.Zero,double.NaN,1,out extent),"NaN particle extent rejected");
        Check(!HologramRasterEffects.TryParticleSprite(Vector2.Zero,double.MaxValue,1,out extent),"double extent cannot overflow float cast");
        Check(!HologramRasterEffects.TryParticleSprite(new Vector2(float.MaxValue,0),float.MaxValue,1,out extent),"sprite corners cannot overflow even when center and extent independently finite");
        Check(!HologramRasterEffects.TryParticleSprite(Vector2.Zero,0,1,out extent),"collapsed particle width suppressed");
        Check(HologramRasterEffects.TryParticleSprite(new Vector2(16,16),2,3,out extent)&&extent==new Vector2(2,3),"ordinary finite particle retains expected bounds");
    }
    public static int Run()
    {_checks=0;DefaultsPreserveContent();PerSampleWipeAndRotation();ScanAndFade();AtomicFailureAndBounds();PhysicalLcdBandClipping();StrokesAndInvalidFrame();HostileParticleSprite();return _checks;}
}
