# Builds the shareable portable package: ZIP with a clean config (no API key) and no user data.
# Run after tools\publish.ps1:
#   powershell -ExecutionPolicy Bypass -File tools\make-portable.ps1
param(
    [string]$Source = "publish\win-x64",
    [string]$DistDir = "dist"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    $sourceDir = (Resolve-Path $Source).Path
    if (-not (Test-Path (Join-Path $sourceDir "Vita.exe"))) {
        throw "Vita.exe não encontrado em $sourceDir. Execute tools\publish.ps1 primeiro."
    }

    $stage = Join-Path $DistDir "Vita-Portatil"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    Get-ChildItem $sourceDir -Force |
        Where-Object { $_.Name -notin @("App_Data", "appsettings.Development.json") } |
        ForEach-Object { Copy-Item $_.FullName -Destination $stage -Recurse -Force }

    # The portable package must never carry the personal API key or user data.
    $configPath = Join-Path $stage "appsettings.json"
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    if ($config.Ai) { $config.Ai.DefaultApiKey = "" }
    $config | ConvertTo-Json -Depth 10 | Set-Content $configPath -Encoding UTF8

    Copy-Item (Join-Path $root "tools\LEIA-ME.txt") (Join-Path $stage "LEIA-ME.txt") -Force

    $zip = Join-Path $DistDir "Vita-Portatil.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

    $size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "Pacote portátil criado: $((Resolve-Path $zip).Path) — $size MB."
}
finally {
    Pop-Location
}
