using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.Safety;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Safety;

/// <summary>
/// AI-ARCHITECTURE assessment rec 3 (work/safety-perimeter-hygiene) — auth selection for
/// the named "ContentSafety" HttpClient. The handler must follow the platform MI cascade
/// (ADR-028): managed-identity bearer when the flag is on OR no API key is configured;
/// Ocp-Apim-Subscription-Key otherwise (local dev). Uses a concrete capture handler +
/// stub <see cref="TokenCredential"/> — no cloud dependency, no
/// <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038).
/// </summary>
[Trait("Category", "SafetyPerimeter")]
public class ContentSafetyAuthHandlerTests
{
    private const string ApiKeyKey = "AiSafety:ContentSafety:ApiKey";
    private const string MiEnabledKey = "AiSafety:ContentSafety:ManagedIdentity:Enabled";

    // =========================================================================
    // Auth selection
    // =========================================================================

    [Fact]
    public async Task SendAsync_AttachesSubscriptionKey_WhenKeyConfiguredAndMiDisabled()
    {
        var capture = new CaptureHandler();
        var client = CreateClient(capture, apiKey: "local-dev-key", miEnabled: false,
            credential: new StubCredential("unused"));

        await client.PostAsync("contentsafety/text:shieldPrompt", new StringContent("{}"));

        capture.LastRequest!.Headers.GetValues("Ocp-Apim-Subscription-Key")
            .Should().ContainSingle().Which.Should().Be("local-dev-key");
        capture.LastRequest.Headers.Authorization.Should().BeNull(
            "key mode must not also send a bearer token");
    }

    [Fact]
    public async Task SendAsync_AttachesBearerToken_WhenManagedIdentityFlagIsTrue_EvenWithKeyConfigured()
    {
        var capture = new CaptureHandler();
        var client = CreateClient(capture, apiKey: "still-in-keyvault", miEnabled: true,
            credential: new StubCredential("mi-token-123"));

        await client.PostAsync("contentsafety/text:shieldPrompt", new StringContent("{}"));

        capture.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capture.LastRequest.Headers.Authorization.Parameter.Should().Be("mi-token-123");
        capture.LastRequest.Headers.Contains("Ocp-Apim-Subscription-Key").Should().BeFalse(
            "the MI flag wins over a still-configured key so the key can be retired after the flip");
    }

    [Fact]
    public async Task SendAsync_FallsBackToBearerToken_WhenNoApiKeyIsConfigured()
    {
        var capture = new CaptureHandler();
        var client = CreateClient(capture, apiKey: null, miEnabled: false,
            credential: new StubCredential("cascade-token"));

        await client.PostAsync("contentsafety/text:shieldPrompt", new StringContent("{}"));

        capture.LastRequest!.Headers.Authorization!.Parameter.Should().Be("cascade-token",
            "absent key means DefaultAzureCredential cascade (ADR-028), not an unauthenticated call");
    }

    [Fact]
    public async Task SendAsync_Throws_WhenBearerModeHasNoCredentialSource()
    {
        // Surfaces to PromptShieldService / GroundednessCheckService fail-open catches —
        // the chat turn is never failed by an auth outage.
        var capture = new CaptureHandler();
        var client = CreateClient(capture, apiKey: null, miEnabled: false,
            credential: new UnavailableCredential());

        var act = () => client.PostAsync("contentsafety/text:shieldPrompt", new StringContent("{}"));

        await act.Should().ThrowAsync<CredentialUnavailableException>();
        capture.LastRequest.Should().BeNull("no unauthenticated request may leave the handler");
    }

    // =========================================================================
    // Token caching (the 100ms Prompt Shield deadline depends on it)
    // =========================================================================

    [Fact]
    public async Task TokenProvider_CachesTheToken_AcrossSequentialRequests()
    {
        var credential = new StubCredential("cached-token");
        var capture = new CaptureHandler();
        var client = CreateClient(capture, apiKey: null, miEnabled: true, credential);

        await client.PostAsync("contentsafety/text:shieldPrompt", new StringContent("{}"));
        await client.PostAsync("contentsafety/text:shieldPrompt", new StringContent("{}"));
        await client.PostAsync("contentsafety/text:detectGroundedness", new StringContent("{}"));

        credential.AcquisitionCount.Should().Be(1,
            "a per-scan token fetch would blow the Prompt Shield 100ms deadline — " +
            "the singleton provider must serve subsequent calls from cache");
    }

    [Fact]
    public async Task TokenProvider_RetriesAcquisition_AfterAFailedAttempt()
    {
        var credential = new FlakyCredential(failFirst: 1, thenToken: "second-try");
        var provider = new ContentSafetyTokenProvider(credential);

        var first = () => provider.GetTokenAsync(CancellationToken.None).AsTask();
        await first.Should().ThrowAsync<CredentialUnavailableException>();

        var token = await provider.GetTokenAsync(CancellationToken.None);
        token.Should().Be("second-try",
            "a transient acquisition failure must not poison the cache permanently");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static HttpClient CreateClient(
        CaptureHandler capture,
        string? apiKey,
        bool miEnabled,
        TokenCredential credential)
    {
        var configData = new Dictionary<string, string?>
        {
            [MiEnabledKey] = miEnabled ? "true" : "false",
        };
        if (apiKey is not null)
        {
            configData[ApiKeyKey] = apiKey;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var handler = new ContentSafetyAuthHandler(
            configuration,
            new ContentSafetyTokenProvider(credential),
            NullLogger<ContentSafetyAuthHandler>.Instance)
        {
            InnerHandler = capture,
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test-content-safety.cognitiveservices.azure.com/"),
        };
    }

    /// <summary>Terminal handler that records the outgoing request and returns 200.</summary>
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            });
        }
    }

    private sealed class StubCredential : TokenCredential
    {
        private readonly string _token;
        private int _acquisitionCount;

        public StubCredential(string token) => _token = token;

        public int AcquisitionCount => _acquisitionCount;

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _acquisitionCount);
            return new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class UnavailableCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new CredentialUnavailableException("No credential available (test stub).");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new CredentialUnavailableException("No credential available (test stub).");
    }

    private sealed class FlakyCredential : TokenCredential
    {
        private int _remainingFailures;
        private readonly string _token;

        public FlakyCredential(int failFirst, string thenToken)
        {
            _remainingFailures = failFirst;
            _token = thenToken;
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _remainingFailures) >= 0)
            {
                throw new CredentialUnavailableException("Transient failure (test stub).");
            }
            return new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
