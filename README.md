<h1 align="center">
  <a href="https://www.microsoft.com/store/apps/9PHMXNX36SZZ"><img src="https://raw.githubusercontent.com/MarkHopper24/barcodrod.io/public/Assets/Square150x150Logo.scale-200.png" alt="barcodrod.io"></a><br>
  barcodrod.io
  
</h1>

<h4 align="center">A free, modern barcode and QR code toolkit for Windows 10/11. 🤠</h4>

<p align="center">
  <a href="#overview">Overview</a> •
  <a href="#installation">Installation</a> •
  <a href="#usage">Usage</a> •
  <a href="#support">Support</a> •
  <a href="#credits">Credits</a> •
  <a href="#license">License</a>
</p>


## Overview
<a href="https://barcodrod.io">barcodrod.io</a> is a free, modern barcode and QR code toolkit for Windows. Decode 17 different barcode types using the Windows Snipping Tool, an image file, your clipboard, or webcam. Create 13 different types of barcodes from text. 

barcodrod.io is built on the WinAppSDK, WinUI3, .NET 9, and the ZXing.Net library.

<p align="center">
<img src = https://github.com/MarkHopper24/barcodrod.io/blob/public/barcodrod.io.gif>
<br>
</p>

## Installation
Method 1: Directly from the Microsoft Store [HERE](https://www.microsoft.com/store/apps/9PHMXNX36SZZ)

Method 2: Downloading and installing the MSIX package directly from the latest GitHub release [HERE](https://github.com/MarkHopper24/barcodrod.io/releases/latest) 

Method 3: Downloading and running the standalone **MSI installer** from the latest GitHub release [HERE](https://github.com/MarkHopper24/barcodrod.io/releases/latest). This is a classic, fully offline installer (x64 and arm64) that does not require the Microsoft Store, winget, or an internet connection. It is code-signed via the [SignPath Foundation](https://signpath.org) (see the Code signing policy below) and is the recommended option for offline or locked-down environments.

Method 4: From Windows Package Manager using winget via command line
```
winget install --name barcodrod.io
```

The Microsoft Store and MSIX install paths (Methods 1, 2 and 4) require an internet connection on first installation for license acquisition through the Microsoft Store; after the first launch the app can be used fully offline. The standalone MSI installer (Method 3) does not have this requirement and installs completely offline.

Method 5: Building the solution on your own in Visual Studio using the provided source code (this method does not require a network connection).

## Usage
Includes support for the following barcode formats:

Decode and encode:
- QR_CODE
- CODE_128
- CODE_39
- EAN_8
- ITF
- UPC_A
- UPC_E
- CODABAR
- DATA_MATRIX
- PDF_417
- AZTEC
- MSI
- PLESSEY

Decode only:
- MAXICODE
- RSS_14
- RSS_EXPANDED
- EAN_13

## Features
### Decoding
- Read QR codes and barcodes from:
  1. The Windows Snipping Tool app.
  2. An image file on your PC.
  3. A folder on your PC to decode images in bulk.
  4. Images copied and pasted from your clipboard.
  5. Video sources such as webcams, OBS virtual cameras, or the Windows Camera app.
- Launch decoded results directly into other apps.

### Encoding
- Built-in helper templates to generate Wi-fi, Email, and vCard (contact card) QR codes. 
- Custom colors, margins, and error correction settings.

### History
- View, manage, and export history of QR codes and barcodes read and created on your PC. (Can be disabled).

### Inventory
- The fourth navigation tab tracks personal belongings, collections, equipment, supplies, or business inventory. Add an item with just a name: it starts with quantity one and a generated, editable item ID. The ID is stored as `SKU` in existing inventory files and workbooks, preserving compatibility.
- The basic form covers name, item ID, quantity, unit, and location. Expand optional sections for barcodes/organization, reservations/quantity targets, value/sourcing, notes, and custom fields. Commercial fields are optional and empty values stay out of the details view.
- **Update quantity** offers Add quantity, Remove quantity, Set total quantity, Reserve quantity, Release reservation, Use reserved quantity, and Move location. Exact counts start at the current total and show the resulting balance. Every saved change is recorded in activity history. Moving a location applies to the entire item's quantity; use separate IDs for quantities in separate places.
- **Scan shortcuts:** choose Look up, Add quantity, or Remove quantity; set the quantity and an optional person/project/reference; scan or paste an item ID/barcode into Search and press Enter. Each Add/Remove scan immediately records that quantity change. This input path supports scanners that type codes as keyboard input; the Read tab's camera/image decoding does not automatically change inventory quantities.
- Search by name, ID, barcode, category, location, notes, or custom values. Filter unavailable or expired items and items below a configured minimum; minimum warnings are off when the minimum is zero.
- **Bulk options:** select multiple items for label printing, export, or archive/restore; Select all shown respects the current filtered view. Excel/CSV imports can add or update many records in one reviewed operation. Quantity changes apply to one item at a time, including repeated scans.
- Import `.xlsx`, CSV, or TSV with configurable worksheet/header row, column mapping, delimiters, number/date culture, and custom columns. Imports show a review before merging matching SKUs. Exports use the selected records, or the current filtered view when nothing is selected.
- Back up and restore the complete inventory, movement log, and configuration. Local saves are atomic and keep the previous snapshot as `inventory.json.bak`; competing app instances cannot silently overwrite each other's changes. Restore is available even if inventory loading fails.

**Excel integration:** Choose **Excel** to create a linked workbook or connect an existing `.xlsx` file. **Save to Excel** writes app changes; **Sync both ways** also imports Excel edits. Optional automatic sync runs after saved inventory changes. Sync compares each mapped field with its last synchronized value, merges compatible edits, and stops on conflicting edits. Other worksheets, unmapped columns, and existing formulas are preserved; mapped formula cells cannot be overwritten. Keep records in the sheet and use `Archived` for intentional removals.

File-based import, export, and synchronization work without Excel installed. With desktop Microsoft Excel, an open workbook can be read and saved directly, including its pending edits; **Open record in Excel** finds the current SKU row and selects it. Saving an open workbook also saves its other pending changes. SKU is the workbook record key and cannot be renamed while connected. Legacy `.xls` and macro-enabled `.xlsm` files are not modified. Workbook saves keep a `.barcodrod.bak` backup (or `.barcodrod.bak.xlsx` for an open Excel workbook). New exports include a movement-log worksheet; use **Export movement log** for a fresh audit export.

### Barcode label printing
- Print from Inventory, Create, or selected History records. Label printing renders black codes on white stock for consistent scanning.
- Presets cover Avery 5160/5260 (30 labels), 5163/5263 (10), L7160 (A4, 21), L7159 (A4, 24), and 2×1, 4×2, or 4×6-inch thermal labels. Paper size, label size, rows/columns, margins, gaps, and calibration offsets can also be customized in millimeters.
- Choose copies per item or quantities based on on-hand, available, or reorder stock; skip used positions on the first sheet. Customize code format, payload templates, and printed name/SKU/location/price/lot/expiry fields. A blank barcode falls back to the SKU. Example payload: `{SKU}:{Lot}` or `{Custom.Brand}`.
- Review the page preview, print through the printer dialog, or save an exact-size PDF. Invalid or excessively dense codes and layouts that do not fit are rejected before printing. Print at **100% / actual size**, match the printer's paper size, and turn browser headers/footers off.

Inventory can also be selected as the default launch page in Settings. Scan shortcuts and Excel workbook controls have separate expandable sections. Item details and recent activity remain available through the toolbar menu on narrow windows. For isolated inventories or UI testing, launch with `--inventory-data-directory <absolute-folder-path>`; otherwise the normal per-user app data folder is used.

Inventory tests: `dotnet test barcodrod.io.Inventory.Tests/barcodrod.io.Inventory.Tests.csproj`. The repeatable desktop check is `scripts/Test-InventoryUI.ps1`; it requires a running app and an empty isolated inventory folder. Its default UI Automation mode tests scan submission without taking keyboard focus; add `-KeyboardInput` to exercise actual keyboard input on an unlocked desktop. `scripts/Test-InventoryIntegrationUI.ps1` continues with workbook conflicts, bulk import, backup restore, Read/Create/History, and compact layouts, using the first script's fixture and a Python runtime with openpyxl. Live Excel navigation and physical scanner/printer validation require the corresponding application/hardware.

### Interface
- Built natively for Windows using WinUI3 and the Windows App SDK; offering a simple, familiar, and fast user interface. 
- Supports light and dark themes, as well as three different backdrop options.

## Privacy Notes
- Barcodes processed with barcodrod.io are read and created locally on your PC, no network connection is required. For now, barcodrod.io requires an internet connection on installation for license acquisition through the Microsoft Store (this includes the build hosted in this repository). After first installation, it can be used fully offline. It is a planned roadmap item to implement an fully offline installation method.

- barcodrod.io does not collect any decoded or generated QR or barcode data.

- barcodrod.io allows you to inspect QR code contents and URLs before opening them, helping verify the legitimacy of the links and data encoded in the QR codes. 

- barcodrod.io does not inject any URL redirection services in the generated QR codes; what you enter into the code is exactly what gets created.

## Support
- This project is a work in progress. Any contributions, suggestions, fixes, bug reports, and feature requests are welcome.
- barcodrod.io is an [emailware](https://en.wiktionary.org/wiki/emailware). If you're a regular user of this app or if it's helped you in any way, I would love to hear from you at <mark.hopper24@gmail.com>.

## Credits
This software would not have been possible without the the following tools, resources, and open source packages:
- [Michael Jahn and the rest of the contributors to the ZXing.Net .NET port of the ZXing Java library](https://github.com/micjahn/ZXing.Net)
- [Windows Community Toolkit](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- [.NET Community Toolkit](https://github.com/CommunityToolkit/dotnet)
- [@amitmerchant1990](https://github.com/amitmerchant1990/electron-markdownify#readme) (readme inspiration)
- [Template Studio for WinUI](https://marketplace.visualstudio.com/items?itemName=TemplateStudio.TemplateStudioForWinUICs)
- [Windows Camera](https://apps.microsoft.com/store/detail/windows-camera/9WZDNCRFJBBG?) and [Snipping Tool](https://apps.microsoft.com/store/detail/snipping-tool/9MZ95KL8MR0L)
- AForge.NET (DirectShow Library)

## Sponsors

| | |
|:--:|:--|
| <img src="https://signpath.org/assets/favicon-50x50.png" width="30" alt="SignPath" /> | Free code signing on Windows provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org) |

## Code signing policy

Free code signing for the standalone MSI installer published in this repository's
[GitHub Releases](https://github.com/MarkHopper24/barcodrod.io/releases) is provided by
[SignPath.io](https://about.signpath.io), with a certificate issued by the
[SignPath Foundation](https://signpath.org).

**Team roles**
- **Authors & reviewers:** project maintainers — see the repository
  [contributors](https://github.com/MarkHopper24/barcodrod.io/graphs/contributors).
- **Approvers:** repository owner ([@MarkHopper24](https://github.com/MarkHopper24)). Every release
  signing request is reviewed and approved manually.

**Privacy**
barcodrod.io processes barcodes and QR codes locally on your PC. This program will not transfer any
information to other networked systems unless specifically requested by the user or the person
installing or operating it. See the [Privacy Notes](#privacy-notes) above.

> The Microsoft Store build is signed by Microsoft through the Store. The MSI installer in GitHub
> Releases is signed via the SignPath Foundation and installs fully offline.

## License
[Apache 2.0](https://github.com/MarkHopper24/barcodrod.io/blob/public/LICENSE.txt)

---
[barcodrod.io website](https://barcodrod.io) &nbsp;&middot;&nbsp;
[MarkHopper24 (GitHub)](https://github.com/MarkHopper24) &nbsp;&middot;&nbsp;
[@Mark_Hopper24 (Twitter)](https://twitter.com/Mark_Hopper24)

