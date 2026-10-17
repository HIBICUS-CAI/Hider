#Requires -Version 7
<#
.SYNOPSIS
Windows capture and drawing under load: two mosaic areas refreshed every 100 ms and one generated area, run for a while
with a small gen-0 budget so finalizers run often. Passes when Hider is still running and captured repeatedly.
Exercises the GPU path where Avalonia renders through ANGLE (on CI runners: WARP, the software D3D11 device).

-AreasJson replaces those three areas with the given areas.json (CI runs it with no areas to see the check fail).

.EXAMPLE
pwsh scripts/windows-stress.ps1 -Exe bin/publish/win-x64/Hider.exe -Seconds 60
#>
[CmdletBinding()]
param(
    [string]$Exe = "bin/publish/win-x64/Hider.exe",
    [ValidateRange(10, 3600)]
    [int]$Seconds = 60,
    [string]$AreasJson = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Each mosaic area asks every 100 ms; at least one successful capture per two seconds counts as "captured repeatedly".
$minCaptures = [int][Math]::Floor($Seconds / 2)

$work = Join-Path ([IO.Path]::GetTempPath()) ("hider-stress-" + [Guid]::NewGuid().ToString('N'))
$process = $null
try {
    New-Item -ItemType Directory -Path (Join-Path $work '.hider') | Out-Null
    $areas = Join-Path $work '.hider/areas.json'
    if ($AreasJson) {
        Copy-Item -LiteralPath $AreasJson -Destination $areas
    } else {
        @'
{ "version": 1, "areas": [
  { "id": "0f3b8d2e", "x": 100, "y": 100, "width": 400, "height": 240, "opacity": 1.0, "pattern": "mosaic",
    "mosaic": { "method": "hexcell", "common": { "intervalMs": 100, "blur": 4 }, "params": { "hexcell": { "radius": 10 } } } },
  { "id": "1a2b3c4d", "x": 550, "y": 100, "width": 400, "height": 240, "opacity": 1.0, "pattern": "mosaic",
    "mosaic": { "method": "pixelate", "common": { "intervalMs": 100, "levels": 8 }, "params": { "pixelate": { "block": 12 } } } },
  { "id": "2b3c4d5e", "x": 100, "y": 400, "width": 300, "height": 200, "opacity": 0.8, "pattern": "pattern",
    "generated": { "method": "noise", "common": { "baseColor": "#68768A", "seed": 7 }, "params": { "noise": { "scale": 4, "levels": 6 } } } }
] }
'@ | Set-Content -Path $areas -Encoding utf8NoBOM
    }

    $env:USERPROFILE = $work
    $env:DOTNET_GCgen0size = '0x100000'
    $log = Join-Path $work 'hider.log'
    $process = Start-Process -FilePath (Resolve-Path $Exe) -ArgumentList '--verbose' -RedirectStandardError $log `
        -RedirectStandardOutput (Join-Path $work 'out.log') -PassThru
    # Opening the handle now keeps ExitCode readable after the process ended (otherwise it can come back empty).
    $null = $process.Handle
    Start-Sleep -Seconds $Seconds
    $alive = -not $process.HasExited
    $captures = @(Select-String -Path $log -Pattern ' capture \d+x\d+: None').Count
    if ($alive) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    } else {
        Write-Host "Hider exited with code $($process.ExitCode)"
    }
    Write-Host "alive after $Seconds s: $alive, successful captures: $captures (at least $minCaptures needed)"
    Select-String -Path $log -Pattern 'pattern drawing:' | Select-Object -First 1 | ForEach-Object { Write-Host $_.Line }
    Get-Content $log -Tail 15
    if (-not $alive -or $captures -lt $minCaptures) {
        exit 1
    }
    Write-Host "windows stress: passed"
} finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        $process.WaitForExit()
    }
    Remove-Item -Recurse -Force -Path $work -ErrorAction SilentlyContinue
}
