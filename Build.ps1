param(
    [string]$GameBin = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64',
    [string]$PelicanHtml
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $GameBin 'Sandbox.Game.dll'))) {
    throw "Space Engineers assemblies not found in $GameBin"
}
$previousBin = $env:SE_BIN
try {
    $env:SE_BIN = $GameBin
    & (Join-Path $taskRoot 'Tools\GenerateLcdTriangles.ps1')
    if ($PelicanHtml) {
        & node (Join-Path $taskRoot 'Tools\PackPelican.mjs') $PelicanHtml
        if ($LASTEXITCODE -ne 0) { throw 'Packed pelican export failed.' }
    }
    & node (Join-Path $taskRoot 'Tools\ExportSvgFrames.mjs') (Join-Path $taskRoot 'Examples\Authoring\pulse.mjs') pulse
    if ($LASTEXITCODE -ne 0) { throw 'SVG authoring export failed.' }
    & node (Join-Path $taskRoot 'Tools\GenerateSampleAssets.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'SVG sample export failed.' }
    & dotnet run --project (Join-Path $taskRoot 'Tools\ScreenCalibration\ScreenCalibration.csproj') -- (Join-Path $taskRoot 'artifacts\screen-calibration.json') (Join-Path $taskRoot 'Mod\Data\Scripts\HoloMap\LcdScreenCalibration.cs')
    if ($LASTEXITCODE -ne 0) { throw 'LCD screen calibration does not match installed models.' }
    & dotnet run --project (Join-Path $taskRoot 'Tools\Checks\Checks.csproj') "-p:GameBin=$GameBin" -- $taskRoot
    if ($LASTEXITCODE -ne 0) { throw 'HDR API checks failed.' }
    Compress-Archive -Path (Join-Path $taskRoot 'Mod\*') -DestinationPath (Join-Path $taskRoot 'artifacts\HDR-API-0.9.10.zip') -Force
    Write-Host "Paste-ready PB demo: $(Join-Path $taskRoot 'artifacts\ConsoleDemo.pb.cs')"
    Write-Host "Paste-ready SVG/image demo: $(Join-Path $taskRoot 'artifacts\SvgDemo.pb.cs')"
    Write-Host "Paste-ready exported animation demo: $(Join-Path $taskRoot 'artifacts\SvgFramesDemo.pb.cs')"
    Write-Host "Compact Custom Data player: $(Join-Path $taskRoot 'artifacts\PackedPelican.pb.cs')"
    Write-Host "Multi-Console Custom Data player: $(Join-Path $taskRoot 'artifacts\MultiConsoleAnimation.pb.cs')"
    Write-Host "SVG/native appearance demo: $(Join-Path $taskRoot 'artifacts\AppearanceDemo.pb.cs')"
    Write-Host "Short mod-command demo (no copied API): $(Join-Path $taskRoot 'artifacts\DrawDemo.pb.cs')"
    Write-Host "Short-command appearance demo: $(Join-Path $taskRoot 'artifacts\AppearanceDemo.Draw.pb.cs')"
    Write-Host "Mod-side multi-display player: $(Join-Path $taskRoot 'artifacts\MultiConsoleAnimationDraw.pb.cs')"
    Write-Host "Compressed animation Custom Data: $(Join-Path $taskRoot 'artifacts\pelican.customdata.txt')"
    Write-Host "Local mod source: $(Join-Path $taskRoot 'Mod')"
}
finally { $env:SE_BIN = $previousBin }

Write-Host "LCD demo: $(Join-Path $taskRoot 'artifacts\LcdDemo.pb.cs')"
Write-Host "Projected screen demo: $(Join-Path $taskRoot 'artifacts\ProjectedScreenDemo.pb.cs')"
Write-Host "Raster/vector surface demo: $(Join-Path $taskRoot 'artifacts\RasterSurfaceDemo.pb.cs')"
Write-Host "Spherical LCD-video relay demo: $(Join-Path $taskRoot 'artifacts\SphericalCameraDemo.pb.cs')"
Write-Host "HDR feature demo: $(Join-Path $taskRoot 'artifacts\HdrFeaturesDemo.pb.cs')"
Write-Host "Native F UI demo: $(Join-Path $taskRoot 'artifacts\UiDemo.pb.cs')"
Write-Host "Plugin-free hologram effects demo: $(Join-Path $taskRoot 'artifacts\HologramEffectsDemo.pb.cs')"
Write-Host "Interactive slider and rotation demo: $(Join-Path $taskRoot 'artifacts\InteractiveControlsDemo.pb.cs')"
