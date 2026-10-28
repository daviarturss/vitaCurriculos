# Removes the installed copy of Vita and its shortcuts. User data lives inside the
# installation folder, so it is removed together after the confirmation in the .cmd.
$ErrorActionPreference = "SilentlyContinue"

$installDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Get-Process Vita | Stop-Process -Force
Start-Sleep -Milliseconds 600

$desktop = [Environment]::GetFolderPath('Desktop')
Remove-Item (Join-Path $desktop "Vita.lnk") -Force

$programs = [Environment]::GetFolderPath('Programs')
Remove-Item (Join-Path $programs "Vita.lnk") -Force

# The folder cannot delete itself while this script is running, so a short delay
# lets PowerShell exit first.
Start-Process cmd -ArgumentList '/c', "timeout /t 2 >nul & rmdir /s /q `"$installDir`"" -WindowStyle Hidden
