# Renders the Sticker Sheet foundations (docs/design/sticker-sheet.md) at 1920 × 720 with isolated settings, into
# artifacts\sticker-sheet\: the default layout and two pages with the other widgets in Midnight, Slate and Warm, text
# at 140%, Widget colours → Accent only, changed inks, and Edit layout's list with the ink picker. Readings are live
# (this PC's stats, audio and media, Open-Meteo's weather for London); Spotify and Discord aren't connected. Phase 2 adds
# the default five (Focus, Now Playing, Quick Launch, the mixer and Spotify) at their Small, Medium, Large and Wide
# designs in each palette, at 140%, and with nothing playing (--render-no-media). Spotify's Large design with a library
# needs an account; tests\Spotify\SpotifyTests.cs renders it with labelled fixtures.
param([string]$AppDirectory = (Join-Path $PSScriptRoot '..\artifacts\app'), [int]$Wait = 20, [string[]]$Only = @())
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$output = Join-Path $root 'artifacts\sticker-sheet'
New-Item -ItemType Directory -Force $output | Out-Null
$exe = Join-Path (Resolve-Path $AppDirectory) 'WidgetScreen.exe'
$ids = 'Focus', 'Media', 'Shortcuts', 'Mixer', 'System', 'Weather', 'Spotify', 'Soundboard', 'Storage', 'Clocks', 'Prayer', 'Notes', 'Claude', 'Clipboard', 'Macros', 'Calendar', 'Games', 'Home'

# Pages: the widgets shown and their bounds on the 1856 × 576 canvas (x, y, width, height). $null is the default layout.
$pages = [ordered]@{
    'default' = $null
    'readings' = @{ Weather = @(0, 0, 472, 576); System = @(488, 0, 456, 280); Storage = @(488, 296, 456, 280); Prayer = @(960, 0, 420, 280); Clocks = @(960, 296, 420, 280); Calendar = @(1396, 0, 460, 576) }
    'lists' = @{ Notes = @(0, 0, 472, 576); Clipboard = @(488, 0, 420, 280); Macros = @(488, 296, 420, 280); Games = @(924, 0, 456, 280); Home = @(924, 296, 456, 280); Soundboard = @(1396, 0, 460, 300); Claude = @(1396, 316, 460, 260) }
    'narrow' = @{ Clocks = @(0, 0, 196, 184); Storage = @(212, 0, 240, 152); Calendar = @(468, 0, 240, 152); Prayer = @(724, 0, 240, 152); Focus = @(980, 0, 280, 192); Media = @(1276, 0, 312, 200); Weather = @(1604, 0, 252, 300) }
    # Phase 2, the default five at their Small, Medium, Large and Wide designs.
    'five-small' = @{ Focus = @(0, 0, 280, 192); Media = @(296, 0, 312, 200); Shortcuts = @(624, 0, 288, 204); Mixer = @(928, 0, 320, 224); Spotify = @(1264, 0, 400, 300) }
    'five-medium' = @{ Focus = @(0, 0, 320, 420); Media = @(336, 0, 472, 310); Shortcuts = @(336, 326, 472, 250); Mixer = @(824, 0, 456, 380); Spotify = @(1296, 0, 560, 576) }
    'five-large' = @{ Focus = @(0, 0, 340, 576); Media = @(356, 0, 480, 576); Mixer = @(852, 0, 600, 576); Shortcuts = @(1468, 0, 388, 576) }
    'five-wide' = @{ Mixer = @(0, 0, 820, 420); Shortcuts = @(836, 0, 1020, 400) }
    # Phase 3, the readings (System stats, Weather, Storage, World clocks, Prayer) at their Small, Medium and Large designs.
    'read-small' = @{ Weather = @(0, 0, 320, 200); System = @(336, 0, 300, 200); Storage = @(652, 0, 300, 180); Clocks = @(968, 0, 300, 180); Prayer = @(1284, 0, 300, 180) }
    'read-medium' = @{ Weather = @(0, 0, 472, 250); System = @(488, 0, 456, 280); Storage = @(960, 0, 456, 250); Clocks = @(0, 296, 472, 250); Prayer = @(488, 296, 456, 280); }
    'read-large' = @{ Weather = @(0, 0, 560, 576); System = @(576, 0, 640, 576); Storage = @(1232, 0, 624, 360) }
    'read-large2' = @{ Clocks = @(0, 0, 720, 400); Prayer = @(736, 0, 560, 420); Weather = @(1312, 0, 544, 340) }
}
# Today's focus sessions for the pips and chips: two sessions, 50 minutes.
$sessions = '<FocusSessionDate>' + (Get-Date -Format 'yyyy-MM-dd') + '</FocusSessionDate><FocusSessionsToday>2</FocusSessionsToday><FocusMinutesToday>50</FocusMinutesToday>'
# Scenes: a page with a theme and extra settings.
$scenes = [ordered]@{}
foreach ($theme in 'Midnight', 'Slate', 'Warm') { foreach ($page in 'default', 'readings', 'lists') { $scenes["$($theme.ToLower())-$page"] = @{ Page = $page; Theme = $theme; Extra = '' } } }
$scenes['midnight-default-140'] = @{ Page = 'default'; Theme = 'Midnight'; Extra = '<TextScale>1.4</TextScale>' }
$scenes['midnight-readings-140'] = @{ Page = 'readings'; Theme = 'Midnight'; Extra = '<TextScale>1.4</TextScale>' }
$scenes['midnight-narrow'] = @{ Page = 'narrow'; Theme = 'Midnight'; Extra = '' }
$scenes['slate-default-accent-only'] = @{ Page = 'default'; Theme = 'Slate'; Extra = '<Accent>Glacier</Accent><WidgetColours>Accent</WidgetColours>' }
$scenes['warm-default-changed-inks'] = @{ Page = 'default'; Theme = 'Warm'; Extra = '<WidgetInks>Focus=Lemon;Media=Mint;Mixer=Lavender;Spotify=Rose</WidgetInks>' }
$scenes['midnight-edit-ink-picker'] = @{ Page = 'default'; Theme = 'Midnight'; Extra = ''; Args = @('--render-edit', '--render-edit-panel') }
# Phase 2: the five cards at each size in each palette, at 140%, and their empty states (nothing playing, Spotify not connected).
foreach ($theme in 'Midnight', 'Slate', 'Warm') { foreach ($page in 'five-small', 'five-medium', 'five-large', 'five-wide') { $scenes["$($theme.ToLower())-$page"] = @{ Page = $page; Theme = $theme; Extra = $sessions } } }
foreach ($page in 'five-medium', 'five-large') { $scenes["midnight-$page-140"] = @{ Page = $page; Theme = 'Midnight'; Extra = $sessions + '<TextScale>1.4</TextScale>' } }
$scenes['midnight-default-nothing-playing'] = @{ Page = 'default'; Theme = 'Midnight'; Extra = $sessions; Args = @('--render-no-media') }
# Phase 3: the readings at each size in each palette, and at 140%.
foreach ($theme in 'Midnight', 'Slate', 'Warm') { foreach ($page in 'read-small', 'read-medium', 'read-large', 'read-large2') { $scenes["$($theme.ToLower())-$page"] = @{ Page = $page; Theme = $theme; Extra = '' } } }
foreach ($page in 'read-small', 'read-medium', 'read-large', 'read-large2') { $scenes["midnight-$page-140"] = @{ Page = $page; Theme = 'Midnight'; Extra = '<TextScale>1.4</TextScale>' } }
# A low-space threshold of 20% so this PC's fullest drives show the caution-tape state.
foreach ($page in 'read-small', 'read-medium', 'read-large') { $scenes["midnight-$page-low-space"] = @{ Page = $page; Theme = 'Midnight'; Extra = '<StorageLowPercent>20</StorageLowPercent>' } }
$scenes['midnight-five-large-nothing-playing'] = @{ Page = 'five-large'; Theme = 'Midnight'; Extra = $sessions; Args = @('--render-no-media') }

foreach ($name in $scenes.Keys) {
    if ($Only.Count -gt 0 -and $Only -notcontains $name) { continue }
    $scene = $scenes[$name]; $layout = $pages[$scene.Page]
    $settings = Join-Path $output ($name + '-settings.xml')
    $placements = if ($layout -eq $null) { '' } else {
        $items = foreach ($id in $ids) {
            $b = if ($layout.Contains($id)) { $layout[$id] } else { @(0, 0, 320, 300) }
            $visible = if ($layout.Contains($id)) { 'true' } else { 'false' }
            "<WidgetPlacement><Id>$id</Id><X>$($b[0])</X><Y>$($b[1])</Y><Width>$($b[2])</Width><Height>$($b[3])</Height><Visible>$visible</Visible></WidgetPlacement>"
        }
        '<WidgetLayout>' + ($items -join '') + '</WidgetLayout>'
    }
    @"
<?xml version="1.0" encoding="utf-8"?>
<AppSettings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Theme>$($scene.Theme)</Theme>$($scene.Extra)
  <WeatherLocation>London, England, United Kingdom</WeatherLocation><Latitude>51.50853</Latitude><Longitude>-0.12574</Longitude><WeatherUnits>metric</WeatherUnits>
  <HintDismissed>true</HintDismissed>
  <PrayerLocation>Riyadh, Riyadh Region, Saudi Arabia</PrayerLocation><PrayerLatitude>24.6877</PrayerLatitude><PrayerLongitude>46.7219</PrayerLongitude><PrayerMethod>Makkah</PrayerMethod>
  <WorldClocks><WorldClock><Zone>Tokyo Standard Time</Zone><Label>Tokyo</Label></WorldClock><WorldClock><Zone>GMT Standard Time</Zone><Label>London</Label></WorldClock><WorldClock><Zone>Pacific Standard Time</Zone><Label>San Francisco</Label></WorldClock></WorldClocks>
  $placements
</AppSettings>
"@ | Set-Content -Encoding UTF8 $settings
    $png = Join-Path $output ($name + '.png')
    $arguments = @('--settings', "`"$settings`"", '--render', "`"$png`"", '--render-wait', $Wait) + $(if ($scene.Args) { $scene.Args } else { @() })
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -Wait
    if ($process.ExitCode -ne 0 -or -not (Test-Path $png)) { throw "Render failed: $name" }
    Write-Output "Rendered $png"
}
