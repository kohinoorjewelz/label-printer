# Kohinoor label printer for Windows

Print 3 × 2 inch inventory labels from the Kohinoor Jewelz admin panel to a MUNBYN RW403B.

[Download the Windows installer](https://github.com/kohinoorjewelz/label-printer/releases/latest/download/KohinoorLabelPrinter-Windows.zip)

1. Install the [MUNBYN RW403B Windows driver](https://support.munbyn.com/hc/en-us/articles/6092502480787-Printer-Drivers-SDK-Download). Load and calibrate the stock. In Windows Printing preferences, create a matching [custom paper size](https://support.munbyn.com/hc/en-us/articles/43066361921427-How-to-create-a-new-customized-label-size). Use 3 inches across the feed and 2 inches along it, or reverse those settings if the roll feeds the other way.
2. Extract the entire release ZIP, then run `Install.cmd`. Setup adds **Kohinoor Label Printer** to the Start menu and preserves existing settings.
3. Open **Kohinoor Label Printer** when you are ready to print. On first launch, enter the store origin, Windows printer, stock dimensions, and the print pairing token configured in the app. To change them later, select **Settings**; the helper pauses polling while the form is open. Leaving the token blank keeps the saved token. **Test connection** checks the app and token without claiming a job or printing. Save settings before closing the form. Check **Product labels** in the admin product editor for the connection and queue status. Use **Stop helper** or close the window when you finish or before signing out. The helper finishes its current label submission before closing. The log is in `%LOCALAPPDATA%\KohinoorLabelPrinter\printer.log`.
4. Test one label before printing a batch. Confirm placement on the tag, read the SKU and price, and scan the QR on a phone. A published item should open its own listing and size. In case of a jam, check the Windows print queue and the physical labels before reprinting individual missing SKUs.

Close the helper before rerunning `Install.cmd` to update the executable. Updating from the first release removes its startup shortcut. Run `Uninstall.cmd` to remove the helper, Start menu shortcut, and local credentials.


## Build from source

On Windows, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1`. The ZIP is written to `dist/`. The executable is unsigned.
