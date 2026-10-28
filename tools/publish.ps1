# Builds the self-contained Windows executable used by the installer and the portable package.
# Usage: powershell -ExecutionPolicy Bypass -File tools\publish.ps1
param(
    [string]$Output = "publish\win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

    dotnet publish "src\Vita.App\Vita.App.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $Output `
        --nologo

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (código $LASTEXITCODE)." }

    # Files used only by build tooling or IIS, not by the desktop app.
    foreach ($name in @("web.config", "appsettings.Development.json", "Vita.Core.pdb", "Vita.pdb")) {
        Remove-Item (Join-Path $Output $name) -Force -ErrorAction SilentlyContinue
    }
    Get-ChildItem $Output -Directory -Filter "BuildHost-*" -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force

    $exe = Join-Path $Output "Vita.exe"
    if (-not (Test-Path $exe)) { throw "Vita.exe não foi gerado." }

    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "Publicado em $((Resolve-Path $Output).Path) — Vita.exe com $size MB."
}
finally {
    Pop-Location
}
