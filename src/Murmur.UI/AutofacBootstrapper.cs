using Autofac;
using MediatR.Extensions.Autofac.DependencyInjection;
using MediatR.Extensions.Autofac.DependencyInjection.Builder;
using Murmur.Interop;

namespace Murmur.UI;

public class AutofacBootstrapper
{
    public static IContainer Bootstrap()
    {
        var builder = new ContainerBuilder();

        // Scan every assembly that contains MediatR handlers.
        // Add the Murmur.Core assembly here (via a handler type) once Core has request handlers.
        var configuration = MediatRConfigurationBuilder
            .Create(
                typeof(AppModule).Assembly,
                typeof(ApiBridge).Assembly
            )
            .WithAllOpenGenericHandlerTypesRegistered()
            .Build();

        builder.RegisterMediatR(configuration);
        builder.RegisterModule<AppModule>();

        return builder.Build();
    }
}
