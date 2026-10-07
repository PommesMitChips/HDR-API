param([switch]$Apply)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskRepo = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$taskWorkspace = Split-Path -Parent $taskRepo
$taskArchive = [IO.Path]::GetFullPath((Join-Path $taskWorkspace ('_archives\HDR_API\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))))
if (-not $taskArchive.StartsWith($taskWorkspace + '\', [StringComparison]::OrdinalIgnoreCase) -or $taskArchive.StartsWith($taskRepo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive must remain in the workspace, outside the repository.' }
$taskCandidates = [Collections.Generic.List[string]]::new()
$taskRootOutput = Join-Path $taskRepo 'artifacts'
$taskKeepRoot = @('client-install','install-backups','SvgSamples','portal-motion-20261007','mod-client-native-proof','api-cleanup')
foreach ($taskEntry in Get-ChildItem -LiteralPath $taskRootOutput -Force) {
    if ($taskEntry.PSIsContainer -and $taskEntry.Name -notin $taskKeepRoot) { $taskCandidates.Add($taskEntry.FullName) }
    if (-not $taskEntry.PSIsContainer -and $taskEntry.Extension -eq '.zip' -and $taskEntry.Name -notin @('HDR-API-0.9.7.zip','HDR-API-0.9.8.zip')) { $taskCandidates.Add($taskEntry.FullName) }
}
$taskPluginOutput = Join-Path $taskRepo 'OptionalPlugins\HDRClientRenderer\artifacts'
foreach ($taskEntry in Get-ChildItem -LiteralPath $taskPluginOutput -Force) {
    if ($taskEntry.PSIsContainer -and $taskEntry.Name -ne 'Interim') { $taskCandidates.Add($taskEntry.FullName) }
    if (-not $taskEntry.PSIsContainer -and $taskEntry.Extension -eq '.zip' -and $taskEntry.Name -notmatch '^HDR-Client-Renderer-0\.9\.(11|12)-(Interim|Legacy)\.zip$') { $taskCandidates.Add($taskEntry.FullName) }
}
$taskInstallOutput = Join-Path $taskRootOutput 'client-install'
foreach ($taskEntry in Get-ChildItem -LiteralPath $taskInstallOutput -Directory -Force) {
    if ($taskEntry.Name -notin @('profile-recovery-20261007','gpu-recovery','projection-20261005','mcp-inspection-artifacts')) { $taskCandidates.Add($taskEntry.FullName) }
}
$taskTracked = @(& git -c "safe.directory=$taskRepo" -C $taskRepo ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify tracked source.' }
$taskManifest = [ordered]@{ Repository=$taskRepo; Archive=$taskArchive; Applied=[bool]$Apply; Entries=@(); Bytes=0L; Files=0 }
foreach ($taskCandidate in ($taskCandidates | Sort-Object -Unique)) {
    $taskSource = (Resolve-Path -LiteralPath $taskCandidate).Path
    if (-not $taskSource.StartsWith($taskRepo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw ('Source escaped repository: ' + $taskSource) }
    $taskRelative = $taskSource.Substring($taskRepo.Length + 1)
    $taskGitPath = $taskRelative.Replace('\','/')
    if (@($taskTracked | Where-Object { $_ -eq $taskGitPath -or $_.StartsWith($taskGitPath + '/', [StringComparison]::OrdinalIgnoreCase) }).Count) { throw ('Refusing to move tracked source: ' + $taskRelative) }
    $taskItem = Get-Item -LiteralPath $taskSource -Force
    $taskTree = @($taskItem)
    if ($taskItem.PSIsContainer) { $taskTree += @(Get-ChildItem -LiteralPath $taskSource -Recurse -Force) }
    if (@($taskTree | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw ('Refusing reparse-point archive: ' + $taskRelative) }
    $taskDestination = [IO.Path]::GetFullPath((Join-Path $taskArchive $taskRelative))
    if (-not $taskDestination.StartsWith($taskArchive + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination escaped archive.' }
    $taskFiles = @($taskTree | Where-Object { -not $_.PSIsContainer })
    $taskHashes = @($taskFiles | ForEach-Object { [ordered]@{ Relative=$_.FullName.Substring($taskRepo.Length + 1); Bytes=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    $taskBytes = [long]($taskFiles | Measure-Object -Property Length -Sum).Sum
    $taskManifest.Entries += [ordered]@{ Source=$taskSource; Destination=$taskDestination; Bytes=$taskBytes; Files=$taskHashes }
    $taskManifest.Bytes += $taskBytes
    $taskManifest.Files += $taskFiles.Count
}
if ($Apply) {
    New-Item -ItemType Directory -Path $taskArchive -Force | Out-Null
    $taskManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskArchive 'manifest.json') -Encoding UTF8
    foreach ($taskEntry in $taskManifest.Entries) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskEntry.Destination) -Force | Out-Null
        Move-Item -LiteralPath $taskEntry.Source -Destination $taskEntry.Destination
        foreach ($taskFile in $taskEntry.Files) {
            $taskArchivedFile = Join-Path $taskArchive $taskFile.Relative
            if ((Get-FileHash -LiteralPath $taskArchivedFile -Algorithm SHA256).Hash -ne $taskFile.SHA256) { throw ('Archive verification failed: ' + $taskArchivedFile) }
        }
    }
    $taskManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskArchive 'manifest.json') -Encoding UTF8
    New-Item -ItemType Directory -Path (Join-Path $taskRootOutput 'api-cleanup') -Force | Out-Null
    $taskManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskRootOutput 'api-cleanup\archive-manifest.json') -Encoding UTF8
}
[pscustomobject]@{ Applied=[bool]$Apply; Archive=$taskArchive; Entries=$taskManifest.Entries.Count; Files=$taskManifest.Files; Megabytes=[Math]::Round($taskManifest.Bytes / 1MB, 2) } | ConvertTo-Json
