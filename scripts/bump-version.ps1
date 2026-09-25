[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('major', 'minor')]
    [string]$Part = 'minor'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-GitAvailability {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "Git is required but was not found on PATH."
    }
}

Assert-GitAvailability

$currentDirectory = Get-Location
$versionPath = Join-Path $currentDirectory 'version.json'
if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
    throw "version.json was not found in the current directory: $($currentDirectory.Path)"
}

$currentBranch = git rev-parse --abbrev-ref HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($currentBranch)) {
    throw "Unable to determine the current branch."
}

if ($currentBranch -ne 'develop') {
    throw "The version bump script must be run on the 'develop' branch. Current branch: $currentBranch"
}

git fetch origin develop --quiet 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Unable to fetch origin/develop. Ensure the repository is connected to origin."
}

$syncStatus = git rev-list --left-right --count origin/develop...HEAD 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Unable to compare the local 'develop' branch to origin/develop."
}

# git's --left-right --count separates the two counts with whitespace whose exact form
# (space vs tab) varies by git version/platform, so split on any whitespace rather than
# matching a literal '0 0' - a literal match silently fails a fully-synced branch.
$syncParts = $syncStatus.Trim() -split '\s+'
if ($syncParts.Count -ne 2 -or $syncParts[0] -ne '0' -or $syncParts[1] -ne '0') {
    throw "The local 'develop' branch is not synced with origin/develop. Sync status: $syncStatus"
}

$pendingChanges = git status --porcelain
if (-not [string]::IsNullOrWhiteSpace($pendingChanges)) {
    throw "There are pending changes. Commit or stash them before bumping the version."
}

try {
    $versionDocument = Get-Content -LiteralPath $versionPath -Raw | ConvertFrom-Json -ErrorAction Stop
}
catch {
    throw "Failed to parse version.json as valid JSON."
}

if (-not ($versionDocument.PSObject.Properties.Name -contains 'version')) {
    throw "version.json does not contain a 'version' property in the Nerdbank.GitVersioning format."
}

$versionValue = [string]$versionDocument.version
if ([string]::IsNullOrWhiteSpace($versionValue)) {
    throw "The version field in version.json is empty."
}

$parts = $versionValue.Split('.')
if ($parts.Length -lt 2) {
    throw "The version value '$versionValue' is not in the expected 'major.minor' format."
}

$major = 0
$minor = 0
if (-not [int]::TryParse($parts[0], [ref]$major) -or -not [int]::TryParse($parts[1], [ref]$minor)) {
    throw "The version value '$versionValue' is not in the expected 'major.minor' format."
}

switch ($Part) {
    'major' {
        $major += 1
        $minor = 0
    }
    'minor' {
        $minor += 1
    }
    default {
        throw "Unsupported version part '$Part'."
    }
}

$newVersion = "$major.$minor"
$versionDocument.version = $newVersion
$versionDocument | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $versionPath -Encoding utf8

git add -- version.json
if ($LASTEXITCODE -ne 0) {
    throw "Unable to stage version.json for commit."
}

git commit -m "version bumped to $newVersion"
if ($LASTEXITCODE -ne 0) {
    throw "The version bump commit failed."
}

Write-Host "Version bumped to $newVersion and committed."
