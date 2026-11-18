param([switch]$ServerOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$settings = Get-Content -LiteralPath (Join-Path $projectRoot 'configs/paths.example.json') -Raw | ConvertFrom-Json
$local = Join-Path $projectRoot 'configs/paths.local.json'
if (Test-Path -LiteralPath $local) {
    $overrides = Get-Content -LiteralPath $local -Raw | ConvertFrom-Json
    foreach ($property in $overrides.PSObject.Properties) { $settings | Add-Member -NotePropertyName $property.Name -NotePropertyValue $property.Value -Force }
}
$env:BANNERGUARD_ROOT = $projectRoot
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
if ($ServerOnly) {
    $interpreter = $env:BANNERGUARD_PYTHON_EXECUTABLE
    if (-not $interpreter) { $interpreter = $settings.PythonExecutable }
    if (-not $interpreter) { $interpreter = Join-Path $projectRoot '.venv/Scripts/python.exe' }
    if (-not (Test-Path -LiteralPath $interpreter)) { throw 'Configure PythonExecutable or run scripts/setup.ps1 first.' }
    & $interpreter -u (Join-Path $projectRoot $settings.BackendEntry)
    exit $LASTEXITCODE
}
$exe = Join-Path $projectRoot 'src/desktop/BannerGuardSystem/bin/Release/net9.0-windows/BannerGuardSystem.exe'
if (-not (Test-Path -LiteralPath $exe)) { & (Join-Path $PSScriptRoot 'build.ps1') }
Start-Process -FilePath $exe -WorkingDirectory $projectRoot -WindowStyle Normal
