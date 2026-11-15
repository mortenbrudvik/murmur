using Autofac;
using Autofac.Extensions.DependencyInjection;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Murmur.Core.Services;
using Murmur.Interop;

namespace Murmur.UI;

public class AutofacBootstrapper
{
    public static IContainer Bootstrap()
    {
        // Mediator's generated AddMediator only speaks Microsoft DI; Autofac takes the registrations over (Populate), so
        // handlers are still built by Autofac with everything AppModule registers.
        var services = new ServiceCollection();
        services.AddMediator((MediatorOptions options) =>
        {
            // Per call: handlers have always been per-dependency here; Mediator's own default is singleton.
            options.ServiceLifetime = ServiceLifetime.Transient;
            // Every assembly that holds requests, notifications or handlers. Must be compile-time constants.
            options.Assemblies = [typeof(AppModule), typeof(ApiBridge), typeof(MurmurPaths)];
        });

        var builder = new ContainerBuilder();
        builder.Populate(services);
        builder.RegisterModule<AppModule>();

        return builder.Build();
    }
}
