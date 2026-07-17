<#
Shared repository text-scanning helper for the verify-phase*.ps1 scripts.

Every safety scan in this repository needs the same two things: search a set of paths for a pattern, and
exclude generated output (bin/obj) plus specific reviewed-boundary exceptions. Historically this was done by
shelling out to `rg` (ripgrep) directly. That silently breaks any Windows PowerShell session that does not have
a `rg.exe` on PATH — which is not guaranteed anywhere the repository documents as a supported environment.

Find-RepositoryPattern prefers, in order:
  1. a repository-pinned ripgrep executable at .tools\ripgrep\rg.exe, if a maintainer has added one;
  2. a `rg` already resolvable on PATH;
  3. a pure PowerShell fallback using Select-String, so the scan always runs with nothing beyond PowerShell
     and .NET itself.

The fallback is intentionally not a "weaker" scan: it honors the same include filter, the same exclude-glob
semantics (directory-prefix and bin/obj exclusion), and the same literal/regex and case-sensitivity behavior as
the ripgrep path, so a forbidden pattern is found (or not) identically regardless of which engine ran.
#>

function Get-RepoRoot {
    Split-Path -Parent $PSScriptRoot | Split-Path -Parent
}

function Get-PinnedOrPathRipgrep {
    $repoRoot = Get-RepoRoot
    $pinned = Join-Path $repoRoot '.tools\ripgrep\rg.exe'
    if (Test-Path -LiteralPath $pinned -PathType Leaf) { return $pinned }
    $onPath = Get-Command rg -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

<#
.SYNOPSIS
Search Paths for Pattern, returning @{ Found = [bool]; Matches = [string[]] }.

.PARAMETER Pattern
A .NET-regex-compatible pattern (or a literal string when -Literal is passed). Every pattern used by the
verify-phase*.ps1 scripts is a simple alternation/character-class expression that both ripgrep and .NET regex
interpret identically.

.PARAMETER Paths
One or more repository-relative roots: directories (searched recursively) or individual files.

.PARAMETER Include
A single filename glob applied to files found under directory roots (default '*', meaning every file).
Individual file paths passed in -Paths are always searched regardless of -Include.

.PARAMETER ExcludeDirs
Repository-relative directory prefixes to exclude entirely (e.g. 'src/Clyr.Core/Execution'). bin/obj/.git/.tools
output directories are always excluded in addition to these.

.PARAMETER Literal
Treat Pattern as a literal substring instead of a regular expression (mirrors `rg -F`).

.PARAMETER CaseInsensitive
Case-insensitive matching (mirrors `rg -i`). Default is case-sensitive, matching ripgrep's default.
#>
function Find-RepositoryPattern {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Pattern,
        [Parameter(Mandatory)] [string[]]$Paths,
        [string]$Include = '*',
        [string[]]$ExcludeDirs = @(),
        [switch]$Literal,
        [switch]$CaseInsensitive
    )

    $repoRoot = Get-RepoRoot
    $tool = Get-PinnedOrPathRipgrep

    if ($tool) {
        $rgArgs = New-Object System.Collections.Generic.List[string]
        $rgArgs.Add('-n')
        if ($Literal) { $rgArgs.Add('-F') }
        if ($CaseInsensitive) { $rgArgs.Add('-i') }
        $rgArgs.Add($Pattern)
        foreach ($p in $Paths) { $rgArgs.Add($p) }
        if ($Include -ne '*') { $rgArgs.Add('--glob'); $rgArgs.Add($Include) }
        $rgArgs.Add('--glob'); $rgArgs.Add('!**/bin/**')
        $rgArgs.Add('--glob'); $rgArgs.Add('!**/obj/**')
        $rgArgs.Add('--glob'); $rgArgs.Add('!**/.git/**')
        $rgArgs.Add('--glob'); $rgArgs.Add('!**/.tools/**')
        foreach ($exclude in $ExcludeDirs) {
            # Cover both an excluded directory's contents and an exact excluded file path.
            $rgArgs.Add('--glob'); $rgArgs.Add("!$exclude/**")
            $rgArgs.Add('--glob'); $rgArgs.Add("!$exclude")
        }

        Push-Location $repoRoot
        try {
            $output = & $tool @rgArgs 2>$null
            $found = ($LASTEXITCODE -eq 0)
            return @{ Found = $found; Matches = @($output) }
        }
        finally { Pop-Location }
    }

    # Pure PowerShell fallback.
    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    foreach ($relativePath in $Paths) {
        $fullPath = Join-Path $repoRoot $relativePath
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            $files.Add((Get-Item -LiteralPath $fullPath))
            continue
        }
        if (Test-Path -LiteralPath $fullPath -PathType Container) {
            $childArgs = @{ LiteralPath = $fullPath; Recurse = $true; File = $true; ErrorAction = 'SilentlyContinue' }
            if ($Include -ne '*') { $childArgs['Filter'] = $Include }
            Get-ChildItem @childArgs | ForEach-Object { $files.Add($_) }
        }
    }

    $normalizedExcludes = $ExcludeDirs | ForEach-Object { ($_ -replace '/', '\').TrimEnd('\') }
    $matchingFiles = $files | Where-Object {
        $relative = $_.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
        $relativeBackslash = $relative -replace '/', '\'
        if ($relativeBackslash -match '(^|\\)(bin|obj|\.git|\.tools)(\\|$)') { return $false }
        foreach ($exclude in $normalizedExcludes) {
            if ($relativeBackslash.StartsWith("$exclude\", [System.StringComparison]::OrdinalIgnoreCase) -or
                $relativeBackslash.Equals($exclude, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
        }
        return $true
    }

    $selectStringArgs = @{ Pattern = $Pattern; CaseSensitive = -not $CaseInsensitive.IsPresent }
    if ($Literal) { $selectStringArgs['SimpleMatch'] = $true }

    # Report paths relative to the repository root, exactly like ripgrep does when run from $repoRoot — this
    # keeps downstream parsing (e.g. "path:line:text") unambiguous, since an absolute Windows path's own drive
    # letter ("D:\...") would otherwise introduce a spurious leading colon.
    $matchLines = New-Object System.Collections.Generic.List[string]
    foreach ($file in $matchingFiles) {
        $relative = $file.FullName.Substring($repoRoot.Length).TrimStart('\', '/') -replace '\\', '/'
        $fileMatches = Select-String -LiteralPath $file.FullName @selectStringArgs -ErrorAction SilentlyContinue
        foreach ($match in $fileMatches) { $matchLines.Add("$($relative):$($match.LineNumber):$($match.Line.Trim())") }
    }
    return @{ Found = ($matchLines.Count -gt 0); Matches = @($matchLines) }
}
