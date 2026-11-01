# Renders the cards in their Small, Medium and Large designs (docs/design/widget-size-views.md) at 1920 × 720 with
# isolated settings, into artifacts\size-designs\. Readings are live: this PC's stats, audio and media, and Open-Meteo's
# weather for London, so a render without network shows the offline state instead. Spotify and Discord aren't
# connected in isolated settings; tests\Spotify\SpotifyTests.cs renders Spotify's Large design with labelled fixtures.
param([string]$AppDirectory = (Join-Path $PSScriptRoot '..\artifacts\app'), [int]$Wait = 30, [string[]]$Only = @())
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$output = Join-Path $root 'artifacts\size-designs'
New-Item -ItemType Directory -Force $output | Out-Null
$exe = Join-Path (Resolve-Path $AppDirectory) 'WidgetScreen.exe'

# Each scene: the widgets shown and their bounds on the 1856 × 576 canvas (x, y, width, height); the rest are hidden.
$scenes = [ordered]@{
    'small'         = @{ System = @(0, 0, 320, 576); Weather = @(336, 0, 300, 184) }
    'medium'        = @{ System = @(0, 0, 472, 310); Weather = @(488, 0, 472, 250) }
    'large'         = @{ System = @(0, 0, 640, 576); Weather = @(656, 0, 520, 576) }
    'wide'          = @{ System = @(0, 0, 1200, 420); Weather = @(1216, 0, 640, 420) }
    'cards-small'   = @{ Focus = @(0, 0, 300, 240); Media = @(316, 0, 360, 240); Shortcuts = @(692, 0, 300, 220); Mixer = @(1008, 0, 360, 260); Spotify = @(1384, 0, 472, 440) }
    'cards-medium'  = @{ Focus = @(0, 0, 320, 400); Media = @(336, 0, 472, 310); Shortcuts = @(336, 326, 472, 250); Mixer = @(824, 0, 456, 360); Spotify = @(1296, 0, 560, 576) }
    'cards-large'   = @{ Focus = @(0, 0, 340, 576); Media = @(356, 0, 480, 576); Shortcuts = @(852, 0, 520, 400); Mixer = @(1388, 0, 468, 576) }
    'cards-wide'    = @{ Mixer = @(0, 0, 820, 576); Shortcuts = @(836, 0, 1020, 400) }
    'storage-small' = @{ Storage = @(0, 0, 300, 184); Weather = @(0, 200, 300, 184); System = @(316, 0, 472, 250) }
    'storage-medium' = @{ Storage = @(0, 0, 472, 250); Focus = @(488, 0, 320, 576) }
    'storage-large' = @{ Storage = @(0, 0, 640, 576); Focus = @(656, 0, 320, 576) }
    'storage-wide'  = @{ Storage = @(0, 0, 1200, 400) }
    'clocks'        = @{ Clocks = @(0, 0, 300, 184); Weather = @(0, 200, 472, 250); System = @(316, 0, 472, 184) }
    'clocks-sizes'  = @{ Clocks = @(0, 0, 472, 250); Storage = @(488, 0, 1368, 576) }
    'clocks-large'  = @{ Clocks = @(0, 0, 1000, 340); Focus = @(1016, 0, 320, 576) }
    'clocks-tall'   = @{ Clocks = @(0, 0, 472, 576); Focus = @(488, 0, 320, 576) }
    'prayer'        = @{ Prayer = @(0, 0, 300, 184); Clocks = @(0, 200, 472, 250); Weather = @(488, 0, 472, 250) }
    'prayer-medium' = @{ Prayer = @(0, 0, 472, 250); Focus = @(488, 0, 320, 576) }
    'prayer-large'  = @{ Prayer = @(0, 0, 472, 576); Focus = @(488, 0, 320, 576) }
    'prayer-wide'   = @{ Prayer = @(0, 0, 1200, 420) }
}
foreach ($name in $scenes.Keys) {
    if ($Only.Count -gt 0 -and $Only -notcontains $name) { continue }
    $scene = $scenes[$name]
    $settings = Join-Path $output ($name + '-settings.xml')
    $placements = foreach ($id in 'Focus', 'Media', 'Shortcuts', 'Mixer', 'System', 'Weather', 'Spotify', 'Soundboard', 'Storage', 'Clocks', 'Prayer') {
        $b = if ($scene.Contains($id)) { $scene[$id] } else { @(0, 0, 320, 300) }
        $visible = if ($scene.Contains($id)) { 'true' } else { 'false' }
        "<WidgetPlacement><Id>$id</Id><X>$($b[0])</X><Y>$($b[1])</Y><Width>$($b[2])</Width><Height>$($b[3])</Height><Visible>$visible</Visible></WidgetPlacement>"
    }
    @"
<?xml version="1.0" encoding="utf-8"?>
<AppSettings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <WeatherLocation>London, England, United Kingdom</WeatherLocation><Latitude>51.50853</Latitude><Longitude>-0.12574</Longitude><WeatherUnits>metric</WeatherUnits>
  <HintDismissed>true</HintDismissed>
  <PrayerLocation>Riyadh, Riyadh Region, Saudi Arabia</PrayerLocation><PrayerLatitude>24.6877</PrayerLatitude><PrayerLongitude>46.7219</PrayerLongitude><PrayerMethod>Makkah</PrayerMethod>
  <WorldClocks><WorldClock><Zone>Tokyo Standard Time</Zone><Label>Tokyo</Label></WorldClock><WorldClock><Zone>GMT Standard Time</Zone><Label>London</Label></WorldClock><WorldClock><Zone>Pacific Standard Time</Zone><Label>San Francisco</Label></WorldClock></WorldClocks>
  <WidgetLayout>$($placements -join '')</WidgetLayout>
</AppSettings>
"@ | Set-Content -Encoding UTF8 $settings
    $png = Join-Path $output ($name + '.png')
    $process = Start-Process -FilePath $exe -ArgumentList @('--settings', "`"$settings`"", '--render', "`"$png`"", '--render-wait', $Wait) -PassThru -Wait
    if ($process.ExitCode -ne 0 -or -not (Test-Path $png)) { throw "Render failed: $name" }
    Write-Output "Rendered $png"
}
