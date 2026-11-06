<#
.SYNOPSIS
    Vixxo Master Build Automation Script
.DESCRIPTION
    Builds the entire Vixxo product from source:
    - Runs all automated tests
    - Publishes self-contained Windows application (win-x64)
    - Compiles Inno Setup 6 Windows installer (artifacts/Vixxo-Setup-x64.exe)
    - Builds Android Release APK (artifacts/vixxo-android-release.apk)
#>

[CmdletBinding()]
param(
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RootDir = Split-Path -Parent $ScriptDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "               VIXXO MASTER BUILD PIPELINE                " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Environment configuration
$env:PATH = "$env:LOCALAPPDATA\dotnet;$env:PATH"
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet"
$env:JAVA_HOME = "C:\Program Files\Eclipse Adoptium\jdk-17.0.20.101-hotspot"
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"

$InnoCompiler = "C:\Users\somya\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $InnoCompiler)) {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        $InnoCompiler = $cmd.Source
    }
}

Write-Host "`n[1/5] Verifying environment tools..." -ForegroundColor Yellow
Write-Host "  .NET SDK:        $((& dotnet --version) 2>$null)"
Write-Host "  Java Home:       $env:JAVA_HOME"
Write-Host "  Android Home:    $env:ANDROID_HOME"
Write-Host "  Inno Setup:      $InnoCompiler"

# 2. Run automated tests
if (-not $SkipTests) {
    Write-Host "`n[2/5] Running automated tests..." -ForegroundColor Yellow
    Set-Location $RootDir
    & dotnet test tests/Vixxo.Tests/Vixxo.Tests.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Automated tests failed!"
    }
    Write-Host "  All tests passed successfully!" -ForegroundColor Green
} else {
    Write-Host "`n[2/5] Skipping tests as requested." -ForegroundColor DarkGray
}

# 3. Publish Windows self-contained application
Write-Host "`n[3/5] Publishing Windows self-contained x64 application..." -ForegroundColor Yellow
$PublishDir = Join-Path $RootDir "artifacts\publish\windows"
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

Set-Location $RootDir
& dotnet publish windows/Vixxo.App/Vixxo.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $PublishDir --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Windows self-contained publish failed!"
}
Write-Host "  Published successfully to $PublishDir" -ForegroundColor Green

# 4. Compile Inno Setup Installer
Write-Host "`n[4/5] Compiling Inno Setup Windows installer..." -ForegroundColor Yellow
if ($InnoCompiler -and (Test-Path $InnoCompiler)) {
    Set-Location $RootDir
    & "$InnoCompiler" installer/vixxo-setup.iss
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed!"
    }
    Write-Host "  Installer compiled successfully: artifacts/Vixxo-Setup-x64.exe" -ForegroundColor Green
} else {
    Write-Warning "Inno Setup 6 compiler (ISCC.exe) not found. Skipping installer generation."
}

# 5. Build Android Release APK
Write-Host "`n[5/5] Building Android Release APK..." -ForegroundColor Yellow
$AndroidDir = Join-Path $RootDir "android"
Set-Location $AndroidDir
& .\gradlew.bat assembleRelease --no-daemon
if ($LASTEXITCODE -ne 0) {
    throw "Android assembleRelease failed!"
}

$ReleaseApkSource = Join-Path $AndroidDir "app\build\outputs\apk\release\app-release.apk"
$ReleaseApkDest = Join-Path $RootDir "artifacts\vixxo-android-release.apk"

if (Test-Path $ReleaseApkSource) {
    Copy-Item $ReleaseApkSource $ReleaseApkDest -Force
    Write-Host "  Copied Release APK to: artifacts/vixxo-android-release.apk" -ForegroundColor Green
} else {
    throw "Built APK not found at $ReleaseApkSource"
}

# 6. Artifact Summary
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "                BUILD ARTIFACTS SUMMARY                   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Set-Location $RootDir
Get-ChildItem -Path artifacts -Filter "*.*" | ForEach-Object {
    $sizeMb = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  $($_.Name.PadRight(30)) : $sizeMb MB ($($_.Length) bytes)" -ForegroundColor White
}
Write-Host "`nBuild pipeline complete!" -ForegroundColor Green
