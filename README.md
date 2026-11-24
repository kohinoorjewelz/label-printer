# Kohinoor label printer for Windows

Print 3 × 2 inch inventory labels from the Kohinoor Jewelz admin panel to a MUNBYN RW403B.

[Download the Windows installer](https://github.com/kohinoorjewelz/label-printer/releases/latest/download/KohinoorLabelPrinter-Windows.zip)

1. Install the [MUNBYN RW403B Windows driver](https://support.munbyn.com/hc/en-us/articles/6092502480787-Printer-Drivers-SDK-Download). Load and calibrate the stock. In Windows Printing preferences, create a matching [custom paper size](https://support.munbyn.com/hc/en-us/articles/43066361921427-How-to-create-a-new-customized-label-size). Use 3 inches across the feed and 2 inches along it, or reverse those settings if the roll feeds the other way.
2. Extract the entire release ZIP, then run `Install.cmd`. Enter your store origin, the Windows printer name, the stock dimensions, and the print pairing token supplied by the person who configures the store. The helper starts immediately and whenever that Windows account signs in.
3. Check **Product labels** in the admin product editor for the helper connection and queue status. If the helper reports a problem, check the label stock and printer settings. The log is in `%LOCALAPPDATA%\KohinoorLabelPrinter\printer.log`.
4. Test one label before printing a batch. Confirm placement on the tag, read the SKU and price, and scan the QR on a phone. A published item should open its own listing and size. In case of a jam, check the Windows print queue and the physical labels before reprinting individual missing SKUs.

Run `Install.cmd` again to change settings or update the executable. Keep existing values by pressing Enter. Run `Uninstall.cmd` to remove the helper, startup shortcut, and local credentials.

## Build from source

On Windows, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1`. The ZIP is written to `dist/`. The executable is unsigned.
