// -----------------------------------------------------------------------------
// ExchangePolicySidecarClient.cs
//
// Production IExchangePolicyApplier (H14a) + IExchangePolicyReadClient (H13 T4): HTTP to the
// Exchange sidecar (Listener.ps1 / SidecarCore.psm1) on localhost beside the Worker. The sidecar
// owns the Exchange PowerShell module; this client owns the two credentials it needs:
//
//   X-Sidecar-Auth          the per-boot shared secret, read from the platform Key Vault (the
//                           sidecar reads the same secret through its own App Service setting —
//                           neither side trusts the other's environment).
//   X-Exchange-Access-Token an Exchange Online token for 'Spaarke Exchange Admin', minted through
//                           the Worker managed identity's federated credential (task 251, owner
//                           D24). The sidecar holds no credential. The token is never logged.
//
// Each body also names the tenant's initial domain as `organization` (from the same token source):
// the only -Organization value that lets app-only Exchange writes succeed.
//
// WIRE (task 251 — RBAC for Applications, owner D26):
//   POST /apply-mailbox-access { tenantId, organization, appId, servicePrincipalObjectId, displayName,
//        scopeGroupId, assignments:[{name, role}], correlationId, timeoutSeconds }
//     200 { outcome: Success|AlreadyCompliant|Drift|Failure, createdCount,
//           assignments:[{name, role, scope, inExpectedScope}], conflicts:[..], diagnostic }
//   POST /read-mailbox-access  { tenantId, organization, appId, scopeGroupId, roles:[..], correlationId }
//     200 { outcome: Success|Failure, servicePrincipalRegistered, assignments:[..], diagnostic }
//   400 invalid body / missing token · 401 bad shared secret · 404 unknown route
//   503 sidecar missing a setting · other 5xx server error
//
// RETRY (DS-1b §3): apply retries ONCE after SidecarTransientRetryDelay on HTTP 5xx (except 503,
// "sidecar not configured") or wire Failure (transient Exchange throttling); everything else is terminal here and the run-level
// reconciler re-enqueues. The read route never retries (H13 classifies its failure Resumable).
//
// LOUD-FAIL: every condition that could become an unauthenticated call or a swallowed error
// returns an explicit Failure naming what to check — never an empty header.
// -----------------------------------------------------------------------------

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>HTTP client for the H14a Exchange sidecar (apply + read routes).</summary>
public sealed class ExchangePolicySidecarClient : IExchangePolicyApplier, IExchangePolicyReadClient
{
    /// <summary>Header carrying the per-boot shared secret.</summary>
    public const string SharedSecretHeaderName = "X-Sidecar-Auth";

    /// <summary>Header carrying the Exchange Online access token.</summary>
    public const string ExchangeTokenHeaderName = "X-Exchange-Access-Token";

    /// <summary>Apply route (H14a).</summary>
    public const string ApplyPath = "/apply-mailbox-access";

    /// <summary>Read-only route (H13 T4).</summary>
    public const string ReadPath = "/read-mailbox-access";

    /// <summary>Wire outcome: assignments created this call.</summary>
    public const string WireOutcomeSuccess = "Success";

    /// <summary>Wire outcome: everything already in place.</summary>
    public const string WireOutcomeAlreadyCompliant = "AlreadyCompliant";

    /// <summary>Wire outcome: T4 drift — existing assignments differ; nothing changed.</summary>
    public const string WireOutcomeDrift = "Drift";

    /// <summary>Wire outcome: no conclusive result inside the sidecar.</summary>
    public const string WireOutcomeFailure = "Failure";

    /// <summary>Advisory budget sent as <c>timeoutSeconds</c>; HttpClient.Timeout is the real bound.</summary>
    public const int ListenerAdvisoryTimeoutSeconds = 300;

    private static readonly JsonSerializerOptions SerializerOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _httpClient;
    private readonly IKvSecretReader _kvSecretReader;
    private readonly ExchangeAdminTokenSource _tokenSource;
    private readonly IntegrationWiringOptions _options;
    private readonly ILogger<ExchangePolicySidecarClient> _logger;

    /// <summary>
    /// Registered as a typed HttpClient for both seams. BaseAddress + Timeout are set from options
    /// unless the injected client already has a BaseAddress (the test seam).
    /// </summary>
    public ExchangePolicySidecarClient(
        HttpClient httpClient,
        IKvSecretReader kvSecretReader,
        ExchangeAdminTokenSource tokenSource,
        IOptions<IntegrationWiringOptions> options,
        ILogger<ExchangePolicySidecarClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(kvSecretReader);
        ArgumentNullException.ThrowIfNull(tokenSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _kvSecretReader = kvSecretReader;
        _tokenSource = tokenSource;
        _options = options.Value;
        _logger = logger;
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.SidecarBaseUrl);
            _httpClient.Timeout = _options.SidecarRequestTimeout;
        }
    }

    /// <inheritdoc/>
    public async Task<ExchangePolicyApplyOutcome> ApplyAsync(ExchangePolicyApplyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return new ExchangePolicyApplyOutcome.Failure(invalid);
        }

        var headers = await ResolveHeadersAsync(request.TenantId, cancellationToken).ConfigureAwait(false);
        if (headers.Failure is not null)
        {
            return new ExchangePolicyApplyOutcome.Failure(headers.Failure);
        }

        var wire = new SidecarApplyRequest
        {
            TenantId = request.TenantId,
            Organization = headers.Organization,
            AppId = request.AppId,
            ServicePrincipalObjectId = request.ServicePrincipalObjectId,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName,
            ScopeGroupId = request.ScopeGroupId,
            Assignments = request.Assignments.Select(a => new SidecarAssignmentSpec { Name = a.Name, Role = a.Role }).ToArray(),
            CorrelationId = request.CorrelationId,
            TimeoutSeconds = ListenerAdvisoryTimeoutSeconds,
        };

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var sent = await SendAsync(ApplyPath, wire, headers, request.CorrelationId, cancellationToken).ConfigureAwait(false);
            ApplyAttempt result = sent.Failure is not null
                ? ApplyAttempt.Terminal(new ExchangePolicyApplyOutcome.Failure(sent.Failure))
                // 503 = the sidecar is missing a setting: deterministic, so not retried (code review S5).
                : sent.Status >= 500 && sent.Status != 503
                    ? ApplyAttempt.Retry($"HTTP {sent.Status} from sidecar: {Truncate(sent.Body, 400)}")
                    : sent.Status == 200
                        ? MapApplyResponse(sent.Body, request.CorrelationId)
                        : ApplyAttempt.Terminal(new ExchangePolicyApplyOutcome.Failure(DescribeStatus(ApplyPath, sent.Status, sent.Body, request.CorrelationId)));

            if (result.Outcome is not null)
            {
                return result.Outcome;
            }
            if (attempt == 2)
            {
                _logger.LogWarning("H14a sidecar failed again after one retry (correlationId={CorrelationId}): {Diagnostic}", request.CorrelationId, result.RetryDiagnostic);
                return new ExchangePolicyApplyOutcome.Failure(
                    $"Sidecar failed after one retry with {_options.SidecarTransientRetryDelay} backoff: {result.RetryDiagnostic} " +
                    $"(correlationId={request.CorrelationId}). The reconciler will re-enqueue.");
            }
            _logger.LogWarning("H14a sidecar retry-eligible failure (correlationId={CorrelationId}); retrying after {Delay}: {Diagnostic}",
                request.CorrelationId, _options.SidecarTransientRetryDelay, result.RetryDiagnostic);
            await Task.Delay(_options.SidecarTransientRetryDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Unreachable: the apply loop returns on its second attempt.");
    }

    /// <inheritdoc/>
    public async Task<ExchangePolicyReadOutcome> ReadAsync(ExchangePolicyReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.AppId) || request.Roles is not { Count: > 0 })
        {
            return new ExchangePolicyReadOutcome.Failure("Read request needs TenantId, AppId and at least one role.");
        }

        var headers = await ResolveHeadersAsync(request.TenantId, cancellationToken).ConfigureAwait(false);
        if (headers.Failure is not null)
        {
            return new ExchangePolicyReadOutcome.Failure(headers.Failure);
        }

        var wire = new SidecarReadRequest
        {
            TenantId = request.TenantId,
            Organization = headers.Organization,
            AppId = request.AppId,
            ScopeGroupId = string.IsNullOrWhiteSpace(request.ScopeGroupId) ? null : request.ScopeGroupId,
            Roles = request.Roles.ToArray(),
            CorrelationId = request.CorrelationId,
        };
        var sent = await SendAsync(ReadPath, wire, headers, request.CorrelationId, cancellationToken).ConfigureAwait(false);
        if (sent.Failure is not null)
        {
            return new ExchangePolicyReadOutcome.Failure(sent.Failure);
        }
        if (sent.Status != 200)
        {
            return new ExchangePolicyReadOutcome.Failure(DescribeStatus(ReadPath, sent.Status, sent.Body, request.CorrelationId));
        }

        SidecarReadResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SidecarReadResponse>(sent.Body, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return new ExchangePolicyReadOutcome.Failure($"Sidecar read returned unparseable JSON (correlationId={request.CorrelationId}): {ex.Message}. Body: {Truncate(sent.Body, 400)}");
        }
        if (parsed is null || parsed.Outcome != WireOutcomeSuccess)
        {
            return new ExchangePolicyReadOutcome.Failure(
                $"Sidecar read did not succeed (outcome '{parsed?.Outcome ?? "(null)"}', correlationId={request.CorrelationId}): {parsed?.Diagnostic ?? Truncate(sent.Body, 400)}");
        }
        return new ExchangePolicyReadOutcome.Success(parsed.ServicePrincipalRegistered, ToViews(parsed.Assignments));
    }

    private static string? Validate(ExchangePolicyApplyRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.TenantId) || string.IsNullOrWhiteSpace(r.AppId)
            || string.IsNullOrWhiteSpace(r.ServicePrincipalObjectId) || string.IsNullOrWhiteSpace(r.ScopeGroupId))
        {
            return "Apply request needs TenantId, AppId, ServicePrincipalObjectId and ScopeGroupId.";
        }
        if (r.Assignments is not { Count: > 0 })
        {
            return "Apply request lists no role assignments.";
        }
        if (string.IsNullOrWhiteSpace(r.CorrelationId))
        {
            return "CorrelationId is required — H14a passes envelope.RunId so sidecar logs interleave with the Worker's by RunId.";
        }
        return null;
    }

    /// <summary>The shared secret, the Exchange token and the organization — all three, or a diagnostic. Never an empty header.</summary>
    private async Task<Headers> ResolveHeadersAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SidecarSharedSecretVaultName)
            || string.IsNullOrWhiteSpace(_options.SidecarSharedSecretSubscriptionId)
            || string.IsNullOrWhiteSpace(_options.SidecarSharedSecretName))
        {
            return Headers.Fail(
                "Sidecar shared secret config missing: " +
                $"SidecarSharedSecretVaultName='{_options.SidecarSharedSecretVaultName}', " +
                $"SidecarSharedSecretSubscriptionId='{_options.SidecarSharedSecretSubscriptionId}', " +
                $"SidecarSharedSecretName='{_options.SidecarSharedSecretName}'. Bind all three " +
                $"{IntegrationWiringModule.ConfigSection}:SidecarSharedSecret* settings.");
        }

        string secret;
        try
        {
            var kv = await _kvSecretReader.ReadSecretAsync(
                _options.SidecarSharedSecretVaultName, _options.SidecarSharedSecretSubscriptionId, _options.SidecarSharedSecretName, cancellationToken)
                .ConfigureAwait(false);
            switch (kv)
            {
                case KvSecretReadResult.Success s:
                    secret = s.Value;
                    break;
                case KvSecretReadResult.NotFound:
                    return Headers.Fail(
                        $"Platform KV secret '{_options.SidecarSharedSecretName}' not found on vault '{_options.SidecarSharedSecretVaultName}'. " +
                        "The sidecar's SIDECAR_SHARED_SECRET setting must reference the same secret, and the Worker managed identity needs " +
                        "'Key Vault Secrets User' on the vault.");
                case KvSecretReadResult.Failure f:
                    return Headers.Fail($"Platform KV read failed for '{_options.SidecarSharedSecretName}' on vault '{_options.SidecarSharedSecretVaultName}': {f.Diagnostic}");
                default:
                    return Headers.Fail($"Unhandled {nameof(KvSecretReadResult)} '{kv.GetType().Name}' reading the sidecar shared secret.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Headers.Fail($"Unexpected {ex.GetType().Name} reading the sidecar shared secret: {ex.Message}");
        }

        var token = await _tokenSource.GetTokenAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return token switch
        {
            ExchangeTokenResult.Success t => Headers.Ok(secret, t.AccessToken, t.Organization),
            ExchangeTokenResult.Failure f => Headers.Fail(f.Diagnostic),
            _ => Headers.Fail($"Unhandled {nameof(ExchangeTokenResult)} '{token.GetType().Name}'."),
        };
    }

    private async Task<Sent> SendAsync(string path, object body, Headers headers, string correlationId, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path);
        message.Headers.TryAddWithoutValidation(SharedSecretHeaderName, headers.Secret);
        message.Headers.TryAddWithoutValidation(ExchangeTokenHeaderName, headers.Token);
        message.Content = JsonContent.Create(body, body.GetType(), options: SerializerOptions);
        try
        {
            using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new Sent((int)response.StatusCode, text, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient.Timeout elapsed — DS-1b §3 InfraFault, no in-client retry.
            _logger.LogWarning("Sidecar POST {Path} timed out after {Timeout} (correlationId={CorrelationId})", path, _httpClient.Timeout, correlationId);
            return new Sent(0, string.Empty, $"Sidecar POST {path} timed out after {_httpClient.Timeout} (correlationId={correlationId}). The reconciler will re-enqueue.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Sidecar POST {Path} transport failure (correlationId={CorrelationId})", path, correlationId);
            return new Sent(0, string.Empty, $"Sidecar POST {path} transport failure: {ex.GetType().Name}: {ex.Message} (correlationId={correlationId}). The reconciler will re-enqueue.");
        }
    }

    private string DescribeStatus(string path, int status, string body, string correlationId) => status switch
    {
        401 => $"Sidecar rejected X-Sidecar-Auth (HTTP 401, correlationId={correlationId}) — platform KV secret '{_options.SidecarSharedSecretName}' " +
               $"on vault '{_options.SidecarSharedSecretVaultName}' must match the sidecar's SIDECAR_SHARED_SECRET setting. Body: {Truncate(body, 300)}",
        404 => $"Sidecar returned HTTP 404 for POST {path} (correlationId={correlationId}) — the deployed sidecar image does not serve this route " +
               $"(an image older than task 251?). Body: {Truncate(body, 300)}",
        400 => $"Sidecar rejected the request (HTTP 400, correlationId={correlationId}): {Truncate(body, 400)}",
        503 => $"Sidecar is not configured (HTTP 503, correlationId={correlationId}): {Truncate(body, 400)}",
        _ => $"Sidecar returned unexpected HTTP {status} for POST {path} (correlationId={correlationId}): {Truncate(body, 400)}",
    };

    private static ApplyAttempt MapApplyResponse(string body, string correlationId)
    {
        SidecarApplyResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SidecarApplyResponse>(body, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return ApplyAttempt.Terminal(new ExchangePolicyApplyOutcome.Failure(
                $"Sidecar returned HTTP 200 with unparseable JSON (correlationId={correlationId}): {ex.Message}. Body: {Truncate(body, 400)}"));
        }
        if (parsed is null)
        {
            return ApplyAttempt.Terminal(new ExchangePolicyApplyOutcome.Failure($"Sidecar returned HTTP 200 with an empty body (correlationId={correlationId})."));
        }

        var diagnostic = string.IsNullOrEmpty(parsed.Diagnostic) ? "(no diagnostic)" : parsed.Diagnostic;
        return parsed.Outcome switch
        {
            WireOutcomeSuccess or WireOutcomeAlreadyCompliant => ApplyAttempt.Terminal(new ExchangePolicyApplyOutcome.Applied(
                parsed.CreatedCount, ToViews(parsed.Assignments).Select(a => a.Name).ToArray())),
            WireOutcomeDrift => ApplyAttempt.Terminal(new ExchangePolicyApplyOutcome.Drift(
                parsed.Conflicts is { Length: > 0 } c ? c : new[] { diagnostic })),
            WireOutcomeFailure => ApplyAttempt.Retry($"sidecar wire Failure: {diagnostic}"),
            _ => ApplyAttempt.Terminal(new ExchangePolicyApplyOutcome.Failure(
                $"Sidecar returned unknown outcome '{parsed.Outcome ?? "(null)"}' (correlationId={correlationId}); expected " +
                $"{WireOutcomeSuccess}, {WireOutcomeAlreadyCompliant}, {WireOutcomeDrift} or {WireOutcomeFailure}. Diagnostic: {diagnostic}")),
        };
    }

    private static IReadOnlyList<ExchangeRoleAssignmentView> ToViews(SidecarAssignmentView[]? views)
        => (views ?? Array.Empty<SidecarAssignmentView>())
            .Select(v => new ExchangeRoleAssignmentView(v.Name ?? string.Empty, v.Role ?? string.Empty, v.Scope ?? string.Empty, v.InExpectedScope))
            .ToArray();

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "...[truncated]");

    // ---- Wire DTOs (internal so the contract tests can assert them) ----

    internal sealed class SidecarAssignmentSpec
    {
        [JsonPropertyName("name")] public string Name { get; init; } = default!;
        [JsonPropertyName("role")] public string Role { get; init; } = default!;
    }

    internal sealed class SidecarApplyRequest
    {
        [JsonPropertyName("tenantId")] public string TenantId { get; init; } = default!;
        [JsonPropertyName("organization")] public string Organization { get; init; } = default!;
        [JsonPropertyName("appId")] public string AppId { get; init; } = default!;
        [JsonPropertyName("servicePrincipalObjectId")] public string ServicePrincipalObjectId { get; init; } = default!;
        [JsonPropertyName("displayName")] public string? DisplayName { get; init; }
        [JsonPropertyName("scopeGroupId")] public string ScopeGroupId { get; init; } = default!;
        [JsonPropertyName("assignments")] public SidecarAssignmentSpec[] Assignments { get; init; } = default!;
        [JsonPropertyName("correlationId")] public string CorrelationId { get; init; } = default!;
        [JsonPropertyName("timeoutSeconds")] public int TimeoutSeconds { get; init; }
    }

    internal sealed class SidecarReadRequest
    {
        [JsonPropertyName("tenantId")] public string TenantId { get; init; } = default!;
        [JsonPropertyName("organization")] public string Organization { get; init; } = default!;
        [JsonPropertyName("appId")] public string AppId { get; init; } = default!;
        [JsonPropertyName("scopeGroupId")] public string? ScopeGroupId { get; init; }
        [JsonPropertyName("roles")] public string[] Roles { get; init; } = default!;
        [JsonPropertyName("correlationId")] public string CorrelationId { get; init; } = default!;
    }

    internal sealed class SidecarAssignmentView
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("role")] public string? Role { get; init; }
        [JsonPropertyName("scope")] public string? Scope { get; init; }
        [JsonPropertyName("inExpectedScope")] public bool InExpectedScope { get; init; }
    }

    internal sealed class SidecarApplyResponse
    {
        [JsonPropertyName("outcome")] public string? Outcome { get; init; }
        [JsonPropertyName("createdCount")] public int CreatedCount { get; init; }
        [JsonPropertyName("assignments")] public SidecarAssignmentView[]? Assignments { get; init; }
        [JsonPropertyName("conflicts")] public string[]? Conflicts { get; init; }
        [JsonPropertyName("diagnostic")] public string? Diagnostic { get; init; }
    }

    internal sealed class SidecarReadResponse
    {
        [JsonPropertyName("outcome")] public string? Outcome { get; init; }
        [JsonPropertyName("servicePrincipalRegistered")] public bool ServicePrincipalRegistered { get; init; }
        [JsonPropertyName("assignments")] public SidecarAssignmentView[]? Assignments { get; init; }
        [JsonPropertyName("diagnostic")] public string? Diagnostic { get; init; }
    }

    // ---- Internal helper types ----

    private readonly record struct Sent(int Status, string Body, string? Failure);

    private readonly record struct Headers(string Secret, string Token, string Organization, string? Failure)
    {
        /// <summary>Never prints the secret or the token (code review W10).</summary>
        public override string ToString() => Failure is null ? $"Headers {{ Secret = ***, Token = ***, Organization = {Organization} }}" : $"Headers {{ Failure = {Failure} }}";

        public static Headers Ok(string secret, string token, string organization) => new(secret, token, organization, null);
        public static Headers Fail(string diagnostic) => new(string.Empty, string.Empty, string.Empty, diagnostic);
    }

    private readonly record struct ApplyAttempt(ExchangePolicyApplyOutcome? Outcome, string? RetryDiagnostic)
    {
        public static ApplyAttempt Terminal(ExchangePolicyApplyOutcome outcome) => new(outcome, null);
        public static ApplyAttempt Retry(string diagnostic) => new(null, diagnostic);
    }
}
