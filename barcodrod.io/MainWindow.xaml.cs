using barcodrod.io.Helpers;
using barcodrod.io.Views;
using Microsoft.UI.Xaml.Media;
using Newtonsoft.Json;
using Windows.Storage;

namespace barcodrod.io;

public sealed partial class MainWindow : WindowEx
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Content = null;
        Title = "AppDisplayName".GetLocalized();
        LoadSettings();

    }

    private async void LoadSettings()
    {
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
                if (backdropIndex == 0)
                {
                    //set backdrop to micabasealt
                    MicaBackdrop backdrop = new MicaBackdrop();
                    backdrop.Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt;
                    App.MainWindow.SystemBackdrop = backdrop;

                }

                if (backdropIndex == 1)
                {
                    MicaBackdrop backdrop = new MicaBackdrop();
                    backdrop.Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base;
                    App.MainWindow.SystemBackdrop = backdrop;

                }


                if (backdropIndex == 2)
                {
                    DesktopAcrylicBackdrop backdrop = new DesktopAcrylicBackdrop();
                    App.MainWindow.SystemBackdrop = backdrop;

                }

                if (backdropIndex == 3)
                {
                    //remove backdrop
                    App.MainWindow.SystemBackdrop = null;

                }
            }

        }
    }
}
