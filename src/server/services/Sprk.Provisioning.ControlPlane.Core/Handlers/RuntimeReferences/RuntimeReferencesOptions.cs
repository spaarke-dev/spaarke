// -----------------------------------------------------------------------------
// RuntimeReferencesOptions.cs
//
// Bound options for the H12c runtime references handler + its
// DataverseWebApiModelDeploymentReferenceWriter collaborator. Loaded from the
// "RuntimeReferences" configuration section by RuntimeReferencesModule.
//
// TASK 153 (Wave G-5, credential-config confirmation): H12c's Dataverse Web
// API auth is ALREADY correct on landing (DataverseWebApiModelDeploymentReference
// Writer.AcquireTokenAsync uses DefaultAzureCredential(TenantId=...) — the SAME
// UAMI-as-Dataverse-App-User idiom every post-H10 handler uses, since H12c
// dispatches after H10 in the DAG per design.md §4.1. There is NO client-secret
// credential to provision for H12c (unlike H7/task 142, which authenticates
// BEFORE H10 exists and therefore needs a confidential-client secret) — see
// notes/task-153-h12c-credential-config-deviations.md D-153-1.
//
// The ONE genuinely unwired config knob task 153 found was
// SharedPlatformOpenAiEndpoint below — it had NO Bicep app-setting wiring
// anywhere (grep-verified against modules/controlplane-worker-app-service.bicep
// pre-task-153), meaning every Model1Shared customer's H12c run would fail
// Resumable at runtime with MissingSharedPlatformEndpointConfiguration. Task
// 153 wires it as a KV-reference app setting sourced from the SAME canonical
// "AzureOpenAI-Endpoint" KV secret the BFF itself already resolves for its own
// AzureOpenAI:Endpoint / DocumentIntelligence:OpenAiEndpoint config (per
// scripts/canonical-secret-catalog/manifest.yaml) — single source of truth for
// the shared platform OpenAI endpoint value, not a second copy.
//
// SectionName / NFR-05 Validate() added by task 153, parity with
// EnvVarValuesOptions (task 142) / DataverseEnvironmentRegistryOptions (task
// 122) shape — see Validate() doc comment below for why
// SharedPlatformOpenAiEndpoint is deliberately NOT boot-required.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

/// <summary>
/// Configuration for H12c. Configuration key: <see cref="SectionName"/> (<c>RuntimeReferences</c>).
/// </summary>
public sealed class RuntimeReferencesOptions
{
    /// <summary>Configuration section name (bound via RuntimeReferencesModule's <c>AddOptions&lt;RuntimeReferencesOptions&gt;()</c>).</summary>
    public const string SectionName = "RuntimeReferences";

    /// <summary>
    /// Shared-platform Azure OpenAI endpoint used for EVERY Model1Shared
    /// customer in this L2 deployment's environment (dev/staging/prod — one
    /// value per environment, NOT per-customer). Per design.md §4.1a: Model 1
    /// shares the platform's fixed-floor OpenAI resource; customers are
    /// differentiated only by per-tenant metering attribution (task 077),
    /// never by a distinct endpoint. Null/blank until the operator configures
    /// it — H12c fails Resumable on any Model1Shared run until set. As of
    /// task 153, <c>modules/controlplane-worker-app-service.bicep</c> wires
    /// this UNCONDITIONALLY as a Key Vault app-setting reference
    /// (<c>RuntimeReferences__SharedPlatformOpenAiEndpoint</c> -&gt;
    /// <c>@Microsoft.KeyVault(VaultName=…;SecretName=AzureOpenAI-Endpoint)</c>)
    /// in every deployed environment — but see <see cref="Validate"/> for why
    /// this specific field is intentionally NOT part of the boot-time
    /// fail-fast check (it is a Model2-only-safe field, unlike
    /// EnvVarValuesOptions.ClientSecret / DataverseEnvironmentRegistryOptions.AdminEnvironmentUrl,
    /// which every run needs).
    /// </summary>
    public string? SharedPlatformOpenAiEndpoint { get; set; }

    /// <summary>Per-request timeout for Dataverse Web API HTTP calls.</summary>
    public TimeSpan DataverseRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Startup validation applied by RuntimeReferencesModule's
    /// <c>AddOptions&lt;RuntimeReferencesOptions&gt;().ValidateOnStart()</c>
    /// registration (task 153, Wave G-5). Throws
    /// <see cref="InvalidOperationException"/> on invalid values so a
    /// misconfigured Worker fails fast at boot (NFR-05 parity with
    /// <c>EnvVarValuesOptions.Validate</c> / <c>DataverseEnvironmentRegistryOptions.Validate</c>).
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT require <see cref="SharedPlatformOpenAiEndpoint"/>
    /// to be non-empty, unlike EnvVarValuesOptions.ClientSecret's unconditional
    /// boot requirement. Reason: SharedPlatformOpenAiEndpoint is ONLY consulted
    /// on H12c's Model1Shared branch (H12cRuntimeReferencesHandler.cs step 5)
    /// — a Worker serving ONLY Model2Dedicated customers in some environment
    /// would crash-loop at every boot for a setting it never uses, which is
    /// the exact silent-fail-in-reverse this project's NFR-05 pattern exists
    /// to avoid causing. The existing per-run runtime guard already classifies
    /// a missing value as Resumable (design.md §4C rollback table) — the
    /// correct, narrower failure surface for a conditionally-required field.
    /// Boot-time validation here covers only the field EVERY H12c run needs
    /// regardless of tenancy model: <see cref="DataverseRequestTimeout"/>.
    /// </remarks>
    internal void Validate()
    {
        if (DataverseRequestTimeout < TimeSpan.FromSeconds(1) || DataverseRequestTimeout > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:DataverseRequestTimeout' must be between 1 second and 5 minutes (actual: {DataverseRequestTimeout}).");
        }
    }
}
