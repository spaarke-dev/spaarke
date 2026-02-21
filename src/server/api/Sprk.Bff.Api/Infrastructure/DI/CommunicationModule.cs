using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Services.Communication;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Communication Service (ADR-010: feature module pattern).
/// Registers communication services and configuration.
/// </summary>
public static class CommunicationModule
{
    public static IServiceCollection AddCommunicationModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind CommunicationOptions from "Communication" section
        services.Configure<CommunicationOptions>(configuration.GetSection(CommunicationOptions.SectionName));

        // Core services (singleton: all dependencies are singleton or options)
        services.AddSingleton<ApprovedSenderValidator>();
        services.AddSingleton<CommunicationService>();
        services.AddSingleton<EmlGenerationService>();

        // AI tool handler (IAiToolHandler — not auto-discovered by ToolFramework which scans IAnalysisToolHandler only)
        services.AddSingleton<SendCommunicationToolHandler>();

        return services;
    }
}
