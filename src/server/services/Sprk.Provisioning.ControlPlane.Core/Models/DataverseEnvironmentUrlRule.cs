// ---------------------------------------------------------------------------
// DataverseEnvironmentUrlRule.cs — which Dataverse environment a run may adopt.
//
// T228 (owner D4 / Q1, 2026-09-30): the operator creates the customer's Dataverse environment and gives its URL at
// intake; H5 adopts it and never creates one. Every Model 1 environment lives in Spaarke's own tenant, so a mistyped
// URL could name ANOTHER customer's environment — and H6, H7, H8 and H10 would then import, write and bind into it.
// The guard is the environment's name: its domain (the host's first label) must be `spaarke-{customerId}` or
// `spaarke-{customerId}-{environmentName}` (AZURE-RESOURCE-NAMING-CONVENTION.md § Dataverse Environments). A label is
// matched whole, so another customer's id can never pass — `spaarke-acme` is not `spaarke-acmex`.
//
// Commercial cloud only (`*.crm[N].dynamics.com`): Model 1 lives in Spaarke's commercial tenant. Sovereign clouds
// (crm.microsoftdynamics.us, crm.appsplatform.us, crm.dynamics.cn) are refused — supporting them (Model 2) means
// changing this rule.
//
// ONE rule, applied at POST /api/runs (reject before anything is created) and again by H5 (reject a run document that
// predates the rule). Reject, never repair: the operator fixes the environment's name or the intake value.
// ---------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Sprk.Provisioning.ControlPlane.Core.Models;

/// <summary>The intake rule for the customer's Dataverse environment URL (T228).</summary>
public static class DataverseEnvironmentUrlRule
{
    /// <summary>A Dataverse environment host: <c>{domain}.crm{N}.dynamics.com</c> (N absent for the US region).</summary>
    private static readonly Regex Host = new(
        @"^(?<domain>[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)\.crm[0-9]*\.dynamics\.com$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// The domain(s) this customer's environment may have: <c>spaarke-{customerId}</c> and
    /// <c>spaarke-{customerId}-{environmentName}</c>.
    /// </summary>
    public static IReadOnlyList<string> AllowedDomains(string customerId, string environmentName)
        => [$"spaarke-{customerId}", $"spaarke-{customerId}-{environmentName}"];

    /// <summary>
    /// Validates <paramref name="value"/> and returns its canonical form <c>https://{host}/</c> (lowercase host, no
    /// path, query or port). False with an operator-facing <paramref name="error"/> otherwise.
    /// </summary>
    public static bool TryNormalize(
        string? value, string customerId, string environmentName, out string normalized, out string error)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "is required: the URL of the Dataverse environment the operator created for this customer.";
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = $"'{value}' is not an environment URL: expected https://{{domain}}.crm[N].dynamics.com/ with no path, query or port.";
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var match = Host.Match(host);
        if (!match.Success)
        {
            error = $"'{value}' is not a Dataverse environment host: expected {{domain}}.crm[N].dynamics.com.";
            return false;
        }

        var allowed = AllowedDomains(customerId, environmentName);
        if (!allowed.Contains(match.Groups["domain"].Value, StringComparer.Ordinal))
        {
            error = $"'{value}' does not belong to customer '{customerId}': its domain is '{match.Groups["domain"].Value}', " +
                    $"and this customer's environment must be named {string.Join(" or ", allowed.Select(d => $"'{d}'"))} " +
                    "(the guard against adopting another customer's environment). Rename the environment's URL in the " +
                    "Power Platform admin center or correct the intake value.";
            return false;
        }

        normalized = $"https://{host}/";
        error = string.Empty;
        return true;
    }
}
