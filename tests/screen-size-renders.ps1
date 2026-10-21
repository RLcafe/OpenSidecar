# Captures the WPF dashboard on screens of several sizes (docs/design/any-screen-size.md) into artifacts\screen-sizes,
# with isolated settings: the default arrangement, Edit layout and Settings for each size.
# Builds first with scripts/build.ps1. Does not alter personal app settings or audio.
param([string]$AppDirectory = 'artifacts\app', [string]$Sizes = '1920x720,2560x720,1920x1080,1280x800,1024x600')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path (Join-Path $projectRoot $AppDirectory) 'WidgetScreen.exe'
$captureRoot = Join-Path $projectRoot 'artifacts\screen-sizes'
New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null
$views = @(
    @{ Name = 'dashboard'; Arguments = @() },
    @{ Name = 'edit'; Arguments = @('--render-edit') },
    @{ Name = 'edit-list'; Arguments = @('--render-edit', '--render-edit-panel') },
    @{ Name = 'settings'; Arguments = @('--render-settings', '--render-tab', '1') }
)
foreach ($size in $Sizes.Split(',')) {
    foreach ($view in $views) {
        $name = $size + '-' + $view.Name
        $settingsPath = Join-Path $captureRoot ($name + '.xml')
        $imagePath = Join-Path $captureRoot ($name + '.png')
        [System.IO.File]::WriteAllText($settingsPath, '<AppSettings><WeatherLocation></WeatherLocation></AppSettings>')
        $arguments = @('--render', ('"{0}"' -f $imagePath), '--render-size', $size, '--settings', ('"{0}"' -f $settingsPath)) + $view.Arguments
        $process = Start-Process -FilePath $appPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(20000)) { throw "Render timed out for $name, process $($process.Id)." }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $imagePath)) { throw "Render failed for $name." }
        Write-Output "Captured $imagePath"
    }
}
