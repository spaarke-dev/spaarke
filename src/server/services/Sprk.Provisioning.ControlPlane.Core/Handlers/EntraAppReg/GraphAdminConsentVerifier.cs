// -----------------------------------------------------------------------------
// GraphAdminConsentVerifier.cs
//
// Task 130 (Wave G-3, xhigh) — production IAdminConsentVerifier. REPLACES
// RETIRED NullAdminConsentVerifier (see that file's retirement banner), which
// always returned Verified unconditionally — the consent gate could
// previously advance on fiction (DS-4 §3 finding, the primary defect this
// task exists to close).
//
// REAL CHECK: queries the BFF app-registration's service principal in the
// TARGET TENANT for its oauth2PermissionGrants, and confirms the aggregate
// granted scope string contains every value in
// EntraAppRegPermissionCatalog.ScopeValues (5 as of task 130 — see that
// file's header for why this is NOT the 14-role app-only catalog H10 owns).
//
// SP-NOT-FOUND SEMANTICS: for a MULTI-TENANT app (Model 1's shared app-reg,
// signInAudience=AzureADMultipleOrgs), the service principal object is only
// materialized in a given tenant once someone there interacts with the app
// (typically the admin-consent flow itself). A 0-result SP lookup in the
// customer tenant is therefore Pending (consent not yet granted), NOT an
// error — the exact "gate can no longer advance on fiction" fix.
//
// NOT UNIT-TESTED IN THE CI SUITE (real Microsoft.Graph HTTP calls) — parity
// with GraphAppRegistrationProvisioner.cs's file-header precedent list.
// H3EntraAppRegHandlerTests cover the orchestration via a fake
// IAdminConsentVerifier.
// -----------------------------------------------------------------------------

using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <inheritdoc cref="IAdminConsentVerifier"/>
public sealed class GraphAdminConsentVerifier : IAdminConsentVerifier
{
    private static readonly string[] GraphDefaultScope = { "https://graph.microsoft.com/.default" };

    private readonly EntraAppRegOptions _options;
    private readonly ILogger<GraphAdminConsentVerifier> _logger;

    public GraphAdminConsentVerifier(
        IOptions<EntraAppRegOptions> options,
        ILogger<GraphAdminConsentVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AdminConsentVerificationResult> VerifyAsync(
        string bffAppRegId,
        string tenantId,
        int expectedDelegatedScopeCount,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bffAppRegId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (expectedDelegatedScopeCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedDelegatedScopeCount), expectedDelegatedScopeCount,
                "expectedDelegatedScopeCount must be >= 1 (5 per EntraAppRegPermissionCatalog.All as of task 130).");
        }

        // §4D I5 — explicit per-tenant credential (parity with
        // GraphAppRegistrationProvisioner's GOTCHA-1 pattern; NOT the L2
        // platform's own shared UAMI credential).
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
        var graph = new GraphServiceClient(credential, GraphDefaultScope);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.GraphRequestTimeout);

        Microsoft.Graph.Models.ServicePrincipalCollectionResponse? spPage;
        try
        {
            spPage = await graph.ServicePrincipals.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = $"appId eq '{EscapeODataLiteral(bffAppRegId)}'";
            }, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 401 or 403)
        {
            // A 401/403 resolving the SP in a fresh customer tenant (before
            // any consent has ever happened) is itself evidence of
            // not-yet-consented — treat as Pending rather than an
            // infrastructure fault, so H3 correctly transitions to
            // WaitingOnGate instead of Failed.
            _logger.LogInformation(
                "Admin-consent verify: SP lookup {Status} in tenant (likely not-yet-consented multi-tenant app) — treating as Pending. bffAppRegId={AppId}",
                ex.ResponseStatusCode, bffAppRegId);
            return new AdminConsentVerificationResult.Pending(
                GrantedCount: 0, ExpectedCount: expectedDelegatedScopeCount,
                Diagnostic: $"Service principal lookup returned {ex.ResponseStatusCode} in target tenant — " +
                            "admin consent has likely not yet been granted (the SP is not materialized until then).",
                Evidence: null);
        }

        var sp = spPage?.Value?.FirstOrDefault();
        if (sp is null || string.IsNullOrWhiteSpace(sp.Id))
        {
            return new AdminConsentVerificationResult.Pending(
                GrantedCount: 0, ExpectedCount: expectedDelegatedScopeCount,
                Diagnostic: "Service principal not found in target tenant — admin consent has not been initiated.",
                Evidence: null);
        }

        var grants = await graph.ServicePrincipals[sp.Id].Oauth2PermissionGrants
            .GetAsync(cancellationToken: timeoutCts.Token).ConfigureAwait(false);

        var grantScopeStrings = (grants?.Value ?? new List<Microsoft.Graph.Models.OAuth2PermissionGrant>())
            .Select(g => g.Scope)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!);

        return EvaluateGrantedScopes(sp.Id!, grantScopeStrings, expectedDelegatedScopeCount);
    }

    /// <summary>
    /// PURE evaluation logic — no Graph I/O. Splits each grant's
    /// space-delimited <c>Scope</c> string into individual scope values,
    /// unions them, and compares against
    /// <see cref="EntraAppRegPermissionCatalog.ScopeValues"/>. Exposed
    /// internal so unit tests can exercise the "gate can no longer advance on
    /// fiction" logic directly against fixture data — WITHOUT the expected
    /// scope set (asserts NOT-Verified/Pending) and WITH it (asserts
    /// Verified) — per this task's acceptance criteria, without requiring a
    /// live/fake Graph transport (parity with the project's established
    /// "collaborator I/O untested, logic extracted + tested" pattern where a
    /// pure decision function exists).
    /// </summary>
    internal static AdminConsentVerificationResult EvaluateGrantedScopes(
        string servicePrincipalId, IEnumerable<string> grantScopeStrings, int expectedDelegatedScopeCount)
    {
        var grantedScopeValues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scopeString in grantScopeStrings)
        {
            foreach (var scope in scopeString.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                grantedScopeValues.Add(scope);
            }
        }

        var missing = EntraAppRegPermissionCatalog.ScopeValues
            .Where(v => !grantedScopeValues.Contains(v))
            .ToList();

        var evidence = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            servicePrincipalId,
            grantedScopes = grantedScopeValues.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
            missingScopes = missing,
        });

        if (missing.Count > 0)
        {
            return new AdminConsentVerificationResult.Pending(
                GrantedCount: EntraAppRegPermissionCatalog.ScopeValues.Count - missing.Count,
                ExpectedCount: expectedDelegatedScopeCount,
                Diagnostic: $"Tenant admin has not granted the following delegated scope(s) yet: {string.Join(", ", missing)}.",
                Evidence: evidence);
        }

        return new AdminConsentVerificationResult.Verified(
            GrantedCount: EntraAppRegPermissionCatalog.ScopeValues.Count,
            ExpectedCount: expectedDelegatedScopeCount,
            Evidence: evidence);
    }

    private static string EscapeODataLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
