# Windows PowerShell 5.1: no subscription fetch and no core run/TUN.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$folder=Join-Path $root 'dist-v0.6'
[void][Reflection.Assembly]::LoadFrom((Join-Path $folder 'LunaVPN.exe'))
try {
    $settings=[Luna.Storage]::Load((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LunaVPN/settings.dat'))
    $ok=0; $failed=0
    foreach($server in $settings.Servers) {
        try {
            [Luna.Configuration]::Check((Join-Path $folder 'sing-box.exe'),[Luna.Configuration]::Build($server))
            $ok++
        } catch { $failed++ }
    }
    Write-Output ('Saved server configurations accepted by sing-box check: '+$ok)
    Write-Output ('Rejected configurations: '+$failed)
} catch { Write-Output 'Saved settings check unavailable; sensitive details suppressed.'; exit 1 }
