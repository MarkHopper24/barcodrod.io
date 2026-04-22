using barcodrod.io.Contracts.Services;

namespace barcodrod.io.Activation;

public class FilePathActivationHandler : ActivationHandler<string>
{
    private readonly INavigationService _navigationService;

    public FilePathActivationHandler(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    protected override bool CanHandleInternal(string args)
    {
        return !string.IsNullOrWhiteSpace(args);
    }

    protected override Task HandleInternalAsync(string args)
    {
        _navigationService.NavigateTo("barcodrod.io.ViewModels.DecodeViewModel", args);
        return Task.CompletedTask;
    }
}
