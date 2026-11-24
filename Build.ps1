param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework compiler was not found.' }
$destination = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$binary = Join-Path $destination 'KohinoorLabelPrinter.exe'
& $compiler /nologo /target:winexe /optimize+ /warnaserror "/out:$binary" /r:System.Drawing.dll /r:System.Security.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'LabelPrinter.cs')
if ($LASTEXITCODE -ne 0) { throw 'Windows helper build failed.' }
$files = @($binary, (Join-Path $PSScriptRoot 'Install.cmd'), (Join-Path $PSScriptRoot 'Install.ps1'), (Join-Path $PSScriptRoot 'Uninstall.cmd'), (Join-Path $PSScriptRoot 'README.md'))
Compress-Archive -LiteralPath $files -DestinationPath (Join-Path $destination 'KohinoorLabelPrinter-Windows.zip') -Force
Get-FileHash -Algorithm SHA256 (Join-Path $destination 'KohinoorLabelPrinter-Windows.zip')
