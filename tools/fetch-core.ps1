$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item -ItemType Directory -Force cache,dist | Out-Null
$version = '1.14.4'
$expectedArchive = '99185d75d314a07dbba4a130c8ad986cbeb629a07927975636b341ca61f991df'
$expectedExe = '8f30682ee198855440d2a7fe070d5d3653793b2d01bb8c85eea88f93bbe7c8b9'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$archive = Join-Path $PWD 'cache/sing-box.zip'
Invoke-WebRequest "https://github.com/SagerNet/sing-box/releases/download/v$version/sing-box-$version-windows-amd64.zip" -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedArchive) { throw 'Archive SHA-256 mismatch; core was not installed' }
Expand-Archive -LiteralPath $archive -DestinationPath cache/core -Force
$core = Join-Path $PWD "cache/core/sing-box-$version-windows-amd64/sing-box.exe"
if ((Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedExe) { throw 'Executable SHA-256 mismatch' }
Copy-Item -LiteralPath $core -Destination dist/sing-box.exe -Force
Write-Output "Official sing-box $version installed; SHA-256 verified."
