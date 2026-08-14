#Requires -Version 5.1
<#
.SYNOPSIS
    Removes generated build, test, QA, and unpacked publish output.

.DESCRIPTION
    Deletes bin/ and obj/ directories, the local artifacts/ workspace, and
    unpacked directories under dist/. Release ZIP files in dist/ are kept.
    Every resolved target must remain inside this repository.

.EXAMPLE
    ./clean-generated.ps1

.EXAMPLE
    ./clean-generated.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$repoPrefix = $repoRoot + '\'
$candidates = [System.Collections.Generic.List[string]]::new()

$artifactsPath = Join-Path $repoRoot 'artifacts'
if (Test-Path -LiteralPath $artifactsPath) {
    $candidates.Add($artifactsPath)
}

$distPath = Join-Path $repoRoot 'dist'
if (Test-Path -LiteralPath $distPath) {
    Get-ChildItem -LiteralPath $distPath -Directory -Force |
        ForEach-Object { $candidates.Add($_.FullName) }
}

Get-ChildItem -LiteralPath $repoRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('bin', 'obj') } |
    ForEach-Object { $candidates.Add($_.FullName) }

# Keep only the highest directory when an entire subtree is already selected.
$targets = [System.Collections.Generic.List[string]]::new()
foreach ($candidate in ($candidates | Sort-Object Length, @{ Expression = { $_ } } -Unique)) {
    $fullPath = [System.IO.Path]::GetFullPath($candidate).TrimEnd('\')
    if (-not $fullPath.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Cleanup target escaped the repository: $fullPath"
    }

    $coveredByParent = $false
    foreach ($parent in $targets) {
        if ($fullPath.StartsWith($parent + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            $coveredByParent = $true
            break
        }
    }

    if (-not $coveredByParent) {
        $targets.Add($fullPath)
    }
}

$bytes = 0L
foreach ($target in $targets) {
    $bytes += [long]((Get-ChildItem -LiteralPath $target -File -Recurse -Force `
        -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum)
}

Write-Host ("Generated output selected: {0:N2} GiB in {1} directories." -f `
    ($bytes / 1GB), $targets.Count) -ForegroundColor Cyan

foreach ($target in $targets) {
    if ($PSCmdlet.ShouldProcess($target, 'Remove generated directory')) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

if (-not $WhatIfPreference) {
    Write-Host ("Freed {0:N2} GiB. Release ZIP files were kept." -f ($bytes / 1GB)) `
        -ForegroundColor Green
}
