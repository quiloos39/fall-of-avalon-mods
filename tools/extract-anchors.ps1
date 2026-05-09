<#
.SYNOPSIS
  Walk every mod project under the repo, scrape the game-side type/method
  symbols our Harmony patches anchor to, and emit a sorted JSON inventory.

.DESCRIPTION
  Recognises the patterns documented in tools/README.md:
    [HarmonyPatch(typeof(X), nameof(X.Y))]
    [HarmonyPatch(typeof(X), "Y")]
    [HarmonyPatch(typeof(X))]              -> recorded as type-only
    [HarmonyPatch(typeof(X), nameof(X.Y), MethodType.Getter)]
    [HarmonyPatch("Awaken.TG.Foo.Bar", "Method")]
    AccessTools.Method(typeof(X), "Y") or AccessTools.Method(typeof(X), nameof(...))
    AccessTools.Field(typeof(X), "Y")
    AccessTools.PropertyGetter(typeof(X), "Y") and AccessTools.PropertySetter(...)

  This is a regex pass over the source. It is *not* a real C# parser, and a
  small fraction of patterns (multi-line attributes spread across `[`...`]`
  with line breaks) may be missed. Anything weird is flagged as a TODO
  entry in the JSON's _todo field, and we try to log to stderr too.

  Output: tools/symbol-anchors.json (committable; sorted for stable diffs).

.EXAMPLE
  pwsh -File tools/extract-anchors.ps1
#>

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$OutFile  = (Join-Path $PSScriptRoot 'symbol-anchors.json')
)

$ErrorActionPreference = 'Stop'

# --- Configuration ----------------------------------------------------------

# Known mod / dev-tool projects. We discover *.csproj automatically, but
# anything under a `*-ref/` folder is gitignored decompilation, not source.
$skipDirRegexes = @(
    '\\bin\\',
    '\\obj\\',
    '\\\.claude\\',
    '\\\.git\\',
    '\\refs\\',
    '\\assets\\',
    '\\dist\\'
)

function Should-Skip([string]$path) {
    foreach ($r in $skipDirRegexes) {
        if ($path -match $r) { return $true }
    }
    return $false
}

# --- Project discovery ------------------------------------------------------

$projects = @{}
Get-ChildItem -Path $RepoRoot -Recurse -Filter '*.csproj' -File | Where-Object {
    -not (Should-Skip $_.FullName)
} | ForEach-Object {
    $modName = Split-Path $_.Directory -Leaf
    $projects[$modName] = $_.Directory.FullName
}

if ($projects.Count -eq 0) {
    Write-Error "No mod projects found under $RepoRoot"
    exit 1
}

Write-Verbose "Scanning $($projects.Count) project(s):"
foreach ($k in $projects.Keys | Sort-Object) {
    Write-Verbose "  $k -> $($projects[$k])"
}

# --- Regexes ----------------------------------------------------------------

# typeof(X) inside an attribute argument; X may be dotted (Awaken.TG.Foo.Bar)
$reTypeOf  = '(?<typeRef>[A-Za-z_][\w\.]*)'
$reLitName = '"(?<member>[A-Za-z_][\w]*)"'
# nameof(...) — we strip the surrounding type prefix and keep the last identifier.
$reNameOf  = 'nameof\(\s*(?<nameofArg>[A-Za-z_][\w\.]*)\s*\)'

# [HarmonyPatch(typeof(X), <member>)]   member is "Foo" or nameof(...)
$harmonyTypeMember = [regex]::new(
    '\[(?:HarmonyLib\.)?HarmonyPatch\s*\(\s*typeof\(\s*' + $reTypeOf + '\s*\)\s*,\s*(?:' +
    $reLitName + '|' + $reNameOf + ')',
    [System.Text.RegularExpressions.RegexOptions]::Compiled)

# [HarmonyPatch(typeof(X))]
$harmonyTypeOnly = [regex]::new(
    '\[(?:HarmonyLib\.)?HarmonyPatch\s*\(\s*typeof\(\s*' + $reTypeOf + '\s*\)\s*\)',
    [System.Text.RegularExpressions.RegexOptions]::Compiled)

# [HarmonyPatch("Awaken.TG.Foo.Bar", "Member")]
$harmonyStringNs = [regex]::new(
    '\[(?:HarmonyLib\.)?HarmonyPatch\s*\(\s*"(?<typeRef>[A-Za-z_][\w\.]*)"\s*,\s*"(?<member>[A-Za-z_][\w]*)"',
    [System.Text.RegularExpressions.RegexOptions]::Compiled)

# AccessTools.Method/Field/PropertyGetter/PropertySetter(typeof(X), "Y" | nameof(...))
$accessTools = [regex]::new(
    'AccessTools\.(?<kind>Method|Field|PropertyGetter|PropertySetter)\s*\(\s*typeof\(\s*' + $reTypeOf +
    '\s*\)\s*,\s*(?:' + $reLitName + '|' + $reNameOf + ')',
    [System.Text.RegularExpressions.RegexOptions]::Compiled)

# AccessTools.Method/Field/PropertyGetter(t, "Y") or (..., "Y") where the type token
# is a local var. We pair this with a nearby AccessTools.TypeByName("Awaken.TG.X")
# so the full type ref still ends up in the inventory.
$accessToolsByVar = [regex]::new(
    'AccessTools\.(?<kind>Method|Field|PropertyGetter|PropertySetter)\s*\(\s*[A-Za-z_][\w]*\s*,\s*' +
    $reLitName,
    [System.Text.RegularExpressions.RegexOptions]::Compiled)

$typeByName = [regex]::new(
    'AccessTools\.TypeByName\s*\(\s*"(?<typeRef>[A-Za-z_][\w\.]*)"\s*\)',
    [System.Text.RegularExpressions.RegexOptions]::Compiled)

# Detector: things that *look* like a HarmonyPatch but didn't match the above.
$harmonySniff = [regex]::new('\[(?:HarmonyLib\.)?HarmonyPatch\b',
    [System.Text.RegularExpressions.RegexOptions]::Compiled)

# --- Helpers ----------------------------------------------------------------

function NameOf-Last([string]$nameofArg) {
    if ([string]::IsNullOrEmpty($nameofArg)) { return $null }
    $parts = $nameofArg -split '\.'
    return $parts[$parts.Length - 1]
}

function Member-FromMatch([System.Text.RegularExpressions.Match]$m) {
    $member = $m.Groups['member'].Value
    if (-not [string]::IsNullOrEmpty($member)) { return $member }
    $nameofArg = $m.Groups['nameofArg'].Value
    if (-not [string]::IsNullOrEmpty($nameofArg)) { return (NameOf-Last $nameofArg) }
    return $null
}

# --- Scan -------------------------------------------------------------------

$anchors  = New-Object System.Collections.Generic.List[object]
$todos    = New-Object System.Collections.Generic.List[string]
$totalCs  = 0

foreach ($mod in $projects.Keys | Sort-Object) {
    $projDir = $projects[$mod]
    $files   = Get-ChildItem -Path $projDir -Recurse -Filter '*.cs' -File | Where-Object {
        -not (Should-Skip $_.FullName)
    }

    foreach ($f in $files) {
        $totalCs++
        # Use $rel so we get a stable repo-relative path (forward slashes for diff sanity).
        $rel = $f.FullName.Substring($RepoRoot.Length).TrimStart('\','/').Replace('\','/')
        $lines = [System.IO.File]::ReadAllLines($f.FullName)

        # Pre-pass: index AccessTools.TypeByName("X") so we can resolve the
        # AccessTools.Method(t, "Y") pattern where the type comes from a local.
        # SortedList[int,string] can't be New-Object'd in 5.1 (the `,` inside
        # the type spec confuses the parser); use a hashtable keyed by line index.
        $typeByNameAt = @{}
        for ($pi = 0; $pi -lt $lines.Length; $pi++) {
            $tnm = $typeByName.Match($lines[$pi])
            if ($tnm.Success) {
                $typeByNameAt[$pi] = $tnm.Groups['typeRef'].Value
            }
        }

        for ($i = 0; $i -lt $lines.Length; $i++) {
            $line = $lines[$i]
            $lineNo = $i + 1

            # Quick reject: nothing of interest on this line.
            if (($line -notmatch 'HarmonyPatch') -and ($line -notmatch 'AccessTools\.')) { continue }

            # ---- HarmonyPatch typeof(X), member ------------------------------
            $m = $harmonyTypeMember.Match($line)
            if ($m.Success) {
                $typeRef = $m.Groups['typeRef'].Value
                $member  = Member-FromMatch $m
                $anchors.Add([pscustomobject]@{
                    mod    = $mod
                    file   = $rel
                    line   = $lineNo
                    type   = $typeRef
                    member = $member
                    kind   = 'method'
                })
                continue
            }

            # ---- HarmonyPatch("Awaken.TG.Foo", "Member") --------------------
            $m = $harmonyStringNs.Match($line)
            if ($m.Success) {
                $anchors.Add([pscustomobject]@{
                    mod    = $mod
                    file   = $rel
                    line   = $lineNo
                    type   = $m.Groups['typeRef'].Value
                    member = $m.Groups['member'].Value
                    kind   = 'method'
                })
                continue
            }

            # ---- HarmonyPatch(typeof(X)) — type-only -----------------------
            $m = $harmonyTypeOnly.Match($line)
            if ($m.Success) {
                $anchors.Add([pscustomobject]@{
                    mod    = $mod
                    file   = $rel
                    line   = $lineNo
                    type   = $m.Groups['typeRef'].Value
                    member = $null
                    kind   = 'type'
                })
                continue
            }

            # ---- AccessTools.Method/Field/PropertyGetter/PropertySetter ----
            $m = $accessTools.Match($line)
            if ($m.Success) {
                $atKind = $m.Groups['kind'].Value
                $kind   = if ($atKind -eq 'Field') { 'field' } else { 'method' }
                $anchors.Add([pscustomobject]@{
                    mod    = $mod
                    file   = $rel
                    line   = $lineNo
                    type   = $m.Groups['typeRef'].Value
                    member = (Member-FromMatch $m)
                    kind   = $kind
                })
                continue
            }

            # ---- AccessTools.Method(t, "Y") where t came from TypeByName --
            $m = $accessToolsByVar.Match($line)
            if ($m.Success) {
                # Find the most recent TypeByName above us in the same file.
                $resolvedType = $null
                foreach ($k in ($typeByNameAt.Keys | Sort-Object -Descending)) {
                    if ($k -lt $i) { $resolvedType = $typeByNameAt[$k]; break }
                }
                if ($resolvedType) {
                    $atKind = $m.Groups['kind'].Value
                    $kind   = if ($atKind -eq 'Field') { 'field' } else { 'method' }
                    $anchors.Add([pscustomobject]@{
                        mod    = $mod
                        file   = $rel
                        line   = $lineNo
                        type   = $resolvedType
                        member = $m.Groups['member'].Value
                        kind   = $kind
                    })
                } else {
                    $todos.Add("$($rel):$lineNo  AccessTools call w/ var-typed type, no nearby TypeByName: $($line.Trim())")
                }
                continue
            }

            # ---- Sniffed but unrecognised HarmonyPatch line ----------------
            if ($harmonySniff.IsMatch($line)) {
                # Skip the bare `[HarmonyPatch]` (used with TargetMethod()) — it's
                # legitimate but resolves at runtime, so we have nothing to verify.
                if ($line -match '\[\s*HarmonyPatch\s*\]') { continue }
                $todos.Add("$rel`:$lineNo  $($line.Trim())")
            }
        }
    }
}

# --- Sort + emit ------------------------------------------------------------

# Sort by mod, file, line for deterministic output.
$sorted = $anchors |
    Sort-Object -Property @{Expression = 'mod'},
                          @{Expression = 'file'},
                          @{Expression = 'line'},
                          @{Expression = 'type'},
                          @{Expression = 'member'}

# Build the JSON payload manually so we can put a TODO header at the top.
# (System.Text.Json doesn't accept comments, but we emit a `_todo` key for
# anything the regex pass couldn't recognise — JSON-friendly.)
$payload = [ordered]@{
    _comment    = "Auto-generated by tools/extract-anchors.ps1. Sorted for diffability. Edit the script, not this file."
    _todo       = @($todos)
    anchorCount = $sorted.Count
    anchors     = @($sorted)
}

$json = $payload | ConvertTo-Json -Depth 6
# ConvertTo-Json on PowerShell 5.1 escapes Unicode like < — we don't have
# any non-ASCII content, so the output stays clean. Trailing newline for git.
[System.IO.File]::WriteAllText($OutFile, $json + "`n", (New-Object System.Text.UTF8Encoding $false))

Write-Host "Scanned $totalCs source file(s) across $($projects.Count) project(s)."
Write-Host "Wrote $($sorted.Count) anchor(s) to $OutFile"
if ($todos.Count -gt 0) {
    Write-Warning "$($todos.Count) line(s) sniffed as HarmonyPatch but not recognised - see _todo in JSON."
}
