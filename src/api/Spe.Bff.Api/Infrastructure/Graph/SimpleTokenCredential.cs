using Azure.Core;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Simple TokenCredential implementation that returns a pre-acquired access token.
/// Used for OBO scenarios where we already have the token from MSAL.
/// </summary>
internal sealed class SimpleTokenCredential : TokenCredential
{
    private readonly string _accessToken;

    public SimpleTokenCredential(string accessToken)
    {
        _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        // Return the pre-acquired token with a reasonable expiry
        return new AccessToken(_accessToken, DateTimeOffset.UtcNow.AddMinutes(55));
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        // Return the pre-acquired token with a reasonable expiry
        var token = new AccessToken(_accessToken, DateTimeOffset.UtcNow.AddMinutes(55));
        return new ValueTask<AccessToken>(token);
    }
}