// -----------------------------------------------------------------------------
// StampBffUrl.cs
//
// Task 258. The stamp BFF's public base URL, derived from its App Service name
// (H2a output InterStepState.AppServiceName) — ONE derivation for the two places
// that need it before and after the BFF is deployed:
//   - H4b writes it as PublicConfig__BffUrl (PerEnvSourceCatalog
//     `from-h2a-output:bff_url`); the BFF refuses to start outside
//     Development/Testing without it (PublicConfigOptionsValidator).
//   - H9 health-probes it and records it as InterStepState.BffApiUrl, which H7
//     writes as sprk_BffApiBaseUrl and H13 calls.
// Two copies of the format could drift, and the browser clients would then be
// told one URL while Dataverse holds another.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers;

/// <summary>The stamp BFF's public base URL (no trailing slash, no <c>/api</c> suffix).</summary>
public static class StampBffUrl
{
    /// <summary>The production slot's URL: <c>https://{appServiceName}.azurewebsites.net</c>.</summary>
    public static string Production(string appServiceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appServiceName);
        return $"https://{appServiceName}.azurewebsites.net";
    }
}
