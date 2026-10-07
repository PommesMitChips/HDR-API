using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.ModAPI;
using VRageMath;
using PbBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        const int MaxTrackedBlocks = 10000;
        readonly Dictionary<long, ConstructPreview> _constructPreviews = new Dictionary<long, ConstructPreview>();
        readonly List<long> _previewCleanup = new List<long>();
        readonly Dictionary<long, int> _previewRetryTicks = new Dictionary<long, int>();

        sealed class ConstructPreview
        {
            public long RootId;
            public Vector3D Center;
            public double BoundRadius = 1;
            public int LastRefresh = -60;
            public readonly Dictionary<long, GridPreview> Grids = new Dictionary<long, GridPreview>();
        }
        sealed class GridPreview
        {
            public IMyCubeGrid Source;
            public MyCubeGrid Preview;
            public bool Dirty = true;
            public int RetryAt;
            public float LastOpacity = -1;
            public readonly List<ModelPreview> Models = new List<ModelPreview>();
            public readonly List<MyEntity> ModelRoots = new List<MyEntity>();
            public readonly List<MyEntity> NativeModels = new List<MyEntity>();
            public void Changed(IMySlimBlock block)
            {
                if (block != null && ReferenceEquals(block.CubeGrid, Source)) Dirty = true;
            }
            public void Close()
            {
                Source.OnBlockAdded -= Changed;
                Source.OnBlockRemoved -= Changed;
                if (Preview != null)
                {
                    MyAPIGateway.Entities.UnregisterForDraw(Preview);
                    Preview.Close(); Preview = null;
                }
                CloseModels();
            }
            public void CloseModels()
            {
                foreach (var root in ModelRoots)
                {
                    MyAPIGateway.Entities.UnregisterForDraw(root);
                    root.Close();
                }
                ModelRoots.Clear(); Models.Clear(); NativeModels.Clear();
            }
        }
        sealed class ModelPreview
        {
            public MyEntity Source;
            public MyEntity Preview;
            public bool IsRoot;
            public bool RenderInitialized;
        }

        MyTuple<bool, string> TrackConstruct(PbBlock caller, PbBlock console, bool enabled, double displayRadius, Vector3D offset)
        {
            return Guard(() =>
            {
                var target = Authorize(caller, console);
                if (!Geometry.Finite(displayRadius) || displayRadius < 0.05 || displayRadius > 5
                    || !Geometry.Finite(offset.X) || !Geometry.Finite(offset.Y) || !Geometry.Finite(offset.Z) || offset.LengthSquared() > 100)
                    throw new ArgumentException("Ship radius must be 0.05–5 display units; offset must be finite and within 10 units.");
                var scene = GetScene(target.EntityId);
                if (scene.TrackingCallerId != 0 && scene.TrackingCallerId != caller.EntityId)
                    throw new ArgumentException("Another PB controls this Console's live construct layer.");
                if (enabled)
                {
                    var grids = new List<IMyCubeGrid>();
                    MyAPIGateway.GridGroups.GetGroup(((IMyProgrammableBlock)caller).CubeGrid, GridLinkTypeEnum.Mechanical, grids);
                    CheckConstructBudget(grids);
                    if(target is IMyTextPanel)scene.LcdCallerId=caller.EntityId; scene.TrackedRootId = caller.CubeGrid.EntityId;
                    scene.TrackingCallerId = caller.EntityId;
                    scene.ShipRadius = displayRadius;
                    scene.ShipOffset = offset;
                    scene.ShipMetresPerUnit = 0;
                }
                else { scene.TrackedRootId = 0; scene.TrackingCallerId = 0; }
            });
        }

        MyTuple<bool, string> TrackConstructWorld(PbBlock caller, PbBlock console, bool enabled, double metresPerUnit, Vector3D origin)
        {
            if (!Geometry.Finite(metresPerUnit) || metresPerUnit < 0.001 || metresPerUnit > 1000000
                || !Geometry.Finite(origin.X) || !Geometry.Finite(origin.Y) || !Geometry.Finite(origin.Z) || origin.LengthSquared() > 1e12)
                return new MyTuple<bool, string>(false, "Map requires finite origin and metres-per-unit in [0.001,1000000].");
            var result = TrackConstruct(caller, console, enabled, 0.55, Vector3D.Zero);
            if (!result.Item1 || !enabled) return result;
            var scene = _scenes[console.EntityId];
            scene.ShipMetresPerUnit = metresPerUnit;
            scene.ShipOrigin = origin;
            return result;
        }

        static void CheckConstructBudget(List<IMyCubeGrid> grids)
        {
            int blocks = 0;
            foreach (var grid in grids)
            {
                var slimBlocks = new List<IMySlimBlock>();
                grid.GetBlocks(slimBlocks);
                blocks += slimBlocks.Count;
            }
            if (blocks > MaxTrackedBlocks) throw new ArgumentException("Live preview supports at most 10,000 blocks across all subgrids.");
        }

        void UpdateConstructPreviews(bool refreshCaches)
        {
            if (MyAPIGateway.Utilities.IsDedicated || !ClientRenderingEnabled) return;
            _previewCleanup.Clear();
            foreach (var pair in _constructPreviews)
            {
                Scene scene;
                if (!_scenes.TryGetValue(pair.Key, out scene) || scene.TrackedRootId == 0 || scene.TrackedRootId != pair.Value.RootId)
                    _previewCleanup.Add(pair.Key);
            }
            foreach (long id in _previewCleanup) CloseConstructPreview(id);
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;
            var camera = MyAPIGateway.Session.Camera.Position;
            foreach (var scene in _scenes.Values)
            {
                if (scene.TrackedRootId == 0) continue;
                int retry;
                if (_previewRetryTicks.TryGetValue(scene.ConsoleId, out retry) && _ticks < retry) continue;
                var console = MyAPIGateway.Entities.GetEntityById(scene.ConsoleId) as IMyTerminalBlock; if(console is IMyTextPanel){CloseConstructPreview(scene.ConsoleId);continue;}
                var root = MyAPIGateway.Entities.GetEntityById(scene.TrackedRootId) as IMyCubeGrid;
                if (console == null || console.Closed || root == null || root.Closed)
                { CloseConstructPreview(scene.ConsoleId); continue; }
                var displayVolume = GetDisplayVolume(scene, console);
                float layerOpacity = ClientLayerAlpha(scene, scene.TrackingCallerId, scene.ConstructLayer);
                bool visible = layerOpacity > 0 && console.IsWorking && Vector3D.DistanceSquared(camera, console.GetPosition()) <= DrawDistance * DrawDistance;
                ConstructPreview construct;
                if (!_constructPreviews.TryGetValue(scene.ConsoleId, out construct))
                {
                    if (!visible || !refreshCaches) continue;
                    construct = new ConstructPreview { RootId = root.EntityId };
                    _constructPreviews.Add(scene.ConsoleId, construct);
                }
                try
                {
                    var rootInverse = MatrixD.Invert(root.WorldMatrix);
                    if (refreshCaches && visible && _ticks - construct.LastRefresh >= 60)
                    {
                        construct.LastRefresh = _ticks;
                        RefreshConstruct(construct, root, rootInverse);
                    }
                    if (refreshCaches) continue; // Model poses are committed with wire geometry in Draw().
                    foreach (var grid in construct.Grids.Values)
                    {
                        if (grid.Source.Closed || grid.Preview == null) continue;
                        if (grid.LastOpacity != layerOpacity)
                        {
                            float transparency = LayerRules.PreviewTransparency(layerOpacity);
                            grid.Preview.Render.Transparency = transparency; grid.Preview.Render.UpdateTransparency();
                            foreach (var model in grid.Models) if (model.Preview.Render != null)
                            { model.Preview.Render.Transparency = transparency; model.Preview.Render.UpdateTransparency(); }
                            grid.LastOpacity = layerOpacity;
                        }
                        double fit = scene.ShipMetresPerUnit > 0 ? 1.0 / scene.ShipMetresPerUnit : scene.ShipRadius / construct.BoundRadius;
                        var center = scene.ShipMetresPerUnit > 0 ? scene.ShipOrigin : construct.Center;
                        var modelToMap = grid.Source.WorldMatrix * rootInverse
                            * MatrixD.CreateTranslation(-center) * MatrixD.CreateScale(fit)
                            * MatrixD.CreateTranslation(scene.ShipOffset);
                        var world = modelToMap * LocalView(scene) * console.WorldMatrix;
                        // SE position components own uniform scaling separately from pose.
                        grid.Preview.PositionComp.Scale = (float)(fit * scene.Scale);
                        world = MatrixD.Normalize(world);
                        grid.Preview.PositionComp.SetWorldMatrix(ref world);
                        grid.Preview.Render.Visible = visible && (displayVolume == null || displayVolume.ContainsBox(grid.Preview.PositionComp.WorldAABB));
                        // Fat-block renderers parent their meshes to native culling cells.
                        // Hide those copies and draw plain model entities independently.
                        foreach (var native in grid.NativeModels)
                            if (!native.Closed && native.Render != null) native.Render.Visible = false;
                        var worldToDisplay = rootInverse * MatrixD.CreateTranslation(-center)
                            * MatrixD.CreateScale(fit) * MatrixD.CreateTranslation(scene.ShipOffset)
                            * LocalView(scene) * console.WorldMatrix;
                        foreach (var model in grid.Models)
                        {
                            if (model.Source.Closed || model.Preview.Closed || model.Preview.Render == null) continue;
                            if (model.IsRoot)
                            {
                                var modelWorld = MatrixD.Normalize(model.Source.WorldMatrix * worldToDisplay);
                                model.Preview.PositionComp.Scale = (float)(fit * scene.Scale);
                                model.Preview.PositionComp.SetWorldMatrix(ref modelWorld);
                            }
                            else
                            {
                                model.Preview.PositionComp.Scale = null;
                                var local = model.Source.PositionComp.LocalMatrix;
                                model.Preview.PositionComp.SetLocalMatrix(ref local);
                            }
                            bool modelVisible = visible && (displayVolume == null || displayVolume.ContainsBox(model.Preview.PositionComp.WorldAABB));
                            model.Preview.Render.Visible = modelVisible;
                            if (modelVisible && !model.RenderInitialized)
                            {
                                // Visible only toggles existing render objects. These
                                // models entered the scene hidden, so explicitly create
                                // their objects after assigning their miniature pose.
                                model.Preview.Render.AddRenderObjects();
                                model.Preview.Render.UpdateRenderObject(true, false);
                                model.RenderInitialized = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    VRage.Utils.MyLog.Default.WriteLineAndConsole("HoloMap live construct: " + ex);
                    CloseConstructPreview(scene.ConsoleId);
                    _previewRetryTicks[scene.ConsoleId] = _ticks + 300;
                }
            }
        }

        void RefreshConstruct(ConstructPreview construct, IMyCubeGrid root, MatrixD rootInverse)
        {
            var grids = new List<IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(root, GridLinkTypeEnum.Mechanical, grids);
            CheckConstructBudget(grids);
            var ids = new HashSet<long>();
            var bounds = BoundingBoxD.CreateInvalid();
            bool refit = construct.Grids.Count == 0;
            foreach (var source in grids)
            {
                ids.Add(source.EntityId);
                var relative = source.WorldMatrix * rootInverse;
                foreach (var corner in source.LocalAABB.GetCorners()) bounds.Include(Vector3D.Transform(corner, relative));
                GridPreview cache;
                if (!construct.Grids.TryGetValue(source.EntityId, out cache))
                {
                    cache = new GridPreview { Source = source };
                    source.OnBlockAdded += cache.Changed;
                    source.OnBlockRemoved += cache.Changed;
                    construct.Grids.Add(source.EntityId, cache);
                    refit = true;
                }
                if (cache.Dirty && _ticks >= cache.RetryAt)
                {
                    cache.RetryAt = _ticks + 300;
                    var replacement = CreatePreview(source);
                    if (cache.Preview != null) { MyAPIGateway.Entities.UnregisterForDraw(cache.Preview); cache.Preview.Close(); }
                    cache.Preview = replacement;
                    BindBlockModels(cache);
                    cache.LastOpacity = -1;
                    cache.Dirty = false;
                    refit = true;
                }
            }
            var removed = new List<long>();
            foreach (var pair in construct.Grids) if (!ids.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (long id in removed) { construct.Grids[id].Close(); construct.Grids.Remove(id); refit = true; }
            // Articulation changes the displayed pose, not the user's view frame.
            // Refit only when grids or blocks change, preventing motion-driven recentering.
            if (refit)
            {
                construct.Center = (bounds.Min + bounds.Max) * 0.5;
                construct.BoundRadius = Math.Max(0.01, (bounds.Max - bounds.Min).Length() * 0.5);
            }
        }

        static MyCubeGrid CreatePreview(IMyCubeGrid source)
        {
            // Preserve actual armor shapes and block models in a render-only grid.
            var builder = (MyObjectBuilder_CubeGrid)source.GetObjectBuilder();
            PreviewBuilder.Prepare(builder);
            var preview = MyAPIGateway.Entities.CreateFromObjectBuilderNoinit(builder) as MyCubeGrid;
            if (preview == null) throw new InvalidOperationException("Cannot create a render-only grid.");
            try
            {
                // Set before initialization, keeping functional blocks out of simulation.
                preview.IsPreview = true;
                preview.Save = false;
                preview.SyncFlag = false;
                preview.Init(builder);
                preview.Render.CastShadows = false;
                preview.Render.Transparency = 0.35f;
                preview.Render.UpdateTransparency();
                preview.Render.Visible = false;
                MyAPIGateway.Entities.AddEntity(preview);
                return preview;
            }
            catch { preview.Close(); throw; }
        }

        static void BindBlockModels(GridPreview grid)
        {
            grid.CloseModels();
            var blocks = new List<IMySlimBlock>();
            grid.Source.GetBlocks(blocks);
            int sourceModels = 0, boundBlocks = 0;
            foreach (var block in blocks)
            {
                var source = block.FatBlock as MyEntity;
                if (source == null) continue;
                sourceModels++;
                var previewBlock = ((IMyCubeGrid)grid.Preview).GetCubeBlock(block.Position);
                var preview = previewBlock == null ? null : previewBlock.FatBlock as MyEntity;
                if (preview != null) HideNativeModelTree(preview, grid.NativeModels);
                // Use the public ModAPI interface. Concrete MyModel.AssetName is
                // prohibited by SE's whitelist even though ordinary C# compiles it.
                var sourceModel = ((IMyEntity)source).Model;
                if (sourceModel == null || string.IsNullOrEmpty(sourceModel.AssetName)) continue;
                var modelRoot = CreateIndependentModel(sourceModel.AssetName);
                grid.ModelRoots.Add(modelRoot);
                BindModelTree(source, modelRoot, grid.Models, true);
                MyAPIGateway.Entities.AddEntity(modelRoot);
                boundBlocks++;
            }
            VRage.Utils.MyLog.Default.WriteLineAndConsole("HoloMap preview models: grid=" + grid.Source.EntityId
                + " sourceBlocks=" + sourceModels + " boundBlocks=" + boundBlocks + " modelsWithSubparts=" + grid.Models.Count);
        }

        static MyEntity CreateIndependentModel(string modelPath)
        {
            var preview = new MyEntity();
            try
            {
                preview.Save = false; preview.SyncFlag = false; preview.IsPreview = true;
                preview.Init(null, modelPath, null, null, null);
                preview.DisplayName = "HoloMap_ModelOnly";
                preview.Flags &= ~EntityFlags.IsGamePrunningStructureObject;
                preview.Flags |= EntityFlags.IsNotGamePrunningStructureObject;
                return preview;
            }
            catch { preview.Close(); throw; }
        }

        static void HideNativeModelTree(MyEntity entity, List<MyEntity> hidden)
        {
            hidden.Add(entity);
            if (entity.Render != null) entity.Render.Visible = false;
            if (entity.Subparts != null)
                foreach (var part in entity.Subparts.Values) HideNativeModelTree(part, hidden);
        }

        static void BindModelTree(MyEntity source, MyEntity preview, List<ModelPreview> models, bool root)
        {
            preview.IsPreview = true;
            preview.Save = false;
            preview.SyncFlag = false;
            if (preview.Render != null)
            {
                preview.Render.CastShadows = false;
                preview.Render.Transparency = 0.35f;
                preview.Render.UpdateTransparency();
                preview.Render.Visible = false;
                preview.Render.EnableColorMaskHsv = true;
                if (source.Render != null) preview.Render.ColorMaskHsv = source.Render.ColorMaskHsv;
                models.Add(new ModelPreview { Source = source, Preview = preview, IsRoot = root });
            }
            if (source.Subparts == null || preview.Subparts == null) return;
            foreach (var pair in preview.Subparts)
            {
                MyEntitySubpart sourceSubpart;
                if (source.Subparts.TryGetValue(pair.Key, out sourceSubpart))
                    BindModelTree(sourceSubpart, pair.Value, models, false);
            }
        }

        void CloseConstructPreview(long id)
        {
            ConstructPreview construct;
            if (!_constructPreviews.TryGetValue(id, out construct)) return;
            foreach (var grid in construct.Grids.Values) grid.Close();
            _constructPreviews.Remove(id);
        }
        void CloseConstructPreviews()
        {
            foreach (var construct in _constructPreviews.Values) foreach (var grid in construct.Grids.Values) grid.Close();
            _constructPreviews.Clear();
            _previewRetryTicks.Clear();
        }
    }

    internal static class PreviewBuilder
    {
        internal static void Prepare(MyObjectBuilder_CubeGrid builder)
        {
            builder.EntityId = 0;
            builder.CreatePhysics = false;
            builder.IsStatic = true;
            builder.Editable = false;
            builder.DestructibleBlocks = false;
            builder.IsRespawnGrid = false;
            builder.DisplayName = "HoloMap_RenderOnly";
            builder.PositionAndOrientation = new MyPositionAndOrientation(MatrixD.Identity);
            builder.PersistentFlags = MyPersistentEntityFlags2.InScene;
            foreach (var block in builder.CubeBlocks)
            {
                block.EntityId = 0;
                var functional = block as MyObjectBuilder_FunctionalBlock;
                if (functional != null) functional.Enabled = false;
                var projector = block as MyObjectBuilder_ProjectorBase;
                if (projector != null) { projector.ProjectedGrid = null; projector.ProjectedGrids = null; }
                var mechanical = block as MyObjectBuilder_MechanicalConnectionBlock;
                if (mechanical != null)
                {
                    mechanical.TopBlockId = null;
                    mechanical.MasterToSlaveTransform = null;
                    mechanical.IsWelded = false; mechanical.ForceWeld = false;
                }
                var motor = block as MyObjectBuilder_MotorBase;
                if (motor != null) { motor.RotorEntityId = null; motor.WeldedEntityId = null; }
                var connector = block as MyObjectBuilder_ShipConnector;
                if (connector != null)
                {
                    connector.Connected = false; connector.ConnectedEntityId = 0;
                    connector.MasterToSlaveTransform = null; connector.MasterToSlaveGrid = null;
                    connector.IsApproaching = false; connector.IsConnecting = false;
                }
            }
        }
    }
}
