# Changelog

## Unreleased

### Bug Fixes

- **Fixed `TextWrapping="WrapWholeWords"` build errors** — Replaced invalid `WrapWholeWords` values with `Wrap` on the `EmailBody` RichEditBox and `SameColorWarning` TextBlock in `EncodePage.xaml`. `WrapWholeWords` is not a valid value for the WinUI `TextWrapping` property.

- **Fixed `WindowsBase` assembly version conflict** — Downgraded `ZXing.Net.Bindings.Windows.Compatibility` from `0.16.14` to `0.16.11` in `barcodrod.io.csproj` to align with `ZXing.Net` `0.16.11` and resolve a conflict between `WindowsBase` 4.0.0.0 and 9.0.0.0. Removed the unused ZXing package references from `barcodrod.io.Core.csproj`.

- **Fixed window icon not showing** — Removed the `<Content Remove="Assets\WindowIcon.ico" />` line from `barcodrod.io.csproj` that was preventing the icon from being deployed to the output directory, which caused `AppWindow.SetIcon()` in `MainWindow.xaml.cs` to fail silently at runtime.

### Removed — MVVM Template Cleanup

Removed unused scaffolding left over from the WinUI Community MVVM template that was never wired up:

- **Deleted empty ViewModels:**
  - `ViewModels/DecodeViewModel.cs` — empty class, never used by `DecodePage`
  - `ViewModels/EncodeViewModel.cs` — empty class, `EncodePage` declared but never used its `ViewModel` property
  - `ViewModels/HistoryViewModel.cs` — empty class, `HistoryPage` called `App.GetService<HistoryViewModel>()` but discarded the result

- **Deleted unused Behaviors:**
  - `Behaviors/NavigationViewHeaderBehavior.cs` — not referenced in `ShellPage.xaml` (the `Interaction.Behaviors` block was empty)
  - `Behaviors/NavigationViewHeaderMode.cs` — only used by the removed `NavigationViewHeaderBehavior`

- **Deleted unused Helpers/Contracts:**
  - `Helpers/SettingsStorageExtensions.cs` — never called from any code
  - `Helpers/FrameExtensions.cs` — only used for `INavigationAware` checks which never triggered
  - `Contracts/ViewModels/INavigationAware.cs` — no class ever implemented this interface

- **Removed `barcodrod.io.Core` project dependency** — The Core project (`IFileService`, `FileService`, `Json` helper) was only consumed by `LocalSettingsService`. These three files were inlined into the main project under their equivalent namespaces:
  - `barcodrod.io.Core/Contracts/Services/IFileService.cs` ? `barcodrod.io/Contracts/Services/IFileService.cs`
  - `barcodrod.io.Core/Services/FileService.cs` ? `barcodrod.io/Services/FileService.cs`
  - `barcodrod.io.Core/Helpers/Json.cs` ? `barcodrod.io/Helpers/Json.cs`

### Changed — Refactored Files

- **`App.xaml.cs`** — Removed DI registrations for `DecodeViewModel`, `EncodeViewModel`, `HistoryViewModel`. Removed `barcodrod.io.Core` using statements. Replaced with local `barcodrod.io` namespace references.

- **`Services/PageService.cs`** — Refactored from `Configure<VM, V>()` (which required ViewModel types) to `Configure<V>(string key)` using string navigation keys directly, eliminating the dependency on ViewModel classes.

- **`Services/NavigationService.cs`** — Removed all `INavigationAware` checks from `GoBack()`, `NavigateTo()`, and `OnNavigated()`. Removed `FrameExtensions` usage. Removed `barcodrod.io.Contracts.ViewModels` using.

- **`Services/NavigationViewService.cs`** — Replaced `typeof(SettingsViewModel).FullName!` with the string literal `"barcodrod.io.ViewModels.SettingsViewModel"`.

- **`Activation/DefaultActivationHandler.cs`** — Replaced `typeof(DecodeViewModel).FullName!` with the string literal `"barcodrod.io.ViewModels.DecodeViewModel"`. Removed `barcodrod.io.ViewModels` using.

- **`Services/LocalSettingsService.cs`** — Updated using statements from `barcodrod.io.Core.Contracts.Services` and `barcodrod.io.Core.Helpers` to `barcodrod.io.Contracts.Services` and `barcodrod.io.Helpers`.

- **`Views/EncodePage.xaml.cs`** — Removed `using barcodrod.io.ViewModels` and the unused `public EncodeViewModel ViewModel { get; }` property.

- **`Views/HistoryPage.xaml.cs`** — Removed `using barcodrod.io.ViewModels` and the discarded `App.GetService<HistoryViewModel>()` call.

- **`barcodrod.io.csproj`** — Removed `<ProjectReference>` to `barcodrod.io.Core`. Added direct `<PackageReference>` for `Newtonsoft.Json` 13.0.4 (previously transitive via Core).

### Added — Pin on Top Feature

- **`Views/ShellPage.xaml`** — Added a `NavigationViewItem` in `NavigationView.FooterMenuItems` (above the Settings gear) with a pin icon (`&#xE718;`) that toggles always-on-top.

- **`Views/ShellPage.xaml.cs`** — Added `PinButton_Click` handler that toggles `App.MainWindow.IsAlwaysOnTop` (WinUIEx), swaps the icon glyph between pin/unpin states, and updates the tooltip.
