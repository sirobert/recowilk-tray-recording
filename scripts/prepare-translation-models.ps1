[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [string]$DestinationDirectory = (Join-Path $env:LOCALAPPDATA 'MeetingAudioRecorder\Models'),
    [switch]$IncludeTurbo
)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath($SourceDirectory)
$targetRoot = [IO.Path]::GetFullPath($DestinationDirectory)
$pins = @(
    @{ Name='ggml-small.en-q8_0.bin'; Bytes=264477561; Hash='67a179f608ea6114bd3fdb9060e762b588a3fb3bd00c4387971be4d177958067' },
    @{ Name='silero_vad.onnx'; Bytes=2327524; Hash='2623a2953f6ff3d2c1e61740c6cdb7168133479b267dfef114a4a3cc5bdd788f' }
)
if ($IncludeTurbo) { $pins += @{ Name='ggml-large-v3-turbo-q8_0.bin'; Bytes=874188075; Hash='317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1' } }
$marian = Get-Content -Raw (Join-Path $PSScriptRoot '..\src\MeetingAudioRecorder.TranslationWorker\marian-pins.json') | ConvertFrom-Json
foreach ($property in $marian.PSObject.Properties) { $pins += @{ Name=('marian\' + $property.Name); Bytes=$property.Value.bytes; Hash=$property.Value.sha256 } }
foreach ($pin in $pins) {
    $inputFile = Join-Path $sourceRoot $pin.Name
    if ((Get-Item -LiteralPath $inputFile).Length -ne $pin.Bytes -or (Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash -ne $pin.Hash) { throw "Nieprawidłowy model: $($pin.Name)" }
}
foreach ($pin in $pins) {
    $inputFile = Join-Path $sourceRoot $pin.Name
    $outputFile = Join-Path $targetRoot $pin.Name
    if (Test-Path -LiteralPath $outputFile) {
        if ((Get-Item -LiteralPath $outputFile).Length -eq $pin.Bytes -and (Get-FileHash -LiteralPath $outputFile -Algorithm SHA256).Hash -eq $pin.Hash) { continue }
        throw "Docelowy plik istnieje z inną zawartością: $outputFile"
    }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFile))
    $temporary = $outputFile + '.' + [Guid]::NewGuid().ToString('N') + '.partial'
    Copy-Item -LiteralPath $inputFile -Destination $temporary
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $pin.Hash) { throw "Błąd kopii: $($pin.Name)" }
    Move-Item -LiteralPath $temporary -Destination $outputFile
}
Write-Host "Modele gotowe: $targetRoot"
