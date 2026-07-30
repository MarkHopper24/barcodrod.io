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

