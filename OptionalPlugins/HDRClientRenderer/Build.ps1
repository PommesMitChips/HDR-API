param(
    [string]$GameBin = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64',
    [ValidateSet('Legacy','Interim')][string]$Runtime = 'Legacy'
)
$ErrorActionPreference = 'Stop'
function Complete-ClientPackage([string]$TaskFolder,[string]$TaskRuntime) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HDRClientRenderer.dll.xml') -Destination $TaskFolder -Force
    Compress-Archive -LiteralPath (Join-Path $TaskFolder 'HDRClientRenderer.dll'),(Join-Path $TaskFolder 'HDRClientRenderer.dll.xml') -DestinationPath (Join-Path $PSScriptRoot ('artifacts\HDR-Client-Renderer-0.9.13-'+$TaskRuntime+'.zip')) -Force
}
if ($Runtime -eq 'Interim') {
    $taskNet10Output = Join-Path $PSScriptRoot 'artifacts\Interim'
    & dotnet build (Join-Path $PSScriptRoot 'HDRClientRenderer.Net10.csproj') --configuration Release "-p:GameBin=$GameBin" --output $taskNet10Output -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'HDR .NET 10 client renderer compilation failed.' }
    Complete-ClientPackage $taskNet10Output $Runtime
    Write-Output "Built optional, uninstalled .NET 10 IPlugin: $(Join-Path $taskNet10Output 'HDRClientRenderer.dll')"
    return
}
$output = Join-Path $PSScriptRoot 'artifacts\HDRClientRenderer.dll'
New-Item -ItemType Directory -Force -Path (Split-Path $output -Parent) | Out-Null
$sdkVersion = (& dotnet --version).Trim()
$dotnet = (Get-Command dotnet).Source
$compiler = Join-Path (Split-Path $dotnet -Parent) "sdk\$sdkVersion\Roslyn\bincore\csc.dll"
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('mscorlib.dll','System.dll','System.Core.dll') | ForEach-Object { "/reference:$(Join-Path $framework $_)" }
$references += @('netstandard.dll','Sandbox.Common.dll','Sandbox.Game.dll','Sandbox.Graphics.dll','SpaceEngineers.Game.dll','VRage.dll','VRage.Game.dll','VRage.Input.dll','VRage.Library.dll','VRage.Math.dll','VRage.Render.dll') | ForEach-Object { "/reference:$(Join-Path $GameBin $_)" }
$taskHarmony = Join-Path $env:APPDATA 'Pulsar\Libraries\Legacy\0Harmony.dll'
if (-not (Test-Path -LiteralPath $taskHarmony)) { throw 'Client-loader Harmony library is required to compile camera capture isolation.' }
$references += "/reference:$taskHarmony"
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Source') -Filter '*.cs' | ForEach-Object FullName
& dotnet $compiler /nologo /target:library /nostdlib+ /langversion:7.3 /nullable:disable /deterministic+ /warnaserror+ "/out:$output" $references $sources
if ($LASTEXITCODE -ne 0) { throw 'HDR client renderer compilation failed.' }
Complete-ClientPackage (Split-Path $output -Parent) $Runtime
Write-Output "Built optional, uninstalled client IPlugin: $output"
