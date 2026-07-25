[CmdletBinding()]
param(
    [switch]$Quick
)

$ErrorActionPreference = "Stop"
$solution = Join-Path $PSScriptRoot "..\MeetingAudioRecorder.sln"

Write-Host "==> Format"
dotnet format $solution --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> Build Release"
dotnet build $solution -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $Quick) {
    Write-Host "==> Tests Release"
    dotnet test $solution -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Quality gate passed."

