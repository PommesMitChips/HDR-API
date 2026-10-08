[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SourceDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[Parameter(Mandatory=$true)][string]$CompilerDll,[string]$FrameworkDirectory=(Join-Path $env:SystemRoot 'Microsoft.NET/Framework64/v4.0.30319'))
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$binary=Join-Path $output 'NumericFrameworkTests.exe'
$references=@('mscorlib.dll','System.dll','System.Core.dll') | ForEach-Object { Join-Path $FrameworkDirectory $_ }
$vendor=Join-Path $SourceDirectory 'HDRJavaScriptRuntime/Vendor'
$runtime=Join-Path $SourceDirectory 'HDRJavaScriptRuntime/Runtime'
$sources=@(Get-ChildItem -LiteralPath $vendor,$runtime -Filter '*.cs' -Recurse -File | Sort-Object FullName | ForEach-Object FullName)
$sources+=Join-Path $SourceDirectory 'HDRJavaScriptRuntime/Host/IJavaScriptRealm.cs'
$sources+=Join-Path $PSScriptRoot 'NumericFrameworkTests.cs'
$response=@('/nologo','/noconfig','/nostdlib+','/target:exe','/langversion:6','/optimize+',('/out:"'+$binary+'"'))
$response+=$references | ForEach-Object { '/reference:"'+$_+'"' }
$response+=$sources | ForEach-Object { '"'+$_+'"' }
$rsp=Join-Path $output 'numeric-csc.rsp'
[IO.File]::WriteAllLines($rsp,[string[]]$response,[Text.UTF8Encoding]::new($false))
$compile=@(& dotnet $CompilerDll ('@'+$rsp) 2>&1 | ForEach-Object { $_.ToString() })
[IO.File]::WriteAllLines((Join-Path $output 'compile.log'),[string[]]$compile)
if($LASTEXITCODE-ne0){$compile|Select-Object -Last 20|Write-Host;throw 'CLR4 numeric fixture failed to compile.'}
$result=Join-Path $output 'receipt.json'
$start=[Diagnostics.ProcessStartInfo]::new()
$start.FileName=$binary;$start.ArgumentList.Add($result);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$process=[Diagnostics.Process]::Start($start)
$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
$finished=$process.WaitForExit(10000)
if(-not$finished){$process.Kill($true);$process.WaitForExit()}
[IO.File]::WriteAllText((Join-Path $output 'run.log'),($stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()),[Text.UTF8Encoding]::new($false))
if(-not$finished-or$process.ExitCode-ne0){throw 'CLR4 numeric fixture failed; see run.log.'}
$receipt=Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
[pscustomobject]@{Name='FrameworkNumeric';Passed=$receipt.Passed;Assertions=$receipt.Assertions;Cases=$receipt.Cases;Failures=$receipt.Failures;ReceiptPath=$result;SHA256=(Get-FileHash -LiteralPath $result -Algorithm SHA256).Hash;GameContextInitialized=$receipt.GameContextInitialized;PluginRequired=$receipt.PluginRequired}
