using barcodrod.io.Activation;
using barcodrod.io.Contracts.Services;
using barcodrod.io.Models;
using barcodrod.io.Services;
using barcodrod.io.ViewModels;
using barcodrod.io.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Xaml;
using System.Linq;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace barcodrod.io;

public partial class App : Application
{
    public IHost Host { get; }

    public static T GetService<T>()
        where T : class
    {
        if ((Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");

        return service;
    }

    public static WindowEx MainWindow { get; } = new MainWindow();

    public App()
    {
        InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder().UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices((context, services) =>
            {
                // Default Activation Handler
                services.AddTransient<ActivationHandler<Microsoft.UI.Xaml.LaunchActivatedEventArgs>, DefaultActivationHandler>();
                services.AddTransient<IActivationHandler, FilePathActivationHandler>();

                // Services
                services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
                services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
                services.AddTransient<INavigationViewService, NavigationViewService>();

                services.AddSingleton<IActivationService, ActivationService>();
                services.AddSingleton<IPageService, PageService>();
                services.AddSingleton<INavigationService, NavigationService>();

                // Core Services
                services.AddSingleton<IFileService, FileService>();

                // Views and ViewModels
                services.AddTransient<HistoryPage>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<EncodePage>();
                services.AddTransient<DecodePage>();
                services.AddTransient<ShellPage>();
                services.AddTransient<ShellViewModel>();

                // Configuration
                services.Configure<LocalSettingsOptions>(
                    context.Configuration.GetSection(nameof(LocalSettingsOptions)));
            }).Build();

        UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "barcodrod.io");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"{DateTime.Now:o}\t{e.Message}\n{e.Exception}\n\n");
        }
        catch
        {
            // Never let crash logging itself crash the app.
        }

        // call e.Handled = true to prevent the app from closing when this method returns.
        e.Handled = true;
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activatedArgs.Kind == ExtendedActivationKind.File && activatedArgs.Data is IFileActivatedEventArgs fileActivatedArgs)
        {
            var filePath = fileActivatedArgs.Files?.OfType<StorageFile>().FirstOrDefault()?.Path;
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                await GetService<IActivationService>().ActivateAsync(filePath);
                return;
            }
        }

        // Classic (unpackaged / MSI) file association passes the file as a command-line argument
        // rather than via the File activation contract used by the packaged (MSIX) build.
        var commandLineFilePath = GetFilePathFromCommandLine();
        if (!string.IsNullOrWhiteSpace(commandLineFilePath))
        {
            await GetService<IActivationService>().ActivateAsync(commandLineFilePath);
            return;
        }

        await GetService<IActivationService>().ActivateAsync(args);
    }

    private static string? GetFilePathFromCommandLine()
    {
        var commandLineArgs = Environment.GetCommandLineArgs();

        // Index 0 is the executable path. Skip Windows App SDK activation tokens (e.g. the
        // "----AppNotificationActivated:" argument used by the toast COM activator) and return
        // the first argument that resolves to an existing file.
        for (var i = 1; i < commandLineArgs.Length; i++)
        {
            var arg = commandLineArgs[i];
            if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith("----"))
            {
                continue;
            }

            if (File.Exists(arg))
            {
                return arg;
            }
        }

        return null;
    }
}