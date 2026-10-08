using System.Collections;
using System.Reflection;
using HoloMap;
using Sandbox.ModAPI;

internal static class ProjectedScreenNetworkTests
{
    static int _checks;
    static void Check(bool value,string name){if(!value)throw new Exception("Projected network: "+name);_checks++;}
    static object Call(object target,string name,params object[] args)=>ClientReplicationTests.Call(target,name,args);
    static object Field(object target,string name)=>ClientReplicationTests.Field(target,name);
    static object Static(string name,params object[] args)
    {
        try{return typeof(HoloMapSession).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
        catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    static HoloProjectedScreenData Screen(string id="screen")=>new HoloProjectedScreenData{
        CallerId=10,Id=id,Pose=new[]{1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1},
        SourcePoints=new[]{0d,0,0,1,0,0,0,1,0},SourceTriangles=new[]{0,1,2},SourceColors=new[]{0f,1,1,1}};
    static HoloSnapshot Snapshot(long id=20)
    {
        var result=ClientReplicationTests.Snapshot(id);result.Scenes[0].Screens=new List<HoloProjectedScreenData>{Screen()};
        result.Scenes[0].Items[0].Id="!s!screen!line";return result;
    }
    static T RoundTrip<T>(T value)=>MyAPIGateway.Utilities.SerializeFromBinary<T>(MyAPIGateway.Utilities.SerializeToBinary(value));
    static object Scene(HoloMapSession session,long id=20)=>((IDictionary)Field(session,"_scenes"))[id];
    static void RejectAtomic(HoloMapSession session,HoloSnapshot bad,string name)
    {
        object prior=Scene(session);
        try{Call(session,"ApplySnapshot",RoundTrip(bad));}
        catch(ArgumentException error){
            if(name=="global projected declaration replication budget")Check(error.Message.Contains("Global display replication budget"),"byte-budget fixture passes individual point, primitive and screen caps");
            Check(ReferenceEquals(prior,Scene(session)),name+" preserves prior scene atomically");return;}
        throw new Exception("Projected network expected rejection: "+name);
    }
    public static int Run()
    {
        _checks=0;using var gateway=new ClientReplicationTests.GatewayScope();
        var baseline=RoundTrip(Snapshot());var session=new HoloMapSession();Call(session,"ApplySnapshot",baseline);
        Check(((IDictionary)Field(Scene(session),"Screens")).Count==1,"protobuf full snapshot imports screen");
        var copy=baseline.Scenes[0].Screens[0];Check(copy.SourcePoints.Length==9&&copy.SourceTriangles.Length==3&&copy.SourceColors[3]==1,"protobuf preserves owned synthetic mesh and opaque colors");
        var before=(HoloSnapshot)Call(session,"CaptureSnapshot");
        var current=(HoloProjectedScreenData)Field(((IDictionary)Field(Scene(session),"Screens"))["10:screen"],"Data");
        current.Opacity=.4f;current.Pose[12]=.3;current.TwoSided=true;current.FrontOpacity=0;current.BackOpacity=.35f;
        current.SurfaceClip=new[]{0d,0,-1,-1.5};
        current.SourceFov=108;current.SourceFeather=12;current.SourceSaturation=1.25;current.SourceCaptureResolution=512;
        var after=(HoloSnapshot)Call(session,"CaptureSnapshot");
        Check(before.Scenes[0].Screens[0].Opacity==1&&before.Scenes[0].Screens[0].Pose[12]==0,"published screen baseline freezes mutable state");
        var delta=(HoloSnapshot)Static("BuildDelta",before,after,1L);
        Check(delta.Scenes.Count==1&&delta.Scenes[0].Partial&&delta.Scenes[0].Items.Count==0,"screen metadata update produces delta without item geometry");
        var merged=(HoloSnapshot)Static("MergeDelta",before,RoundTrip(delta),1L);Call(session,"ApplySnapshot",merged);
        Check(merged.Scenes[0].Screens[0].Opacity==.4f&&merged.Scenes[0].Screens[0].Pose[12]==.3,"screen metadata delta applies");
        Check(before.Scenes[0].Screens[0].SurfaceClip==null&&merged.Scenes[0].Screens[0].SurfaceClip.SequenceEqual(current.SurfaceClip),"crop planes travel in metadata deltas without changing the published baseline");
        Check(before.Scenes[0].Screens[0].SourceFov==105&&merged.Scenes[0].Screens[0].SourceFov==108&&merged.Scenes[0].Screens[0].SourceFeather==12&&merged.Scenes[0].Screens[0].SourceSaturation==1.25&&merged.Scenes[0].Screens[0].SourceCaptureResolution==512,"camera lens, feather, colour and resolution settings replicate as frozen metadata");
        var zeroSettings=Snapshot();zeroSettings.Scenes[0].Screens[0].SourceFeather=0;zeroSettings.Scenes[0].Screens[0].SourceSaturation=0;
        var zeroRoundTrip=RoundTrip(zeroSettings).Scenes[0].Screens[0];
        Check(zeroRoundTrip.SourceFeather==0&&zeroRoundTrip.SourceSaturation==0,"zero feather and greyscale remain zero instead of restoring constructor defaults in network serialization");
        current.SurfaceClip[3]=-2;
        Check(after.Scenes[0].Screens[0].SurfaceClip[3]==-1.5,"published crop planes are detached from mutable source data");
        Check(before.Scenes[0].Screens[0].FrontOpacity==1&&!before.Scenes[0].Screens[0].TwoSided&&merged.Scenes[0].Screens[0].TwoSided&&merged.Scenes[0].Screens[0].FrontOpacity==0&&merged.Scenes[0].Screens[0].BackOpacity==.35f,"screen delta freezes old sidedness and preserves zero/front and nonzero/back opacity");
        var late=RoundTrip(after);var joined=new HoloMapSession();Call(joined,"ApplySnapshot",late);
        Check(((IDictionary)Field(Scene(joined),"Screens")).Count==1,"late join receives current projected declaration");
        var joinedData=(HoloProjectedScreenData)Field(((IDictionary)Field(Scene(joined),"Screens"))["10:screen"],"Data");Check(joinedData.TwoSided&&joinedData.FrontOpacity==0&&joinedData.BackOpacity==.35f,"late join preserves two-sided presentation and fully transparent primary side");
        var removed=RoundTrip(after);removed.Scenes[0].Screens.Clear();removed.Scenes[0].Items.Clear();
        var removeDelta=(HoloSnapshot)Static("BuildDelta",after,removed,2L);
        Call(session,"ApplySnapshot",Static("MergeDelta",after,RoundTrip(removeDelta),2L));
        Check(((IDictionary)Field(Scene(session),"Screens")).Count==0&&((IDictionary)Field(Scene(session),"Items")).Count==0,"screen and owned content deletion replicate together");
        Call(session,"ApplySnapshot",RoundTrip(Snapshot()));
        void Invalid(Action<HoloSnapshot> mutate,string name){var bad=Snapshot();mutate(bad);RejectAtomic(session,bad,name);}
        Invalid(s=>s.Scenes[0].Screens[0].CallerId=0,"missing screen owner");
        Invalid(s=>s.Scenes[0].Screens[0].Pose[0]=-1,"mirrored screen pose");
        Invalid(s=>s.Scenes[0].Screens[0].Pose[12]=double.NaN,"nonfinite pose");
        Invalid(s=>s.Scenes[0].Screens.Add(Screen()),"duplicate screen identity");
        Invalid(s=>s.Scenes[0].Screens=null,"omitted screen declarations orphan content");
        Invalid(s=>s.Scenes[0].Items[0].CallerId=11,"foreign owner cannot borrow projected namespace");
        Invalid(s=>s.Scenes[0].Screens[0].SourceTriangles[0]=99,"invalid source triangle index");
        Invalid(s=>s.Scenes[0].Screens[0].SourceColors[3]=.5f,"transparent perspective source rejected");
        Invalid(s=>s.Scenes[0].Screens[0].Camera[10]=s.Scenes[0].Screens[0].Camera[11],"invalid near/far planes");
        Invalid(s=>s.Scenes[0].Screens[0].RasterWidth=129,"raster width cap");
        Invalid(s=>s.Scenes[0].Screens[0].FrontOpacity=float.NaN,"nonfinite primary-side opacity");
        Invalid(s=>s.Scenes[0].Screens[0].BackOpacity=1.01f,"back-side opacity upper bound");
        Invalid(s=>s.Scenes[0].Screens[0].BackOpacity=-.1f,"back-side opacity lower bound");
        Invalid(s=>s.Scenes[0].Screens[0].SourceFov=double.NaN,"nonfinite camera FOV");
        Invalid(s=>s.Scenes[0].Screens[0].SourceFov=121,"camera FOV upper bound");
        Invalid(s=>s.Scenes[0].Screens[0].SourceFeather=26,"camera feather upper bound");
        Invalid(s=>s.Scenes[0].Screens[0].SourceSaturation=-1,"camera saturation lower bound");
        Invalid(s=>s.Scenes[0].Screens[0].SourceCaptureResolution=4096,"native camera capture size bound");
        Invalid(s=>{s.Scenes[0].Screens[0].SourceProvider="camera-panorama";s.Scenes[0].Screens[0].SourceId="123,123";},"duplicate camera IDs");
        Invalid(s=>{s.Scenes[0].Screens[0].SourceProvider="camera-panorama";s.Scenes[0].Screens[0].SourceId="1,2,3,4,5,6,7";},"camera count bound");
        Invalid(s=>s.Scenes[0].Screens[0].SurfaceClip=new double[3],"incomplete crop plane");
        Invalid(s=>s.Scenes[0].Screens[0].SurfaceClip=new double[36],"crop plane count bound");
        Invalid(s=>s.Scenes[0].Screens[0].SurfaceClip=new[]{0d,0,0,1},"zero crop normal");
        Invalid(s=>s.Scenes[0].Screens[0].SurfaceClip=new[]{0d,0,1,double.NaN},"nonfinite crop distance");
        Invalid(s=>s.Scenes[0].Screens[0].SurfaceClip=new[]{double.MaxValue,0,0,0},"overflowing crop normal");
        Invalid(s=>s.Scenes[0].Screens[0].SurfaceClip=new[]{0d,0,1,51},"crop distance bound");
        Invalid(s=>{s.Scenes[0].Screens.Clear();for(int i=0;i<5;i++)s.Scenes[0].Screens.Add(Screen("s"+i));},"per anchor screen cap");
        Invalid(s=>{
            s.Scenes.Clear();for(int anchor=0;anchor<5;anchor++){var scene=ClientReplicationTests.Scene(20+anchor);scene.Items.Clear();scene.Screens=new List<HoloProjectedScreenData>();for(int i=0;i<(anchor==4?1:4);i++)scene.Screens.Add(Screen("s"+i));s.Scenes.Add(scene);}
        },"global 16 screen cap");
        Invalid(s=>{
            var scene=s.Scenes[0];scene.Items.Clear();scene.Items.Add(ClientReplicationTests.Item());scene.Screens=new List<HoloProjectedScreenData>();
            for(int i=0;i<2;i++){var screen=Screen("s"+i);screen.SourcePoints=new double[2048*3];scene.Screens.Add(screen);}
        },"native and projected meshes share point cap");
        Invalid(s=>{
            var scene=s.Scenes[0];scene.Screens=new List<HoloProjectedScreenData>();scene.Items[0].Id="line";
            for(int i=0;i<2;i++){var screen=Screen("s"+i);screen.SourceTriangles=new int[4096*3];screen.SourceColors=Enumerable.Range(0,4096*4).Select(n=>n%4==3?1f:0f).ToArray();scene.Screens.Add(screen);}
        },"native and projected meshes share primitive cap");
        Invalid(s=>{
            s.Scenes.Clear();for(int anchor=0;anchor<8;anchor++){var scene=ClientReplicationTests.Scene(20+anchor);scene.Items.Clear();scene.Screens=new List<HoloProjectedScreenData>();for(int i=0;i<2;i++){var screen=Screen("s"+i);screen.SourcePoints=new double[2048*3];screen.SourceTriangles=new int[4094*3];screen.SourceColors=Enumerable.Range(0,4094*4).Select(n=>n%4==3?1f:0f).ToArray();scene.Screens.Add(screen);}s.Scenes.Add(scene);}
        },"global projected declaration replication budget");
        int protocol=(int)typeof(HoloMapSession).GetField("NetworkProtocol",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
        Check(protocol==19,"portal, effect and numeric interaction declarations use explicit protocol");
        return _checks;
    }
}
