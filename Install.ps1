$ErrorActionPreference = 'Stop'
$directory = Join-Path $env:LOCALAPPDATA 'KohinoorLabelPrinter'
$binary = Join-Path $directory 'KohinoorLabelPrinter.exe'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
Get-Process -Name 'KohinoorLabelPrinter' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -eq $binary
} | Stop-Process
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'KohinoorLabelPrinter.exe') -Destination $binary -Force
$oldStartupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Kohinoor Label Printer.lnk'
if (Test-Path -LiteralPath $oldStartupShortcut) { Remove-Item -LiteralPath $oldStartupShortcut }
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Kohinoor Label Printer.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $binary
$shortcut.WorkingDirectory = $directory
$shortcut.Description = 'Kohinoor boutique label printer'
$shortcut.Save()
Write-Host 'Installed. Open Kohinoor Label Printer from the Start menu. On first launch, enter your app URL, printer, dimensions, and pairing token.'
Write-Host 'Existing settings are preserved. Use Settings in the helper to change them.'
Write-Host "Log: $directory\printer.log"
