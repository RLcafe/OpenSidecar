param([switch]$Windowed, [string]$AppDirectory = 'artifacts\app')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path (Join-Path $projectRoot $AppDirectory) 'WidgetScreen.exe'
if (-not (Test-Path -LiteralPath $appPath)) { & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $AppDirectory }
$launchOptions = @{ FilePath = $appPath; WorkingDirectory = (Split-Path -Parent $appPath) }
if ($Windowed) { $launchOptions.ArgumentList = '--windowed' }
Start-Process @launchOptions
