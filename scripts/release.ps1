param([string]$Version, [switch]$UpdateShortcut, [switch]$Force)
# Builds a tagged version from a clean checkout of its tag, runs every check, and copies
# the app into releases\<version>. Defaults to the newest vX.Y.Z tag reachable from main.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    if (-not $Version) { $Version = git describe --tags --abbrev=0 --match 'v[0-9]*' main }
    if (-not $Version) { throw 'No vX.Y.Z tag found on main.' }
    git rev-parse --verify --quiet "refs/tags/$Version" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Tag $Version does not exist." }
    $commit = git rev-parse --short "$Version^{commit}"

    $releasePath = Join-Path $projectRoot "releases\$Version"
    if (Test-Path -LiteralPath $releasePath) {
        if (-not $Force) { throw "releases\$Version already exists. Use -Force to rebuild it (close the app first)." }
        Remove-Item -LiteralPath $releasePath -Recurse -Force
    }

    # Build from the tag itself so uncommitted or later work never leaks into a release.
    $buildCheckout = Join-Path $projectRoot ".worktrees\release-$Version"
    if (Test-Path -LiteralPath $buildCheckout) { git worktree remove --force $buildCheckout }
    git worktree add --detach $buildCheckout $Version
    if ($LASTEXITCODE -ne 0) { throw "Could not check out $Version." }
    try {
        # The hardware-sensor files are git-ignored: the ones already fetched and checked here go to the build checkout,
        # whose build.ps1 checks them again against the tag's SHA256SUMS.txt.
        $sensorBin = Join-Path $projectRoot 'third-party\LibreHardwareMonitor\bin'
        if ((Test-Path -LiteralPath $sensorBin) -and (Test-Path -LiteralPath (Join-Path $buildCheckout 'third-party\LibreHardwareMonitor'))) {
            Copy-Item -LiteralPath $sensorBin -Destination (Join-Path $buildCheckout 'third-party\LibreHardwareMonitor') -Recurse -Force
        }
        & (Join-Path $buildCheckout 'scripts\build.ps1') -Test
        New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
        Copy-Item -Path (Join-Path $buildCheckout 'artifacts\app\*') -Destination $releasePath -Recurse
        # The Android app (tags from v0.4.0 on) ships next to the exe when the Android SDK is installed.
        $androidScript = Join-Path $buildCheckout 'scripts\build-android.ps1'
        if (Test-Path -LiteralPath $androidScript) {
            try {
                & $androidScript -Test
                Copy-Item -LiteralPath (Join-Path $buildCheckout 'artifacts\android\WidgetScreen.apk') -Destination $releasePath
            } catch { Write-Warning "The Android app was not built for this release: $($_.Exception.Message)" }
        }
        # The setup exe (tags from v0.12.0 on) installs this release with Start menu and startup shortcuts.
        $packageScript = Join-Path $buildCheckout 'scripts\package.ps1'
        if (Test-Path -LiteralPath $packageScript) { & $packageScript -SourceDirectory $releasePath -OutputDirectory $releasePath -Version $Version -Test }
    } finally {
        git worktree remove --force $buildCheckout
        git worktree prune
    }
} finally { Pop-Location }

@(
    "Widget Screen $Version"
    "Commit: $commit"
    "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    'Settings: %LOCALAPPDATA%\WidgetScreen\settings.xml (shared with every build)'
    $(if (Test-Path -LiteralPath (Join-Path $releasePath "WidgetScreen-Setup-$Version.exe")) { "Setup: WidgetScreen-Setup-$Version.exe installs it for you, with Start menu and startup shortcuts and an uninstaller (docs\design\setup.md)" })
    $(if (Test-Path -LiteralPath (Join-Path $releasePath 'WidgetScreen.apk')) { 'Android: WidgetScreen.apk, to install on a phone or tablet (docs\design\android-app.md)' })
) | Set-Content -LiteralPath (Join-Path $releasePath 'release.txt') -Encoding utf8
Write-Output "Release: $releasePath\WidgetScreen.exe"

if ($UpdateShortcut) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Widget Screen.lnk'
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $releasePath 'WidgetScreen.exe'
    $shortcut.WorkingDirectory = $releasePath
    $shortcut.Description = "Widget Screen $Version"
    $shortcut.Save()
    Write-Output "Shortcut: $shortcutPath -> $($shortcut.TargetPath)"
}
