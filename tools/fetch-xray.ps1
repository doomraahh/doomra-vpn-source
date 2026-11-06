param([string]$OutputDirectory='dist')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$version='26.10.31'
$archiveHash='2e04a9995a6f2ad1b28c0bb25c708ecb2695e43a1c9c7889c4b6ac2b48cf7a6a'
$exeHash='d5b558191cccb1e92a5772b68b8cab67717b26f634b0f15153215f65ae982a7e'
New-Item -ItemType Directory -Force cache,$OutputDirectory | Out-Null
$archive=Join-Path $PWD "cache/xray-$version.zip"
if(!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest "https://github.com/XTLS/Xray-core/releases/download/v$version/Xray-windows-64.zip" -OutFile $archive }
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archiveHash){throw 'Xray archive SHA-256 mismatch'}
$unpacked=Join-Path $PWD "cache/xray-$version"
Expand-Archive -LiteralPath $archive -DestinationPath $unpacked -Force
$exe=Join-Path $unpacked 'xray.exe'
if((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant() -ne $exeHash){throw 'Xray executable SHA-256 mismatch'}
Copy-Item -LiteralPath $exe -Destination (Join-Path $OutputDirectory 'xray.exe') -Force
Copy-Item -LiteralPath (Join-Path $unpacked 'LICENSE') -Destination (Join-Path $OutputDirectory 'xray-LICENSE.txt') -Force
Write-Output "Xray $version installed; archive and executable SHA-256 verified."
