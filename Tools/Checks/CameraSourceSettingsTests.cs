using System.Collections;
using System.Reflection;
using HoloMap;
using VRage;
using VRageMath;
using Sandbox.ModAPI;

internal static class CameraSourceSettingsTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string reason) { if (!ok) throw new Exception("Camera source settings: " + reason); checks++; }
        object Static(string method, params object[] args) {
            try { return typeof(HoloMapSession).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args); }
            catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw(); throw; }
        }
        void Reject(string provider, string source) {
            try { Static("ValidateDisplaySource", provider, source); throw new Exception("Malformed camera source accepted."); }
            catch (ArgumentException) { checks++; }
        }
        string ids = string.Join(',', Enumerable.Range(0, 6).Select(i => (long.MaxValue - i).ToString()));
        Static("ValidateDisplaySource", "camera-panorama", ids);
        Check(ids.Length > 64, "six camera entity IDs are admitted without widening ordinary providers' allowance");
        Reject("lcd-texture", ids);
        foreach (string bad in new[] { "", "0", "-1", "+1", "1,1", "1,", "1, 2", "9223372036854775808", "1,2,3,4,5,6,7", "registration,1" }) Reject("camera-panorama", bad);
        Reject("camera-panorama", "registration");

        using var gateway = new ClientReplicationTests.GatewayScope { Server = false };
        var session = new HoloMapSession();
        ClientReplicationTests.SetField(session, "_displaySourceRegistered", true);
        var scene = Activator.CreateInstance(typeof(HoloMapSession).GetNestedType("Scene", BindingFlags.NonPublic)!, true)!;
        ClientReplicationTests.SetField(scene, "ConsoleId", 20L);
        ((IDictionary)ClientReplicationTests.Field(session, "_scenes"))[20L] = scene;
        var data = new HoloProjectedScreenData { CallerId = 10, Id = "panorama", SourceProvider = "camera-panorama", SourceId = ids, SourceFov = 110, SourceFeather = 10, SourceSaturation = 1.3, SourceCaptureResolution = 512 };
        data.RefreshHz=144;
        ClientReplicationTests.SetField(session,"ClientLcdRefreshCap",1d);
        Check((double)ClientReplicationTests.Call(session,"SourceRefreshLimit",data)==144,"camera request is not silently clamped by LCD sampling preferences");
        var ordinary=new HoloProjectedScreenData {RefreshHz=144}; ordinary.SourceProvider="lcd-texture";
        Check((double)ClientReplicationTests.Call(session,"SourceRefreshLimit",ordinary)==1,"LCD relay still follows its separate viewer sampling preference");
        var screen = Activator.CreateInstance(typeof(HoloMapSession).GetNestedType("ProjectedScreen", BindingFlags.NonPublic)!, true)!;
        ClientReplicationTests.SetField(screen, "Data", data);
        ((IDictionary)ClientReplicationTests.Field(scene, "Screens"))["10:panorama"] = screen;
        object Query(params object[] args) => ClientReplicationTests.Call(session, "DisplaySourceService", "source-panoramasettings", args);
        var settings = (MyTuple<double, double, double, int>)Query("camera-panorama", 20L, 10L, ids);
                object Profile(params object[] args) => ClientReplicationTests.Call(session, "DisplaySourceService", "source-renderprofile", args);
        Check(Profile("camera-panorama", 20L, 10L, ids) is int initial && initial == 0, "normal is the default profile");
        data.SourceCaptureProfile = 1;
        Check(Profile("camera-panorama", 20L, 10L, ids) is int lite && lite == 1, "profile query preserves the independently versioned four-tuple");
        Check(Profile("camera-panorama", 20L, 11L, ids) is false && Profile("camera-panorama", 21L, 10L, ids) is false && Profile("lcd-texture", 20L, 10L, ids) is false && Profile("camera-panorama", 20, 10L, ids) is false, "profile query is scoped to the exact owner/anchor/provider and rejects wrong types");
        data.SourceCaptureProfile = 0;
        Check(settings.Item1 == 110 && settings.Item2 == 10 && settings.Item3 == 1.3 && settings.Item4 == 512, "provider reads the exact owner's declared source settings");
        Check(Query("camera-panorama", 20L, 11L, ids) is false && Query("camera-panorama", 21L, 10L, ids) is false && Query("camera-panorama", 20L, 10L, "1") is false, "foreign owner, anchor and source identities cannot read another declaration");
        Check(Query("lcd-texture", 20L, 10L, ids) is false && Query("camera-panorama", 20, 10L, ids) is false, "query rejects unrelated providers and malformed argument types");
        ClientReplicationTests.SetField(session, "_displaySourceRegistered", false);
        Check(Query("camera-panorama", 20L, 10L, ids) is false, "world/service retirement disables source queries immediately");

        HoloProjectedScreenData Declaration(string id) => new HoloProjectedScreenData {
            CallerId=10,Id=id,Pose=new[]{1d,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1},SourceProvider="camera-panorama",SourceId="1,2" };
        HoloSnapshot Snapshot() {
            var value=ClientReplicationTests.Snapshot();value.Scenes[0].Screens=new List<HoloProjectedScreenData>{Declaration("first"),Declaration("second")};return value;
        }
        var imported=new HoloMapSession();ClientReplicationTests.Call(imported,"ApplySnapshot",Snapshot());
        var prior=((IDictionary)ClientReplicationTests.Field(imported,"_scenes"))[20L];
        foreach(var change in new Action<HoloProjectedScreenData>[] {d=>d.SourceFov=110,d=>d.SourceFeather=0,d=>d.SourceSaturation=0,d=>d.SourceCaptureResolution=256,d=>d.SourceCaptureProfile=1}) {
            var bad=Snapshot();change(bad.Scenes[0].Screens[1]);
            try { ClientReplicationTests.Call(imported,"ApplySnapshot",bad);throw new Exception("Conflicting camera settings imported."); }
            catch(ArgumentException) { Check(ReferenceEquals(prior,((IDictionary)ClientReplicationTests.Field(imported,"_scenes"))[20L]),"network settings conflict preserves previous scene atomically"); }
        }
        var distinct=Snapshot();distinct.Scenes[0].Screens[1].CallerId=11;distinct.Scenes[0].Screens[1].SourceFov=110;
        ClientReplicationTests.Call(imported,"ApplySnapshot",distinct);Check(((IDictionary)ClientReplicationTests.Field(((IDictionary)ClientReplicationTests.Field(imported,"_scenes"))[20L],"Screens")).Count==2,"different owners retain independently scoped settings");

        var caller=DrawTestProxy.Make(typeof(IMyProgrammableBlock),(m,a)=>m.Name switch {"get_EntityId"=>10L,"get_OwnerId"=>77L,"get_Closed"=>false,"IsSameConstructAs"=>true,_=>throw new Exception(m.Name)});
        var anchor=DrawTestProxy.Make(typeof(IMyProjector),(m,a)=>m.Name switch {"get_EntityId"=>20L,"get_Closed"=>false,"HasPlayerAccess"=>true,_=>throw new Exception(m.Name)});
        gateway.Server=true;gateway.Install("Entities",(m,a)=>m.Name=="GetEntityById"?((long)a[0]==10?caller:(long)a[0]==20?anchor:null):throw new Exception(m.Name));
        var committed=new HoloMapSession();var draw=(Func<string,object[],object>)ClientReplicationTests.Call(committed,"DrawEndpoint",caller);draw("target",new[]{anchor});
        foreach(string id in new[]{"first","second"}){draw("screen",new object[]{id,MatrixD.Identity,2d,1d,2d,1d});draw("screen-source",new object[]{"camera-panorama","1,2"});}
        var committedScene=((IDictionary)ClientReplicationTests.Field(committed,"_scenes"))[20L];var screens=(IDictionary)ClientReplicationTests.Field(committedScene,"Screens");object old=screens["10:second"];
        try{draw("screen-panorama",new object[]{110d,8d,1.15d,1024});throw new Exception("Conflicting camera settings committed.");}
        catch(ArgumentException){Check(ReferenceEquals(old,screens["10:second"]),"PB settings conflict restores prior declaration atomically");}
        draw("screen-source-clear",Array.Empty<object>());draw("screen-panorama",new object[]{110d,0d,0d,256});
        draw("screen-source",new object[]{"camera-panorama","3,4"});Check(((HoloProjectedScreenData)ClientReplicationTests.Field(screens["10:second"],"Data")).SourceFov==110,"distinct source can commit different settings");
        return checks;
    }
}
