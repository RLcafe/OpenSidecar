# Captures the actual WPF dashboard at its target resolution using isolated settings.
# Builds first with scripts/build.ps1. Does not alter personal app settings or audio.
param([string]$AppDirectory = 'artifacts\app', [string]$Only = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path (Join-Path $projectRoot $AppDirectory) 'WidgetScreen.exe'
$captureRoot = Join-Path $projectRoot 'artifacts\polish'
New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null
$cases = @(
    @{ Name = 'dashboard'; Scale = '1'; Accent = 'Lime'; Theme = 'Midnight'; Swap = 'false'; Settings = $false },
    @{ Name = 'spotify-large'; Scale = '1.15'; Accent = 'Glacier'; Theme = 'Slate'; Swap = 'false'; Settings = $false },
    @{ Name = 'spotify-settings'; Scale = '1.15'; Accent = 'Glacier'; Theme = 'Slate'; Swap = 'false'; Settings = $true; Tab = 'Spotify' },
    @{ Name = 'large-text'; Scale = '1.15'; Accent = 'Glacier'; Theme = 'Slate'; Swap = 'true'; Settings = $false },
    @{ Name = 'settings'; Scale = '1.15'; Accent = 'Glacier'; Theme = 'Slate'; Swap = 'true'; Settings = $true; Tab = 'Theme' },
    @{ Name = 'display-settings'; Scale = '1.15'; Accent = 'Amber'; Theme = 'Warm'; Swap = 'false'; Settings = $true; Tab = 'Display' },
    @{ Name = 'widget-settings'; Scale = '1.15'; Accent = 'Lime'; Theme = 'Midnight'; Swap = 'false'; Settings = $true; Tab = 'Widgets' },
    @{ Name = 'shortcut-settings'; Scale = '1.15'; Accent = 'Glacier'; Theme = 'Slate'; Swap = 'false'; Settings = $true; Tab = 'Quick Launch' },
    @{ Name = 'weather-settings'; Scale = '1.15'; Accent = 'Glacier'; Theme = 'Slate'; Swap = 'false'; Settings = $true; Tab = 'Weather' },
    @{ Name = 'deck-pages'; Scale = '1.15'; Accent = 'Lime'; Theme = 'Midnight'; Swap = 'false'; Settings = $false; Extra = '<Shortcuts><Shortcut><Label>Music</Label><Target>spotify:</Target></Shortcut><Shortcut><Label>Calculator</Label><Target>%windir%\System32\calc.exe</Target></Shortcut><Shortcut><Label>Notes</Label><Target>%windir%\System32\notepad.exe</Target></Shortcut><Shortcut><Label>Browser</Label><Target>https://example.com</Target></Shortcut><Shortcut><Label>Play / pause</Label><Action>Control</Action><Target>play</Target></Shortcut><Shortcut><Label>Microphone</Label><Action>Control</Action><Target>microphone</Target></Shortcut><Shortcut><Label>Screenshot</Label><Action>Hotkey</Action><Target>Win+Shift+S</Target></Shortcut></Shortcuts>' },
    @{ Name = 'resized'; Scale = '1.15'; Accent = 'Amber'; Theme = 'Warm'; Swap = 'false'; Settings = $false; Extra = '<ClockWidth>10</ClockWidth><MediaWidth>0</MediaWidth><MixerWidth>0</MixerWidth><SystemWidth>0</SystemWidth><ClockHeight>276</ClockHeight><MediaHeight>376</MediaHeight><MixerHeight>476</MixerHeight><SystemHeight>316</SystemHeight><CornerRadius>0</CornerRadius>' },
    @{ Name = 'no-stats'; Scale = '1.15'; Accent = 'Lavender'; Theme = 'Midnight'; Swap = 'true'; Settings = $false; Extra = '<ShowCpu>false</ShowCpu><ShowGpu>false</ShowGpu><ShowRam>false</ShowRam><ShowNetwork>false</ShowNetwork><ShowUptime>false</ShowUptime>' },
    @{ Name = 'soundboard'; Scale = '1.15'; Accent = 'Lime'; Theme = 'Midnight'; Swap = 'false'; Settings = $false; Extra = '<WidgetLayout><WidgetPlacement><Id>Focus</Id><X>0</X><Y>0</Y><Width>320</Width><Height>576</Height><Visible>true</Visible></WidgetPlacement><WidgetPlacement><Id>Media</Id><X>336</X><Y>0</Y><Width>472</Width><Height>310</Height><Visible>true</Visible></WidgetPlacement><WidgetPlacement><Id>Shortcuts</Id><X>336</X><Y>326</Y><Width>472</Width><Height>250</Height><Visible>false</Visible></WidgetPlacement><WidgetPlacement><Id>Mixer</Id><X>824</X><Y>0</Y><Width>456</Width><Height>576</Height><Visible>true</Visible></WidgetPlacement><WidgetPlacement><Id>System</Id><X>1296</X><Y>0</Y><Width>560</Width><Height>300</Height><Visible>false</Visible></WidgetPlacement><WidgetPlacement><Id>Weather</Id><X>1296</X><Y>300</Y><Width>560</Width><Height>276</Height><Visible>false</Visible></WidgetPlacement><WidgetPlacement><Id>Spotify</Id><X>1296</X><Y>0</Y><Width>560</Width><Height>576</Height><Visible>true</Visible></WidgetPlacement><WidgetPlacement><Id>Soundboard</Id><X>336</X><Y>326</Y><Width>472</Width><Height>250</Height><Visible>true</Visible></WidgetPlacement></WidgetLayout>' },
    @{ Name = 'prayer-settings'; Scale = '1.15'; Accent = 'Lime'; Theme = 'Midnight'; Swap = 'false'; Settings = $true; Tab = 'Prayer' },
    @{ Name = 'discord-settings'; Scale = '1.15'; Accent = 'Lavender'; Theme = 'Midnight'; Swap = 'false'; Settings = $true; Tab = 'Discord' },
    @{ Name = 'live-editor'; Scale = '1.15'; Accent = 'Glacier'; Theme = 'Slate'; Swap = 'false'; Settings = $false; Edit = $true },
    @{ Name = 'live-edited'; Scale = '1.15'; Accent = 'Amber'; Theme = 'Warm'; Swap = 'false'; Settings = $false; Edit = $true; Demo = $true }
)
if ($Only) { $cases = @($cases | Where-Object { $_.Name -in $Only.Split(',') }) }
foreach ($capture in $cases) {
    $settingsPath = Join-Path $captureRoot ($capture.Name + '.xml')
    $imagePath = Join-Path $captureRoot ($capture.Name + '.png')
    $xml = '<AppSettings><Theme>' + $capture.Theme + '</Theme><Accent>' + $capture.Accent + '</Accent><TextScale>' + $capture.Scale + '</TextScale><SwapSideWidgets>' + $capture.Swap + '</SwapSideWidgets><WeatherLocation></WeatherLocation>' + $capture.Extra + '</AppSettings>'
    [System.IO.File]::WriteAllText($settingsPath, $xml)
    $arguments = @('--render', ('"{0}"' -f $imagePath), '--settings', ('"{0}"' -f $settingsPath))
    if ($capture.Settings) { $arguments += @('--render-settings', '--render-tab', ('"{0}"' -f $capture.Tab)) }
    if ($capture.Edit) { $arguments += '--render-edit' }
    if ($capture.Demo) { $arguments += '--render-edit-demo' }
    $process = Start-Process -FilePath $appPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(15000)) { throw "Render timed out for $($capture.Name), process $($process.Id)." }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $imagePath)) { throw "Render failed for $($capture.Name)." }
    Write-Output "Captured $imagePath"
}
