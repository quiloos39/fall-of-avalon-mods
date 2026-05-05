#Requires -Version 5.1
<#
.SYNOPSIS
  Build a single mod, package it for Nexus, tag, push, and publish a GitHub release.

.EXAMPLE
  ./release.ps1 -Mod AutoLoot -GameVersion 0.5.2
  ./release.ps1 -Mod SpoilsOfTheSlain -GameVersion 0.5.2 -NoPublish
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Mod,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$GameVersion,

    # Skip the local git commit (PluginVersion / csproj edits stay unstaged).
    [switch]$NoCommit,

    # Skip creating the git tag.
    [switch]$NoTag,

    # Skip the git push and gh release create. The GH Action only fires on a published release,
    # so -NoPublish means a fully local dry run.
    [switch]$NoPublish
)

$ErrorActionPreference = 'Stop'
$RepoRoot = $PSScriptRoot

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Done($msg) { Write-Host "    $msg" -ForegroundColor Green }

$ModDir   = Join-Path $RepoRoot $Mod
$Csproj   = Join-Path $ModDir   "$Mod.csproj"
$PluginCs = Join-Path $ModDir   'Plugin.cs'

if (-not (Test-Path $ModDir))   { throw "Mod folder not found: $ModDir" }
if (-not (Test-Path $Csproj))   { throw "Missing csproj: $Csproj" }
if (-not (Test-Path $PluginCs)) { throw "Missing Plugin.cs: $PluginCs" }

$Tag = "$Mod-v$GameVersion"
Write-Step "Releasing $Tag"

# 1) Bump PluginVersion in Plugin.cs (UTF-8 no BOM, preserves line endings of the original).
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$pluginText = [System.IO.File]::ReadAllText($PluginCs)
$newPlugin  = $pluginText -replace 'public const string PluginVersion = "[^"]+";', `
                                   ('public const string PluginVersion = "' + $GameVersion + '";')
if ($newPlugin -eq $pluginText) {
    throw "Could not find PluginVersion constant in $PluginCs"
}
if ($newPlugin -ne $pluginText) {
    [System.IO.File]::WriteAllText($PluginCs, $newPlugin, $utf8NoBom)
    Write-Done "Updated PluginVersion in Plugin.cs -> $GameVersion"
}

# 2) Bump <Version> in csproj if one exists (not all mods have it).
$csprojText = [System.IO.File]::ReadAllText($Csproj)
$newCsproj  = $csprojText -replace '<Version>[^<]+</Version>', "<Version>$GameVersion</Version>"
if ($newCsproj -ne $csprojText) {
    [System.IO.File]::WriteAllText($Csproj, $newCsproj, $utf8NoBom)
    Write-Done "Updated <Version> in $Mod.csproj -> $GameVersion"
}

# 3) Build Release.
Write-Step "dotnet build -c Release"
& dotnet build $Csproj -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$Dll = Join-Path $ModDir "bin\Release\$Mod.dll"
if (-not (Test-Path $Dll)) { throw "Built DLL not found: $Dll" }

# 4) Stage zip layout: BepInEx/plugins/<Mod>.dll
$DistDir = Join-Path $RepoRoot 'dist'
$Stage   = Join-Path $DistDir  "$Mod-v$GameVersion"
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
$PluginsDir = Join-Path $Stage 'BepInEx/plugins'
New-Item -ItemType Directory -Path $PluginsDir -Force | Out-Null
Copy-Item $Dll $PluginsDir
Write-Done "Staged BepInEx/plugins/$Mod.dll"

# 5) Zip.
$Zip = Join-Path $DistDir "$Mod-v$GameVersion.zip"
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path (Join-Path $Stage 'BepInEx') -DestinationPath $Zip
Write-Done "Created $Zip"

# 6) Commit (only the two files we touched).
if (-not $NoCommit) {
    Push-Location $RepoRoot
    try {
        & git add -- "$Mod/Plugin.cs" "$Mod/$Mod.csproj"
        if ($LASTEXITCODE -ne 0) { throw "git add failed" }

        & git diff --cached --quiet
        if ($LASTEXITCODE -eq 0) {
            Write-Done "No version bump to commit (already at $GameVersion)"
        } else {
            & git commit -m "Release $Mod v$GameVersion"
            if ($LASTEXITCODE -ne 0) { throw "git commit failed" }
            Write-Done "Committed version bump"
        }
    }
    finally { Pop-Location }
}

# 7) Tag.
if (-not $NoTag) {
    Push-Location $RepoRoot
    try {
        & git tag $Tag
        if ($LASTEXITCODE -ne 0) { throw "git tag failed (does $Tag already exist?)" }
        Write-Done "Tagged $Tag"
    }
    finally { Pop-Location }
}

# 8) Push + GH release. The Nexus upload workflow fires on release: published.
if (-not $NoPublish) {
    Push-Location $RepoRoot
    try {
        & git push origin HEAD --tags
        if ($LASTEXITCODE -ne 0) { throw "git push failed" }
        Write-Done "Pushed branch + tag"

        & gh release create $Tag $Zip `
            --title  "$Mod v$GameVersion" `
            --notes  "Built against Tainted Grail: Fall of Avalon $GameVersion."
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
        Write-Done "Published GitHub Release $Tag"

        Write-Step "Nexus upload workflow will run automatically — watch:"
        Write-Host "    https://github.com/quiloos39/fall-of-avalon-mods/actions" -ForegroundColor DarkGray
    }
    finally { Pop-Location }
} else {
    Write-Step "Local build complete (no publish). Zip: $Zip"
}
