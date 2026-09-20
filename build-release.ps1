# build-release.ps1
# Builds RocketLaunch as a self-contained single-file exe for Windows x64.
# Usage:
#   .\build-release.ps1                 # publish only
#   .\build-release.ps1 -BuildInstaller # publish + Inno Setup installer

param([switch]$BuildInstaller)

$ErrorActionPreference = "Stop"

$ProjectRoot   = $PSScriptRoot
$LauncherProj  = Join-Path $ProjectRoot "src\LauncherApp\LauncherApp.csproj"
$PublishDir    = Join-Path $ProjectRoot "installer\publish"
$InnoScript    = Join-Path $ProjectRoot "installer\RocketLaunch.iss"
$InnoExe       = "C:\Program Files (x86)\Inno Setup 6\iscc.exe"

Write-Host "=== RocketLaunch Release Builder ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: publish
Write-Host "[1/3] Publishing LauncherApp (self-contained, single-file, win-x64)..." -ForegroundColor Yellow

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

$publishArgs = @(
    "publish", $LauncherProj,
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--output", $PublishDir,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:PublishReadyToRun=true",
    "--nologo"
)

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet publish failed." -ForegroundColor Red
    exit 1
}

Write-Host "OK - Published to: $PublishDir" -ForegroundColor Green

# Step 2: list output
Write-Host ""
Write-Host "[2/3] Published files:" -ForegroundColor Yellow

Get-ChildItem $PublishDir | ForEach-Object {
    $size = if ($_.Length -gt 1MB) { "{0:N1} MB" -f ($_.Length / 1MB) }
            else { "{0:N0} KB" -f ($_.Length / 1KB) }
    Write-Host ("  {0,-42} {1,10}" -f $_.Name, $size)
}

# Step 3: Inno Setup (optional)
if ($BuildInstaller) {
    Write-Host ""
    Write-Host "[3/3] Building Windows installer..." -ForegroundColor Yellow

    if (-not (Test-Path $InnoExe)) {
        Write-Host ""
        Write-Host "WARNING: Inno Setup 6 not found at:" -ForegroundColor Yellow
        Write-Host "  $InnoExe" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Download it from: https://jrsoftware.org/isdl.php" -ForegroundColor Cyan
        Write-Host "Then re-run: .\build-release.ps1 -BuildInstaller" -ForegroundColor Cyan
    } else {
        & $InnoExe $InnoScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Inno Setup failed." -ForegroundColor Red
            exit 1
        }
        $setupExe = Join-Path $ProjectRoot "installer\Output\RocketLaunch-Setup.exe"
        if (Test-Path $setupExe) {
            $sizeMB = "{0:N1} MB" -f ((Get-Item $setupExe).Length / 1MB)
            Write-Host "OK - Installer ready: $setupExe ($sizeMB)" -ForegroundColor Green
        }
    }
} else {
    Write-Host ""
    Write-Host "[3/3] Installer skipped. Run with -BuildInstaller to create Setup.exe" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
