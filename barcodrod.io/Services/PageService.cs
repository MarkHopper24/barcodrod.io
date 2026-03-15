using barcodrod.io.Contracts.Services;
using barcodrod.io.Views;

using Microsoft.UI.Xaml.Controls;

namespace barcodrod.io.Services;

public class PageService : IPageService
{
    private readonly Dictionary<string, Type> _pages = new();

    public PageService()
    {
        Configure<DecodePage>("barcodrod.io.ViewModels.DecodeViewModel");
        Configure<EncodePage>("barcodrod.io.ViewModels.EncodeViewModel");
        Configure<SettingsPage>("barcodrod.io.ViewModels.SettingsViewModel");
        Configure<HistoryPage>("barcodrod.io.ViewModels.HistoryViewModel");
    }

    public Type GetPageType(string key)
    {
        Type? pageType;
        lock (_pages)
        {
            if (!_pages.TryGetValue(key, out pageType))
            {
                throw new ArgumentException($"Page not found: {key}. Did you forget to call PageService.Configure?");
            }
        }

        return pageType;
    }

    private void Configure<V>(string key)
        where V : Page
    {
        lock (_pages)
        {
            if (_pages.ContainsKey(key))
            {
                throw new ArgumentException($"The key {key} is already configured in PageService");
            }

            var type = typeof(V);
            if (_pages.Any(p => p.Value == type))
            {
                throw new ArgumentException($"This type is already configured with key {_pages.First(p => p.Value == type).Key}");
            }

            _pages.Add(key, type);
        }
    }
}
