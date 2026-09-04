// #781 item 4b — the reported-status contract for the Compose save-identity key probe.
//
// WHY THIS NEEDS PINNING AT ALL. The check reports DEGRADED, never Unhealthy, and is tagged so it
// lands on /healthz/catalog rather than the /healthz liveness probe. Both are deliberate, both look
// like under-reporting to a reader who does not know why, and "upgrading" either one is a plausible,
// well-meant change that would take App Service instances out of rotation over a DATA condition the
// platform is actively compensating for (the item-2 self-heal keeps existing documents saving).
// Comments alone have not historically survived that kind of edit; these tests will fail it.
//
// The status ladder is asserted against the class directly, where each rung is unambiguous. The
// liveness-isolation property is asserted at the ENDPOINT, because that is the only place it exists
// — it is a property of the tag routing in EndpointMappingExtensions, not of the class.
//
// Test seam: the production `protected virtual FetchKeyStatusAsync` (the idiom
// MembershipFieldDiscoveryService established) is overridden with a canned status. No transport is
// mocked, no ServiceClient is stood up (ADR-038 B1).
//
// KEEP path: tests/integration/contract/** — the contract here is "what state of the key produces
// what reported health, on which endpoint".

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Compose;

public sealed class ComposeIdentityKeyHealthCheckContractTests : IClassFixture<ComposeContractFixture>
{
    private readonly ComposeContractFixture _fixture;

    public ComposeIdentityKeyHealthCheckContractTests(ComposeContractFixture fixture) => _fixture = fixture;

    /// <summary>A probe with a canned key status — the production fetch seam, overridden.</summary>
    private sealed class StubbedProbe : ComposeIdentityKeyHealthCheck
    {
        private readonly KeyProbe _probe;
        private readonly Exception? _throws;

        public StubbedProbe(IServiceProvider sp, KeyProbe probe, Exception? throws = null)
            : base(sp, NullLogger<ComposeIdentityKeyHealthCheck>.Instance)
        {
            _probe = probe;
            _throws = throws;
        }

        protected override Task<KeyProbe> FetchKeyStatusAsync(IDataverseService dataverse, CancellationToken ct) =>
            _throws is not null ? Task.FromException<KeyProbe>(_throws) : Task.FromResult(_probe);
    }

    /// <summary>A provider carrying an <see cref="IDataverseService"/>, so the probe reaches the seam.</summary>
    private static IServiceProvider ProviderWithDataverse() =>
        new ServiceCollection()
            .AddSingleton(new Mock<IDataverseService>().Object)
            .BuildServiceProvider();

    private static ComposeIdentityKeyHealthCheck WithStatus(string? status) =>
        new StubbedProbe(ProviderWithDataverse(), new ComposeIdentityKeyHealthCheck.KeyProbe(status, null));

    private static Task<HealthCheckResult> Check(ComposeIdentityKeyHealthCheck sut) =>
        sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The status ladder.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckHealth_WhenTheKeyIsActive_ReportsHealthy()
    {
        var result = await Check(WithStatus("Active"));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("sprk_graphitemid_uk");
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Pending")]
    [InlineData("InProgress")]
    public async Task CheckHealth_WhenTheKeyIndexIsNotActive_ReportsDegradedAndNamesTheRepairScript(string status)
    {
        var result = await Check(WithStatus(status));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain(status);
        result.Description.Should().Contain("Repair-ComposeIdentityKey.ps1",
            "an operator reading a health probe at 3am must be told the remedy, not just the symptom");
    }

    [Fact]
    public async Task CheckHealth_WhenTheKeyIsAbsentEntirely_ReportsDegradedWithTheSolutionImportRemedy()
    {
        // Absent and Failed are different conditions with different fixes. Telling an operator to
        // dedupe rows for a key that does not exist wastes the thing they have least of.
        var result = await Check(WithStatus(null));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("NOT FOUND");
        result.Description.Should().Contain("re-import",
            "the remedy for an absent key is a solution import, not a dedupe");
        result.Description.Should().NotContain("Repair-ComposeIdentityKey.ps1");
    }

    [Fact]
    public async Task CheckHealth_WhenDataverseIsNotRegistered_ReportsHealthyRatherThanFalseAlarming()
    {
        // A test host / local run without Dataverse is not a broken key. This uses the REAL probe
        // path (no stub) with an empty provider, so it exercises the actual gate.
        var sut = new ComposeIdentityKeyHealthCheck(
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<ComposeIdentityKeyHealthCheck>.Instance);

        var result = await Check(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("skipped");
    }

    [Fact]
    public async Task CheckHealth_WhenTheMetadataReadThrows_ReportsDegradedAndNeverUnhealthy()
    {
        // A Dataverse blip is not evidence of a broken key. Asserting a fault we did not observe is
        // how a health check earns the reputation that gets it ignored.
        var sut = new StubbedProbe(
            ProviderWithDataverse(),
            new ComposeIdentityKeyHealthCheck.KeyProbe(null, null),
            new InvalidOperationException("Dataverse is unreachable"));

        var result = await Check(sut);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Status.Should().NotBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_ForEveryKnownKeyState_NeverReportsUnhealthy()
    {
        // THE guard on the deliberate design choice. Unhealthy at /healthz/catalog is the sibling
        // routing check's deploy gate; here it would assert that the platform is broken when the
        // platform is compensating. If a future change decides otherwise, it should have to delete
        // this test and say why in the diff.
        foreach (var status in new[] { "Active", "Failed", "Pending", "InProgress", null })
        {
            var result = await Check(WithStatus(status));
            result.Status.Should().NotBe(HealthStatus.Unhealthy,
                $"key status '{status ?? "absent"}' must never take instances out of rotation");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Liveness isolation — a property of the TAG ROUTING, so it can only be asserted end to end.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Healthz_WhenTheIdentityKeyIsBroken_StaysHealthySoInstancesAreNotRecycled()
    {
        // /healthz is the App Service LIVENESS probe. If a broken identity key could turn it red,
        // a data condition would recycle every instance in a loop — while the platform was still
        // serving saves correctly via the item-2 self-heal. This is the single most important
        // property of the whole check, and it lives in the "catalog" tag, not in the class.
        using var factory = _fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ComposeIdentityKeyHealthCheck>();
                services.AddSingleton<ComposeIdentityKeyHealthCheck>(sp =>
                    new StubbedProbe(sp, new ComposeIdentityKeyHealthCheck.KeyProbe("Failed", null)));
            }));

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy",
            "the identity-key check is tagged `catalog`, and /healthz filters on " +
            "!Tags.Contains(\"catalog\") — a degraded key must never reach the liveness probe");
    }
}
