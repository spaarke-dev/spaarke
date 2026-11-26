using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Tokens;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// ISS-010 (#3184): the REAL <see cref="DataverseUserClient"/> (the type production registers as a transient typed
/// <c>HttpClient</c>), with only its wire replaced, so a test measures the type the request-scoped metadata store is gated on
/// (<c>CallerMetadataReadMemo.For</c>) and a request that resolves a fresh client per create, as production does.
/// </summary>
/// <remarks>
/// The OBO exchange stays offline: MSAL's user token cache is seeded, on the credential provider's client, with one Dataverse
/// access token for the caller's bearer token (the technique of <c>McpCallerPathTests</c>), and the client assertion provider
/// refuses every mint, so a cache miss fails the call instead of reaching Entra. The caller's bearer token is the one on the
/// request's <c>Authorization</c> header (<see cref="BearerToken"/> by default, which is what the commit rig's request carries).
/// </remarks>
internal sealed class RealCallerClientRig
{
    public const string BearerToken = "caller-token";
    public const string EnvironmentUrl = "https://iss010-caller.crm.dynamics.com";
    private const string TenantId = "11111111-2222-4333-8444-555555555555";
    private const string ClientId = "66666666-7777-4888-9999-aaaaaaaaaaaa";
    private const string CallerDataverseToken = "test-CALLER-dataverse-token-iss010";

    private readonly OrderedCredentialClientProvider _credentials;
    private readonly IConfiguration _configuration;
    private int _created;

    private RealCallerClientRig(OrderedCredentialClientProvider credentials, IConfiguration configuration)
    {
        _credentials = credentials;
        _configuration = configuration;
    }

    /// <summary>How many distinct client instances have been resolved (a request that runs four creates resolves four).</summary>
    public int ClientsCreated => Volatile.Read(ref _created);

    public static RealCallerClientRig Create(string bearerToken = BearerToken)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = TenantId,
            ["AzureAd:ClientId"] = ClientId,
        }).Build();
        var assertions = new GuardedAssertions();
        var credentials = new OrderedCredentialClientProvider(
            Options.Create(new CredentialSelectionOptions { Order = new List<string> { nameof(CredentialKind.ManagedIdentityFederated) } }),
            new ConfigurationBuilder().Build(),
            NullLogger<OrderedCredentialClientProvider>.Instance,
            assertions);
        var cca = credentials.GetClientAsync(TenantId, ClientId).GetAwaiter().GetResult(); // builds the client: this one mint is allowed
        assertions.RefuseFromNow();
        SeedOboAccessToken(cca, bearerToken);
        return new RealCallerClientRig(credentials, configuration);
    }

    /// <summary>A new client instance over <paramref name="wire"/> (the handler is shared and never disposed by a client).</summary>
    public DataverseUserClient NewClient(HttpMessageHandler wire)
    {
        Interlocked.Increment(ref _created);
        return new DataverseUserClient(
            new HttpClient(wire, disposeHandler: false),
            Options.Create(new DataverseOptions { EnvironmentUrl = EnvironmentUrl }),
            _configuration,
            new HttpContextAccessor(), // the ambient accessor: the same request the route core runs in
            NullLogger<DataverseUserClient>.Instance,
            _credentials);
    }

    /// <summary>A request that carries the rig's bearer token, the way the BFF's own requests do.</summary>
    public static DefaultHttpContext Request(string bearerToken = BearerToken)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer " + bearerToken;
        return http;
    }

    /// <summary>One OBO access token in MSAL's user cache for <paramref name="userAssertion"/>, so the exchange is a cache hit.</summary>
    private static void SeedOboAccessToken(IConfidentialClientApplication client, string userAssertion)
    {
        // MSAL judges the entry against the real system clock (McpCallerPathTests found fixed far-future values rejected).
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expires = (now + 3600).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var cache = new JsonObject
        {
            ["AccessToken"] = new JsonObject
            {
                ["seed"] = new JsonObject
                {
                    ["home_account_id"] = $"{Guid.Empty:D}.{TenantId}",
                    ["environment"] = "login.microsoftonline.com",
                    ["client_id"] = ClientId,
                    ["credential_type"] = "AccessToken",
                    ["realm"] = TenantId,
                    ["target"] = $"{EnvironmentUrl}/.default",
                    ["secret"] = CallerDataverseToken,
                    ["cached_at"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["expires_on"] = expires,
                    ["extended_expires_on"] = expires,
                    ["token_type"] = "Bearer",
                    ["user_assertion_hash"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(userAssertion))),
                },
            },
            ["Account"] = new JsonObject(),
            ["IdToken"] = new JsonObject(),
            ["RefreshToken"] = new JsonObject(),
            ["AppMetadata"] = new JsonObject(),
        };
        var serialized = Encoding.UTF8.GetBytes(cache.ToJsonString());
        var seeded = 0;
        client.UserTokenCache.SetBeforeAccess(args =>
        {
            if (Interlocked.Exchange(ref seeded, 1) == 0)
                args.TokenCache.DeserializeMsalV3(serialized, shouldClearExistingCache: false);
        });
    }

    /// <summary>Mints once while the credential provider builds its client, then refuses every mint, so a cache miss stays offline instead of reaching Entra.</summary>
    private sealed class GuardedAssertions : IClientAssertionProvider
    {
        private int _refuse;

        public void RefuseFromNow() => Volatile.Write(ref _refuse, 1);

        public Task<string> GetAssertionAsync(CancellationToken cancellationToken = default) =>
            Volatile.Read(ref _refuse) == 1
                ? throw new MsalClientException("test_mint_refused", "Test: no token request can leave the process.")
                : Task.FromResult("test-client-assertion-iss010");
    }
}
