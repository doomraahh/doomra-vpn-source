# Synthetic loopback-only config, no inbounds/TUN, no user settings or credentials.
param([switch]$RemoveDirectDetour)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$folder=Join-Path $root 'dist-v0.6'
[void][Reflection.Assembly]::LoadFrom((Join-Path $folder 'LunaVPN.exe'))
$server=[Luna.Subscriptions]::Parse('trojan://synthetic@127.0.0.1:443').Servers[0]
$config=[Luna.Configuration]::Build($server) | ConvertFrom-Json
$config.inbounds=@()
foreach($dns in $config.dns.servers) {
    $dns.server='127.0.0.1'
    if($RemoveDirectDetour -and $dns.tag -eq 'bootstrap') { $dns.PSObject.Properties.Remove('detour') }
}
$json=ConvertTo-Json -InputObject $config -Depth 32 -Compress
$p=New-Object Diagnostics.Process
$p.StartInfo=[Luna.Configuration]::StartInfo((Join-Path $folder 'sing-box.exe'),'run')
[void]$p.Start()
$err=$p.StandardError.ReadToEndAsync()
$out=$p.StandardOutput.ReadToEndAsync()
$writer=New-Object IO.StreamWriter($p.StandardInput.BaseStream,(New-Object Text.UTF8Encoding($false)))
$writer.Write($json); $writer.Close()
if($p.WaitForExit(2000)) {
    Write-Output ('Synthetic core exit: '+$p.ExitCode)
    Write-Output ($err.GetAwaiter().GetResult())
} else {
    Write-Output 'Synthetic DNS initialization passed; stopping no-inbound process.'
    $p.Kill(); $p.WaitForExit()
}
$p.Dispose()
