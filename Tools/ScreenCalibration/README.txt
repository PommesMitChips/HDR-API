ScreenCalibration: offline vanilla LCD screen geometry export

Run from the HDR_API directory:
  dotnet run --project Tools/ScreenCalibration -- artifacts/screen-calibration.json Mod/Data/Scripts/HoloMap/LcdScreenCalibration.cs

SE_BIN can point to another installed Space Engineers Bin64 directory.
For compiler references at a nondefault installation, also pass -p:GameBin=PATH.

The exporter reads game files only. It uses public MyModelImporter.ImportData and
GetTagData, follows GeometryDataAsset to LOD0, decodes packed positions as xyz*w,
and decodes normals following Content/Shaders/VertexTransformations.hlsli.
It extracts the actual screen material's UV rectangle, never an interaction dummy
or bounding box. UVs within 1e-5 of 0/1 are snapped to their intended corner;
the physical packed vertex positions remain exact. It requires four distinct
front corners, an affine orthogonal rectangle and two triangles with full area.

Six supported models: large/small LCDPanel, large/small LCDPanelWide, and
large/small TransparentLCD, each with its four native rotation materials.
Transparent models contain front and back meshes in the same screen material.
Only their +local-Z front face is calibrated; the renderer must cull the back.
The small transparent model's unrotated screen really has different dimensions,
center and depth from its three rotated versions. Do not simplify those records.

Output JSON records include exact physical basis/corners, material, rotation,
source asset names, and SHA256 hashes of the model and LOD0 geometry. Adjacent
.entries.txt contains static C# entries. The optional second argument verifies
every extracted C# entry appears in the production calibration source, failing
if installed game assets and the checked-in calibration have diverged.

Runtime production code uses only public ModAPI state: IMyModel.AssetName,
IMyTextSurface.Name and IMyLcdSurfaceComponent.SelectedRotationIndex. Unsupported
or inconsistent model/material/rotation combinations return false for native
renderer fallback. No runtime reflection, model importer or extra entities.
