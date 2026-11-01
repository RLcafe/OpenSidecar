param([string]$Zip)
# Fetches the pinned LibreHardwareMonitor 0.9.6 files for the hardware sensors (third-party\LibreHardwareMonitor\README.md):
# downloads the official release zip (or uses -Zip <path>), checks its SHA-256, extracts the nine DLLs Widget Screen uses
# into third-party\LibreHardwareMonitor\bin and checks each one's SHA-256. Nothing is run from the zip.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $projectRoot 'third-party\LibreHardwareMonitor'
$url = 'https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.zip'
$sums = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $folder 'SHA256SUMS.txt')) {
    if ($line -match '^([0-9a-f]{64})\s+(\S+)$') { $sums[$Matches[2]] = $Matches[1] }
}
if (-not $Zip) {
    $downloads = Join-Path $projectRoot 'artifacts\downloads'
    New-Item -ItemType Directory -Path $downloads -Force | Out-Null
    $Zip = Join-Path $downloads 'LibreHardwareMonitor-0.9.6.zip'
    if (-not (Test-Path -LiteralPath $Zip)) {
        Write-Output "Downloading $url"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        (New-Object Net.WebClient).DownloadFile($url, $Zip)
    }
}
$hash = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $sums['LibreHardwareMonitor.zip']) { throw "The zip's SHA-256 is $hash, not the pinned $($sums['LibreHardwareMonitor.zip']). Delete it and try again." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bin = Join-Path $folder 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$archive = [IO.Compression.ZipFile]::OpenRead($Zip)
try {
    foreach ($name in $sums.Keys | Where-Object { $_ -like '*.dll' } | Sort-Object) {
        $entry = $archive.Entries | Where-Object { $_.FullName -eq $name } | Select-Object -First 1
        if (-not $entry) { throw "$name isn't in the zip." }
        $target = Join-Path $bin $name
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        $fileHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($fileHash -ne $sums[$name]) { Remove-Item -LiteralPath $target -Force; throw "$name has SHA-256 $fileHash, not the pinned $($sums[$name])." }
        Write-Output "OK $name"
    }
} finally { $archive.Dispose() }
Write-Output "Hardware sensor files are in $bin"
