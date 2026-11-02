# Publikacja self-contained x64 Meeting Audio Recorder
# Użycie: .\scripts\publish.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "publish\win-x64"

# Each package starts clean: preserve earlier build artifacts rather than mixing runtime versions.
$publishRoot = [IO.Path]::GetFullPath((Join-Path $root "publish"))
$resolvedOutput = [IO.Path]::GetFullPath($out)
if (-not $resolvedOutput.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedOutput) -ne "win-x64") {
    throw "Unexpected publish target: $resolvedOutput"
}
if (Test-Path -LiteralPath $resolvedOutput) {
    $previousOutput = [IO.Path]::GetFullPath((Join-Path $publishRoot ("previous-win-x64-" + [Guid]::NewGuid().ToString("N"))))
    if (-not $previousOutput.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid archive target" }
    Move-Item -LiteralPath $resolvedOutput -Destination $previousOutput
}
[void][IO.Directory]::CreateDirectory($resolvedOutput)
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

dotnet publish (Join-Path $root "src\MeetingAudioRecorder.TranslationWorker\MeetingAudioRecorder.TranslationWorker.csproj") `
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false `
  -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $out "TranslationWorker")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Gotowe: $out\MeetingAudioRecorder.exe" -ForegroundColor Green
Write-Host "Most przeglądarki: $out\MeetingAudioRecorder.BrowserBridge.exe" -ForegroundColor Green
Write-Host "Uwaga: PublishSingleFile=false — Media Foundation i natywne zależności NAudio działają stabilniej w trybie multi-file."
Write-Host "Aby zbudować instalator Inno Setup, uruchom scripts\build-installer.ps1 (wymaga ISCC)."

# The tested Marian ONNX export is local, not a publicly hosted upstream artifact.
# Build input can be provided on another machine through RECORDER_MARIAN_MODELS.
$marianSource = $env:RECORDER_MARIAN_MODELS
if (-not $marianSource) { $marianSource = Join-Path $env:LOCALAPPDATA 'MeetingAudioRecorder\Models\marian' }
$pins = Get-Content (Join-Path $root 'src/MeetingAudioRecorder.TranslationWorker/marian-pins.json') -Raw | ConvertFrom-Json
$marianOutput = Join-Path $out 'TranslationModels\marian'
New-Item -ItemType Directory -Force $marianOutput | Out-Null
foreach ($pin in $pins.PSObject.Properties) {
    $source = Join-Path $marianSource $pin.Name
    if (-not (Test-Path -LiteralPath $source) -or (Get-Item -LiteralPath $source).Length -ne $pin.Value.bytes -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $pin.Value.sha256) { throw "Missing/invalid Marian build input: $($pin.Name). Set RECORDER_MARIAN_MODELS to the verified export directory." }
    Copy-Item -LiteralPath $source -Destination (Join-Path $marianOutput $pin.Name)
}
