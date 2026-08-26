// -----------------------------------------------------------------------------
// BapRestEnvironmentRateProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for BapRestEnvironmentRateProbe (task 120,
// Wave G-2). Three layers:
//   1. Evaluate() boundary-case tests — the pure rate-window comparison logic
//      ported from Test-DataverseEnvCreationRate.ps1 (empty / under / at /
//      over the rate limit, plus the immediate arg-sanity short-circuit).
//   2. ParseCreationTimes() tests — pure JSON-shape parsing against the
//      documented BAP admin environments-list response (see
//      BapRestEnvironmentRateProbe.cs file header's live-verification-pending
//      note), including the defensive fallback-field-name path.
//   3. CheckAsync() end-to-end test — a real HttpClient wrapping a hand-rolled
//      fake HttpMessageHandler (NOT Mock&lt;HttpMessageHandler&gt;), matching
//      testing.md's explicit "fake HttpClient via test-double" guidance for
//      raw-HttpClient collaborators (parity with GraphRestB2BConsentVerifier's
//      established pattern elsewhere in Handlers/**).
//
// ADR-038 alignment: pure C# unit tests. No live Azure/BAP, no
// Mock&lt;HttpMessageHandler&gt;, no SDK-client wrapper mock.
// -----------------------------------------------------------------------------

using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class BapRestEnvironmentRateProbeTests
{
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    // ---------- Evaluate() boundary cases ----------

    [Fact]
    public void Evaluate_NoEnvironments_TriviallyPassesWithFullHeadroom()
    {
        var result = BapRestEnvironmentRateProbe.Evaluate(
            TenantId, minSlotsRequired: 1, rateWindowHours: 1, rateLimit: 4, Now,
            Array.Empty<DateTimeOffset>());

        result.Passed.Should().BeTrue();
        result.Diagnostic.Should().Contain("full headroom");
    }

    [Fact]
    public void Evaluate_UnderRateLimit_Passes()
    {
        // 2 envs created in the last hour, out of a 4/hr limit, needing 1 slot.
        var creationTimes = new[] { Now.AddMinutes(-10), Now.AddMinutes(-30) };

        var result = BapRestEnvironmentRateProbe.Evaluate(
            TenantId, minSlotsRequired: 1, rateWindowHours: 1, rateLimit: 4, Now, creationTimes);

        result.Passed.Should().BeTrue("2 observed, 2 slots free >= 1 required");
    }

    [Fact]
    public void Evaluate_ExactlyAtRateLimit_FailsWhenNoSlotsFree()
    {
        // 4 envs created in the last hour == the 4/hr limit -> 0 slots free, need 1.
        var creationTimes = new[]
        {
            Now.AddMinutes(-5), Now.AddMinutes(-15), Now.AddMinutes(-25), Now.AddMinutes(-35),
        };

        var result = BapRestEnvironmentRateProbe.Evaluate(
            TenantId, minSlotsRequired: 1, rateWindowHours: 1, rateLimit: 4, Now, creationTimes);

        result.Passed.Should().BeFalse("0 slots free < 1 required");
        result.Diagnostic.Should().Contain("EXHAUSTED");
    }

    [Fact]
    public void Evaluate_StaleEnvironmentsOutsideWindow_AreExcludedFromTheCount()
    {
        // 3 recent (within 1h) + 1 stale (3h old, outside the 1h window) -> only
        // the 3 recent ones count. Without correct window-exclusion this would
        // (wrongly) count 4 -> 0 slots free -> Fail; WITH correct exclusion it's
        // 3 -> 1 slot free -> Pass (need 1).
        var creationTimes = new[]
        {
            Now.AddMinutes(-5), Now.AddMinutes(-15), Now.AddMinutes(-25), Now.AddHours(-3),
        };

        var result = BapRestEnvironmentRateProbe.Evaluate(
            TenantId, minSlotsRequired: 1, rateWindowHours: 1, rateLimit: 4, Now, creationTimes);

        result.Passed.Should().BeTrue("the 3h-old environment falls outside the 1h rate window and must not count");
    }

    // Note: the MinSlotsRequired > RateLimit arg-sanity short-circuit lives in
    // CheckAsync (it exists specifically to skip the BAP HTTP call entirely —
    // see CheckAsync_MinSlotsRequiredOverride_ExceedsRateLimit_ShortCircuitsBeforeHttpCall
    // below), not in Evaluate(), which assumes the fetch already happened.

    // ---------- ParseCreationTimes() pure parsing tests ----------

    [Fact]
    public void ParseCreationTimes_DocumentedSchema_ReadsPropertiesCreatedTime()
    {
        var body = """
        {
          "value": [
            { "id": "/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments/env1", "name": "env1",
              "properties": { "displayName": "Env 1", "createdTime": "2026-08-19T10:00:00.000Z" } },
            { "id": "/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments/env2", "name": "env2",
              "properties": { "displayName": "Env 2", "createdTime": "2026-08-19T11:30:00.000Z" } }
          ],
          "nextLink": null
        }
        """;

        var times = BapRestEnvironmentRateProbe.ParseCreationTimes(body);

        times.Should().HaveCount(2);
        times.Should().Contain(DateTimeOffset.Parse("2026-08-19T10:00:00.000Z"));
    }

    [Fact]
    public void ParseCreationTimes_FallbackCasing_StillParses()
    {
        // Defensive fallback path — a hypothetical casing drift the probe must tolerate.
        var body = """
        { "value": [ { "name": "env1", "properties": { "CreatedOn": "2026-08-19T09:00:00.000Z" } } ] }
        """;

        var times = BapRestEnvironmentRateProbe.ParseCreationTimes(body);

        times.Should().ContainSingle();
    }

    [Fact]
    public void ParseCreationTimes_MissingValueArray_Throws()
    {
        var act = () => BapRestEnvironmentRateProbe.ParseCreationTimes("""{ "somethingElse": [] }""");

        act.Should().Throw<InvalidOperationException>().WithMessage("*shape may have drifted*");
    }

    [Fact]
    public void ParseCreationTimes_EmptyBody_ReturnsEmpty()
    {
        var times = BapRestEnvironmentRateProbe.ParseCreationTimes(string.Empty);

        times.Should().BeEmpty();
    }

    // ---------- CheckAsync() end-to-end via real HttpClient + fake handler ----------

    [Fact]
    public async Task CheckAsync_CallsRealBapEndpoint_AndEvaluatesResponse()
    {
        var handler = new FakeBapHttpMessageHandler(request =>
        {
            request.RequestUri!.Host.Should().Be("api.bap.microsoft.com",
                "the probe must call the REAL BAP admin endpoint, not a hard-coded stub");
            request.Headers.Authorization.Should().NotBeNull("the probe must attach a bearer token");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{ "value": [ { "name": "env1", "properties": { "createdTime": "2026-08-19T11:00:00.000Z" } } ] }""",
                    System.Text.Encoding.UTF8, "application/json"),
            };
        });
        var probe = new BapRestEnvironmentRateProbe(
            new HttpClient(handler), FakeCredentialFactory, NullLogger<BapRestEnvironmentRateProbe>.Instance, new TestTimeProvider(Now));
        var input = new PreflightProbeInput(
            "acme-corp", TenantId, new Dictionary<string, string>());

        var result = await probe.CheckAsync(input, CancellationToken.None);

        result.Passed.Should().BeTrue("1 env in the last hour leaves 3 of 4 slots free");
        result.CheckName.Should().Be(PreflightCheckNames.DataverseEnvCreationRate);
        handler.RequestedUris.Should().ContainSingle();
    }

    [Fact]
    public async Task CheckAsync_MinSlotsRequiredOverride_ExceedsRateLimit_ShortCircuitsBeforeHttpCall()
    {
        var handler = new FakeBapHttpMessageHandler(_ => throw new InvalidOperationException("must not call BAP"));
        var probe = new BapRestEnvironmentRateProbe(
            new HttpClient(handler), FakeCredentialFactory, NullLogger<BapRestEnvironmentRateProbe>.Instance, new TestTimeProvider(Now));
        var input = new PreflightProbeInput(
            "acme-corp", TenantId,
            new Dictionary<string, string> { ["minSlotsRequired"] = "10", ["rateLimit"] = "4" });

        var result = await probe.CheckAsync(input, CancellationToken.None);

        result.Passed.Should().BeFalse();
        handler.RequestedUris.Should().BeEmpty("arg-sanity check must short-circuit before any BAP call");
    }

    /// <summary>Minimal TimeProvider double — matches HandlerOutcomeApplierTests.cs's convention (avoids a new package dep).</summary>
    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>
    /// Fake credential factory — replaces the probe's default
    /// DefaultAzureCredential-chain factory so tests never touch a live
    /// credential provider (would otherwise probe env vars / MI endpoint /
    /// az CLI session and add real wall-clock latency to a "unit" test).
    /// </summary>
    private static TokenCredential FakeCredentialFactory(string tenantId) => new FakeCredential();

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-bap-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>
    /// Hand-rolled fake <see cref="HttpMessageHandler"/> — a genuine test
    /// double (NOT Moq's Mock&lt;HttpMessageHandler&gt;, which testing.md
    /// bans) wrapped by a real <see cref="HttpClient"/> so the probe's own
    /// request construction / header attachment / JSON parsing all run
    /// unmodified against a canned response.
    /// </summary>
    private sealed class FakeBapHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public FakeBapHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        public List<Uri> RequestedUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);
            return Task.FromResult(_responder(request));
        }
    }
}
