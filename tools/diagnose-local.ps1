# Run with Windows PowerShell 5.1. Reads protected settings locally; prints no URL,
# server address, UUID, key, password, or unfiltered server name.
# Default: no HTTP. -Fetch reads only HTTP response metadata and sends no HWID.
param([switch]$Fetch,[switch]$InspectContent,[string]$ClientDirectory='dist-v0.5')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path (Join-Path $root $ClientDirectory) 'LunaVPN.exe'))
try {
    $path=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LunaVPN/settings.dat'
    $settings=[Luna.Storage]::Load($path)
    $format=if($settings.SubscriptionFormat -in @('Happ','v2rayN','Luna')){$settings.SubscriptionFormat}else{'unknown'}
    Write-Output ('Selected request profile: '+$format)
    Write-Output ('Saved server count: '+$settings.Servers.Count)
    foreach($server in $settings.Servers) {
        $words=[regex]::Matches($server.Name.ToLowerInvariant(),'приложение|не поддерживается|попробуйте|установить|рекомендован+ое|из списка') | ForEach-Object Value
        Write-Output ('Known instruction words only: '+($words -join ' | '))
    }
    if($InspectContent) {
        if(!$settings.SendDeviceId -or [string]::IsNullOrEmpty($settings.DeviceId)){throw 'Existing opt-in ID required'}
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        $content=[Luna.Subscriptions]::Download($settings.Subscription,$format,$settings.DeviceId).GetAwaiter().GetResult()
        Write-Output ('Response characters: '+$content.Length)
        $trimmed=$content.Trim()
        if(!$trimmed.Contains('://') -and !$trimmed.StartsWith('{') -and !$trimmed.StartsWith('[')) {
            try { $trimmed=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($trimmed)); Write-Output 'Outer encoding: Base64' } catch { Write-Output 'Outer encoding: other' }
        }
        if($trimmed.StartsWith('{') -or $trimmed.StartsWith('[')) {
            Write-Output 'Payload: JSON'
            $configs=@(ConvertFrom-Json -InputObject $trimmed)
            Write-Output ('Config count: '+$configs.Count)
            $protocols=@(); $networks=@(); $security=@()
            foreach($config in $configs) {
                foreach($outbound in $config.outbounds) {
                    if($outbound.protocol -in @('vless','vmess','trojan','shadowsocks','freedom','blackhole','dns')){$protocols+=$outbound.protocol}else{$protocols+='other'}
                    if($outbound.streamSettings.network -in @('tcp','ws','grpc','xhttp','httpupgrade')){$networks+=$outbound.streamSettings.network}
                    if($outbound.streamSettings.security -in @('none','tls','reality')){$security+=$outbound.streamSettings.security}
                }
            }
            Write-Output ('Known outbound protocols: '+(($protocols|Select-Object -Unique)-join ','))
            Write-Output ('Known transports: '+(($networks|Select-Object -Unique)-join ','))
            Write-Output ('Known TLS modes: '+(($security|Select-Object -Unique)-join ','))
        } elseif($trimmed -match '^(vless|vmess|trojan|ss)://') { Write-Output 'Payload: URI list' }
        else { Write-Output 'Payload: other (not printed)' }
        $content=$null; $trimmed=$null; $configs=$null
    }
    if($Fetch) {
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        $request=[Net.HttpWebRequest]::Create($settings.Subscription)
        $request.UserAgent='Happ/1.0'
        $request.AllowAutoRedirect=$false
        $request.Timeout=20000
        $request.ReadWriteTimeout=20000
        $response=$null
        try { $response=$request.GetResponse() }
        catch [Net.WebException] { $response=$_.Exception.Response; if($null -eq $response){throw} }
        try {
            Write-Output ('HTTP status: '+[int]$response.StatusCode)
            foreach($key in @('x-hwid-not-supported','x-hwid-max-devices-reached','x-hwid-limit','x-hwid-active')) {
                Write-Output ($key+': '+($response.Headers[$key] -eq 'true'))
            }
            $kind='other'
            if($response.ContentType -match 'json'){$kind='json'}
            elseif($response.ContentType -match 'html'){$kind='html'}
            elseif($response.ContentType -match 'text/plain'){$kind='text'}
            Write-Output ('Response type: '+$kind)
        } finally { if($response){$response.Close()} }
    }
} catch { Write-Output 'Local settings unavailable; sensitive details suppressed.'; exit 1 }
