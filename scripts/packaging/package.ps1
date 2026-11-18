param([string]$Destination = '')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $Destination) { $Destination = Join-Path $projectRoot 'runtime/dist/BannerGuardSystem' }
$target = [IO.Path]::GetFullPath($Destination)
if (-not $target.StartsWith((Join-Path $projectRoot 'runtime') + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Package destination must be inside runtime.' }
if (Test-Path -LiteralPath $target) { throw 'Destination exists; choose a new directory rather than overwrite.' }
& dotnet publish (Join-Path $projectRoot 'src/desktop/BannerGuardSystem/BannerGuardSystem.csproj') -c Release --no-self-contained -o $target
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
foreach ($directory in @('configs','database','models','data/samples','src/backend/app')) {
    $output = Join-Path $target $directory
    New-Item -ItemType Directory -Force -Path (Split-Path $output -Parent) | Out-Null
    if ($directory -eq 'configs') {
        New-Item -ItemType Directory -Force -Path $output | Out-Null
        $settings = Get-Content -LiteralPath (Join-Path $projectRoot 'configs/paths.example.json') -Raw | ConvertFrom-Json
        $settings.RuntimeDirectory = '%LOCALAPPDATA%/BannerGuard'
        $settings.DatabasePath = '%LOCALAPPDATA%/BannerGuard/db/banner_tracking.db'
        $settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'paths.example.json') -Encoding UTF8
    }
    elseif ($directory -eq 'src/backend/app') {
        $pythonSource = Join-Path $projectRoot $directory
        foreach ($file in (Get-ChildItem -LiteralPath $pythonSource -Recurse -File -Filter '*.py')) {
            $relative = $file.FullName.Substring($pythonSource.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
            $pythonTarget = Join-Path $output $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $pythonTarget -Parent) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $pythonTarget
        }
    }
    else {
        & robocopy (Join-Path $projectRoot $directory) $output /E /XD __pycache__ /R:1 /W:1 /NFL /NDL /NP /NJH /NJS
        if ($LASTEXITCODE -ge 8) { throw "Copy failed: $directory" }
    }
}
Copy-Item -LiteralPath (Join-Path $projectRoot '.bannerguard-root') -Destination $target
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start.bat') -Destination $target
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $target
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $target
Copy-Item -LiteralPath (Join-Path $projectRoot 'src/backend/requirements.lock.txt') -Destination (Join-Path $target 'src/backend')
# Publish includes desktop poster images. Use the public synthetic image in the
# generated package; keep the application's original source posters untouched.
$sample = Join-Path $projectRoot 'data/samples/vlm/synthetic_demo.jpg'
foreach ($poster in (Get-ChildItem -LiteralPath (Join-Path $target 'Assets') -File -Filter '*.jpg')) {
    Copy-Item -LiteralPath $sample -Destination $poster.FullName -Force
}
if (Get-ChildItem -LiteralPath $target -Recurse -File -Filter '*.db') { throw 'Private database found in package' }
if (Test-Path -LiteralPath (Join-Path $target 'configs/paths.local.json')) { throw 'Local settings found in package' }
Write-Output "Packaged at $target. Requires .NET 9 Desktop Runtime and a configured Python AI environment."
$global:LASTEXITCODE = 0
