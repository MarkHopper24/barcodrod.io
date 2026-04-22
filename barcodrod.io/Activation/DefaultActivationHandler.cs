using barcodrod.io.Contracts.Services;

using Microsoft.UI.Xaml;
using Newtonsoft.Json.Linq;
using Windows.Storage;

namespace barcodrod.io.Activation;

public class DefaultActivationHandler : ActivationHandler<Microsoft.UI.Xaml.LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService;

    public DefaultActivationHandler(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    protected override bool CanHandleInternal(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // None of the ActivationHandlers has handled the activation.
        return _navigationService.Frame?.Content == null;
    }

    protected async override Task HandleInternalAsync(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var defaultLaunchPage = await GetDefaultLaunchPageAsync();
        _navigationService.NavigateTo(defaultLaunchPage, args.Arguments);

        await Task.CompletedTask;
    }

    private static async Task<string> GetDefaultLaunchPageAsync()
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var settingsFilePath = Path.Combine(localFolder.Path, "settings.json");
            if (!File.Exists(settingsFilePath))
                return "barcodrod.io.ViewModels.DecodeViewModel";

            var json = await File.ReadAllTextAsync(settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
                return "barcodrod.io.ViewModels.DecodeViewModel";

            var settings = JObject.Parse(json);
            var configuredPage = settings["DefaultLaunchPage"]?.Value<string>();

            if (configuredPage == "barcodrod.io.ViewModels.EncodeViewModel" ||
                configuredPage == "barcodrod.io.ViewModels.HistoryViewModel" ||
                configuredPage == "barcodrod.io.ViewModels.DecodeViewModel")
            {
                return configuredPage;
            }
        }
        catch
        {
        }

        return "barcodrod.io.ViewModels.DecodeViewModel";
    }
}
