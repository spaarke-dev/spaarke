// -----------------------------------------------------------------------------
// AiSearchTenantFilterInvariantProbeTests.cs
//
// Unit tests for the H13 I2 real invariant probe
// (<see cref="AiSearchTenantFilterInvariantProbe"/>, task 173 Wave G-7
// Batch G-7A1). Replaces the InfraFault outcome the retired
// <c>PlaceholderInvariantVerifier</c> unconditionally returned for I2 with
// real Passed / Failed / InfraFault verdicts backed by a live sample
// /docs/search POST against the customer's AI Search endpoint.
//
// TEST BOUNDARY (ADR-038):
//   - Never Mock&lt;HttpMessageHandler&gt;: uses a hand-rolled
//     <see cref="FakeAiSearchHttpMessageHandler"/> paired with
//     <see cref="IHttpClientFactory"/> so the probe's real HttpClient +
//     JsonDocument.Parse machinery runs unmodified against canned bytes.
//   - Never Mock&lt;T&gt; for the collaborators: hand-rolled FakeRepository
//     (parity with <see cref="H2bAiSearchIndexHandlerTests"/>'s FakeRepository),
//     hand-rolled FakeCanonicalIndexCatalog (parity with H2b's fake),
//     hand-rolled FakeTokenCredential (parity with ArmSdkTestFakes).
//
// COVERAGE (per POML acceptance criteria):
//   AC-1 replaces Placeholder: exercised implicitly (the probe class implements
//        IInvariantProbe with Kind == I2AiSearchTenantFilter — asserted).
//   AC-2 Fail outcome on real failing condition: cross-tenant leak / HTTP 400 /
//        HTTP 404 / doc-missing-tenantId / doc-tenantId-wrong-type.
//   AC-3 Pass outcome on real passing condition: happy path with all-matching
//        returned docs for both tenancy models (task 225b — one path on the
//        stamp's own endpoint); empty-result Pass-eligible.
//   (The Model 1 template-artifact tests were deleted with the template store by
//   task 225b.)
//   AC-4 dotnet build / test green: covered by CI.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class AiSearchTenantFilterInvariantProbeTests
{
    private const string CustomerId = "acme";
    private const string RunId = "run-i2-probe-1";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string OtherTenantId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string DedicatedEndpoint = "https://acme-search.search.windows.net";

    // ------------------------------------------------------------------------
    // AC-1: probe metadata + IInvariantProbe contract
    // ------------------------------------------------------------------------

    [Fact]
    public void Kind_IsI2AiSearchTenantFilter()
    {
        var probe = BuildProbe();
        probe.Kind.Should().Be(InvariantKind.I2AiSearchTenantFilter);
    }

    // ------------------------------------------------------------------------
    // AC-3: Pass outcome on real passing condition
    // ------------------------------------------------------------------------

    [Theory]
    [InlineData("Model1")]
    [InlineData("Model2")]
    public async Task ProbeAsync_EndpointFromRequest_MatchingDocs_ReturnsPassed(string tenancyModel)
    {
        // Task 225b (D-12): both tenancy models are dedicated stamps — the probe queries the
        // stamp's own AI Search endpoint (H2a output) on every canonical index.
        var run = NewRun(tenancyModel: tenancyModel);
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = FakeHandler.SearchWithDocs(
            expectedFilterFragment: $"tenantId eq '{TenantId}'",
            docTenantIds: new[] { TenantId });

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>();
        handler.RequestedUrls.Should().HaveCount(catalog.CanonicalIndexNames.Length);
        handler.RequestedUrls.Should().OnlyContain(u => u.StartsWith(DedicatedEndpoint, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeAsync_EmptyResults_ReturnsPassed()
    {
        // Empty index (no data yet — brand-new customer) is Pass-eligible per the probe's honest
        // can-vs-cannot-detect posture: the filter mechanic is accepted (HTTP 200) and no foreign
        // docs are observed.
        var run = NewRun(tenancyModel: "Model1");
        var repo = new FakeRepository(run);
        var handler = FakeHandler.SearchEmpty();

        var probe = BuildProbe(repo, new FakeCanonicalIndexCatalog(), handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>();
    }

    [Fact]
    public async Task ProbeAsync_UnspecifiedTenancyModel_Rejects_WithNoDefault()
    {
        // Task 223 (D-12) REWRITE — pre-D-12 the probe mirrored H2b's silent-default
        // (blank → Model 2). D-12's parse-or-reject contract makes that mirror wrong:
        // both H2b and this probe now reject unrecognized tenancyModel at their edge,
        // and the pre-D-12 test that pinned the defaulting behaviour is retired.
        var run = NewRun(tenancyModel: string.Empty);
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = FakeHandler.SearchEmpty();

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        // Blank tenancyModel now surfaces as InfraFault via the probe's TryParse guard —
        // pre-D-12 this was InvariantVerificationOutcome.Passed under the silent default.
        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not a recognized TenancyModel")
            .And.Contain("Task 223");
    }

    [Fact]
    public async Task ProbeAsync_UnrecognizedTenancyModel_Rejects()
    {
        // Task 223 (D-12): non-blank but unknown tenancyModel value ALSO rejects — not just
        // blank. Pre-D-12 an unknown string would have hit the `else` (Model 2) branch and
        // possibly Passed on empty results, masking a real config error. Post-D-12 the parser
        // is case-sensitive + exhaustive over the enum, so 'model2dedicated' / 'Model3Future'
        // both fail here.
        var run = NewRun(tenancyModel: "model2dedicated"); // wrong case
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = FakeHandler.SearchEmpty();

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not a recognized TenancyModel");
    }

    // ------------------------------------------------------------------------
    // AC-2: Fail outcome — the CATASTROPHIC branch. This is the POML's
    // "not merely present in the template, but actually applied server-side"
    // check-of-checks.
    // ------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_ServerReturnsForeignTenantDoc_ReturnsFailedCatastrophic()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        // Server returns a doc whose tenantId does NOT match — this is the
        // exact silent-fail I2 mandates catching. The probe must FAIL, not
        // false-Pass, not InfraFault.
        var handler = FakeHandler.SearchWithDocs(
            expectedFilterFragment: null,
            docTenantIds: new[] { TenantId, OtherTenantId });

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("CATASTROPHIC")
            .And.Contain(OtherTenantId)
            .And.Contain(TenantId);
    }

    [Fact]
    public async Task ProbeAsync_ServerReturnsDocMissingTenantIdField_ReturnsFailedCatastrophic()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = new FakeAiSearchHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.OK, """{"value":[{"@search.score":1.0,"otherField":"x"}]}"""));

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("CATASTROPHIC")
            .And.Contain("NO 'tenantId'");
    }

    [Fact]
    public async Task ProbeAsync_ServerReturnsDocWithNonStringTenantId_ReturnsFailedCatastrophic()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = new FakeAiSearchHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.OK, """{"value":[{"tenantId":123}]}"""));

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("CATASTROPHIC")
            .And.Contain("not a JSON string");
    }

    // ------------------------------------------------------------------------
    // AC-2: Fail outcome — HTTP-shape signals from AI Search.
    // ------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_ServerReturns400_ReturnsFailed_FilterFieldNotFilterable()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = new FakeAiSearchHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.BadRequest, """{"error":{"code":"InvalidRequestParameter","message":"Unknown field 'tenantId' in $filter"}}"""));

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("HTTP 400")
            .And.Contain("filterable");
    }

    [Fact]
    public async Task ProbeAsync_ServerReturns404_ReturnsFailed_IndexMissing()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = new FakeAiSearchHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.NotFound, """{"error":{"code":"NotFound","message":"index not found"}}"""));

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
            .Which.Diagnostic.Should().Contain("NOT FOUND");
    }

    // ------------------------------------------------------------------------
    // AC-2: InfraFault — the "no verdict" branch.
    // ------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_ServerReturns401_ReturnsInfraFault_RbacIssue()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = new FakeAiSearchHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") });

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("Search Index Data Reader");
    }

    [Fact]
    public async Task ProbeAsync_ServerReturns500_ReturnsInfraFault()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = new FakeAiSearchHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.InternalServerError, "boom"));

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("HTTP 500");
    }

    [Fact]
    public async Task ProbeAsync_HttpTransportException_ReturnsInfraFault()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = new FakeAiSearchHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("HttpRequestException");
    }

    [Fact]
    public async Task ProbeAsync_ResponseIsMalformedJson_ReturnsInfraFault()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = new FakeAiSearchHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.OK, "{this-is-not-valid-json"));

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not parseable JSON");
    }

    [Fact]
    public async Task ProbeAsync_ResponseMissingValueArray_ReturnsInfraFault()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = new FakeAiSearchHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.OK, """{"unexpected":"shape"}"""));

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("missing 'value'");
    }

    // ------------------------------------------------------------------------
    // Precondition guards (defense-in-depth — parent H13 handler also guards).
    // ------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_RunNotFoundInPartition_ReturnsInfraFault()
    {
        var repo = new FakeRepository(existing: null);
        var probe = BuildProbe(repo, new FakeCanonicalIndexCatalog(), FakeHandler.Unused());

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not found");
    }

    [Fact]
    public async Task ProbeAsync_EmptyTenantId_ReturnsInfraFault_DefenseInDepth()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var probe = BuildProbe(repo, new FakeCanonicalIndexCatalog(), FakeHandler.Unused());

        var outcome = await probe.ProbeAsync(BuildRequest(tenantId: ""), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("TenantId is empty");
    }

    [Theory]
    [InlineData("Model1")]
    [InlineData("Model2")]
    public async Task ProbeAsync_MissingEndpoint_ReturnsInfraFault(string tenancyModel)
    {
        var run = NewRun(tenancyModel: tenancyModel);
        var repo = new FakeRepository(run);
        var probe = BuildProbe(repo, new FakeCanonicalIndexCatalog(), FakeHandler.Unused());

        var outcome = await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: string.Empty), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("I2 probe requires request.AiSearchEndpoint");
    }

    [Fact]
    public async Task ProbeAsync_UnknownTenancyModel_ReturnsInfraFault()
    {
        // Task 223 (D-12): unknown tenancyModel still returns InfraFault, but via the
        // early-exit TryParse guard now — pre-D-12 the guard was the `else` arm at the
        // bottom of the branch. The diagnostic wording shifted from "Unknown tenancyModel"
        // (pre-D-12) to "not a recognized TenancyModel" (post-D-12) to align with the
        // TenancyModelParser.FormatExpectedValues output every other Task 223 site emits.
        var run = NewRun(tenancyModel: "SomethingElse");
        var repo = new FakeRepository(run);
        var probe = BuildProbe(repo, new FakeCanonicalIndexCatalog(), FakeHandler.Unused());

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("not a recognized TenancyModel");
    }

    // ------------------------------------------------------------------------
    // OData escaping (defense-in-depth on non-GUID tenantIds).
    // ------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_TenantIdWithSingleQuote_EscapesBeforeBuildingFilter()
    {
        var oddTenantId = "abc'def"; // Never a real tenant id, but the escape must not silently break.
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = FakeHandler.SearchWithDocs(
            expectedFilterFragment: "tenantId eq 'abc''def'",
            docTenantIds: new[] { oddTenantId });

        var probe = BuildProbe(repo, catalog, handler);

        var outcome = await probe.ProbeAsync(
            BuildRequest(aiSearchEndpoint: DedicatedEndpoint, tenantId: oddTenantId), CancellationToken.None);

        outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>();
        handler.CapturedRequestBodies.Should().OnlyContain(body => body.Contains("tenantId eq 'abc''def'", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------
    // Cancellation
    // ------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_CancellationBeforeHttpCall_Throws()
    {
        var run = NewRun(tenancyModel: "Model2");
        var repo = new FakeRepository(run);
        var catalog = new FakeCanonicalIndexCatalog();
        var handler = FakeHandler.SearchEmpty();

        var probe = BuildProbe(repo, catalog, handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await probe.ProbeAsync(BuildRequest(aiSearchEndpoint: DedicatedEndpoint), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    private static AiSearchTenantFilterInvariantProbe BuildProbe(
        FakeRepository? repo = null,
        FakeCanonicalIndexCatalog? catalog = null,
        FakeAiSearchHttpMessageHandler? handler = null)
    {
        repo ??= new FakeRepository(NewRun("Model2"));
        catalog ??= new FakeCanonicalIndexCatalog();
        handler ??= FakeHandler.SearchEmpty();

        var options = Options.Create(new AiSearchIndexOptions
        {
            SearchApiVersion = "2024-07-01",
            RestCallTimeout = TimeSpan.FromSeconds(30),
        });

        return new AiSearchTenantFilterInvariantProbe(
            repo, catalog,
            new FakeHttpClientFactory(handler),
            new FakeTokenCredential(),
            options,
            NullLogger<AiSearchTenantFilterInvariantProbe>.Instance);
    }

    private static InvariantVerificationRequest BuildRequest(
        string tenantId = TenantId,
        string aiSearchEndpoint = "")
        => new(
            CustomerId: CustomerId,
            RunId: RunId,
            TenantId: tenantId,
            SubscriptionId: Guid.NewGuid().ToString(),
            AiSearchEndpoint: aiSearchEndpoint,
            CosmosEndpoint: string.Empty,
            BffApiUrl: "https://bff.example");

    private static ProvisioningRun NewRun(string tenancyModel)
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-" + CustomerId,
            TenancyModel = tenancyModel,
            Status = RunStatus.Running,
            CurrentPhase = "H13",
        };
        return run;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ---- Fake collaborators (all hand-rolled per ADR-038 §5 no Mock<T>) ----

    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private readonly ProvisioningRun? _existing;
        public FakeRepository(ProvisioningRun? existing) { _existing = existing; }
        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult(_existing is null
                ? null
                : new ProvisioningRunReadResult(_existing, "etag-fake"));
        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class FakeCanonicalIndexCatalog : ICanonicalIndexCatalog
    {
        public ImmutableArray<string> CanonicalIndexNames { get; } = ImmutableArray.Create(
            "spaarke-files-index",
            "spaarke-discovery-index",
            "spaarke-records-index",
            "spaarke-rag-references",
            "spaarke-insights-index",
            "spaarke-session-files",
            "spaarke-invoices-index");
        public ImmutableHashSet<string> RetiredIndexNames { get; }
            = ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase, "spaarke-playbook-embeddings");
        public bool IsRetired(string indexName) => RetiredIndexNames.Contains(indexName);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-i2-probe-token", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>
    /// Hand-rolled <see cref="HttpMessageHandler"/> — records every requested
    /// URL + captured request body so tests can assert the probe issued a real
    /// POST /docs/search with the correct filter shape. Never Mock&lt;T&gt;.
    /// </summary>
    private sealed class FakeAiSearchHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestedUrls { get; } = new();
        public List<string> CapturedRequestBodies { get; } = new();

        public FakeAiSearchHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri?.ToString() ?? "");
            if (request.Content is not null)
            {
                CapturedRequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
            return _responder(request);
        }
    }

    private static class FakeHandler
    {
        public static FakeAiSearchHttpMessageHandler Unused()
            => new(_ => throw new InvalidOperationException("HttpClient must not be exercised in this test path"));

        public static FakeAiSearchHttpMessageHandler SearchEmpty()
            => new(_ => JsonResponse(HttpStatusCode.OK, """{"value":[]}"""));

        public static FakeAiSearchHttpMessageHandler SearchWithDocs(string? expectedFilterFragment, IEnumerable<string> docTenantIds)
        {
            var docs = string.Join(",", docTenantIds.Select(t =>
                $$"""{"@search.score":1.0,"tenantId":"{{t}}"}"""));
            var body = $$"""{"value":[{{docs}}]}""";
            return new FakeAiSearchHttpMessageHandler(req =>
            {
                if (expectedFilterFragment is not null && req.Content is not null)
                {
                    var bodyText = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!bodyText.Contains(expectedFilterFragment, StringComparison.Ordinal))
                    {
                        return JsonResponse(HttpStatusCode.BadRequest, $"expected filter fragment '{expectedFilterFragment}' not present in body: {bodyText}");
                    }
                }
                return JsonResponse(HttpStatusCode.OK, body);
            });
        }
    }
}
