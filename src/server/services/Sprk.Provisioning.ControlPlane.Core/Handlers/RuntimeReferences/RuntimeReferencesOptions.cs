// -----------------------------------------------------------------------------
// RuntimeReferencesOptions.cs
//
// Bound options for the H12c runtime references handler + its
// DataverseWebApiModelDeploymentReferenceWriter collaborator. Loaded from the
// "RuntimeReferences" configuration section by RuntimeReferencesModule.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

/// <summary>
/// Configuration for H12c. Configuration key: <c>RuntimeReferences</c>.
/// </summary>
public sealed class RuntimeReferencesOptions
{
    /// <summary>
    /// Shared-platform Azure OpenAI endpoint used for EVERY Model1Shared
    /// customer in this L2 deployment's environment (dev/staging/prod — one
    /// value per environment, NOT per-customer). Per design.md §4.1a: Model 1
    /// shares the platform's fixed-floor OpenAI resource; customers are
    /// differentiated only by per-tenant metering attribution (task 077),
    /// never by a distinct endpoint. Null/blank until the operator configures
    /// it — H12c fails Resumable on any Model1Shared run until set.
    /// </summary>
    public string? SharedPlatformOpenAiEndpoint { get; set; }

    /// <summary>Per-request timeout for Dataverse Web API HTTP calls.</summary>
    public TimeSpan DataverseRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
