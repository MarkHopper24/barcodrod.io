using barcodrod.io.Helpers;
using Microsoft.UI.Xaml.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace barcodrod.io;

public sealed partial class MainWindow : WindowEx
{
    private string? localFolderPath;
    private string? settingsFilePath;
    private bool SettingsLoaded = false;
    private string? currentLogPath;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Content = null;
        Title = "AppDisplayName".GetLocalized();
        LoadSettings();
    }

    public async void LoadSettings()
    {
        try
        {
            await InitializeLog();
            Log("Initalizing logging.");
            localFolderPath = AppPaths.LocalDataFolder;
            Log("localAppFolder: " + localFolderPath);
            settingsFilePath = Path.Combine(localFolderPath, "settings.json");
            //barcodrod.io defaults
            var historyEnabled = true;
            var backdropIndex = 0;
            Log("Checking for settings file.");
            //if settings.json doesn't exist, create it with barcodrod.io defaults
            if (File.Exists(settingsFilePath) == false)
            {
                Log("Settings file not found. Creating with default values.");
                var data = new
                {
                    HistoryEnabled = historyEnabled,
                    BackdropIndex = backdropIndex
                };

                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(settingsFilePath, json);
                Log("Settings file created.");
            }

            //if settings.json exists, update variables above from it's data
            if (File.Exists(settingsFilePath))
            {
                Log("Settings file found. Loading settings.");

                var jsonSettings = await File.ReadAllTextAsync(settingsFilePath);
                if (jsonSettings != null && jsonSettings != "")
                {
                    var settings = JObject.Parse(jsonSettings);
                    var currentBackDropIndex = settings["BackdropIndex"];

                    Log("Checking backdrop index.");

                    if (currentBackDropIndex != null)
                    {
                        backdropIndex = currentBackDropIndex.Value<int>();
                        Log("Backdrop index: " + currentBackDropIndex.ToString());

                        if (IsMicaSupported() == true)
                        {
                            Log("Mica supported on client: true.");
                            if (backdropIndex == 0)
                            {
                                Log("Setting backdrop to MicaBaseAlt");
                                //set backdrop to micabasealt
                                var backdrop = new MicaBackdrop();
                                backdrop.Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt;
                                App.MainWindow.SystemBackdrop = backdrop;
                            }

                            if (backdropIndex == 1)
                            {
                                Log("Setting backdrop to MicaBase");
                                var backdrop = new MicaBackdrop();
                                backdrop.Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base;
                                App.MainWindow.SystemBackdrop = backdrop;
                            }
                        }

                        if (backdropIndex == 2)
                        {
                            Log("Setting backdrop to Acrylic");
                            var backdrop = new DesktopAcrylicBackdrop();
                            App.MainWindow.SystemBackdrop = backdrop;
                        }

                        if (backdropIndex == 3)
                        {
                            Log("Setting backdrop to Acrylic Alt");
                            var backdrop = new AcrylicAltBackdrop();
                            App.MainWindow.SystemBackdrop = backdrop;
                        }
                    }
                }
            }

            SettingsLoaded = true;
            Log("Settings loaded and applied.");
        }
        catch (Exception ex)
        {
            Log("Error loading settings file. Exception details:");
            Log(ex.ToString());
            Log(ex.GetBaseException().ToString());
            Log(ex.Message);

            if (settingsFilePath == null)
                Log("Path: null");
            else
                Log("Path: " + settingsFilePath.ToString());

            Log("Settings file not found. Creating with default values.");

            SettingsLoaded = false;
            localFolderPath = AppPaths.LocalDataFolder;
            settingsFilePath = Path.Combine(localFolderPath, "settings.json");
            Log("Settings file created.");
            var data = new
            {
                HistoryEnabled = true,
                BackdropIndex = 0
            };

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(settingsFilePath, json);
            Log("Default values written.");

            return;
        }
    }

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

    public async Task InitializeLog()
    {
        try
        {
            localFolderPath = AppPaths.LocalDataFolder;

            //create log file based on current date and time
            var logFileName = "log.txt";
            var logFullPath = Path.Combine(localFolderPath, logFileName);

            //if the log file is greater than 30mb, start a new log file
            if (File.Exists(logFullPath))
            {
                var logSize = new FileInfo(logFullPath).Length;
                if (logSize > 30000000)
                {
                    File.WriteAllText(logFullPath, string.Empty);
                }
            }

            currentLogPath = logFullPath;

            return;
        }
        catch
        {
            return;
        }
    }

    public void Log(string message)
    {
        try
        {
            if (currentLogPath != null)
            {
                var logMessage = DateTime.Now.ToString() + ": " + message + "\n";
                File.AppendAllText(currentLogPath, logMessage);
            }
        }
        catch
        {
            return;
        }
    }
}