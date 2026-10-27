using Tether.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.SharedKernel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OwnershipSaveChangesInterceptor>();
        return services;
    }
}
