$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$directory = Join-Path $env:LOCALAPPDATA 'KohinoorLabelPrinter'
$binary = Join-Path $directory 'KohinoorLabelPrinter.exe'
$config = Join-Path $directory 'config.json'
$old = if (Test-Path -LiteralPath $config) { Get-Content -LiteralPath $config -Raw | ConvertFrom-Json }

function Read-Setting($label, $default) {
    $value = Read-Host "$label [$default]"
    if ([string]::IsNullOrWhiteSpace($value)) { return $default }
    return $value.Trim()
}

function Read-Number($label, $default, $min, $max) {
    $value = Read-Setting $label ($default.ToString([Globalization.CultureInfo]::InvariantCulture))
    $number = 0.0
    if (-not [double]::TryParse($value, [Globalization.NumberStyles]::AllowDecimalPoint, [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -or $number -lt $min -or $number -gt $max) {
        throw "Enter a number from $min to $max for $label."
    }
    return $number
}

$siteDefault = if ($old) { $old.Site } else { 'https://kohinoorjewelz.ca' }
$site = [uri](Read-Setting 'Store admin site origin' $siteDefault)
if (-not $site.IsAbsoluteUri -or ($site.Scheme -ne 'https' -and -not ($site.Scheme -eq 'http' -and $site.IsLoopback)) -or
    $site.UserInfo -or $site.PathAndQuery -ne '/' -or $site.Fragment) {
    throw 'Enter an HTTPS site origin, or a loopback HTTP origin for local testing.'
}
$printers = @(Get-Printer | Select-Object -ExpandProperty Name)
Write-Host 'Installed Windows printer names:'
$printers | ForEach-Object { Write-Host "  $_" }
$printerDefault = if ($old) { $old.Printer } else { $printers | Where-Object { $_ -match '403B' } | Select-Object -First 1 }
$printer = Read-Setting 'Exact label printer name' $printerDefault
if (-not $printer -or $printers -notcontains $printer) { throw 'Install the MUNBYN driver and select the exact Windows printer name.' }
$width = Read-Number 'Width across the feed (inches)' $(if ($old) { $old.Width } else { 3.0 }) 1.58 4.33
$height = Read-Number 'Length along the feed (inches)' $(if ($old) { $old.Height } else { 2.0 }) 1.5 8.0
$margin = Read-Number 'Inset on each side (inches)' $(if ($old) { $old.Margin } else { 0.10 }) 0 0.25
$secret = Read-Host 'Print pairing token (Enter to keep an existing token)' -AsSecureString
if ($secret.Length -eq 0 -and $old) {
    $encrypted = $old.Token
} else {
    if ($secret.Length -lt 32) { throw 'A pairing token of at least 32 characters is required.' }
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes([Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer))
        try {
            $encrypted = [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect($bytes, $null, 'CurrentUser'))
        } finally {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

New-Item -ItemType Directory -Path $directory -Force | Out-Null
Get-Process -Name 'KohinoorLabelPrinter' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -eq $binary
} | Stop-Process
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'KohinoorLabelPrinter.exe') -Destination $binary -Force
$settings = @{ Site = $site.GetLeftPart([UriPartial]::Authority); Token = $encrypted; Printer = $printer; Width = $width; Height = $height; Margin = $margin }
[IO.File]::WriteAllText($config, ($settings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$oldStartupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Kohinoor Label Printer.lnk'
if (Test-Path -LiteralPath $oldStartupShortcut) { Remove-Item -LiteralPath $oldStartupShortcut }
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Kohinoor Label Printer.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $binary
$shortcut.WorkingDirectory = $directory
$shortcut.Description = 'Kohinoor boutique label printer'
$shortcut.Save()
Write-Host 'Installed. Open Kohinoor Label Printer from the Start menu to print queued labels. Close its window when finished.'
Write-Host "Log: $directory\printer.log"
