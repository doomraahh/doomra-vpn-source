param([string]$InputDirectory='dist',[string]$ArchivePath='releases/doomraVPN-0.50-windows-x64.zip')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$stage=(Resolve-Path -LiteralPath $InputDirectory).Path
if (!(Test-Path (Join-Path $stage 'doomraVPN.exe')) -or !(Test-Path (Join-Path $stage 'sing-box.exe'))) { throw 'Build and fetch the core first' }
$source=Join-Path $stage 'source'
$documentation=Join-Path $stage 'docs'
New-Item -ItemType Directory -Force $source,$documentation | Out-Null
Copy-Item src,tests,tools,docs,assets,installer -Destination $source -Recurse -Force
Copy-Item README.md,LICENSE,.gitignore -Destination $source -Force
Copy-Item docs/* -Destination $documentation -Recurse -Force
Copy-Item README.md $stage -Force
$shippingNames=@('doomraVPN.exe','doomraVPN.exe.config','doomra.ico','sing-box.exe','xray.exe','xray-LICENSE.txt','LICENSE','sing-box-LICENSE.txt','README.md')
$files=@($shippingNames | ForEach-Object {
    $path=Join-Path $stage $_
    if(!(Test-Path -LiteralPath $path)){throw "Missing release file: $_"}
    Get-Item -LiteralPath $path
})
$files | ForEach-Object { ((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name) } | Set-Content (Join-Path $stage 'SHA256SUMS.txt') -Encoding ascii
New-Item -ItemType Directory -Force releases | Out-Null
$entries=@($files.FullName) + @((Join-Path $stage 'SHA256SUMS.txt'),$source,$documentation)
Compress-Archive -Path $entries -DestinationPath $ArchivePath -Force
Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256 | Format-List
