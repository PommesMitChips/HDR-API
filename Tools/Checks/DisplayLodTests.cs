using System;
using HoloMap;
using VRageMath;

internal static class DisplayLodTests
{
    static int _checks;
    static void Check(bool truth,string name)
    { if(!truth)throw new Exception("Display LOD: "+name);_checks++; }
    static void Reject(Action action,string name)
    { try{action();}catch(ArgumentException){_checks++;return;}throw new Exception("Display LOD accepted "+name); }
    static readonly int[] Quad={0,1,2,0,2,3};
    static readonly Vector2[] Uv={new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
    static readonly MatrixD Projection=Perspective();
    static MatrixD Perspective()
    {
        var m=MatrixD.Identity;
        m.M34=1;m.M43=-1;m.M44=0;
        return m;
    }
    static Vector3D[] Plane(double cx,double cy,double z,double width=2,double height=2)
    {
        return new[]{new Vector3D(cx-width/2,cy-height/2,z),new Vector3D(cx+width/2,cy-height/2,z),
            new Vector3D(cx+width/2,cy+height/2,z),new Vector3D(cx-width/2,cy+height/2,z)};
    }
    static DisplayLodResult L(Vector3D[] points,Vector2I viewport,Vector2I requested,int previous=-1)
    { return DisplayLod.Evaluate(points,Quad,Projection,viewport,requested,previous); }
    static void Size(DisplayLodResult result,int width,int height,string name)
    { Check(result.Visible&&result.Resolution.X==width&&result.Resolution.Y==height,name); }
    public static int Run()
    {
        _checks=0;
        var higher=DisplayLod.MaximumVisible(new Vector2I(4096,2048),true);
        Size(higher,4096,2048,"camera panorama keeps its higher-detail ceiling");
        var ordinary=DisplayLod.MaximumVisible(new Vector2I(4096,2048));
        Check((long)ordinary.Resolution.X*ordinary.Resolution.Y<=1048576,"ordinary raster UI retains its existing memory bounds");
        var vp=new Vector2I(1920,1080);
        var req=new Vector2I(512,256);
        var near=L(Plane(0,0,2),vp,req);
        Size(near,512,256,"near plane uses requested pixels");
        Check(near.Level==0,"near level zero");
        var mid=L(Plane(0,0,10),vp,req);
        Size(mid,256,128,"middle distance halves resolution");
        var far=L(Plane(0,0,40),vp,req);
        Size(far,64,32,"far distance reduces by powers of two");
        Check(far.Level==3,"far level tracks power of two");
        var tiny=L(Plane(0,0,1000),vp,req);
        Size(tiny,32,16,"minimum side holds at sixteen");
        Check(tiny.Resolution.X*tiny.Resolution.Y<=DisplayLod.MaxPixels,"distant area bounded");
        var largerViewport=L(Plane(0,0,10),new Vector2I(3840,2160),req);
        Check(largerViewport.Visible&&largerViewport.Level<mid.Level,"4K viewer resolves more pixels than 1080p");
        var smallViewport=L(Plane(0,0,10),new Vector2I(960,540),req);
        Check(smallViewport.Visible&&smallViewport.Level>mid.Level,"small viewer resolves fewer pixels");
        Check(!L(Plane(20,0,2),vp,req).Visible,"right offscreen rejected");
        Check(!L(Plane(-20,0,2),vp,req).Visible,"left offscreen rejected");
        Check(!L(Plane(0,20,2),vp,req).Visible,"top offscreen rejected");
        Check(!L(Plane(0,-20,2),vp,req).Visible,"bottom offscreen rejected");
        var behind=L(Plane(0,0,-2),vp,req);
        Check(!behind.Visible&&behind.Resolution==Vector2I.Zero&&behind.Level==-1,"behind camera has no work");
        Check(!L(Plane(0,0,.5),vp,req).Visible,"near-clipped plane has no work");
        var crossing=new[]{new Vector3D(-8,-4,2),new Vector3D(8,-4,2),new Vector3D(0,8,2)};
        var crossed=DisplayLod.Evaluate(crossing,new[]{0,1,2},Projection,vp,req,-1);
        Size(crossed,512,256,"all vertices outside but triangle crosses frustum");
        var edge=L(Plane(9.9,0,10,2,2),vp,req);
        Check(edge.Visible,"partially clipped screen is visible");
        Check(edge.Resolution.X>=mid.Resolution.X,"edge clipping preserves uncut footprint");
        var sliver=L(Plane(10.99,0,10,2,2),vp,req);
        Check(sliver.Visible&&sliver.Resolution.X>=mid.Resolution.X,"tiny visible sliver retains source footprint");
        Check(!L(Plane(12,0,10,2,2),vp,req).Visible,"fully past edge is culled");
        Check(!L(Plane(11,0,10,2,2),vp,req).Visible,"zero-area edge contact has no work");
        var nearCross=Plane(0,0,2);nearCross[0].Z=.5;
        Check(L(nearCross,vp,req).Visible,"near-plane intersection survives clipping");
        Check(L(nearCross,vp,req).Resolution.X==512,"near-plane crossing conservatively uses full width");
        var tall=L(Plane(0,0,3,1,4),vp,new Vector2I(256,512));
        Check(tall.Visible&&tall.Resolution.Y==2*tall.Resolution.X,"portrait source aspect retained");
        var capped=DisplayLod.MaximumVisible(new Vector2I(4096,2160));
        Check(capped.Visible&&capped.Level==0,"maximum fallback visible");
        Check((long)capped.Resolution.X*capped.Resolution.Y<=DisplayLod.MaxPixels,"maximum pixel product bounded");
        Check(Math.Abs((double)capped.Resolution.X/capped.Resolution.Y-4096d/2160)<.01,"capped aspect close to source");
        Check(capped.Resolution.X<=4096&&capped.Resolution.Y<=2160,"capped output never exceeds request");
        var square=DisplayLod.MaximumVisible(new Vector2I(4096,4096));
        Size(square,1024,1024,"square 4K request caps at one megapixel");
        var already=DisplayLod.MaximumVisible(new Vector2I(128,64));
        Size(already,128,64,"small request remains intact");
        var narrow=DisplayLod.MaximumVisible(new Vector2I(16,1024));
        Check(narrow.Resolution.X==16&&narrow.Resolution.Y==1024,"maximum supported aspect fits side cap");
        var wide=DisplayLod.MaximumVisible(new Vector2I(4096,64));
        Size(wide,2048,32,"4096 source width with valid aspect respects both side limits");
        Check(DisplayLod.BoundResolution(new Vector2I(2048,1024),1024,1024,262144)==new Vector2I(724,362),
            "legacy client capability preserves aspect and its former pixel ceiling");
        Check(capped.Resolution.X<=DisplayLod.MaxSide&&capped.Resolution.Y<=DisplayLod.MaxSide,"each side stays within image cap");
        var hysteresisBase=L(Plane(0,0,10),vp,req);
        var slightGrow=L(Plane(0,0,9.3),vp,req,hysteresisBase.Level);
        Check(slightGrow.Level==hysteresisBase.Level,"small growth retains prior level");
        var strongGrow=L(Plane(0,0,6),vp,req,hysteresisBase.Level);
        Check(strongGrow.Level<hysteresisBase.Level,"substantial growth promotes level");
        var slightShrink=L(Plane(0,0,10.8),vp,req,hysteresisBase.Level);
        Check(slightShrink.Level==hysteresisBase.Level,"small shrink retains prior level");
        var strongShrink=L(Plane(0,0,24),vp,req,hysteresisBase.Level);
        Check(strongShrink.Level>hysteresisBase.Level,"substantial shrink demotes level");
        var uvLod=DisplayLod.Evaluate(Plane(0,0,10),Quad,Uv,Projection,vp,req,-1);
        Check(uvLod.Visible&&uvLod.Level==mid.Level,"UV-aware footprint matches planar bounds");
        var compressedUv=new[]{new Vector2(0,0),new Vector2(.5f,0),new Vector2(.5f,1),new Vector2(0,1)};
        var magnified=DisplayLod.Evaluate(Plane(0,0,10),Quad,compressedUv,Projection,vp,req,-1);
        Check(magnified.Level<uvLod.Level,"UV magnification requests more source pixels");
        var oblique=new[]{new Vector3D(-.05,-.05,1.1),new Vector3D(.5,-.05,10),
            new Vector3D(.5,.05,10),new Vector3D(-.05,.05,1.1)};
        var obliqueBounds=DisplayLod.Evaluate(oblique,Quad,Projection,vp,req,-1);
        var obliqueUv=DisplayLod.Evaluate(oblique,Quad,Uv,Projection,vp,req,-1);
        Check(obliqueBounds.Visible&&obliqueBounds.Level>=2,"oblique plane has small projected bounds");
        Check(obliqueUv.Visible&&obliqueUv.Level==0,"perspective UV density near the eye retains full resolution");
        var seamUv=new[]{Vector2.Zero,Vector2.Zero,Vector2.Zero,Vector2.Zero};
        Check(DisplayLod.Evaluate(Plane(0,0,10),Quad,seamUv,Projection,vp,req,-1).Level==0,
            "collapsed UVs conservatively choose maximum");
        Reject(()=>L(Plane(0,0,2),new Vector2I(0,1080),req),"zero viewport");
        Reject(()=>L(Plane(0,0,2),vp,new Vector2I(8,512)),"subminimum request");
        Reject(()=>DisplayLod.Evaluate(Plane(0,0,2),new[]{0,1,4},Projection,vp,req,-1),"invalid index");
        Reject(()=>L(Plane(0,0,2),vp,req,21),"invalid previous level");
        Reject(()=>DisplayLod.MaximumVisible(new Vector2I(4096,16)),"unrepresentable aspect and side cap");
        return _checks;
    }
}
