using System.Reflection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// DI extension methods for registering the AI Tool Framework.
/// Follows ADR-010 feature module pattern.
/// </summary>
public static class ToolFrameworkExtensions
{
    /// <summary>
    /// Adds the AI Tool Framework services to the service collection.
    /// Discovers and registers all IAnalysisToolHandler implementations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddToolFramework(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration options
        services.Configure<ToolFrameworkOptions>(
            configuration.GetSection(ToolFrameworkOptions.SectionName));

        // Discover and register all tool handlers from this assembly
        services.AddToolHandlersFromAssembly(Assembly.GetExecutingAssembly());

        // Register the tool handler registry as singleton
        services.AddSingleton<IToolHandlerRegistry, ToolHandlerRegistry>();

        return services;
    }

    /// <summary>
    /// Adds the AI Tool Framework with custom options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure tool framework options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddToolFramework(
        this IServiceCollection services,
        Action<ToolFrameworkOptions> configureOptions)
    {
        // Register configuration options
        services.Configure(configureOptions);

        // Discover and register all tool handlers from this assembly
        services.AddToolHandlersFromAssembly(Assembly.GetExecutingAssembly());

        // Register the tool handler registry as singleton
        services.AddSingleton<IToolHandlerRegistry, ToolHandlerRegistry>();

        return services;
    }

    /// <summary>
    /// Discovers and registers all IAnalysisToolHandler implementations from an assembly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddToolHandlersFromAssembly(
        this IServiceCollection services,
        Assembly assembly)
    {
        var handlerInterface = typeof(IAnalysisToolHandler);

        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass
                && !t.IsAbstract
                && handlerInterface.IsAssignableFrom(t))
            .ToList();

        foreach (var handlerType in handlerTypes)
        {
            // Register as IAnalysisToolHandler for enumeration by registry
            services.AddSingleton(handlerInterface, handlerType);
        }

        return services;
    }

    /// <summary>
    /// Registers a specific tool handler type.
    /// Use this for explicit handler registration instead of assembly scanning.
    /// </summary>
    /// <typeparam name="THandler">The tool handler type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddToolHandler<THandler>(this IServiceCollection services)
        where THandler : class, IAnalysisToolHandler
    {
        services.AddSingleton<IAnalysisToolHandler, THandler>();
        return services;
    }

    /// <summary>
    /// Registers a tool handler instance.
    /// Use this for handlers that require special construction.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="handler">The handler instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddToolHandler(
        this IServiceCollection services,
        IAnalysisToolHandler handler)
    {
        services.AddSingleton(typeof(IAnalysisToolHandler), handler);
        return services;
    }
}
