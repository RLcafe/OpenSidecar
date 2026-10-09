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
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Recurse -Filter '*.cs' | Sort-Object FullName | Select-Object -ExpandProperty FullName)
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/warn:4', ('/out:' + (Join-Path $outputPath 'WidgetScreen.exe')), ('/win32manifest:' + (Join-Path $projectRoot 'src\app\app.manifest')), ('/resource:' + (Join-Path $projectRoot 'src\app\MainWindow.xaml') + ',WidgetScreen.MainWindow.xaml')) + $references + $sources
& $compilerPath $arguments
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\app\App.config') -Destination (Join-Path $outputPath 'WidgetScreen.exe.config') -Force
Write-Output "Built: $outputPath\WidgetScreen.exe"
if ($Test) {
    $testOutput = Join-Path $projectRoot 'artifacts\tests'
    New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
    $testArguments = @('/nologo', '/target:exe', '/platform:x64', '/utf8output', ('/out:' + (Join-Path $testOutput 'WidgetScreen.Tests.exe')), ('/reference:' + (Join-Path $outputPath 'WidgetScreen.exe'))) + $references + @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Recurse -Filter '*.cs' | Select-Object -ExpandProperty FullName)
    & $compilerPath $testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $outputPath 'WidgetScreen.exe') -Destination $testOutput -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\app\App.config') -Destination (Join-Path $testOutput 'WidgetScreen.Tests.exe.config') -Force
    & (Join-Path $testOutput 'WidgetScreen.Tests.exe') $testOutput
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
