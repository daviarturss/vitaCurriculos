# Installs Vita in the current user's profile and creates Desktop/Start Menu shortcuts.
# Run after tools\publish.ps1:
#   powershell -ExecutionPolicy Bypass -File tools\install.ps1
param(
    [string]$Source = "publish\win-x64",
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\Vita"),
    [switch]$NoShortcuts
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    $sourceDir = (Resolve-Path $Source).Path
    $sourceExe = Join-Path $sourceDir "Vita.exe"
    if (-not (Test-Path $sourceExe)) {
        throw "Vita.exe não encontrado em $sourceDir. Execute tools\publish.ps1 primeiro."
    }

    # Updating the app requires closing the previous copy.
    Get-Process Vita -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 600

    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Get-ChildItem $sourceDir -Force |
        Where-Object { $_.Name -ne "App_Data" } |
        ForEach-Object { Copy-Item $_.FullName -Destination $InstallDir -Recurse -Force }

    Copy-Item (Join-Path $root "tools\uninstall.ps1") (Join-Path $InstallDir "uninstall.ps1") -Force

    $uninstallCmd = @'
@echo off
set /p answer=Remover a Vita e todos os dados salvos neste computador? (s/N):
if /i not "%answer%"=="s" exit /b 0
taskkill /IM Vita.exe /F >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
'@
    Set-Content -Path (Join-Path $InstallDir "Desinstalar Vita.cmd") -Value $uninstallCmd -Encoding ASCII

    $exe = Join-Path $InstallDir "Vita.exe"
    if (-not $NoShortcuts) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcuts = @(
            (Join-Path ([Environment]::GetFolderPath('Desktop')) "Vita.lnk"),
            (Join-Path ([Environment]::GetFolderPath('Programs')) "Vita.lnk")
        )
        foreach ($shortcutPath in $shortcuts) {
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = $exe
            $shortcut.WorkingDirectory = $InstallDir
            $shortcut.IconLocation = "$exe,0"
            $shortcut.Description = "Vita - currículo, ATS, vagas e assistente com IA"
            $shortcut.Save()
            Write-Host "Atalho criado: $shortcutPath"
        }
        [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
    }

    Write-Host "Vita instalada em $InstallDir"
    Write-Host "Abra pelo atalho 'Vita' na área de trabalho ou no menu Iniciar."
}
finally {
    Pop-Location
}
