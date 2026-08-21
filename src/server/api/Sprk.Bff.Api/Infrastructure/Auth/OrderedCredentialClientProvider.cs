using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Infrastructure.Auth;

/// <summary>
/// <see cref="IConfidentialClientProvider"/> that selects the BFF's confidential credential from the
/// configured ordered list — MI-FIC, then a Key Vault certificate, then the transitional client secret
/// (ADR-028 Amendment <b>A4</b>, spec FR-B2) — and owns the single process-wide confidential-client
/// cache.
///
/// <para><b>What makes this the rollback mechanism.</b> Design §6 claims rollback at every phase is a
/// credential reorder. That is true only because the selection reads <c>Graph:Credentials:Order</c> at
/// runtime instead of each call site hard-coding one credential. Reordering the list plus a restart
/// changes which credential the BFF presents, with no code change and no redeploy (NFR-06). Removing
/// this class does not degrade the system gracefully — it makes that sentence false.</para>
///
/// <para><b>Selection proves a credential, it does not assume one.</b> Each candidate is <i>acquired</i>
/// before it is bound: an assertion is minted, a certificate is fetched. A credential that cannot be
/// acquired is skipped with a logged warning naming it and the reason. This is why
/// <see cref="GetClientAsync"/> is async — the alternative, binding a credential and discovering at the
/// first token request that it never worked, gives the caller a failed OBO exchange instead of a
/// fall-through, and on the OBO path that is every user at once.</para>
///
/// <para><b>Fall-through is NOT uniform, and that asymmetry is the point.</b> See
/// <see cref="IsFallThroughEligible"/>. Treating every managed-identity failure as a fall-through would
/// convert this class from a safety mechanism into a hazard: the FR-B4 wrong-identity signature would
/// silently route production onto the transitional secret while every health signal stayed green.</para>
///
/// <para><b>Recovery is automatic and time-bounded.</b> When a lower-priority credential wins, the
/// selection is cached only until the suppression on the skipped higher-priority credentials expires —
/// seconds, not minutes. After that the next call re-evaluates from the top. Without that the first
/// transient MI-FIC failure would pin the process to the fallback secret until someone restarted it,
/// which is the silent downgrade this project exists to eliminate. The bound comes from task 030's
/// measurement of Entra's ~2-minute post-change flap window, not from taste.</para>
///
/// <para><b>Instance state, not static.</b> Registered singleton, injected everywhere. The existing
/// per-class caches use <c>static</c> fields because their owning types are transient and could not
/// otherwise share; that constraint does not apply here, and instance state keeps the cache disposable
/// and testable without cross-test bleed. Task 022 collapses those three caches onto this one and
/// closes task 011's time-boxed A4 exception.</para>
///
/// <para>Introduced by <c>spaarke-auth-v4-dataverse-MI</c> task 021 (FR-B2).</para>
/// </summary>
public sealed class OrderedCredentialClientProvider : IConfidentialClientProvider
{
    private readonly IClientAssertionProvider? _assertionProvider;
    private readonly SecretClient? _secretClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrderedCredentialClientProvider> _logger;
    private readonly TimeProvider _time;
    private readonly CredentialSelectionOptions _options;
    private readonly IReadOnlyList<CredentialKind> _order;

    /// <summary>
    /// THE confidential-client cache. Keyed <c>(tenant | client | kind | credential-fingerprint)</c>.
    ///
    /// <para><b>Every part of that key is load-bearing.</b> <c>kind</c> because MSAL binds the credential
    /// at <c>Build()</c> and holds it for the client's lifetime — a client built on MI-FIC and one built
    /// on the secret are different objects, and omitting the kind would hand a rollback the
    /// pre-rollback client. The <c>fingerprint</c> because of the same binding: a
    /// <c>(tenant|client)</c>-only key silently reuses a client built with a <i>stale</i> secret after a
    /// rotation, presenting as <c>AADSTS7000215</c> on OBO while app-only keeps working, and "fixed" by
    /// a restart nobody can explain. That was task 011's code-review finding W-1 and it is preserved
    /// here deliberately rather than rediscovered later.</para>
    /// </summary>
    private readonly ConcurrentDictionary<string, IConfidentialClientApplication> _clients = new();

    /// <summary>
    /// Per-key count of clients actually BUILT. Test-observability surface, not API: it makes client
    /// sharing assertable as behaviour (construct N, observe one build) without reflecting on private
    /// state (ADR-038 ban B8) or resolving from a container (ban B3). Counts builds rather than entries,
    /// so a <c>GetOrAdd</c> factory that ran twice under contention is visible rather than silent.
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _builds = new();

    /// <summary>Which credential currently wins for a <c>(tenant | client)</c>, and until when that answer stands.</summary>
    private readonly ConcurrentDictionary<string, Selection> _selection = new();

    /// <summary>Consecutive-failure memo per <c>(tenant | client | kind)</c> — the negative cache.</summary>
    private readonly ConcurrentDictionary<string, Failure> _failures = new();

    /// <summary>Serialises selection per <c>(tenant | client)</c> so concurrent first-callers probe once, not N times.</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    private sealed record Selection(CredentialKind Kind, DateTimeOffset ReevaluateAt);

    private sealed record Failure(int ConsecutiveCount, DateTimeOffset SuppressedUntil);

    public OrderedCredentialClientProvider(
        IOptions<CredentialSelectionOptions> options,
        IConfiguration configuration,
        ILogger<OrderedCredentialClientProvider> logger,
        IClientAssertionProvider? assertionProvider = null,
        SecretClient? secretClient = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assertionProvider = assertionProvider;
        _secretClient = secretClient;
        _time = timeProvider ?? TimeProvider.System;
        _options = options.Value;

        _order = _options.Order
            .Select(raw => Enum.TryParse<CredentialKind>(raw?.Trim(), ignoreCase: true, out var k)
                ? (CredentialKind?)k
                : null)
            .Where(k => k.HasValue)
            .Select(k => k!.Value)
            .ToList();

        // The validator rejects an unparseable or empty order at startup, so reaching here with an empty
        // list means validation was bypassed (direct construction in a test). Fail with the same
        // actionable message rather than degrading into "no credential, no explanation".
        if (_order.Count == 0)
        {
            throw new InvalidOperationException(
                $"{CredentialSelectionOptions.SectionName}:Order resolved to no valid credential kinds. "
                + $"Valid values: {string.Join(", ", Enum.GetNames<CredentialKind>())}.");
        }

        if (CredentialSelectionOptionsValidator.PromotesSecretAboveSecretFreeCredential(_order))
        {
            // ADR-028 A4 deviation. Permitted, because this ordering IS the rollback (see the validator's
            // remarks on why rejecting it would disable the emergency exit at the moment it is needed) —
            // but never silent. If this line is in the logs of a steady-state environment, the temporary
            // rollback became permanent.
            _logger.LogError(
                "ADR-028 A4 DEVIATION: credential order {Order} places the client secret ABOVE a "
                + "secret-free credential. This is valid only as a deliberate, temporary rollback. "
                + "Restore the secret-free credential to the top once the incident is resolved.",
                string.Join(" > ", _order));
        }

        _logger.LogInformation(
            "Ordered credential selection active: {Order}. Negative-cache {Seconds}s after "
            + "{Failures} consecutive failures.",
            string.Join(" > ", _order), _options.NegativeCacheSeconds, _options.FailuresBeforeSuppression);
    }

    /// <summary>
    /// The single place that decides whether a managed-identity failure means "not here" (fall through)
    /// or "wrong" (fail loud). One predicate, one test, no re-derivation per consumer.
    ///
    /// <para><b>The distinction that matters.</b>
    /// <c>managed_identity_unreachable_network</c> means there is no route to IMDS at all — the ordinary
    /// developer-workstation shape, and exactly what ordered selection exists to handle.
    /// <c>managed_identity_request_failed</c> means IMDS answered and the configured identity was absent
    /// or wrong. That is the <b>FR-B4 signature</b>: five user-assigned identities exist in the dev
    /// subscription and one is named like the BFF's without being attached to it. Falling through on it
    /// would run production on the transitional secret while every health check stayed green — the
    /// failure mode is not an outage, it is an outage that never appears. So it is rethrown.</para>
    ///
    /// <para><b>Allowlist, not denylist.</b> Only codes proven to mean "no managed identity in this
    /// environment" fall through; every other code — including ones MSAL may add in a future version —
    /// fails loud. A denylist would silently grant fall-through to unknown future errors, which is the
    /// wrong default for a credential downgrade.</para>
    /// </summary>
    public static bool IsFallThroughEligible(MsalServiceException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.ErrorCode is
            MsalError.ManagedIdentityUnreachableNetwork or
            MsalError.ManagedIdentityAllSourcesUnavailable;
    }

    /// <summary>
    /// The credential kind currently selected for a <c>(tenant, client)</c>, or <c>null</c> if selection
    /// has not run. Test-observability surface, not API — it is what lets a test assert that reordering
    /// the configured list changes the selected credential, rather than asserting on DI wiring.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public CredentialKind? SelectedKindFor(string tenantId, string clientId)
        => _selection.TryGetValue(SelectionKey(tenantId, clientId), out var s) ? s.Kind : null;

    /// <inheritdoc cref="_builds" />
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public int BuildCountFor(string tenantId, string clientId, CredentialKind kind)
    {
        var descriptor = DescribeCredential(kind);
        return descriptor is not null
            && _builds.TryGetValue(ClientCacheKey(tenantId, clientId, kind, descriptor.Fingerprint), out var n)
            ? n
            : 0;
    }

    /// <inheritdoc />
    public async Task<IConfidentialClientApplication> GetClientAsync(
        string tenantId,
        string clientId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var selectionKey = SelectionKey(tenantId, clientId);

        if (TryUseExistingSelection(selectionKey, tenantId, clientId, out var cached))
        {
            return cached;
        }

        // Serialise per (tenant, client): concurrent first-callers would otherwise each mint an
        // assertion or fetch a certificate to answer the same question.
        var gate = _gates.GetOrAdd(selectionKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (TryUseExistingSelection(selectionKey, tenantId, clientId, out cached))
            {
                return cached;
            }

            return await SelectAndBuildAsync(tenantId, clientId, selectionKey, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryUseExistingSelection(
        string selectionKey,
        string tenantId,
        string clientId,
        out IConfidentialClientApplication client)
    {
        client = null!;

        if (!_selection.TryGetValue(selectionKey, out var selection))
        {
            return false;
        }

        // A selection that skipped a higher-priority credential is valid only while that credential is
        // still suppressed. Past that instant, re-evaluate from the top so recovery is automatic —
        // otherwise one transient MI-FIC failure pins the process to the fallback until a restart.
        if (_time.GetUtcNow() >= selection.ReevaluateAt)
        {
            return false;
        }

        var descriptor = DescribeCredential(selection.Kind);
        if (descriptor is null)
        {
            return false;
        }

        return _clients.TryGetValue(
            ClientCacheKey(tenantId, clientId, selection.Kind, descriptor.Fingerprint), out client!);
    }

    private async Task<IConfidentialClientApplication> SelectAndBuildAsync(
        string tenantId,
        string clientId,
        string selectionKey,
        CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var skipped = new List<string>();

        // Earliest instant at which a skipped higher-priority credential becomes worth retrying. Drives
        // automatic recovery back up the list.
        var earliestRetry = DateTimeOffset.MaxValue;

        foreach (var kind in _order)
        {
            var failureKey = FailureKey(tenantId, clientId, kind);

            if (_failures.TryGetValue(failureKey, out var failure) && failure.SuppressedUntil > now)
            {
                // The warning was already emitted when the credential actually failed; repeating it on
                // every request during suppression would bury it. Debug here, and the suppression is
                // bounded in seconds.
                _logger.LogDebug(
                    "Credential {Kind} suppressed for a further {Seconds:F1}s after {Count} consecutive "
                    + "failures; trying the next credential.",
                    kind, (failure.SuppressedUntil - now).TotalSeconds, failure.ConsecutiveCount);

                skipped.Add($"{kind} (suppressed after {failure.ConsecutiveCount} consecutive failures)");
                earliestRetry = Min(earliestRetry, failure.SuppressedUntil);
                continue;
            }

            CredentialDescriptor? descriptor;
            try
            {
                descriptor = await AcquireAsync(kind, ct).ConfigureAwait(false);
            }
            catch (MsalServiceException ex) when (IsFallThroughEligible(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Credential {Kind} is unavailable in this environment ({ErrorCode}); falling through "
                    + "to the next configured credential.",
                    kind, ex.ErrorCode);

                skipped.Add($"{kind} ({ex.ErrorCode})");
                earliestRetry = Min(earliestRetry, RecordFailure(failureKey, now));
                continue;
            }
            catch (RequestFailedException ex) when (IsTransientKeyVaultFailure(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Credential {Kind} could not be loaded from Key Vault (HTTP {Status}); falling "
                    + "through to the next configured credential.",
                    kind, ex.Status);

                skipped.Add($"{kind} (Key Vault HTTP {ex.Status})");
                earliestRetry = Min(earliestRetry, RecordFailure(failureKey, now));
                continue;
            }

            if (descriptor is null)
            {
                // Not configured at all — the ordinary "no secret in this environment" case. A
                // configured-but-broken credential does NOT arrive here; it throws above.
                _logger.LogWarning(
                    "Credential {Kind} is not configured; falling through to the next configured credential.",
                    kind);

                skipped.Add($"{kind} (not configured)");
                earliestRetry = Min(earliestRetry, RecordFailure(failureKey, now));
                continue;
            }

            _failures.TryRemove(failureKey, out _);

            var built = false;
            var client = _clients.GetOrAdd(
                ClientCacheKey(tenantId, clientId, kind, descriptor.Fingerprint),
                key =>
                {
                    built = true;
                    _builds.AddOrUpdate(key, 1, (_, n) => n + 1);
                    return descriptor.Apply(
                            ConfidentialClientApplicationBuilder
                                .Create(clientId)
                                .WithAuthority($"https://login.microsoftonline.com/{tenantId}"))
                        .Build();
                });

            // Acquiring a credential can allocate a disposable — a certificate materialised from Key
            // Vault holds an ephemeral private key. When the factory ran, MSAL owns it for the client's
            // lifetime and disposing it here would break signing. When the factory did NOT run (a
            // concurrent caller won, or re-evaluation found the client already cached), nothing owns it
            // and it must be released. Re-evaluation is time-driven, so without this the certificate
            // path would leak one key handle every suppression window.
            if (!built)
            {
                descriptor.Dispose();
            }

            _selection[selectionKey] = new Selection(kind, earliestRetry);

            if (skipped.Count > 0)
            {
                _logger.LogWarning(
                    "Confidential client for {ClientId} built with FALLBACK credential {Kind} after "
                    + "skipping: {Skipped}. Re-evaluating from the top of the order at {RetryAt:O}.",
                    clientId, kind, string.Join("; ", skipped), earliestRetry);
            }
            else
            {
                _logger.LogInformation(
                    "Confidential client for {ClientId} built with credential {Kind}.", clientId, kind);
            }

            return client;
        }

        // Fail closed. An OBO path with no credential cannot authenticate anyone, so degrading quietly
        // here would produce an application that appears healthy and authorises nobody (NFR-03).
        throw new InvalidOperationException(
            $"No confidential credential could be obtained for client {clientId} in tenant {tenantId}. "
            + $"Configured order: {string.Join(" > ", _order)}. Attempts: {string.Join("; ", skipped)}. "
            + $"Set {CredentialSelectionOptions.SectionName}:Order to a credential this environment can "
            + "actually provide.");
    }

    /// <summary>
    /// Acquires the credential material for <paramref name="kind"/>, or returns <c>null</c> when the
    /// credential is simply not configured here.
    ///
    /// <para><b>"Not configured" and "configured but broken" are different answers</b> and are
    /// deliberately signalled differently: <c>null</c> falls through, an exception does not (unless the
    /// fall-through predicate says otherwise). Collapsing them would reintroduce FR-B4 through the back
    /// door — a wrong certificate name or an absent Key Vault would read as "this environment does not
    /// use certificates" and quietly select the secret.</para>
    /// </summary>
    private async Task<CredentialDescriptor?> AcquireAsync(CredentialKind kind, CancellationToken ct)
    {
        switch (kind)
        {
            case CredentialKind.ManagedIdentityFederated:
            {
                if (_assertionProvider is null)
                {
                    return null;
                }

                // Mint once, here. This is not the availability probe task 021's constraints forbid:
                // ManagedIdentityClientAssertion caches the signed assertion until expiry, so the
                // callback below returns this very assertion rather than issuing a second request.
                // A credential is only bound once it has been shown to exist.
                await _assertionProvider.GetAssertionAsync(ct).ConfigureAwait(false);

                var identity = ManagedIdentityCredentialFactory.ResolveUamiClientId(_configuration)
                    ?? "system-assigned";

                return new CredentialDescriptor(
                    identity,
                    builder => builder.WithClientAssertion(
                        options => _assertionProvider.GetAssertionAsync(options.CancellationToken)));
            }

            case CredentialKind.KeyVaultCertificate:
            {
                var certificateName = _options.KeyVaultCertificateName;
                if (string.IsNullOrWhiteSpace(certificateName))
                {
                    return null;
                }

                if (_secretClient is null)
                {
                    // The operator listed this credential AND named a certificate, so an absent Key
                    // Vault client is a deployment-shape error, not an environment without certificates.
                    // Falling through here would be the silent downgrade in a different costume.
                    throw new InvalidOperationException(
                        $"{CredentialSelectionOptions.SectionName}:Order includes "
                        + $"{CredentialKind.KeyVaultCertificate} and names certificate "
                        + $"'{certificateName}', but no Key Vault SecretClient is registered. Configure "
                        + "SpeAdmin:KeyVaultUri (or KeyVaultUri) so the certificate can be loaded.");
                }

                var certificate = await KeyVaultCertificateLoader
                    .LoadAsync(_secretClient, certificateName, ct)
                    .ConfigureAwait(false);

                // Fingerprinted on the certificate NAME, not its thumbprint, because the key has to be
                // computable without a Key Vault round trip on the cache-hit path. Consequence, stated
                // rather than discovered: rotating a certificate under the same name needs a process
                // restart to take effect. That is exactly how CiamGraphClientFactory already behaves —
                // it caches its built client for the process lifetime — so this is parity with the
                // certificate exemplar, not a new limitation.
                return new CredentialDescriptor(
                    $"cert:{certificateName}",
                    builder => builder.WithCertificate(certificate),
                    certificate);
            }

            case CredentialKind.ClientSecret:
            {
                var secret = ResolveClientSecret();
                if (string.IsNullOrWhiteSpace(secret))
                {
                    return null;
                }

                // Fingerprint, never the secret itself: a raw secret in a dictionary key widens its
                // memory-dump surface and leaks through any future key-listing diagnostic (task 011 W-1).
                var fingerprint = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(secret)))[..16];

                return new CredentialDescriptor(fingerprint, builder => builder.WithClientSecret(secret));
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Resolves the transitional client secret, canonical key first.
    ///
    /// <para>The four call sites task 022 migrates read <i>different</i> keys today —
    /// <c>AzureAd:ClientSecret</c>, <c>API_CLIENT_SECRET</c>, <c>AZURE_CLIENT_SECRET</c> — for what is
    /// the same app registration. Centralising the lookup here is part of the point: it removes
    /// per-call-site credential knowledge, which is what let the estate drift in the first place.</para>
    ///
    /// <para><b>Deliberately excluded: <c>AgentToken:ClientSecret</c>.</b> It is options-bound rather
    /// than raw configuration and nominally describes the same app registration, but silently folding it
    /// into this precedence could change which secret <c>AgentTokenService</c> presents. Reconciling it
    /// is booked onto task 022, where that call site is migrated and the change is observable.</para>
    /// </summary>
    private string? ResolveClientSecret()
        => FirstNonBlank(
            _configuration["AzureAd:ClientSecret"],
            _configuration["API_CLIENT_SECRET"],
            _configuration["AZURE_CLIENT_SECRET"]);

    /// <summary>
    /// Cheap, configuration-only description of a credential — enough to compute a cache key without
    /// acquiring the credential. Returns <c>null</c> when the kind is not configured.
    /// </summary>
    private CredentialDescriptor? DescribeCredential(CredentialKind kind) => kind switch
    {
        CredentialKind.ManagedIdentityFederated when _assertionProvider is not null =>
            new CredentialDescriptor(
                ManagedIdentityCredentialFactory.ResolveUamiClientId(_configuration) ?? "system-assigned",
                static builder => builder),

        CredentialKind.KeyVaultCertificate when !string.IsNullOrWhiteSpace(_options.KeyVaultCertificateName) =>
            new CredentialDescriptor($"cert:{_options.KeyVaultCertificateName}", static builder => builder),

        CredentialKind.ClientSecret when ResolveClientSecret() is { Length: > 0 } secret =>
            new CredentialDescriptor(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))[..16],
                static builder => builder),

        _ => null,
    };

    /// <summary>
    /// Records a failure and returns the instant this credential becomes worth retrying.
    ///
    /// <para><b>A single failure must not demote.</b> Suppression waits for
    /// <c>FailuresBeforeSuppression</c> consecutive failures, because one failure inside Entra's
    /// post-change flap window (~2 minutes of interleaved successes and failures, measured at task 030)
    /// is not evidence that a credential is broken. Demoting on it would hold the process on the
    /// fallback secret after MI-FIC had already recovered.</para>
    /// </summary>
    private DateTimeOffset RecordFailure(string failureKey, DateTimeOffset now)
    {
        var updated = _failures.AddOrUpdate(
            failureKey,
            _ => Suppress(1, now),
            (_, existing) => Suppress(existing.ConsecutiveCount + 1, now));

        // Below the threshold the credential is retried immediately on the next call, so the caller
        // should re-evaluate the order at once rather than settling on the fallback.
        return updated.SuppressedUntil > now ? updated.SuppressedUntil : now;
    }

    private Failure Suppress(int consecutiveCount, DateTimeOffset now)
        => new(
            consecutiveCount,
            consecutiveCount >= _options.FailuresBeforeSuppression
                ? now.AddSeconds(_options.NegativeCacheSeconds)
                : now);

    /// <summary>
    /// A Key Vault failure that says "not right now" rather than "not configured correctly".
    /// 403 and 404 mean the vault answered and the named certificate is missing or inaccessible — a
    /// misconfiguration that must fail loud for the same reason <c>managed_identity_request_failed</c>
    /// does. Throttling, timeouts and 5xx are genuinely transient and fall through.
    /// </summary>
    private static bool IsTransientKeyVaultFailure(RequestFailedException exception)
        => exception.Status is 0 or 408 or 429 or >= 500;

    private static string SelectionKey(string tenantId, string clientId) => $"{tenantId}|{clientId}";

    private static string FailureKey(string tenantId, string clientId, CredentialKind kind)
        => $"{tenantId}|{clientId}|{kind}";

    private static string ClientCacheKey(string tenantId, string clientId, CredentialKind kind, string fingerprint)
        => $"{tenantId}|{clientId}|{kind}|{fingerprint}";

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// A credential that has been obtained: a fingerprint for the cache key, and the builder call that
    /// binds it. Keeping the binding as a delegate is what lets the three kinds share one selection
    /// loop despite <c>.WithClientAssertion</c> / <c>.WithCertificate</c> / <c>.WithClientSecret</c>
    /// having nothing in common at the type level — the same asymmetry that made a second contract
    /// necessary in the first place.
    /// </summary>
    private sealed record CredentialDescriptor(
        string Fingerprint,
        Func<ConfidentialClientApplicationBuilder, ConfidentialClientApplicationBuilder> Apply,
        IDisposable? Resource = null)
    {
        /// <summary>
        /// Releases credential material that was acquired but never handed to MSAL. Only meaningful for
        /// the certificate branch; the assertion and secret branches hold nothing disposable.
        /// </summary>
        public void Dispose() => Resource?.Dispose();
    }
}
