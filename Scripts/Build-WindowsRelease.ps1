[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0",
    [string]$OutputRoot = "",
    [switch]$BuildInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    return [System.IO.Path]::GetFullPath($PathValue)
}

$repoRoot = Resolve-FullPath (Join-Path $PSScriptRoot "..\..")
$projectPath = Join-Path $repoRoot "Windows\Petrichor.App\Petrichor.App.csproj"
$installerScriptPath = Join-Path $repoRoot "Windows\installer\Petrichor.iss"

if (-not (Test-Path -LiteralPath $projectPath))
{
    throw "Could not find project: $projectPath"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $OutputRoot = Join-Path $repoRoot "Windows\dist\release"
}

$resolvedOutputRoot = Resolve-FullPath $OutputRoot
$publishDir = Resolve-FullPath (Join-Path $resolvedOutputRoot "petrichor-$Version-$Runtime")
$portableZipPath = Resolve-FullPath (Join-Path $resolvedOutputRoot "Petrichor-$Version-$Runtime-portable.zip")
$installerOutputDir = Resolve-FullPath (Join-Path $resolvedOutputRoot "installer")

if (-not $publishDir.StartsWith($resolvedOutputRoot, [StringComparison]::OrdinalIgnoreCase))
{
    throw "Publish output path is outside of output root. PublishDir=$publishDir"
}

New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null

if (Test-Path -LiteralPath $publishDir)
{
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

Write-Host "Publishing Petrichor..."
Write-Host "  Project: $projectPath"
Write-Host "  Runtime: $Runtime"
Write-Host "  Version: $Version"
Write-Host "  Output : $publishDir"

$publishArgs = @(
    "publish"
    $projectPath
    "-c"
    $Configuration
    "-r"
    $Runtime
    "--self-contained"
    "true"
    "/p:PublishSingleFile=true"
    "/p:IncludeNativeLibrariesForSelfExtract=true"
    "/p:EnableCompressionInSingleFile=true"
    "/p:PublishReadyToRun=true"
    "/p:DebugType=None"
    "/p:DebugSymbols=false"
    "/p:Version=$Version"
    "-o"
    $publishDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed."
}

if (Test-Path -LiteralPath $portableZipPath)
{
    Remove-Item -LiteralPath $portableZipPath -Force
}

Write-Host "Creating portable zip..."
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $portableZipPath -CompressionLevel Optimal -Force

Write-Host "Portable package ready: $portableZipPath"

if ($BuildInstaller)
{
    if (-not (Test-Path -LiteralPath $installerScriptPath))
    {
        throw "Could not find installer script: $installerScriptPath"
    }

    $isccPath = $env:INNO_SETUP_COMPILER
    if ([string]::IsNullOrWhiteSpace($isccPath))
    {
        $defaultIsccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
        if (Test-Path -LiteralPath $defaultIsccPath)
        {
            $isccPath = $defaultIsccPath
        }
    }

    if ([string]::IsNullOrWhiteSpace($isccPath) -or -not (Test-Path -LiteralPath $isccPath))
    {
        throw "Inno Setup compiler not found. Install Inno Setup 6 or set INNO_SETUP_COMPILER."
    }

    New-Item -ItemType Directory -Path $installerOutputDir -Force | Out-Null

    Write-Host "Building installer with Inno Setup..."
    & $isccPath "/DReleaseDir=$publishDir" "/DOutputDir=$installerOutputDir" "/DAppVersion=$Version" $installerScriptPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Inno Setup build failed."
    }

    Write-Host "Installer output: $installerOutputDir"
}

Write-Host "Done."
