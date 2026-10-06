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
    /// <remarks>
    /// <c>internal virtual</c> as a test seam (unified-access-control-r2 task 132), the subclass-seam convention the
    /// BFF's own data sources use. The MSAL exchange is the one step a test cannot drive offline; overriding ONLY it
    /// leaves every Dataverse read, and the fault-versus-answer classification over their responses, running as
    /// production code. Behaviour is unchanged by the modifier.
    /// </remarks>
    internal virtual async Task<string> GetDataverseTokenViaOBOAsync(string userAccessToken, CancellationToken ct = default)
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
            var lookup = await LookupDataverseUserIdAsync(userId, ct);
            if (string.IsNullOrEmpty(lookup.SystemUserId))
            {
                // Task 132 (C12): "no such user" (the lookup SUCCEEDED and found no row) is an answer and may be
                // cached; a lookup that could not be completed is a fault and may not. Both deny this request.
                _logger.LogWarning(
                    "Could not find Dataverse user for Azure AD OID {AzureAdOid} (lookup faulted: {Faulted}). Returning None access.",
                    userId, lookup.Faulted);
                return new AccessSnapshot
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    AccessRights = AccessRights.None,
                    TeamMemberships = Array.Empty<string>(),
                    Roles = Array.Empty<string>(),
                    CachedAt = DateTimeOffset.UtcNow,
                    Faulted = lookup.Faulted
                };
            }

            var dataverseUserId = lookup.SystemUserId;
            _logger.LogDebug("Mapped Azure AD OID {AzureAdOid} to Dataverse systemuserid {DataverseUserId}", userId, dataverseUserId);

            // Query user permissions from Dataverse using the Dataverse user ID
            var permissions = await QueryUserPermissionsAsync(dataverseUserId, resourceId, dataverseToken, ct);

            // Query team memberships using Dataverse user ID
            var teams = await QueryUserTeamMembershipsAsync(dataverseUserId, ct);

            // Query user roles using Dataverse user ID
            var roles = await QueryUserRolesAsync(dataverseUserId, ct);

            // Determine granular access rights based on permissions
            var accessRights = DetermineAccessLevel(permissions.Records);

            // Task 132 (C12): one fault anywhere — a degraded (probe-derived) or faulted permission read, or a failed
            // team or role sub-read — makes the whole snapshot uncacheable. The rights returned are unchanged.
            var faulted = permissions.Faulted || teams.Faulted || roles.Faulted;

            var snapshot = new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = accessRights,
                TeamMemberships = teams.Values,
                Roles = roles.Values,
                CachedAt = DateTimeOffset.UtcNow,
                Faulted = faulted
            };

            _logger.LogInformation(
                "Access snapshot retrieved for user {UserId}: AccessRights={AccessRights}, Teams={TeamCount}, Roles={RoleCount}, Faulted={Faulted}",
                userId, accessRights, teams.Values.Count, roles.Values.Count, faulted);

            return snapshot;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Task 132 (C12): the CALLER cancelled. Propagate — turning a client abort into an ordinary-looking
            // None is what made it cacheable. An HttpClient TIMEOUT also surfaces as OperationCanceledException, but
            // with the caller's token NOT cancelled: that falls through to the fault arm below.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "Failed to fetch access data for user {UserId} on resource {ResourceId}. Fail-closed: returning AccessRights.None",
                userId,
                resourceId);

            // Fail-closed security: Return None on errors — FAULTED, so it is never cached (task 132).
            return new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = AccessRights.None,
                TeamMemberships = Array.Empty<string>(),
                Roles = Array.Empty<string>(),
                CachedAt = DateTimeOffset.UtcNow,
                Faulted = true
            };
        }
    }

    /// <summary>The entity set targeted by the document-scoped <see cref="GetUserAccessAsync"/> path.</summary>
    /// <remarks>Public since unified-access-control-r2 task 132's share-change eviction: the BFF's snapshot decorator keys
    /// a document snapshot WITHOUT the set (the path is document-only), so an owner or share change on a record of this
    /// set must also evict that key — and it learns "this set is the document path" from here, not from a copy.</remarks>
    public const string DocumentEntitySetName = "sprk_documents";

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

        // faulted (task 132 · C12): true when the denial comes from a read that could not be completed rather than
        // from Dataverse's answer. Both deny this request; only an answer may be cached.
        AccessSnapshot Denied(string reason, bool faulted = false)
        {
            _logger.LogWarning(
                "[UAC-DIAG] RECORD-ACCESS DENIED ({Reason}): User={UserId}, EntitySet={EntitySet}, Record={RecordId}, Faulted={Faulted}",
                reason, userId, entitySetName, recordId, faulted);

            return new AccessSnapshot
            {
                UserId = userId,
                ResourceId = recordId.ToString(),
                AccessRights = AccessRights.None,
                TeamMemberships = Array.Empty<string>(),
                Roles = Array.Empty<string>(),
                CachedAt = DateTimeOffset.UtcNow,
                Faulted = faulted
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
            var lookup = await LookupDataverseUserIdAsync(dataverseToken, userId, ct);
            if (string.IsNullOrEmpty(lookup.SystemUserId))
            {
                // Found no systemuser = an answer (cached); could not look = a fault (never cached). Task 132.
                return lookup.Faulted
                    ? Denied("caller_lookup_faulted", faulted: true)
                    : Denied("caller_not_a_dataverse_user");
            }

            var dataverseUserId = lookup.SystemUserId;

            // AUTHORITATIVE: Dataverse's own answer for this principal on this record.
            var rights = await TryRetrievePrincipalAccessAsync(
                dataverseUserId, entitySetName, recordId.ToString(), dataverseToken, ct);

            var faulted = false;
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
                //
                // Task 132 (C12): what the probe returns is classified, not just mapped. A readable record gives a
                // DEGRADED Read — right for this request, but caching it would pin a Write holder at Read for the
                // TTL, so it is marked faulted. A 403/404 is Dataverse's answer ("you cannot read this") and may be
                // cached. Any other status, or a thrown probe, is a fault.
                var probe = await ProbeRecordReadAccessAsync(entitySetName, recordId, dataverseToken, ct);
                rights = probe == ProbeOutcome.Readable ? AccessRights.Read : AccessRights.None;
                faulted = probe != ProbeOutcome.Refused;
            }

            _logger.LogInformation(
                "[UAC-DIAG] RECORD-ACCESS: User={UserId}, EntitySet={EntitySet}, Record={RecordId}, Rights={Rights}, Faulted={Faulted}",
                userId, entitySetName, recordId, rights.Value, faulted);

            return new AccessSnapshot
            {
                UserId = userId,
                ResourceId = recordId.ToString(),
                AccessRights = rights.Value,
                TeamMemberships = Array.Empty<string>(),
                Roles = Array.Empty<string>(),
                CachedAt = DateTimeOffset.UtcNow,
                Faulted = faulted
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Task 132: the caller cancelled — propagate. A timeout (token NOT cancelled) is a fault, below.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                exception: ex,
                message: "[UAC-DIAG] RECORD-ACCESS ERROR for User={UserId}, EntitySet={EntitySet}, " +
                         "Record={RecordId}. Fail-closed: returning AccessRights.None",
                userId, entitySetName, recordId);

            return Denied("exception", faulted: true);
        }
    }

    /// <summary>What a retrieval probe established (task 132 · C12).</summary>
    internal enum ProbeOutcome
    {
        /// <summary>The probe could not be completed: a status other than 2xx/403/404, or an exception.</summary>
        Faulted = 0,

        /// <summary>2xx: the caller can retrieve the record, so Dataverse granted at least Read.</summary>
        Readable,

        /// <summary>403 or 404: Dataverse's answer is that the caller cannot read the record.</summary>
        Refused,
    }

    /// <summary>
    /// Classifies a probe response: 2xx is readable, 403/404 is Dataverse's refusal, anything else is a fault
    /// (task 132 · C12). The ONE rule both probes use, so they cannot classify the same status differently.
    /// </summary>
    internal static ProbeOutcome ClassifyProbeStatus(System.Net.HttpStatusCode status)
        => (int)status is >= 200 and <= 299
            ? ProbeOutcome.Readable
            : status is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound
                ? ProbeOutcome.Refused
                : ProbeOutcome.Faulted;

    /// <summary>
    /// Entity-agnostic retrieval probe: whether the caller can retrieve the record, which means
    /// Dataverse granted at least Read. Selects <c>createdon</c> because every Dataverse table has it —
    /// this avoids needing each entity's primary-key attribute name, which would have to be guessed.
    /// </summary>
    private async Task<ProbeOutcome> ProbeRecordReadAccessAsync(
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
            var outcome = ClassifyProbeStatus(response.StatusCode);

            if (outcome != ProbeOutcome.Readable)
            {
                _logger.LogWarning(
                    "[UAC-DIAG] RECORD-PROBE {Outcome}: {StatusCode} for EntitySet={EntitySet}, Record={RecordId}",
                    outcome, response.StatusCode, entitySetName, recordId);
            }

            return outcome;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                exception: ex,
                message: "[UAC-DIAG] RECORD-PROBE threw for EntitySet={EntitySet}, Record={RecordId}. " +
                         "Fail-closed: no access (faulted).",
                entitySetName, recordId);

            return ProbeOutcome.Faulted;
        }
    }

    /// <summary>
    /// The outcome of an oid → systemuserid lookup (task 132 · C12). <see cref="SystemUserId"/> null with
    /// <see cref="Faulted"/> false means the lookup SUCCEEDED and found no systemuser — an answer; null with
    /// <see cref="Faulted"/> true means it could not be completed.
    /// </summary>
    private readonly record struct UserLookup(string? SystemUserId, bool Faulted)
    {
        public static UserLookup NotFound => new(null, false);
        public static UserLookup Fault => new(null, true);
    }

    /// <summary>
    /// Looks up the Dataverse systemuserid for an Azure AD Object ID using an EXPLICIT token, so the
    /// call does not depend on (or mutate) <c>_httpClient.DefaultRequestHeaders</c>.
    /// </summary>
    private async Task<UserLookup> LookupDataverseUserIdAsync(
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
                return UserLookup.Fault;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(ct));

            if (!doc.RootElement.TryGetProperty("value", out var value)
                || value.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                // A 2xx without a value array is not a Dataverse answer.
                return UserLookup.Fault;
            }

            if (value.GetArrayLength() == 0)
            {
                return UserLookup.NotFound;
            }

            return value[0].TryGetProperty("systemuserid", out var id) && !string.IsNullOrEmpty(id.GetString())
                ? new UserLookup(id.GetString(), false)
                : UserLookup.Fault;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                exception: ex,
                message: "[UAC-DIAG] systemuser lookup threw for AzureAdOid={AzureAdOid}. Fail-closed: null (faulted).",
                azureAdObjectId);
            return UserLookup.Fault;
        }
    }

    /// <summary>
    /// Looks up the Dataverse systemuserid for a given Azure AD Object ID.
    /// </summary>
    /// <param name="azureAdObjectId">Azure AD Object ID (from token 'oid' claim)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The lookup outcome — found, not found (an answer), or faulted (task 132).</returns>
    private async Task<UserLookup> LookupDataverseUserIdAsync(string azureAdObjectId, CancellationToken ct)
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
                return UserLookup.Fault;
            }

            var result = await response.Content.ReadFromJsonAsync<ODataResponse<SystemUserDto>>(ct);

            if (result?.Value == null)
            {
                // A 2xx without a value array is not a Dataverse answer.
                return UserLookup.Fault;
            }

            if (!result.Value.Any())
            {
                _logger.LogWarning("No Dataverse user found for Azure AD OID {AzureAdOid}", azureAdObjectId);
                return UserLookup.NotFound;
            }

            var user = result.Value.First();
            // PII (D9-01): user full name removed from this authorization-path log. The systemuserid
            // and Azure AD OID GUIDs are sufficient non-PII correlation identifiers for diagnostics.
            _logger.LogInformation("Found Dataverse user (systemuserid: {SystemUserId}) for Azure AD OID {AzureAdOid}",
                user.SystemUserId, azureAdObjectId);

            return string.IsNullOrEmpty(user.SystemUserId) ? UserLookup.Fault : new UserLookup(user.SystemUserId, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error looking up Dataverse user for Azure AD OID {AzureAdOid}", azureAdObjectId);
            return UserLookup.Fault;
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
    private async Task<PermissionRead> QueryUserPermissionsAsync(
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
                return new PermissionRead(new List<PermissionRecord>(), Faulted: false);
            }

            _logger.LogInformation(
                "[UAC-DIAG] RetrievePrincipalAccess SUCCESS: User={UserId}, Resource={ResourceId}, GrantedAccess={AccessRights}",
                userId, resourceId, principalRights.Value);

            return new PermissionRead(
                new List<PermissionRecord> { new PermissionRecord(userId, resourceId, principalRights.Value) },
                Faulted: false);
        }

        // FALLBACK: RetrievePrincipalAccess was unusable. Degrade to the original read probe, which
        // grants at most Read and only when the principal can actually retrieve the record.
        return await QueryReadAccessByProbeAsync(userId, resourceId, dataverseToken, ct);
    }

    /// <summary>
    /// The permission records for one principal on one document, and whether they are a complete answer
    /// (task 132 · C12): <see cref="Faulted"/> is true for a probe-derived (degraded) Read or a failed probe.
    /// </summary>
    private readonly record struct PermissionRead(List<PermissionRecord> Records, bool Faulted);

    /// <summary>A sub-read's values, and whether the read could be completed (task 132 · C12).</summary>
    private readonly record struct SubRead(IReadOnlyList<string> Values, bool Faulted)
    {
        public static SubRead Fault => new(Array.Empty<string>(), true);
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Task 132: the caller cancelled — propagate rather than degrading to the probe.
            throw;
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
    /// <remarks>
    /// Task 132 (C12): the outcome is classified by <see cref="ClassifyProbeStatus"/>, the rule the record probe
    /// uses too. A retrievable document gives a DEGRADED Read (faulted — never cached, because it would pin a
    /// Write holder at Read); a 403/404 is Dataverse's answer (cacheable None); any other status or a thrown
    /// probe is a fault (uncacheable None). Before this, every non-2xx — a 429 or a 500 included — was "no
    /// access" exactly like a 403.
    /// </remarks>
    private async Task<PermissionRead> QueryReadAccessByProbeAsync(
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
            var outcome = ClassifyProbeStatus(response.StatusCode);

            if (outcome != ProbeOutcome.Readable)
            {
                // Capture response body for diagnostics
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                _logger.LogWarning(
                    "[UAC-DIAG] Document query FAILED: StatusCode={StatusCode}, User={UserId}, Resource={ResourceId}, ResponseBody={ResponseBody}",
                    response.StatusCode, userId, resourceId, responseBody);

                // 403 or 404 means no access
                if (outcome == ProbeOutcome.Refused)
                {
                    // Log specific failure reason for diagnostics
                    var failureReason = response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "Document not found (404) - possible replication lag or invalid ID"
                        : "Access forbidden (403) - user lacks permission to this record";

                    _logger.LogWarning(
                        "[UAC-DIAG] Access denied: {FailureReason}, User={UserId}, Resource={ResourceId}",
                        failureReason, userId, resourceId);

                    return new PermissionRead(new List<PermissionRecord>(), Faulted: false);
                }

                // Other errors - log and return empty (fail-closed), FAULTED so it is never cached.
                return new PermissionRead(new List<PermissionRecord>(), Faulted: true);
            }

            // Success: the principal can retrieve the document, so they hold at least Read.
            _logger.LogInformation(
                "[UAC-DIAG] Document query SUCCESS (fallback probe): User={UserId}, Resource={ResourceId}, GrantedAccess=Read",
                userId, resourceId);

            return new PermissionRead(
                new List<PermissionRecord>
                {
                    // Read only. This probe cannot observe Write/Delete/Create/Share — it only knows the
                    // record was retrievable. The old comment here claimed "Dataverse will enforce
                    // Write/Delete separately"; on the SPA/Teams surface that is false, because the BFF
                    // filter IS the enforcement point (finding A-20). RetrievePrincipalAccess above is the
                    // path that answers the full question; reaching here means it was unavailable.
                    new PermissionRecord(userId, resourceId, AccessRights.Read)
                },
                // DEGRADED: right for this request, never cached (task 132).
                Faulted: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error querying Dataverse access for {UserId} on {ResourceId}", userId, resourceId);
            return new PermissionRead(new List<PermissionRecord>(), Faulted: true);
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
    /// <remarks>Task 132 (C12): a failed read still yields an empty list for this request, but is marked faulted so
    /// the snapshot carrying it is never cached; a successful read of zero teams is an answer.</remarks>
    private async Task<SubRead> QueryUserTeamMembershipsAsync(string userId, CancellationToken ct)
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
                return SubRead.Fault;
            }

            var result = await response.Content.ReadFromJsonAsync<ODataResponse<TeamDto>>(ct);

            if (result?.Value == null)
            {
                return SubRead.Fault;
            }

            return new SubRead(result.Value.Select(t => t.TeamId ?? t.Name ?? "unknown").ToList(), Faulted: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error querying team memberships for {UserId}", userId);
            return SubRead.Fault;
        }
    }

    /// <summary>
    /// Queries user's security roles.
    /// </summary>
    /// <remarks>Task 132 (C12): classified exactly as <see cref="QueryUserTeamMembershipsAsync"/>.</remarks>
    private async Task<SubRead> QueryUserRolesAsync(string userId, CancellationToken ct)
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
                return SubRead.Fault;
            }

            var result = await response.Content.ReadFromJsonAsync<ODataResponse<RoleDto>>(ct);

            if (result?.Value == null)
            {
                return SubRead.Fault;
            }

            return new SubRead(result.Value.Select(r => r.Name ?? "unknown").ToList(), Faulted: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(exception: ex, message: "Error querying user roles for {UserId}", userId);
            return SubRead.Fault;
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
