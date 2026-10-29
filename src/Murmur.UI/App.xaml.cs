using System.IO;
using System.Windows;
using Autofac;
using Serilog;
using Wpf.Ui.Appearance;

namespace Murmur.UI;

public partial class App : Application
{
    private IContainer? _container;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Murmur", "Logs", "Murmur-.log");

        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .Enrich.FromLogContext()
            .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                fileSizeLimitBytes: 50_000_000,
                rollOnFileSizeLimit: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Murmur starting");

        if (!IsWebView2RuntimeInstalled())
        {
            MessageBox.Show(
                "WebView2 runtime is required. Please install it from https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                "Missing Dependency",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "UI thread exception");
            args.Handled = args.Exception is not (OutOfMemoryException or StackOverflowException);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Log.Fatal(ex, "Unhandled exception");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        _container = AutofacBootstrapper.Bootstrap();

        // Register API endpoints here: one MediatR request per IPC call, e.g.
        //   var registry = _container.Resolve<ApiRegistry>();
        //   registry.Register<GetThingRequest>("thingService", "getThing");

        // The theme is fixed to dark and applied before the window is created to avoid a flicker.
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        var mainWindow = _container.Resolve<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _container?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static bool IsWebView2RuntimeInstalled()
    {
        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrEmpty(version);
        }
        catch
        {
            return false;
        }
    }
}
