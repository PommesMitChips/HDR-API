[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$GameBin = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64',
    [ValidateSet('ModApi','Ingame')][string]$Target = 'ModApi',
    [string]$FrameworkDirectory = (Join-Path $env:SystemRoot 'Microsoft.NET/Framework64/v4.0.30319'),
    [string]$CompilerDll
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Inputs.ps1')
. (Join-Path $PSScriptRoot 'AssertReceipt.ps1')
$output = [IO.Path]::GetFullPath($OutputDirectory)
Assert-HtmlOutputDirectory ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))) $output
$game = [IO.Path]::GetFullPath($GameBin)
$source = [IO.Path]::GetFullPath($SourceDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
if (-not $CompilerDll) {
    $sdkVersion = ((& dotnet --version) | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to determine the selected .NET SDK.' }
    $pattern = '^' + [regex]::Escape($sdkVersion) + ' \[(.+)\]$'
    $sdkRoot = $null
    foreach ($line in (& dotnet --list-sdks)) { if ($line -match $pattern) { $sdkRoot = $Matches[1] } }
    if (-not $sdkRoot) { throw "The selected SDK $sdkVersion was not found in dotnet --list-sdks." }
    $CompilerDll = Join-Path $sdkRoot "$sdkVersion/Roslyn/bincore/csc.dll"
}
if (-not (Test-Path -LiteralPath $CompilerDll -PathType Leaf)) { throw "C# compiler not found: $CompilerDll" }
$references = @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll','System.Runtime.dll','System.Threading.Tasks.dll','System.Text.Encoding.dll','System.Collections.dll','System.Reflection.dll','System.IO.dll') | ForEach-Object { Join-Path $FrameworkDirectory $_ }
$references += @('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll','System.Collections.Immutable.dll','netstandard.dll') | ForEach-Object { Join-Path $game $_ }
foreach ($reference in $references) { if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) { throw "Whitelist host reference not found: $reference" } }
$binary = Join-Path $output 'WhitelistGate.Framework.exe'
$response = @('/nologo','/noconfig','/nostdlib+','/target:exe','/langversion:6','/optimize+',('/out:"' + $binary + '"'))
$response += $references | ForEach-Object { '/reference:"' + $_ + '"' }
$response += '"' + (Join-Path $PSScriptRoot 'Program.cs') + '"'
$responsePath = Join-Path $output 'framework-csc.rsp'
[IO.File]::WriteAllLines($responsePath, [string[]]$response, [Text.UTF8Encoding]::new($false))
$compileLog = @(& dotnet $CompilerDll ('@' + $responsePath) 2>&1 | ForEach-Object { $_.ToString() })
$compileExit = $LASTEXITCODE
[IO.File]::WriteAllLines((Join-Path $output 'compile.log'), [string[]]$compileLog)
if ($compileExit -ne 0) { $compileLog | ForEach-Object { Write-Host $_ }; throw 'Framework whitelist host compilation failed.' }
# The CLR resolves typed analyzer references before Main installs its resolver.
# Dependencies come only from the installed game and stay in isolated tool output.
foreach ($name in @('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll','System.Collections.Immutable.dll','System.Reflection.Metadata.dll','System.Memory.dll','System.Buffers.dll','System.Runtime.CompilerServices.Unsafe.dll')) {
    $path = Join-Path $game $name
    if (Test-Path -LiteralPath $path -PathType Leaf) { Copy-Item -LiteralPath $path -Destination (Join-Path $output $name) -Force }
}
$configuration = '<?xml version="1.0" encoding="utf-8"?><configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>'
[IO.File]::WriteAllText(($binary + '.config'), $configuration, [Text.UTF8Encoding]::new($false))
$reportPath = Join-Path $output 'receipt.json'
$runLog = @(& $binary $source $reportPath $game $Target 2>&1 | ForEach-Object { $_.ToString() })
$runExit = $LASTEXITCODE
[IO.File]::WriteAllLines((Join-Path $output 'run.log'), [string[]]$runLog)
if ($runExit -ne 0) { $runLog | Select-Object -Last 25 | ForEach-Object { Write-Host $_ }; throw "Actual $Target whitelist gate failed. Report: $reportPath" }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
Assert-HtmlWhitelistReceipt $report $Target
Write-Host "PASS actual $Target whitelist: $($report.CandidateSourceCount) source files; allowed, unused-reflection, and pragma controls certified."
if ($Target -eq 'Ingame') { Write-Host 'PASS PB stock blacklist, rejected ModApi/temp-file controls, actual memory-safe rewrites and successful emission.' }
[pscustomobject]@{ ReceiptPath=$reportPath; SourceCount=$report.CandidateSourceCount; Passed=$true; HostCompiler=[pscustomobject]@{File=[IO.Path]::GetFullPath($CompilerDll); SHA256=(Get-FileHash -LiteralPath $CompilerDll -Algorithm SHA256).Hash} }
