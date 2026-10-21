# Captures the WPF dashboard on screens of several sizes (docs/design/any-screen-size.md) into artifacts\screen-sizes,
# with isolated settings: the default arrangement, Edit layout with and without its list, Settings, and big cards
# (Focus, Quick Launch, Weather and Now Playing) to check that content follows the card size.
# Builds first with scripts/build.ps1. Does not alter personal app settings or audio.
param([string]$AppDirectory = 'artifacts\app', [string]$Sizes = '1920x720,2560x720,1920x1080,1280x800,1024x600')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path (Join-Path $projectRoot $AppDirectory) 'WidgetScreen.exe'
$captureRoot = Join-Path $projectRoot 'artifacts\screen-sizes'
New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null
function Place([string]$id, [int]$x, [int]$y, [int]$w, [int]$h, [bool]$visible) {
    '<WidgetPlacement><Id>' + $id + '</Id><X>' + $x + '</X><Y>' + $y + '</Y><Width>' + $w + '</Width><Height>' + $h + '</Height><Visible>' + $visible.ToString().ToLower() + '</Visible></WidgetPlacement>'
}
$bigCards = '<WidgetLayout>' + (Place 'Focus' 0 0 560 576 $true) + (Place 'Shortcuts' 576 0 640 360 $true) + (Place 'Weather' 576 376 640 200 $true) + (Place 'Media' 1232 0 624 576 $true) +
    (Place 'Mixer' 0 0 380 420 $false) + (Place 'System' 0 0 380 310 $false) + (Place 'Spotify' 0 0 560 500 $false) + (Place 'Soundboard' 0 0 360 240 $false) + '</WidgetLayout>'
$views = @(
    @{ Name = 'dashboard'; Arguments = @(); Extra = '' },
    @{ Name = 'edit'; Arguments = @('--render-edit'); Extra = '' },
    @{ Name = 'edit-list'; Arguments = @('--render-edit', '--render-edit-panel'); Extra = '' },
    @{ Name = 'settings'; Arguments = @('--render-settings', '--render-tab', '1'); Extra = '' },
    @{ Name = 'cards'; Arguments = @(); Extra = $bigCards }
)
foreach ($size in $Sizes.Split(',')) {
    foreach ($view in $views) {
        $name = $size + '-' + $view.Name
        $settingsPath = Join-Path $captureRoot ($name + '.xml')
        $imagePath = Join-Path $captureRoot ($name + '.png')
        [System.IO.File]::WriteAllText($settingsPath, '<AppSettings><WeatherLocation></WeatherLocation>' + $view.Extra + '</AppSettings>')
        $arguments = @('--render', ('"{0}"' -f $imagePath), '--render-size', $size, '--settings', ('"{0}"' -f $settingsPath)) + $view.Arguments
        $process = Start-Process -FilePath $appPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(20000)) { throw "Render timed out for $name, process $($process.Id)." }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $imagePath)) { throw "Render failed for $name." }
        Write-Output "Captured $imagePath"
    }
}
