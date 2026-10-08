[CmdletBinding()]
param(
    [string]$GameBin = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts'),
    [string]$VerifiedReceipt
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$gate = Join-Path $repo 'Tools/HtmlChecks/Check.ps1'
if (-not (Test-Path -LiteralPath $gate -PathType Leaf)) {
    throw 'Build from the HDR_API source repository, which contains Tools/HtmlChecks. The ZIP is the game-loadable mod source package.'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
. (Join-Path $repo 'Tools/HtmlChecks/Inputs.ps1')
. (Join-Path $repo 'Tools/HtmlChecks/Whitelist/AssertReceipt.ps1')
Assert-HtmlOutputDirectory $repo $output
$version = Get-HtmlFrontendVersion $repo
$suites = Get-HtmlGateSuites $repo
$expectedTotal = ($suites | Measure-Object -Property Expected -Sum).Sum
$pbSources = Get-Content -LiteralPath (Join-Path $repo 'Tools/HtmlChecks/PbSources.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $output -Force | Out-Null
if ($VerifiedReceipt) {
    $receiptPath = [IO.Path]::GetFullPath($VerifiedReceipt)
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    $inputs = Get-HtmlGateInputs $repo
    if (-not $receipt.FullSuite -or -not $receipt.SourceStableDuringGate -or $receipt.ProductionLanguage -ne 'C#6' -or $receipt.FrontendVersion -ne $version -or $receipt.Assertions -ne $expectedTotal -or @($receipt.Suites).Count -ne $suites.Count -or @($receipt.Whitelist).Count -ne 4 -or @($receipt.Suites | Where-Object { -not $_.Passed }).Count -ne 0 -or ($receipt.Sources | ConvertTo-Json -Depth 8 -Compress) -ne ($inputs | ConvertTo-Json -Depth 8 -Compress)) { throw 'Verified receipt does not certify all current source, documentation and tools. Run the default build gate.' }
    foreach ($suite in $suites) {
        $result = @($receipt.Suites | Where-Object { $_.Suite -eq $suite.Name -and $_.Assertions -eq $suite.Expected -and $_.Passed })
        if ($result.Count -ne 1) { throw "Verified receipt does not certify the frozen $($suite.Name) suite." }
    }
    if ([IO.Path]::GetFullPath($receipt.GameBin) -ne [IO.Path]::GetFullPath($GameBin)) { throw 'Verified receipt was produced with a different GameBin. Run the default build gate.' }
    if (-not $receipt.HostCompiler -or @($receipt.BuildReferences).Count -lt 13) { throw 'Verified receipt has no complete compiler/reference fingerprint.' }
    foreach ($reference in @($receipt.BuildReferences) + @($receipt.HostCompiler)) {
        if ((Get-FileHash -LiteralPath $reference.File -Algorithm SHA256).Hash -ne $reference.SHA256) { throw 'An installed build/compiler reference changed; run the default build gate.' }
    }
    foreach ($group in $receipt.Whitelist) {
        $whitelist = Get-Content -LiteralPath $group.Receipt -Raw | ConvertFrom-Json
        $target = if ($group.Group -eq 'PbIngame') { 'Ingame' } else { 'ModApi' }
        Assert-HtmlWhitelistReceipt $whitelist $target
        if ($target -eq 'Ingame') {
            $expectedWrappers = @('HelperProgram.cs') + @($pbSources.Demos | ForEach-Object { $_.Wrapper })
            $actualWrappers = @($whitelist.SourceFiles | ForEach-Object { [IO.Path]::GetFileName($_.File) })
            if ($whitelist.CandidateSourceCount -ne $expectedWrappers.Count -or (($expectedWrappers | Sort-Object) -join ',') -ne (($actualWrappers | Sort-Object) -join ',')) { throw 'Verified Ingame receipt does not cover the canonical helper and every reviewed PB demo wrapper.' }
        }
        $whitelistReferences = @($whitelist.References) + @($whitelist.AnalyzerAssembly,$whitelist.RegistrationAssembly)
        if ($target -eq 'Ingame') { $whitelistReferences += $whitelist.BlacklistEvidenceAssembly }
        else { $whitelistReferences += $whitelist.ModCompatibilityEvidenceAssembly }
        foreach ($reference in $whitelistReferences) {
            if ((Get-FileHash -LiteralPath $reference.File -Algorithm SHA256).Hash -ne $reference.SHA256) { throw 'An installed whitelist/compiler reference changed; run the default build gate.' }
        }
    }
    if (@($receipt.Whitelist | Where-Object { $_.Group -eq 'PbIngame' }).Count -ne 1 -or (($receipt.Whitelist | ForEach-Object { $_.Group } | Sort-Object) -join ',') -ne 'Consumer,Core,Frontend,PbIngame') { throw 'Verified receipt does not certify all four distinct whitelist groups.' }
    $checkResult = [pscustomobject]@{Assertions=$receipt.Assertions; ReceiptPath=$receiptPath; OutputDirectory=[IO.Path]::GetDirectoryName($receiptPath)}
    Write-Host 'PASS existing complete gate receipt: all current source/docs/tools and inspected game references match.'
}
else { $checkResult = & $gate -GameBin $GameBin -RepositoryRoot $repo -OutputDirectory (Join-Path $output "html-prototype/checks-$version") }
$sealedReceipt = Get-Content -LiteralPath $checkResult.ReceiptPath -Raw | ConvertFrom-Json
$destination = Join-Path $output "HDR-HTML-Frontend-$version.zip"
# Explicit allowlist: compiled test binaries, imported game DLLs, receipts and the
# repository-only build script never become mod content.
$entries = @('Data','Docs','Examples','README.md','metadata.mod') | ForEach-Object {
    $path = Join-Path $PSScriptRoot $_
    if (Test-Path -LiteralPath $path -PathType Container) { Get-ChildItem -LiteralPath $path -Recurse -File }
    elseif (Test-Path -LiteralPath $path -PathType Leaf) { Get-Item -LiteralPath $path }
}
$entries = @($entries | Sort-Object FullName)
# Sorted paths and a fixed ZIP timestamp make identical sources produce identical
# packages, independent of checkout timestamps. All paths are mod-root relative.
Add-Type -AssemblyName System.IO.Compression
$pendingPackage = $destination + '.pending'
$zipStream = [IO.File]::Open($pendingPackage, [IO.FileMode]::Create, [IO.FileAccess]::Write)
try {
    $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $entries) {
            $name = $file.FullName.Substring($PSScriptRoot.Length + 1).Replace('\','/')
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero)
            $inputStream = [IO.File]::OpenRead($file.FullName)
            try { $entryStream = $entry.Open(); try { $inputStream.CopyTo($entryStream) } finally { $entryStream.Dispose() } }
            finally { $inputStream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
finally { $zipStream.Dispose() }
$afterInputs = Get-HtmlGateInputs $repo
if (($sealedReceipt.Sources | ConvertTo-Json -Depth 8 -Compress) -ne ($afterInputs | ConvertTo-Json -Depth 8 -Compress)) { throw 'Source/docs/tools changed while packaging. The ZIP is not certified; freeze inputs and rebuild.' }
Move-Item -LiteralPath $pendingPackage -Destination $destination -Force
$packageHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
[ordered]@{
    Package=$destination; SHA256=$packageHash; Version=$version; Stage='ALPHA';
    GateReceipt=$checkResult.ReceiptPath; Assertions=$checkResult.Assertions;
    Files=@($entries | ForEach-Object { [ordered]@{Path=$_.FullName.Substring($PSScriptRoot.Length + 1).Replace('\','/'); SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash} });
    Installed=$false; ProfilesChanged=$false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $checkResult.OutputDirectory 'package-receipt.json') -Encoding utf8
Write-Host "Created game-loadable ALPHA mod source package: $destination"
Write-Host "SHA256: $packageHash"
