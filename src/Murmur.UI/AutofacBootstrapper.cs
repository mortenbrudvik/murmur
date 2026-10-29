using Autofac;
using MediatR.Extensions.Autofac.DependencyInjection;
using MediatR.Extensions.Autofac.DependencyInjection.Builder;
using Murmur.Core.Services;
using Murmur.Interop;

namespace Murmur.UI;

public class AutofacBootstrapper
{
    public static IContainer Bootstrap()
    {
        var builder = new ContainerBuilder();

        // Scan every assembly that contains MediatR requests, notifications and handlers.
        var configuration = MediatRConfigurationBuilder
            .Create(
                typeof(AppModule).Assembly,
                typeof(ApiBridge).Assembly,
                typeof(MurmurPaths).Assembly
            )
            .WithAllOpenGenericHandlerTypesRegistered()
            .Build();

        builder.RegisterMediatR(configuration);
        builder.RegisterModule<AppModule>();

        return builder.Build();
    }
}
