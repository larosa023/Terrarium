# Builds Terrarium.exe locally into .\dist (requires the .NET 10 SDK).
#   .\build.ps1                 # version from Directory.Build.props
#   .\build.ps1 -Version 0.2.0  # override
#   .\build.ps1 -Run            # build, then launch it
param(
    [string]$Version,
    [switch]$Run
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host "Running core tests..." -ForegroundColor Cyan
dotnet run --project tests/Terrarium.Core.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

Write-Host "Publishing Terrarium.exe..." -ForegroundColor Cyan
$publishArgs = @('publish', 'src/Terrarium.App/Terrarium.App.csproj', '-c', 'Release', '-o', 'dist')
if ($Version) { $publishArgs += "-p:Version=$Version" }
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$exe = Join-Path $PSScriptRoot 'dist\Terrarium.exe'
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Built $exe ($size MB)" -ForegroundColor Green

if ($Run) { Start-Process $exe }
