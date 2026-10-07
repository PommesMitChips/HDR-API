param(
    [switch]$InspectOnly,
    [switch]$IncludeMod,
    [switch]$ModOnly,
    [switch]$IncludeRedirectedCopies,
    [switch]$IncludeCamera360,
    [string]$Camera360Root,
    [string]$AppDataRoot = $env:APPDATA,
    [string]$LocalAppDataRoot = $env:LOCALAPPDATA,
    [string]$PulsarLegacyRoot,
    [string]$GitHubPublisherFolder = 'PommesMitChips',
    [string]$UserModRoot,
    [string[]]$RedirectedRoamingRoots,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$hdrRoot = Split-Path -Parent $PSScriptRoot
if ($ModOnly) { $IncludeMod = $true }
if (-not $ReportPath) {
    $ReportPath = Join-Path $hdrRoot 'artifacts\client-install\physical-plugin-install.json'
}
$ReportPath = [System.IO.Path]::GetFullPath($ReportPath)
$report = [ordered]@{ Success = $false; InspectOnly = [bool]$InspectOnly; IncludeMod = [bool]$IncludeMod; ModOnly = [bool]$ModOnly; RequiresWorldReload = [bool]$ModOnly; IncludeRedirectedCopies = [bool]$IncludeRedirectedCopies; IncludeCamera360 = [bool]$IncludeCamera360; PID = $PID; PackagedContext = $null; Files = @(); ModFiles = @() }

try {
    if (-not $PulsarLegacyRoot) {
        if (-not $AppDataRoot) { throw 'Specify -AppDataRoot or -PulsarLegacyRoot when APPDATA is unavailable.' }
        $PulsarLegacyRoot = Join-Path $AppDataRoot 'Pulsar\Legacy'
    }
    if (-not $UserModRoot) {
        if (-not $AppDataRoot) { throw 'Specify -AppDataRoot or -UserModRoot when APPDATA is unavailable.' }
        $UserModRoot = Join-Path $AppDataRoot 'SpaceEngineers\Mods'
    }
    $PulsarLegacyRoot = [System.IO.Path]::GetFullPath($PulsarLegacyRoot).TrimEnd('\')
    $UserModRoot = [System.IO.Path]::GetFullPath($UserModRoot).TrimEnd('\')
    if (-not $GitHubPublisherFolder -or $GitHubPublisherFolder -in @('.', '..') -or $GitHubPublisherFolder.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        throw '-GitHubPublisherFolder must name one publisher folder.'
    }
    if (-not $PSBoundParameters.ContainsKey('RedirectedRoamingRoots')) {
        $RedirectedRoamingRoots = @()
        if ($LocalAppDataRoot) {
            $packageRoot = Join-Path $LocalAppDataRoot 'Packages'
            if (Test-Path -LiteralPath $packageRoot -PathType Container) {
                $RedirectedRoamingRoots = @(Get-ChildItem -LiteralPath $packageRoot -Directory -Filter 'OpenAI.Codex_*' | ForEach-Object {
                    Join-Path $_.FullName 'LocalCache\Roaming'
                })
            }
        }
    }
    $RedirectedRoamingRoots = @($RedirectedRoamingRoots | ForEach-Object { [System.IO.Path]::GetFullPath($_).TrimEnd('\') } | Select-Object -Unique)

    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class HdrInstallerPaths {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetCurrentPackageFullName(ref uint size, StringBuilder name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder name, uint size, uint flags);
}
'@
    [uint32]$packageNameSize = 0
    $packageResult = [HdrInstallerPaths]::GetCurrentPackageFullName([ref]$packageNameSize, $null)
    $report.PackagedContext = $packageResult -ne 15700
    if ($report.PackagedContext) {
        throw 'Windows is virtualizing this process under an app package. Run this installer from ordinary Windows PowerShell or Explorer so it can reach the real Pulsar folders.'
    }

    function Get-ActualFilePath([string]$Path) {
        $file = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
        try {
            $buffer = New-Object System.Text.StringBuilder 32768
            $count = [HdrInstallerPaths]::GetFinalPathNameByHandle($file.SafeFileHandle, $buffer, $buffer.Capacity, 0)
            if ($count -eq 0 -or $count -ge $buffer.Capacity) { throw ('Cannot resolve actual file path: ' + $Path) }
            return $buffer.ToString()
        } finally { $file.Dispose() }
    }

    function Get-ExpectedFilePath([string]$Path) {
        $fullPath = [System.IO.Path]::GetFullPath($Path)
        if ($fullPath.StartsWith('\\')) { return '\\?\UNC\' + $fullPath.Substring(2) }
        return '\\?\' + $fullPath
    }

    function Get-Sha256([string]$Path) {
        $file = [System.IO.File]::OpenRead($Path)
        $hash = [System.Security.Cryptography.SHA256]::Create()
        try { return [System.BitConverter]::ToString($hash.ComputeHash($file)).Replace('-', '') }
        finally { $hash.Dispose(); $file.Dispose() }
    }

    $sourcePaths = @(
        (Join-Path $hdrRoot 'OptionalPlugins\HDRClientRenderer\artifacts\Interim\HDRClientRenderer.dll'),
        (Join-Path $hdrRoot 'OptionalPlugins\HDRClientRenderer\HDRClientRenderer.dll.xml')
    )
    if ($IncludeCamera360 -and -not $ModOnly) {
        if (-not $Camera360Root) { throw 'Specify -Camera360Root when using -IncludeCamera360.' }
        $Camera360Root = [System.IO.Path]::GetFullPath($Camera360Root)
        $sourcePaths += Join-Path $Camera360Root 'artifacts\Interim\Camera360Client.dll'
        $sourcePaths += Join-Path $Camera360Root 'Client\Camera360Client.dll.xml'
    }
    $destinations = @(
        @{ Name = 'Local'; Path = (Join-Path $PulsarLegacyRoot 'Local') },
        @{ Name = ('GitHub-' + $GitHubPublisherFolder); Path = (Join-Path (Join-Path $PulsarLegacyRoot 'GitHub') $GitHubPublisherFolder) }
    )
    if ($IncludeRedirectedCopies) {
        # A launcher inheriting the desktop app's filesystem view can see these
        # existing private copies instead of the physical Roaming installation.
        $redirectedIndex = 0
        foreach ($redirectedRoaming in $RedirectedRoamingRoots) {
            $redirectedIndex++
            $redirectedLegacy = Join-Path $redirectedRoaming 'Pulsar\Legacy'
            if (Test-Path -LiteralPath (Join-Path $redirectedLegacy 'Local\HDRClientRenderer.dll') -PathType Leaf) {
                $destinations += @{ Name = ('Redirected-' + $redirectedIndex + '-Local'); Path = (Join-Path $redirectedLegacy 'Local') }
                $destinations += @{ Name = ('Redirected-' + $redirectedIndex + '-GitHub-' + $GitHubPublisherFolder); Path = (Join-Path (Join-Path $redirectedLegacy 'GitHub') $GitHubPublisherFolder) }
            }
        }
    }

    $runningGameOrLoader = @(Get-Process -Name Interim,SpaceEngineers,Modern,Pulsar -ErrorAction SilentlyContinue).Count -gt 0
    if ($ModOnly) { $sourcePaths = @(); $destinations = @() }
    if (-not $InspectOnly) {
        if (-not $ModOnly -and $runningGameOrLoader) {
            throw 'Close Space Engineers/Pulsar before installing the client plugins.'
        }
        $backupRoot = Join-Path $hdrRoot ('artifacts\install-backups\physical-pulsar-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Path $backupRoot -ErrorAction Stop | Out-Null
        $report.Backup = $backupRoot
    }

    # Profile files are read/copied for evidence only, never rewritten.
    $profileRoots = @(
        @{ Name = 'Regular'; Path = (Join-Path $PulsarLegacyRoot 'Profiles') }
    )
    $redirectedIndex = 0
    foreach ($redirectedRoaming in $RedirectedRoamingRoots) {
        $redirectedIndex++
        $profileRoots += @{ Name = ('Redirected-' + $redirectedIndex); Path = (Join-Path $redirectedRoaming 'Pulsar\Legacy\Profiles') }
    }
    $profileBefore = @{}
    foreach ($profileRoot in $profileRoots) {
        if (!(Test-Path -LiteralPath $profileRoot.Path)) { continue }
        foreach ($profileFile in Get-ChildItem -LiteralPath $profileRoot.Path -File -Force) {
            if ((Get-ActualFilePath $profileFile.FullName) -ine (Get-ExpectedFilePath $profileFile.FullName)) { throw 'Profile evidence access was redirected.' }
            $profileBefore[$profileFile.FullName] = Get-Sha256 $profileFile.FullName
            if (!$InspectOnly) {
                $profileBackup = Join-Path $backupRoot ('Profiles-' + $profileRoot.Name)
                New-Item -ItemType Directory -Path $profileBackup -Force | Out-Null
                Copy-Item -LiteralPath $profileFile.FullName -Destination (Join-Path $profileBackup $profileFile.Name)
                if ((Get-Sha256 (Join-Path $profileBackup $profileFile.Name)) -ne $profileBefore[$profileFile.FullName]) { throw 'Profile evidence backup mismatch.' }
            }
        }
    }

    foreach ($destination in $destinations) {
        if (-not $InspectOnly) {
            New-Item -ItemType Directory -Path $destination.Path -Force -ErrorAction Stop | Out-Null
            $backupDir = Join-Path $backupRoot $destination.Name
            New-Item -ItemType Directory -Path $backupDir -ErrorAction Stop | Out-Null
        }
        foreach ($sourcePath in $sourcePaths) {
            $fileName = [System.IO.Path]::GetFileName($sourcePath)
            $installedPath = Join-Path $destination.Path $fileName
            $sourceHash = Get-Sha256 $sourcePath
            if (-not $InspectOnly) {
                if (Test-Path -LiteralPath $installedPath -PathType Leaf) {
                    $previousHash = Get-Sha256 $installedPath
                    $backupPath = Join-Path $backupDir $fileName
                    Copy-Item -LiteralPath $installedPath -Destination $backupPath -ErrorAction Stop
                    if ((Get-Sha256 $backupPath) -ne $previousHash) { throw ('Backup mismatch: ' + $installedPath) }
                }
                Copy-Item -LiteralPath $sourcePath -Destination $installedPath -Force -ErrorAction Stop
            }
            $entry = [ordered]@{ Path = $installedPath; Exists = (Test-Path -LiteralPath $installedPath -PathType Leaf); SHA256 = $null; ActualPath = $null; MatchesSource = $false }
            if ($entry.Exists) {
                $entry.SHA256 = Get-Sha256 $installedPath
                $entry.ActualPath = Get-ActualFilePath $installedPath
                $expectedPath = Get-ExpectedFilePath $installedPath
                if ($entry.ActualPath -ine $expectedPath) { throw ('Installation was redirected: ' + $entry.ActualPath) }
                $entry.MatchesSource = $entry.SHA256 -eq $sourceHash
                if (-not $InspectOnly -and -not $entry.MatchesSource) { throw ('Copy mismatch: ' + $installedPath) }
            }
            $report.Files += [pscustomobject]$entry
        }
    }
    if ($IncludeMod) {
        $modSource = [System.IO.Path]::GetFullPath((Join-Path $hdrRoot 'Mod')).TrimEnd('\')
        $modTargets = @(@{ Name = 'HDR API'; Path = (Join-Path $UserModRoot 'HDR API') })
        if ($IncludeRedirectedCopies) {
            $redirectedIndex = 0
            foreach ($redirectedRoaming in $RedirectedRoamingRoots) {
                $redirectedIndex++
                $redirectedMod = Join-Path $redirectedRoaming 'SpaceEngineers\Mods\HDR API'
                if (Test-Path -LiteralPath (Join-Path $redirectedMod 'metadata.mod') -PathType Leaf) {
                    $modTargets += @{ Name = ('Redirected-' + $redirectedIndex + '-HDR API'); Path = $redirectedMod }
                }
            }
        }
        $modPlan = @()
        foreach ($modDestination in $modTargets) {
        $modTarget = $modDestination.Path
        foreach ($sourceFile in Get-ChildItem -LiteralPath $modSource -File -Recurse) {
            $relative = $sourceFile.FullName.Substring($modSource.Length + 1)
            $targetFile = [System.IO.Path]::GetFullPath((Join-Path $modTarget $relative))
            if (-not $targetFile.StartsWith($modTarget + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Mod file escaped the exact HDR API directory.' }
            $sourceHash = Get-Sha256 $sourceFile.FullName
            $needsCopy = -not (Test-Path -LiteralPath $targetFile -PathType Leaf)
            if (-not $needsCopy) { $needsCopy = (Get-Sha256 $targetFile) -ne $sourceHash }
            if ($ModOnly -and $runningGameOrLoader -and $needsCopy -and $sourceFile.Extension -notin @('.cs','.mod')) {
                throw ('Close Space Engineers before replacing mod assets: ' + $relative)
            }
            $modPlan += [pscustomobject]@{ Source = $sourceFile.FullName; Target = $targetFile; BackupName = $modDestination.Name; Relative = $relative; SHA256 = $sourceHash; NeedsCopy = $needsCopy }
        }
        }
        if (-not $InspectOnly) {
            # Preserve all replaced mod files before writing any of them.
            foreach ($entry in $modPlan) {
                if ($ModOnly -and -not $entry.NeedsCopy) { continue }
                if (Test-Path -LiteralPath $entry.Target -PathType Leaf) {
                    $backupFile = Join-Path (Join-Path $backupRoot $entry.BackupName) $entry.Relative
                    New-Item -ItemType Directory -Path (Split-Path -Parent $backupFile) -Force | Out-Null
                    Copy-Item -LiteralPath $entry.Target -Destination $backupFile -ErrorAction Stop
                    if ((Get-Sha256 $backupFile) -ne (Get-Sha256 $entry.Target)) { throw ('Mod backup mismatch: ' + $entry.Target) }
                }
            }
        }
        foreach ($entry in $modPlan) {
            if (-not $InspectOnly -and (-not $ModOnly -or $entry.NeedsCopy)) {
                New-Item -ItemType Directory -Path (Split-Path -Parent $entry.Target) -Force | Out-Null
                Copy-Item -LiteralPath $entry.Source -Destination $entry.Target -Force -ErrorAction Stop
            }
            $installed = [ordered]@{ Path = $entry.Target; Exists = (Test-Path -LiteralPath $entry.Target -PathType Leaf); ActualPath = $null; MatchesSource = $false }
            if ($installed.Exists) {
                $installed.ActualPath = Get-ActualFilePath $entry.Target
                if ($installed.ActualPath -ine (Get-ExpectedFilePath $entry.Target)) { throw ('Mod installation was redirected: ' + $installed.ActualPath) }
                $installed.MatchesSource = (Get-Sha256 $entry.Target) -eq $entry.SHA256
            }
            $report.ModFiles += [pscustomobject]$installed
            if (-not $InspectOnly -and -not $installed.MatchesSource) { throw ('Installed mod mismatch: ' + $entry.Target) }
        }
    }

    $profilePath = Join-Path $PulsarLegacyRoot 'Profiles\Current.xml'
    if (Test-Path -LiteralPath $profilePath) {
        [xml]$profile = Get-Content -LiteralPath $profilePath -Raw
        $report.LocalProfileIDs = @($profile.SelectNodes('/Profile/Local/string') | ForEach-Object { $_.InnerText })
        $report.GitHubProfileCount = $profile.SelectNodes('/Profile/GitHub/GitHubPluginConfig').Count
    }
    $report.WorldMods = @()
    $worldModNames = @('HDR API')
    if ($IncludeCamera360) { $worldModNames += '360 Camera' }
    foreach ($modName in $worldModNames) {
        $metadataPath = Join-Path (Join-Path $UserModRoot $modName) 'metadata.mod'
        $modEntry = [ordered]@{ Name = $modName; Exists = (Test-Path -LiteralPath $metadataPath -PathType Leaf); Version = $null; ActualPath = $null }
        if ($modEntry.Exists) {
            [xml]$metadata = Get-Content -LiteralPath $metadataPath -Raw
            $modEntry.Version = [string]$metadata.ModMetadata.ModVersion
            $modEntry.ActualPath = Get-ActualFilePath $metadataPath
        }
        $report.WorldMods += [pscustomobject]$modEntry
    }
    $report.Profiles = @()
    foreach ($profileRoot in $profileRoots) {
        if (!(Test-Path -LiteralPath $profileRoot.Path)) { continue }
        foreach ($profileFile in Get-ChildItem -LiteralPath $profileRoot.Path -File -Force) {
            if ((Get-ActualFilePath $profileFile.FullName) -ine (Get-ExpectedFilePath $profileFile.FullName)) { throw 'Profile readback access was redirected.' }
            $profileHash = Get-Sha256 $profileFile.FullName
            $sameProfile = $profileBefore.ContainsKey($profileFile.FullName) -and $profileBefore[$profileFile.FullName] -eq $profileHash
            $report.Profiles += @{ Path = $profileFile.FullName; SHA256 = $profileHash; Unchanged = $sameProfile }
            if (!$sameProfile) { throw ('Profile changed during installation: ' + $profileFile.FullName) }
        }
    }
    foreach ($profilePathBefore in $profileBefore.Keys) {
        if (!(Test-Path -LiteralPath $profilePathBefore -PathType Leaf)) { throw ('Profile disappeared during installation: ' + $profilePathBefore) }
    }
    $report.ProfilesUnchanged = $true
    $report.Success = $true
} catch {
    $report.Error = $_.Exception.Message
} finally {
    New-Item -ItemType Directory -Path (Split-Path -Parent $ReportPath) -Force | Out-Null
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
if (-not $report.Success) { throw $report.Error }
$report | ConvertTo-Json -Depth 6
