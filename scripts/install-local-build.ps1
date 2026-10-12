#!/usr/bin/env pwsh
#
# install-local-build - pack the current source and (re)install it as a global .NET tool
# entirely from the local ./release folder. Nothing is fetched from or pushed to nuget.org.
#
# Repo-agnostic: copy this file verbatim into scripts/ of any grdev CLI. It discovers the
# tool project by finding the single <PackAsTool>true</PackAsTool> .csproj under src/, reads
# its <PackageId> / <ToolCommandName>, and drives pack + tool (un)install around a temporary
# local NuGet source. No per-repo edits required.
#
# Steps:
#   1. delete every *.nupkg already in ./release
#   2. dotnet pack the tool project in Release (output lands in ./release)
#   3. register ./release as a temporary NuGet source
#   4. uninstall the currently installed global copy of the tool, if any
#   5. install the exact version just packed, resolved from ./release
#   6. remove the temporary NuGet source (always, even if a step above failed)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$srcDir     = Join-Path $repoRoot 'src'
$releaseDir = Join-Path $repoRoot 'release'
$sourceName = 'install-local-build'

function Invoke-Checked {
    param([Parameter(Mandatory)][string]$What, [Parameter(Mandatory)][scriptblock]$Do)
    & $Do
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit $LASTEXITCODE)" }
}

# --- discover the tool project -------------------------------------------------

if (-not (Test-Path $srcDir)) { throw "No src/ folder at $repoRoot" }

$toolProject = Get-ChildItem -Path $srcDir -Recurse -Filter *.csproj |
    Where-Object { (Get-Content $_.FullName -Raw) -match '<PackAsTool>\s*true\s*</PackAsTool>' } |
    Select-Object -First 1

if (-not $toolProject) {
    throw "No project with <PackAsTool>true</PackAsTool> found under $srcDir"
}

$csproj      = [xml](Get-Content $toolProject.FullName -Raw)
$packageId   = $csproj.SelectNodes('//PackageId')       | ForEach-Object { $_.InnerText.Trim() } | Where-Object { $_ } | Select-Object -First 1
$toolCommand = $csproj.SelectNodes('//ToolCommandName') | ForEach-Object { $_.InnerText.Trim() } | Where-Object { $_ } | Select-Object -First 1
if (-not $packageId) { throw "No <PackageId> in $($toolProject.Name)" }

Write-Host "Tool project : $($toolProject.FullName)"
Write-Host "Package id   : $packageId"
Write-Host "Release dir  : $releaseDir"
Write-Host ''

# --- 1. clean previous packages ---------------------------------------------------

if (Test-Path $releaseDir) {
    Get-ChildItem -Path $releaseDir -Filter '*.nupkg' | ForEach-Object {
        Write-Host "removing $($_.Name)"
        Remove-Item $_.FullName -Force
    }
}

# --- 2. pack the current source -------------------------------------------------

Invoke-Checked 'dotnet pack' { dotnet pack $toolProject.FullName -c Release --nologo }

$package = Get-ChildItem -Path $releaseDir -Filter '*.nupkg' | Select-Object -First 1
if (-not $package) { throw "pack produced no .nupkg in $releaseDir" }

# <PackageId>.<version>.nupkg  ->  version
$version = $package.BaseName.Substring($packageId.Length + 1)
Write-Host ''
Write-Host "packed $($package.Name)  (version $version)"
Write-Host ''

# --- 3. temporary local NuGet source -----------------------------------------------

& dotnet nuget remove source $sourceName 2>$null | Out-Null   # clear a stale one from a failed run
Invoke-Checked 'dotnet nuget add source' { dotnet nuget add source $releaseDir --name $sourceName }

try {
    # --- 4. uninstall the current global copy ------------------------------------
    & dotnet tool uninstall --global $packageId 2>$null | Out-Null

    # --- 5. install the exact version just packed, from ./release ---------------
    # --version pins the local build; --add-source guarantees it resolves here and not from
    # any other configured feed.
    Invoke-Checked 'dotnet tool install' {
        dotnet tool install --global $packageId --version $version --add-source $releaseDir --ignore-failed-sources
    }
}
finally {
    # --- 6. always remove the temporary source ---------------------------------
    & dotnet nuget remove source $sourceName 2>$null | Out-Null
}

Write-Host ''
$runHint = if ($toolCommand) { "Run '$toolCommand' to use it." } else { '' }
Write-Host "installed $packageId $version. $runHint" -ForegroundColor Green
