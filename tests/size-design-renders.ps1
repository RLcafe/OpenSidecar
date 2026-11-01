# Renders the cards in their Small, Medium and Large designs (docs/design/widget-size-views.md) at 1920 × 720 with
# isolated settings, into artifacts\size-designs\. Readings are live: this PC's stats, audio and media, and Open-Meteo's
# weather for London, so a render without network shows the offline state instead. The claude-* scenes use the card's
# labelled fixture. Spotify and Discord aren't
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
    'notes'         = @{ Notes = @(0, 0, 472, 310); Prayer = @(488, 0, 300, 200) }
    'notes-large'   = @{ Notes = @(0, 0, 1000, 420) }
    # Claude Code with its labelled fixture (--render-claude-sample): "Sample" shows in the card's heading.
    'claude-small'  = @{ Claude = @(0, 0, 300, 200); Focus = @(316, 0, 320, 576) }
    'claude-medium' = @{ Claude = @(0, 0, 472, 310); Focus = @(488, 0, 320, 576) }
    'claude-large'  = @{ Claude = @(0, 0, 1216, 576); Focus = @(1232, 0, 320, 576) }
    'claude-large-narrow' = @{ Claude = @(0, 0, 900, 576); Focus = @(916, 0, 320, 576) }
    'claude-large-min' = @{ Claude = @(0, 0, 640, 420) }
    'claude-wide'   = @{ Claude = @(0, 0, 1856, 576) }
    'claude-needs'  = @{ Claude = @(0, 0, 472, 310); Storage = @(488, 0, 300, 220) }
    'claude-needs-small' = @{ Claude = @(0, 0, 300, 200); Storage = @(316, 0, 300, 220) }
    'claude-needs-large' = @{ Claude = @(0, 0, 1216, 576) }
    # The Claude card on a second page: the header's pill says a session needs you.
    'claude-pill'   = @{ Focus = @(0, 0, 320, 576); Media = @(336, 0, 472, 310) }
}
foreach ($name in $scenes.Keys) {
    if ($Only.Count -gt 0 -and $Only -notcontains $name) { continue }
    $scene = $scenes[$name]
    $settings = Join-Path $output ($name + '-settings.xml')
    $placements = foreach ($id in 'Focus', 'Media', 'Shortcuts', 'Mixer', 'System', 'Weather', 'Spotify', 'Soundboard', 'Storage', 'Clocks', 'Prayer', 'Notes', 'Claude') {
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
  <WidgetLayout>$($placements -join '')</WidgetLayout>$(if ($name -eq 'claude-pill') { '<MorePages><LayoutPage><Name>Claude</Name><WidgetLayout><WidgetPlacement><Id>Claude</Id><X>0</X><Y>0</Y><Width>472</Width><Height>310</Height><Visible>true</Visible></WidgetPlacement></WidgetLayout></LayoutPage></MorePages>' })
</AppSettings>
"@ | Set-Content -Encoding UTF8 $settings
    # A labelled fixture for the Notes card: made-up notes in the scene's own notes.xml.
    $notesFile = Join-Path $output 'notes.xml'
    '<?xml version="1.0"?><NotesData><Text>TEST FIXTURE: example notes. Call about the order on Tuesday.</Text><Items><TodoItem><Id>a1</Id><Text>Example: renew the domain</Text><Done>true</Done></TodoItem><TodoItem><Id>a2</Id><Text>Example: back up the photos</Text><Done>false</Done></TodoItem><TodoItem><Id>a3</Id><Text>Example: book a haircut</Text><Done>false</Done></TodoItem></Items></NotesData>' | Set-Content -Encoding UTF8 $notesFile
    $png = Join-Path $output ($name + '.png')
    $arguments = @('--settings', "`"$settings`"", '--render', "`"$png`"", '--render-wait', $Wait)
    if ($name -like 'claude-*') { $arguments += '--render-claude-sample'; if ($name -like 'claude-needs*' -or $name -eq 'claude-pill') { $arguments += '--render-claude-needs' } }
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -Wait
    if ($process.ExitCode -ne 0 -or -not (Test-Path $png)) { throw "Render failed: $name" }
    Write-Output "Rendered $png"
}
