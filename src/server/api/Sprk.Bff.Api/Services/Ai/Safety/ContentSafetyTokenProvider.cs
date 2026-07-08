using Azure.Core;
using Sprk.Bff.Api.Infrastructure.Auth;

namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Acquires and caches Entra ID bearer tokens for the Azure AI Content Safety resource
/// (scope <c>https://cognitiveservices.azure.com/.default</c>), backing the managed-identity
/// auth mode of <see cref="ContentSafetyAuthHandler"/> (AI-ARCHITECTURE assessment rec 3 /
/// ADR-028 managed-identity-first).
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>:
/// (1) <i>Existing</i> — <see cref="ManagedIdentityCredentialFactory"/> builds the credential
/// but has no token cache; SDK clients (CosmosClient, GraphClient) cache internally, while the
/// Content Safety perimeter uses a raw named <c>HttpClient</c> with no SDK to cache for it.
/// (2) <i>Extension</i> — this REUSES <see cref="ManagedIdentityCredentialFactory.Create"/>
/// for the credential cascade (UAMI clientId pinning, AzureCliCredential local dev); only the
/// cache layer is new.
/// (3) <i>Cost-of-doing-nothing</i> — without a cache, every Prompt Shield scan would call
/// <c>DefaultAzureCredential.GetTokenAsync</c> inside the 100ms hard deadline
/// (<see cref="PromptShieldService"/>), turning EVERY scan into a fail-open timeout: the
/// safety perimeter would be permanently open in MI mode.
/// </para>
/// <para>
/// Refresh semantics: the cached token is reused until 5 minutes before expiry. Acquisition
/// runs on a background task NOT linked to the caller's cancellation token, so a Prompt Shield
/// scan that times out (100ms) while the very first token is being fetched fails open ONCE
/// while the acquisition completes in the background — subsequent scans hit the cache.
/// </para>
/// <para>Lifetime: singleton (registered in <see cref="Infrastructure.DI.AiSafetyModule"/>) —
/// the cache must outlive the ~2-minute HttpClientFactory handler rotation.</para>
/// </remarks>
public sealed class ContentSafetyTokenProvider
{
    /// <summary>Entra ID scope for Azure Cognitive Services (Content Safety included).</summary>
    private static readonly string[] Scopes = ["https://cognitiveservices.azure.com/.default"];

    /// <summary>Refresh margin: treat tokens expiring within this window as stale.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly TokenCredential _credential;
    private readonly object _lock = new();

    private volatile CachedToken? _cached;
    private Task<string>? _refreshTask;

    /// <summary>
    /// DI constructor: builds the platform-standard credential cascade via
    /// <see cref="ManagedIdentityCredentialFactory"/> (DefaultAzureCredential pinned to the
    /// configured UAMI clientId; chains through AzureCliCredential etc. for local dev).
    /// </summary>
    public ContentSafetyTokenProvider(IConfiguration configuration)
        : this(ManagedIdentityCredentialFactory.Create(configuration))
    {
    }

    /// <summary>Test seam: inject a stub <see cref="TokenCredential"/> (no cloud dependency).</summary>
    public ContentSafetyTokenProvider(TokenCredential credential)
    {
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
    }

    /// <summary>
    /// Returns a valid bearer token, from cache when fresh. On cache miss, awaits a shared
    /// background acquisition with the caller's cancellation token — cancelling the wait
    /// (e.g. the Prompt Shield 100ms deadline) does NOT cancel the acquisition itself.
    /// </summary>
    /// <exception cref="Azure.Identity.CredentialUnavailableException">
    /// No credential source is available (surfaces to the caller's fail-open path).
    /// </exception>
    public async ValueTask<string> GetTokenAsync(CancellationToken ct)
    {
        var cached = _cached;
        if (cached is not null && cached.ExpiresOn > DateTimeOffset.UtcNow + RefreshMargin)
        {
            return cached.Token;
        }

        Task<string> refresh;
        lock (_lock)
        {
            // A completed task (succeeded with a now-stale token, faulted, or cancelled) must
            // not be served again — start a fresh acquisition. Checking IsCompleted here (rather
            // than clearing in a finally inside AcquireAsync) avoids caching a task that
            // completed synchronously BEFORE the assignment below could be cleared.
            if (_refreshTask is null || _refreshTask.IsCompleted)
            {
                _refreshTask = AcquireAsync();
            }
            refresh = _refreshTask;
        }

        return await refresh.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task<string> AcquireAsync()
    {
        // CancellationToken.None: acquisition is shared across callers and must survive
        // an individual caller's (100ms) deadline so the next scan finds a warm cache.
        var token = await _credential
            .GetTokenAsync(new TokenRequestContext(Scopes), CancellationToken.None)
            .ConfigureAwait(false);

        _cached = new CachedToken(token.Token, token.ExpiresOn);
        return token.Token;
    }

    /// <summary>Reference-type holder so the cached (token, expiry) pair is read/written atomically.</summary>
    private sealed record CachedToken(string Token, DateTimeOffset ExpiresOn);
}
