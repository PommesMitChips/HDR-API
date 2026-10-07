param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$project = [System.IO.Path]::GetFullPath($ProjectRoot)
$provider = Join-Path $project 'OptionalMods\HDRInputBackend'
& dotnet run --project (Join-Path $project 'Tools\HDRProviderChecks\HDRProviderChecks.csproj') -- $project
if ($LASTEXITCODE -ne 0) { throw 'Optional provider checks failed.' }
$output = Join-Path $project 'artifacts\HDRInputBackend-experimental.zip'
Compress-Archive -Path (Join-Path $provider '*') -DestinationPath $output -Force
Write-Host "Created optional experimental package: $output"
Write-Host 'Acknowledged capture is deliberately unavailable. This does not install or modify Workshop files.'
