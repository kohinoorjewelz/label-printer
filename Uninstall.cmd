@echo off
setlocal
powershell.exe -NoProfile -Command "$p = Join-Path $env:LOCALAPPDATA 'KohinoorLabelPrinter'; $bin = Join-Path $p 'KohinoorLabelPrinter.exe'; Get-Process -Name 'KohinoorLabelPrinter' -ErrorAction SilentlyContinue | Where-Object Path -eq $bin | Stop-Process; $paths = @((Join-Path ([Environment]::GetFolderPath('Startup')) 'Kohinoor Label Printer.lnk'), $bin, (Join-Path $p 'config.json'), (Join-Path $p 'printer.log')); foreach ($file in $paths) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force } }"
pause
