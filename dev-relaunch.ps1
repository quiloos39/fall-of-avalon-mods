<#
.SYNOPSIS
    Fast rebuild + relaunch loop for Tainted Grail: Fall of Avalon BepInEx mod development.

.DESCRIPTION
    Builds plugin(s), kills the running game (graceful then forced), launches via
    Steam, and optionally tails the BepInEx log. Designed as a fallback when
    runtime hot-reload via ScriptEngine is not viable for the change at hand
    (e.g. you edited Plugin.cs metadata, added new HarmonyX patches that need
    re-registration via ChainloaderEntry, or changed publicizer-affected types).

    For most code changes the faster loop is:
        1. Run with -ScriptEngine to copy the freshly-built DLL into BepInEx\scripts
        2. Press F6 in-game (default ScriptEngine reload key)

.PARAMETER Mod
    Name of a single project to build (e.g. "BetterSortingCrafting"). When
    omitted, the full TaintedGrailMods.sln is built.

.PARAMETER NoBuild
    Skip the build step entirely. Useful when you only want to relaunch.

.PARAMETER NoLaunch
    Kill the game (if running) but do not relaunch.

.PARAMETER TailLog
    After launching, follow BepInEx\LogOutput.log. Ctrl-C to stop tailing.

.PARAMETER ScriptEngine
    Instead of relaunching, copy the freshly-built DLL into BepInEx\scripts
    so ScriptEngine reloads it on F6. Skips game kill/launch. Combine with
    -Mod to copy a single plugin.

.PARAMETER Configuration
    Build configuration. Defaults to Debug.

.PARAMETER Verbosity
    MSBuild verbosity. Defaults to minimal.

.EXAMPLE
    .\dev-relaunch.ps1 -Mod BetterSortingCrafting
    Build only BetterSortingCrafting, kill game, relaunch via Steam.

.EXAMPLE
    .\dev-relaunch.ps1 -Mod BetterSortingCrafting -ScriptEngine
    Build, copy to scripts/, then press F6 in-game to reload (no relaunch).

.EXAMPLE
    .\dev-relaunch.ps1 -TailLog
    Build everything, relaunch, and tail BepInEx log until Ctrl-C.

.EXAMPLE
    .\dev-relaunch.ps1 -NoBuild
    Just kill and relaunch the game (use after manual build / Visual Studio build).
#>

[CmdletBinding()]
param(
    [string]$Mod,
    [switch]$NoBuild,
    [switch]$NoLaunch,
    [switch]$TailLog,
    [switch]$ScriptEngine,
    [string]$Configuration = "Debug",
    [string]$Verbosity = "minimal"
)

$ErrorActionPreference = "Stop"

# --- constants -----------------------------------------------------------
$SteamAppId   = 1466060
$GameRoot     = "C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA"
$GameExeName  = "Fall of Avalon"          # Get-Process drops the .exe suffix
$GameExePath  = Join-Path $GameRoot "Fall of Avalon.exe"
$BepInExLog   = Join-Path $GameRoot "BepInEx\LogOutput.log"
$PluginsDir   = Join-Path $GameRoot "BepInEx\plugins"
$ScriptsDir   = Join-Path $GameRoot "BepInEx\scripts"
$Solution     = Join-Path $PSScriptRoot "TaintedGrailMods.sln"
$KillTimeout  = [TimeSpan]::FromSeconds(8)

function Write-Step($msg, $color = "Cyan")  { Write-Host "==> $msg" -ForegroundColor $color }
function Write-Note($msg)                   { Write-Host "    $msg" -ForegroundColor DarkGray }
function Write-Ok($msg)                     { Write-Host "    $msg" -ForegroundColor Green }
function Write-Warn($msg)                   { Write-Host "    $msg" -ForegroundColor Yellow }
function Write-Err($msg)                    { Write-Host "    $msg" -ForegroundColor Red }

# --- build ---------------------------------------------------------------
function Invoke-Build {
    if ($NoBuild) {
        Write-Step "Skipping build (-NoBuild)" "DarkGray"
        return
    }

    if ($Mod) {
        $proj = Join-Path $PSScriptRoot (Join-Path $Mod "$Mod.csproj")
        if (-not (Test-Path $proj)) {
            throw "Project not found: $proj"
        }
        Write-Step "Building $Mod ($Configuration)"
        $target = $proj
    } else {
        if (-not (Test-Path $Solution)) { throw "Solution not found: $Solution" }
        Write-Step "Building solution ($Configuration)"
        $target = $Solution
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & dotnet build $target -c $Configuration -v $Verbosity --nologo
    $sw.Stop()
    if ($LASTEXITCODE -ne 0) {
        Write-Err "Build FAILED (exit $LASTEXITCODE) after $([int]$sw.Elapsed.TotalMilliseconds) ms"
        exit $LASTEXITCODE
    }
    Write-Ok "Build OK in $([int]$sw.Elapsed.TotalMilliseconds) ms"
}

# --- ScriptEngine path: copy DLL(s) into BepInEx\scripts ----------------
function Invoke-ScriptEngineCopy {
    if (-not (Test-Path $ScriptsDir)) {
        New-Item -ItemType Directory -Path $ScriptsDir | Out-Null
    }
    if ($Mod) {
        $src = Join-Path $PluginsDir "$Mod.dll"
        if (-not (Test-Path $src)) {
            throw "Built DLL not found at $src -- did the post-build copy succeed?"
        }
        Copy-Item $src (Join-Path $ScriptsDir "$Mod.dll") -Force
        Write-Ok "Staged $Mod.dll -> BepInEx\scripts\ (press F6 in-game to reload)"
    } else {
        # Copy every project's freshly-built DLL we can identify.
        $modDirs = Get-ChildItem $PSScriptRoot -Directory |
            Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") }
        foreach ($d in $modDirs) {
            $src = Join-Path $PluginsDir "$($d.Name).dll"
            if (Test-Path $src) {
                Copy-Item $src (Join-Path $ScriptsDir "$($d.Name).dll") -Force
                Write-Note "  staged $($d.Name).dll"
            }
        }
        Write-Ok "All staged. Press F6 in-game (default ScriptEngine reload key)."
    }
}

# --- kill game -----------------------------------------------------------
function Stop-Game {
    $procs = Get-Process -Name $GameExeName -ErrorAction SilentlyContinue
    if (-not $procs) {
        Write-Step "Game not running, skipping kill" "DarkGray"
        return
    }

    Write-Step "Stopping running game (PID $($procs.Id -join ', '))"
    foreach ($p in $procs) {
        try { [void]$p.CloseMainWindow() } catch { }
    }
    $deadline = (Get-Date) + $KillTimeout
    while ((Get-Date) -lt $deadline) {
        $alive = Get-Process -Name $GameExeName -ErrorAction SilentlyContinue
        if (-not $alive) { Write-Ok "Game closed gracefully"; return }
        Start-Sleep -Milliseconds 300
    }

    Write-Warn "Graceful close timed out, force-killing..."
    Get-Process -Name $GameExeName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    if (Get-Process -Name $GameExeName -ErrorAction SilentlyContinue) {
        Write-Err "Game process is still alive after Stop-Process -Force"
    } else {
        Write-Ok "Game force-killed"
    }
}

# --- launch --------------------------------------------------------------
function Start-Game {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Step "Launching via Steam (appid $SteamAppId)"
    try {
        Start-Process "steam://rungameid/$SteamAppId" | Out-Null
        Write-Ok "Steam URL invoked in $([int]$sw.Elapsed.TotalMilliseconds) ms"
    } catch {
        Write-Warn "Steam launch failed: $_"
        if (Test-Path $GameExePath) {
            Write-Warn "Falling back to direct exe (BepInEx still loads via winhttp doorstop)"
            Start-Process -FilePath $GameExePath -WorkingDirectory $GameRoot
            Write-Ok "Direct launch invoked"
        } else {
            throw "Cannot find $GameExePath"
        }
    }
}

# --- tail ----------------------------------------------------------------
function Watch-Log {
    if (-not (Test-Path $BepInExLog)) {
        Write-Warn "Log not found yet at $BepInExLog -- waiting for game to create it..."
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-Date) -lt $deadline -and -not (Test-Path $BepInExLog)) {
            Start-Sleep -Milliseconds 500
        }
        if (-not (Test-Path $BepInExLog)) {
            Write-Err "Log never appeared -- aborting tail"
            return
        }
    }
    Write-Step "Tailing $BepInExLog (Ctrl-C to stop)"
    Get-Content -Wait -Tail 0 $BepInExLog
}

# --- main ----------------------------------------------------------------
$total = [System.Diagnostics.Stopwatch]::StartNew()

Invoke-Build

if ($ScriptEngine) {
    Invoke-ScriptEngineCopy
    $total.Stop()
    Write-Host ""
    Write-Host "Total: $([int]$total.Elapsed.TotalMilliseconds) ms" -ForegroundColor Magenta
    return
}

Stop-Game

if ($NoLaunch) {
    Write-Step "Skipping launch (-NoLaunch)" "DarkGray"
    $total.Stop()
    Write-Host ""
    Write-Host "Total: $([int]$total.Elapsed.TotalMilliseconds) ms" -ForegroundColor Magenta
    return
}

Start-Game
$total.Stop()
Write-Host ""
Write-Host "Total: $([int]$total.Elapsed.TotalMilliseconds) ms" -ForegroundColor Magenta

if ($TailLog) {
    Watch-Log
}
