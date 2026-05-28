using System.Text;
using System.Text.Json;
using Azure.Core;

namespace Sprk.Bff.Api.Infrastructure.Auth;

/// <summary>
/// DIAGNOSTIC ONLY — wraps another <see cref="TokenCredential"/> and logs selected
/// JWT claims (oid, appid, aud, iss, idtyp, scopes, expiresOn) on the first token
/// acquisition only. The raw token is NEVER logged.
///
/// Gated by <c>OpenAI:LogTokenClaims</c> config flag. Default OFF. Intended to be
/// enabled in dev only to diagnose 401s from Azure OpenAI / AI Services when MI auth
/// fails despite correct RBAC.
///
/// Remove or leave gated off once diagnosis is complete.
/// </summary>
internal sealed class LoggingTokenCredential : TokenCredential
{
    private readonly TokenCredential _inner;
    private readonly ILogger _logger;
    private int _logged;

    public LoggingTokenCredential(TokenCredential inner, ILogger logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var token = _inner.GetToken(requestContext, cancellationToken);
        LogClaimsOnce(token, requestContext);
        return token;
    }

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var token = await _inner.GetTokenAsync(requestContext, cancellationToken).ConfigureAwait(false);
        LogClaimsOnce(token, requestContext);
        return token;
    }

    private void LogClaimsOnce(AccessToken token, TokenRequestContext requestContext)
    {
        if (Interlocked.Exchange(ref _logged, 1) != 0) return;

        try
        {
            var parts = token.Token.Split('.');
            if (parts.Length < 2) return;

            var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            _logger.LogWarning(
                "DIAG OpenAI MI token claims: oid={Oid} appid={Appid} aud={Aud} iss={Iss} idtyp={Idtyp} tid={Tid} requestedScopes={Scopes} expiresOn={ExpiresOn}",
                ReadString(root, "oid"),
                ReadString(root, "appid"),
                ReadString(root, "aud"),
                ReadString(root, "iss"),
                ReadString(root, "idtyp"),
                ReadString(root, "tid"),
                requestContext.Scopes is { Length: > 0 } s ? string.Join(",", s) : "(none)",
                token.ExpiresOn.ToString("O"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DIAG OpenAI MI token claim parse failed");
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? "(null)"
            : "(missing)";

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
