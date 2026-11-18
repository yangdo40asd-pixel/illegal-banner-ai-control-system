$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
& python (Join-Path $PSScriptRoot 'verify.py')
exit $LASTEXITCODE
