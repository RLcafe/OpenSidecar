param([string]$SourceDirectory = 'artifacts\app', [string]$OutputDirectory = 'artifacts\setup', [string]$Version, [switch]$Test)
# Packs a built Widget Screen (WidgetScreen.exe, its .config, the ClaudeHook.exe bridge and, when the build has them, the
# hardware-sensor files in sensors\ with their notice and licences) into one setup exe,
# WidgetScreen-Setup-<version>.exe, built with the Framework's csc.exe like the app. See docs\design\setup.md.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
function Resolve-ProjectPath([string]$path) { if ([IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $projectRoot $path } }
$source = Resolve-ProjectPath $SourceDirectory
$output = Resolve-ProjectPath $OutputDirectory
foreach ($file in @('WidgetScreen.exe', 'WidgetScreen.exe.config', 'ClaudeHook.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $file))) { throw "$file is missing from $source. Build the app first." }
}
# The version comes from the newest vX.Y.Z tag, like the app's own (builds after a tag carry the commit count and hash).
if (-not $Version) { $Version = git -C $projectRoot describe --tags --match 'v[0-9]*' --dirty 2>$null }
if (-not $Version) { $Version = 'v0.0.0' }
$label = 'v' + ([string]$Version).TrimStart('v')
$match = [regex]::Match($label, '^v(\d+)\.(\d+)\.(\d+)')
$core = if ($match.Success) { '{0}.{1}.{2}' -f $match.Groups[1].Value, $match.Groups[2].Value, $match.Groups[3].Value } else { '0.0.0' }

$frameworkPath = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkPath 'csc.exe'
$generated = Join-Path $projectRoot 'artifacts\obj\setup'
New-Item -ItemType Directory -Path $generated, $output -Force | Out-Null
$info = Join-Path $generated 'SetupInfo.cs'
# The optional hardware sensors (decision 016): every file under sensors\, packed as payload/sensors/...; setup installs
# them only when Install hardware sensors is ticked.
$sensorFolder = Join-Path $source 'sensors'
$sensorResources = @()
$sensorsVersion = ''
if (Test-Path -LiteralPath (Join-Path $sensorFolder 'LibreHardwareMonitorLib.dll')) {
    $sensorsVersion = '0.9.6'
    foreach ($file in Get-ChildItem -LiteralPath $sensorFolder -Recurse -File) {
        $relative = $file.FullName.Substring($sensorFolder.Length + 1).Replace('\', '/')
        $sensorResources += ('/resource:' + $file.FullName + ',payload/sensors/' + $relative)
    }
}
@(
    '[assembly: System.Reflection.AssemblyTitle("Widget Screen Setup")]'
    '[assembly: System.Reflection.AssemblyProduct("Widget Screen")]'
    '[assembly: System.Reflection.AssemblyCompany("Ali Alyami")]'
    '[assembly: System.Reflection.AssemblyCopyright("Copyright © 2026 Ali Alyami")]'
    '[assembly: System.Reflection.AssemblyVersion("' + $core + '.0")]'
    '[assembly: System.Reflection.AssemblyFileVersion("' + $core + '.0")]'
    '[assembly: System.Reflection.AssemblyInformationalVersion("' + $label.TrimStart('v') + '")]'
    'namespace WidgetScreenSetup { static class SetupInfo { public const string Version = "' + $label.TrimStart('v') + '"; public const string SensorsVersion = "' + $sensorsVersion + '"; } }'
) | Set-Content -LiteralPath $info -Encoding utf8
$setupPath = Join-Path $output "WidgetScreen-Setup-$label.exe"
$references = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkPath $_) }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'installer') -Filter '*.cs' | Sort-Object FullName | Select-Object -ExpandProperty FullName) + $info
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/warn:4', "/out:$setupPath",
    ('/win32manifest:' + (Join-Path $projectRoot 'installer\setup.manifest')), ('/win32icon:' + (Join-Path $projectRoot 'assets\WidgetScreen.ico')),
    ('/resource:' + (Join-Path $source 'WidgetScreen.exe') + ',payload/WidgetScreen.exe'),
    ('/resource:' + (Join-Path $source 'WidgetScreen.exe.config') + ',payload/WidgetScreen.exe.config'),
    ('/resource:' + (Join-Path $source 'ClaudeHook.exe') + ',payload/ClaudeHook.exe')) + $sensorResources + $references + $sources
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw "Setup build failed (exit $LASTEXITCODE)." }
$sensorNote = if ($sensorsVersion) { " (with hardware sensors $sensorsVersion)" } else { ' (without hardware sensors)' }
Write-Output "Setup: $setupPath$sensorNote"
if ($Test) {
    & (Join-Path $projectRoot 'tests\Installer\installer-checks.ps1') -Setup $setupPath -Source $source
    if ($LASTEXITCODE -ne 0) { throw 'Setup checks failed.' }
}
