function Get-HtmlGateInputs([string]$RepositoryRoot) {
    $files = @(
        Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'OptionalMods/HDRHtmlFrontend') -Recurse -File
        Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'Tools/HtmlChecks') -Recurse -File
        Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'Mod/Data/Scripts/HoloMap') -Filter '*.cs' -File
        Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'Api/Ingame') -Filter '*.cs' -File
        Get-Item -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.props'), (Join-Path $RepositoryRoot 'Api/Mods/HdrModApi.cs'), (Join-Path $RepositoryRoot 'Api/Mods/HdrHtmlApi.cs'), (Join-Path $RepositoryRoot 'Tools/Checks/DrawCommandTests.cs'), (Join-Path $RepositoryRoot 'Tools/Checks/ClientReplicationTests.cs')
    )
    @($files | Sort-Object FullName -Unique | ForEach-Object {
        [pscustomobject]@{ Path=$_.FullName.Substring($RepositoryRoot.Length + 1).Replace('\','/'); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
function Get-HtmlGateSuites([string]$RepositoryRoot) {
    $suites = @(Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Tools/HtmlChecks/Suites.json') -Raw | ConvertFrom-Json)
    $required = @('Parser','Layout','Retained','NativeLcd','Host','CoreSeams','Pb','Composition')
    if ($suites.Count -ne $required.Count -or (($suites.Name | Sort-Object) -join ',') -ne (($required | Sort-Object) -join ',') -or @($suites | Where-Object { -not $_.Expected -or [int]$_.Expected -lt 1 }).Count) { throw 'The eight HTML test suites need reviewed, frozen assertion counts in Suites.json before the release gate can run.' }
    return $suites
}
function Get-HtmlFrontendVersion([string]$RepositoryRoot) {
    [xml]$metadata = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'OptionalMods/HDRHtmlFrontend/metadata.mod') -Raw
    $version = [string]$metadata.ModMetadata.ModVersion
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Frontend metadata must declare a three-part source package version.' }
    return $version
}
function Assert-HtmlOutputDirectory([string]$RepositoryRoot, [string]$OutputDirectory) {
    $allowed = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'artifacts')).TrimEnd('\','/')
    $target = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\','/')
    if (-not $target.Equals($allowed,[StringComparison]::OrdinalIgnoreCase) -and -not $target.StartsWith(($allowed + [IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)) {
        throw 'HTML build/check output must stay inside the repository artifacts directory. Game, world, mod-install and Pulsar paths are never output targets.'
    }
}
