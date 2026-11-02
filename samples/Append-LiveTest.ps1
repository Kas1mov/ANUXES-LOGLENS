# ANUXES LOGLENS - Live Monitor test helper
# 1) Open live-test.log in ANUXES LOGLENS.
# 2) Enable Live Mode.
# 3) Run this script in PowerShell. Press Ctrl+C to stop.

$logPath = Join-Path $PSScriptRoot 'live-test.log'

if (-not (Test-Path -LiteralPath $logPath)) {
    [System.IO.File]::WriteAllText($logPath, "[LIVE TEST] File created. Enable Live Mode now.`r`n", [System.Text.UTF8Encoding]::new($false))
}

$levels = @('INFO', 'DEBUG', 'WARN', 'ERROR', 'INFO', 'TRACE')
$counter = 1

Write-Host "Appending test entries to: $logPath" -ForegroundColor Cyan
Write-Host 'Press Ctrl+C to stop.' -ForegroundColor DarkGray

while ($true) {
    $level = $levels[($counter - 1) % $levels.Length]
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'
    $line = "[$timestamp] [$level] [LiveTest] Generated live entry #$counter"
    [System.IO.File]::AppendAllText($logPath, $line + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    Write-Host $line
    $counter++
    Start-Sleep -Milliseconds 1400
}
