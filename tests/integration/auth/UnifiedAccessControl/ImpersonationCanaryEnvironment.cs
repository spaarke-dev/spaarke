using System.Globalization;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The canary user's provisioning contract, expressed as configuration the live tests read.
/// The prose version an operator follows once per environment is
/// <c>tests/integration/auth/README.md</c> § "NFR-04 impersonation negative canary".
/// </summary>
/// <remarks>
/// <para><b>Missing configuration FAILS; it never skips</b> (spec NFR-01). A skipped canary and a
/// passing canary are indistinguishable in a build log, and the thing being gated — impersonation
/// silently returning org-wide rows — is invisible by construction. So <see cref="Require"/> throws,
/// and it throws with the specific user, privilege, and seed count an operator needs, rather than
/// "configuration missing".</para>
///
/// <para><b>Credential model.</b> The live tests authenticate with
/// <c>DefaultAzureCredential</c> (the <c>Graph:ManagedIdentity:Enabled=true</c> branch of
/// <see cref="Spaarke.Dataverse.DataverseWebApiService"/>), which resolves to the operator's
/// <c>az login</c> identity locally and to the workload identity in a hosted runner. The
/// managed-identity-DISABLED branch is deliberately not offered here: it requires an
/// <c>IConfidentialClientProvider</c> that only the BFF's DI container builds, and reaching for a
/// client secret in a test is exactly what auth-v4 removed.</para>
/// </remarks>
public sealed record ImpersonationCanaryEnvironment(
    string DataverseServiceUrl,
    Guid CanarySystemUserId,
    IReadOnlyCollection<Guid> SeededMatterIds,
    string? ManagedIdentityClientId)
{
    /// <summary>Dataverse environment URL, e.g. <c>https://spaarkedev1.crm.dynamics.com</c>.</summary>
    public const string ServiceUrlVariable = "SPAARKE_CANARY_DATAVERSE_URL";

    /// <summary>The canary user's <c>systemuserid</c> (NOT its Entra object id — see DataverseImpersonation.cs:20-21).</summary>
    public const string SystemUserIdVariable = "SPAARKE_CANARY_SYSTEMUSERID";

    /// <summary>Comma-separated <c>sprk_matterid</c> values the canary user owns. This is K.</summary>
    public const string SeededMatterIdsVariable = "SPAARKE_CANARY_SEEDED_MATTER_IDS";

    /// <summary>Optional user-assigned managed identity client id; omit to use the ambient credential.</summary>
    public const string ManagedIdentityClientIdVariable = "SPAARKE_CANARY_MI_CLIENT_ID";

    /// <summary>The entity set the canary reads. Matters are the contract because the canary role scopes them.</summary>
    public const string EntitySetName = "sprk_matters";

    /// <summary>The primary key selected by both the app-only and impersonated queries.</summary>
    public const string PrimaryKeyField = "sprk_matterid";

    /// <summary>True when every required variable is present and parseable.</summary>
    public static bool IsConfigured() => TryRead(out _, out _);

    /// <summary>
    /// Reads the canary configuration or throws an operator-actionable exception. Never returns null and
    /// never signals "not configured" through a skip.
    /// </summary>
    public static ImpersonationCanaryEnvironment Require()
    {
        if (TryRead(out var environment, out var problems))
        {
            return environment!;
        }

        throw new InvalidOperationException(
            "The NFR-04 impersonation negative canary cannot run because its environment is not provisioned. "
            + "This is a FAILURE, not a skip: an unrun canary is an open gate (spec NFR-01).\n\n"
            + "Problems:\n  - " + string.Join("\n  - ", problems) + "\n\n"
            + "Required provisioning (once per environment — full procedure in "
            + "tests/integration/auth/README.md § 'NFR-04 impersonation negative canary'):\n"
            + "  1. A dedicated, ENABLED, low-privilege systemuser (the canary user) holding a custom "
            + "security role whose ONLY grant is User-level (basic) Read on sprk_matter. No Business "
            + "Unit / Parent-Child / Organization depth, and no other entity privileges.\n"
            + "  2. Exactly K > 0 sprk_matter rows OWNED by that user, and strictly more matters in the "
            + "org that it cannot read (otherwise 'strictly fewer' is unsatisfiable and the canary is "
            + "structurally unable to pass).\n"
            + "  3. The BFF Dataverse application user holding prvActOnBehalfOfAnotherUser (Delegate), "
            + "and remaining BROADLY scoped — a narrowed app user silently narrows impersonated results "
            + "(investigation 08 §3c).\n"
            + $"  4. Environment variables: {ServiceUrlVariable}, {SystemUserIdVariable}, "
            + $"{SeededMatterIdsVariable} (comma-separated), optionally {ManagedIdentityClientIdVariable}.\n"
            + "  5. An ambient Azure credential (az login / workload identity) mapped to the BFF "
            + "application user.");
    }

    private static bool TryRead(out ImpersonationCanaryEnvironment? environment, out List<string> problems)
    {
        environment = null;
        problems = new List<string>();

        var serviceUrl = Environment.GetEnvironmentVariable(ServiceUrlVariable);
        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            problems.Add($"{ServiceUrlVariable} is not set (expected the Dataverse environment URL).");
        }
        else if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out _))
        {
            problems.Add($"{ServiceUrlVariable} is not an absolute URL: '{serviceUrl}'.");
        }

        var rawUserId = Environment.GetEnvironmentVariable(SystemUserIdVariable);
        var canaryUserId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(rawUserId))
        {
            problems.Add(
                $"{SystemUserIdVariable} is not set (expected the canary user's systemuserid — the "
                + "Dataverse row id, not the Entra object id).");
        }
        else if (!Guid.TryParse(rawUserId, out canaryUserId) || canaryUserId == Guid.Empty)
        {
            problems.Add($"{SystemUserIdVariable} is not a non-empty GUID: '{rawUserId}'.");
        }

        var rawSeeds = Environment.GetEnvironmentVariable(SeededMatterIdsVariable);
        var seeds = new List<Guid>();
        if (string.IsNullOrWhiteSpace(rawSeeds))
        {
            problems.Add(
                $"{SeededMatterIdsVariable} is not set (expected the comma-separated sprk_matterid values "
                + "the canary user owns — this is K, and K must be > 0).");
        }
        else
        {
            foreach (var part in rawSeeds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Guid.TryParse(part, out var seed) && seed != Guid.Empty)
                {
                    seeds.Add(seed);
                }
                else
                {
                    problems.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{SeededMatterIdsVariable} contains a value that is not a non-empty GUID: '{part}'."));
                }
            }

            if (seeds.Count == 0 && problems.Count == 0)
            {
                problems.Add($"{SeededMatterIdsVariable} parsed to zero ids; K must be > 0.");
            }
        }

        if (problems.Count > 0)
        {
            return false;
        }

        environment = new ImpersonationCanaryEnvironment(
            serviceUrl!.TrimEnd('/'),
            canaryUserId,
            seeds,
            Environment.GetEnvironmentVariable(ManagedIdentityClientIdVariable));

        return true;
    }
}
