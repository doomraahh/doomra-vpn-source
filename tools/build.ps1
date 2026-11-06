param([switch]$Test,[string]$OutputDirectory='dist')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
& (Join-Path $PSScriptRoot "prepare-custom-flags.ps1")
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$outputExe = Join-Path $OutputDirectory 'doomraVPN.exe'
$outputTests = Join-Path $OutputDirectory 'Tests.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$references = @('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll','/r:System.Net.Http.dll','/r:System.IO.Compression.dll')
$artwork = @('/win32icon:assets\doomra.ico','/resource:assets\doomra.ico,Doomra.Icon','/resource:assets\doomra-logo.png,Doomra.Logo','/resource:assets\fonts\JetBrainsMono-Regular.ttf,Doomra.FontRegular','/resource:assets\fonts\JetBrainsMono-Bold.ttf,Doomra.FontBold','/resource:assets\flags.zip,Doomra.Flags','/resource:assets\flags-background.zip,Doomra.FlagBackgrounds','/resource:assets\custom-flags.zip,Doomra.CustomFlagBackgrounds')
$sources = @(Get-ChildItem src/*.cs | ForEach-Object FullName)
$references += '/r:System.ServiceProcess.dll'
& $compiler /nologo /utf8output /codepage:65001 /langversion:5 /target:winexe /platform:x64 /optimize+ /win32manifest:src\app.manifest ('/out:'+$outputExe) @references @artwork @sources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed' }
Copy-Item LICENSE,docs/sing-box-LICENSE.txt,docs/xray-LICENSE.txt $OutputDirectory -Force
Copy-Item src/LunaVPN.exe.config (Join-Path $OutputDirectory 'doomraVPN.exe.config') -Force
Copy-Item assets/doomra.ico $OutputDirectory -Force
if ($Test) {
    & $compiler /nologo /utf8output /codepage:65001 /target:exe /platform:x64 ('/out:'+$outputTests) /main:Luna.Tests @references @artwork @sources @(Get-ChildItem tests/*.cs | ForEach-Object FullName)
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    & (Join-Path $PWD $outputTests)
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}
