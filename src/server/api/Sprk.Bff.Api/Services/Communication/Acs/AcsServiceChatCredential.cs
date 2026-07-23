using Azure;
using Azure.Communication;
using Azure.Communication.Identity;

namespace Sprk.Bff.Api.Services.Communication.Acs;

/// <summary>
/// Builds the <see cref="CommunicationTokenCredential"/> the ACS <c>ChatClient</c> authenticates with,
/// rooted in the DI-injected central credential (ADR-028 / NFR-05). The ACS Chat data plane has NO
/// app-only / managed-identity path — a <c>ChatClient</c> can ONLY be constructed from an ACS user access
/// token. So the BFF (the ACS "trusted service") mints a <c>chat</c>-scoped token for a BFF-owned service
/// ACS identity via the already-registered <see cref="CommunicationIdentityClient"/> — which task 010 built
/// from the injected central <c>TokenCredential</c>. The token is therefore transitively rooted in the
/// central credential; NO credential is <c>new</c>'d here and NO ACS connection-string key is used.
/// </summary>
/// <remarks>
/// The service ACS identity is created LAZILY on first token request and cached for the process lifetime,
/// so DI resolution + app startup make no live ACS call (no live ACS resource is provisioned yet — task
/// 012). The <see cref="CommunicationTokenRefreshOptions"/> refresher lets the singleton <c>ChatClient</c>
/// transparently renew the 1–24h chat token. Kept inside the Communication boundary (ADR-045).
/// </remarks>
internal sealed class AcsServiceChatCredential
{
    private readonly CommunicationIdentityClient _identityClient;
    private readonly ILogger<AcsServiceChatCredential> _logger;
    private readonly SemaphoreSlim _userGate = new(1, 1);
    private CommunicationUserIdentifier? _serviceUser;

    public AcsServiceChatCredential(
        CommunicationIdentityClient identityClient,
        ILogger<AcsServiceChatCredential> logger)
    {
        _identityClient = identityClient;
        _logger = logger;
    }

    /// <summary>
    /// A refresh-capable ACS credential. Constructing it makes no live call — the first ACS chat operation
    /// triggers the refresher, which lazily creates the service identity and mints a chat token.
    /// </summary>
    public CommunicationTokenCredential CreateCredential()
    {
        var options = new CommunicationTokenRefreshOptions(
            refreshProactively: true,
            tokenRefresher: ct => MintServiceTokenAsync(ct).GetAwaiter().GetResult())
        {
            AsyncTokenRefresher = async ct => await MintServiceTokenAsync(ct).ConfigureAwait(false)
        };
        return new CommunicationTokenCredential(options);
    }

    /// <summary>
    /// Mints a fresh <c>chat</c>-scoped token for the BFF service ACS identity (creating that identity once,
    /// lazily). Internal for unit-test observation of the credential-rooting + chat-scope-only behavior.
    /// </summary>
    internal async Task<string> MintServiceTokenAsync(CancellationToken ct)
    {
        var user = await EnsureServiceUserAsync(ct).ConfigureAwait(false);
        Response<Azure.Core.AccessToken> token = await _identityClient
            .GetTokenAsync(user, new[] { CommunicationTokenScope.Chat }, ct)
            .ConfigureAwait(false);
        return token.Value.Token;
    }

    private async Task<CommunicationUserIdentifier> EnsureServiceUserAsync(CancellationToken ct)
    {
        if (_serviceUser is not null) return _serviceUser;

        await _userGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_serviceUser is null)
            {
                Response<CommunicationUserIdentifier> created = await _identityClient
                    .CreateUserAsync(ct)
                    .ConfigureAwait(false);
                _serviceUser = created.Value;
                _logger.LogInformation(
                    "Created BFF service ACS identity for chat thread operations (chat-scope only).");
            }

            return _serviceUser;
        }
        finally
        {
            _userGate.Release();
        }
    }
}
