param([string]$Report = (Join-Path (Split-Path $PSScriptRoot) 'dist\service-report.json'))
$ErrorActionPreference = 'Stop'
$result = [ordered]@{ service = $false; automatic = $false; system = $false; secureDesktop = $false; session = $null; version = $null; restart = $false; error = $null }
try {
    $service = Get-CimInstance Win32_Service -Filter "Name='LanTouchPad'"
    if (!$service -or $service.StartName -ne 'LocalSystem' -or $service.State -ne 'Running') {
        throw 'The LanTouchPad service is not running as LocalSystem'
    }
    $result.service = $true
    $result.automatic = $service.StartMode -eq 'Auto'
    if (!$result.automatic) { throw 'The service is not configured for automatic system startup' }
    $info = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try { $info = Invoke-RestMethod 'http://127.0.0.1:45832/diagnostics' -TimeoutSec 1; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$info.system -or $info.session -eq 0) { throw 'The receiver is not running as SYSTEM in the interactive console session' }
    $result.system = $info.system
    $result.secureDesktop = $info.secureDesktopAccess
    if (!$result.secureDesktop) { throw "The receiver cannot access the Winlogon desktop (error $($info.secureDesktopError))" }
    $result.session = $info.session
    $result.version = $info.version
    Stop-Service LanTouchPad
    (Get-Service LanTouchPad).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
    $listener = Get-NetTCPConnection -LocalPort 45832 -State Listen -ErrorAction SilentlyContinue
    if ($listener) { throw 'The receiver did not exit with its service' }
    Start-Service LanTouchPad
    $restarted = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            $next = Invoke-RestMethod 'http://127.0.0.1:45832/diagnostics' -TimeoutSec 1
            if ($next.system -and $next.session -eq $info.session -and $next.version -eq $info.version) { $restarted = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (!$restarted) { throw 'Receiver did not recover after service restart' }
    $result.restart = $true
} catch { $result.error = $_.Exception.Message }
finally {
    Start-Service LanTouchPad -ErrorAction SilentlyContinue
    $result | ConvertTo-Json | Set-Content -LiteralPath $Report -Encoding UTF8
}
if ($result.error) { throw $result.error }
