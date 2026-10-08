[CmdletBinding()]
param(
    [string]$GameBin = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Bin64',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/js-prototype'),
    [string]$VerifiedReceipt
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$package = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\','/')
$output = [IO.Path]::GetFullPath($OutputDirectory)
$gate = Join-Path $repository 'Tools/JavaScriptChecks/Check.ps1'
if (-not (Test-Path -LiteralPath $gate -PathType Leaf)) {
    throw 'Build from the HDR_API source repository with Tools/JavaScriptChecks. The ZIP is the game-loadable source mod.'
}
. (Join-Path $repository 'Tools/JavaScriptChecks/Inputs.ps1')
Assert-JavaScriptOutputDirectory $repository $output
$version = ([xml](Get-Content -LiteralPath (Join-Path $package 'metadata.mod') -Raw)).ModMetadata.ModVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid JavaScript runtime package version.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
if ($VerifiedReceipt) {
    $receiptPath = [IO.Path]::GetFullPath($VerifiedReceipt)
}
else {
    $result = & $gate -PackageRoot $package -OutputDirectory (Join-Path $output "checks-$version") -GameBin $GameBin
    $receiptPath = $result.ReceiptPath
    if (-not $receiptPath) { throw 'The complete JavaScript gate returned no receipt path.' }
}
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
Assert-JavaScriptGateReceipt $receipt $repository $package $GameBin
$sealedInputs = Get-JavaScriptGateInputs $repository $package
Write-Host 'PASS complete JavaScript receipt: actual ModApi, runtime, frontend integration and all current sealed inputs.'

# Explicit game-content allowlist. No compiler binaries, tests, original upstream
# archives, private receipts or repository-only build tools enter the mod ZIP.
$entries = @('Data','Docs','Examples','LICENSES','ORIGIN','README.md','ORIGIN.md','ORIGIN.json','metadata.mod') | ForEach-Object {
    $path = Join-Path $package $_
    if (Test-Path -LiteralPath $path -PathType Container) { Get-ChildItem -LiteralPath $path -Recurse -File }
    elseif (Test-Path -LiteralPath $path -PathType Leaf) { Get-Item -LiteralPath $path }
    else { throw "Required package content is missing: $_" }
}
$entries = @($entries | Sort-Object FullName)
$destination = Join-Path $output "HDR-JavaScript-Runtime-$version.zip"
$pending = $destination + '.pending'
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($pending,[IO.FileMode]::Create,[IO.FileAccess]::Write)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try {
        foreach ($file in $entries) {
            $name = $file.FullName.Substring($package.Length + 1).Replace('\','/')
            $entry = $archive.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero)
            $sourceStream = [IO.File]::OpenRead($file.FullName)
            try { $entryStream = $entry.Open(); try { $sourceStream.CopyTo($entryStream) } finally { $entryStream.Dispose() } }
            finally { $sourceStream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
finally { $stream.Dispose() }

# Inspect the actual ZIP, including Jint's BSD 2-Clause conditions/disclaimer and
# the additional notices carried by the mixed-license upstream source tree.
$readStream = [IO.File]::OpenRead($pending)
try {
    $archive = [IO.Compression.ZipArchive]::new($readStream,[IO.Compression.ZipArchiveMode]::Read,$true)
    try {
        if ($archive.Entries.Count -ne $entries.Count) { throw 'Package file count changed.' }
        foreach ($proof in @($receipt.Licenses)) {
            $entry = $archive.GetEntry([string]$proof.File)
            if (-not $entry) { throw "Required license/notice is absent from ZIP: $($proof.File)" }
            $reader = [IO.StreamReader]::new($entry.Open())
            try { $licenseText = $reader.ReadToEnd() } finally { $reader.Dispose() }
            foreach ($fragment in @($proof.RequiredFragments)) {
                if (-not $licenseText.Contains([string]$fragment)) { throw "Required license text is absent from ZIP: $($proof.File)" }
            }
        }
        foreach ($file in $entries) {
            $name = $file.FullName.Substring($package.Length + 1).Replace('\','/')
            $entry = $archive.GetEntry($name)
            if (-not $entry) { throw "Package entry is missing: $name" }
            $entryStream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($entryStream)).Replace('-','') }
            finally { $sha.Dispose(); $entryStream.Dispose() }
            if ($hash -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "Package entry differs from certified source: $name" }
        }
    }
    finally { $archive.Dispose() }
}
finally { $readStream.Dispose() }
Assert-JavaScriptInputsStable $sealedInputs (Get-JavaScriptGateInputs $repository $package)
Assert-JavaScriptGateReceipt $receipt $repository $package $GameBin
Move-Item -LiteralPath $pending -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
[ordered]@{
    Package=$destination; SHA256=$hash; Version=$version; Stage='ALPHA';
    GateReceipt=$receiptPath; GateReceiptSHA256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash;
    Files=@($entries | ForEach-Object { [ordered]@{Path=$_.FullName.Substring($package.Length + 1).Replace('\','/'); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} });
    LicenseNoticesVerifiedInsideZip=$true; Installed=$false; ProfilesChanged=$false
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output "package-$version.json") -Encoding utf8
Write-Host "Created game-loadable ALPHA source mod: $destination"
Write-Host "SHA256: $hash"
