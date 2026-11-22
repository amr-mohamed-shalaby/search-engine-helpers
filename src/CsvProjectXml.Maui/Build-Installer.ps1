param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$projectDir = $PSScriptRoot
$projectFile = Join-Path $projectDir 'CsvProjectXml.Maui.csproj'
$issFile = Join-Path $projectDir 'CsvProjectXml.InnoSetup.iss'
$publishDir = Join-Path $projectDir 'bin\Release\net10.0-windows10.0.19041.0\win-x64\publish'
$appExe = Join-Path $publishDir 'CsvProjectXml.Maui.exe'
$webViewUserDataDir = Join-Path $publishDir 'CsvProjectXml.Maui.exe.WebView2'

if (-not (Test-Path $projectFile)) {
    throw "Project file not found: $projectFile`nPlace this script in the CsvProjectXml.Maui project directory, beside the .csproj file."
}

if (-not (Test-Path $issFile)) {
    throw "Inno Setup script not found: $issFile"
}

if (-not $SkipPublish) {
    Write-Host 'Publishing the MAUI application...' -ForegroundColor Cyan

    & dotnet publish $projectFile `
        -f net10.0-windows10.0.19041.0 `
        -c Release `
        -p:RuntimeIdentifierOverride=win-x64 `
        -p:WindowsPackageType=None `
        -p:WindowsAppSDKSelfContained=true `
        -p:SelfContained=true `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path $appExe)) {
    throw "Published EXE not found: $appExe`nRun this script without -SkipPublish to publish the application first."
}

# Locate the Inno Setup command-line compiler (ISCC.exe).
$isccPath = $null
$isccCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
if ($isccCommand) {
    $isccPath = $isccCommand.Source
}

if (-not $isccPath) {
    $isccCandidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )

    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ }) {
        $isccCandidates += Join-Path $root 'Inno Setup 7\ISCC.exe'
        $isccCandidates += Join-Path $root 'Inno Setup 6\ISCC.exe'
    }

    $isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $isccPath) {
    throw 'Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 6 or newer, then run this script again.'
}

if (Test-Path $webViewUserDataDir) {
    Write-Host 'Note: Excluding the runtime WebView2 user-data directory from the installer:' -ForegroundColor Yellow
    Write-Host $webViewUserDataDir -ForegroundColor Yellow
    Write-Host 'This folder contains browser cache/state and lock files; it is not part of the application payload.' -ForegroundColor Yellow
}

$outputDir = Join-Path $projectDir 'installer-output'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "Compiling installer with: $isccPath" -ForegroundColor Cyan
& $isccPath $issFile

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE. Close the running app and retry if a file-lock error appears."
}

$installerPath = Join-Path $projectDir 'installer-output\CsvProjectXml-Setup-1.0.0.exe'
if (Test-Path $installerPath) {
    Write-Host ''
    Write-Host 'Installer created successfully:' -ForegroundColor Green
    Write-Host $installerPath -ForegroundColor Green
} else {
    Write-Warning 'The compiler finished, but the expected installer file was not found. Check the Inno Setup compiler output above.'
}
