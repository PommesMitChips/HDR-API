[CmdletBinding()]
param(
    [string]$PackageRoot = (Join-Path $PSScriptRoot '../../OptionalMods/HDRJavaScriptRuntime'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/js-prototype/checks'),
    [string]$GameBin = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64',
    [string]$LicenseInventory,
    [switch]$WhitelistOnly,
    [switch]$FocusedCheckpoint
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd('\','/')
. (Join-Path $PSScriptRoot 'Inputs.ps1')
$package = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\','/')
$output = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\','/')
Assert-JavaScriptOutputDirectory $repository $output
$inputsBefore = Get-JavaScriptGateInputs $repository $package
if (-not $FocusedCheckpoint -and -not $WhitelistOnly) { $fixtureDefinition = Get-JavaScriptFixtureDefinition $repository }
$source = Join-Path $package 'Data/Scripts'
if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw "Candidate source root is missing: $source" }
if (-not $LicenseInventory) { $LicenseInventory = Join-Path $package 'LICENSES/Licenses.json' }
if (-not (Test-Path -LiteralPath $LicenseInventory -PathType Leaf)) { throw 'The package must provide a shipped third-party license inventory.' }
$licenses = @(Get-Content -LiteralPath $LicenseInventory -Raw | ConvertFrom-Json)
$licenseInventoryHash = (Get-FileHash -LiteralPath $LicenseInventory -Algorithm SHA256).Hash
if ($licenses.Count -lt 1 -or @($licenses | Where-Object { $_.Spdx -eq 'BSD-2-Clause' }).Count -lt 1) { throw 'The package inventory must include Jint BSD-2-Clause.' }
$licenseProof = @()
foreach ($entry in $licenses) {
    $path = [IO.Path]::GetFullPath((Join-Path $package ([string]$entry.File)))
    if (-not $path.StartsWith(($package + [IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)) { throw 'A license path escapes the package root.' }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "A shipped license is missing: $($entry.File)" }
    $license = [IO.File]::ReadAllText($path)
    if (-not $entry.Spdx -or @($entry.MustContain).Count -lt 3) { throw 'Every license entry must identify its license and retain copyright, conditions and disclaimer proof.' }
    foreach ($fragment in @($entry.MustContain)) { if (-not $license.Contains([string]$fragment)) { throw "License text lacks required original fragment: $($entry.File)" } }
    $licenseProof += [pscustomobject]@{ File=$entry.File; Spdx=$entry.Spdx; SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; RequiredFragments=@($entry.MustContain) }
}
function Get-JavaScriptSources([string]$Root) {
    @(Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.cs' -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ File=$_.FullName.Substring($Root.Length + 1).Replace('\','/'); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
$before = Get-JavaScriptSources $source
if ($before.Count -lt 1) { throw 'Candidate source inventory is empty.' }
$sdkCopies = @($before | Where-Object { [IO.Path]::GetFileName($_.File) -eq 'HdrHtmlApi.cs' })
if ($sdkCopies.Count -ne 1 -or $sdkCopies[0].SHA256 -ne (Get-FileHash -LiteralPath (Join-Path $repository 'Api/Mods/HdrHtmlApi.cs') -Algorithm SHA256).Hash) { throw 'The shipped HTML SDK consumer copy must exactly match its canonical public source.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$stage = Join-Path $output ('candidate-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($item in $before) {
    $destination = Join-Path $stage $item.File
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $source $item.File) -Destination $destination
}
[IO.File]::WriteAllText((Join-Path $output 'source-inputs.json'), ($before | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $output 'license-inputs.json'), ($licenseProof | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
$whitelist = & (Join-Path $repository 'Tools/HtmlChecks/Whitelist/Check.ps1') -SourceDirectory $stage -OutputDirectory (Join-Path $output 'actual-modapi') -GameBin $GameBin -Target ModApi
if (-not $whitelist.Passed) { throw 'Actual ModApi gate returned no certified pass.' }
$runtimePassed = $false
$frontendIntegrationPassed = $false
$fixtureRuns = @()
$frameworkNumericPassed = $false
function Invoke-JavaScriptFixture([string]$Name, [string]$Project, [string]$ReceiptName) {
    $buildLog = @(& dotnet build $Project -c Release --nologo -v:q ('-p:JavaScriptSourceRoot=' + $stage) ('-p:JavaScriptChecksOutputRoot=' + $output) ('-p:GameBin=' + $GameBin) 2>&1 | ForEach-Object { $_.ToString() })
    $buildExit = $LASTEXITCODE
    [IO.File]::WriteAllLines((Join-Path $output ($Name + '-build.log')), [string[]]$buildLog)
    if ($buildExit -ne 0) { $buildLog | Select-Object -Last 30 | Write-Host; throw "$Name CPU fixtures did not compile." }
    $projectName = [IO.Path]::GetFileNameWithoutExtension($Project)
    $binary = Join-Path $output ('build/' + $projectName + '/bin/Release/net10.0/' + $projectName + '.dll')
    $resultPath = Join-Path $output $ReceiptName
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command dotnet).Source
    $start.ArgumentList.Add($binary); $start.ArgumentList.Add($resultPath)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    $finished = $process.WaitForExit(60000)
    if (-not $finished) { $process.Kill($true); $process.WaitForExit() }
    [IO.File]::WriteAllText((Join-Path $output ($Name + '-run.log')), ($stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()), [Text.UTF8Encoding]::new($false))
    if (-not $finished) { throw "$Name fixtures exceeded the external 60-second backstop." }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    [pscustomobject]@{ Name=$Name; Passed=($process.ExitCode -eq 0 -and $result.Passed); Assertions=$result.Assertions; Cases=$result.Cases; Failures=$result.Failures; ReceiptPath=$resultPath; SHA256=(Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash; GameContextInitialized=$result.GameContextInitialized; PluginRequired=$result.PluginRequired }
}
if (-not $WhitelistOnly) {
    $runtime = Invoke-JavaScriptFixture 'Runtime' (Join-Path $PSScriptRoot 'Runtime/JavaScriptRuntimeTests.csproj') 'runtime-receipt.json'
    $fixtureRuns += $runtime; $runtimePassed = $runtime.Passed
    $seams = Invoke-JavaScriptFixture 'HtmlSeams' (Join-Path $PSScriptRoot 'HtmlSeams/JavaScriptHtmlSeamTests.csproj') 'html-seams-receipt.json'
    $fixtureRuns += $seams; $frontendIntegrationPassed = $seams.Passed
    $frameworkNumeric = & (Join-Path $PSScriptRoot 'Framework/Check.ps1') -SourceDirectory $stage -OutputDirectory (Join-Path $output 'framework-numeric') -CompilerDll $whitelist.HostCompiler.File
    $fixtureRuns += $frameworkNumeric
    $frameworkNumericPassed = $frameworkNumeric.Passed
    if (-not $runtimePassed -or -not $frontendIntegrationPassed -or -not $frameworkNumeric.Passed) { throw 'CPU interpreter, Framework numeric or actual HTML integration fixtures failed. See fixture run logs.' }
}
$after = Get-JavaScriptSources $source
if (($before | ConvertTo-Json -Depth 10 -Compress) -ne ($after | ConvertTo-Json -Depth 10 -Compress)) { throw 'Candidate source changed during the gate; freeze and rerun.' }
$inputsAfter = Get-JavaScriptGateInputs $repository $package
$allInputsStable = ($inputsBefore | ConvertTo-Json -Depth 5 -Compress) -eq ($inputsAfter | ConvertTo-Json -Depth 5 -Compress)
if (-not $FocusedCheckpoint) { Assert-JavaScriptInputsStable $inputsBefore $inputsAfter }
if ($licenseInventoryHash -ne (Get-FileHash -LiteralPath $LicenseInventory -Algorithm SHA256).Hash) { throw 'License inventory changed during the gate.' }
foreach ($proof in $licenseProof) { if ($proof.SHA256 -ne (Get-FileHash -LiteralPath (Join-Path $package $proof.File) -Algorithm SHA256).Hash) { throw 'A shipped license changed during the gate.' } }
$gateTools = @('Tools/JavaScriptChecks/Check.ps1','Tools/JavaScriptChecks/Directory.Build.props','Tools/HtmlChecks/Whitelist/Check.ps1','Tools/HtmlChecks/Whitelist/Program.cs','Tools/HtmlChecks/Whitelist/AssertReceipt.ps1') | ForEach-Object { [pscustomobject]@{File=$_; SHA256=(Get-FileHash -LiteralPath (Join-Path $repository $_) -Algorithm SHA256).Hash} }
[xml]$metadata = Get-Content -LiteralPath (Join-Path $package 'metadata.mod') -Raw
$version = [string]$metadata.ModMetadata.ModVersion
$fixturePath = Join-Path $PSScriptRoot 'Fixtures.json'
$fixtureProof = if (Test-Path -LiteralPath $fixturePath -PathType Leaf) { [pscustomobject]@{File=[IO.Path]::GetFullPath($fixturePath); SHA256=(Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash} } else { $null }
$receiptPath = Join-Path $output 'receipt.json'
$receipt = [pscustomobject]@{ Schema='HDR.JavaScript/Gate1'; Success=($runtimePassed -and $frontendIntegrationPassed -and $frameworkNumericPassed -and $allInputsStable -and -not $WhitelistOnly); ReceiptPath=$receiptPath; Version=$version; PackageRoot=$package; GameBin=[IO.Path]::GetFullPath($GameBin); ActualModApiPassed=$true; RuntimePassed=$runtimePassed; FrontendIntegrationPassed=$frontendIntegrationPassed; FrameworkNumericPassed=$frameworkNumericPassed; FocusedCheckpoint=[bool]$FocusedCheckpoint; FixtureRuns=$fixtureRuns; FixtureDefinition=$fixtureProof; Compiler=$whitelist.HostCompiler; WhitelistOnly=[bool]$WhitelistOnly; SourceStableDuringGate=$true; AllInputsStableDuringGate=$allInputsStable; SourceCount=$before.Count; Sources=$before; Inputs=$inputsBefore; LicenseInventorySHA256=$licenseInventoryHash; Licenses=$licenseProof; GateTools=$gateTools; WhitelistReceipt=$whitelist.ReceiptPath; WhitelistReceiptSHA256=(Get-FileHash -LiteralPath $whitelist.ReceiptPath -Algorithm SHA256).Hash; CandidateStage=$stage; GameContextInitialized=$false; PluginRequired=$false }
if (-not $FocusedCheckpoint -and -not $WhitelistOnly) { Assert-JavaScriptGateReceipt $receipt $repository $package $GameBin }
[IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
Write-Host "JavaScript gate: $($before.Count) actual ModApi sources; $($runtime.Assertions) runtime, $($seams.Assertions) actual HTML and $($frameworkNumeric.Assertions) CLR4 numeric assertions."
[pscustomobject]@{ReceiptPath=$receiptPath;Version=$version;Passed=$receipt.Success;FocusedCheckpoint=[bool]$FocusedCheckpoint;SourceCount=$before.Count;FixtureRuns=$fixtureRuns}
