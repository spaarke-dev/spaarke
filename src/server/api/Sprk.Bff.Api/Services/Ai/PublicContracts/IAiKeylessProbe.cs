using Spaarke.Contracts.Provisioning;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Facade (ADR-013) for the AI-owned half of the keyless proof (task 230b, owner D13): one real, minimal,
/// side-effect-free call to each AI-owned Azure service of this stamp, authenticated with the BFF's managed
/// identity — Azure OpenAI chat and embeddings, Document Intelligence, AI Search, Cosmos DB, the session-file
/// Blob container, and Content Safety (Prompt Shield and groundedness).
/// </summary>
/// <remarks>
/// <para>Consumed by <c>Infrastructure/Diagnostics/KeylessProofService</c>, which adds the platform services and is
/// called by provisioning's acceptance gate (H13) through <see cref="KeylessProofContract.Route"/>.</para>
/// <para>A service whose key setting is configured is reported as <see cref="KeylessProofContract.Outcomes.KeyCredential"/> and
/// is NOT called: a call made with a key proves nothing about the identity, and a stamp must hold no key at all.</para>
/// </remarks>
public interface IAiKeylessProbe
{
    /// <summary>Runs every AI-owned probe and returns one result per service, in a stable order.</summary>
    Task<IReadOnlyList<KeylessProbeResult>> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The result of one service's keyless probe. Carries a status and timing only — never data, a secret or exception
/// text (task 230b auth constraint). <see cref="Code"/> is a short machine code (for example <c>ok</c>,
/// <c>http-403</c>, <c>token-unavailable</c>, <c>setting-missing:AzureOpenAI:Endpoint</c>).
/// </summary>
/// <param name="Service">Stable service id — one of <see cref="KeylessProofContract.Services"/>.</param>
/// <param name="Outcome">One of <see cref="KeylessProofContract.Outcomes"/>.</param>
/// <param name="StatusCode">The HTTP status the service answered with, when there was one.</param>
/// <param name="ElapsedMs">Wall-clock time spent on the probe.</param>
/// <param name="Code">Short machine code explaining the outcome.</param>
public sealed record KeylessProbeResult(string Service, string Outcome, int? StatusCode, long ElapsedMs, string Code);
