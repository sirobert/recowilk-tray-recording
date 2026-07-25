# Buduje publikację i instalator Inno Setup (opcjonalnie)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot "publish.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "Nie znaleziono Inno Setup 6 (ISCC.exe). Zainstaluj z https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    Write-Host "Możesz ręcznie skompilować scripts\installer.iss"
    exit 0
}

& $iscc (Join-Path $PSScriptRoot "installer.iss")
Write-Host "Instalator w folderze publish\installer\" -ForegroundColor Green
