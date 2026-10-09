using Sprk.Bff.Api.Infrastructure.Diagnostics;
using Sprk.Bff.Api.Services.Ai.Diagnostics;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI for the keyless proof (task 230b, owner D13) behind <c>POST /api/platform/keyless-proof</c> (ADR-010 feature
/// module). Registered UNCONDITIONALLY because the route maps unconditionally (bff-extensions §F.1); neither class
/// depends on a conditionally registered service — the AI probe builds its own SDK clients from the shared
/// <c>TokenCredential</c> and configuration.
/// </summary>
public static class KeylessProofModule
{
    /// <summary>Registers the AI-owned probe facade and the proof service.</summary>
    public static IServiceCollection AddKeylessProofModule(this IServiceCollection services)
    {
        services.AddSingleton<IAiKeylessProbe, AiKeylessProbe>();
        services.AddSingleton<KeylessProofService>();
        return services;
    }
}
