# One-time setup for a fresh clone: points git at the shared hooks in scripts/.githooks.
# Usage:  pwsh -File ./scripts/setup.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    git config core.hooksPath scripts/.githooks
    if ($LASTEXITCODE -ne 0) { throw 'git config core.hooksPath failed.' }

    Write-Host 'Git hooks enabled from scripts/.githooks.'
    Write-Host 'Disable the format check for this clone with: git config glq.hooks.format false'
}
finally {
    Pop-Location
}
