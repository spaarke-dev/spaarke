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
// Task 225b (D-12) removed SharedPlatformOpenAiEndpoint (and its Worker Bicep KV
// reference): it served only H12c's retired Model 1 shared-platform branch.
//
// SectionName / NFR-05 Validate() added by task 153, parity with
// EnvVarValuesOptions (task 142) / DataverseEnvironmentRegistryOptions (task
// 122) shape.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

/// <summary>
/// Configuration for H12c. Configuration key: <see cref="SectionName"/> (<c>RuntimeReferences</c>).
/// </summary>
public sealed class RuntimeReferencesOptions
{
    /// <summary>Configuration section name (bound via RuntimeReferencesModule's <c>AddOptions&lt;RuntimeReferencesOptions&gt;()</c>).</summary>
    public const string SectionName = "RuntimeReferences";

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
    internal void Validate()
    {
        if (DataverseRequestTimeout < TimeSpan.FromSeconds(1) || DataverseRequestTimeout > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:DataverseRequestTimeout' must be between 1 second and 5 minutes (actual: {DataverseRequestTimeout}).");
        }
    }
}
