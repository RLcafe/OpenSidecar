param([switch]$Test, [string]$OutputDirectory = 'artifacts\android')
# Builds the Android app (android\) into a signed WidgetScreen.apk with the Android SDK's own tools:
# aapt2, javac, d8, zipalign and apksigner. No Gradle, no downloaded libraries.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$androidRoot = Join-Path $projectRoot 'android'
$minSdk = 26; $targetSdk = 35

function Find-Sdk {
    foreach ($candidate in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk'))) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'platforms'))) { return $candidate }
    }
    throw 'The Android SDK is required. Install it with Android Studio or the command-line tools, or set ANDROID_HOME.'
}
function Version-Of([string]$name) { $digits = [regex]::Match($name, '\d+(\.\d+)*').Value; if ($digits) { [version]($digits + $(if ($digits.Contains('.')) { '' } else { '.0' })) } else { [version]'0.0' } }
function Invoke-Tool([string]$path, [string[]]$arguments, [string]$what) {
    & $path @arguments
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)." }
}

$sdk = Find-Sdk
$platform = Get-ChildItem -LiteralPath (Join-Path $sdk 'platforms') -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'android.jar') } | Sort-Object { Version-Of $_.Name } | Select-Object -Last 1
$buildTools = Get-ChildItem -LiteralPath (Join-Path $sdk 'build-tools') -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'd8.bat') } | Sort-Object { Version-Of $_.Name } | Select-Object -Last 1
if (-not $platform -or -not $buildTools) { throw 'The Android SDK needs a platform (android.jar) and build-tools (d8, aapt2, apksigner).' }
$androidJar = Join-Path $platform.FullName 'android.jar'
$tools = $buildTools.FullName
$javaBin = if ($env:JAVA_HOME -and (Test-Path -LiteralPath (Join-Path $env:JAVA_HOME 'bin\javac.exe'))) { Join-Path $env:JAVA_HOME 'bin' } else { Split-Path -Parent (Get-Command javac -ErrorAction Stop).Source }
$javac = Join-Path $javaBin 'javac.exe'; $jar = Join-Path $javaBin 'jar.exe'; $java = Join-Path $javaBin 'java.exe'; $keytool = Join-Path $javaBin 'keytool.exe'

# Same version source as the Windows build: the newest vX.Y.Z tag, plus commits since it.
$described = git -C $projectRoot describe --tags --match 'v[0-9]*' --dirty 2>$null
$versionMatch = [regex]::Match([string]$described, '^v(\d+)\.(\d+)\.(\d+)(?:-(\d+)-g[0-9a-f]+)?')
$major = 0; $minor = 0; $patch = 0; $commits = 0
if ($versionMatch.Success) { $major = [int]$versionMatch.Groups[1].Value; $minor = [int]$versionMatch.Groups[2].Value; $patch = [int]$versionMatch.Groups[3].Value }
if ($versionMatch.Groups[4].Success) { $commits = [int]$versionMatch.Groups[4].Value }
$versionName = if ($described) { ([string]$described).TrimStart('v') } else { '0.0.0-unknown' }
# Grows with every tag and every commit after it (up to 99), so a newer build installs over an older one.
$versionCode = $major * 1000000 + $minor * 10000 + $patch * 100 + [Math]::Min(99, $commits)
if ($versionCode -lt 1) { $versionCode = 1 }

$outputPath = Join-Path $projectRoot $OutputDirectory
$work = Join-Path $projectRoot 'artifacts\android-obj'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
foreach ($folder in @($outputPath, $work, "$work\gen", "$work\classes", "$work\dex")) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }

# 1. Resources and manifest.
Invoke-Tool (Join-Path $tools 'aapt2.exe') @('compile', '--dir', (Join-Path $androidRoot 'res'), '-o', "$work\resources.zip") 'Resource compilation'
Invoke-Tool (Join-Path $tools 'aapt2.exe') @('link', '-I', $androidJar, '--manifest', (Join-Path $androidRoot 'AndroidManifest.xml'), '--java', "$work\gen",
    '--min-sdk-version', $minSdk, '--target-sdk-version', $targetSdk, '--version-code', $versionCode, '--version-name', $versionName,
    '-o', "$work\base.apk", "$work\resources.zip") 'Resource linking'

# 2. Java sources, compiled against the platform's android.jar.
$sources = @(Get-ChildItem -LiteralPath (Join-Path $androidRoot 'src'), "$work\gen" -Recurse -Filter '*.java' | Sort-Object FullName | Select-Object -ExpandProperty FullName)
$sourceList = "$work\sources.txt"
$sources | ForEach-Object { '"' + $_.Replace('\', '/') + '"' } | Set-Content -LiteralPath $sourceList -Encoding ascii
Invoke-Tool $javac @('-nowarn', '-Xlint:-options', '-encoding', 'UTF-8', '-source', '8', '-target', '8', '-bootclasspath', $androidJar, '-d', "$work\classes", "@$sourceList") 'Java compilation'
Invoke-Tool $jar @('cf', "$work\classes.jar", '-C', "$work\classes", '.') 'Packing classes'

# 3. Dex, package, align and sign.
Invoke-Tool (Join-Path $tools 'd8.bat') @('--release', '--min-api', $minSdk, '--lib', $androidJar, '--output', "$work\dex", "$work\classes.jar") 'Dexing'
Push-Location "$work\dex"
try { Invoke-Tool (Join-Path $tools 'aapt.exe') @('add', "$work\base.apk", 'classes.dex') 'Adding classes.dex' | Out-Null } finally { Pop-Location }
Invoke-Tool (Join-Path $tools 'zipalign.exe') @('-p', '-f', '4', "$work\base.apk", "$work\aligned.apk") 'Aligning'

# The standard Android debug key, so each new build installs over the last one on the same device.
$keystore = Join-Path $env:USERPROFILE '.android\debug.keystore'
if (-not (Test-Path -LiteralPath $keystore)) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $keystore) -Force | Out-Null
    Invoke-Tool $keytool @('-genkeypair', '-keystore', $keystore, '-storepass', 'android', '-keypass', 'android', '-alias', 'androiddebugkey', '-keyalg', 'RSA', '-keysize', '2048', '-validity', '10000', '-dname', 'CN=Android Debug,O=Android,C=US') 'Creating the debug key'
}
$apk = Join-Path $outputPath 'WidgetScreen.apk'
Invoke-Tool (Join-Path $tools 'apksigner.bat') @('sign', '--ks', $keystore, '--ks-pass', 'pass:android', '--key-pass', 'pass:android', '--ks-key-alias', 'androiddebugkey', '--out', $apk, "$work\aligned.apk") 'Signing'
Invoke-Tool (Join-Path $tools 'apksigner.bat') @('verify', $apk) 'Signature check'
Write-Output "Built: $apk ($versionName, code $versionCode, $([Math]::Round((Get-Item -LiteralPath $apk).Length / 1KB)) KB)"

if ($Test) {
    # The checks run on the desktop JVM against the platform-independent core (com.alialyami.widgetscreen.core),
    # which must not use android.* classes. Compiling it without android.jar enforces that.
    $testWork = Join-Path $projectRoot 'artifacts\android-tests'
    if (Test-Path -LiteralPath $testWork) { Remove-Item -LiteralPath $testWork -Recurse -Force }
    New-Item -ItemType Directory -Path $testWork -Force | Out-Null
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $androidRoot 'src\com\alialyami\widgetscreen\core'), (Join-Path $androidRoot 'tests') -Recurse -Filter '*.java' | Select-Object -ExpandProperty FullName)
    $testList = "$testWork\sources.txt"
    $testSources | ForEach-Object { '"' + $_.Replace('\', '/') + '"' } | Set-Content -LiteralPath $testList -Encoding ascii
    Invoke-Tool $javac @('-nowarn', '-encoding', 'UTF-8', '-d', $testWork, "@$testList") 'Test compilation'
    # The Windows checks (build.ps1 -Test) save a real answer from the phone link; the phone must read it.
    $sample = Join-Path $projectRoot 'artifacts\tests\remote-state-sample.json'
    Invoke-Tool $java @("-Dwidgetscreen.sample=$sample", '-cp', $testWork, 'com.alialyami.widgetscreen.tests.AndroidTests') 'Tests'
}
