$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
& dotnet build (Join-Path $projectRoot 'BannerGuardSystem.sln') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
