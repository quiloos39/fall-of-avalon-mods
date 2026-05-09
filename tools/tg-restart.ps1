# Kills Tainted Grail if running, launches via Steam, waits for stable load,
# reports BepInEx log state. Use during dev/diagnostic loops.
#
# Usage: pwsh tools\tg-restart.ps1
#        pwsh tools\tg-restart.ps1 -WaitAfterLaunch 25
param(
    [int]$WaitAfterLaunch = 20,
    [int]$LaunchTimeout   = 60
)

$gameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA'
$appId   = '1466060'

# 1. Kill if running
$p = Get-Process -Name 'Fall of Avalon' -ErrorAction SilentlyContinue
if ($p) {
    Write-Host "[1] killing PID(s): $($p.Id -join ',')" -ForegroundColor Yellow
    $p | Stop-Process -Force -ErrorAction SilentlyContinue
    $p | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
} else {
    Write-Host "[1] no game process running"
}

# 2. Verify closed
Start-Sleep -Milliseconds 500
if (Get-Process -Name 'Fall of Avalon' -ErrorAction SilentlyContinue) {
    throw "[2] game process refused to die"
}
Write-Host "[2] confirmed closed"

# Clear stale logs
Remove-Item (Join-Path $gameDir 'output_log.txt')         -ErrorAction SilentlyContinue
Remove-Item (Join-Path $gameDir 'BepInEx\LogOutput.log')  -ErrorAction SilentlyContinue

# 3. Launch via Steam (DRM requires Steam parent — direct EXE exits immediately)
Write-Host "[3] launching via steam://run/$appId"
Start-Process "steam://run/$appId"

# 4. Poll for the game process to appear and stabilize
$found = $null
$ticks = [int]($LaunchTimeout / 2)
for ($i = 1; $i -le $ticks; $i++) {
    Start-Sleep -Seconds 2
    $cur = Get-Process -Name 'Fall of Avalon' -ErrorAction SilentlyContinue |
           Sort-Object StartTime -Descending |
           Select-Object -First 1
    if ($cur) {
        $found = $cur
        $age = [int]((Get-Date) - $cur.StartTime).TotalSeconds
        $mem = [Math]::Round($cur.WorkingSet64 / 1MB, 0)
        Write-Host "[4] tick $i — PID $($cur.Id), age $age s, $mem MB"
        if ($age -ge 8 -and $mem -ge 500) {
            Write-Host "    OK: stably running" -ForegroundColor Green
            break
        }
    } else {
        Write-Host "[4] tick $i — game not visible yet"
    }
}
if (-not $found) {
    Write-Host "[4] FAILED: game never appeared after $LaunchTimeout s" -ForegroundColor Red
    exit 1
}

# 5. Wait for BepInEx bootstrap window, then report
Write-Host "[5] waiting $WaitAfterLaunch s for BepInEx to finish bootstrap..."
Start-Sleep -Seconds $WaitAfterLaunch

$logBep = Join-Path $gameDir 'BepInEx\LogOutput.log'
$logUni = Join-Path $gameDir 'output_log.txt'
$bepSize = if (Test-Path $logBep) { (Get-Item $logBep).Length } else { -1 }
$uniSize = if (Test-Path $logUni) { (Get-Item $logUni).Length } else { -1 }
$proc    = Get-Process -Name 'Fall of Avalon' -ErrorAction SilentlyContinue |
           Sort-Object StartTime -Descending |
           Select-Object -First 1

Write-Host "`n=== Result ==="
Write-Host "Game process:           $(if ($proc) { "PID $($proc.Id), $([int]($proc.WorkingSet64/1MB)) MB, runtime $([int]((Get-Date)-$proc.StartTime).TotalSeconds) s" } else { 'GONE' })"
Write-Host "BepInEx LogOutput.log:  $(if ($bepSize -ge 0) { "$bepSize bytes" } else { 'MISSING' })"
Write-Host "output_log.txt:         $(if ($uniSize -ge 0) { "$uniSize bytes" } else { 'MISSING' })"
if ($bepSize -gt 0) {
    Write-Host "`n--- BepInEx log first 30 lines ---"
    Get-Content $logBep -TotalCount 30
}
