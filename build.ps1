#Requires -Version 5.1
<#
.SYNOPSIS
  Build AnarlogTrigger and produce MSI installers.

.EXAMPLE
  .\build.ps1
  Builds Release x64 + arm64 MSIs (sequentially) and copies them to dist\.

.EXAMPLE
  .\build.ps1 -Architecture x64
  Builds only the x64 MSI.

.EXAMPLE
  .\build.ps1 -Configuration Debug -SkipDist
  Debug build without copying to dist\.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('all', 'x64', 'arm64')]
    [string] $Architecture = 'all',

    [switch] $Clean,
    [switch] $SkipDist
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$DistDir = Join-Path $Root 'dist'
$X64Project = Join-Path $Root 'src\AnarlogTrigger.Installer\AnarlogTrigger.Installer.x64.wixproj'
$Arm64Project = Join-Path $Root 'src\AnarlogTrigger.Installer\AnarlogTrigger.Installer.arm64.wixproj'

function Invoke-DotNet {
    param([string[]] $CommandArgs)
    Write-Host ">> dotnet $($CommandArgs -join ' ')" -ForegroundColor Cyan
    & dotnet @CommandArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE"
    }
}

function Build-Installer {
    param(
        [string] $ProjectPath,
        [string] $Configuration
    )
    # Single-threaded MSBuild avoids obj/ publish races (Defender + parallel publish).
    Invoke-DotNet @('build', $ProjectPath, '-c', $Configuration, '-m:1')
}

Push-Location $Root
try {
    if ($Clean) {
        Write-Host 'Cleaning...' -ForegroundColor Yellow
        Invoke-DotNet @('clean', $X64Project, '-c', $Configuration)
        Invoke-DotNet @('clean', $Arm64Project, '-c', $Configuration)
        Invoke-DotNet @('clean', (Join-Path $Root 'src\AnarlogTrigger\AnarlogTrigger.csproj'), '-c', $Configuration)
    }

    switch ($Architecture) {
        'all' {
            # Build installers sequentially — do NOT dotnet build the slnx (parallel app + publish collides).
            Build-Installer -ProjectPath $X64Project -Configuration $Configuration
            Build-Installer -ProjectPath $Arm64Project -Configuration $Configuration
            $Artifacts = @(
                (Join-Path $Root "src\AnarlogTrigger.Installer\bin\x64\$Configuration\AnarlogTrigger-x64.msi"),
                (Join-Path $Root "src\AnarlogTrigger.Installer\bin\arm64\$Configuration\AnarlogTrigger-arm64.msi")
            )
        }
        'x64' {
            Build-Installer -ProjectPath $X64Project -Configuration $Configuration
            $Artifacts = @(
                (Join-Path $Root "src\AnarlogTrigger.Installer\bin\x64\$Configuration\AnarlogTrigger-x64.msi")
            )
        }
        'arm64' {
            Build-Installer -ProjectPath $Arm64Project -Configuration $Configuration
            $Artifacts = @(
                (Join-Path $Root "src\AnarlogTrigger.Installer\bin\arm64\$Configuration\AnarlogTrigger-arm64.msi")
            )
        }
    }

    if (-not $SkipDist) {
        New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
        foreach ($msi in $Artifacts) {
            if (-not (Test-Path $msi)) {
                throw "Expected MSI not found: $msi"
            }
            $dest = Join-Path $DistDir (Split-Path $msi -Leaf)
            Copy-Item -Path $msi -Destination $dest -Force
            Write-Host "Copied: $dest" -ForegroundColor Green
        }
    }

    Write-Host ''
    Write-Host 'Build succeeded.' -ForegroundColor Green
    foreach ($msi in $Artifacts) {
        if (Test-Path $msi) {
            $item = Get-Item $msi
            Write-Host "  $($item.FullName)  ($([math]::Round($item.Length / 1MB, 1)) MB, $($item.LastWriteTime))"
        }
    }
    if (-not $SkipDist -and (Test-Path $DistDir)) {
        Write-Host "  dist: $DistDir"
    }
}
finally {
    Pop-Location
}
