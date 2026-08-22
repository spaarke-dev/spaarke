using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Answers ONE question, for ONE record, AS THE CALLER: <i>what rights does the human who made this
/// request hold on this Dataverse record?</i>
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b> (unified-access-control-r2 task 008, spec FR-07, finding A-6). The
/// delegation rule — "you may grant access to a record if you have Write on that record" (owner
/// decision B-14) — needs the caller's rights on a <c>sprk_project</c> / <c>sprk_matter</c> /
/// <c>sprk_workassignment</c>. Nothing in the codebase could answer that:</para>
///
/// <list type="bullet">
///   <item><description><see cref="Spaarke.Core.Auth.AuthorizationService"/> routes to
///   <see cref="DataverseAccessDataSource"/>, which hard-codes <c>sprk_documents({id})</c> in both its
///   <c>RetrievePrincipalAccess</c> target and its fallback read probe. Asked about a project it
///   answers <see cref="AccessRights.None"/> for every caller, however privileged — so the filter
///   would deny universally. Generalizing that seam changes <c>IAccessDataSource</c> for every
///   consumer and is task 032's scope (Phase 1 evaluator), not this task's.</description></item>
///   <item><description><c>IDataverseUserClient</c> is the right shape — entity-generic, OBO-only,
///   fail-closed — but it is registered inside a compound AI gate AND behind
///   <c>ToolFramework:Enabled</c>. Six unconditionally-mapped routes depending on a twice-gated
///   service is the asymmetric-registration anti-pattern (CLAUDE.md §10 F.1 / ADR-032), and it would
///   be a CRUD→AI dependency besides (§10 bullet 3).</description></item>
///   <item><description><see cref="DataverseWebApiClient"/> — already injected into every one of
///   these handlers — is app-only. An app-only Write probe answers "can the APPLICATION write",
///   which is finding A-2 rebuilt.</description></item>
/// </list>
///
/// <para><b>Caller-scoped by construction, not by parameter.</b> Both calls run on an OBO token
/// exchanged from the caller's own bearer token, and the principal is resolved with
/// <c>WhoAmI()</c> — which, under an OBO token, <i>cannot</i> name anyone but the caller. That
/// matters more than it looks: <c>RetrievePrincipalAccess</c> takes the principal as an argument, so
/// an app-only implementation would carry the caller's identity as DATA. A wrong or defaulted id
/// would then silently answer about the wrong person, which is the exact shape that let A-2 survive.
/// Here the identity is the CREDENTIAL, so there is no id to get wrong.</para>
///
/// <para><b>Fail closed, with no Read-shaped consolation prize.</b> Every failure — no token, OBO
/// exchange rejected, <c>WhoAmI</c> unavailable, <c>RetrievePrincipalAccess</c> unavailable,
/// unparseable body, transport error — yields <see cref="AccessRights.None"/>. Deliberately there is
/// NO fallback to a record read: a read proves Read, and treating Read as licence to grant is
/// precisely the privilege escalation FR-07 exists to close. <see cref="DataverseAccessDataSource"/>
/// may degrade to a read probe because it is answering "can you see this document"; this type may
/// not, because it is answering "may you hand this record to someone else".</para>
///
/// <para><b>Operational note.</b> A systematic <c>RetrievePrincipalAccess</c> outage therefore denies
/// all six external-access mutations rather than silently widening them. Failures log the
/// <see cref="FallbackMarker"/> marker so that state is diagnosable rather than mysterious. Live
/// verification of the function under a delegated token is owned by task 034, which already carries
/// the same obligation for task 005's document-path use of it.</para>
///
/// <para><b>ADR-010.</b> Concrete class, registered as a concrete (no interface). The substitution
/// seam for tests is <c>virtual</c> on <see cref="GetCallerRightsAsync"/>, following the
/// <see cref="DataverseWebApiClient"/> precedent that ADR-038 §4 designates as the module
/// boundary.</para>
/// </remarks>
public class CallerRecordAccessProbe
{
    /// <summary>Log marker for "Dataverse could not answer" — grep this to detect an outage.</summary>
    internal const string FallbackMarker = "DELEGATION-RPA-UNAVAILABLE";

    /// <summary>
    /// Process-wide MSAL confidential-client cache keyed by (authority, clientId). This type is a
    /// typed <see cref="HttpClient"/> (transient), so building a CCA per instance would discard
    /// MSAL's user-token cache and force a network OBO exchange on every mutation. Same reasoning,
    /// and same shape, as <c>DataverseUserClient</c>'s cache.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IConfidentialClientApplication>
        CcaCache = new();

    private readonly HttpClient _httpClient;
    private readonly ILogger<CallerRecordAccessProbe> _logger;
    private readonly string? _environmentUrl;
    private readonly IConfidentialClientApplication? _cca;

    public CallerRecordAccessProbe(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<CallerRecordAccessProbe> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _environmentUrl = configuration["Dataverse:ServiceUrl"]?.TrimEnd('/');

        // AzureAd:* is the canonical OBO configuration (module CLAUDE.md); TENANT_ID / API_APP_ID /
        // API_CLIENT_SECRET are the legacy keys DataverseAccessDataSource uses. The audience of the
        // inbound token is the BFF's own registration, so it must be the BFF's client id.
        var tenantId = FirstNonEmpty(configuration["AzureAd:TenantId"], configuration["TENANT_ID"]);
        var clientId = FirstNonEmpty(configuration["AzureAd:ClientId"], configuration["API_APP_ID"]);
        var clientSecret = FirstNonEmpty(configuration["AzureAd:ClientSecret"], configuration["API_CLIENT_SECRET"]);

        if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
        {
            _cca = CcaCache.GetOrAdd($"{tenantId}|{clientId}", _ =>
                ConfidentialClientApplicationBuilder
                    .Create(clientId)
                    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                    .WithClientSecret(clientSecret)
                    .Build());
        }
        else
        {
            // No app-only fallback by design — see the class remarks. Without OBO configuration every
            // delegation check denies, which is the correct direction for a missing credential.
            _cca = null;
            _logger.LogWarning(
                "[{Marker}] CallerRecordAccessProbe has no OBO configuration (AzureAd:TenantId/ClientId/" +
                "ClientSecret or TENANT_ID/API_APP_ID/API_CLIENT_SECRET). Every external-access mutation " +
                "will be denied. There is deliberately no app-only fallback: an app-only Write probe would " +
                "answer for the application, not the caller.", FallbackMarker);
        }
    }

    /// <summary>
    /// The caller's rights on one record, as Dataverse itself reports them.
    /// </summary>
    /// <param name="callerBearerToken">The caller's bearer token from the inbound request.</param>
    /// <param name="entitySet">Dataverse entity SET name (plural), e.g. <c>sprk_projects</c>.</param>
    /// <param name="recordId">The record being asked about.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The caller's rights, or <see cref="AccessRights.None"/> when the question could not be
    /// answered. The two are deliberately indistinguishable to callers: both mean "not authorized".
    /// </returns>
    public virtual async Task<AccessRights> GetCallerRightsAsync(
        string? callerBearerToken,
        string entitySet,
        Guid recordId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callerBearerToken) || _cca is null || string.IsNullOrEmpty(_environmentUrl))
        {
            _logger.LogWarning(
                "[{Marker}] Delegation check cannot run for {EntitySet}({RecordId}): " +
                "hasToken={HasToken}, hasOboClient={HasOboClient}, hasEnvironmentUrl={HasEnvironmentUrl}. " +
                "Denying (fail closed).",
                FallbackMarker, entitySet, recordId,
                !string.IsNullOrWhiteSpace(callerBearerToken), _cca is not null, !string.IsNullOrEmpty(_environmentUrl));

            return AccessRights.None;
        }

        string dataverseToken;
        try
        {
            var result = await _cca.AcquireTokenOnBehalfOf(
                    new[] { $"{_environmentUrl}/.default" },
                    new UserAssertion(callerBearerToken))
                .ExecuteAsync(ct)
                .ConfigureAwait(false);

            dataverseToken = result.AccessToken;
        }
        catch (MsalException ex)
        {
            // ADR-015: MSAL error CODE only — never the assertion or token material.
            _logger.LogWarning(
                "[{Marker}] OBO exchange for the delegation check failed ({ErrorCode}) on {EntitySet}({RecordId}). Denying.",
                FallbackMarker, ex.ErrorCode, entitySet, recordId);

            return AccessRights.None;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[{Marker}] OBO exchange for the delegation check threw on {EntitySet}({RecordId}). Denying.",
                FallbackMarker, entitySet, recordId);

            return AccessRights.None;
        }

        var callerSystemUserId = await ResolveCallerSystemUserIdAsync(dataverseToken, ct).ConfigureAwait(false);
        if (callerSystemUserId is null)
        {
            return AccessRights.None;
        }

        return await RetrievePrincipalAccessAsync(
            dataverseToken, callerSystemUserId.Value, entitySet, recordId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The caller's Dataverse <c>systemuserid</c>, via <c>WhoAmI()</c> on the OBO token.
    /// </summary>
    /// <remarks>
    /// <c>WhoAmI</c> rather than an <c>azureactivedirectoryobjectid</c> lookup on purpose: under an OBO
    /// token it answers for the token's subject and nothing else, so the caller's identity cannot be
    /// mis-supplied. It also needs no privilege beyond being a user, and no oid→systemuser mapping
    /// that could silently miss (<see cref="DataverseAccessDataSource"/> returns
    /// <see cref="AccessRights.None"/> exactly there, which reads as "denied" rather than "unmapped").
    /// </remarks>
    private async Task<Guid?> ResolveCallerSystemUserIdAsync(string dataverseToken, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_environmentUrl}/api/data/v9.2/WhoAmI()");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[{Marker}] WhoAmI returned {StatusCode} for the delegation check. Denying.",
                    FallbackMarker, (int)response.StatusCode);

                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("UserId", out var userId) &&
                userId.ValueKind == JsonValueKind.String &&
                Guid.TryParse(userId.GetString(), out var systemUserId) &&
                systemUserId != Guid.Empty)
            {
                return systemUserId;
            }

            _logger.LogWarning(
                "[{Marker}] WhoAmI succeeded but carried no usable UserId. Denying.", FallbackMarker);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Marker}] WhoAmI threw during the delegation check. Denying.", FallbackMarker);
            return null;
        }
    }

    /// <summary>
    /// <c>GET systemusers({principal})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)</c>
    /// — the Dataverse function that answers "what rights does this principal hold on this record".
    /// </summary>
    /// <remarks>
    /// Same call shape task 005 introduced for documents, with the target entity set supplied rather
    /// than hard-coded — the one difference that makes it usable for a project / matter / work
    /// assignment. The response is a comma-separated rights string
    /// (<c>"ReadAccess,WriteAccess,..."</c>) parsed by <see cref="DataverseAccessRightsMapper"/>, the
    /// single place in the codebase that reads that wire format.
    /// </remarks>
    private async Task<AccessRights> RetrievePrincipalAccessAsync(
        string dataverseToken,
        Guid principalSystemUserId,
        string entitySet,
        Guid recordId,
        CancellationToken ct)
    {
        try
        {
            var target = $"{{\"@odata.id\":\"{entitySet}({recordId})\"}}";
            var url = $"{_environmentUrl}/api/data/v9.2/systemusers({principalSystemUserId})"
                      + $"/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)"
                      + $"?@p1={Uri.EscapeDataString(target)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[{Marker}] RetrievePrincipalAccess returned {StatusCode} for {EntitySet}({RecordId}). " +
                    "Denying — there is no read-probe fallback here, because a read proves Read and Read " +
                    "is not licence to grant.",
                    FallbackMarker, (int)response.StatusCode, entitySet, recordId);

                return AccessRights.None;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);

            var rightsString = document.RootElement.TryGetProperty("AccessRights", out var rights)
                ? rights.GetString()
                : null;

            // An absent or empty rights string is an authoritative "no rights", not a parse failure:
            // Dataverse answered, and the answer was nothing.
            var mapped = DataverseAccessRightsMapper.FromAccessRightsString(rightsString);

            _logger.LogInformation(
                "[DELEGATION] Caller rights on {EntitySet}({RecordId}): {AccessRights}",
                entitySet, recordId, mapped);

            return mapped;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[{Marker}] RetrievePrincipalAccess threw for {EntitySet}({RecordId}). Denying.",
                FallbackMarker, entitySet, recordId);

            return AccessRights.None;
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
