[CmdletBinding()]
param(
    [string]$GameBin = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Inputs.ps1')
$repo = [IO.Path]::GetFullPath($RepositoryRoot)
$game = [IO.Path]::GetFullPath($GameBin)
$suites = Get-HtmlGateSuites $repo
$version = Get-HtmlFrontendVersion $repo
$beforeInputs = Get-HtmlGateInputs $repo
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo "artifacts/html-prototype/checks-$version" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
Assert-HtmlOutputDirectory $repo $output
New-Item -ItemType Directory -Path $output -Force | Out-Null
$logs = Join-Path $output 'logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null

# No downloaded references, game launch, mod installation, or game/profile writes.
[xml]$references = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'GameReferences.props') -Raw
foreach ($reference in $references.Project.ItemGroup.Reference) {
    $assembly = Join-Path $game ($reference.Include + '.dll')
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw "Required game assembly not found: $assembly" }
}
$referenceFiles = @($references.Project.ItemGroup.Reference | ForEach-Object { Join-Path $game ($_.Include + '.dll') })
$referenceFiles += @('System.Collections.Immutable.dll','System.Reflection.Metadata.dll','System.Memory.dll','System.Buffers.dll','System.Runtime.CompilerServices.Unsafe.dll') | ForEach-Object { Join-Path $game $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
$buildReferences = @($referenceFiles | Sort-Object -Unique | ForEach-Object { [pscustomobject]@{File=$_; SHA256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash} })
foreach ($fixture in (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Fixtures.json') -Raw | ConvertFrom-Json)) {
    $path = Join-Path $PSScriptRoot $fixture.Fixture
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $fixture.SHA256) {
        throw "Accepted fixture changed: $($fixture.Fixture). Review the change and update Fixtures.json intentionally."
    }
}
$canonicalSdk = Join-Path $repo 'Api/Mods/HdrModApi.cs'
$packagedSdk = Join-Path $repo 'OptionalMods/HDRHtmlFrontend/Data/Scripts/HdrHtml/HdrModApi.cs'
if ((Get-FileHash -LiteralPath $canonicalSdk).Hash -ne (Get-FileHash -LiteralPath $packagedSdk).Hash) {
    throw 'The frontend packaged HDR SDK does not match Api/Mods/HdrModApi.cs.'
}

# The PB API is an import-free paste fragment. Compile exactly the canonical
# fragment with standard PB imports, rather than storing another SDK snapshot.
$generated = Join-Path $output 'generated'
New-Item -ItemType Directory -Path $generated -Force | Out-Null
$pb = Join-Path $generated 'CanonicalPbHelper.cs'
$imports = 'using System;using System.Collections.Generic;using Sandbox.ModAPI.Ingame;using Sandbox.ModAPI.Interfaces;using VRageMath;'
[IO.File]::WriteAllText($pb, $imports + [Environment]::NewLine + [IO.File]::ReadAllText((Join-Path $repo 'Api/Ingame/HdrIngameApi.cs')))
$pbSources = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'PbSources.json') -Raw | ConvertFrom-Json
$htmlPbSource = Join-Path $repo $pbSources.Helper
$htmlPb = Join-Path $generated 'CanonicalHtmlPbHelper.cs'
[IO.File]::WriteAllText($htmlPb, $imports + [Environment]::NewLine + [IO.File]::ReadAllText($htmlPbSource))
$runId = [Guid]::NewGuid().ToString('N')
$pbCandidates = Join-Path $generated ('whitelist-pb-' + $runId)
New-Item -ItemType Directory -Path $pbCandidates -Force | Out-Null
$pbHeader = 'using System;using System.Collections.Generic;using System.Linq;using System.Text;using Sandbox.ModAPI.Ingame;using Sandbox.ModAPI.Interfaces;using SpaceEngineers.Game.ModAPI.Ingame;using VRage.Game;using VRage.Game.ModAPI.Ingame;using VRage.Game.ModAPI.Ingame.Utilities;using VRageMath;public class Program:MyGridProgram{'
[IO.File]::WriteAllText((Join-Path $pbCandidates 'HelperProgram.cs'), $pbHeader + [Environment]::NewLine + 'public void Main(string argument,UpdateType updateSource){}' + [Environment]::NewLine + [IO.File]::ReadAllText($htmlPbSource) + [Environment]::NewLine + '}')
foreach ($demo in $pbSources.Demos) {
    if ([IO.Path]::GetFileName($demo.Wrapper) -ne $demo.Wrapper -or [IO.Path]::GetFileName($demo.PasteArtifact) -ne $demo.PasteArtifact) { throw 'Reviewed PB wrapper and paste artifact names must be filenames, not paths.' }
    $demoSource = Join-Path $repo $demo.Source
    [IO.File]::WriteAllText((Join-Path $pbCandidates $demo.Wrapper), $pbHeader + [Environment]::NewLine + [IO.File]::ReadAllText($demoSource) + [Environment]::NewLine + '}')
    # Published paste bodies are exactly the raw demos; neither needs an appended SDK.
    Copy-Item -LiteralPath $demoSource -Destination (Join-Path $output $demo.PasteArtifact) -Force
}
$properties = @("-p:GameBin=$game", "-p:RepositoryRoot=$repo", "-p:HtmlChecksOutputRoot=$output", "-p:GeneratedPbHelper=$pb", "-p:GeneratedHtmlPbHelper=$htmlPb")

function Invoke-LoggedDotnet([string]$Name, [string[]]$Arguments) {
    $captured = @(& dotnet @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exit = $LASTEXITCODE
    $log = Join-Path $logs ($Name + '.log')
    [IO.File]::WriteAllLines($log, [string[]]$captured)
    if ($exit -ne 0) {
        $captured | Select-Object -Last 30 | ForEach-Object { Write-Host $_ }
        throw "$Name failed (exit $exit). Full log: $log"
    }
    return $captured
}
function Build-Project([string]$RelativeProject) {
    $project = Join-Path $PSScriptRoot $RelativeProject
    $name = [IO.Path]::GetFileNameWithoutExtension($project)
    $arguments = @('build', $project, '--configuration', 'Release', '--nologo', '--verbosity', 'quiet') + $properties
    $null = Invoke-LoggedDotnet ($name + '-build') $arguments
    return Join-Path $output "build/$name/bin/Release/net10.0/$name.dll"
}

$oldBin = $env:SE_BIN
try {
    $env:SE_BIN = $game
    $null = Build-Project 'Frontend/FrontendCompile.csproj'
    $null = Build-Project 'Retained/CoreCompile.csproj'
    $null = Build-Project 'CoreSeams/CoreSdkCompile.csproj'
    $null = Build-Project 'Frontend/PbHelperCompile.csproj'
    Write-Host 'PASS C#6: complete frontend, packaged SDK, consumer helper/example, actual core and canonical PB/mod helpers.'
    $whitelistGate = Join-Path $PSScriptRoot 'Whitelist/Check.ps1'
    $whitelistFrontend = & $whitelistGate -SourceDirectory (Join-Path $repo 'OptionalMods/HDRHtmlFrontend/Data/Scripts/HdrHtml') -GameBin $game -OutputDirectory (Join-Path $output 'whitelist/frontend')
    $whitelistCore = & $whitelistGate -SourceDirectory (Join-Path $repo 'Mod/Data/Scripts/HoloMap') -GameBin $game -OutputDirectory (Join-Path $output 'whitelist/core')
    $consumer = Join-Path $generated ('whitelist-consumer-' + $runId)
    New-Item -ItemType Directory -Path $consumer -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'Api/Mods/HdrHtmlApi.cs') -Destination (Join-Path $consumer 'HdrHtmlApi.cs') -Force
    Copy-Item -LiteralPath (Join-Path $repo 'OptionalMods/HDRHtmlFrontend/Examples/HtmlConsumerExample.cs') -Destination (Join-Path $consumer 'HtmlConsumerExample.cs') -Force
    $whitelistConsumer = & $whitelistGate -SourceDirectory $consumer -GameBin $game -OutputDirectory (Join-Path $output 'whitelist/consumer')
    $whitelistPb = & $whitelistGate -SourceDirectory $pbCandidates -Target Ingame -GameBin $game -OutputDirectory (Join-Path $output 'whitelist/pb-ingame')
    if ($whitelistPb.SourceCount -ne (1 + @($pbSources.Demos).Count)) { throw 'PB Ingame proof must include the complete helper and every reviewed demo wrapper.' }
    $whitelistResults = @(
        [pscustomobject]@{Group='Frontend'; Receipt=$whitelistFrontend.ReceiptPath; Sources=(Get-Content -LiteralPath $whitelistFrontend.ReceiptPath -Raw | ConvertFrom-Json).SourceFiles},
        [pscustomobject]@{Group='Core'; Receipt=$whitelistCore.ReceiptPath; Sources=(Get-Content -LiteralPath $whitelistCore.ReceiptPath -Raw | ConvertFrom-Json).SourceFiles},
        [pscustomobject]@{Group='Consumer'; Receipt=$whitelistConsumer.ReceiptPath; Sources=@($beforeInputs | Where-Object { $_.Path -eq 'Api/Mods/HdrHtmlApi.cs' -or $_.Path -eq 'OptionalMods/HDRHtmlFrontend/Examples/HtmlConsumerExample.cs' })},
        [pscustomobject]@{Group='PbIngame'; Receipt=$whitelistPb.ReceiptPath; Sources=@($beforeInputs | Where-Object { $_.Path -eq $pbSources.Helper -or $_.Path -in @($pbSources.Demos | ForEach-Object { $_.Source }) }); GeneratedWrappers=(Get-Content -LiteralPath $whitelistPb.ReceiptPath -Raw | ConvertFrom-Json).SourceFiles}
    )
    $results = @()
    foreach ($suite in $suites) {
        $binary = Build-Project $suite.Project
        $arguments = @($binary)
        if ($suite.EvidenceDirectory) { $arguments += (Join-Path $output $suite.EvidenceDirectory) }
        $lines = Invoke-LoggedDotnet ($suite.Name + '-run') $arguments
        $summary = $lines | Where-Object { $_ -match $suite.Pattern } | Select-Object -Last 1
        if (-not $summary -or $summary -notmatch $suite.Pattern) { throw "Missing assertion receipt for $($suite.Name)." }
        $count = [int]$Matches[1]
        if ($count -ne $suite.Expected) { throw "$($suite.Name) ran $count assertions; frozen fixture expects $($suite.Expected)." }
        $results += [pscustomobject]@{ Suite=$suite.Name; Assertions=$count; Passed=$true; Log=('logs/' + $suite.Name + '-run.log') }
        Write-Host "PASS $($suite.Name): $count assertions."
    }
    $sourceHashes = Get-HtmlGateInputs $repo
    if (($beforeInputs | ConvertTo-Json -Depth 8 -Compress) -ne ($sourceHashes | ConvertTo-Json -Depth 8 -Compress)) { throw 'Gate inputs changed while checks were running. Freeze source/docs/tools and rerun.' }
    $receipt = [ordered]@{
        FrontendVersion=$version; Stage='ALPHA'; Profile='HDR.HTML/Profile1';
        Utc=[DateTime]::UtcNow.ToString('o'); GameBin=$game;
        DotnetSdk=((& dotnet --version) | Out-String).Trim();
        ProductionLanguage='C#6'; FullSuite=$true; SourceStableDuringGate=$true; Whitelist=$whitelistResults; Suites=$results;
        BuildReferences=$buildReferences; HostCompiler=$whitelistFrontend.HostCompiler;
        Assertions=($results | Measure-Object -Property Assertions -Sum).Sum;
        LiveGameValidation=$false; GpuPerformanceMeasured=$false;
        Sources=$sourceHashes
    }
    $receiptPath = Join-Path $output 'receipt.json'
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding utf8
    Write-Host "HTML gate passed: $($receipt.Assertions) assertions. Receipt: $receiptPath"
    [pscustomobject]@{ Assertions=$receipt.Assertions; ReceiptPath=$receiptPath; OutputDirectory=$output }
}
finally { $env:SE_BIN = $oldBin }
