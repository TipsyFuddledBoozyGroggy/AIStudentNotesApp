using CollegeCompanion.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CollegeCompanion.SharedKernel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OwnershipSaveChangesInterceptor>();
        return services;
    }
}
