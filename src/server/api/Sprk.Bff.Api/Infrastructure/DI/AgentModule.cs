using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration for the M365 Copilot agent module.
/// Registers agent gateway services, token exchange, card formatting,
/// conversation management, configuration, telemetry, and error handling.
/// </summary>
public static class AgentModule
{
    public static IServiceCollection AddAgentModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Agent token exchange (OBO flow for M365 → Graph/Dataverse)
        services.AddOptions<AgentTokenOptions>()
            .Bind(configuration.GetSection(AgentTokenOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AgentTokenOptions>, AgentTokenOptionsValidator>();
        services.AddScoped<AgentTokenService>();

        // Agent configuration (playbook visibility, role restrictions, feature toggles)
        services.AddOptions<AgentConfigurationOptions>()
            .Bind(configuration.GetSection(AgentConfigurationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<AgentConfigurationService>();

        // Adaptive Card formatter (loads templates, transforms responses → card JSON)
        var cardTemplatesPath = Path.Combine(
            AppContext.BaseDirectory, "cards");
        services.AddSingleton(new AdaptiveCardFormatterService(cardTemplatesPath));

        // Handoff URL builder (deep-links to Analysis Workspace + wizard Code Pages)
        var dataverseUrl = configuration["Dataverse:EnvironmentUrl"]
            ?? configuration["DataverseServiceClient:Url"]
            ?? "https://spaarkedev1.crm.dynamics.com";
        services.AddSingleton(new HandoffUrlBuilder(dataverseUrl));

        // Conversation management (maps M365 conversations → BFF chat sessions)
        services.AddSingleton<AgentConversationService>();

        // Playbook invocation orchestration
        services.AddScoped<PlaybookInvocationService>();

        // Email drafting
        services.AddScoped<EmailDraftService>();

        // Async playbook status tracking
        services.AddSingleton<AgentPlaybookStatusService>();

        // Error handling (user-friendly Adaptive Card errors)
        services.AddSingleton<AgentErrorHandler>();

        // Telemetry (interaction logging, playbook metrics)
        services.AddSingleton<AgentTelemetry>();

        return services;
    }
}
