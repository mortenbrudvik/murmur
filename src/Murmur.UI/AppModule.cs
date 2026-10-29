using Autofac;
using Murmur.Interop;
using Murmur.WebView;

namespace Murmur.UI;

public class AppModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // WebView
        builder.RegisterType<WebViewControl>()
            .AsSelf()
            .As<IWebViewInterop>()
            .SingleInstance();

        // Interop Bridge
        builder.RegisterType<ApiBridge>().SingleInstance();
        builder.RegisterType<ApiEventAggregator>().SingleInstance();
        builder.RegisterType<ApiRegistry>().SingleInstance();

        // ViewModels
        builder.RegisterType<MainViewModel>().SingleInstance();

        // Views
        builder.RegisterType<MainWindow>().SingleInstance();
    }
}
