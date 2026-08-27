#!/usr/bin/env pwsh
# Build the packable SCIM projects in Release and push the resulting
# .nupkg files to the Anacle NuGet source (assumed already configured).

[CmdletBinding()]
param(
    [string]$Source = "Anacle",
    [string]$Configuration = "Release",
    [switch]$Upload
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$projects = @(
    "Microsoft.SystemForCrossDomainIdentityManagement/Microsoft.SCIM.Core.csproj",
    "Microsoft.SCIM.AspNet/Microsoft.SCIM.AspNet.csproj",
    "Microsoft.SCIM.AspNetCore/Microsoft.SCIM.AspNetCore.csproj"
)

# 1. Build in Release (GeneratePackageOnBuild produces the .nupkg files).
foreach ($project in $projects) {
    $path = Join-Path $root $project
    Write-Host "Building $project ($Configuration)..." -ForegroundColor Cyan
    dotnet build $path -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $project" }
}

# 2. Push every produced package to the Anacle source (only when -Upload).
if (-not $Upload) {
    Write-Host "Build complete. Skipping push (pass -Upload to push to $Source)." -ForegroundColor Yellow
    return
}

$packages = foreach ($project in $projects) {
    $dir = Join-Path (Split-Path (Join-Path $root $project)) "bin/$Configuration"
    if (Test-Path $dir) {
        Get-ChildItem -Path $dir -Filter *.nupkg -Recurse
    }
}

if (-not $packages) { throw "No .nupkg files found under bin/$Configuration" }

foreach ($package in ($packages | Sort-Object FullName -Unique)) {
    Write-Host "Pushing $($package.Name) to $Source..." -ForegroundColor Cyan
    dotnet nuget push $package.FullName --source $Source --skip-duplicate
    if ($LASTEXITCODE -ne 0) { throw "Push failed for $($package.Name)" }
}

Write-Host "Done." -ForegroundColor Green
