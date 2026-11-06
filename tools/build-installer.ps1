param(
    [string]$InputDirectory='dist-v0.50',
    [string]$Compiler='cache/InnoSetup/ISCC.exe',
    [string]$SigningCommand
)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$stage=(Resolve-Path -LiteralPath $InputDirectory).Path
$compilerPath=(Resolve-Path -LiteralPath $Compiler).Path
$release=(New-Item -ItemType Directory -Force releases).FullName
$arguments=@("/DStage=$stage","/DOutput=$release")
if($SigningCommand) {
    $exe=Join-Path $stage 'doomraVPN.exe'
    if((Get-AuthenticodeSignature -LiteralPath $exe).Status -ne 'Valid'){
        throw 'Sign doomraVPN.exe with the publisher certificate before building a signed setup.'
    }
    $arguments += '/DSignRelease',("/Sdoomra=$SigningCommand")
}
& $compilerPath @arguments installer/doomra.iss
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
Get-FileHash -LiteralPath (Join-Path $release 'doomraVPN-0.50-setup-windows-x64.exe') -Algorithm SHA256
