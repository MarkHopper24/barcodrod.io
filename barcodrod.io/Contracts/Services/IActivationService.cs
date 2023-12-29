namespace barcodrod.io.Contracts.Services;

public interface IActivationService
{
    Task ActivateAsync(object activationArgs);
}
