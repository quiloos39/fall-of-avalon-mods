<#
.SYNOPSIS
  Verify the symbol anchors written by extract-anchors.ps1 against the
  decompiled `*-ref/` source trees in this repo.

.DESCRIPTION
  For each anchor in tools/symbol-anchors.json:
    1. Resolve the type ref. Fully-qualified (Awaken.TG.Foo.Bar) -> direct file
       lookup; short name -> grep across all `*-ref/` trees for `class|struct|
       interface|enum X`.
    2. Find the named member declaration inside the type's source file(s).
       Match by name only - don't try to match overload signatures.
    3. Emit a per-anchor result:
         OK   <mod>  <type>.<member>          (verbose only)
         WARN <mod>  <type>.<member>: reason  (yellow - ambiguous etc.)
         FAIL <mod>  <type>.<member>: reason  (red - genuine miss)

  Exit 0 if every anchor is OK or WARN, exit 1 if any FAIL. Suitable for CI.

.PARAMETER Json
  Emit a single machine-readable JSON object instead of the coloured report.

.EXAMPLE
  pwsh -File tools/verify-anchors.ps1
  pwsh -File tools/verify-anchors.ps1 -Verbose
  pwsh -File tools/verify-anchors.ps1 -Json | ConvertFrom-Json
#>

[CmdletBinding()]
param(
    [string]$RepoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$AnchorFile = (Join-Path $PSScriptRoot 'symbol-anchors.json'),
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

# --- Load anchors -----------------------------------------------------------

if (-not (Test-Path -LiteralPath $AnchorFile)) {
    Write-Error "Anchor file not found: $AnchorFile`nRun extract-anchors.ps1 first."
    exit 2
}

$payload = Get-Content -LiteralPath $AnchorFile -Raw | ConvertFrom-Json
$anchors = @($payload.anchors)
if ($anchors.Count -eq 0) {
    Write-Error "No anchors in $AnchorFile"
    exit 2
}

# --- Discover ref trees -----------------------------------------------------

$refsRoot = Join-Path $RepoRoot 'refs'
$refRoots = @()
if (Test-Path $refsRoot) {
    $refRoots = Get-ChildItem -Path $refsRoot -Directory -Recurse -Depth 1 |
        Where-Object { $_.Parent.Name -in @('game', 'mods') } |
        ForEach-Object { $_.FullName }
}

if ($refRoots.Count -eq 0) {
    Write-Error "No subfolders under $refsRoot/{game,mods}/. Decompile first - see tools/README.md."
    exit 2
}

# --- Helpers ----------------------------------------------------------------

# Cache per-type lookup so we don't re-grep the same short name N times.
$typeCache = @{}

function Find-TypeFiles([string]$typeRef) {
    if ($typeCache.ContainsKey($typeRef)) { return $typeCache[$typeRef] }

    $found = New-Object System.Collections.Generic.List[string]

    if ($typeRef -match '\.') {
        # Fully-qualified. Last segment is the type name.
        $idx = $typeRef.LastIndexOf('.')
        $ns  = $typeRef.Substring(0, $idx)
        $tn  = $typeRef.Substring($idx + 1)
        # First try the canonical `<ref>/<namespace>/<TypeName>.cs` layout.
        foreach ($r in $refRoots) {
            $direct = Join-Path (Join-Path $r $ns) ($tn + '.cs')
            if (Test-Path -LiteralPath $direct) {
                [void]$found.Add($direct)
            }
        }
        # Fall back to filename-only search if not found - some files were
        # split by ilspy on partial classes.
        if ($found.Count -eq 0) {
            foreach ($r in $refRoots) {
                Get-ChildItem -Path $r -Recurse -Filter ($tn + '.cs') -File -ErrorAction SilentlyContinue |
                    ForEach-Object {
                        if (Confirm-DeclaresType $_.FullName $tn) { [void]$found.Add($_.FullName) }
                    }
            }
        }
    } else {
        # Short name. Grep all ref roots for `class X` / `struct X` / `interface X` / `enum X`.
        foreach ($r in $refRoots) {
            Get-ChildItem -Path $r -Recurse -Filter ($typeRef + '.cs') -File -ErrorAction SilentlyContinue |
                ForEach-Object {
                    if (Confirm-DeclaresType $_.FullName $typeRef) { [void]$found.Add($_.FullName) }
                }
        }
        # Also pick up types whose declaration lives in a differently-named file (rare in
        # ilspy `-p` output, but partial classes happen). Only do this if the simple
        # filename-based search came up empty, to keep the common case fast.
        if ($found.Count -eq 0) {
            $needle = "(class|struct|interface|enum)\s+$([Regex]::Escape($typeRef))(\b|<)"
            foreach ($r in $refRoots) {
                Get-ChildItem -Path $r -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
                    ForEach-Object {
                        $hit = Select-String -Path $_.FullName -Pattern $needle -CaseSensitive -Quiet -ErrorAction SilentlyContinue
                        if ($hit) { [void]$found.Add($_.FullName) }
                    }
            }
        }
    }

    $result = @($found | Sort-Object -Unique)
    $typeCache[$typeRef] = $result
    return $result
}

function Confirm-DeclaresType([string]$path, [string]$typeName) {
    $needle = "(class|struct|interface|enum)\s+$([Regex]::Escape($typeName))(\b|<)"
    return Select-String -Path $path -Pattern $needle -CaseSensitive -Quiet -ErrorAction SilentlyContinue
}

function Find-MemberDeclarations([string[]]$files, [string]$memberName, [string]$kind) {
    $hits = New-Object System.Collections.Generic.List[object]
    if (-not $files -or $files.Count -eq 0) { return ,$hits }

    $escaped = [Regex]::Escape($memberName)
    # ilspy `-p` decompiler output formats declarations consistently: the
    # line begins with leading whitespace, then access/type modifiers
    # (public/private/protected/internal/static/...), then optionally a
    # return type, then the member name. We anchor to that shape directly.
    $modifiers = '(?:public|private|protected|internal|static|virtual|override|abstract|sealed|new|async|extern|unsafe|readonly|const|partial|explicit|implicit|volatile|ref)\b'
    $declModifier = '^\s*(?:' + $modifiers + '\s+)+[\s\S]*?(?<![\w\.])' + $escaped + '\s*(?:\(|\{|=>|;|,|=\s)'
    # Also handle decompiled record-like or expression-bodied declarations
    # whose first line happens to omit explicit modifiers, plus nested
    # auto-property `[Foo.Bar.SomeAttr] public ... Name { get; }` that may
    # have an attribute on the same line.
    $declAttrThenModifier = '^\s*\[[^\]]*\]\s*(?:' + $modifiers + '\s+)+[\s\S]*?(?<![\w\.])' + $escaped + '\s*(?:\(|\{|=>|;|,|=\s)'

    foreach ($f in $files) {
        # Read the whole file once - we need it for the leading-modifier
        # anchor (Select-String operates per-line, which is fine).
        $lines = [System.IO.File]::ReadAllLines($f)
        for ($idx = 0; $idx -lt $lines.Length; $idx++) {
            $line = $lines[$idx]
            if ($line -cnotmatch '\b' + $escaped + '\b') { continue }
            $codeOnly = ($line -split '//')[0]
            $isDecl = ($codeOnly -cmatch $declModifier) -or ($codeOnly -cmatch $declAttrThenModifier)
            if (-not $isDecl) { continue }
            [void]$hits.Add([pscustomobject]@{
                file = $f
                line = $idx + 1
                text = $line.Trim()
            })
        }
    }
    return ,$hits
}

# --- Verify -----------------------------------------------------------------

$results = New-Object System.Collections.Generic.List[object]

foreach ($a in $anchors) {
    $status = 'OK'
    $reason = $null
    $files  = Find-TypeFiles $a.type

    if ($files.Count -eq 0) {
        $status = 'FAIL'
        $reason = "type not found"
    } elseif ($files.Count -gt 1 -and -not ($a.type -match '\.')) {
        # Short-name collision across ref trees - we'll still try to verify
        # the member, but warn that the user should disambiguate by adding
        # the full namespace.
        $status = 'WARN'
        $reason = "ambiguous short name: $($files.Count) types match. Consider using the full namespace in the patch attribute."
    }

    if ($a.kind -ne 'type' -and $a.member -and $files.Count -gt 0) {
        $hits = Find-MemberDeclarations $files $a.member $a.kind
        if ($hits.Count -eq 0) {
            $status = 'FAIL'
            $reason = "member '$($a.member)' not found on $($a.type)"
        } elseif ($hits.Count -gt 1 -and $status -eq 'OK') {
            # Multiple matches - probably overloads, we don't try to pick one.
            $status = 'WARN'
            $reason = "$($hits.Count) declarations of '$($a.member)' (overloads or partial-class duplicates) - matched by name only"
        }
    }

    $results.Add([pscustomobject]@{
        mod    = $a.mod
        file   = $a.file
        line   = $a.line
        type   = $a.type
        member = $a.member
        kind   = $a.kind
        status = $status
        reason = $reason
        files  = $files
    })
}

# --- Report -----------------------------------------------------------------

$counts = @{ OK = 0; WARN = 0; FAIL = 0 }
foreach ($r in $results) { $counts[$r.status]++ }

if ($Json) {
    [pscustomobject]@{
        total = $results.Count
        ok    = $counts.OK
        warn  = $counts.WARN
        fail  = $counts.FAIL
        results = $results
    } | ConvertTo-Json -Depth 6
} else {
    foreach ($r in $results) {
        $label = "{0,-4} {1,-22} {2}.{3}" -f $r.status, $r.mod, $r.type, ($(if ($r.member) { $r.member } else { '<type>' }))
        switch ($r.status) {
            'OK' {
                if ($VerbosePreference -ne 'SilentlyContinue') {
                    Write-Host $label -ForegroundColor Green
                }
            }
            'WARN' {
                Write-Host ("{0}: {1}" -f $label, $r.reason) -ForegroundColor Yellow
            }
            'FAIL' {
                Write-Host ("{0}: {1}" -f $label, $r.reason) -ForegroundColor Red
            }
        }
    }
    Write-Host ""
    Write-Host ("Summary: {0} OK, {1} WARN, {2} FAIL (of {3} anchors)" -f `
        $counts.OK, $counts.WARN, $counts.FAIL, $results.Count) -ForegroundColor Cyan
}

if ($counts.FAIL -gt 0) { exit 1 } else { exit 0 }
