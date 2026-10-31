# Renders System stats and Weather in their Small, Medium and Large designs (docs/design/widget-size-views.md) at
# 1920 × 720 with isolated settings, into artifacts\size-designs\. Readings are live: this PC's stats and Open-Meteo's
# weather for London, so a render without network shows the offline state instead.
param([string]$AppDirectory = (Join-Path $PSScriptRoot '..\artifacts\app'), [int]$Wait = 30)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$output = Join-Path $root 'artifacts\size-designs'
New-Item -ItemType Directory -Force $output | Out-Null
$exe = Join-Path (Resolve-Path $AppDirectory) 'WidgetScreen.exe'

# Each scene: name, then System and Weather bounds on the 1856 × 576 canvas (x, y, width, height).
$scenes = @(
    @{ Name = 'small';  System = @(0, 0, 320, 576);  Weather = @(336, 0, 300, 184) },
    @{ Name = 'medium'; System = @(0, 0, 472, 310);  Weather = @(488, 0, 472, 250) },
    @{ Name = 'large';  System = @(0, 0, 640, 576);  Weather = @(656, 0, 520, 576) },
    @{ Name = 'wide';   System = @(0, 0, 1200, 420); Weather = @(1216, 0, 640, 420) }
)
foreach ($scene in $scenes) {
    $settings = Join-Path $output ($scene.Name + '-settings.xml')
    $placements = foreach ($id in 'Focus', 'Media', 'Shortcuts', 'Mixer', 'System', 'Weather', 'Spotify', 'Soundboard') {
        $b = if ($id -eq 'System') { $scene.System } elseif ($id -eq 'Weather') { $scene.Weather } else { @(0, 0, 320, 300) }
        $visible = if ($id -eq 'System' -or $id -eq 'Weather') { 'true' } else { 'false' }
        "<WidgetPlacement><Id>$id</Id><X>$($b[0])</X><Y>$($b[1])</Y><Width>$($b[2])</Width><Height>$($b[3])</Height><Visible>$visible</Visible></WidgetPlacement>"
    }
    @"
<?xml version="1.0" encoding="utf-8"?>
<AppSettings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <WeatherLocation>London, England, United Kingdom</WeatherLocation><Latitude>51.50853</Latitude><Longitude>-0.12574</Longitude><WeatherUnits>metric</WeatherUnits>
  <HintDismissed>true</HintDismissed>
  <WidgetLayout>$($placements -join '')</WidgetLayout>
</AppSettings>
"@ | Set-Content -Encoding UTF8 $settings
    $png = Join-Path $output ($scene.Name + '.png')
    $process = Start-Process -FilePath $exe -ArgumentList @('--settings', "`"$settings`"", '--render', "`"$png`"", '--render-wait', $Wait) -PassThru -Wait
    if ($process.ExitCode -ne 0 -or -not (Test-Path $png)) { throw "Render failed: $($scene.Name)" }
    Write-Output "Rendered $png"
}
