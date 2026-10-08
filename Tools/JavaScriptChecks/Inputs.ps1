function Assert-JavaScriptOutputDirectory([string]$RepositoryRoot, [string]$OutputDirectory) {
    $allowed = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'artifacts/js-prototype')).TrimEnd('\','/')
    $target = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\','/')
    if (-not $target.Equals($allowed,[StringComparison]::OrdinalIgnoreCase) -and -not $target.StartsWith(($allowed + [IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)) {
        throw 'JavaScript build/check outputs must stay inside artifacts/js-prototype; game, installation, and Pulsar paths are never output targets.'
    }
}

function Get-JavaScriptGateInputs([string]$RepositoryRoot, [string]$PackageRoot) {
    $repository = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\','/')
    $package = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\','/')
    if (-not $package.StartsWith(($repository + [IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)) { throw 'The JavaScript package root must be inside its repository.' }
    $packageFiles = @(Get-ChildItem -LiteralPath $package -Recurse -File)
    foreach ($file in $packageFiles) {
        if ($file.Extension -in @('.dll','.exe','.pdb','.zip','.nupkg') -or $file.FullName.Substring($package.Length + 1) -match '(^|[\\/])(\.git|bin|obj)([\\/]|$)') {
            throw 'JavaScript source packages must not contain compiled binaries, archives, Git internals or build output.'
        }
    }
    $files = @(
        $packageFiles
        Get-ChildItem -LiteralPath (Join-Path $repository 'Tools/JavaScriptChecks') -Recurse -File
        Get-ChildItem -LiteralPath (Join-Path $repository 'OptionalMods/HDRHtmlFrontend/Data/Scripts') -Recurse -Filter '*.cs' -File
        Get-ChildItem -LiteralPath (Join-Path $repository 'Tools/HtmlChecks/Host') -Filter 'Host*.cs' -File
        Get-Item -LiteralPath (Join-Path $repository 'Directory.Build.props'), (Join-Path $repository 'Api/Mods/HdrHtmlApi.cs'), (Join-Path $repository 'Api/Mods/HdrJavaScriptApi.cs'), (Join-Path $repository 'OptionalMods/HDRHtmlFrontend/metadata.mod'), (Join-Path $repository 'Tools/HtmlChecks/GameReferences.props'), (Join-Path $repository 'Tools/HtmlChecks/Inputs.ps1'), (Join-Path $repository 'Tools/HtmlChecks/Whitelist/Check.ps1'), (Join-Path $repository 'Tools/HtmlChecks/Whitelist/Program.cs'), (Join-Path $repository 'Tools/HtmlChecks/Whitelist/AssertReceipt.ps1')
    )
    @($files | Sort-Object FullName -Unique | ForEach-Object {
        [pscustomobject]@{ Path=$_.FullName.Substring($repository.Length + 1).Replace('\','/'); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}

function Assert-JavaScriptInputsStable($Before, $After) {
    if (($Before | ConvertTo-Json -Depth 5 -Compress) -ne ($After | ConvertTo-Json -Depth 5 -Compress)) { throw 'A sealed JavaScript package, fixture, tool, SDK, or frontend integration input changed during the gate; freeze and rerun.' }
}

function Get-JavaScriptFixtureDefinition([string]$RepositoryRoot) {
    $path = Join-Path $RepositoryRoot 'Tools/JavaScriptChecks/Fixtures.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'JavaScript fixture counts and hashes have not been frozen.' }
    $definition = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $names = @($definition.Suites | ForEach-Object Name | Sort-Object)
    if ($definition.Version -ne 1 -or ($names -join ',') -ne 'FrameworkNumeric,HtmlSeams,Runtime' -or @($definition.Suites).Count -ne 3) { throw 'JavaScript fixture definition must contain all three reviewed suites.' }
    foreach ($suite in $definition.Suites) { if ($suite.Assertions -lt 1 -or $suite.Cases -lt 1) { throw 'JavaScript fixture counts must be positive and exact.' } }
    $actual = @(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'Tools/JavaScriptChecks/Runtime'), (Join-Path $RepositoryRoot 'Tools/JavaScriptChecks/HtmlSeams'), (Join-Path $RepositoryRoot 'Tools/JavaScriptChecks/Framework') -Filter '*.cs' -File | Sort-Object FullName | ForEach-Object { [pscustomobject]@{ Path=$_.FullName.Substring($RepositoryRoot.TrimEnd('\','/').Length + 1).Replace('\','/'); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    if (($actual | ConvertTo-Json -Depth 5 -Compress) -ne (@($definition.FixtureFiles) | ConvertTo-Json -Depth 5 -Compress)) { throw 'JavaScript fixture source differs from its reviewed frozen hashes.' }
    return $definition
}

function Assert-JavaScriptFileProof($Proof) {
    if (-not $Proof -or -not $Proof.File -or $Proof.SHA256 -notmatch '^[0-9A-F]{64}$' -or -not (Test-Path -LiteralPath $Proof.File -PathType Leaf) -or (Get-FileHash -LiteralPath $Proof.File -Algorithm SHA256).Hash -ne $Proof.SHA256) { throw 'A JavaScript gate evidence/compiler/game-reference file is absent or changed.' }
}

function Assert-JavaScriptGateReceipt($Receipt, [string]$RepositoryRoot, [string]$PackageRoot, [string]$GameBin) {
    $repository = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\','/')
    $package = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\','/')
    $game = [IO.Path]::GetFullPath($GameBin).TrimEnd('\','/')
    if (-not $Receipt -or $Receipt.Schema -ne 'HDR.JavaScript/Gate1' -or -not $Receipt.Success -or $Receipt.FocusedCheckpoint -or $Receipt.WhitelistOnly -or -not $Receipt.ActualModApiPassed -or -not $Receipt.RuntimePassed -or -not $Receipt.FrontendIntegrationPassed -or -not $Receipt.FrameworkNumericPassed -or -not $Receipt.SourceStableDuringGate -or -not $Receipt.AllInputsStableDuringGate -or $Receipt.GameContextInitialized -or $Receipt.PluginRequired -or -not $Receipt.ReceiptPath) { throw 'A complete sealed JavaScript gate is required; focused and whitelist-only checkpoints cannot authorize packaging.' }
    Assert-JavaScriptOutputDirectory $repository ([IO.Path]::GetDirectoryName($Receipt.ReceiptPath))
    if ([IO.Path]::GetFullPath($Receipt.GameBin).TrimEnd('\','/') -ne $game -or [IO.Path]::GetFullPath($Receipt.PackageRoot).TrimEnd('\','/') -ne $package) { throw 'JavaScript gate belongs to a different game or source package.' }
    [xml]$metadata = Get-Content -LiteralPath (Join-Path $package 'metadata.mod') -Raw
    if ($Receipt.Version -ne [string]$metadata.ModMetadata.ModVersion -or $Receipt.Version -notmatch '^\d+\.\d+\.\d+$') { throw 'JavaScript gate version differs from package metadata.' }
    Assert-JavaScriptInputsStable @($Receipt.Inputs) (Get-JavaScriptGateInputs $repository $package)
    $definition = Get-JavaScriptFixtureDefinition $repository
    Assert-JavaScriptFileProof $Receipt.FixtureDefinition
    Assert-JavaScriptFileProof $Receipt.Compiler
    if (@($Receipt.FixtureRuns).Count -ne 3) { throw 'JavaScript gate lacks a runtime, actual HTML or CLR4 suite.' }
    foreach ($suite in $definition.Suites) {
        $run = @($Receipt.FixtureRuns | Where-Object Name -eq $suite.Name)
        if ($run.Count -ne 1 -or -not $run[0].Passed -or $run[0].Assertions -ne $suite.Assertions -or $run[0].Cases -ne $suite.Cases -or $run[0].Failures -ne 0 -or $run[0].GameContextInitialized -or $run[0].PluginRequired) { throw 'JavaScript gate does not match exact frozen fixture counts and execution scope.' }
        Assert-JavaScriptFileProof ([pscustomobject]@{File=$run[0].ReceiptPath;SHA256=$run[0].SHA256})
        $result = Get-Content -LiteralPath $run[0].ReceiptPath -Raw | ConvertFrom-Json
        if (-not $result.Passed -or $result.Assertions -ne $suite.Assertions -or $result.Cases -ne $suite.Cases -or $result.Failures -ne 0 -or $result.GameContextInitialized -or $result.PluginRequired) { throw 'An embedded fixture result differs from its hashed CPU execution receipt.' }
    }
    Assert-JavaScriptFileProof ([pscustomobject]@{File=$Receipt.WhitelistReceipt;SHA256=$Receipt.WhitelistReceiptSHA256})
    $actual = Get-Content -LiteralPath $Receipt.WhitelistReceipt -Raw | ConvertFrom-Json
    . (Join-Path $repository 'Tools/HtmlChecks/Whitelist/AssertReceipt.ps1')
    Assert-HtmlWhitelistReceipt $actual 'ModApi'
    foreach ($proof in @($actual.References) + @($actual.AnalyzerAssembly, $actual.RegistrationAssembly, $actual.ModCompatibilityEvidenceAssembly)) { Assert-JavaScriptFileProof $proof }
    $sourceRoot = Join-Path $package 'Data/Scripts'
    $sources = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.cs' -File | Sort-Object FullName | ForEach-Object { [pscustomobject]@{ File=$_.FullName.Substring($sourceRoot.Length + 1).Replace('\','/'); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    if ($sources.Count -ne $Receipt.SourceCount -or $sources.Count -ne $actual.CandidateSourceCount -or ($sources | ConvertTo-Json -Depth 5 -Compress) -ne (@($Receipt.Sources) | ConvertTo-Json -Depth 5 -Compress)) { throw 'JavaScript gate did not compile every current selected source file.' }
    $checked = @($actual.SourceFiles | ForEach-Object { [pscustomobject]@{ File=$_.File.Substring(([string]$actual.SourceRoot).Length + 1).Replace('\','/'); SHA256=$_.SHA256 } } | Sort-Object File)
    if (($sources | ConvertTo-Json -Depth 5 -Compress) -ne ($checked | ConvertTo-Json -Depth 5 -Compress)) { throw 'Actual analyzer source fingerprints differ from the shipped package.' }
    foreach ($sdk in @('HdrHtmlApi.cs','HdrJavaScriptApi.cs')) {
        $copies = @($sources | Where-Object { [IO.Path]::GetFileName($_.File) -eq $sdk })
        if ($copies.Count -ne 1 -or $copies[0].SHA256 -ne (Get-FileHash -LiteralPath (Join-Path $repository ('Api/Mods/' + $sdk)) -Algorithm SHA256).Hash) { throw 'A shipped SDK consumer copy differs from its canonical public source.' }
    }
    $inventoryPath = Join-Path $package 'LICENSES/Licenses.json'
    if ((Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash -ne $Receipt.LicenseInventorySHA256) { throw 'Shipped license inventory changed after validation.' }
    $inventory = @(Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json)
    if ($inventory.Count -ne @($Receipt.Licenses).Count -or @($inventory | Where-Object Spdx -eq 'BSD-2-Clause').Count -lt 1) { throw 'Shipped BSD license inventory is incomplete.' }
    foreach ($license in $inventory) {
        $proof = @($Receipt.Licenses | Where-Object File -eq $license.File)
        if ($proof.Count -ne 1 -or $proof[0].Spdx -ne $license.Spdx) { throw 'License proof differs from its shipped inventory.' }
        Assert-JavaScriptFileProof ([pscustomobject]@{File=(Join-Path $package $license.File);SHA256=$proof[0].SHA256})
        $text = [IO.File]::ReadAllText((Join-Path $package $license.File))
        foreach ($fragment in $license.MustContain) { if (-not $text.Contains([string]$fragment)) { throw 'Shipped license lost original attribution, conditions or disclaimer.' } }
    }
}
