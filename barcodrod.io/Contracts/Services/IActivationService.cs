using System;
using System.Threading.Tasks;

namespace barcodrod.io.Contracts.Services;

public interface IActivationService
{
    Task ActivateAsync(object activationArgs);
}
