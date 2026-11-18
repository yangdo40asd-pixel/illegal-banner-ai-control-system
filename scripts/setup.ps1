param([string]$Python = '', [switch]$SkipPython)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipPython) {
    if (-not $Python) { $Python = (& py -3.13 -c 'import sys; print(sys.executable)') }
    if (-not $Python) { throw 'Python 3.13 is required for the captured environment.' }
    & $Python -m venv (Join-Path $projectRoot '.venv')
    if ($LASTEXITCODE -ne 0) { throw 'Python venv creation failed' }
    $interpreter = Join-Path $projectRoot '.venv/Scripts/python.exe'
    & $interpreter -m pip install -r (Join-Path $projectRoot 'src/backend/requirements.lock.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Python dependencies installation failed' }
    $local = Join-Path $projectRoot 'configs/paths.local.json'
    $settings = if (Test-Path -LiteralPath $local) { Get-Content -LiteralPath $local -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
    $settings | Add-Member -NotePropertyName PythonExecutable -NotePropertyValue $interpreter -Force
    $settings | ConvertTo-Json | Set-Content -LiteralPath $local -Encoding UTF8
}
& (Join-Path $PSScriptRoot 'build.ps1')
