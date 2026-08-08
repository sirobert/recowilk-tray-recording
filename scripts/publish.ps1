# Publikacja self-contained x64 Meeting Audio Recorder
# Użycie: .\scripts\publish.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "publish\win-x64"

Write-Host "Publishing to $out ..." -ForegroundColor Cyan

dotnet publish (Join-Path $root "src\MeetingAudioRecorder.App\MeetingAudioRecorder.App.csproj") `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $out

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish (Join-Path $root "src\MeetingAudioRecorder.BrowserBridge\MeetingAudioRecorder.BrowserBridge.csproj") `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $out

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Gotowe: $out\MeetingAudioRecorder.exe" -ForegroundColor Green
Write-Host "Most przeglądarki: $out\MeetingAudioRecorder.BrowserBridge.exe" -ForegroundColor Green
Write-Host "Uwaga: PublishSingleFile=false — Media Foundation i natywne zależności NAudio działają stabilniej w trybie multi-file."
Write-Host "Aby zbudować instalator Inno Setup, uruchom scripts\build-installer.ps1 (wymaga ISCC)."
