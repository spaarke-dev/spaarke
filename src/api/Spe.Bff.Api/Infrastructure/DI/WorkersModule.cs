using Spe.Bff.Api.Services.BackgroundServices;
using Spe.Bff.Api.Services.Jobs;

namespace Spe.Bff.Api.Infrastructure.DI;

public static class WorkersModule
{
    public static IServiceCollection AddWorkersModule(this IServiceCollection services)
    {
        // Register job processor background service
        services.AddHostedService<JobProcessor>();

        // Register job handlers (scan for IJobHandler implementations)
        var assembly = typeof(WorkersModule).Assembly;
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IJobHandler).IsAssignableFrom(t));

        foreach (var handlerType in handlerTypes)
        {
            services.AddScoped(typeof(IJobHandler), handlerType);
        }

        return services;
    }
}