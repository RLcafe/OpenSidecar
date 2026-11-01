param([switch]$Test, [string]$OutputDirectory = 'artifacts\app')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$frameworkPath = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkPath 'csc.exe'
$outputPath = Join-Path $projectRoot $OutputDirectory
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The Windows .NET Framework 4.8 compiler is required.' }
$assemblyNames = @('System.dll', 'System.Core.dll', 'System.Security.dll', 'System.Web.dll', 'System.Xaml.dll', 'System.Xml.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll')
$references = @($assemblyNames | ForEach-Object { '/reference:' + (Join-Path $frameworkPath $_) })
foreach ($assemblyName in @('System.Runtime', 'System.Runtime.WindowsRuntime', 'System.ObjectModel', 'System.Threading.Tasks', 'System.Runtime.InteropServices.WindowsRuntime')) {
    $assemblyPath = Get-ChildItem -Path (Join-Path $env:windir "Microsoft.NET\assembly\GAC_MSIL\$assemblyName\*\$assemblyName.dll") -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    if ($assemblyPath) { $references += '/reference:' + $assemblyPath }
}
foreach ($metadataName in @('Windows.Foundation.winmd', 'Windows.Media.winmd', 'Windows.Storage.winmd')) {
    $references += '/reference:' + (Join-Path $env:windir "System32\WinMetadata\$metadataName")
}
# The exe's version comes from the newest vX.Y.Z tag; builds after a tag carry the commit count and hash.
$described = git -C $projectRoot describe --tags --match 'v[0-9]*' --dirty 2>$null
$versionMatch = [regex]::Match([string]$described, '^v(\d+)\.(\d+)\.(\d+)(?:-(\d+)-g[0-9a-f]+)?')
$versionCore = if ($versionMatch.Success) { '{0}.{1}.{2}' -f $versionMatch.Groups[1].Value, $versionMatch.Groups[2].Value, $versionMatch.Groups[3].Value } else { '0.0.0' }
$commitsSinceTag = if ($versionMatch.Groups[4].Success) { $versionMatch.Groups[4].Value } else { '0' }
$informationalVersion = if ($described) { ([string]$described).TrimStart('v') } else { '0.0.0-unknown' }
$generatedPath = Join-Path $projectRoot 'artifacts\obj'
New-Item -ItemType Directory -Path $generatedPath -Force | Out-Null
$versionSource = Join-Path $generatedPath 'BuildVersion.cs'
@(
    '[assembly: System.Reflection.AssemblyVersion("' + $versionCore + '.0")]'
    '[assembly: System.Reflection.AssemblyFileVersion("' + $versionCore + '.' + $commitsSinceTag + '")]'
    '[assembly: System.Reflection.AssemblyInformationalVersion("' + $informationalVersion + '")]'
) | Set-Content -LiteralPath $versionSource -Encoding utf8
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Recurse -Filter '*.cs' | Sort-Object FullName | Select-Object -ExpandProperty FullName) + $versionSource
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/warn:4', ('/out:' + (Join-Path $outputPath 'WidgetScreen.exe')), ('/win32manifest:' + (Join-Path $projectRoot 'src\app\app.manifest')), ('/win32icon:' + (Join-Path $projectRoot 'assets\WidgetScreen.ico')), ('/resource:' + (Join-Path $projectRoot 'src\app\MainWindow.xaml') + ',WidgetScreen.MainWindow.xaml')) + $references + $sources
& $compilerPath $arguments
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\app\App.config') -Destination (Join-Path $outputPath 'WidgetScreen.exe.config') -Force
Write-Output "Built: $outputPath\WidgetScreen.exe ($informationalVersion)"
# ClaudeHook.exe, the Claude Code bridge (decision 014): a console exe from hook\ and the shared Claude files in src\.
$claudeShared = @('ClaudeJson.cs', 'ClaudeBridge.cs', 'ClaudeProcesses.cs') | ForEach-Object { Join-Path $projectRoot "src\integrations\Claude\$_" }
$hookSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'hook') -Filter '*.cs' | Sort-Object FullName | Select-Object -ExpandProperty FullName) + $claudeShared + $versionSource
$hookArguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/utf8output', '/warn:4', ('/out:' + (Join-Path $outputPath 'ClaudeHook.exe')), ('/win32icon:' + (Join-Path $projectRoot 'assets\WidgetScreen.ico')),
    ('/reference:' + (Join-Path $frameworkPath 'System.dll')), ('/reference:' + (Join-Path $frameworkPath 'System.Core.dll'))) + $hookSources
& $compilerPath $hookArguments
if ($LASTEXITCODE -ne 0) { throw "ClaudeHook build failed (exit $LASTEXITCODE)." }
Write-Output "Built: $outputPath\ClaudeHook.exe"
if ($Test) {
    $testOutput = Join-Path $projectRoot 'artifacts\tests'
    New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
    $testArguments = @('/nologo', '/target:exe', '/platform:x64', '/utf8output', ('/out:' + (Join-Path $testOutput 'WidgetScreen.Tests.exe')), ('/reference:' + (Join-Path $outputPath 'WidgetScreen.exe'))) + $references + @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Recurse -Filter '*.cs' | Select-Object -ExpandProperty FullName)
    & $compilerPath $testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $outputPath 'WidgetScreen.exe') -Destination $testOutput -Force
    Copy-Item -LiteralPath (Join-Path $outputPath 'ClaudeHook.exe') -Destination $testOutput -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\app\App.config') -Destination (Join-Path $testOutput 'WidgetScreen.Tests.exe.config') -Force
    & (Join-Path $testOutput 'WidgetScreen.Tests.exe') $testOutput
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
