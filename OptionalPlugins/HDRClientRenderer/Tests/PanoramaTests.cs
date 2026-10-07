using System;
using System.Collections.Generic;
using HDRClientRenderer;

internal static class PanoramaTests
{
    static int checks;
    static void Check(bool value, string description) { if (!value) throw new Exception("Panorama policy: " + description); checks++; }
    internal static PanoramaStore.Face Forward(long camera = 1)
    {
        return new PanoramaStore.Face { Camera = camera, Texture = "HDR_DirectCamera_" + camera, Width = 1024, Height = 1024,
            Generation = 1,Profile=0,ContentRevision=1, RightX = 1, RightY = 0, UpX = 0, UpY = 1, UpZ = 0, ForwardY = 0, ForwardZ = 1,
            TanHorizontal = Math.Tan(105 * Math.PI / 360), TanVertical = Math.Tan(105 * Math.PI / 360), Evidence = new object() };
    }
    sealed class World : PanoramaStore.IWorld
    {
        internal bool Visible = true, ThrowCompose, Healthy = true,Ready=true;
        internal readonly HashSet<long> Denied = new HashSet<long>(), Pending = new HashSet<long>();
        internal readonly Dictionary<long, PanoramaStore.Face> Faces = new Dictionary<long, PanoramaStore.Face>();
        internal PanoramaStore.Settings Settings = new PanoramaStore.Settings();
        internal int Creates, Composes, Destroys, Requests, Retired;
        internal int? VisibleMask;
        internal readonly Dictionary<long,int> RequestedSizes = new Dictionary<long,int>();
        internal readonly Dictionary<long,double> RequestedRates = new Dictionary<long,double>();
        internal readonly Dictionary<long,int> RequestedProfiles = new Dictionary<long,int>();
        internal readonly Dictionary<long,PanoramaDensity> RequestedDensity = new Dictionary<long,PanoramaDensity>();
        internal readonly Dictionary<string,int> Profiles = new Dictionary<string,int>();
        internal readonly Dictionary<string,PanoramaDensity[]> Fields = new Dictionary<string,PanoramaDensity[]>();
        internal readonly List<long> Warmed = new List<long>();
        internal readonly Dictionary<string,int> Masks = new Dictionary<string,int>();
        internal readonly Dictionary<string,int[]> Suggested = new Dictionary<string,int[]>();
        internal readonly Dictionary<string,double> Rates = new Dictionary<string,double>();
        internal double Clock=0,Rate=30;
        internal int ViewWidth=2048,ViewHeight=1024;
        internal bool Fresh=false;
        public double Now {get{return Clock;}}
        internal World() { for (long id = 1; id <= 6; id++) Faces[id] = Forward(id); }
        public bool Active(long anchor, long caller, string id) { return Visible && anchor == 10 && caller == 20; }
        public bool TrySettings(long anchor, long caller, string id, out PanoramaStore.Settings settings) { settings = Settings.Copy();int profile;if(Profiles.TryGetValue(id,out profile))settings.Profile=profile; return true; }
        public bool DisplayDemand(long anchor,long caller,string id,out int width,out int height,out double rate)
        {width=ViewWidth;height=ViewHeight;if(!Rates.TryGetValue(id,out rate))rate=Rate;return Visible;}
        public bool TryFace(long anchor, long caller, long camera, PanoramaStore.Settings settings, PanoramaDensity density, out PanoramaStore.Face face)
        { Requests++; RequestedSizes[camera]=settings.CaptureResolution;RequestedRates[camera]=settings.Rate;RequestedProfiles[camera]=settings.Profile;RequestedDensity[camera]=density; Faces.TryGetValue(camera, out face); return !Denied.Contains(camera) && !Pending.Contains(camera) && face != null; }
        public bool Authorized(long anchor,long caller,long camera) { return !Denied.Contains(camera)&&Faces.ContainsKey(camera); }
        public PanoramaDemand Demand(long anchor,long caller,string id,long[] cameras,PanoramaStore.Settings settings)
        {
            var demand=PanoramaDemand.Full(cameras.Length,settings.CaptureResolution);
            if(VisibleMask.HasValue){demand.Mask=VisibleMask.Value;demand.Known=true;for(int i=0;i<cameras.Length;i++)if((demand.Mask&(1<<i))==0)demand.Sizes[i]=0;}
            int mask;if(Masks.TryGetValue(id,out mask)){demand.Mask=mask;demand.Known=true;for(int i=0;i<cameras.Length;i++)if((mask&(1<<i))==0)demand.Sizes[i]=0;}
            int[] suggested;if(Suggested.TryGetValue(id,out suggested))demand.Sizes=(int[])suggested.Clone();
            for(int i=0;i<cameras.Length;i++)demand.Fresh[i]=Fresh;
            PanoramaDensity[] fields;if(Fields.TryGetValue(id,out fields))demand.Density=(PanoramaDensity[])fields.Clone();
            return demand;
        }
        public void Warm(long camera) { Warmed.Add(camera); }
        public bool ValidFace(long anchor, long caller, PanoramaStore.Face face, PanoramaStore.Settings settings)
        { PanoramaStore.Face current; return !Denied.Contains(face.Camera) && !Pending.Contains(face.Camera) && Faces.TryGetValue(face.Camera, out current) && face.Same(current); }
        public void CreateTarget(string target, int width, int height) { Creates++; }
        public bool Compose(string target, int width, int height, PanoramaStore.Face[] faces, PanoramaStore.Settings settings)
        { Composes++; if (ThrowCompose) throw new InvalidOperationException("fake GPU fault"); return true; }
        public bool TargetHealthy(string target) { return Healthy; }
        public bool TargetReady(string target){return Ready;}
        public void DestroyTarget(string target) { Destroys++; }
        public void RetireFaces(PanoramaStore.Face[] faces) { Retired += faces.Length; }
    }
    internal static int Run()
    {
        checks = 0; ParsingAndBounds(); MappingAndShader(); LeaseAndAuthority(); QuotasAndQueue(); DemandAndLod();AtlasAndProfiles();
        return checks;
    }
    static void ParsingAndBounds()
    {
        long[] cameras;
        Check(PanoramaStore.ParseSources("1", out cameras) && cameras.Length == 1 && cameras[0] == 1, "one typed positive camera source");
        Check(PanoramaStore.ParseSources("1,2,3,4,5,6", out cameras) && cameras.Length == 6, "six camera sources admitted");
        foreach (string id in new[] { null, "", "0", "-1", "1,1", "1,2,3,4,5,6,7", "1, 2", "1,", "1:0", "file.hlsl", "9223372036854775808", new string('1', 257) })
            Check(!PanoramaStore.ParseSources(id, out cameras), "malformed or untrusted source rejected: " + id);
        int width, height;
        Check(PanoramaStore.BoundSize(2048, 1024, out width, out height) && width == 2048 && height == 1024, "requested two-megapixel panorama retained");
        Check(PanoramaStore.BoundSize(4096, 2048, out width, out height) && width == 4096 && height == 2048, "higher-detail panorama ceiling is retained");
        Check(PanoramaStore.BoundSize(512, 256, out width, out height) && width == 512 && height == 256, "small output unchanged");
        Check(!PanoramaStore.BoundSize(16, 4096, out width, out height) && !PanoramaStore.BoundSize(0, 512, out width, out height), "invalid and abusive aspect requests denied");
        var settings = new PanoramaStore.Settings();
        Check(settings.Valid && settings.FovDegrees == 105 && settings.FeatherDegrees == 8 && settings.Saturation == 1.15 && settings.CaptureResolution == 1024, "shared bounded defaults");
        foreach (double fov in new[] { double.NaN, double.PositiveInfinity, 59.9, 120.1 }) { settings.FovDegrees = fov; Check(!settings.Valid, "FOV invalid value denied"); }
        settings.FovDegrees = 105; settings.FeatherDegrees = 25; settings.Saturation = 2; settings.CaptureResolution = 256;
        Check(settings.Valid, "inclusive feather/saturation/native resolution bounds");
        settings.FeatherDegrees = -.1; Check(!settings.Valid, "negative feather denied"); settings.FeatherDegrees = 8;
        settings.Saturation = 2.01; Check(!settings.Valid, "excess saturation denied"); settings.Saturation = 1.15;
        settings.CaptureResolution = 2048; Check(settings.Valid, "2048 native capture resolution admitted");
        settings.CaptureResolution = 4096; Check(!settings.Valid, "unsupported native capture resolution denied");
    }
    static void MappingAndShader()
    {
        Check(PanoramaShader.ConstantBytes<=65536&&PanoramaShader.ConstantBytes%16==0,"expanded lookup and descriptors fit the D3D11 constant-buffer ABI");
        var quarterTiles=new[]{new PanoramaTile(0,0,.5,.5,0,0,.5,.5),new PanoramaTile(.5,0,1,.5,.5,0,1,.5),new PanoramaTile(0,.5,.5,1,0,.5,.5,1),new PanoramaTile(.5,.5,1,1,.5,.5,1,1)};
        var lookup=PanoramaTile.Lookup(quarterTiles);
        Check(lookup.Length==256&&lookup[0]==0&&lookup[15]==1&&lookup[240]==2&&lookup[255]==3,"source-cell lookup addresses each quadrant at first/last cells");
        double edgeU,edgeV;
        Check(PanoramaTile.Map(quarterTiles,1024,1024,.5,.5,out edgeU,out edgeV)&&edgeU>.5&&edgeV>.5,"exact shared corner belongs deterministically to the lower-right half-open region");
        Check(PanoramaTile.Map(quarterTiles,1024,1024,1,1,out edgeU,out edgeV)&&edgeU<1&&edgeV<1,"outer source boundary remains sampled at its last texel centre");
        var arbitrary=new[]{new PanoramaTile(.1,.1,.6,.6,0,0,1,1)};
        var arbitraryLookup=PanoramaTile.Lookup(arbitrary);
        Check(arbitraryLookup[1*16+1]==-2&&arbitraryLookup[4*16+4]==0,"non-grid boundaries retain a scan sentinel while interior cells use direct indices");
        var ended=new[]{new PanoramaTile(.1,.1,.5,.6,0,0,1,1)};var endedLookup=PanoramaTile.Lookup(ended);
        Check(endedLookup[1*16+8]==-1&&endedLookup[1*16+7]==-2&&PanoramaTile.Map(ended,1024,1024,.5,.1,out edgeU,out edgeV),
            "arbitrary sparse boundary requires the adjacent-cell ambiguity fallback and retains its closed edge sample");
        var thin=new[]{new PanoramaTile(.1,0,.1000000001,1,0,0,1,1)};
        Check(PanoramaTile.Lookup(thin)[1]==-2,"thin valid arbitrary source intervals do not disappear from the lookup");
        var thinFace=Forward();thinFace.Tiles=thin;
        Check(thinFace.Valid&&PanoramaShader.Constants(new[]{thinFace},new PanoramaStore.Settings())==null,"source intervals that collapse in float shader constants fail closed");
        var face = Forward(); double u, v, weight;
        Check(PanoramaShader.Project(.5, .5, face, 8, out u, out v, out weight) && Math.Abs(u - .5) < 1e-9 && Math.Abs(v - .5) < 1e-9 && weight == 1,
            "canonical +Z sphere centre maps to pinhole centre");
        Check(PanoramaShader.Project(.5, .25, face, 8, out u, out v, out weight) && v < .5, "positive latitude uses top-down texture V");
        Check(!PanoramaShader.Project(0, .5, face, 8, out u, out v, out weight), "uncovered back direction stays transparent");
        Check(!PanoramaShader.Project(.5, 0, face, 8, out u, out v, out weight), "uncovered pole stays transparent");
        Check(PanoramaShader.Project(.5 + 52d / 360, .5, face, 8, out u, out v, out weight) && weight > 0 && weight < .1, "angular edge feathers smoothly inside true coverage");
        var right = Forward(2); right.RightX = 0; right.RightZ = -1; right.ForwardZ = 0; right.ForwardX = 1;
        double other;
        Check(PanoramaShader.Project(.625, .5, face, 8, out u, out v, out weight) && PanoramaShader.Project(.625, .5, right, 8, out u, out v, out other), "105-degree adjacent faces overlap");
        PanoramaShader.Project(.625, .5, right, 8, out u, out v, out other);
        Check(Math.Abs(weight / (weight + other) + other / (weight + other) - 1) < 1e-12, "normalized overlap weights preserve full coverage");
        var back = Forward(3); back.ForwardZ = -1;
        double firstU, firstV;
        Check(PanoramaShader.Project(0, .5, back, 8, out firstU, out firstV, out weight) && PanoramaShader.Project(1, .5, back, 8, out u, out v, out other)
            && Math.Abs(u - firstU) < 1e-9 && Math.Abs(v - firstV) < 1e-9, "equirectangular seam wraps to the same physical ray");
        var settings = new PanoramaStore.Settings();
        var constants = PanoramaShader.Constants(new[] { face, right }, settings);
        Check(constants.Length * 4 == 18832 && constants[11] == 1 && constants[PanoramaShader.FaceFloats+11] == 1 && constants[PanoramaShader.FaceFloats*2+11] == 0 && Math.Abs(constants[PanoramaShader.SettingsOffset+1] - 1.15) < .00001,
            "fixed b1 buffer has exact six-face padding and bounded saturation");
        Check(PanoramaShader.Source.Contains("register(b1)") && PanoramaShader.Source.Contains("SampleLevel") && PanoramaShader.Source.Contains("return float4(0, 0, 0, 0)")
            && PanoramaShader.Source.Contains("return float4(colour, 1)") && !PanoramaShader.Source.Contains("#include"), "trusted shader ABI preserves transparent gaps and covered alpha without includes");
        face.RightX = double.NaN;
        Check(PanoramaShader.Constants(new[] { face }, settings) == null && !face.Valid, "nonfinite bases never reach GPU constants");
    }
    static void LeaseAndAuthority()
    {
        var warmingWorld=new World{Ready=false};var warming=new PanoramaStore(warmingWorld);warming.Tick();
        Check(warming.Acquire(10,20,"1","sphere",1024,512)==null&&warmingWorld.Creates==1,"panorama does not publish an uncleared/uncomposited target");
        warming.Prune();warming.Tick();
        Check(warming.Acquire(10,20,"1","sphere",1024,512)==null&&warmingWorld.Creates==1&&warmingWorld.Destroys==0,"panorama retains warming allocation across frame retries");
        warmingWorld.Ready=true;warming.Tick();
        Check(warming.Acquire(10,20,"1","sphere",1024,512)!=null&&warmingWorld.Creates==1,"panorama publishes once render-thread completion is confirmed");
        var world = new World(); var store = new PanoramaStore(world); store.Tick();
        var lease = store.Acquire(10, 20, "1,2,3,4,5,6", "sphere", 1024, 512);
        Check(lease != null && world.Requests == 6 && store.Valid(lease, 10, 20, "1,2,3,4,5,6"), "all six authorized verified snapshots form one panorama lease");
        Check(world.Creates == 1 && world.Composes == 1 && lease.Material == "HDR_ClientPanorama_0", "one owned fixed panorama target");
        store.Tick(); var same = store.Acquire(10, 20, "1,2,3,4,5,6", "sphere", 1024, 512);
        Check(ReferenceEquals(lease, same) && world.Creates == 1 && world.Composes == 2, "same-target refresh reuses resource and evidence");
        Check(!store.Valid(lease, 11, 20, "1,2,3,4,5,6") && !store.Valid(lease, 10, 21, "1,2,3,4,5,6") && !store.Valid(lease, 10, 20, "1"), "proof belongs to exact anchor/caller/source vector");
        world.Denied.Add(3);
        Check(!store.Valid(lease, 10, 20, "1,2,3,4,5,6") && world.Destroys == 1 && store.ResidentTargets == 0, "one denied camera immediately retires cached output");
        world.Denied.Clear(); store.Tick(); var restored = store.Acquire(10, 20, "1,2,3,4,5,6", "sphere", 1024, 512);
        Check(restored != null && !store.Valid(lease, 10, 20, "1,2,3,4,5,6"), "renewed authority cannot revive an old proof");
        world.Settings.Saturation = 1.2;
        Check(!store.Valid(restored, 10, 20, "1,2,3,4,5,6"), "settings mutation immediately invalidates cached colour");
        store.Tick(); restored = store.Acquire(10, 20, "1", "sphere", 1024, 512);
        world.Faces[1].Generation++;
        Check(!store.Valid(restored, 10, 20, "1"), "native target generation replacement revokes cache");
        world.Pending.Add(6); world.Requests = 0; store.Tick();
        Check(store.Acquire(10, 20, "1,2,3,4,5,6", "sphere", 1024, 512) == null && world.Requests == 6, "pending capture never masks scheduling of its peers");
        world.Pending.Clear(); store.Tick(); restored = store.Acquire(10, 20, "1", "sphere", 1024, 512);
        world.Visible = false; store.Prune();
        Check(!store.Valid(restored, 10, 20, "1") && store.ResidentBytes == 0, "visibility loss retires target and native demand");
        world.Visible = true; store.Tick(); restored = store.Acquire(10, 20, "1", "sphere", 1024, 512);
        var other = store.Acquire(10, 20, "1", "peer", 1024, 512);
        Check(restored != null && other != null && restored.Material != other.Material, "two screens have separate fixed ownership");
        Check(store.Release(restored) && store.Valid(other, 10, 20, "1") && store.Demands(1), "releasing one screen retains the peer's authorized demand");
        int requests = world.Requests; world.Settings.FovDegrees = 110; store.Tick();
        Check(store.Acquire(10, 20, "1", "conflicting", 512, 256) == null && world.Requests == requests,
            "conflicting shared-camera configuration cannot thrash an established native capture");
        world.Settings.FovDegrees = 105;
        Check(store.Valid(other, 10, 20, "1"), "configuration conflict leaves the matching peer live");
        store.NewEpoch(); Check(!store.Valid(other, 10, 20, "1") && store.ResidentTargets == 0 && store.ResidentBytes == 0, "world/device epoch clears every lease and resident target");
        store.Tick(); restored = store.Acquire(10, 20, "1", "sphere", 1024, 512);
        world.ThrowCompose = true; store.Tick();
        Check(store.Acquire(10, 20, "1", "sphere", 1024, 512) == null && !store.Valid(restored, 10, 20, "1") && store.ResidentTargets == 0,
            "GPU queue exceptions retire previous pixels and owned resources");
        world.ThrowCompose = false; store.Tick(); restored = store.Acquire(10, 20, "1", "sphere", 1024, 512); world.Healthy = false;
        Check(!store.Valid(restored, 10, 20, "1") && store.ResidentTargets == 0, "render-thread failure health immediately revokes cached material evidence");
    }
    static void QuotasAndQueue()
    {
        var world = new World(); var store = new PanoramaStore(world); store.Tick();
        var first = store.Acquire(10, 20, "1", "a", 4096, 2048);
        Check(first != null && store.ResidentBytes == 64 * 1024 * 1024, "full target reserves conservative mip-inclusive bytes");
        Check(store.Acquire(10, 20, "2", "b", 4096, 2048) == null && world.Creates == 1, "initial CPU upload ceiling prevents two full allocations in one update");
        store.Tick(); var second = store.Acquire(10, 20, "2", "b", 4096, 2048);
        Check(second != null && store.ResidentTargets == 2 && store.ResidentBytes == 128 * 1024 * 1024, "two full targets fit resident ceiling");
        store.Tick(); Check(store.Acquire(10, 20, "3", "c", 256, 128) == null, "third active target cannot allocate");
        Check(store.Acquire(10, 20, "1", "a", 4096, 2048) != null && store.Acquire(10, 20, "2", "b", 4096, 2048) != null
            && store.Acquire(10, 20, "1", "a", 4096, 2048) == null, "per-update draws and output pixels remain bounded");
        Check(world.Creates == 2, "video refresh adds no CPU target upload");
        store.Release(second); store.Tick(); var resized = store.Acquire(10, 20, "1", "a", 512, 256);
        Check(resized != null && !store.Valid(first, 10, 20, "1") && store.ResidentTargets == 1, "resize replaces ownership while reusing the bounded slot");
        var queue = new PanoramaQueue(); var settings = new PanoramaStore.Settings(); var faces = new[] { Forward() };
        Check(!queue.Enqueue("Caller_Target", 1024, 512, faces, settings), "GPU queue rejects arbitrary destination identity");
        Check(!queue.Enqueue("HDR_ClientPanorama_0", 4096, 2049, faces, settings), "GPU queue independently enforces output ceiling");
        Check(queue.Enqueue("HDR_ClientPanorama_0", 2048, 1024, faces, settings) && queue.Enqueue("HDR_ClientPanorama_1", 2048, 1024, faces, settings), "only two fixed panorama jobs pending");
        faces[0].Texture = "mutated";
        int draws = 0; var reports = new List<string>();
        queue.Drain(request => { Check(request.Faces[0].Texture != "mutated", "queued snapshots detach producer mutation"); draws++; return true; }, reports.Add);
        Check(draws == 2 && queue.Count == 0, "render-frame compositor obeys two-pass/four-megapixel cap");
        faces[0] = Forward(); queue.Enqueue("HDR_ClientPanorama_0", 512, 256, faces, settings); queue.Cancel("HDR_ClientPanorama_0");
        Check(queue.Count == 0, "release cancels old writes before target reuse");
        for (int i = 0; i < 120; i++) { queue.Enqueue("HDR_ClientPanorama_0", 512, 256, faces, settings); queue.Drain(request => false, reports.Add); }
        Check(queue.Count == 0 && reports.Count == 1, "missing native resources expire despite repeated frame requests");
        queue.Enqueue("HDR_ClientPanorama_0", 512, 256, faces, settings);
        queue.Drain(request => { throw new InvalidOperationException("fake draw"); }, reports.Add);
        Check(queue.Count == 0 && reports.Count == 2, "GPU failures are bounded and contained");
        Check(CaptureIsolation.IsHdrMaterial("HDR_ClientPanorama_0") && CaptureIsolation.IsHdrMaterial("HDR_ClientPanorama_1"), "camera isolation excludes both owned panorama materials");
    }
    static void AtlasAndProfiles()
    {
        var face=Forward();face.Width=512;face.Height=256;
        face.Tiles=new[]{new PanoramaTile(0,0,.5,.5,.5,0,1,1)};
        double u,v;
        Check(face.Valid&&PanoramaTile.Map(face.Tiles,face.Width,face.Height,.25,.25,out u,out v)&&Math.Abs(u-.75)<1e-12&&Math.Abs(v-.5)<1e-12,
            "physical source UV maps to its cropped tile in a rectangular atlas");
        Check(!PanoramaTile.Map(face.Tiles,face.Width,face.Height,.75,.75,out u,out v),"omitted source quadrant is a true transparent gap");
        Check(PanoramaTile.Map(face.Tiles,face.Width,face.Height,.5,.5,out u,out v)&&u<1&&v<1&&u==1-.5/512&&v==1-.5/256,
            "tile edge sampling clamps only to its own texel centres");
        var copy=face.Copy();copy.Tiles[0]=PanoramaTile.Full;
        Check(face.Tiles[0].SourceMaxU==.5&&!face.Same(copy),"queued tile layout is detached and participates in evidence identity");
        var constants=PanoramaShader.Constants(new[]{face},new PanoramaStore.Settings());
        Check(constants!=null&&constants[12]==1f/512&&constants[13]==1f/256&&constants[14]==1&&constants[18]==.5&&constants[20]==.5&&constants[22]==1,
            "fixed shader receives full source footprint and atlas pixel-edge bounds");
        var invalid=Forward();invalid.Tiles=new[]{new PanoramaTile(0,0,.5,.5,0,0,.5,.5),new PanoramaTile(.25,.25,.75,.75,.5,.5,1,1)};
        Check(!invalid.Valid&&PanoramaShader.Constants(new[]{invalid},new PanoramaStore.Settings())==null,"overlapping source footprints fail before shader upload");
        invalid.Tiles=new[]{new PanoramaTile(0,0,.5,.5,0,0,.75,.75),new PanoramaTile(.5,.5,1,1,.5,.5,1,1)};
        Check(!invalid.Valid,"overlapping atlas tiles cannot bleed into another capture");
        invalid.Tiles=new[]{new PanoramaTile(double.NaN,0,1,1,0,0,1,1)};Check(!invalid.Valid,"nonfinite tile descriptor rejected");
        invalid.Tiles=new[]{new PanoramaTile(0,0,1,1,0,0,.00001,1)};Check(!invalid.Valid,"subtexel atlas regions rejected");
        invalid.Tiles=new PanoramaTile[65];Check(!invalid.Valid,"more than sixty-four source tiles rejected");
        var settings=new PanoramaStore.Settings{Profile=1};
        Check(settings.Valid&&settings.Copy().Profile==1&&!settings.Same(new PanoramaStore.Settings()),"explicit Lite profile is validated and changes cached settings");
        settings.Profile=2;Check(!settings.Valid,"unknown capture profile rejected");
        var world=new World();world.Profiles["1,2"]=1;world.Profiles["1,3"]=0;world.Masks["1,2"]=world.Masks["1,3"]=1;
        var left=new double[256*7];left[0]=1;left[1]=128;left[2]=16384;left[3]=64;left[4]=4096;left[5]=1;left[6]=1;
        var right=new double[256*7];int cell=255*7;Array.Copy(left,0,right,cell,7);
        world.Fields["1,2"]=new[]{PanoramaDensity.FromPacked(left,256,true),PanoramaDensity.Full(256)};
        world.Fields["1,3"]=new[]{PanoramaDensity.FromPacked(right,256,true),PanoramaDensity.Full(256)};
        var store=new PanoramaStore(world);store.Tick();
        var first=store.Acquire(10,20,"1,2","lite",512,256);
        Check(first!=null&&world.RequestedProfiles[1]==1,"single Lite consumer requests explicit native Lite profile");
        var second=store.Acquire(10,20,"1,3","normal",512,256);
        Check(second!=null&&world.RequestedProfiles[1]==0,"Normal consumer wins shared physical camera profile union");
        var density=world.RequestedDensity[1];
        Check(density.Known&&density.At(0,0).Covered&&density.At(15,15).Covered&&!density.At(8,8).Covered,
            "shared camera receives both consumers' source UV cells rather than a uniform scalar size");
    }
    static void DemandAndLod()
    {
        int count=2;var packet=new double[2+count+count*16*16*7];packet[0]=16;packet[1]=count;packet[2]=512;
        int start=2+count;
        for(int cell=0;cell<256;cell++)
        {int i=start+cell*7;packet[i]=1;packet[i+1]=512;packet[i+2]=512*512;packet[i+3]=128;packet[i+4]=64;packet[i+5]=.5;packet[i+6]=1;}
        var demand=PanoramaDemand.Read(2,1024,1,packet,true);
        Check(demand.Known&&demand.Mask==1&&demand.Sizes[0]==512&&demand.Sizes[1]==0&&demand.Fresh[0]&&!demand.Fresh[1],
            "exact packet decodes intentional omission and per-camera fresh observations");
        Check(PanoramaDemand.Read(2,1024,0,packet,false).Mask==3,"unknown geometry requests every configured camera");
        var malformed=(double[])packet.Clone();malformed[start+1]=double.NaN;
        Check(!PanoramaDemand.Read(2,1024,1,malformed,true).Known&&PanoramaDemand.Read(2,1024,1,new double[1],true).Mask==3,
            "nonfinite/truncated density packet defaults to conservative full demand");
        var stale=(double[])packet.Clone();for(int cell=0;cell<256;cell++)stale[start+cell*7+6]=0;
        var staleDemand=PanoramaDemand.Read(2,1024,1,stale,true);
        Check(staleDemand.Known&&staleDemand.Mask==1&&!staleDemand.Fresh[0],"proven current mask does not imply fresh local density");
        var lod=new PanoramaResolution();
        Check(lod.Observe(512,true,1024,0)==1024&&lod.Observe(1024,false,1024,.5)==1024&&lod.Observe(512,true,1024,1.9)==1024,
            "lower fresh evidence requires sustained two-second hysteresis");
        Check(lod.Observe(512,true,1024,2.01)==512&&lod.Observe(1024,false,1024,2.1)==512,
            "sustained lower density shrinks without stale packets erasing that LOD");
        Check(lod.Observe(1024,true,1024,2.2)==1024,"fresh higher density grows promptly");
        Check(lod.Observe(1024,false,256,2.3)==256&&lod.Observe(1024,false,1024,2.4)==1024,"whole-screen occupation caps capture and grows promptly when occupation rises");
        Check(PanoramaResolution.OutputLimit(512,256,1024)==256&&PanoramaResolution.OutputLimit(2048,1024,1024)==1024,
            "effective equirectangular dimensions bound uniform camera resolution");

        var world=new World{VisibleMask=1,Rate=6};var store=new PanoramaStore(world);store.Tick();world.Pending.Add(2);
        var lease=store.Acquire(10,20,"1,2","sphere",2048,1024);
        Check(lease!=null&&lease.Faces.Length==1&&world.Requests==1&&world.RequestedRates[1]==6,
            "omitted camera is not pending and source rate6 reaches native requests");
        store.RefreshDemands();
        Check(world.Warmed.Contains(2)&&!world.RequestedSizes.ContainsKey(2),"omitted camera warms without requesting capture");
        world.VisibleMask=3;store.Tick();var retained=store.Acquire(10,20,"1,2","sphere",2048,1024);
        Check(ReferenceEquals(retained,lease)&&store.Valid(lease,10,20,"1,2"),"newly required pending camera preserves still-valid previous composite");
        world.Pending.Clear();store.Tick();var joined=store.Acquire(10,20,"1,2","sphere",2048,1024);
        Check(joined!=null&&joined.Faces.Length==2,"warmed camera joins once its verified snapshot exists");
        world.VisibleMask=0;store.Tick();var empty=store.Acquire(10,20,"1,2","sphere",2048,1024);
        Check(empty!=null&&empty.Faces.Length==0&&PanoramaShader.Constants(empty.Faces,new PanoramaStore.Settings())!=null,
            "known zero contributor mask produces intentional transparent output");
        store.RefreshDemands();Check(world.Warmed.Contains(1),"zero-mask consumer pauses every native capture");
        world.Denied.Add(2);Check(!store.Valid(empty,10,20,"1,2"),"ACL revocation remains immediate even for intentionally omitted cameras");

        var unionWorld=new World();unionWorld.Masks["1,2"]=1;unionWorld.Masks["2,1"]=1;
        var union=new PanoramaStore(unionWorld);union.Tick();
        var first=union.Acquire(10,20,"1,2","a",1024,512);var peer=union.Acquire(10,20,"2,1","b",1024,512);
        unionWorld.Warmed.Clear();union.RefreshDemands();
        Check(first!=null&&peer!=null&&unionWorld.Warmed.Count==0&&unionWorld.RequestedSizes[1]==1024&&unionWorld.RequestedSizes[2]==1024&&unionWorld.RequestedDensity[1].Ceiling==512&&unionWorld.RequestedDensity[2].Ceiling==512,
            "opposite consumer masks union without pausing either needed camera");
        unionWorld.Masks["1,2"]=0;union.Prune();unionWorld.Warmed.Clear();union.RefreshDemands();
        Check(unionWorld.Warmed.Contains(1)&&!unionWorld.Warmed.Contains(2),"one consumer cannot pause a camera requested by its peer");
        union.Release(first);Check(union.Valid(peer,10,20,"2,1"),"release preserves the peer's demanded camera and material");
        unionWorld.ViewWidth=256;unionWorld.ViewHeight=128;unionWorld.Rate=6;union.Prune();union.RefreshDemands();
        Check(unionWorld.RequestedSizes[2]==1024&&unionWorld.RequestedDensity[2].Ceiling==256&&unionWorld.RequestedRates[2]==6,"display occupation lowers density ceiling without rebuilding the camera canvas; cadence remains requested");
    }
}
