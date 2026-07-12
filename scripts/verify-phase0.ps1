[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Failures = [System.Collections.Generic.List[string]]::new()
$script:Checks = 0

function Test-Condition {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    $script:Checks++
    if (-not $Condition) {
        $script:Failures.Add($Message)
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

$requiredFiles = @(
    'README.md', 'WHOLEPLAN.md', 'ROADMAP.md', 'PHASE_STATUS.md',
    'CHANGELOG.md', 'CONTRIBUTING.md', 'SECURITY.md', 'PRIVACY.md',
    'CODE_OF_CONDUCT.md', 'LICENSE', '.gitignore', '.gitattributes',
    '.editorconfig', 'docs/EXECUTIVE_BRIEF.md', 'docs/ASSUMPTIONS.md',
    'docs/PRODUCT_SPEC.md', 'docs/ARCHITECTURE.md', 'docs/WORKFLOWS.md',
    'docs/TECH_STACK.md', 'docs/SAFETY_MODEL.md', 'docs/THREAT_MODEL.md',
    'docs/PRIVILEGE_MODEL.md', 'docs/SCANNING_ENGINE.md',
    'docs/RULE_ENGINE.md', 'docs/DATA_MODEL.md', 'docs/UI_UX.md',
    'docs/TESTING_STRATEGY.md', 'docs/RELEASE_AND_SIGNING.md',
    'docs/PERFORMANCE_BUDGETS.md', 'docs/ERROR_HANDLING.md',
    'docs/EXPORT_FORMAT.md', 'docs/STORAGE_ACCOUNTING.md',
    'docs/SUPPORT_MATRIX.md', 'docs/CAPABILITY_MATRIX.md',
    'docs/UX_STATE_MACHINE.md', 'docs/SECURE_IPC.md',
    'docs/UPDATE_SECURITY.md', 'docs/RISK_REGISTER.md',
    'docs/DECISION_LOG.md', 'docs/RESEARCH_NOTES.md',
    'docs/OPERATIONS.md', 'docs/GLOSSARY.md',
    'docs/PHASE_0_CHECKLIST.md', 'docs/GITHUB_PROJECT.md',
    'docs/adr/0001-native-windows-stack.md',
    'docs/adr/0002-separate-elevated-helper.md',
    'docs/adr/0003-dry-run-default.md',
    'docs/adr/0004-versioned-rule-packs.md',
    'docs/adr/0005-no-cloud-or-llm-dependency.md',
    'rules/schemas/rule.schema.json',
    'rules/schemas/export-report.schema.json',
    'rules/examples/npm-cache.valid.yaml',
    'rules/examples/path-traversal.invalid.yaml',
    'rules/examples/shell-command.invalid.yaml',
    'rules/examples/summary-report.valid.json',
    '.github/ISSUE_TEMPLATE/bug.yml',
    '.github/ISSUE_TEMPLATE/false-positive.yml',
    '.github/ISSUE_TEMPLATE/new-rule.yml',
    '.github/ISSUE_TEMPLATE/security.yml',
    '.github/ISSUE_TEMPLATE/feature.yml',
    '.github/pull_request_template.md', '.github/CODEOWNERS',
    '.github/dependabot.yml', '.github/workflows/phase0-validation.yml'
)

foreach ($path in $requiredFiles) {
    $exists = Test-Path -LiteralPath $path -PathType Leaf
    Test-Condition $exists "Required file is missing: $path"
    if ($exists) {
        Test-Condition ((Get-Item -LiteralPath $path).Length -gt 0) "Required file is empty: $path"
    }
}

$utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$textFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -File |
    Where-Object {
        $_.FullName -notlike "$repoRoot\.git\*" -and
        $_.Extension -in @('.md', '.json', '.yaml', '.yml', '.mmd', '.ps1') -and
        $_.FullName -ne $PSCommandPath
    }

foreach ($file in $textFiles) {
    try {
        $content = $utf8.GetString([System.IO.File]::ReadAllBytes($file.FullName))
        Test-Condition (-not $content.Contains([char]0)) "NUL character found: $($file.FullName)"
        Test-Condition (-not $content.Contains([char]0xFFFD)) "Replacement character found: $($file.FullName)"
        Test-Condition ($content.EndsWith("`n")) "Missing final newline: $($file.FullName)"
    }
    catch {
        $script:Failures.Add("Invalid UTF-8: $($file.FullName): $($_.Exception.Message)")
    }
}

$jsonFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter '*.json' |
    Where-Object { $_.FullName -notlike "$repoRoot\.git\*" }
foreach ($file in $jsonFiles) {
    try {
        $null = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        Test-Condition $true "JSON parsed: $($file.FullName)"
    }
    catch {
        $script:Failures.Add("Invalid JSON: $($file.FullName): $($_.Exception.Message)")
    }
}

$ruleSchema = Get-Content -LiteralPath 'rules/schemas/rule.schema.json' -Raw -Encoding UTF8 | ConvertFrom-Json
$exportSchema = Get-Content -LiteralPath 'rules/schemas/export-report.schema.json' -Raw -Encoding UTF8 | ConvertFrom-Json
Test-Condition ($ruleSchema.'$schema' -eq 'https://json-schema.org/draft/2020-12/schema') 'Rule schema is not Draft 2020-12.'
Test-Condition ($exportSchema.'$schema' -eq 'https://json-schema.org/draft/2020-12/schema') 'Export schema is not Draft 2020-12.'

$ruleSchemaText = Get-Content -LiteralPath 'rules/schemas/rule.schema.json' -Raw -Encoding UTF8
Test-Condition ($ruleSchemaText -match '"report-only"') 'Rule schema does not permit report-only.'
Test-Condition ($ruleSchemaText -notmatch 'trusted-tool-command|recycle-files|quarantine-files|permanent-delete') 'Phase 0 rule schema exposes a future or prohibited action.'
Test-Condition ($ruleSchemaText -match '"additionalProperties"\s*:\s*false') 'Rule schema does not reject unknown fields.'

$validRule = Get-Content -LiteralPath 'rules/examples/npm-cache.valid.yaml' -Raw -Encoding UTF8
$invalidTraversal = Get-Content -LiteralPath 'rules/examples/path-traversal.invalid.yaml' -Raw -Encoding UTF8
$invalidCommand = Get-Content -LiteralPath 'rules/examples/shell-command.invalid.yaml' -Raw -Encoding UTF8
Test-Condition ($validRule -match '(?m)^\s*type:\s*report-only\s*$') 'Valid rule is not report-only.'
Test-Condition ($validRule -match '(?m)^\s*followReparsePoints:\s*false\s*$') 'Valid rule does not disable reparse traversal.'
Test-Condition ($validRule -notmatch '(?im)^\s*(command|executable|script|shell)\s*:') 'Valid rule contains executable data.'
Test-Condition ($invalidTraversal -match '\.\.') 'Traversal-negative fixture no longer contains traversal input.'
Test-Condition ($invalidCommand -match '(?im)^\s*command\s*:') 'Command-negative fixture no longer contains a forbidden command.'

$allowedMermaidHeaders = @('flowchart ', 'graph ', 'stateDiagram-v2', 'sequenceDiagram', 'classDiagram', 'erDiagram')
$mermaidFiles = Get-ChildItem -LiteralPath 'docs/diagrams' -File -Filter '*.mmd'
Test-Condition ($mermaidFiles.Count -ge 9) 'Fewer than nine authoritative Mermaid sources exist.'
foreach ($file in $mermaidFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    $first = ($content -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1).Trim()
    $known = $false
    foreach ($header in $allowedMermaidHeaders) {
        if ($first.StartsWith($header, [System.StringComparison]::Ordinal)) { $known = $true }
    }
    Test-Condition $known "Unrecognized Mermaid header in $($file.Name): $first"
    Test-Condition (-not $content.Contains('```')) "Markdown fence found inside Mermaid source: $($file.Name)"
}

$allText = ($textFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join "`n"
Test-Condition ($allText -notmatch ('(?i)\bspace' + 'trace\b')) 'Legacy product name remains in repository text.'
Test-Condition ($allText -notmatch '(?i)lorem ipsum|insert content here|example\.com|your-email|OWNER/REPO') 'Placeholder or invented repository content remains.'
Test-Condition ($allText -notmatch '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----') 'Private key material detected.'
Test-Condition ($allText -notmatch 'gh[pousr]_[A-Za-z0-9]{30,}|AKIA[0-9A-Z]{16}') 'Credential-shaped value detected.'

$canonicalRisks = @('Informational', 'Low', 'Medium', 'High', 'Prohibited')
foreach ($risk in $canonicalRisks) {
    Test-Condition ($allText.Contains($risk)) "Canonical risk term is absent: $risk"
}

$productCode = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.cs', '.csproj', '.sln', '.props', '.targets') })
Test-Condition ($productCode.Count -eq 0) 'Phase 0 contains .NET product or solution files.'

$forbiddenPatterns = @(
    '(?i)Remove-Item\s+.*-Recurse',
    '(?i)Directory\.Delete\s*\(',
    '(?i)File\.Delete\s*\(',
    '(?i)powershell(?:\.exe)?\s+-command',
    '(?i)Process\.Start\s*\('
)
foreach ($pattern in $forbiddenPatterns) {
    $matches = Get-ChildItem -LiteralPath $repoRoot -Recurse -File |
        Where-Object { $_.FullName -notlike "$repoRoot\.git\*" -and $_.Extension -notin @('.md', '.mmd') } |
        Select-String -Pattern $pattern
    Test-Condition ($null -eq $matches) "Executable destructive or shell pattern found: $pattern"
}

if (Get-Command git -ErrorAction SilentlyContinue) {
    $diffCheck = & git diff --check 2>&1
    Test-Condition ($LASTEXITCODE -eq 0) ("git diff --check failed: " + ($diffCheck -join '; '))
}

if ($script:Failures.Count -gt 0) {
    Write-Host "Phase 0 verification FAILED ($($script:Failures.Count) failures / $($script:Checks) checks)." -ForegroundColor Red
    foreach ($failure in $script:Failures) { Write-Host " - $failure" -ForegroundColor Red }
    exit 1
}

Write-Host "Phase 0 verification PASSED ($($script:Checks) checks; $($requiredFiles.Count) required files; $($mermaidFiles.Count) Mermaid sources; $($jsonFiles.Count) JSON files)." -ForegroundColor Green
Write-Host 'Note: full Draft 2020-12 and Mermaid parser conformance is recorded separately; Phase 1 must pin those validators.'
