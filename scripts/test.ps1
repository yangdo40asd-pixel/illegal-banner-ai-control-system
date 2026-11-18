param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    & dotnet test BannerGuardSystem.sln -c Release --logger 'trx;LogFileName=desktop.trx' --results-directory runtime/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Desktop tests failed' }
    & $Python -m pytest tests/backend -q --junitxml=runtime/test-results/backend.xml
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed' }
    & $Python scripts/check_docs.py
    if ($LASTEXITCODE -ne 0) { throw 'Documentation links failed' }
}
finally { Pop-Location }
