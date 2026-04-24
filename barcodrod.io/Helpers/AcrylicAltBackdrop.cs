using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace barcodrod.io.Helpers;

public sealed class AcrylicAltBackdrop : Microsoft.UI.Xaml.Media.SystemBackdrop
{
    private DesktopAcrylicController? _acrylicController;
    private ICompositionSupportsSystemBackdrop? _connectedTarget;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        if (_acrylicController is not null)
        {
            if (_connectedTarget is not null)
            {
                _acrylicController.RemoveSystemBackdropTarget(_connectedTarget);
            }

            _acrylicController.Dispose();
            _acrylicController = null;
            _connectedTarget = null;
        }

        _acrylicController = new DesktopAcrylicController();
        _connectedTarget = connectedTarget;

        var defaultConfiguration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
        ApplyDynamicTint(defaultConfiguration.Theme);
        _acrylicController.SetSystemBackdropConfiguration(defaultConfiguration);
        _acrylicController.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        if (_acrylicController is null || _connectedTarget is null)
        {
            return;
        }

        if (!ReferenceEquals(target, _connectedTarget))
        {
            return;
        }

        var defaultConfiguration = GetDefaultSystemBackdropConfiguration(_connectedTarget, xamlRoot);
        ApplyDynamicTint(defaultConfiguration.Theme);

        try
        {
            _acrylicController.SetSystemBackdropConfiguration(defaultConfiguration);
        }
        catch (ArgumentException)
        {
            return;
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        if (_acrylicController is not null && _connectedTarget is not null && ReferenceEquals(disconnectedTarget, _connectedTarget))
        {
            _acrylicController.RemoveSystemBackdropTarget(disconnectedTarget);
            _acrylicController.Dispose();
            _acrylicController = null;
            _connectedTarget = null;
        }
    }

    private void ApplyDynamicTint(SystemBackdropTheme theme)
    {
        if (_acrylicController is null)
        {
            return;
        }

        var isDarkTheme = theme == SystemBackdropTheme.Dark;

        _acrylicController.TintColor = isDarkTheme
            ? Color.FromArgb(255, 30, 30, 30)
            : Color.FromArgb(255, 248, 248, 248);

        _acrylicController.FallbackColor = isDarkTheme
            ? Color.FromArgb(255, 24, 24, 24)
            : Color.FromArgb(255, 252, 252, 252);

        _acrylicController.TintOpacity = isDarkTheme ? 0.80f : 0.60f;
        _acrylicController.LuminosityOpacity = isDarkTheme ? 0.72f : 0.82f;
    }
}
