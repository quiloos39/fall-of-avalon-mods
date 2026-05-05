#Requires -Version 5.1
<#
.SYNOPSIS
  Build a single mod, package it for Nexus, tag, push, and upload directly to Nexus.

  Loads NEXUSMODS_API_KEY from .env (or the process environment). The Nexus upload runs
  locally — no GitHub Actions involvement — because game DLLs aren't redistributable to
  cloud runners.

.EXAMPLE
  ./release.ps1 -Mod AutoLoot -GameVersion 0.5.2                # full release
  ./release.ps1 -Mod AutoLoot -GameVersion 0.5.2 -NoPublish     # local dry run
  ./release.ps1 -Mod AutoLoot -GameVersion 0.5.2 -NoNexus       # push GH release, skip Nexus
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

    # Skip git push, gh release create, and Nexus upload — fully local dry run.
    [switch]$NoPublish,

    # Skip just the Nexus upload (still pushes + creates the GitHub release).
    [switch]$NoNexus
)

$ErrorActionPreference = 'Stop'
$RepoRoot = $PSScriptRoot

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Done($msg) { Write-Host "    $msg" -ForegroundColor Green }

# ---------------------------------------------------------------------------
# .env loader (existing process env vars take precedence — useful for CI/manual override)
# ---------------------------------------------------------------------------
function Import-DotEnv {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return }
    Get-Content $Path | ForEach-Object {
        $line = $_.Trim()
        if ([string]::IsNullOrEmpty($line) -or $line.StartsWith('#')) { return }
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
            $k = $matches[1]
            $v = $matches[2].Trim()
            if (($v.StartsWith('"') -and $v.EndsWith('"')) -or
                ($v.StartsWith("'") -and $v.EndsWith("'"))) {
                $v = $v.Substring(1, $v.Length - 2)
            }
            if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($k))) {
                [Environment]::SetEnvironmentVariable($k, $v, 'Process')
            }
        }
    }
}
Import-DotEnv (Join-Path $RepoRoot '.env')

# ---------------------------------------------------------------------------
# Direct Nexus Upload API client.
# Mirrors Nexus-Mods/upload-action: multipart init -> presigned PUT per part ->
# complete (XML to S3) -> finalise -> poll until 'available' -> associate with file_group_id.
# ---------------------------------------------------------------------------
function Send-NexusUpload {
    param(
        [Parameter(Mandatory = $true)][string]$ApiKey,
        [Parameter(Mandatory = $true)][string]$FileGroupId,
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$DisplayName,
        [string]$Description,
        [bool]$ArchiveExisting = $true
    )

    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $apiBase = 'https://api.nexusmods.com/v3'
    $authHeaders = @{ apikey = $ApiKey; 'User-Agent' = 'release.ps1 (quiloos39)' }

    $size = (Get-Item $ZipPath).Length
    $fileName = [System.IO.Path]::GetFileName($ZipPath)
    if ([string]::IsNullOrEmpty($DisplayName)) { $DisplayName = $fileName }

    # 1) Initialise multipart upload — returns presigned URLs to PUT each part to.
    Write-Done "Requesting multipart upload ($size bytes)"
    $initBody = @{ filename = $fileName; size_bytes = "$size" } | ConvertTo-Json -Compress
    $init = Invoke-RestMethod -Uri "$apiBase/uploads/multipart" `
        -Method Post -Headers $authHeaders `
        -ContentType 'application/json' -Body $initBody
    $uploadId    = $init.data.id
    $partUrls    = @($init.data.part_presigned_urls)
    $partSize    = [int]$init.data.part_size_bytes
    $completeUrl = $init.data.complete_presigned_url
    Write-Done "Upload id $uploadId, $($partUrls.Count) part(s) of $partSize bytes"

    # 2) PUT each part to its presigned URL — these go straight to S3, NOT through the apikey-authed API.
    $parts = @()
    $stream = [System.IO.File]::OpenRead($ZipPath)
    try {
        for ($i = 0; $i -lt $partUrls.Count; $i++) {
            $partNumber  = $i + 1
            $offset      = [int64]$i * [int64]$partSize
            $remaining   = $size - $offset
            $bytesToRead = [int][Math]::Min([int64]$partSize, $remaining)

            $buffer = New-Object byte[] $bytesToRead
            $null = $stream.Seek($offset, 'Begin')
            $null = $stream.Read($buffer, 0, $bytesToRead)

            Write-Done "Part $partNumber/$($partUrls.Count) ($bytesToRead bytes)"
            $resp = Invoke-WebRequest -Uri $partUrls[$i] -Method Put `
                -Body $buffer -ContentType 'application/octet-stream' `
                -UseBasicParsing
            $etag = $resp.Headers['ETag']
            if ($etag -is [array]) { $etag = $etag[0] }
            $etag = $etag.Trim('"')
            $parts += [pscustomobject]@{ PartNumber = $partNumber; ETag = $etag }
        }
    } finally { $stream.Dispose() }

    # 3) Complete multipart — POST XML manifest of parts/ETags to the presigned complete URL.
    $partsXml = ($parts | ForEach-Object {
        "  <Part>`n    <PartNumber>$($_.PartNumber)</PartNumber>`n    <ETag>$($_.ETag)</ETag>`n  </Part>"
    }) -join "`n"
    $completeXml = "<CompleteMultipartUpload>`n$partsXml`n</CompleteMultipartUpload>"
    Write-Done "Completing multipart upload"
    $null = Invoke-WebRequest -Uri $completeUrl -Method Post `
        -Body $completeXml -ContentType 'application/xml' -UseBasicParsing

    # 4) Finalise on Nexus — moves the bytes into Nexus's processing pipeline.
    Write-Done "Finalising"
    $null = Invoke-RestMethod -Uri "$apiBase/uploads/$uploadId/finalise" `
        -Method Post -Headers $authHeaders -ContentType 'application/json'

    # 5) Poll until the upload is ready to associate with a mod.
    $maxAttempts = 60; $attempt = 0
    while ($true) {
        $attempt++
        Start-Sleep -Seconds 2
        $state = Invoke-RestMethod -Uri "$apiBase/uploads/$uploadId" -Headers $authHeaders
        Write-Done "State: $($state.data.state) (attempt $attempt/$maxAttempts)"
        if ($state.data.state -eq 'available') { break }
        if ($attempt -ge $maxAttempts) { throw "Nexus upload polling timed out for $uploadId" }
    }

    # 6) Associate with the mod's file_group_id — this is what makes the new version visible on the mod page.
    $updateBody = @{
        upload_id              = $uploadId
        name                   = $DisplayName
        version                = $Version
        file_category          = 'main'
        archive_existing_file  = $ArchiveExisting
    }
    if (-not [string]::IsNullOrEmpty($Description)) { $updateBody.description = $Description }
    Write-Done "Associating with file_group_id $FileGroupId"
    $update = Invoke-RestMethod -Uri "$apiBase/mod-file-update-groups/$FileGroupId/versions" `
        -Method Post -Headers $authHeaders `
        -ContentType 'application/json' -Body ($updateBody | ConvertTo-Json -Compress)
    Write-Done "Nexus file uid: $($update.data.id)"
    return $update.data.id
}

# ---------------------------------------------------------------------------
# Validate inputs and look up Nexus metadata BEFORE doing 30 seconds of build work.
# ---------------------------------------------------------------------------
$ModDir   = Join-Path $RepoRoot $Mod
$Csproj   = Join-Path $ModDir   "$Mod.csproj"
$PluginCs = Join-Path $ModDir   'Plugin.cs'

if (-not (Test-Path $ModDir))   { throw "Mod folder not found: $ModDir" }
if (-not (Test-Path $Csproj))   { throw "Missing csproj: $Csproj" }
if (-not (Test-Path $PluginCs)) { throw "Missing Plugin.cs: $PluginCs" }

$NexusEntry = $null
if (Test-Path (Join-Path $RepoRoot 'nexus.json')) {
    $nexusJson  = Get-Content (Join-Path $RepoRoot 'nexus.json') -Raw | ConvertFrom-Json
    $NexusEntry = $nexusJson.$Mod
}

$WillUploadToNexus = -not $NoPublish -and -not $NoNexus
if ($WillUploadToNexus) {
    if (-not $NexusEntry -or -not $NexusEntry.file_group_id) {
        throw "No nexus.json entry for '$Mod' — needs file_group_id"
    }
    $ApiKey = $env:NEXUSMODS_API_KEY
    if ([string]::IsNullOrEmpty($ApiKey)) {
        throw "NEXUSMODS_API_KEY not set. Create .env from .env.example or set the env var."
    }
}

$Tag = "$Mod-v$GameVersion"
Write-Step "Releasing $Tag"

# ---------------------------------------------------------------------------
# 1) Bump PluginVersion in Plugin.cs (UTF-8 no BOM).
# ---------------------------------------------------------------------------
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$pluginText = [System.IO.File]::ReadAllText($PluginCs)
$newPlugin  = $pluginText -replace 'public const string PluginVersion = "[^"]+";', `
                                   ('public const string PluginVersion = "' + $GameVersion + '";')
if ($newPlugin -eq $pluginText) {
    throw "Could not find PluginVersion constant in $PluginCs"
}
[System.IO.File]::WriteAllText($PluginCs, $newPlugin, $utf8NoBom)
Write-Done "Updated PluginVersion in Plugin.cs -> $GameVersion"

# 2) Bump <Version> in csproj if present (not all mods declare one).
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
    } finally { Pop-Location }
}

# 7) Tag.
if (-not $NoTag) {
    Push-Location $RepoRoot
    try {
        & git tag $Tag
        if ($LASTEXITCODE -ne 0) { throw "git tag failed (does $Tag already exist?)" }
        Write-Done "Tagged $Tag"
    } finally { Pop-Location }
}

# 8) Push + GitHub release (archival).
if (-not $NoPublish) {
    Push-Location $RepoRoot
    try {
        & git push origin HEAD --tags
        if ($LASTEXITCODE -ne 0) { throw "git push failed" }
        Write-Done "Pushed branch + tag"

        & gh release create $Tag $Zip `
            --title "$Mod v$GameVersion" `
            --notes "Built against Tainted Grail: Fall of Avalon $GameVersion."
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
        Write-Done "Published GitHub Release $Tag"
    } finally { Pop-Location }
}

# 9) Upload to Nexus.
if ($WillUploadToNexus) {
    Write-Step "Uploading to Nexus (file_group_id $($NexusEntry.file_group_id))"
    $newFileUid = Send-NexusUpload `
        -ApiKey       $ApiKey `
        -FileGroupId  $NexusEntry.file_group_id `
        -ZipPath      $Zip `
        -Version      $GameVersion `
        -DisplayName  "$Mod $GameVersion" `
        -Description  "Built against Tainted Grail: Fall of Avalon $GameVersion." `
        -ArchiveExisting $true
    Write-Step "Done. New Nexus file uid: $newFileUid"
    if ($NexusEntry.mod_id) {
        Write-Host "    https://www.nexusmods.com/taintedgrailthefallofavalon/mods/$($NexusEntry.mod_id)" -ForegroundColor DarkGray
    }
}
elseif ($NoPublish) {
    Write-Step "Local build complete (no publish). Zip: $Zip"
}
else {
    Write-Step "GitHub release published; skipped Nexus per -NoNexus. Zip: $Zip"
}
