using barcodrod.io.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Windows.Storage;

namespace barcodrod.io.Views;

// TODO: Set the URL for your privacy policy by updating SettingsPage_PrivacyTermsLink.NavigateUri in Resources.resw.
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        LoadSettings();
        //Backdrops.SelectionChanged += UpdateBackDrop;

    }

    //function to check if mica backdrop is supported on the current system
    private bool IsMicaSupported()
    {
        //check if the current system is running Windows 11
        if (Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 13))
        {
            //check if the current system is running Windows 11 build 22000 or higher
            if (Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 14))
            {
                return true;
            }
            else
            {
                //check if the current system is running Windows 11 build 22000 or higher
                if (Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 13, 1))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
        else
        {
            return false;
        }

    }



    private async void LoadSettings()
    {
        if (IsMicaSupported() == false)
        {
            Backdrops.Visibility = Visibility.Collapsed;
        }
        var localFolder = ApplicationData.Current.LocalFolder;
        String? settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        //barcodrod.io defaults
        bool userModified = false;
        bool historyEnabled = true;
        int backdropIndex = 0;

        //if settings.json doesn't exist, create it with barcodrod.io defaults
        if (File.Exists(settingsFilePath) == false)
        {
            var settingsFile = await localFolder.CreateFileAsync("settings.json", Windows.Storage.CreationCollisionOption.OpenIfExists);
            var data = new
            {
                UserModified = userModified,
                HistoryEnabled = historyEnabled,
                BackdropIndex = backdropIndex
            };

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(settingsFilePath, json);
        }
        //if settings.json exists, update variables above from it's data
        if (File.Exists(settingsFilePath))
        {
            string? loadedJson = await File.ReadAllTextAsync(settingsFilePath);


            dynamic loadedData = JsonConvert.DeserializeObject(loadedJson);
            if (loadedData != null)
            {
                userModified = loadedData.UserModified;
                historyEnabled = loadedData.HistoryEnabled;
                backdropIndex = loadedData.BackdropIndex;
            }

        }

        //set UI elements to values from settings.json
        HistoryEnabled.IsChecked = historyEnabled;
        Backdrops.SelectedIndex = backdropIndex;

        //Backdrops.SelectionChanged += UpdateBackDrop;

    }


    private async void EnableHistory(object sender, RoutedEventArgs e)
    {
        var localFolder = ApplicationData.Current.LocalFolder;
        String settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        //read HistoryEnabled from settings.json
        string jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        JObject settings = JObject.Parse(jsonSettings);
        var currentHistorySetting = settings["HistoryEnabled"];
        if (currentHistorySetting != null)
        {
            if (currentHistorySetting.Value<bool>() == true)
            {
                return;
            }
        }

        settings["HistoryEnabled"] = true;
        string output = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(settingsFilePath, output);
    }

    private async void DisableHistory(object sender, RoutedEventArgs e)
    {
        //get the history folder

        var localFolder = ApplicationData.Current.LocalFolder;
        String settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        //get the history folder
        var historyFolder = await localFolder.CreateFolderAsync("history", Windows.Storage.CreationCollisionOption.OpenIfExists);
        //delete all files in history folder

        //read HistoryEnabled from settings.json
        string jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        JObject settings = JObject.Parse(jsonSettings);
        var currentHistorySetting = settings["HistoryEnabled"];
        if (currentHistorySetting != null)
        {
            if (currentHistorySetting.Value<bool>() == false)
            {
                return;
            }
        }

        settings["HistoryEnabled"] = false;
        string output = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(settingsFilePath, output);

        var files = await historyFolder.GetFilesAsync();
        //get count of png files in files

        var pngFiles = files.Where(file => file.FileType == ".png");

        if (pngFiles.Count() > 0)
        {
            //prompt the user to confirm the deletion of the history
            ContentDialog deleteHistoryDialog = new ContentDialog
            {
                Title = "History Disabled",
                Content = "Do you want to delete the " + pngFiles.Count() + " barcode(s) already in your history? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Keep"
            };
            deleteHistoryDialog.XamlRoot = this.XamlRoot;

            ContentDialogResult result = await deleteHistoryDialog.ShowAsync();
            //if the user clicks the delete button, delete the history
            if (result == ContentDialogResult.Primary)
            {
                foreach (var file in files)
                {
                    await file.DeleteAsync();
                }
            }
            //if the user clicks the cancel button, do nothing
            else
            {
                return;
            }
        }

    }
    private async void UpdateBackDrop(object sender, SelectionChangedEventArgs e)
    {
        var localFolder = ApplicationData.Current.LocalFolder;
        String settingsFilePath = Path.Combine(localFolder.Path, "settings.json");

        int backdropIndex = Backdrops.SelectedIndex;

        string jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
        JObject settings = JObject.Parse(jsonSettings);
        settings["BackdropIndex"] = backdropIndex;
        string output = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(settingsFilePath, output);
        var currentBackdrop = App.MainWindow.SystemBackdrop;

        if (backdropIndex == 0)
        {
            //get MainWindow's SystemBackrop and check if it's a MicaBackdrop
            if (currentBackdrop is MicaBackdrop)
            {
                //check if it's a MicaKind.BaseAlt
                if (((MicaBackdrop)currentBackdrop).Kind == Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt)
                {
                    return;
                }
                else
                {
                    MicaBackdrop backdrop = new MicaBackdrop();
                    backdrop.Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt;
                    App.MainWindow.SystemBackdrop = backdrop;
                }

            }
            else
            {
                MicaBackdrop backdrop = new MicaBackdrop();
                backdrop.Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt;
                App.MainWindow.SystemBackdrop = backdrop;
            }




        }

        else if (backdropIndex == 1)
        {
            if (currentBackdrop is MicaBackdrop)
            {
                //check if it's a MicaKind.BaseAlt
                if (((MicaBackdrop)currentBackdrop).Kind == Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base)
                {
                    return;

                }
                else
                {
                    MicaBackdrop backdrop = new MicaBackdrop();
                    backdrop.Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base;
                    App.MainWindow.SystemBackdrop = backdrop;
                }
            }
            else
            {
                MicaBackdrop backdrop = new MicaBackdrop();
                backdrop.Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base;
                App.MainWindow.SystemBackdrop = backdrop;
            }
        }

        else if (backdropIndex == 2)
        {
            if (currentBackdrop is DesktopAcrylicBackdrop)
            {
                return;

            }
            else
            {
                DesktopAcrylicBackdrop backdrop = new DesktopAcrylicBackdrop();
                App.MainWindow.SystemBackdrop = backdrop;
            }



        }

    }


}
