#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the self-contained Windows package that is uploaded to the Releases tab.

.DESCRIPTION
    The version is read from src/App/Pso2ShapeStudio.App.csproj so the assembly,
    the archive name, and the git tag cannot drift apart. Tests run before the
    package is produced. Output lands in dist/, which is not tracked by git.

.EXAMPLE
    ./publish.ps1

.EXAMPLE
    ./publish.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\App\Pso2ShapeStudio.App.csproj'
$solution = Join-Path $repoRoot 'Pso2ShapeStudio.sln'
$distRoot = Join-Path $repoRoot 'dist'
$publishDir = Join-Path $distRoot "publish\$Runtime"

function Invoke-Dotnet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$amlProject = Join-Path $repoRoot 'external\PSO2-Aqua-Library\AquaModelLibrary.Data\AquaModelLibrary.Data.csproj'
if (-not (Test-Path -Path $amlProject)) {
    throw 'The PSO2-Aqua-Library submodule is not initialised. Run: git submodule update --init --recursive external/PSO2-Aqua-Library'
}

$version = ([xml](Get-Content -Path $appProject -Raw)).Project.PropertyGroup |
    Where-Object { $_.Version } |
    Select-Object -First 1 -ExpandProperty Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "No <Version> element found in $appProject."
}

# A package built from a dirty tree cannot be reproduced from its tag later.
try {
    $pending = & git -C $repoRoot status --porcelain
    if ($LASTEXITCODE -eq 0 -and $pending) {
        Write-Warning 'The working tree has uncommitted changes, so this package will not match any tag.'
    }
} catch {
    Write-Warning 'Could not read the git status; skipping the clean-tree check.'
}

Write-Host "Packaging PSO2 Shape Studio $version ($Configuration / $Runtime)" -ForegroundColor Cyan

if ($SkipTests) {
    Write-Warning 'Tests were skipped at the caller''s request.'
} else {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    Invoke-Dotnet @('test', $solution, '-c', $Configuration, '-p:Platform=x64', '--nologo')
}

if (Test-Path -Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

Write-Host 'Publishing...' -ForegroundColor Cyan
Invoke-Dotnet @(
    'publish', $appProject,
    '-c', $Configuration,
    '-r', $Runtime,
    '-p:Platform=x64',
    '--self-contained', 'true',
    '-o', $publishDir,
    '--nologo')

# Skia and HarfBuzz ship ~100 MB of native symbols that no end user needs.
Get-ChildItem -Path $publishDir -Recurse -Filter '*.pdb' | Remove-Item -Force

# The licence and the three readmes sit beside the executable in the package.
foreach ($document in @('LICENSE', 'README.md', 'README.ko.md', 'README.ja.md')) {
    Copy-Item -Path (Join-Path $repoRoot $document) -Destination $publishDir -Force
}

$archivePath = Join-Path $distRoot "PSO2-Shape-Studio-v$version-$Runtime.zip"
if (Test-Path -Path $archivePath) {
    Remove-Item -Path $archivePath -Force
}

# The published files sit at the archive root; earlier releases have no
# wrapper directory and unpacking one over another must keep working.
Write-Host 'Creating the archive...' -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $archivePath -CompressionLevel Optimal

$archive = Get-Item -Path $archivePath
$hash = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash

Write-Host ''
Write-Host "Package : $($archive.FullName)" -ForegroundColor Green
Write-Host "Size    : $([math]::Round($archive.Length / 1MB, 1)) MB"
Write-Host "SHA-256 : $hash"
Write-Host ''
Write-Host 'Next steps:'
Write-Host "  git tag v$version"
Write-Host "  git push origin v$version"
Write-Host "  gh release create v$version `"$($archive.FullName)`" --title `"PSO2 Shape Studio $version`" --notes-file <release notes>"
