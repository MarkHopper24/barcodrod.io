# Changelog

## v2.1

### Added — Native Arm64 support

- **barcodrod.io now ships a native Arm64 build.** Every download for this release is offered for
  both **x64** and **Arm64**, so devices such as the Surface Pro / Surface Laptop (Snapdragon) and
  other Windows on Arm PCs run barcodrod.io natively instead of through x64 emulation. That means
  faster startup, quicker scanning and encoding, and lower battery use on those machines. The Arm64
  build is a first-class release artifact: it is produced by the same CI pipeline and carries the
  same code signature as the x64 build.
- Note: the legacy AForge/DirectShow webcam pipeline remains x64-only. On Arm64 use the default
  Windows MediaCapture webcam engine, which is fully supported.

### Added — CI/CD code signing (SignPath) for the MSI

- **GitHub Actions release workflow** — Added `.github/workflows/release.yml`, which builds the MSI
  for **x64 and arm64** on GitHub-hosted runners (via `installer/build-msi.ps1`) and code-signs it
  through the [SignPath Foundation](https://signpath.org) OSS program: `workflow_dispatch` test-signs
  (validation), version tags `v*` release-sign (manual approval) and publish a GitHub Release with
  `SHA256SUMS.txt`, and pull requests build only. SignPath org/project/policy values are read from
  GitHub Actions variables/secret (`SIGNPATH_*`), so nothing sensitive is committed. The Microsoft
  Store continues to sign and distribute the MSIX.
- **SignPath artifact configuration** — Added `.signpath/artifact-config-msi.xml`, which deep-signs
  the first-party binaries with enforced `product-name`/`product-version` metadata and signs the MSI.
- **Deterministic version stamping** — `installer/build-msi.ps1` now stamps the published binaries'
  `FileVersion`/`ProductVersion` from the build version so SignPath's metadata restriction is
  enforceable; the arm64 build path and `resources.pri` staging were made architecture-robust.

### Changed

- **`barcodrod.io.csproj` cleanup** — Removed the duplicate `GenerateTemporaryStoreCertificate`
  property and replaced the hardcoded `AppxPackageDir` (`D:\barcodrod.io_builds\`) with a
  repo-relative default so CI and other contributors don't depend on a machine-specific path.

### Added — Classic (unpackaged) MSI installer

- **Standalone MSI installer** — Added `installer/barcodrod.io.wxs` (WiX v5) and
  `installer/build-msi.ps1` to produce a classic, fully **unpackaged** MSI that installs
  barcodrod.io into Program Files with a Start Menu shortcut and Add/Remove Programs entry. The app
  runs with no MSIX, no Microsoft Store, and no Store license, enabling offline and locked-down
  environments (addresses #25). The installer auto-harvests the published output (no hand-maintained
  file list).

- **File type associations (MSI ↔ MSIX parity)** — The MSI registers a `barcodrod.io.image` ProgID
  and advertises barcodrod.io in the Windows "Open with" list for the same 14 image extensions as the
  MSIX manifest (`.jpg .jpeg .png .bmp .gif .tif .tiff .webp .heif .heic .jfif .ico .svg .jp2`).
  `App.OnLaunched` now also accepts the file path from the command line, since a classic file
  association passes the file as an argument rather than via the packaged File activation contract.

- **Toast notification COM activator (MSI ↔ MSIX parity)** — The MSI registers
  `HKCR\CLSID\{DD6D181A-17B6-4A3A-9E66-B9399E0F1184}\LocalServer32` and sets
  `System.AppUserModel.ToastActivatorCLSID` on the Start Menu shortcut, mirroring the MSIX manifest's
  `windows.comServer` toast activator one-to-one.

### Bug Fixes — Unpackaged execution

- **App now runs unpackaged** — Added `Helpers/AppPaths.cs` (`LocalDataFolder` /
  `GetLocalFolderAsync()`) which returns the app data folder via `ApplicationData.Current.LocalFolder`
  when packaged and `%LOCALAPPDATA%\barcodrod.io` (`StorageFolder.GetFolderFromPathAsync`) when
  unpackaged. Replaced all ~44 unguarded `ApplicationData.Current.LocalFolder` usages across
  `MainWindow`, `SettingsPage`, `HistoryPage`, `EncodePage`, `DecodePage`, and
  `DefaultActivationHandler` with this helper. `ApplicationData.Current` throws without package
  identity, which previously caused the unpackaged app to start with no window.

- **Unhandled exceptions are now logged** — `App.App_UnhandledException` writes to
  `%LOCALAPPDATA%\barcodrod.io\crash.log` instead of silently swallowing exceptions (`e.Handled` is
  still set to keep prior behaviour). This surfaced the unpackaged startup failures above.

### Bug Fixes — Webcam

- **Fixed the webcam button being disabled on launch (#36)** — On the default (Windows Camera /
  MediaCapture) backend the camera source dropdown was populated but never given a selection, and
  the webcam button is only enabled in response to a selection change. `LoadWebcamSettings` was
  meant to restore the previous source, but it returned early when `settings.json` was missing or
  empty, skipping the "select the first source" fallback at the end of the method. On a fresh
  install the button therefore stayed greyed out. Source restoration is now separated from the
  fallback (`TryRestoreSavedWebcamSourceAsync`), so a default source is always selected when nothing
  can be restored — including missing, empty, or malformed settings, or a saved camera that is no
  longer present. This restores the behaviour added in #27.

## v2.0

### Bug Fixes

- **Fixed `TextWrapping="WrapWholeWords"` build errors** � Replaced invalid `WrapWholeWords` values with `Wrap` on the `EmailBody` RichEditBox and `SameColorWarning` TextBlock in `EncodePage.xaml`. `WrapWholeWords` is not a valid value for the WinUI `TextWrapping` property.

- **Fixed `WindowsBase` assembly version conflict** � Downgraded `ZXing.Net.Bindings.Windows.Compatibility` from `0.16.14` to `0.16.11` in `barcodrod.io.csproj` to align with `ZXing.Net` `0.16.11` and resolve a conflict between `WindowsBase` 4.0.0.0 and 9.0.0.0. Removed the unused ZXing package references from `barcodrod.io.Core.csproj`.

- **Fixed window icon not showing** � Removed the `<Content Remove="Assets\WindowIcon.ico" />` line from `barcodrod.io.csproj` that was preventing the icon from being deployed to the output directory, which caused `AppWindow.SetIcon()` in `MainWindow.xaml.cs` to fail silently at runtime.

### Removed � MVVM Template Cleanup

Removed unused scaffolding left over from the WinUI Community MVVM template that was never wired up:

- **Deleted empty ViewModels:**
  - `ViewModels/DecodeViewModel.cs` � empty class, never used by `DecodePage`
  - `ViewModels/EncodeViewModel.cs` � empty class, `EncodePage` declared but never used its `ViewModel` property
  - `ViewModels/HistoryViewModel.cs` � empty class, `HistoryPage` called `App.GetService<HistoryViewModel>()` but discarded the result

- **Deleted unused Behaviors:**
  - `Behaviors/NavigationViewHeaderBehavior.cs` � not referenced in `ShellPage.xaml` (the `Interaction.Behaviors` block was empty)
  - `Behaviors/NavigationViewHeaderMode.cs` � only used by the removed `NavigationViewHeaderBehavior`

- **Deleted unused Helpers/Contracts:**
  - `Helpers/SettingsStorageExtensions.cs` � never called from any code
  - `Helpers/FrameExtensions.cs` � only used for `INavigationAware` checks which never triggered
  - `Contracts/ViewModels/INavigationAware.cs` � no class ever implemented this interface

- **Removed `barcodrod.io.Core` project dependency** � The Core project (`IFileService`, `FileService`, `Json` helper) was only consumed by `LocalSettingsService`. These three files were inlined into the main project under their equivalent namespaces:
  - `barcodrod.io.Core/Contracts/Services/IFileService.cs` ? `barcodrod.io/Contracts/Services/IFileService.cs`
  - `barcodrod.io.Core/Services/FileService.cs` ? `barcodrod.io/Services/FileService.cs`
  - `barcodrod.io.Core/Helpers/Json.cs` ? `barcodrod.io/Helpers/Json.cs`

### Changed � Refactored Files

- **`App.xaml.cs`** � Removed DI registrations for `DecodeViewModel`, `EncodeViewModel`, `HistoryViewModel`. Removed `barcodrod.io.Core` using statements. Replaced with local `barcodrod.io` namespace references.

- **`Services/PageService.cs`** � Refactored from `Configure<VM, V>()` (which required ViewModel types) to `Configure<V>(string key)` using string navigation keys directly, eliminating the dependency on ViewModel classes.

- **`Services/NavigationService.cs`** � Removed all `INavigationAware` checks from `GoBack()`, `NavigateTo()`, and `OnNavigated()`. Removed `FrameExtensions` usage. Removed `barcodrod.io.Contracts.ViewModels` using.

- **`Services/NavigationViewService.cs`** � Replaced `typeof(SettingsViewModel).FullName!` with the string literal `"barcodrod.io.ViewModels.SettingsViewModel"`.

- **`Activation/DefaultActivationHandler.cs`** � Replaced `typeof(DecodeViewModel).FullName!` with the string literal `"barcodrod.io.ViewModels.DecodeViewModel"`. Removed `barcodrod.io.ViewModels` using.

- **`Services/LocalSettingsService.cs`** � Updated using statements from `barcodrod.io.Core.Contracts.Services` and `barcodrod.io.Core.Helpers` to `barcodrod.io.Contracts.Services` and `barcodrod.io.Helpers`.

- **`Views/EncodePage.xaml.cs`** � Removed `using barcodrod.io.ViewModels` and the unused `public EncodeViewModel ViewModel { get; }` property.

- **`Views/HistoryPage.xaml.cs`** � Removed `using barcodrod.io.ViewModels` and the discarded `App.GetService<HistoryViewModel>()` call.

- **`barcodrod.io.csproj`** � Removed `<ProjectReference>` to `barcodrod.io.Core`. Added direct `<PackageReference>` for `Newtonsoft.Json` 13.0.4 (previously transitive via Core).

### Added � Pin on Top Feature

- **`Views/ShellPage.xaml`** � Added a `NavigationViewItem` in `NavigationView.FooterMenuItems` (above the Settings gear) with a pin icon (`&#xE718;`) that toggles always-on-top.

- **`Views/ShellPage.xaml.cs`** � Added `PinButton_Click` handler that toggles `App.MainWindow.IsAlwaysOnTop` (WinUIEx), swaps the icon glyph between pin/unpin states, and updates the tooltip.
