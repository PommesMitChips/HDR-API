using System.Collections;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

internal static class CameraSourceDemandIntegrationTests
{
    static int checks;
    static void Check(bool ok,string why){if(!ok)throw new Exception("Camera density service: "+why);checks++;}
    static object New(string name)=>Activator.CreateInstance(typeof(HoloMapSession).GetNestedType(name,BindingFlags.NonPublic)!,true)!;
    static object Field(object o,string n)=>ClientReplicationTests.Field(o,n);
    static void Set(object o,string n,object v)=>ClientReplicationTests.SetField(o,n,v);
    static object Call(object o,string n,params object[] a)=>ClientReplicationTests.Call(o,n,a);
    static readonly double[] Identity={1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
    static readonly double[] Bases={1d,0,0,0,1,0,0,0,1,1,1,-1,0,0,0,1,0,0,0,-1,1,1};
    static SurfaceMesh Mesh(bool rear=false)
    {
        float lo=rear?.01f:.45f,hi=rear?.05f:.55f;
        return new SurfaceMesh{Geometry=new Geometry(new[]{new Vector3D(-.4,-.4,.5),new Vector3D(.4,-.4,.5),new Vector3D(.4,.4,.5),new Vector3D(-.4,.4,.5)},Array.Empty<int>(),new[]{0,1,2,0,2,3}),UV=new[]{new Vector2(lo,.55f),new Vector2(hi,.55f),new Vector2(hi,.45f),new Vector2(lo,.45f)}};
    }
    sealed class Fixture:IDisposable
    {
        public readonly ClientReplicationTests.GatewayScope Gateway=new();
        public readonly HoloMapSession Session=new();
        public readonly object Scene=New("Scene");
        public MatrixD View=MatrixD.Identity,World=MatrixD.Identity;
        public Vector2 Viewport=new(800,600);
        public bool Fault;
        public Fixture()
        {
            Set(Scene,"ConsoleId",20L);((IDictionary)Field(Session,"_scenes"))[20L]=Scene;Set(Session,"_displaySourceRegistered",true);
            object anchor=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>m.Name switch{"get_Closed"=>false,"get_WorldMatrix"=>Fault?throw new InvalidOperationException("Injected transform failure"):World,_=>throw new Exception(m.Name)});
            Gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"&&(long)a[0]==20?anchor:null);
            Gateway.Install("Session",(m,a)=>m.Name=="get_Camera"?DrawTestProxy.Make(m.ReturnType,(cm,ca)=>cm.Name switch{
                "get_Position"=>new Vector3D(0,0,2),"get_ViewMatrix"=>View,"get_ProjectionMatrix"=>MatrixD.Identity,
                "get_ViewportSize"=>cm.ReturnType==typeof(Vector2I)?new Vector2I((int)Viewport.X,(int)Viewport.Y):Viewport,
                _=>throw new Exception(cm.Name)}):throw new Exception(m.Name));
        }
        public HoloProjectedScreenData Add(string id,string source="1,2",bool rear=false,long caller=10)
        {
            var d=new HoloProjectedScreenData{CallerId=caller,Id=id,Pose=(double[])Identity.Clone(),SourceProvider="camera-panorama",SourceId=source,SourceCaptureResolution=512};
            var screen=New("ProjectedScreen");Set(screen,"Data",d);((IDictionary)Field(Scene,"Screens"))[caller+":"+id]=screen;
            var cache=New("ProjectedCache");Set(cache,"Anchor",20L);Set(cache,"Caller",caller);Set(cache,"Id",id);
            var mapped=New("MappedScreenMesh");Set(mapped,"Settings",d);Set(mapped,"Mesh",Mesh(rear));Set(mapped,"Depth",.0005d);Set(mapped,"Seen",(int)Field(Session,"_ticks"));
            ((IDictionary)Field(cache,"Mapped"))["source"]=mapped;((IDictionary)Field(Session,"_projectedCaches"))["20:"+caller+":"+id]=cache;
            return d;
        }
        public MyTuple<int,double[],bool> Query(string source="1,2",long caller=10)=>(MyTuple<int,double[],bool>)Call(Session,"DisplaySourceService","source-cameradensity",new object[]{"camera-panorama",20L,caller,source,(double[])Bases.Clone()});
        public void Tick(int value)=>Set(Session,"_ticks",value);
        public void Dispose()=>Gateway.Dispose();
    }
    static int Samples(MyTuple<int,double[],bool> p)=>Enumerable.Range(0,(p.Item2.Length-4)/7).Count(i=>p.Item2[4+i*7+6]==1);
    static void Conservative(MyTuple<int,double[],bool> p,int mask,bool known,string why)
    {
        Check(p.Item1==mask&&p.Item3==known,why+" mask/proof");Check(p.Item2.Length==4+2*256*7&&p.Item2[0]==16&&p.Item2[1]==2,why+" bounded packet shape");
        for(int camera=0;camera<2;camera++)
        {
            bool visible=(mask&(1<<camera))!=0;Check(p.Item2[2+camera]==(visible?512:0),why+" suggested size");
            for(int cell=0;cell<256;cell++)
            {
                int start=4+(camera*256+cell)*7;
                Check(p.Item2[start]==(visible?1:0)&&p.Item2[start+1]==(visible?512:0)&&p.Item2[start+2]==(visible?512d*512:0)&&p.Item2[start+6]==0,why+" conservative cell");
            }
        }
    }
    public static int Run()
    {
        checks=0;CurrentVisibilityAndStaleDensity();SourceUnionAndFallback();GlobalFairnessAndCacheBounds();OwnerIsolation();MapperEvidenceAndPreciseCrop();MaskAndFieldAgreement();MalformedAndRetirement();return checks;
    }
    static void CurrentVisibilityAndStaleDensity()
    {
        using var f=new Fixture();f.Add("front");var fresh=f.Query();Check(fresh.Item1==1&&fresh.Item3&&Samples(fresh)>0,"first visible source has actual local samples");
        fresh.Item2[0]=999;fresh.Item2[4]=999;var repeat=f.Query();Check(repeat.Item2[0]==16&&repeat.Item2[4]!=999,"provider cannot mutate cached packet storage");
        f.Tick(1);f.View=MatrixD.CreateRotationY(Math.PI);Conservative(f.Query(),0,true,"same-tick viewer rotation removes offscreen demand immediately");
        f.View=MatrixD.Identity;f.Add("front",rear:true);Conservative(f.Query(),2,true,"new camera footprint before density allowance");
        f.Tick(29);Conservative(f.Query(),2,true,"stale density never labels old samples current");
        f.Tick(30);var newer=f.Query();Check(newer.Item1==2&&newer.Item3&&Samples(newer)>0,"density refreshes at bounded interval");
        f.Tick(31);f.Viewport=new Vector2(1600,900);Conservative(f.Query(),2,true,"viewport change invalidates density immediately");
        f.World=MatrixD.CreateTranslation(10,0,0);Conservative(f.Query(),0,true,"anchor movement recomputes current visibility");
    }
    static void SourceUnionAndFallback()
    {
        using var f=new Fixture();var front=f.Add("front");f.Query();f.Add("rear",rear:true);var both=f.Query();Check(both.Item1==3&&both.Item3,"all matching screens union demand within same frame");
        front.Visible=false;Check(f.Query().Item1==2,"hiding one consumer retains other consumer demand");
        ((IDictionary)Field(f.Session,"_projectedCaches")).Remove("20:10:rear");Conservative(f.Query(),3,false,"missing displayed mesh cannot incorrectly hide sources");
        f.Add("rear",rear:true);f.Fault=true;Conservative(f.Query(),3,false,"projection fault retains conservative demand");f.Fault=false;
        var d=f.Add("rear",rear:true);d.SurfaceClip=new[]{1d,0,0,-2};Conservative(f.Query(),0,true,"anchor crop removes all demand without stale reuse");
    }
    static void GlobalFairnessAndCacheBounds()
    {
        using var f=new Fixture();f.Add("first");Check(Samples(f.Query())>0,"first context gets initial density slot");f.Add("second","3,4",true);Conservative(f.Query("3,4"),2,true,"second context shares global allowance");
        f.Tick(30);f.Viewport=new Vector2(801,600);Conservative(f.Query(),1,true,"first caller yields density slot to older pending context");Check(Samples(f.Query("3,4"))>0,"pending context is not starved by first caller");
        f.Tick(60);Check(Samples(f.Query())>0,"density slots rotate back to first context");
        for(int i=0;i<18;i++){string source=(100+i*2)+","+(101+i*2);f.Add("extra",source);f.Query(source);}
        Check(((IDictionary)Field(f.Session,"_cameraDemandCaches")).Count==16,"cache admission evicts one entry instead of clearing all contexts");
        Check((int)Field(f.Session,"_cameraDensityNextTick")==90,"new sources and cache eviction cannot bypass global density work allowance");
    }
    static void MalformedAndRetirement()
    {
        using var f=new Fixture();f.Add("front");
        object Query(params object[] a)=>Call(f.Session,"DisplaySourceService","source-cameradensity",a);
        Check(Query("camera-panorama",20L,11L,"1,2",Bases) is false,"foreign declaration owner rejected");
        Check(Query("camera-panorama",20,10L,"1,2",Bases) is false,"wrong anchor primitive type rejected");
        Check(Query("camera-panorama",20L,10L,"1,2",new double[11]) is false,"wrong lens count rejected");
        var invalid=(double[])Bases.Clone();invalid[0]=double.NaN;Check(Query("camera-panorama",20L,10L,"1,2",invalid) is false,"nonfinite lens rejected");
        invalid=(double[])Bases.Clone();invalid[0]=2;Check(Query("camera-panorama",20L,10L,"1,2",invalid) is false,"non-unit source axes rejected");
        f.Query();Set(f.Session,"_displaySourceRegistered",false);Check(Query("camera-panorama",20L,10L,"1,2",Bases) is false,"retired service cannot return cached demand");
        Call(f.Session,"UnregisterDisplaySources");Check(((IDictionary)Field(f.Session,"_cameraDemandCaches")).Count==0,"retirement releases density inputs and packets even after registration stops");
    }
    static void OwnerIsolation()
    {
        using var f=new Fixture();f.Add("first");f.Query();var other=f.Add("second",rear:true,caller:11);other.SourceCaptureResolution=256;
        var foreign=f.Query(caller:11);Check(foreign.Item1==2&&foreign.Item3&&foreign.Item2[3]==256,"same source ID under another PB has its own visibility and cap");
        var original=f.Query();Check(original.Item1==1&&original.Item2[2]<=512&&Samples(original)>0,"another owner's settings cannot replace cached density");
        f.Add("conflict");var conflict=(HoloProjectedScreenData)Field(((IDictionary)Field(f.Scene,"Screens"))["10:conflict"],"Data");conflict.SourceFov=110;
        Check(Call(f.Session,"DisplaySourceService","source-cameradensity",new object[]{"camera-panorama",20L,10L,"1,2",Bases}) is false,"conflicting in-memory declarations never publish an arbitrary first setting");
    }
    static void MaskAndFieldAgreement()
    {
        var lenses=new[]{new CameraDensityLens{CaptureMax=512},new CameraDensityLens{Right=-Vector3D.UnitX,Forward=-Vector3D.UnitZ,CaptureMax=512}};
        var field=CameraDensityMap.Evaluate(null,null,null,MatrixD.Identity,new Vector2I(800,600),null,lenses);
        field.VisibleMask=2;field.Cameras[1].SuggestedResolution=0;Array.Clear(field.Cameras[1].Cells);
        var packed=(MyTuple<int,double[],bool>)typeof(HoloMapSession).GetMethod("PackCameraDensity",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{field,true})!;
        Conservative(packed,2,true,"broader cheap mask and finer density field disagreement");
    }
    static void MapperEvidenceAndPreciseCrop()
    {
        using var f=new Fixture();var d=f.Add("first");f.Query();d.TwoSided=true;
        d.Pose=(double[])Identity.Clone();d.Pose[14]=3;
        Conservative(f.Query(),3,false,"crossing a two-sided surface invalidates the old layer-depth mesh");
        d.Pose=(double[])Identity.Clone();d.PanoramaGroup="join";
        Conservative(f.Query(),3,false,"new joined panorama cannot reuse an unjoined mapped mesh");
        d.PanoramaGroup=null;
        double bound=1.00000003;d.SurfaceClip=new[]{1d,0,0,bound};var pose=MatrixD.CreateTranslation(.000000001,0,0);
        var planes=(Vector4D[])typeof(HoloMapSession).GetMethod("CameraDensityCrop",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{d,pose})!;
        Check(planes[0].W==bound-.000000001&&planes[0].W!=(double)(float)planes[0].W,"tangent crop retains original double boundary instead of narrowing to float");
        var local=new Vector3D(planes[0].W-1e-10,0,0);var anchor=Vector3D.Transform(local,pose);
        Check(Vector3D.Dot(new Vector3D(planes[0].X,planes[0].Y,planes[0].Z),local)<=planes[0].W&&anchor.X<=bound,"transformed local crop agrees with anchor-space near-boundary witness");
    }
}
