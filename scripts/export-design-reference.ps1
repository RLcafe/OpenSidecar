param([string]$AppDirectory = 'artifacts\specification', [string]$OutputDirectory = 'docs\design\as-built-reference')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $projectRoot $OutputDirectory
$appPath = Join-Path (Join-Path $projectRoot $AppDirectory) 'WidgetScreen.exe'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$assembly = [Reflection.Assembly]::LoadFrom($appPath)
$fixtureRoot = Join-Path $projectRoot 'artifacts\design-reference-fixtures'
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
# Render the real WPF visual tree without showing a window or firing Loaded.
# No live integrations, account connections, playback actions or personal preferences.
$cases = @(
    @{ Name = '01-dashboard'; Theme = 'Midnight'; Accent = 'Lime'; Scale = 1 },
    @{ Name = '02-theme'; Theme = 'Midnight'; Accent = 'Lime'; Scale = 1; Tab = 0 },
    @{ Name = '03-display'; Theme = 'Midnight'; Accent = 'Lime'; Scale = 1; Tab = 1 },
    @{ Name = '04-widgets'; Theme = 'Midnight'; Accent = 'Lime'; Scale = 1; Tab = 2 },
    @{ Name = '05-weather'; Theme = 'Midnight'; Accent = 'Lime'; Scale = 1; Tab = 3 },
    @{ Name = '06-spotify'; Theme = 'Midnight'; Accent = 'Lime'; Scale = 1; Tab = 4 },
    @{ Name = '07-quick-launch'; Theme = 'Midnight'; Accent = 'Lime'; Scale = 1; Tab = 5 },
    @{ Name = '08-layout-editor'; Theme = 'Midnight'; Accent = 'Lime'; Scale = 1; Edit = $true },
    @{ Name = '09-slate-large'; Theme = 'Slate'; Accent = 'Glacier'; Scale = 1.15 },
    @{ Name = '10-warm-square'; Theme = 'Warm'; Accent = 'Amber'; Scale = 1; Corners = 0 },
    @{ Name = '11-optional-widgets'; Theme = 'Midnight'; Accent = 'Lime'; Scale = 1; Legacy = $true }
)
foreach ($case in $cases) {
    $settings = New-Object WidgetScreen.AppSettings
    $settings.Theme = $case.Theme; $settings.Accent = $case.Accent; $settings.TextScale = $case.Scale
    $settings.StartFullscreen = $false
    if ($case.ContainsKey('Corners')) { $settings.CornerRadius = $case.Corners }
    if ($case.Legacy) { $settings.WidgetLayout = [WidgetScreen.WidgetLayout]::Default($settings) }
    else { $settings.WidgetLayout = [WidgetScreen.WidgetLayout]::SpotifyDefault($settings) }
    $settingsPath = Join-Path $fixtureRoot ($case.Name + '.xml')
    [WidgetScreen.SettingsStore]::Save($settingsPath, $settings)
    $stream = $assembly.GetManifestResourceStream('WidgetScreen.MainWindow.xaml')
    try { $window = [Windows.Markup.XamlReader]::Load($stream) } finally { $stream.Dispose() }
    $controller = New-Object WidgetScreen.DashboardController($window, $settingsPath, $true)
    try {
        # Stable display-only fixtures, explicitly marked in every image.
        $window.FindName('ClockTime').Text = '10:09'
        $window.FindName('ClockPeriod').Text = 'AM'
        $window.FindName('ClockDay').Text = 'FRIDAY'
        $window.FindName('ClockDate').Text = 'October 9'
        $window.FindName('AudioStatus').Text = 'UNAVAILABLE'
        $window.FindName('AudioStatus').SetResourceReference([Windows.Controls.TextBlock]::ForegroundProperty, 'Warning')
        $window.FindName('AudioDevice').Text = 'Audio controls are unavailable'
        $window.FindName('MasterSlider').IsEnabled = $false
        $window.FindName('MuteButton').IsEnabled = $false
        $window.FindName('MicrophoneButton').IsEnabled = $false
        $window.FindName('MicrophoneButton').Content = 'Audio device unavailable'
        $window.FindName('SessionCount').Text = [string][char]0x2014
        $window.FindName('NoSessions').Text = 'Reconnect your audio output or open Windows sound settings using the gear above.'
        if ($case.ContainsKey('Tab')) {
            $window.FindName('SettingsButton').RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent)))
            $window.FindName('SettingsTabs').SelectedIndex = $case.Tab
            # Avoid embedding this PC's monitor names or Start menu paths in the references.
            $window.FindName('MonitorChoice').ItemsSource = @('DISPLAY2 | 1920 x 720 (reference fixture)')
            $window.FindName('MonitorChoice').SelectedIndex = 0
            $window.FindName('SettingsStatus').Text = 'DESIGN REFERENCE | isolated fixtures; no live account or device data.'
        }
        if ($case.Edit) { $controller.Editor.Begin() }
        $window.FindName('StatusMessage').Text = 'DESIGN REFERENCE | fixed clock; integrations inactive; no personal settings.'
        $dashboard = $window.FindName('Dashboard')
        $dashboard.Measure((New-Object Windows.Size(1920,720)))
        $dashboard.Arrange((New-Object Windows.Rect(0,0,1920,720)))
        $dashboard.UpdateLayout()
        $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap(1920,720,96,96,[Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($dashboard)
        $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $imagePath = Join-Path $outputPath ($case.Name + '.png')
        $file = [IO.File]::Create($imagePath)
        try { $encoder.Save($file) } finally { $file.Dispose() }
        Write-Output "Rendered $($case.Name)"
    } finally { $controller.Dispose(); $window.Close() }
}
