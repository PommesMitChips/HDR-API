using System;
using HDRClientRenderer;

internal static class ClientRenderSettingsTests
{
    internal static int Run()
    {
        int checks=0;Action<bool,string> check=(ok,why)=>{if(!ok)throw new Exception("Client render controls: "+why);checks++;};
        var options=new ClientRenderSettings();long frame=1;
        check(!options.DisplayOcclusion&&options.Apply("occlusion on")&&options.DisplayOcclusion&&options.Apply("occlusion off")&&!options.DisplayOcclusion,"display occlusion is opt-in and can be disabled locally");
        check(!options.Apply("occlusion maybe"),"invalid occlusion preference rejected");
        check(options.TileLimit==16&&options.TilePassCost==16384,"subdivision starts with bounded pass-aware defaults");
        check(options.Apply("tiles 64")&&options.Apply("tile-cost 0")&&options.Valid,"local subdivision controls allow finest bounded planner and zero pass penalty");
        foreach(var invalid in new[]{"tiles 0","tiles 65","tiles off","tile-cost -1","tile-cost NaN","tile-cost Infinity"})check(!options.Apply(invalid),"invalid subdivision preference rejected: "+invalid);
        var budget=new ClientPixelBudget(()=>new ClientPixelBudget.Frame(frame,320,200));
        budget.Configure(options);
        check(budget.PixelLimit==int.MaxValue,"default has no forced viewport/4K scheduling ceiling");
        check(budget.TrySpend("large",ClientPixelBudget.Kind.Capture,1024*1024),"default permits a physical image larger than viewport area");
        check(options.Apply("pixels 100000")&&options.Apply("passes 12")&&options.Apply("rate 120"),"absolute pixel, pass and rate values parse");
        budget.Configure(options);frame++;
        check(budget.PixelLimit==100000&&!budget.TrySpend("too-large",ClientPixelBudget.Kind.Capture,100001),"configured ceiling is enforced");budget.Clear();
        check(options.Apply("pixels viewport 2.5"),"viewport multiplier is selectable");budget.Configure(options);frame++;
        check(budget.PixelLimit==160000,"viewport budget is a preference instead of a fixed clamp");
        check(options.Apply("pixels off")&&options.Apply("passes unlimited")&&options.Apply("rate off")&&options.Apply("adaptive off"),"all scheduling ceilings can be disabled locally");
        check(options.CapturePasses==0&&options.CaptureRate==0&&!options.Adaptive&&options.Valid,"uncapped preferences remain valid");
        foreach(var invalid in new[]{"pixels -1","passes -2","rate NaN","rate Infinity","pixels viewport 0","pixels viewport NaN","adaptive maybe","passes 9999999999999999"})
            check(!options.Apply(invalid),"malformed numeric preference rejected: "+invalid);
        var policy=new DirectCameraCapturePolicy{RateCeiling=0,RetainCompletedWhileLive=true};
        var source=policy.Request(1,105,1024,144,0,new object());policy.Complete(source,source.Generation,new object(),0);
        check(source.Rate==144,"native policy does not impose a hidden30Hz ceiling");
        policy.Request(1,105,1024,144,10,new object());
        check(policy.Valid(1,source.Generation,10),"authorized renewed demand retains completed pixels despite ten-second capture delay");
        source.DemandUntil=9;
        check(!policy.Valid(1,source.Generation,10),"retention never overrides demand revocation");
        policy.RateCeiling=12;source=policy.Request(1,105,1024,144,11,new object());
        check(source.Rate==12,"an explicit local rate ceiling still applies");
        return checks;
    }
}
