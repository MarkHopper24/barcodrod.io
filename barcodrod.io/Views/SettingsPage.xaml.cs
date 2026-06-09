using barcodrod.io.Helpers;
using barcodrod.io.ViewModels;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.InteropServices;
using Windows.Storage;

namespace barcodrod.io.Views;

// TODO: Set the URL for your privacy policy by updating SettingsPage_PrivacyTermsLink.NavigateUri in Resources.resw.
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    private StorageFolder? localFolder;
    private string? settingsFilePath;
    private bool SettingsLoaded = false;
    private bool _updatingLegacyDirectShowToggle;

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        if (IsMicaSupported() == false) Backdrops.Visibility = Visibility.Collapsed;
        if (!IsLegacyDirectShowSupportedArchitecture()) UseLegacyDirectShow.Visibility = Visibility.Collapsed;

        try
        {
            LoadSettings();
        }
        catch
        {
            if (IsMicaSupported() == true) Backdrops.SelectedIndex = 0;

            HistoryEnabled.IsChecked = true;
            Backdrops.SelectedIndex = 0;
            return;
        }
    }

    //function to check if mica backdrop is supported on the current system
    private bool IsMicaSupported()
    {
        //check if the current system is running Windows 11
        if (Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract",
                13))
        {
            //check if the current system is running Windows 11 build 22000 or higher
            if (Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent(
                    "Windows.Foundation.UniversalApiContract", 14))
            {
                return true;
            }
            else
            {
                //check if the current system is running Windows 11 build 22000 or higher
                if (Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent(
                        "Windows.Foundation.UniversalApiContract", 13, 1))
                    return true;
                else
                    return false;
            }
        }
        else
        {
            return false;
        }
    }


    public async void LoadSettings()
    {
        try
        {
            localFolder = (await AppPaths.GetLocalFolderAsync());
            settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

            //barcodrod.io defaults
            var historyEnabled = true;
            var backdropIndex = 0;
            var autoCopyToClipboard = false;
            var autoOpenUrl = false;
            var pasteToDecode = false;
            var autoEncodeOnPaste = false;
            var useLegacyDirectShow = false;
            var defaultLaunchPage = "barcodrod.io.ViewModels.DecodeViewModel";
            //if settings.json doesn't exist, create it with barcodrod.io defaults
            if (File.Exists(settingsFilePath) == false)
            {
                var settingsFile = await localFolder.CreateFileAsync("settings.json",
                    CreationCollisionOption.OpenIfExists);
                var data = new
                {
                    HistoryEnabled = historyEnabled,
                    BackdropIndex = backdropIndex,
                    AutoCopyToClipboard = autoCopyToClipboard,
                    AutoEncodeOnPaste = autoEncodeOnPaste,
                    UseLegacyDirectShow = useLegacyDirectShow,
                    DefaultLaunchPage = defaultLaunchPage
                };

                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(settingsFilePath, json);
            }

            //if settings.json exists, update variables above from it's data
            if (File.Exists(settingsFilePath))
            {
                var loadedJson = await File.ReadAllTextAsync(settingsFilePath);
                if (loadedJson != null && loadedJson != "")
                {
                    dynamic loadedData = JsonConvert.DeserializeObject(loadedJson);
                    if (loadedData != null)
                    {
                        historyEnabled = loadedData.HistoryEnabled;
                        backdropIndex = loadedData.BackdropIndex;
                        if (loadedData.AutoCopyToClipboard != null)
                            autoCopyToClipboard = loadedData.AutoCopyToClipboard;
                        if (loadedData.AutoOpenUrl != null)
                            autoOpenUrl = loadedData.AutoOpenUrl;
                        if (loadedData.PasteToDecode != null)
                            pasteToDecode = loadedData.PasteToDecode;
                        if (loadedData.AutoEncodeOnPaste != null)
                            autoEncodeOnPaste = loadedData.AutoEncodeOnPaste;
                        if (loadedData.UseLegacyDirectShow != null)
                            useLegacyDirectShow = loadedData.UseLegacyDirectShow;
                        if (loadedData.DefaultLaunchPage != null)
                            defaultLaunchPage = loadedData.DefaultLaunchPage;

                        if (Backdrops.SelectedIndex != backdropIndex)
                        {
                            ApplyBackdropByIndex(backdropIndex);
                        }
                    }
                }
            }

            SettingsLoaded = true;
            HistoryEnabled.IsChecked = historyEnabled;
            Backdrops.SelectedIndex = backdropIndex;
            AutoCopyToClipboard.IsChecked = autoCopyToClipboard;
            AutoOpenUrl.IsChecked = autoOpenUrl;
            PasteToDecode.IsChecked = pasteToDecode;
            AutoEncodeOnPaste.IsChecked = autoEncodeOnPaste;
            _updatingLegacyDirectShowToggle = true;
            UseLegacyDirectShow.IsChecked = useLegacyDirectShow;
            _updatingLegacyDirectShowToggle = false;
            DefaultLaunchPageSelector.SelectedIndex = defaultLaunchPage switch
            {
                "barcodrod.io.ViewModels.EncodeViewModel" => 1,
                "barcodrod.io.ViewModels.HistoryViewModel" => 2,
                _ => 0
            };

        }
        catch
        {
            SettingsLoaded = false;
            localFolder = (await AppPaths.GetLocalFolderAsync());
            settingsFilePath = Path.Combine(localFolder.Path, "settings.json");
            var settingsFile = await localFolder.CreateFileAsync("settings.json",
                CreationCollisionOption.ReplaceExisting);
            var data = new
            {
                HistoryEnabled = true,
                BackdropIndex = 0,
                AutoEncodeOnPaste = false,
                UseLegacyDirectShow = false,
                DefaultLaunchPage = "barcodrod.io.ViewModels.DecodeViewModel"
            };

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            settingsFilePath = Path.Combine(localFolder.Path, "settings.json");
            File.WriteAllText(settingsFilePath, json);
            HistoryEnabled.IsChecked = true;
            return;
        }
    }


    private async void EnableHistory(object sender, RoutedEventArgs e)
    {
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        //read HistoryEnabled from settings.json
        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        var currentHistorySetting = settings["HistoryEnabled"];
        if (currentHistorySetting != null)
            if (currentHistorySetting.Value<bool>() == true)
                return;

        settings["HistoryEnabled"] = true;
        var output = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(settingsFilePath, output);
    }

    private async void DisableHistory(object sender, RoutedEventArgs e)
    {
        //get the history folder

        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        //get the history folder
        var historyFolder =
            await localFolder.CreateFolderAsync("history", CreationCollisionOption.OpenIfExists);
        //delete all files in history folder

        //read HistoryEnabled from settings.json
        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        var currentHistorySetting = settings["HistoryEnabled"];
        if (currentHistorySetting != null)
            if (currentHistorySetting.Value<bool>() == false)
                return;

        settings["HistoryEnabled"] = false;
        var output = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(settingsFilePath, output);

        var files = await historyFolder.GetFilesAsync();
        //get count of png files in files

        var pngFiles = files.Where(file => file.FileType == ".png");

        if (pngFiles.Count() > 0)
        {
            //prompt the user to confirm the deletion of the history
            var deleteHistoryDialog = new ContentDialog
            {
                Title = "History Disabled",
                Content = "Do you want to delete the " + pngFiles.Count() +
                          " barcode(s) already in your history? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Keep"
            };
            deleteHistoryDialog.XamlRoot = XamlRoot;

            var result = await deleteHistoryDialog.ShowAsync();
            //if the user clicks the delete button, delete the history
            if (result == ContentDialogResult.Primary)
                foreach (var file in files)
                    await file.DeleteAsync();
            //if the user clicks the cancel button, do nothing
            else
                return;
        }
    }

    private async void UpdateBackDrop(object sender, SelectionChangedEventArgs e)
    {
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        var backdropIndex = Backdrops.SelectedIndex;

        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        settings["BackdropIndex"] = backdropIndex;
        var output = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(settingsFilePath, output);
        ApplyBackdropByIndex(backdropIndex);
    }

    private void ApplyBackdropByIndex(int backdropIndex)
    {
        if (backdropIndex == 0)
        {
            var currentBackdrop = App.MainWindow.SystemBackdrop as MicaBackdrop;
            if (currentBackdrop != null && currentBackdrop.Kind == Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt)
                return;

            var backdrop = new MicaBackdrop();
            backdrop.Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt;
            App.MainWindow.SystemBackdrop = backdrop;
            return;
        }

        if (backdropIndex == 1)
        {
            var currentBackdrop = App.MainWindow.SystemBackdrop as MicaBackdrop;
            if (currentBackdrop != null && currentBackdrop.Kind == Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base)
                return;

            var backdrop = new MicaBackdrop();
            backdrop.Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base;
            App.MainWindow.SystemBackdrop = backdrop;
            return;
        }

        if (backdropIndex == 2)
        {
            if (App.MainWindow.SystemBackdrop is DesktopAcrylicBackdrop)
                return;

            var backdrop = new DesktopAcrylicBackdrop();
            App.MainWindow.SystemBackdrop = backdrop;
            return;
        }

        if (backdropIndex == 3)
        {
            if (App.MainWindow.SystemBackdrop is AcrylicAltBackdrop)
                return;

            App.MainWindow.SystemBackdrop = new AcrylicAltBackdrop();
        }
    }

    private async void EnableAutoCopy(object sender, RoutedEventArgs e)
    {
        if (!SettingsLoaded) return;
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        settings["AutoCopyToClipboard"] = true;
        File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
    }

    private async void DisableAutoCopy(object sender, RoutedEventArgs e)
    {
        if (!SettingsLoaded) return;
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        settings["AutoCopyToClipboard"] = false;
        File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
    }

    private async void EnableAutoOpenUrl(object sender, RoutedEventArgs e)
    {
        if (!SettingsLoaded) return;
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        settings["AutoOpenUrl"] = true;
        File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
    }

    private async void DisableAutoOpenUrl(object sender, RoutedEventArgs e)
    {
        if (!SettingsLoaded) return;
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        settings["AutoOpenUrl"] = false;
        File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
    }

    private async void EnablePasteToDecode(object sender, RoutedEventArgs e)
    {
        if (!SettingsLoaded) return;
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        settings["PasteToDecode"] = true;
        File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
    }

    private async void DisablePasteToDecode(object sender, RoutedEventArgs e)
    {
        if (!SettingsLoaded) return;
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        settings["PasteToDecode"] = false;
        File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
    }

    private async void EnableAutoEncodeOnPaste(object sender, RoutedEventArgs e)
    {
        if (!SettingsLoaded) return;
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        settings["AutoEncodeOnPaste"] = true;
        File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
    }

    private async void DisableAutoEncodeOnPaste(object sender, RoutedEventArgs e)
    {
        if (!SettingsLoaded) return;
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);
        settings["AutoEncodeOnPaste"] = false;
        File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
    }

    private async void UpdateDefaultLaunchPage(object sender, SelectionChangedEventArgs e)
    {
        if (!SettingsLoaded) return;

        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        var settings = JObject.Parse(jsonSettings);

        settings["DefaultLaunchPage"] = DefaultLaunchPageSelector.SelectedIndex switch
        {
            1 => "barcodrod.io.ViewModels.EncodeViewModel",
            2 => "barcodrod.io.ViewModels.HistoryViewModel",
            _ => "barcodrod.io.ViewModels.DecodeViewModel"
        };

        File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
    }

    private static bool IsLegacyDirectShowSupportedArchitecture()
    {
        return RuntimeInformation.OSArchitecture == Architecture.X64 || RuntimeInformation.OSArchitecture == Architecture.X86;
    }

    private async void ToggleLegacyDirectShow(object sender, RoutedEventArgs e)
    {
        if (!SettingsLoaded || _updatingLegacyDirectShowToggle)
            return;

        var requestedValue = UseLegacyDirectShow.IsChecked == true;
        var localFolder = (await AppPaths.GetLocalFolderAsync());
        var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        JObject settings;
        if (File.Exists(settingsFilePath))
        {
            var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
            settings = string.IsNullOrWhiteSpace(jsonSettings) ? new JObject() : JObject.Parse(jsonSettings);
        }
        else
        {
            settings = new JObject();
        }

        var currentValue = settings["UseLegacyDirectShow"]?.Value<bool>() ?? false;
        if (currentValue == requestedValue)
            return;

        var restartDialog = new ContentDialog
        {
            Title = "Restart required",
            Content = "Switching webcam engine requires restarting barcodrod.io now. Restart now?",
            PrimaryButtonText = "Restart now",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };

        var dialogResult = await restartDialog.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary)
        {
            _updatingLegacyDirectShowToggle = true;
            UseLegacyDirectShow.IsChecked = currentValue;
            _updatingLegacyDirectShowToggle = false;
            return;
        }

        settings["UseLegacyDirectShow"] = requestedValue;
        File.WriteAllText(settingsFilePath, settings.ToString(Formatting.Indented));
        AppInstance.Restart(string.Empty);
    }
}