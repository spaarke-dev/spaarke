using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Handoff;
using Sprk.Bff.Api.Services.Dataverse.Extensions;
using Sprk.Bff.Api.Infrastructure.Dataverse;

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

        // Register prompt schema renderer (stateless, singleton)
        services.AddSingleton<PromptSchemaRenderer>();

        // Register lookup choices resolver (scoped: caches per-request, needs IScopeResolverService)
        services.AddScoped<LookupChoicesResolver>();

        // IDataverseUserClient (the user-OBO Dataverse Web API boundary the Dataverse* tool handlers
        // below require) is registered UNCONDITIONALLY in AddSpaarkeCore, not here. It used to be
        // registered here, inside the compound AI gate — correct while only these tool handlers consumed
        // it, and a CLAUDE.md §10 F.1 asymmetric-registration defect once a non-AI caller did. That caller
        // is /api/office/communications/* (unified-access-control-r2 task 127, #1020), which maps
        // unconditionally; with the AI gate off, its three handlers could not bind. (Task 126 moved it for
        // /api/office/search/entities, which spaarkeai-word-add-in-r1 task 062 later reimplemented on an
        // impersonated read — the communications routes are what keep this registration load-bearing.)
        // Do NOT move it back behind the gate. The handlers below still resolve it.

        // spaarkeai-assistant-enhancements-r3 task 020 (FR-06) — GridOverviewHandler injects
        // TimeProvider to compute `today` deterministically server-side. Registered HERE (idempotent
        // TryAddSingleton, matching WorkspaceModule/MembershipModule/CommunicationModule) so the
        // handler resolves even when AddToolFramework is used in isolation — avoids the
        // asymmetric-registration anti-pattern (CLAUDE.md §10 F.1) if the tool framework is added
        // without a feature module that also registers TimeProvider.
        services.TryAddSingleton(TimeProvider.System);

        // unified-access-control-r2 task 052 (FR-26) - EmailDraftToolHandler stamps the core-record
        // ancestor of its regarding target onto the sprk_communication it drafts. The handler is
        // registered by the assembly scan below, so its dependency must be registered HERE too:
        // relying on AddDataverseMetadataServices alone would make the handler unresolvable wherever
        // the tool framework is composed without that module - the asymmetric-registration
        // anti-pattern (CLAUDE.md 10 F.1). Idempotent, same posture as TimeProvider above.
        services.AddCoreAncestorResolver();

        // spaarke-SPA-external-access-platform-r3 task 078 (ISS-010) - the generic dataverse.read_query /
        // search_data / describe handlers decide whether the caller is external (request principal ->
        // ICallerSystemUserResolver -> ISystemUserIdentityResolver.IsExternalAsync). The handlers are
        // registered by the assembly scan below, so the accessor and the caller resolver are registered
        // HERE too, idempotently (AddHttpContextAccessor is a TryAdd; TryAddScoped never clobbers the
        // CommunicationModule / AnalysisServicesModule registration) - CLAUDE.md 10 F.1, same posture as
        // above. ISystemUserIdentityResolver is NOT registered here: NotificationsModule registers it
        // unconditionally from Program.cs (before the AI module composes this framework), and its own
        // dependencies (IDataverseService, IDistributedCache) belong to GraphModule / CacheModule, not to
        // this framework - the same documented posture as CommunicationModule's caller-resolver note.
        services.AddHttpContextAccessor();
        services.TryAddScoped<Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver,
                              Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolver>();

        AddHandoffUrlBuilder(services, configuration);

        // Discover and register all tool handlers from this assembly
        services.AddToolHandlersFromAssembly(Assembly.GetExecutingAssembly());

        // Register the tool handler registry as scoped (matches handler lifetime)
        services.AddScoped<IToolHandlerRegistry, ToolHandlerRegistry>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="HandoffUrlBuilder"/>, the deep-link composer the chat create-record tool
    /// (<c>DataverseCreateRecordHandler</c>) injects. spaarkeai-mcp-bff-api-r2 task 001 moved this registration from the
    /// removed legacy agent module. It is registered HERE because the assembly scan in this method registers its consumer
    /// (CLAUDE.md §10 F.1). It is an idempotent singleton.
    /// </summary>
    /// <remarks>
    /// <b>#3198 (spaarkeai-mcp-bff-api-r2 task 030).</b> The base URL is the validated <c>Dataverse:EnvironmentUrl</c>
    /// (<see cref="DataverseOptions.EnvironmentUrl"/>: <c>[Required]</c>, validated at startup by <c>ConfigurationModule</c>), the
    /// one key the rest of the BFF reads. The old chain fell back to <c>DataverseServiceClient:Url</c> and then to the literal dev
    /// environment, so a customer BFF with the URL under another key silently pointed every deep link (chat refusal links, the MCP
    /// run tools' console link) at Spaarke's dev environment. There is no fallback now: no URL is an
    /// <see cref="OptionsValidationException"/> (see <see cref="HandoffBaseUrl"/>).
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root; when null, the host's <see cref="IConfiguration"/> is used.</param>
    private static void AddHandoffUrlBuilder(IServiceCollection services, IConfiguration? configuration) =>
        services.TryAddSingleton(sp => new HandoffUrlBuilder(HandoffBaseUrl(
            sp.GetService<IOptions<DataverseOptions>>()?.Value,
            configuration ?? sp.GetService<IConfiguration>())));

    /// <summary>
    /// The Dataverse environment URL deep links are built on: <see cref="DataverseOptions.EnvironmentUrl"/>, else the same key read
    /// from <paramref name="configuration"/> (a host that binds no options). Never a default environment (#3198).
    /// </summary>
    /// <exception cref="OptionsValidationException">Neither holds a URL.</exception>
    internal static string HandoffBaseUrl(DataverseOptions? options, IConfiguration? configuration)
    {
        foreach (var candidate in new[] { options?.EnvironmentUrl, configuration?["Dataverse:EnvironmentUrl"] })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        throw new OptionsValidationException(
            DataverseOptions.SectionName,
            typeof(DataverseOptions),
            ["Dataverse:EnvironmentUrl is required: every server-built deep link (HandoffUrlBuilder) is built on it, and there is no default environment (#3198)."]);
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

        // Register prompt schema renderer (stateless, singleton)
        services.AddSingleton<PromptSchemaRenderer>();

        // Register lookup choices resolver (scoped: caches per-request, needs IScopeResolverService)
        services.AddScoped<LookupChoicesResolver>();

        // IDataverseUserClient: see the note in the primary overload — registered unconditionally in
        // AddSpaarkeCore, so both entry points resolve the Dataverse* handlers without registering it here.

        // spaarkeai-assistant-enhancements-r3 task 020 (FR-06) — GridOverviewHandler injects
        // TimeProvider to compute `today` deterministically server-side. Registered HERE (idempotent
        // TryAddSingleton, matching WorkspaceModule/MembershipModule/CommunicationModule) so the
        // handler resolves even when AddToolFramework is used in isolation — avoids the
        // asymmetric-registration anti-pattern (CLAUDE.md §10 F.1) if the tool framework is added
        // without a feature module that also registers TimeProvider.
        services.TryAddSingleton(TimeProvider.System);

        // unified-access-control-r2 task 052 (FR-26) - EmailDraftToolHandler stamps the core-record
        // ancestor of its regarding target onto the sprk_communication it drafts. The handler is
        // registered by the assembly scan below, so its dependency must be registered HERE too:
        // relying on AddDataverseMetadataServices alone would make the handler unresolvable wherever
        // the tool framework is composed without that module - the asymmetric-registration
        // anti-pattern (CLAUDE.md 10 F.1). Idempotent, same posture as TimeProvider above.
        services.AddCoreAncestorResolver();

        // spaarke-SPA-external-access-platform-r3 task 078 (ISS-010) - the generic dataverse.read_query /
        // search_data / describe handlers decide whether the caller is external (request principal ->
        // ICallerSystemUserResolver -> ISystemUserIdentityResolver.IsExternalAsync). The handlers are
        // registered by the assembly scan below, so the accessor and the caller resolver are registered
        // HERE too, idempotently (AddHttpContextAccessor is a TryAdd; TryAddScoped never clobbers the
        // CommunicationModule / AnalysisServicesModule registration) - CLAUDE.md 10 F.1, same posture as
        // above. ISystemUserIdentityResolver is NOT registered here: NotificationsModule registers it
        // unconditionally from Program.cs (before the AI module composes this framework), and its own
        // dependencies (IDataverseService, IDistributedCache) belong to GraphModule / CacheModule, not to
        // this framework - the same documented posture as CommunicationModule's caller-resolver note.
        services.AddHttpContextAccessor();
        services.TryAddScoped<Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver,
                              Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolver>();

        AddHandoffUrlBuilder(services, configuration: null);

        // Discover and register all tool handlers from this assembly
        services.AddToolHandlersFromAssembly(Assembly.GetExecutingAssembly());

        // Register the tool handler registry as scoped (matches handler lifetime)
        services.AddScoped<IToolHandlerRegistry, ToolHandlerRegistry>();

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
            // Use scoped lifetime to allow injection of scoped services (e.g., IScopeResolverService)
            services.AddScoped(handlerInterface, handlerType);
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
        services.AddScoped<IAnalysisToolHandler, THandler>();
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
