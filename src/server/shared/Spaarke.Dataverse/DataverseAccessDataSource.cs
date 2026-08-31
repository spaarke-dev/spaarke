using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace Spaarke.Dataverse;

/// <summary>
/// Queries Dataverse for user access permissions and team memberships.
/// Implements fail-closed security: returns AccessRights.None on errors.
/// </summary>
public class DataverseAccessDataSource : IAccessDataSource
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DataverseAccessDataSource> _logger;
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;

    /// <summary>
    /// Supplies the OBO confidential client, with the credential already selected from the configured
    /// ordered list (MI-FIC → Key Vault certificate → transitional secret).
    ///
    /// <para><b>This class is the only reason the contract exists.</b> The three BFF-side consumers
    /// (<c>GraphClientFactory</c>, <c>DataverseUserClient</c>, <c>AgentTokenService</c>) inject the
    /// implementation concretely. This one is in <c>Spaarke.Dataverse</c> — the base layer, CI-enforced
    /// to reference no other Spaarke project (FR-14) — so it can only receive a BFF-owned credential by
    /// dependency inversion.</para>
    ///
    /// <para>Null outside the BFF DI container (tooling, direct construction in tests), in which case
    /// delegated access <b>fails closed</b>, exactly as a missing confidential client did before
    /// task 022.</para>
    /// </summary>
    private readonly IConfidentialClientProvider? _confidentialClients;

    /// <summary>Directory (tenant) id the OBO exchange authenticates against. Null when unconfigured.</summary>
    private readonly string? _tenantId;

    /// <summary>App registration the OBO exchange authenticates AS — never the UAMI clientId (FR-B4).</summary>
    private readonly string? _clientId;

    private readonly string _apiUrl;
    private readonly string _dataverseScope;
    private AccessToken? _currentToken;

    /// <summary>
    /// Whether delegated (OBO) access can be attempted at all. Task 022 changed what this depends on,
    /// and the change is the point of the task: it used to require a client <b>secret</b>, and now
    /// requires only an identity plus a credential provider. Which credential actually proves that
    /// identity is the provider's decision, re-made per call and recoverable — not a fact frozen into
    /// this object at construction.
    /// </summary>
    private bool OboAvailable =>
        _confidentialClients is not null
        && !string.IsNullOrEmpty(_tenantId)
        && !string.IsNullOrEmpty(_clientId);

    /// <param name="confidentialClients">
    /// Ordered credential provider (auth-v4 task 021, FR-B2), supplied by the BFF. <b>Replaces the
    /// <c>IClientAssertionProvider assertion</c> parameter</b> that task 020 threaded in here as a
    /// placeholder: selection spans assertion / certificate / secret and only the first of those IS an
    /// assertion, so the seam this class actually needs is the client-level one. The assertion contract
    /// is still what mints the MI-FIC credential — one level down, inside the provider.
    ///
    /// <para>Nullable with a null default, deliberately: this mirrors <c>credential</c> above and is
    /// what keeps the existing test fixtures compiling unchanged (NFR-04). A required parameter would
    /// break every one of them.</para>
    /// </param>
    public DataverseAccessDataSource(
        IDataverseService dataverseService,
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DataverseAccessDataSource> logger,
        TokenCredential? credential = null,
        IConfidentialClientProvider? confidentialClients = null)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _confidentialClients = confidentialClients;

        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        var tenantId = configuration["TENANT_ID"];
        var clientId = configuration["API_APP_ID"];

        if (string.IsNullOrEmpty(dataverseUrl))
            throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";
        _dataverseScope = $"{dataverseUrl.TrimEnd('/')}/.default";

        // ---------------------------------------------------------------------------------------
        // FR-A1 (auth-v4 task 010) — DECOUPLED credential selection.
        //
        // These are TWO INDEPENDENT concerns and used to share a single `if`:
        //   (1) _credential — the APP-ONLY token used by EnsureAuthenticatedAsync.
        //   (2) _cca        — the OBO confidential client for DELEGATED (per-user) access.
        //
        // The old shape selected BOTH on "is a client secret present?", which had two bugs:
        //   * The app-only path ignored Graph:ManagedIdentity:Enabled entirely, so on dev — where
        //     API_CLIENT_SECRET is set BECAUSE OBO needs it — this class ran on the client secret
        //     even though MI was enabled. That is the defect FR-A1 exists to fix.
        //   * Fixing that naively (copying DataverseWebApiService's plain if/else) would have put
        //     `_cca = null` in the MI branch, so enabling MI would DISABLE OBO and every delegated
        //     access check would throw at GetDataverseTokenViaOBOAsync. DataverseWebApiService is a
        //     safe template only because it has no OBO path; this class does.
        //
        // So: gate (1) on the flag, and make (2) available whenever an identity plus a credential
        // provider exist — independent of the flag. DefaultAzureCredential cannot perform an OBO
        // exchange (ADR-028 A4), and the MI flag says nothing about delegated access.
        //
        // TASK 022: neither concern constructs a credential inline any more. (2) asks the provider at
        // the moment of the exchange; (1)'s secret branch is a provider-backed TokenCredential. The
        // decoupling above is preserved exactly — it is still two independent selections, and the
        // source-analysis guard task 060 adds exists to keep them from being "simplified" back into one
        // if/else that would set the OBO client to null whenever MI is enabled.
        // ---------------------------------------------------------------------------------------

        _tenantId = tenantId;
        _clientId = clientId;

        // (1) APP-ONLY credential — gated by the flag.
        var useManagedIdentity = string.Equals(
            configuration["Graph:ManagedIdentity:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

        if (useManagedIdentity)
        {
            // BFF-FIX-2026-05-24: prefer the DI-injected TokenCredential (pinned to the UAMI clientId
            // by the BFF's ManagedIdentityCredentialFactory). Fall back to DefaultAzureCredential for
            // instantiation outside the BFF DI container (tooling, integration tests).
            var miClientId = configuration["ManagedIdentity:ClientId"]
                ?? configuration["Graph:ManagedIdentity:ClientId"];
            _credential = credential ?? new DefaultAzureCredential(
                string.IsNullOrEmpty(miClientId)
                    ? new DefaultAzureCredentialOptions()
                    : new DefaultAzureCredentialOptions { ManagedIdentityClientId = miClientId });
            _logger.LogInformation(
                "DataverseAccessDataSource app-only auth: Managed Identity (ADR-028; {CredentialKind}, clientId {ClientId})",
                credential != null ? "DI-injected TokenCredential" : "DefaultAzureCredential (fallback)",
                miClientId ?? "(system-assigned)");
        }
        else
        {
            // Fail fast with an actionable message rather than handing back a null/unusable credential.
            // The IDENTITY is still required here — it is what the token is issued to, and no provider
            // can supply it. What is NO LONGER required is the client SECRET: which credential proves
            // this identity is the provider's ordered decision (FR-B2/FR-B5), and whether ANY credential
            // is obtainable is checked once at startup by IdentityConfigurationValidator rule 4 rather
            // than re-derived here. Task 022.
            if (string.IsNullOrEmpty(tenantId))
                throw new InvalidOperationException("TENANT_ID configuration is required (Graph:ManagedIdentity:Enabled is not true)");
            if (string.IsNullOrEmpty(clientId))
                throw new InvalidOperationException("API_APP_ID configuration is required (Graph:ManagedIdentity:Enabled is not true)");
            if (confidentialClients is null)
                throw new InvalidOperationException(
                    "An IConfidentialClientProvider is required for app-only authentication when "
                    + "Graph:ManagedIdentity:Enabled is not true. Inside the BFF it is registered by "
                    + "AuthorizationModule.AddCredentialSelection; constructing this type directly "
                    + "requires supplying one (previously this branch built a ClientSecretCredential "
                    + "from API_CLIENT_SECRET inline — removed by auth-v4 task 022, ADR-028 A4).");

            // No per-instance token cache to worry about any more: the provider owns the ONE client
            // cache, and MSAL caches the app token on that client. FR-A2's SecretCredentialCache
            // existed only because ClientSecretCredential cached per instance and this type is
            // transient; that reason is gone with the credential.
            _credential = new ConfidentialClientTokenCredential(confidentialClients, tenantId, clientId);
            _logger.LogInformation(
                "DataverseAccessDataSource app-only auth: ordered credential provider (ADR-028 A4)");
        }

        // (2) OBO delegated access — INDEPENDENT of the MI flag, and no longer dependent on a secret.
        //     The confidential client is fetched from the provider at the moment of the exchange
        //     (GetDataverseTokenViaOBOAsync), not built here: the provider's contract is async because
        //     selection PROVES a credential before binding it, and a constructor cannot await.
        if (OboAvailable)
        {
            _logger.LogInformation(
                "DataverseAccessDataSource delegated auth: OBO available via the ordered credential provider");
        }
        else
        {
            _logger.LogWarning(
                "DataverseAccessDataSource delegated auth: OBO NOT available ({Reason}). "
                + "Delegated access checks will fail closed.",
                _confidentialClients is null
                    ? "no IConfidentialClientProvider was supplied"
                    : "TENANT_ID / API_APP_ID are not configured");
        }
    }

    /// <summary>
    /// Ensures the HttpClient has a valid authentication token.
    /// Uses service principal (app-only) authentication.
    /// </summary>
    private async Task EnsureAuthenticatedAsync(CancellationToken ct = default)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { _dataverseScope }),
                ct);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("DataverseAccessDataSource: Refreshed service principal access token");
        }
    }

    /// <summary>
    /// Performs On-Behalf-Of token exchange to get Dataverse token for the user.
    /// </summary>
    /// <param name="userAccessToken">User's bearer token from Authorization header</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Dataverse access token for the user</returns>
    private async Task<string> GetDataverseTokenViaOBOAsync(string userAccessToken, CancellationToken ct = default)
    {
        if (!OboAvailable)
        {
            throw new InvalidOperationException(
                "OBO authentication requires an identity and a confidential-client provider. " +
                "Ensure TENANT_ID and API_APP_ID are set and an IConfidentialClientProvider is supplied.");
        }

        _logger.LogDebug("Performing OBO token exchange for Dataverse access");

        try
        {
            // Asked per exchange rather than held: the provider owns the ONE client cache (so this is
            // a dictionary lookup on the hot path) AND re-evaluates the credential once a skipped
            // higher-priority one stops being suppressed. Caching the client in a field here would
            // defeat that recovery and pin the process to a fallback after a single transient blip.
            var cca = await _confidentialClients!
                .GetClientAsync(_tenantId!, _clientId!, ct)
                .ConfigureAwait(false);

            var result = await cca.AcquireTokenOnBehalfOf(
                new[] { _dataverseScope },
                new UserAssertion(userAccessToken))
                .ExecuteAsync(ct);

            _logger.LogInformation("OBO token exchange successful for Dataverse. Scopes: {Scopes}",
                string.Join(", ", result.Scopes));

            return result.AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogError(ex, "OBO failed - MSAL UI required. ErrorCode: {ErrorCode}", ex.ErrorCode);
            throw;
        }
        catch (MsalServiceException ex)
        {
            _logger.LogError(ex, "OBO failed - MSAL service exception. ErrorCode: {ErrorCode}, StatusCode: {StatusCode}",
                ex.ErrorCode, ex.StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OBO failed - unexpected exception");
            throw;
        }
    }

    public async Task<AccessSnapshot> GetUserAccessAsync(
        string userId,
        string resourceId,
        string? userAccessToken = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId, nameof(resourceId));

        _logger.LogInformation(
            "[UAC-DIAG] GetUserAccessAsync START: AzureAdOid={UserId}, ResourceId={ResourceId}, UsingOBO={UsingOBO}",
            userId, resourceId, !string.IsNullOrEmpty(userAccessToken));

        try
        {
            // Determine which authentication mode to use
            string dataverseToken;

            if (!string.IsNullOrEmpty(userAccessToken))
            {
                // Use OBO to call Dataverse as the user
                _logger.LogDebug("[UAC-DIAG] Using OBO authentication for user context");
                dataverseToken = await GetDataverseTokenViaOBOAsync(userAccessToken, ct);

                // CRITICAL: Set the OBO token on HttpClient headers for all subsequent API calls
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", dataverseToken);

                _logger.LogDebug("[UAC-DIAG] Set OBO token on HttpClient authorization header");
            }
            else
            {
                // Use service principal (app-only) authentication
                _logger.LogDebug("[UAC-DIAG] Using service principal authentication");
                await EnsureAuthenticatedAsync(ct);
                dataverseToken = _currentToken!.Value.Token;
            }

            // Map Azure AD Object ID to Dataverse systemuserid
            var dataverseUserId = await LookupDataverseUserIdAsync(userId, ct);
            if (string.IsNullOrEmpty(dataverseUserId))
            {
                _logger.LogWarning("Could not find Dataverse user for Azure AD OID {AzureAdOid}. Returning None access.", userId);
                return new AccessSnapshot
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    AccessRights = AccessRights.None,
                    TeamMemberships = Array.Empty<string>(),
                    Roles = Array.Empty<string>(),
                    CachedAt = DateTimeOffset.UtcNow
                };
            }

            _logger.LogDebug("Mapped Azure AD OID {AzureAdOid} to Dataverse systemuserid {DataverseUserId}", userId, dataverseUserId);

            // Query user permissions from Dataverse using the Dataverse user ID
            var permissions = await QueryUserPermissionsAsync(dataverseUserId, resourceId, dataverseToken, ct);

            // Query team memberships using Dataverse user ID
            var teams = await QueryUserTeamMembershipsAsync(dataverseUserId, ct);

            // Query user roles using Dataverse user ID
            var roles = await QueryUserRolesAsync(dataverseUserId, ct);

            // Determine granular access rights based on permissions
            var accessRights = DetermineAccessLevel(permissions);

            var snapshot = new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = accessRights,
                TeamMemberships = teams,
                Roles = roles,
                CachedAt = DateTimeOffset.UtcNow
            };

            _logger.LogInformation("Access snapshot retrieved for user {UserId}: AccessRights={AccessRights}, Teams={TeamCount}, Roles={RoleCount}",
                userId, accessRights, teams.Count(), roles.Count());

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "Failed to fetch access data for user {UserId} on resource {ResourceId}. Fail-closed: returning AccessRights.None",
                userId,
                resourceId);

            // Fail-closed security: Return None on errors
            return new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = AccessRights.None,
                TeamMemberships = Array.Empty<string>(),
                Roles = Array.Empty<string>(),
                CachedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>The entity set targeted by the document-scoped <see cref="GetUserAccessAsync"/> path.</summary>
    private const string DocumentEntitySetName = "sprk_documents";

    /// <inheritdoc />
    public async Task<AccessSnapshot> GetRecordAccessAsync(
        string userId,
        string entitySetName,
        Guid recordId,
        string? userAccessToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySetName, nameof(entitySetName));

        AccessSnapshot Denied(string reason)
        {
            _logger.LogWarning(
                "[UAC-DIAG] RECORD-ACCESS DENIED ({Reason}): User={UserId}, EntitySet={EntitySet}, Record={RecordId}",
                reason, userId, entitySetName, recordId);

            return new AccessSnapshot
            {
                UserId = userId,
                ResourceId = recordId.ToString(),
                AccessRights = AccessRights.None,
                TeamMemberships = Array.Empty<string>(),
                Roles = Array.Empty<string>(),
                CachedAt = DateTimeOffset.UtcNow
            };
        }

        // Fail closed on a missing record id — an unresolvable target cannot be proven accessible.
        if (recordId == Guid.Empty)
        {
            return Denied("empty_record_id");
        }

        // Fail closed on a missing caller token. NEVER degrade to app-only: on BFF-served surfaces
        // reads are app-only, so Dataverse row-level security is inert and app-only answers "yes"
        // for every caller — finding A-2, the exact disclosure this seam exists to prevent.
        if (string.IsNullOrWhiteSpace(userAccessToken))
        {
            return Denied("no_caller_token");
        }

        try
        {
            var dataverseToken = await GetDataverseTokenViaOBOAsync(userAccessToken, ct);

            // Resolve oid -> systemuserid. Done with an EXPLICIT per-request token rather than by
            // mutating _httpClient.DefaultRequestHeaders (which GetUserAccessAsync does): that field is
            // shared across concurrent requests, so setting it here would race another caller's identity
            // onto this request. RetrievePrincipalAccess is bound to the principal, so a wrong
            // systemuserid would silently authorize the wrong person.
            var dataverseUserId = await LookupDataverseUserIdAsync(dataverseToken, userId, ct);
            if (string.IsNullOrEmpty(dataverseUserId))
            {
                return Denied("caller_not_a_dataverse_user");
            }

            // AUTHORITATIVE: Dataverse's own answer for this principal on this record.
            var rights = await TryRetrievePrincipalAccessAsync(
                dataverseUserId, entitySetName, recordId.ToString(), dataverseToken, ct);

            if (rights is null)
            {
                // RetrievePrincipalAccess gave no answer. Degrade to the retrieval probe, which grants
                // at most Read and only when the caller can genuinely retrieve the record — still
                // Dataverse's answer, just a narrower one.
                //
                // The probe is retained rather than denying outright because a systematic RPA outage
                // would otherwise deny EVERY caller on the flagship Matter form. A form that shows a
                // user nothing gets reverted, and reverting reopens the disclosure this closes — so the
                // safe-looking choice is the less safe one. The probe cannot over-grant: Read only,
                // conditional on Dataverse permitting the read.
                var probed = await ProbeRecordReadAccessAsync(entitySetName, recordId, dataverseToken, ct);
                rights = probed ? AccessRights.Read : AccessRights.None;
            }

            _logger.LogInformation(
                "[UAC-DIAG] RECORD-ACCESS: User={UserId}, EntitySet={EntitySet}, Record={RecordId}, Rights={Rights}",
                userId, entitySetName, recordId, rights.Value);

            return new AccessSnapshot
            {
                UserId = userId,
                ResourceId = recordId.ToString(),
                AccessRights = rights.Value,
                TeamMemberships = Array.Empty<string>(),
                Roles = Array.Empty<string>(),
                CachedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "[UAC-DIAG] RECORD-ACCESS ERROR for User={UserId}, EntitySet={EntitySet}, " +
                         "Record={RecordId}. Fail-closed: returning AccessRights.None",
                userId, entitySetName, recordId);

            return Denied("exception");
        }
    }

    /// <summary>
    /// Entity-agnostic retrieval probe: <c>true</c> iff the caller can retrieve the record, which means
    /// Dataverse granted at least Read. Selects <c>createdon</c> because every Dataverse table has it —
    /// this avoids needing each entity's primary-key attribute name, which would have to be guessed.
    /// </summary>
    private async Task<bool> ProbeRecordReadAccessAsync(
        string entitySetName,
        Guid recordId,
        string dataverseToken,
        CancellationToken ct)
    {
        try
        {
            var url = $"{entitySetName}({recordId})?$select=createdon";

            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken) }
            };

            var response = await _httpClient.SendAsync(requestMessage, ct);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogWarning(
                "[UAC-DIAG] RECORD-PROBE denied: {StatusCode} for EntitySet={EntitySet}, Record={RecordId}",
                response.StatusCode, entitySetName, recordId);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                exception: ex,
                message: "[UAC-DIAG] RECORD-PROBE threw for EntitySet={EntitySet}, Record={RecordId}. " +
                         "Fail-closed: no access.",
                entitySetName, recordId);

            return false;
        }
    }

    /// <summary>
    /// Looks up the Dataverse systemuserid for an Azure AD Object ID using an EXPLICIT token, so the
    /// call does not depend on (or mutate) <c>_httpClient.DefaultRequestHeaders</c>.
    /// </summary>
    private async Task<string?> LookupDataverseUserIdAsync(
        string dataverseToken,
        string azureAdObjectId,
        CancellationToken ct)
    {
        try
        {
            var url = $"systemusers?$filter=azureactivedirectoryobjectid eq '{azureAdObjectId}'"
                      + "&$select=systemuserid";

            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken) }
            };

            var response = await _httpClient.SendAsync(requestMessage, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[UAC-DIAG] systemuser lookup failed: {StatusCode} for AzureAdOid={AzureAdOid}",
                    response.StatusCode, azureAdObjectId);
                return null;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(ct));

            if (!doc.RootElement.TryGetProperty("value", out var value)
                || value.GetArrayLength() == 0)
            {
                return null;
            }

            return value[0].TryGetProperty("systemuserid", out var id) ? id.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                exception: ex,
                message: "[UAC-DIAG] systemuser lookup threw for AzureAdOid={AzureAdOid}. Fail-closed: null.",
                azureAdObjectId);
            return null;
        }
    }

    /// <summary>
    /// Looks up the Dataverse systemuserid for a given Azure AD Object ID.
    /// </summary>
    /// <param name="azureAdObjectId">Azure AD Object ID (from token 'oid' claim)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Dataverse systemuserid, or null if not found</returns>
    private async Task<string?> LookupDataverseUserIdAsync(string azureAdObjectId, CancellationToken ct)
    {
        try
        {
            // Query systemusers by azureactivedirectoryobjectid
            var url = $"systemusers?$filter=azureactivedirectoryobjectid eq '{azureAdObjectId}'&$select=systemuserid,fullname";

            _logger.LogDebug("Looking up Dataverse user for Azure AD OID {AzureAdOid}: {Url}", azureAdObjectId, url);

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to lookup Dataverse user: {StatusCode}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<ODataResponse<SystemUserDto>>(ct);

            if (result?.Value == null || !result.Value.Any())
            {
                _logger.LogWarning("No Dataverse user found for Azure AD OID {AzureAdOid}", azureAdObjectId);
                return null;
            }

            var user = result.Value.First();
            // PII (D9-01): user full name removed from this authorization-path log. The systemuserid
            // and Azure AD OID GUIDs are sufficient non-PII correlation identifiers for diagnostics.
            _logger.LogInformation("Found Dataverse user (systemuserid: {SystemUserId}) for Azure AD OID {AzureAdOid}",
                user.SystemUserId, azureAdObjectId);

            return user.SystemUserId;
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error looking up Dataverse user for Azure AD OID {AzureAdOid}", azureAdObjectId);
            return null;
        }
    }

    /// <summary>
    /// Resolves the principal's ACTUAL access rights on a record.
    /// </summary>
    /// <param name="userId">Dataverse systemuserid (already mapped from the Entra oid by the caller)</param>
    /// <param name="resourceId">Document resource ID</param>
    /// <param name="dataverseToken">Dataverse access token (from OBO or service principal)</param>
    /// <param name="ct">Cancellation token</param>
    /// <remarks>
    /// <para><b>unified-access-control-r2 task 005 (spec FR-04, finding A-20 Read-ceiling half).</b>
    /// This method used to answer with a single hard-coded <see cref="AccessRights.Read"/> on success:
    /// it probed <c>GET sprk_documents({id})</c> and reasoned "the query succeeded, therefore Read".
    /// The comment on the old implementation said Dataverse "will enforce Write/Delete separately" —
    /// but on the SPA/Teams surface the BFF filter IS the enforcement point, so nothing enforced them.
    /// Every policy requiring more than Read was unsatisfiable: <c>upload_file</c> (Write|Create),
    /// <c>create_container</c> (Create|Write), <c>download_file</c> (Write), <c>delete_file</c>
    /// (Delete), <c>share_document</c> (Share) denied for every caller, however privileged.</para>
    ///
    /// <para><b>Now:</b> <c>RetrievePrincipalAccess</c> — the Dataverse function that answers exactly
    /// this question — is called first, and its full flag set is mapped by
    /// <see cref="MapDataverseAccessRights"/>. Both that mapper and
    /// <see cref="PrincipalAccessResponse"/> already existed in this file but were <b>dead code</b>:
    /// orphaned wiring left behind when the direct-query probe replaced the original implementation.
    /// This task reconnects them rather than writing anything new.</para>
    ///
    /// <para><b>Why the probe survives as a fallback.</b> The removed comment claimed
    /// RetrievePrincipalAccess "may not be available" with delegated tokens. That claim is unverified
    /// (it has zero call sites repo-wide, so nothing ever exercised it) and cannot be settled offline.
    /// Rather than bet the fix on it, any RetrievePrincipalAccess failure falls back to the original
    /// probe. The fallback is strictly safe: it grants Read only when the principal can genuinely read
    /// the record, so the snapshot is never wider than today's and never wider than Dataverse's own
    /// answer. A failure is logged with the <c>RPA-FALLBACK</c> marker so a systematic outage is
    /// visible rather than silently capping everyone at Read again.</para>
    ///
    /// <para><b>Fail-closed.</b> No path infers rights from anything but Dataverse's answer. Errors
    /// yield no rights (an empty record list → <see cref="AccessRights.None"/>).</para>
    /// </remarks>
    private async Task<List<PermissionRecord>> QueryUserPermissionsAsync(
        string userId,
        string resourceId,
        string dataverseToken,
        CancellationToken ct)
    {
        // AUTHORITATIVE: ask Dataverse what rights this principal holds on this record.
        // This overload of the question is document-scoped by contract (see IAccessDataSource
        // .GetUserAccessAsync); GetRecordAccessAsync is the entity-agnostic sibling (task 070).
        var principalRights = await TryRetrievePrincipalAccessAsync(
            userId, DocumentEntitySetName, resourceId, dataverseToken, ct);

        if (principalRights.HasValue)
        {
            if (principalRights.Value == AccessRights.None)
            {
                _logger.LogInformation(
                    "[UAC-DIAG] RetrievePrincipalAccess: no rights. User={UserId}, Resource={ResourceId}",
                    userId, resourceId);
                return new List<PermissionRecord>();
            }

            _logger.LogInformation(
                "[UAC-DIAG] RetrievePrincipalAccess SUCCESS: User={UserId}, Resource={ResourceId}, GrantedAccess={AccessRights}",
                userId, resourceId, principalRights.Value);

            return new List<PermissionRecord>
            {
                new PermissionRecord(userId, resourceId, principalRights.Value)
            };
        }

        // FALLBACK: RetrievePrincipalAccess was unusable. Degrade to the original read probe, which
        // grants at most Read and only when the principal can actually retrieve the record.
        return await QueryReadAccessByProbeAsync(userId, resourceId, dataverseToken, ct);
    }

    /// <summary>
    /// Calls Dataverse's <c>RetrievePrincipalAccess</c> function for one principal against one record.
    /// </summary>
    /// <returns>
    /// The principal's rights (possibly <see cref="AccessRights.None"/>) when Dataverse answered, or
    /// <c>null</c> when the function could not be used — the signal to fall back. The distinction
    /// matters: <c>None</c> is an authoritative "no rights", <c>null</c> is "no answer".
    /// </returns>
    /// <param name="entitySetName">
    /// The Dataverse entity SET (plural) name of the target record — e.g. <c>sprk_documents</c>,
    /// <c>sprk_matters</c>. Parameterised by unified-access-control-r2 task 070: this was hard-coded to
    /// <c>sprk_documents</c>, which is what made the whole authorization seam document-only and left
    /// <c>scope=entity</c> on <c>POST /api/ai/search</c> with nothing it could ask. Callers pass a value
    /// from an explicit allow-list; nothing here pluralizes or guesses.
    /// </param>
    private async Task<AccessRights?> TryRetrievePrincipalAccessAsync(
        string userId,
        string entitySetName,
        string resourceId,
        string dataverseToken,
        CancellationToken ct)
    {
        try
        {
            // GET systemusers(<systemuserid>)/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)
            //     ?@p1={"@odata.id":"<entitySetName>(<recordid>)"}
            // The function is bound to the PRINCIPAL; Target names the record. The response carries a
            // comma-separated rights string ("ReadAccess,WriteAccess,AppendToAccess,...") — exactly the
            // shape MapDataverseAccessRights and PrincipalAccessResponse were written to consume.
            var target = $"{{\"@odata.id\":\"{entitySetName}({resourceId})\"}}";
            var url = $"systemusers({userId})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)"
                      + $"?@p1={Uri.EscapeDataString(target)}";

            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken) }
            };

            var response = await _httpClient.SendAsync(requestMessage, ct);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                _logger.LogWarning(
                    "[UAC-DIAG] RPA-FALLBACK: RetrievePrincipalAccess returned {StatusCode} for User={UserId}, " +
                    "Resource={ResourceId}. Falling back to the read probe, which caps rights at Read — " +
                    "Write+ operations will deny for this request. ResponseBody={ResponseBody}",
                    response.StatusCode, userId, resourceId, responseBody);

                return null;
            }

            var principalAccess = await response.Content.ReadFromJsonAsync<PrincipalAccessResponse>(ct);

            if (principalAccess is null)
            {
                _logger.LogWarning(
                    "[UAC-DIAG] RPA-FALLBACK: RetrievePrincipalAccess returned an unparseable body for " +
                    "User={UserId}, Resource={ResourceId}. Falling back to the read probe.",
                    userId, resourceId);

                return null;
            }

            // An absent/empty rights string is an authoritative "no rights", not a parse failure:
            // Dataverse answered, and the answer was nothing. MapDataverseAccessRights returns None.
            return MapDataverseAccessRights(principalAccess.AccessRights);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                exception: ex,
                message: "[UAC-DIAG] RPA-FALLBACK: RetrievePrincipalAccess threw for User={UserId}, " +
                         "Resource={ResourceId}. Falling back to the read probe.",
                userId, resourceId);

            return null;
        }
    }

    /// <summary>
    /// The original (pre-task-005) access probe, retained as the fallback path.
    /// If the principal can retrieve the record, they have at least Read; otherwise nothing.
    /// Grants at most <see cref="AccessRights.Read"/> — it cannot observe Write/Delete/Share.
    /// </summary>
    private async Task<List<PermissionRecord>> QueryReadAccessByProbeAsync(
        string userId,
        string resourceId,
        string dataverseToken,
        CancellationToken ct)
    {
        try
        {
            // APPROACH: Query the document directly using the OBO token.
            // If the query succeeds, the user has at least Read access (Dataverse enforces this).
            // If it fails with 403/404, they don't have access.

            _logger.LogInformation(
                "[UAC-DIAG] Checking document access via direct query: User={UserId}, Resource={ResourceId}",
                userId, resourceId);

            // Query the document - just retrieve the ID to minimize data transfer
            var url = $"sprk_documents({resourceId})?$select=sprk_documentid";

            // Create request message with the OBO token
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken) }
            };

            var response = await _httpClient.SendAsync(requestMessage, ct);

            if (!response.IsSuccessStatusCode)
            {
                // Capture response body for diagnostics
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                _logger.LogWarning(
                    "[UAC-DIAG] Document query FAILED: StatusCode={StatusCode}, User={UserId}, Resource={ResourceId}, ResponseBody={ResponseBody}",
                    response.StatusCode, userId, resourceId, responseBody);

                // 403 or 404 means no access
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                    response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Log specific failure reason for diagnostics
                    var failureReason = response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "Document not found (404) - possible replication lag or invalid ID"
                        : "Access forbidden (403) - user lacks permission to this record";

                    _logger.LogWarning(
                        "[UAC-DIAG] Access denied: {FailureReason}, User={UserId}, Resource={ResourceId}",
                        failureReason, userId, resourceId);

                    return new List<PermissionRecord>();
                }

                // Other errors - log and return empty (fail-closed)
                return new List<PermissionRecord>();
            }

            // Success: the principal can retrieve the document, so they hold at least Read.
            _logger.LogInformation(
                "[UAC-DIAG] Document query SUCCESS (fallback probe): User={UserId}, Resource={ResourceId}, GrantedAccess=Read",
                userId, resourceId);

            return new List<PermissionRecord>
            {
                // Read only. This probe cannot observe Write/Delete/Create/Share — it only knows the
                // record was retrievable. The old comment here claimed "Dataverse will enforce
                // Write/Delete separately"; on the SPA/Teams surface that is false, because the BFF
                // filter IS the enforcement point (finding A-20). RetrievePrincipalAccess above is the
                // path that answers the full question; reaching here means it was unavailable.
                new PermissionRecord(userId, resourceId, AccessRights.Read)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error querying Dataverse access for {UserId} on {ResourceId}", userId, resourceId);
            return new List<PermissionRecord>();
        }
    }

    /// <summary>
    /// Maps a Dataverse rights string to <see cref="AccessRights"/> flags, and logs the mapping.
    /// </summary>
    private AccessRights MapDataverseAccessRights(string? accessRightsString)
    {
        var accessRights = DataverseAccessRightsMapper.FromAccessRightsString(accessRightsString);

        _logger.LogDebug("Mapped Dataverse rights '{Rights}' to {AccessRights}",
            accessRightsString, accessRights);

        return accessRights;
    }

    /// <summary>
    /// Queries user's team memberships.
    /// </summary>
    private async Task<IEnumerable<string>> QueryUserTeamMembershipsAsync(string userId, CancellationToken ct)
    {
        try
        {
            // OData query: GET /systemusers(userId)/teammembership_association?$select=name
            var url = $"systemusers({userId})/teammembership_association?$select=name,teamid";

            _logger.LogDebug("Querying team memberships: {Url}", url);

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to query team memberships: {StatusCode}", response.StatusCode);
                return Array.Empty<string>();
            }

            var result = await response.Content.ReadFromJsonAsync<ODataResponse<TeamDto>>(ct);

            if (result?.Value == null)
            {
                return Array.Empty<string>();
            }

            return result.Value.Select(t => t.TeamId ?? t.Name ?? "unknown").ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error querying team memberships for {UserId}", userId);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Queries user's security roles.
    /// </summary>
    private async Task<IEnumerable<string>> QueryUserRolesAsync(string userId, CancellationToken ct)
    {
        try
        {
            // OData query: GET /systemusers(userId)/systemuserroles_association?$select=name
            var url = $"systemusers({userId})/systemuserroles_association?$select=name,roleid";

            _logger.LogDebug("Querying user roles: {Url}", url);

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to query user roles: {StatusCode}", response.StatusCode);
                return Array.Empty<string>();
            }

            var result = await response.Content.ReadFromJsonAsync<ODataResponse<RoleDto>>(ct);

            if (result?.Value == null)
            {
                return Array.Empty<string>();
            }

            return result.Value.Select(r => r.Name ?? "unknown").ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error querying user roles for {UserId}", userId);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Aggregates granular access rights from all permission records.
    /// Combines permissions using bitwise OR to allow cumulative rights.
    /// </summary>
    /// <param name="permissions">List of permission records from Dataverse</param>
    /// <returns>Combined AccessRights from all sources (teams, roles, direct grants)</returns>
    private AccessRights DetermineAccessLevel(List<PermissionRecord> permissions)
    {
        if (!permissions.Any())
        {
            return AccessRights.None;
        }

        // Aggregate all permissions (user may have rights from multiple sources: direct, teams, roles)
        var aggregatedRights = AccessRights.None;

        foreach (var permission in permissions)
        {
            aggregatedRights |= permission.AccessRights;
        }

        _logger.LogDebug("Aggregated access rights: {AccessRights} from {PermissionCount} permission record(s)",
            aggregatedRights, permissions.Count);

        return aggregatedRights;
    }

    // DTOs for Dataverse responses
    private record PermissionRecord(string UserId, string ResourceId, AccessRights AccessRights);

    private class ODataResponse<T>
    {
        public List<T>? Value { get; set; }
    }

    private class PrincipalAccessResponse
    {
        public string? AccessRights { get; set; }
    }

    private class TeamDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("teamid")]
        public string? TeamId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class RoleDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("roleid")]
        public string? RoleId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class SystemUserDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("systemuserid")]
        public string? SystemUserId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fullname")]
        public string? FullName { get; set; }
    }
}
