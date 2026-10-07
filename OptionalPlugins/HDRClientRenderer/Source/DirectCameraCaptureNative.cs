using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sandbox.Game.Entities;
using VRageMath;
using VRageRender;

namespace HDRClientRenderer
{
    // Independent native renderer adapter. It does not call CameraLCD or obtain
    // camera pixels through an LCD, raycast, GPU readback or programmable code.
    // Renderer-private ABI is validated before any hook or GPU allocation.
    internal sealed class DirectCameraCaptureNative
    {
        internal readonly Assembly Assembly;
        PortalCaptureIntegrationNative portals;
        PortalCaptureBillboardIsolationNative portalBillboards;
        PortalPrimaryViewNative primaryReader;
        internal bool PortalReady { get { return portals != null && portals.Ready && portalBillboards != null && portalBillboards.Ready && Healthy; } }
        internal int PortalEpoch { get { return portals == null ? 0 : portals.Epoch; } }
        internal string PortalIssue { get { return portalBillboards != null && !portalBillboards.Ready ? portalBillboards.Reason : portals == null ? "Portal capture adapter has not been installed." : portals.Reason; } }
        internal bool TryInstallPortals(int initialEpoch)
        {
            if (portals == null) portals = new PortalCaptureIntegrationNative(Assembly, initialEpoch);
            if (portalBillboards == null) portalBillboards = new PortalCaptureBillboardIsolationNative(Assembly, () => DirectCameraCapture.IsPortalCapturing);
            return portalBillboards.TryInstall() && portals.TryInstall();
        }
        internal bool PreparePortals()
        { if (!PortalReady || !FrameIdle) return false; EnsureDevice(); return portals.PrepareRenderResources(); }
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal readonly MethodInfo DrawScene, SupportsOcclusion, ActorIsOccluded, ShadowQueries, UpdateAfterCull, UpdateWorldMatrix, CheckDistanceCulling, UpdateInstanceLods, UpdateCullProxies;
        readonly CaptureModelState modelState;
        internal readonly MethodInfo[] SkipVoid, SkipInt;
        internal readonly MethodInfo[] BorrowMethods, ReleaseMethods;
        internal readonly DirectCameraCaptureParticles Particles;
        internal readonly MethodInfo SchedulerDone;
        internal readonly MethodInfo SchedulerExecute, CullDone, CullReset;
        internal readonly MethodInfo CopyPass, SetBlend;
        internal readonly FieldInfo OpaqueCopyBlend;
        internal object CaptureOutput;
        internal bool SchedulerFinished;
        internal bool SchedulerEntered, CullingFinished, CullingReset, CullingDoneAttempted;
        internal readonly object EmptyShadowQueries;
        readonly PropertyInfo device, rc, backbuffer, frame, viewport, stereo;
        readonly FieldInfo environment, resolution, settings, postprocess, overrides, gbufferMain, gbufferHdr;
        readonly FieldInfo gbufferSamples, gbufferQuality;
        readonly PropertyInfo lodding;
        readonly FieldInfo matrices, commonLastCamera, geometryLodEnabled;
        readonly MethodInfo setup, createTarget, resetTarget, destroyTarget, clearState, generateMips,
            createContextState, swapContextState, gbufferResize, gbufferRelease, updateFrameConstants, enqueueUpdate;
        readonly MethodInfo gbufferDone;
        readonly FieldInfo gbufferCopy;
        readonly FieldInfo fileTextures, geometryRenderer;
        readonly PropertyInfo targetRtv, targetSrv, targetSize, targetFormat;
        readonly PropertyInfo targetResource;
        readonly MethodInfo copyRegion, copyResource;
        readonly ConstructorInfo resourceRegion;
        readonly MethodInfo clearTarget;
        readonly ConstructorInfo color;
        readonly Type messageType, stereoRegion, featureLevel, deviceApi;
        readonly ConstructorInfo gbufferConstructor;
        readonly PropertyInfo[] gbufferAttachments;
        readonly FieldInfo[] commonState;
        readonly FieldInfo contextState, deviceContext;
        readonly FieldInfo[] stages;
        readonly PropertyInfo debugAmbient;
        readonly FieldInfo fullViewport;
        readonly FieldInfo[] hbaoTextures, bloomArrays, debugTextureState;
        readonly MethodInfo hbaoInit, hbaoRelease;
        readonly FieldInfo shadowsManager, bufferManager, lockImmediate;
        readonly FieldInfo renderScheduler;
        readonly FieldInfo cullManager, schedulerBatch, batchJobCount;
        readonly MethodInfo getCullQueries, clearBatch;
        readonly PropertyInfo cullQuerySize;
        readonly MethodInfo getCascades, fillShadowConstants, createShadowBuffer, disposeShadowBuffers;
        readonly FieldInfo[] shadowBufferFields;
        readonly PropertyInfo shadowByteSize;
        readonly Type shadowBufferType;
        object[] ownedShadowBuffers;
        readonly Dictionary<Tuple<int, int, int>, CaptureBuffers> captureBuffers = new Dictionary<Tuple<int, int, int>, CaptureBuffers>();
        readonly Dictionary<object, int> borrowed = new Dictionary<object, int>();
        readonly FieldInfo renderableProxies, commonObjectData;
        readonly object sceneGate = new object();
        readonly Dictionary<object, FieldSnapshot> sceneMutations = new Dictionary<object, FieldSnapshot>();
        readonly CaptureInstanceState instanceState;
        readonly FieldInfo queryResults,queryProxies,proxyUpdated;
        readonly Dictionary<object,int> captureUpdateMarkers=new Dictionary<object,int>();
        object ownedGbuffer, contextToken, resourceDevice;
        int bufferSize;
        internal Action<string> Trace;
        void Stage(string name) { var trace = Trace; if (trace != null) trace(name); }
        internal bool Healthy { get; private set; } = true;
        internal long Frame { get { return (long)frame.GetValue(null); } }
        internal object DeviceIdentity { get { return device.GetValue(null); } }
        internal PortalPrimaryView PrimaryView()
        { if(primaryReader==null)primaryReader=new PortalPrimaryViewNative(Assembly);PortalPrimaryView view;return primaryReader.TrySnapshot(PortalEpoch,out view)?view:null; }
        internal void PreparePrimaryDevice()
        { if(!Healthy||!FrameIdle)throw new InvalidOperationException("Portal primary preparation requires idle native ownership.");EnsureDevice(); }
        internal bool DeviceChanged { get { return !ReferenceEquals(resourceDevice, device.GetValue(null)); } }
        internal bool FrameIdle
        {
            get
            {
                object manager = cullManager.GetValue(null), scheduler = renderScheduler.GetValue(null);
                return manager != null && scheduler != null && (int)cullQuerySize.GetValue(getCullQueries.Invoke(manager, null)) == 0 &&
                    (int)batchJobCount.GetValue(schedulerBatch.GetValue(scheduler)) == 0;
            }
        }

        internal DirectCameraCaptureNative(Assembly assembly)
        {
            Assembly = assembly;
            Func<string, Type> type = name => FindType(assembly, name);
            var renderer = type("VRageRender.MyRender11");
            var context = type("VRage.Render11.RenderContext.MyRenderContext");
            var common = type("VRageRender.MyCommon");
            var gbuffer = type("VRage.Render11.Resources.MyGBuffer");
            var texture = type("VRage.Render11.Resources.IUserGeneratedTexture");
            var resource = type("VRage.Render11.Resources.IResource");
            var rtv = type("VRage.Render11.Resources.IRtvBindable");
            var srv = type("VRage.Render11.Resources.ISrvBindable");
            var managers = type("VRage.Render11.Common.MyManagers");
            var file = type("VRage.Render11.Resources.MyFileTextureManager");
            var cameraMessage = type("VRageRender.Messages.MyRenderMessageSetCameraViewMatrix");
            var matricesType = type("VRageRender.MyEnvironmentMatrices");
            messageType = cameraMessage;
            stereoRegion = type("VRageRender.MyStereoRegion");
            deviceApi = type("SharpDX.Direct3D11.Device");
            var device1 = type("SharpDX.Direct3D11.Device1");
            var nativeContext = type("SharpDX.Direct3D11.DeviceContext1");
            featureLevel = type("SharpDX.Direct3D.FeatureLevel");
            DrawScene = Need(renderer.GetMethod("DrawGameScene", Static, null,
                new[] { rtv, type("VRage.Render11.Resources.IBorrowedRtvTexture").MakeByRefType() }, null), "DrawGameScene");
            if (DrawScene.ReturnType != typeof(void)) throw Changed("scene return type");
            device = Property(renderer, "DeviceInstance"); rc = Property(renderer, "RC");
            backbuffer = Property(renderer, "Backbuffer"); frame = Property(common, "FrameCounter");
            viewport = Property(renderer, "ViewportResolution");
            fullViewport = WritableField(renderer, "FullResViewport", Static);
            if (!viewport.CanWrite || viewport.PropertyType != typeof(Vector2I)) throw Changed("viewport");
            stereo = Property(type("VRageRender.MyStereoRender"), "Enable");
            environment = Field(renderer, "Environment", Static);
            matrices = Field(environment.FieldType, "Matrices", Instance);
            // These are readonly owner references in the initialized renderer.
            // Capture changes their existing mutable contents, never the owners.
            if (environment.FieldType.IsValueType || matrices.FieldType.IsValueType) throw Changed("camera state object identities");
            foreach (var field in matricesType.GetFields(Instance).Where(f => !f.FieldType.IsValueType))
                if ((field.Name != "ViewFrustumClippedD" && field.Name != "ViewFrustumClippedFarD") ||
                    field.FieldType != typeof(BoundingFrustumD)) throw Changed("camera matrices nested state " + field.Name);
            resolution = WritableField(renderer, "m_resolution", Static);
            settings = WritableField(renderer, "Settings", Static); postprocess = WritableField(renderer, "Postprocess", Static);
            overrides = WritableField(renderer, "m_debugOverrides", Static);
            lodding = Property(common, "LoddingSettings");
            if (!lodding.CanWrite) throw Changed("LOD settings setter");
            commonLastCamera = Field(common, "m_lastCameraPosition", Static);
            commonState = common.GetFields(Static).Where(f => !f.IsInitOnly && !f.IsLiteral &&
                (f.FieldType.IsValueType || f.Name == "m_lastCameraPosition")).ToArray();
            geometryRenderer = Field(managers, "GeometryRenderer", Static);
            geometryLodEnabled = WritableField(geometryRenderer.FieldType, "IsLodUpdateEnabled", Instance);
            setup = Method(renderer, "SetupCameraMatricesInternal", Static, cameraMessage, matricesType, stereoRegion);
            fileTextures = Field(managers, "FileTextures", Static);
            renderScheduler = Field(managers, "RenderScheduler", Static);
            SchedulerDone = Method(renderScheduler.FieldType, "Done", Instance);
            SchedulerExecute = Method(renderScheduler.FieldType, "Execute", Instance);
            schedulerBatch = Field(renderScheduler.FieldType, "m_batch", Instance);
            batchJobCount = Field(schedulerBatch.FieldType, "m_jobCount", Instance);
            clearBatch = Method(schedulerBatch.FieldType, "Clear", Instance, typeof(int));
            cullManager = Field(managers, "Cull", Static);
            getCullQueries = Method(cullManager.FieldType, "GetCullQueries", Instance);
            cullQuerySize = Need(getCullQueries.ReturnType.GetProperty("Size", Instance), "cull query count");
            CullDone = Method(cullManager.FieldType, "DoneFrame", Instance);
            CullReset = Method(cullManager.FieldType, "OnFrameEnd", Instance);
            createTarget = Method(file, "CreateGeneratedTexture", Instance, typeof(string), typeof(int), typeof(int),
                typeof(VRageRender.Messages.MyGeneratedTextureType), typeof(bool), typeof(byte[]), typeof(bool));
            if (createTarget.ReturnType != texture || !rtv.IsAssignableFrom(texture) || !srv.IsAssignableFrom(texture))
                throw Changed("generated target RTV/SRV interfaces");
            resetTarget = Method(texture, "Reset", Instance, typeof(byte[]));
            destroyTarget = Method(file, "DestroyGeneratedTexture", Instance, typeof(string));
            targetRtv = Need(rtv.GetProperty("Rtv", Instance), "target RTV");
            targetSrv = Need(srv.GetProperty("Srv", Instance), "target SRV");
            targetSize = Need(resource.GetProperty("Size", Instance), "target size");
            targetResource = Need(resource.GetProperty("Resource", Instance), "target resource");
            targetFormat = Need(type("VRage.Render11.Resources.ITexture").GetProperty("Format", Instance), "target format");
            clearState = Method(context, "ClearState", Instance);
            clearTarget = Need(context.GetMethods(Instance).SingleOrDefault(m => m.Name == "ClearRtv" && m.GetParameters().Length == 2), "opaque target clear");
            color = Need(clearTarget.GetParameters()[1].ParameterType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) }), "opaque clear color");
            SetBlend = Need(context.GetMethods(Instance).SingleOrDefault(m => m.Name == "SetBlendState" && m.GetParameters().Length == 2), "opaque copy blend binding");
            OpaqueCopyBlend = Field(type("VRage.Render11.Resources.MyBlendStateManager"), "BlendReplaceNoAlphaChannel", Static);
            CopyPass = Method(type("VRageRender.MyCopyToRT"), "Run", Static, rtv, srv, typeof(bool), typeof(MyViewport?), typeof(bool));
            generateMips = Method(context, "GenerateMips", Instance, srv);
            contextState = Field(context, "m_state", Instance);
            deviceContext = Field(context, "m_deviceContext", Instance);
            var region = type("SharpDX.Direct3D11.ResourceRegion");
            resourceRegion = Need(region.GetConstructor(new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) }), "atlas copy region");
            var dxResource = type("SharpDX.Direct3D11.Resource");
            copyRegion = Method(type("SharpDX.Direct3D11.DeviceContext"), "CopySubresourceRegion", Instance,
                dxResource, typeof(int), typeof(Nullable<>).MakeGenericType(region), dxResource, typeof(int), typeof(int), typeof(int), typeof(int));
            copyResource = Method(type("SharpDX.Direct3D11.DeviceContext"), "CopyResource", Instance, dxResource, dxResource);
            stages = new[] { "m_vertexShaderStage", "m_geometryShaderStage", "m_pixelShaderStage", "m_computeShaderStage" }
                .Select(name => Field(context, name, Instance)).ToArray();
            createContextState = Need(device1.GetMethods(Instance).SingleOrDefault(m => m.Name == "CreateDeviceContextState" &&
                m.IsGenericMethodDefinition && m.GetParameters().Length == 3), "native context state factory").MakeGenericMethod(deviceApi);
            swapContextState = Need(nativeContext.GetMethods(Instance).SingleOrDefault(m => m.Name == "SwapDeviceContextState" &&
                m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType.IsByRef), "native context state swap");
            gbufferMain = WritableField(gbuffer, "Main", Static);
            gbufferHdr = WritableField(gbuffer, "m_hdrType", Static);
            gbufferSamples = Field(gbuffer, "m_samplesCount", Instance);
            gbufferQuality = Field(gbuffer, "m_samplesQuality", Instance);
            gbufferConstructor = Need(gbuffer.GetConstructor(Instance, null, Type.EmptyTypes, null), "GBuffer constructor");
            gbufferResize = Method(gbuffer, "Resize", Instance, typeof(int), typeof(int), typeof(int), typeof(int), gbufferHdr.FieldType);
            gbufferRelease = Method(gbuffer, "Release", Instance);
            gbufferDone = Method(gbuffer, "DoneFrame", Instance);
            gbufferCopy = WritableField(gbuffer, "m_gbuffer1Copy", Instance);
            gbufferAttachments = new[] { "DepthStencil", "ResolvedDepthStencil", "LBuffer", "GBuffer0", "GBuffer1", "GBuffer2" }
                .Select(name => Need(gbuffer.GetProperty(name, Instance), "GBuffer " + name)).ToArray();
            updateFrameConstants = Method(common, "UpdateFrameConstants", Static);
            enqueueUpdate = Method(renderer, "EnqueueUpdate", Static, typeof(Action));
            lockImmediate = WritableField(renderer, "LockImmediateRC", Static);
            debugAmbient = Property(type("VRage.Render11.GBufferResolve.MyGBufferResolver"), "DebugAmbientOcclusion");
            if (!debugAmbient.CanWrite) throw Changed("debug AO setter");
            var hbao = type("VRageRender.MyHBAO");
            hbaoTextures = new[] { "m_fullResViewDepthTarget", "m_fullResNormalTexture", "m_fullResAOZTexture", "m_fullResAOZTexture2",
                "m_quarterResViewDepthTextureArray", "m_quarterResAOTextureArray" }.Select(name => WritableField(hbao, name, Static)).ToArray();
            hbaoInit = Method(hbao, "InitScreenResources", Static);
            hbaoRelease = Method(hbao, "ReleaseScreenResources", Static);
            bloomArrays = new[] { "m_bloomCascadeDown", "m_bloomCascadeUp" }
                .Select(name => Field(type("VRageRender.MyModernBloom"), name, Static)).ToArray();
            if (bloomArrays.Any(f => !f.FieldType.IsArray || f.FieldType.GetArrayRank() != 1)) throw Changed("bloom scratch array ABI");
            debugTextureState = type("VRage.Render11.Tools.MyDebugTextureDisplay").GetFields(Static)
                .Where(f => !f.IsLiteral && !f.IsInitOnly).ToArray();
            var borrowedManager = type("VRage.Render11.Resources.MyBorrowedRwTextureManager");
            // Hook only the dimensional implementation overload. Convenience
            // overloads call these methods and must not double-count a lease.
            BorrowMethods = borrowedManager.GetMethods(Instance).Where(m =>
                (m.Name == "BorrowRtv" || m.Name == "BorrowUav") && m.GetParameters().Length == 6 ||
                m.Name == "BorrowCustom" && m.GetParameters().Length == 5).ToArray();
            var borrowedContract = type("VRage.Render11.Resources.IBorrowedSrvTexture");
            Type[] resources;
            try { resources = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { resources = ex.Types.Where(t => t != null).ToArray(); }
            ReleaseMethods = resources.Where(t => !t.IsAbstract && borrowedContract.IsAssignableFrom(t))
                .SelectMany(t => t.GetMethods(Instance)).Where(m => !m.IsAbstract && m.ReturnType == typeof(void) &&
                    m.GetParameters().Length == 0 && (m.Name == "Release" || m.Name.EndsWith(".Release", StringComparison.Ordinal)))
                .Select(m => m.DeclaringType.GetMethod(m.Name, Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null))
                .Where(m => m != null && m.GetMethodBody() != null).Distinct().ToArray();
            if (BorrowMethods.Length == 0 || ReleaseMethods.Length == 0) throw Changed("borrowed capture lease tracking");
            shadowsManager = Field(managers, "Shadows", Static);
            getCascades = Method(shadowsManager.FieldType, "get_ShadowCascades", Instance);
            shadowBufferFields = new[] { "m_csmConstants", "m_csmConstants2" }
                .Select(name => WritableField(getCascades.ReturnType, name, Instance)).ToArray();
            shadowBufferType = type("VRage.Render11.Resources.IConstantBuffer");
            shadowByteSize = Need(type("VRage.Render11.Resources.IBuffer").GetProperty("ByteSize", Instance), "shadow constant buffer size");
            fillShadowConstants = Method(getCascades.ReturnType, "FillConstantBuffer", Instance, context, shadowBufferType);
            bufferManager = Field(managers, "Buffers", Static);
            createShadowBuffer = Method(bufferManager.FieldType, "CreateConstantBuffer", Instance, typeof(string), typeof(int), typeof(IntPtr?),
                type("SharpDX.Direct3D11.ResourceUsage"), typeof(bool));
            disposeShadowBuffers = Method(bufferManager.FieldType, "Dispose", Instance, shadowBufferType.MakeArrayType());

            var cullQuery = type("VRage.Render11.Culling.MyCullQuery");
            var component = type("VRage.Render11.Scene.Components.MyRenderableComponent");
            var proxy = type("VRage.Render11.Culling.MyCullProxy");
            UpdateAfterCull = Method(component, "UpdateAfterCull", Instance);
            CheckDistanceCulling = Method(component,"CheckDistanceCulling",Instance,typeof(float));
            if(CheckDistanceCulling.ReturnType!=typeof(bool))throw Changed("individual-model size culling");
            modelState=new CaptureModelState(assembly);
            instanceState=new CaptureInstanceState(assembly);
            UpdateInstanceLods=Method(type("VRage.Render11.GeometryStage2.Rendering.MyGeometryRenderer"),"UpdateLods",Instance,cullQuery);
            UpdateCullProxies=Method(type("VRageRender.MyGeometryRendererOld"),"UpdateCullProxies",Instance,cullQuery);
            queryResults=Field(cullQuery,"Results",Instance);queryProxies=Field(queryResults.FieldType,"CullProxies",Instance);
            proxyUpdated=Field(proxy,"Updated",Instance);
            if(proxyUpdated.FieldType!=typeof(int))throw Changed("legacy proxy update marker");
            ActorIsOccluded=Method(type("VRage.Render.Scene.MyActor"),"IsOccluded",Instance,typeof(int));
            if(ActorIsOccluded.ReturnType!=typeof(bool))throw Changed("cached actor occlusion predicate");
            UpdateWorldMatrix = Method(proxy, "UpdateWorldMatrix", Instance);
            renderableProxies = Field(proxy, "RenderableProxies", Instance);
            commonObjectData = Field(renderableProxies.FieldType.GetElementType(), "CommonObjectData", Instance);
            SupportsOcclusion = Method(type("VRage.Render11.Culling.MyCullManager"), "SupportsOcclusion", Static,
                type("VRage.Render.Scene.MyViewType"));
            if (SupportsOcclusion.ReturnType != typeof(bool)) throw Changed("occlusion guard");
            ShadowQueries = Method(type("VRageRender.MyShadows"), "PrepareQueries", Instance, context);
            var list = typeof(List<>).MakeGenericType(ShadowQueries.ReturnType.GetGenericArguments().Single());
            var conversion = Need(ShadowQueries.ReturnType.GetMethods(Static).SingleOrDefault(m => m.Name == "op_Implicit" &&
                m.ReturnType == ShadowQueries.ReturnType && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == list), "empty shadow queries conversion");
            EmptyShadowQueries = conversion.Invoke(null, new[] { Activator.CreateInstance(list) });

            SkipVoid = new[] {
                Method(type("VRageRender.MyEyeAdaptation"), "ConstantExposure", Static, context),
                Method(type("VRage.Render11.Render.MyOffscreenRenderer"), "Render", Static),
                Method(type("VRage.Render11.Scene.MyScene11"), "PerformVicinityUpdates", Instance),
                Method(type("VRage.Render11.Scene.Components.MyRenderableComponent"), "UpdateLodState", Instance, typeof(float)),
                Method(type("VRage.Render11.Culling.Occlusion.MyActorOcclusionRenderer"), "Render", Instance, context, cullQuery),
                Method(type("VRage.Render11.LightingStage.EnvironmentProbe.MyEnvironmentProbe"), "UpdateProbe", Instance),
                Method(type("VRage.Render11.LightingStage.EnvironmentProbe.MyEnvironmentProbe"), "UpdateCullQuery", Instance, context,
                    type("VRage.Render11.Culling.MyCullQueries")),
                Method(type("VRage.Render11.LightingStage.EnvironmentProbe.MyEnvironmentProbe"), "FinalizeEnvProbes", Instance, context),
                Method(type("VRage.Render11.Ansel.MyAnselRenderManager"), "MarkHdrBufferFinished", Instance),
                Method(type("VRage.Render11.Ansel.MyAnselRenderManager"), "MarkHdrBufferBind", Instance)
            };
            if (SkipVoid.Any(m => m.ReturnType != typeof(void))) throw Changed("void side-effect guard");
            SkipInt = new[] {
                Method(type("VRageRender.MyGPUParticleRenderer"), "Update", Static, cullQuery),
                Method(type("VRage.Render11.Culling.MyCullManager"), "SendGlobalOutputMessages", Instance, cullQuery),
                Method(type("VRage.Render11.Culling.Occlusion.MyOcclusionTask"), "DoWork", Static, cullQuery),
                Method(type("VRageRender.MyShadows"), "SetVisibleLights", Instance, cullQuery),
                Method(type("VRageRender.MyScreenDecals"), "Preprocess", Static, cullQuery)
            };
            if (SkipInt.Any(m => m.ReturnType != typeof(int))) throw Changed("integer side-effect guard");
            // Validate fields before enabling hooks. Unsupported renderer variants
            // fail closed instead of drawing with an incomplete state contract.
            WritableField(settings.FieldType, "ShadowCameraFrozen", Instance);
            WritableField(settings.FieldType, "UseIncrementalCulling", Instance);
            WritableField(postprocess.FieldType, "EnableEyeAdaptation", Instance);
            Field(overrides.FieldType, "Flares", Instance); Field(overrides.FieldType, "SSAO", Instance);
            Field(overrides.FieldType, "Bloom", Instance); Field(overrides.FieldType, "Shadows", Instance);
            Field(overrides.FieldType, "Foliage", Instance);
            foreach (string name in new[] { "ViewMatrix", "CameraPosition", "FOV", "FOVForSkybox", "NearPlane", "FarPlane", "FarFarPlane",
                "ProjectionOffsetX", "ProjectionOffsetY", "ProjectionMatrix", "ProjectionFarMatrix", "LastMomentUpdateIndex", "Smooth" })
                WritableField(cameraMessage, name, Instance);
            Particles = new DirectCameraCaptureParticles(assembly);
        }

        internal bool IsMainTarget(object target)
        { return target != null && ReferenceEquals(target, backbuffer.GetValue(null)); }
        internal void EnqueueCleanup(Action cleanup)
        { enqueueUpdate.Invoke(null, new object[] { cleanup }); }
        internal void TrackBorrow(object lease)
        { if (lease != null) lock (sceneGate) { int count; borrowed.TryGetValue(lease, out count); borrowed[lease] = count + 1; } }
        internal void TrackRelease(object lease)
        { if (lease != null) lock (sceneGate) { int count; if (!borrowed.TryGetValue(lease, out count)) return; if (count == 1) borrowed.Remove(lease); else borrowed[lease] = count - 1; } }
        void ReleasePendingBorrowed()
        {
            KeyValuePair<object, int>[] leases; lock (sceneGate) { leases = borrowed.ToArray(); borrowed.Clear(); }
            var failures = new List<Exception>();
            foreach (var lease in leases) for (int i = 0; i < lease.Value; i++) Restore(() => ReleaseBorrowed(lease.Key), failures);
            if (failures.Count != 0) throw new AggregateException("Capture borrowed lease cleanup failed.", failures);
        }
        internal void RecordSceneMutation(object owner, bool worldMatrix)
        {
            lock (sceneGate)
            {
                if (!sceneMutations.ContainsKey(owner)) sceneMutations.Add(owner, new FieldSnapshot(owner, true));
                if (!worldMatrix) { modelState.Prepare(owner);return; }
                var proxies = renderableProxies.GetValue(owner) as Array;
                if (proxies == null) return;
                foreach (object proxy in proxies)
                    if (proxy != null && !sceneMutations.ContainsKey(proxy))
                        sceneMutations.Add(proxy, new FieldSnapshot(proxy, true));
            }
        }
        internal void PrepareInstanceLods(object query) { lock(sceneGate)instanceState.Prepare(query); }
        internal void PrepareLegacyQuery(object query)
        {
            lock(sceneGate)
            {
                var list=queryProxies.GetValue(queryResults.GetValue(query));
                int count=(int)list.GetType().GetProperty("Count",Instance).GetValue(list);
                var proxies=(Array)list.GetType().GetMethod("GetInternalArray",Instance).Invoke(list,null);
                for(int i=0;i<count;i++)
                {
                    object proxy=proxies.GetValue(i);if(proxy==null||captureUpdateMarkers.ContainsKey(proxy))continue;
                    int marker=(int)proxyUpdated.GetValue(proxy);
                    if(marker==1)throw new InvalidOperationException("Legacy proxy update work is still active.");
                    captureUpdateMarkers.Add(proxy,marker);proxyUpdated.SetValue(proxy,0);
                }
            }
        }
        void RestoreSceneMutations()
        {
            lock (sceneGate)
            {
                var restore = sceneMutations.Values.ToArray(); sceneMutations.Clear();
                var failures = new List<Exception>();
                foreach (var snapshot in restore) Restore(snapshot.Restore, failures);
                Restore(modelState.Restore,failures);
                Restore(instanceState.Restore,failures);
                foreach(var entry in captureUpdateMarkers)Restore(()=>proxyUpdated.SetValue(entry.Key,entry.Value),failures);
                captureUpdateMarkers.Clear();
                if (failures.Count != 0) throw new AggregateException("Capture scene proxy restoration failed.", failures);
            }
        }
        internal int BoundResolution(int requested)
        {
            object buffer = gbufferMain.GetValue(null);
            if (buffer == null || (bool)stereo.GetValue(null)) return 0;
            int width = int.MaxValue, height = int.MaxValue;
            foreach (var attachment in gbufferAttachments)
            {
                object resource = attachment.GetValue(buffer);
                if (resource == null) return 0;
                var size = (Vector2I)targetSize.GetValue(resource);
                width = Math.Min(width, size.X); height = Math.Min(height, size.Y);
            }
            // The private GBuffer determines capture size, not the main window.
            if(width<64||height<64)return 0;
            return DirectCameraCapturePolicy.BoundResolution(requested,DirectCameraCapturePolicy.MaxResolution,DirectCameraCapturePolicy.MaxResolution,
                DirectCameraCapturePolicy.MaxResolution,DirectCameraCapturePolicy.MaxResolution);
        }
        internal object CreateTarget(string name, int size) { return CreateAtlasTarget(name, size, size); }
        internal object CreateAtlasTarget(string name, int width, int height)
        {
            // With mipmaps enabled the engine interprets a byte[] as the ENTIRE
            // mip chain. A base-level array makes native DataBox pointers walk
            // beyond pinned memory. Allocate with null initial data instead; the
            // first render clears opaque alpha before any snapshot is published.
            try
            {
                Stage("target metadata create; " + width + "x" + height + ", complete mip chain, no CPU initial data");
                object target = createTarget.Invoke(fileTextures.GetValue(null), new object[] {
                    name, width, height, VRageRender.Messages.MyGeneratedTextureType.RGBA, true, null, true });
                Stage("target GPU allocation; Reset(null)");
                if (target != null) resetTarget.Invoke(target, new object[] { null });
                Stage("target RTV/SRV validation");
                if (target == null || targetRtv.GetValue(target) == null || targetSrv.GetValue(target) == null ||
                    !((Vector2I)targetSize.GetValue(target)).Equals(new Vector2I(width, height)) ||
                    Convert.ToInt32(targetFormat.GetValue(target)) != 29) // R8G8B8A8_UNorm_SRgb
                    throw Changed("allocated sRGB target");
                return target;
            }
            catch
            {
                // Allocation can fail after the manager registered the name but
                // before the service records a target. Retire that orphan as well.
                try { DestroyTarget(name); } catch { }
                throw;
            }
        }
        internal void DestroyTarget(string name)
        { destroyTarget.Invoke(fileTextures.GetValue(null), new object[] { name }); }

        internal bool Capture(object target, int size, MatrixD pose, double fovDegrees, int profile = 0,
            CaptureTileAtlas.Tile? crop = null, bool mipmaps = true, PortalRayMapSpec portalSpec = null)
        {
            if (!Healthy || !FrameIdle || size < 64 || size > DirectCameraCapturePolicy.MaxResolution ||
                targetRtv.GetValue(target) == null || targetSrv.GetValue(target) == null) return false;
            var physical = (Vector2I)targetSize.GetValue(target);
            // The shared scratch can be larger than this logical cropped view.
            // DrawGameScene's final copy receives an explicit size-square viewport;
            // only that top-left mip0 region is later copied into the atlas.
            if (!ScratchFits(size, physical.X, physical.Y)) throw Changed("logical crop exceeds physical scratch target");
            object renderContext = rc.GetValue(null);
            object dxContext = deviceContext.GetValue(renderContext);
            Stage("native device/context-state preparation");
            EnsureDevice();
            var gpuCache = new List<FieldSnapshot> { new FieldSnapshot(contextState.GetValue(renderContext)) };
            foreach (var stage in stages) gpuCache.Add(new FieldSnapshot(stage.GetValue(renderContext)));
            var cameraSnapshot = new CameraMatricesSnapshot(environment, matrices);
            object mainMatrices = cameraSnapshot.Matrices;
            object mainBuffer = gbufferMain.GetValue(null), mainSettings = settings.GetValue(null),
                mainPostprocess = postprocess.GetValue(null), mainOverrides = overrides.GetValue(null),
                mainLodding = lodding.GetValue(null), mainViewport = viewport.GetValue(null),
                mainResolution = resolution.GetValue(null), mainAmbient = debugAmbient.GetValue(null);
            object mainFullViewport = fullViewport.GetValue(null);
            var hbaoSnapshot = new StaticSnapshot(hbaoTextures);
            var bloomSnapshot = new StaticSnapshot(bloomArrays);
            var debugSnapshot = new StaticSnapshot(debugTextureState);
            object geometry = geometryRenderer.GetValue(null), mainLodEnabled = geometryLodEnabled.GetValue(geometry);
            var commonSnapshot = new StaticSnapshot(commonState);
            object cascades = getCascades.Invoke(shadowsManager.GetValue(null), null);
            object[] mainShadowBuffers = shadowBufferFields.Select(f => f.GetValue(cascades)).ToArray();
            object mainImmediateLock = lockImmediate.GetValue(null);
            var sceneSnapshot = CaptureSceneEnvironment();
            object previousToken = null, returnedToken = null;
            bool swapped = false, success = false;
            bool sceneStarted = false;
            PortalCaptureIntegrationNative.Session portalSession = null;
            var failures = new List<Exception>();
            try
            {
                Stage("swap isolated D3D context state");
                var args = new object[] { contextToken, null };
                swapContextState.Invoke(dxContext, args); previousToken = args[1]; swapped = true;
                clearState.Invoke(renderContext, null);
                clearTarget.Invoke(renderContext, new[] { target, color.Invoke(new object[] { 0f, 0f, 0f, 1f }) });
                CaptureOutput = target;
                viewport.SetValue(null, new Vector2I(size, size)); resolution.SetValue(null, new Vector2I(size, size));
                fullViewport.SetValue(null, new MyViewport(size, size));
                Stage("private GBuffer/HBAO/bloom preparation");
                bloomSnapshot.ClearArrays();
                PrepareCaptureBuffers(size, portalSpec == null ? (int)gbufferSamples.GetValue(mainBuffer) : 1,
                    portalSpec == null ? (int)gbufferQuality.GetValue(mainBuffer) : 0);
                gbufferMain.SetValue(null, ownedGbuffer);
                object captureSettings = Clone(mainSettings); Set(captureSettings, "ShadowCameraFrozen", true);
                // Full frustum culling yields the same visible geometry without
                // editing the main view's persistent incremental actor caches.
                Set(captureSettings, "UseIncrementalCulling", false);
                settings.SetValue(null, captureSettings);
                // Reuse the main view's exposure texture without advancing its
                // adaptation; preserve visual postprocessing settings by default.
                object capturePost = Clone(mainPostprocess); Set(capturePost, "EnableEyeAdaptation", false);
                postprocess.SetValue(null, capturePost);
                object captureOverrides = CaptureOverrides(mainOverrides, profile);
                overrides.SetValue(null, captureOverrides);
                object captureLodding = Clone(mainLodding); SetNested(captureLodding, "Global", "IsUpdateEnabled", false);
                lodding.SetValue(null, captureLodding); geometryLodEnabled.SetValue(geometry, false);
                object message = CameraMessage(pose, fovDegrees, mainMatrices, crop, portalSpec == null ? (MatrixD?)null : portalSpec.Projection);
                Stage("capture camera matrices");
                setup.Invoke(null, new[] { message, mainMatrices, Enum.ToObject(stereoRegion, 0) });
                if (portalSpec != null)
                {
                    // FOV=0 selects the engine's authored-projection branch.
                    // Restore descriptive FOV fields used by scene constants;
                    // the actual frusta and raster projection remain authored.
                    Set(mainMatrices, "FovH", (float)(2 * Math.Atan(1 / Math.Max(1e-6, Math.Abs(portalSpec.Projection.M11)))));
                    Set(mainMatrices, "FovV", (float)(2 * Math.Atan(1 / Math.Max(1e-6, Math.Abs(portalSpec.Projection.M22)))));
                }
                PrepareShadowConstants(renderContext, cascades, mainShadowBuffers);
                Stage("private particle shader preparation");
                Particles.Prepare(resourceDevice, (Vector3D)commonSnapshot.Value(commonLastCamera), pose.Translation);
                var drawArgs = new object[] { target, null };
                SchedulerFinished = SchedulerEntered = CullingFinished = CullingReset = CullingDoneAttempted = false;
                if (portalSpec != null)
                {
                    if (portals == null || portalSpec.Epoch != portals.Epoch) return false;
                    portalSession = portals.Begin(renderContext, portalSpec, size, size, 1);
                    if (portalSession == null) return false;
                }
                sceneStarted = true;
                Stage("invoke native DrawGameScene; auxiliary target");
                try { DrawScene.Invoke(null, drawArgs); }
                finally
                {
                    // The scene's caller owns its auxiliary debug AO lease.
                    if (drawArgs[1] != null && !ReferenceEquals(drawArgs[1], mainAmbient)) ReleaseBorrowed(drawArgs[1]);
                }
                clearState.Invoke(renderContext, null);
                Stage("capture target mip generation; RTV unbound");
                if (mipmaps) generateMips.Invoke(renderContext, new[] { target }); success = true;
            }
            catch { Healthy = false; throw; }
            finally
            {
                Stage("restore renderer/camera/pipeline state");
                // DependencyBatch.Execute joins every worker before propagating a
                // task exception. Done then consumes/disposes the completed view's
                // deferred contexts while its camera and buffers are still active.
                if (sceneStarted && !SchedulerFinished)
                {
                    Restore(CancelUnstartedCaptureJobs, failures);
                    Restore(() => SchedulerDone.Invoke(renderScheduler.GetValue(null), null), failures);
                }
                if (sceneStarted) Restore(FinishCaptureCulling, failures);
                if (portalSession != null)
                    Restore(() => {
                        // Scope closure is distinct from frame validity: even
                        // an unknown-shader frame must detach its joined guard.
                        if (!portals.EndAfterJoin(portalSession, SchedulerFinished && FrameIdle)) success = false;
                        if (portals.HasActiveSession) throw new InvalidOperationException("Portal workers did not retire; source capture disabled and private resources retained.");
                    }, failures);
                Restore(() => {
                    if (ownedGbuffer == null || gbufferCopy.GetValue(ownedGbuffer) == null) return;
                    try { gbufferDone.Invoke(ownedGbuffer, null); }
                    finally { gbufferCopy.SetValue(ownedGbuffer, null); }
                }, failures);
                Restore(cameraSnapshot.Restore, failures);
                Restore(() => gbufferMain.SetValue(null, mainBuffer), failures);
                Restore(() => settings.SetValue(null, mainSettings), failures);
                Restore(() => postprocess.SetValue(null, mainPostprocess), failures);
                Restore(() => overrides.SetValue(null, mainOverrides), failures);
                for (int i = 0; i < shadowBufferFields.Length; i++)
                { int index = i; Restore(() => shadowBufferFields[index].SetValue(cascades, mainShadowBuffers[index]), failures); }
                Restore(() => lockImmediate.SetValue(null, mainImmediateLock), failures);
                Restore(() => lodding.SetValue(null, mainLodding), failures);
                Restore(() => geometryLodEnabled.SetValue(geometry, mainLodEnabled), failures);
                Restore(() => viewport.SetValue(null, mainViewport), failures);
                Restore(() => resolution.SetValue(null, mainResolution), failures);
                Restore(() => fullViewport.SetValue(null, mainFullViewport), failures);
                Restore(ReleasePendingBorrowed, failures);
                Restore(hbaoSnapshot.Restore, failures);
                Restore(bloomSnapshot.Restore, failures);
                Restore(debugSnapshot.Restore, failures);
                Restore(() => debugAmbient.SetValue(null, mainAmbient), failures);
                if (sceneSnapshot != null) Restore(sceneSnapshot.Restore, failures);
                Restore(RestoreSceneMutations, failures);
                Restore(commonSnapshot.Restore, failures);
                // Frame constants are GPU contents, not D3D pipeline state. Rewrite
                // the original view, then restore the CPU delta/timing fields again.
                Restore(() => updateFrameConstants.Invoke(null, null), failures);
                Restore(commonSnapshot.Restore, failures);
                Restore(() => clearState.Invoke(renderContext, null), failures);
                if (swapped)
                {
                    Restore(() => {
                        var args = new object[] { previousToken, null };
                        swapContextState.Invoke(dxContext, args); returnedToken = args[1];
                    }, failures);
                }
                foreach (var cache in gpuCache) Restore(cache.Restore, failures);
                CaptureOutput = null;
                Restore(() => DisposeObject(previousToken), failures);
                Restore(() => DisposeObject(returnedToken), failures);
                if (failures.Count != 0)
                { Healthy = false; throw new AggregateException("Native capture state restoration failed; capture disabled.", failures); }
                Stage("renderer/camera/pipeline restored");
            }
            return success;
        }
        internal void CancelUnstartedCaptureJobs()
        {
            object batch = schedulerBatch.GetValue(renderScheduler.GetValue(null));
            int jobs = (int)batchJobCount.GetValue(batch);
            if (jobs == 0) return;
            // Execute joins its workers and clears its batch in finally, even
            // when a task failed. Only a never-entered batch can be cancelled.
            if (SchedulerEntered) throw new InvalidOperationException("Capture scheduler returned with unfinished jobs; capture disabled.");
            clearBatch.Invoke(batch, new object[] { jobs });
        }
        internal void FinishCaptureCulling()
        {
            object manager = cullManager.GetValue(null);
            var failures = new List<Exception>();
            if (!CullingFinished && (int)cullQuerySize.GetValue(getCullQueries.Invoke(manager, null)) != 0)
            {
                if (CullingDoneAttempted) failures.Add(new InvalidOperationException("Capture cull work finalization failed; pooled work was not returned twice."));
                else
                {
                    CullingDoneAttempted = true;
                    Restore(() => { CullDone.Invoke(manager, null); CullingFinished = true; }, failures);
                }
            }
            if (!CullingReset)
                Restore(() => { CullReset.Invoke(manager, null); CullingReset = true; }, failures);
            if ((int)cullQuerySize.GetValue(getCullQueries.Invoke(manager, null)) != 0)
                failures.Add(new InvalidOperationException("Capture cull queries were not retired before the main view."));
            if (failures.Count != 0) throw new AggregateException("Capture culling retirement failed.", failures);
        }
        internal void ClearAtlas(object target)
        { WithCopyState((context, dx) => clearTarget.Invoke(context, new[] { target, color.Invoke(new object[] { 0f, 0f, 0f, 0f }) })); }
        internal void CopyTile(object source, object atlas, CaptureTileAtlas.Tile tile)
        {
            var src = (Vector2I)targetSize.GetValue(source); var dst = (Vector2I)targetSize.GetValue(atlas);
            if (tile.Size < 64 || tile.Size > src.X || tile.Size > src.Y || tile.X < 0 || tile.Y < 0 || tile.X + tile.Size > dst.X || tile.Y + tile.Size > dst.Y)
                throw new ArgumentException("Atlas copy exceeds owned resource dimensions: crop "+tile.Size+" at "+tile.X+","+tile.Y+"; scratch "+src+"; atlas "+dst+".");
            WithCopyState((context, dx) => copyRegion.Invoke(dx, new object[] { targetResource.GetValue(source), 0,
                resourceRegion.Invoke(new object[] { 0, 0, 0, tile.Size, tile.Size, 1 }), targetResource.GetValue(atlas), 0, tile.X, tile.Y, 0 }));
        }
        internal void PublishAtlas(object staging, object front)
        {
            if (!Equals(targetSize.GetValue(staging), targetSize.GetValue(front))) throw new ArgumentException("Atlas publication dimensions differ.");
            WithCopyState((context, dx) => { generateMips.Invoke(context, new[] { staging });
                copyResource.Invoke(dx, new[] { targetResource.GetValue(staging), targetResource.GetValue(front) }); });
        }
        internal void WithIsolatedPass(Action<object> action)
        {
            if (action == null) throw new ArgumentNullException("action");
            WithCopyState((context, dx) => action(context));
        }
        void WithCopyState(Action<object, object> action)
        {
            EnsureDevice(); object context = rc.GetValue(null), dx = deviceContext.GetValue(context);
            var caches = new List<FieldSnapshot> { new FieldSnapshot(contextState.GetValue(context)) };
            foreach (var stage in stages) caches.Add(new FieldSnapshot(stage.GetValue(context)));
            object previous = null, returned = null; bool swapped = false;
            try
            {
                var args = new object[] { contextToken, null }; swapContextState.Invoke(dx, args); previous = args[1]; swapped = true;
                clearState.Invoke(context, null); action(context, dx);
            }
            finally
            {
                var failures = new List<Exception>(); Restore(() => clearState.Invoke(context, null), failures);
                if (swapped) Restore(() => { var args = new object[] { previous, null }; swapContextState.Invoke(dx, args); returned = args[1]; }, failures);
                foreach (var cache in caches) Restore(cache.Restore, failures);
                Restore(() => DisposeObject(previous), failures); Restore(() => DisposeObject(returned), failures);
                if (failures.Count != 0) { Healthy = false; throw new AggregateException("Atlas copy state restoration failed.", failures); }
            }
        }
        static object CaptureOverrides(object main, int profile)
        {
            if (profile < 0 || profile > 1) throw new ArgumentOutOfRangeException("profile");
            object clone = Clone(main);
            if (profile == 1) foreach (string name in new[] { "Fog", "SSAO", "Shadows", "Foliage", "Bloom", "Flares" }) Set(clone, name, false);
            return clone;
        }
        internal static bool ScratchFits(int logical, int width, int height)
        { return logical >= 64 && logical <= 2048 && width == height && width >= logical && width <= 2048; }
        void PrepareShadowConstants(object context, object cascades, object[] original)
        {
            if (original.Any(x => x == null)) return;
            if (ownedShadowBuffers == null)
            {
                ownedShadowBuffers = new object[shadowBufferFields.Length];
                var usage = createShadowBuffer.GetParameters()[3].ParameterType;
                for (int i = 0; i < ownedShadowBuffers.Length; i++)
                    ownedShadowBuffers[i] = createShadowBuffer.Invoke(bufferManager.GetValue(null), new object[] {
                        "HDR.DirectCamera.ShadowConstants." + i, (int)shadowByteSize.GetValue(original[i]), null, Enum.Parse(usage, "Dynamic"), false });
            }
            for (int i = 0; i < ownedShadowBuffers.Length; i++)
            {
                // Native getters translate each cached world projection to the
                // current optical origin. Only the private constants are written.
                fillShadowConstants.Invoke(cascades, new[] { context, ownedShadowBuffers[i] });
                shadowBufferFields[i].SetValue(cascades, ownedShadowBuffers[i]);
            }
        }

        void EnsureDevice()
        {
            object current = device.GetValue(null);
            if (current == null) throw new InvalidOperationException("Render device is unavailable.");
            if (!ReferenceEquals(current, resourceDevice) || contextToken == null)
            {
                ReleaseDeviceResources();
                var levels = Array.CreateInstance(featureLevel, 1); levels.SetValue(Enum.Parse(featureLevel, "Level_11_0"), 0);
                var parameters = createContextState.GetParameters();
                var args = new object[] { Enum.ToObject(parameters[0].ParameterType, 0), levels, Enum.ToObject(featureLevel, 0) };
                object preparedToken = createContextState.Invoke(current, args);
                if (preparedToken == null) throw Changed("created native context state");
                contextToken = preparedToken; resourceDevice = current;
            }
        }
        sealed class CaptureBuffers
        {
            internal object Gbuffer;
            internal object[] Hbao;
            internal int Samples, Quality;
        }
        void PrepareCaptureBuffers(int size, int samples, int quality)
        {
            CaptureBuffers group;
            var key = Tuple.Create(size, samples, quality);
            if (captureBuffers.TryGetValue(key, out group))
            {
                if (group.Samples != samples || group.Quality != quality) throw new InvalidOperationException("Capture multisampling configuration changed; a new renderer epoch is required.");
                ownedGbuffer = group.Gbuffer; for (int i = 0; i < hbaoTextures.Length; i++) hbaoTextures[i].SetValue(null, group.Hbao[i]); return;
            }
            if (captureBuffers.Count >= 10) throw new InvalidOperationException("Capture buffer configuration quota exceeded.");
            if (samples < 1 || samples > 8 || quality < 0) throw Changed("capture multisampling bounds");
            group = new CaptureBuffers { Gbuffer = gbufferConstructor.Invoke(null), Samples = samples, Quality = quality };
            object hdr = gbufferHdr.GetValue(null);
            bool captureHbaoFields = false;
            try
            {
                gbufferResize.Invoke(group.Gbuffer, new[] { (object)size, size, samples, quality, hdr });
                foreach (var field in hbaoTextures) field.SetValue(null, null);
                captureHbaoFields = true;
                hbaoInit.Invoke(null, null);
                group.Hbao = hbaoTextures.Select(f => f.GetValue(null)).ToArray();
                if (group.Hbao.Any(x => x == null)) throw Changed("private HBAO screen resources");
                captureBuffers.Add(key, group); ownedGbuffer = group.Gbuffer; bufferSize = size;
            }
            catch
            {
                try { if (captureHbaoFields) hbaoRelease.Invoke(null, null); } finally { gbufferRelease.Invoke(group.Gbuffer, null); }
                throw;
            }
            finally { gbufferHdr.SetValue(null, hdr); }
            foreach (var attachment in gbufferAttachments)
            {
                object value = attachment.GetValue(ownedGbuffer);
                if (value == null) throw Changed("owned GBuffer attachment");
                var actual = (Vector2I)targetSize.GetValue(value);
                if (actual.X < size || actual.Y < size) throw Changed("owned GBuffer resource dimensions");
            }
        }
        internal void ReleaseDeviceResources()
        {
            var hbaoSnapshot = new StaticSnapshot(hbaoTextures);
            var groups = captureBuffers.Values.ToArray(); captureBuffers.Clear();
            object token = contextToken; contextToken = null; resourceDevice = null; ownedGbuffer = null; bufferSize = 0;
            var failures = new List<Exception>();
            if (portals != null) Restore(portals.ResetRenderResources, failures);
            foreach (var group in groups)
            {
                Restore(() => gbufferRelease.Invoke(group.Gbuffer, null), failures);
                Restore(() => { for (int i = 0; i < hbaoTextures.Length; i++) hbaoTextures[i].SetValue(null, group.Hbao[i]); hbaoRelease.Invoke(null, null); }, failures);
            }
            Restore(hbaoSnapshot.Restore, failures); Restore(() => DisposeObject(token), failures);
            Restore(Particles.Dispose, failures);
            if (ownedShadowBuffers != null)
            {
                var handles = Array.CreateInstance(shadowBufferType, ownedShadowBuffers.Length);
                for (int i = 0; i < ownedShadowBuffers.Length; i++) handles.SetValue(ownedShadowBuffers[i], i);
                ownedShadowBuffers = null;
                Restore(() => disposeShadowBuffers.Invoke(bufferManager.GetValue(null), new object[] { handles }), failures);
            }
            if (failures.Count != 0) throw new AggregateException("Capture device resource cleanup failed.", failures);
        }
        internal void DisposeNative()
        {
            // Terminal teardown removes private portal hooks on the render
            // thread; a device/world reset only retires their GPU resources.
            if (portals != null) { portals.Dispose(); portals = null; }
            if (portalBillboards != null) { portalBillboards.Dispose(); portalBillboards = null; }
            ReleaseDeviceResources();
        }
        object CameraMessage(MatrixD pose, double fovDegrees, object original, CaptureTileAtlas.Tile? crop = null, MatrixD? explicitProjection = null)
        {
            var tile = crop ?? CaptureTileAtlas.Uniform(64).At(0);
            var projection = explicitProjection.HasValue ? new CaptureTileProjection.View((float)(fovDegrees * Math.PI / 180), 0, 0,
                (Matrix)explicitProjection.Value, (Matrix)explicitProjection.Value) :
                CaptureTileProjection.Create(fovDegrees, (Matrix)Read(original, "OriginalProjection"),
                (Matrix)Read(original, "OriginalProjectionFar"), tile);
            object message = Activator.CreateInstance(messageType, true);
            Set(message, "ViewMatrix", MatrixD.Invert(pose)); Set(message, "CameraPosition", pose.Translation);
            Set(message, "FOV", projection.Fov);
            Set(message, "FOVForSkybox", projection.Fov);
            Set(message, "NearPlane", Read(original, "NearClipping")); Set(message, "FarPlane", Read(original, "FarClipping"));
            Set(message, "FarFarPlane", Read(original, "LargeDistanceFarClipping"));
            Set(message, "ProjectionOffsetX", projection.OffsetX); Set(message, "ProjectionOffsetY", projection.OffsetY);
            Set(message, "ProjectionMatrix", projection.Near);
            Set(message, "ProjectionFarMatrix", projection.Far);
            if (explicitProjection.HasValue)
            {
                // Both capture depth families use the same complementary
                // projection, so one private cutoff texture has one depth ABI.
                var authored = explicitProjection.Value;
                if (!PortalProjection.ComplementaryProjection(authored)) throw new ArgumentException("Portal capture requires complementary perspective depth.");
                Set(message, "ProjectionMatrix", (Matrix)authored);
                Set(message, "ProjectionFarMatrix", (Matrix)authored);
                // Positive FOV makes SetupCameraMatricesInternal replace the
                // supplied matrix with a symmetric infinite projection.
                Set(message, "FOV", 0f);
                Set(message, "ProjectionOffsetX", (float)authored.M31); Set(message, "ProjectionOffsetY", (float)authored.M32);
            }
            Set(message, "LastMomentUpdateIndex", 1); Set(message, "Smooth", false);
            return message;
        }
        static SceneMemberSnapshot CaptureSceneEnvironment()
        {
            var scene = FindType(null, "VRage.Render11.Scene.MyScene11");
            object instance = Field(scene, "Instance", Static).GetValue(null);
            for (Type current = scene; current != null; current = current.BaseType)
            {
                var field = current.GetField("Environment", Instance | BindingFlags.DeclaredOnly);
                if (field != null) return new SceneMemberSnapshot(instance, field);
            }
            throw Changed("scene environment camera");
        }
        sealed class SceneMemberSnapshot
        {
            readonly object owner, original;
            readonly FieldInfo field;
            readonly FieldSnapshot contents;
            internal SceneMemberSnapshot(object owner, FieldInfo field)
            {
                this.owner = owner; this.field = field; original = field.GetValue(owner);
                if (!field.FieldType.IsValueType && original != null) contents = new FieldSnapshot(original);
            }
            internal void Restore()
            {
                if (contents != null) contents.Restore();
                // MyScene.Environment is a struct. Restoring a separately boxed
                // copy would leave its actual camera position changed in the scene.
                field.SetValue(owner, original);
            }
        }
        internal static MatrixD OpticalPose(MyCameraBlock camera)
        {
            var pose = camera.WorldMatrix;
            // The block's optical centre comes from its shipped model dummy. The
            // entity view controller and its FOV/zoom fields remain untouched.
            var model = camera.Model;
            if (model != null && model.Dummies != null)
            {
                var dummy = model.Dummies.ContainsKey("camera") ? model.Dummies["camera"] : null;
                if (dummy != null)
                    pose.Translation = Vector3D.Transform(dummy.Matrix.Translation, pose);
            }
            return MatrixD.CreateWorld(pose.Translation, pose.Forward, pose.Up);
        }

        static void ReleaseBorrowed(object value)
        {
            var contract = value.GetType().GetInterfaces().FirstOrDefault(t => t.FullName == "VRage.Render11.Resources.IBorrowedSrvTexture");
            Need(contract == null ? null : contract.GetMethod("Release"), "borrowed debug lease release").Invoke(value, null);
        }
        static void DisposeObject(object value)
        { var disposable = value as IDisposable; if (disposable != null) disposable.Dispose(); }
        static void Restore(Action action, List<Exception> failures)
        { try { action(); } catch (Exception ex) { failures.Add(ex.GetBaseException()); } }
        static object Clone(object value)
        { return typeof(object).GetMethod("MemberwiseClone", Instance).Invoke(value, null); }
        static object Read(object value, string name)
        { return Field(value.GetType(), name, Instance).GetValue(value); }
        static void Set(object value, string name, object next)
        { WritableField(value.GetType(), name, Instance).SetValue(value, next); }
        static void SetNested(object owner, string parent, string name, object next)
        { var field = WritableField(owner.GetType(), parent, Instance); object value = Clone(field.GetValue(owner)); Set(value, name, next); field.SetValue(owner, value); }
        static FieldInfo Field(Type type, string name, BindingFlags flags)
        { return Need(type.GetField(name, flags), type.FullName + "." + name); }
        static FieldInfo WritableField(Type type, string name, BindingFlags flags)
        {
            var field = Field(type, name, flags);
            if (field.IsInitOnly || field.IsLiteral) throw Changed("mutable field " + type.FullName + "." + name);
            return field;
        }
        static PropertyInfo Property(Type type, string name)
        { return Need(type.GetProperty(name, Static), type.FullName + "." + name); }
        static MethodInfo Method(Type type, string name, BindingFlags flags, params Type[] parameters)
        { return Need(type.GetMethod(name, flags, null, parameters, null), type.FullName + "." + name); }
        static T Need<T>(T value, string name) where T : class
        { if (value == null) throw Changed(name); return value; }
        static InvalidOperationException Changed(string member)
        { return new InvalidOperationException("Native camera renderer ABI unavailable: " + member + "."); }
        internal static Type FindType(Assembly preferred, string name)
        {
            var type = preferred == null ? null : preferred.GetType(name, false);
            if (type != null) return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            { type = assembly.GetType(name, false); if (type != null) return type; }
            if (name.StartsWith("SharpDX.D3DCompiler.", StringComparison.Ordinal))
            { type = Assembly.Load("SharpDX.D3DCompiler").GetType(name, false); if (type != null) return type; }
            throw Changed(name);
        }
        sealed class StaticSnapshot
        {
            readonly FieldInfo[] fields;
            readonly object[] values;
            readonly Array[] arrays;
            internal StaticSnapshot(FieldInfo[] fields)
            {
                this.fields = fields; values = fields.Select(f => f.GetValue(null)).ToArray();
                arrays = values.Select(v => v is Array ? (Array)((Array)v).Clone() : null).ToArray();
            }
            internal object Value(FieldInfo field)
            { for (int i = 0; i < fields.Length; i++) if (fields[i] == field) return values[i]; throw Changed("captured static field"); }
            internal void ClearArrays()
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    var array = values[i] as Array;
                    if (array == null || !ReferenceEquals(fields[i].GetValue(null), array)) throw Changed("bloom scratch array identity");
                    Array.Clear(array, 0, array.Length);
                }
            }
            internal void Restore()
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    if (arrays[i] != null) Array.Copy(arrays[i], (Array)values[i], arrays[i].Length);
                    if (!fields[i].IsInitOnly && !fields[i].IsLiteral) fields[i].SetValue(null, values[i]);
                    else if (fields[i].FieldType.IsValueType ? !Equals(fields[i].GetValue(null), values[i]) :
                        !ReferenceEquals(fields[i].GetValue(null), values[i])) throw Changed("readonly renderer static identity changed");
                }
            }
        }
        sealed class CameraMatricesSnapshot
        {
            readonly FieldInfo environment, matrices;
            readonly object owner;
            readonly FieldSnapshot state;
            readonly FieldSnapshot[] frusta;
            internal object Matrices { get; private set; }
            internal CameraMatricesSnapshot(FieldInfo environment, FieldInfo matrices)
            {
                this.environment = environment; this.matrices = matrices;
                owner = Need(environment.GetValue(null), "existing environment");
                Matrices = Need(matrices.GetValue(owner), "existing camera matrices");
                state = new FieldSnapshot(Matrices);
                // SetupCameraMatricesInternal mutates each frustum's Matrix and
                // cached planes/corners. Restoring its reference alone is not enough.
                frusta = new[] { "ViewFrustumClippedD", "ViewFrustumClippedFarD" }
                    .Select(name => Field(Matrices.GetType(), name, Instance).GetValue(Matrices))
                    .Where(value => value != null).Select(value => new FieldSnapshot(value)).ToArray();
            }
            internal void Restore()
            {
                if (!ReferenceEquals(environment.GetValue(null), owner) || !ReferenceEquals(matrices.GetValue(owner), Matrices))
                    throw Changed("readonly camera owner identity changed");
                foreach (var frustum in frusta) frustum.Restore();
                state.Restore();
            }
        }
        sealed class FieldSnapshot
        {
            readonly object owner;
            readonly FieldInfo[] fields;
            readonly object[] values;
            readonly Array[] arrays;
            internal FieldSnapshot(object owner, bool valueFieldsOnly = false)
            {
                this.owner = owner;
                var all = new List<FieldInfo>();
                for (Type type = owner.GetType(); type != null; type = type.BaseType)
                    all.AddRange(type.GetFields(Instance | BindingFlags.DeclaredOnly).Where(f => !f.IsLiteral &&
                        (!valueFieldsOnly || f.FieldType.IsValueType && f.Name != "Updated")));
                fields = all.ToArray(); values = fields.Select(f => f.GetValue(owner)).ToArray();
                arrays = values.Select(v => v is Array ? (Array)((Array)v).Clone() : null).ToArray();
            }
            internal void Restore()
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    if (arrays[i] != null) Array.Copy(arrays[i], (Array)values[i], arrays[i].Length);
                    if (!fields[i].IsInitOnly) fields[i].SetValue(owner, values[i]);
                    else if (fields[i].FieldType.IsValueType ? !Equals(fields[i].GetValue(owner), values[i]) :
                        !ReferenceEquals(fields[i].GetValue(owner), values[i]))
                        throw Changed("readonly renderer cache reference changed");
                }
            }
        }
    }
}
