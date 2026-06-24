using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests;

/// <summary>
/// Integration tests for the deprecation telemetry on
/// <c>GET /api/ai/playbooks/by-name/{name}</c> (task 024 / FR-03).
/// </summary>
/// <remarks>
/// <para>
/// Verifies that every call to the deprecated endpoint emits exactly one warning-level log
/// entry + Activity tags (<c>deprecated.endpoint</c>, <c>deprecated.name</c>) so the
/// stabilization-window owner can dashboard call-rate decay (KQL queries in
/// <c>projects/spaarke-ai-platform-chat-routing-redesign-r1/notes/handoffs/024-deprecation-dashboard.md</c>).
/// </para>
/// <para>
/// ADR-015 tier-1 audit (in-test): asserts the log payload contains ONLY the deterministic
/// identifier (the playbook name parameter — a stable identifier, NOT user content), the
/// tenant id from <c>tid</c>, the endpoint marker, and the User-Agent string. No user message
/// text, no document content, no memory facts.
/// </para>
/// </remarks>
public class PlaybookByNameDeprecationTests : IClassFixture<PlaybookByNameDeprecationTestFixture>
{
    private readonly PlaybookByNameDeprecationTestFixture _fixture;

    public PlaybookByNameDeprecationTests(PlaybookByNameDeprecationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task ByName_Returns200_BehaviorUnchanged_OnSuccessfulLookup()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync(
            $"/api/ai/playbooks/by-name/{Uri.EscapeDataString(PlaybookByNameDeprecationTestConstants.KnownGoodName)}");

        // Assert — endpoint still functional (telemetry is additive, behavior unchanged).
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "deprecation telemetry must NOT change endpoint behavior (FR-03 task 024)");
    }

    [Fact]
    public async Task ByName_EmitsExactlyOneWarning_PerCall_WithTier1SafePayload()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient(
            tenantId: PlaybookByNameDeprecationTestConstants.TenantA,
            userAgent: PlaybookByNameDeprecationTestConstants.TestUserAgent);

        // Act — single call.
        var response = await client.GetAsync(
            $"/api/ai/playbooks/by-name/{Uri.EscapeDataString(PlaybookByNameDeprecationTestConstants.KnownGoodName)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — exactly one warning-level entry for the deprecation marker.
        var deprecationWarnings = _fixture.LoggerProvider.Entries
            .Where(e => e.Level == LogLevel.Warning
                     && e.Category == "PlaybookEndpoints"
                     && e.Message.Contains("Deprecated endpoint /api/ai/playbooks/by-name/"))
            .ToArray();

        deprecationWarnings.Should().HaveCount(1,
            "FR-03 requires exactly one deprecation warning per call so call-rate dashboards are accurate");

        var entry = deprecationWarnings[0];

        // Tier-1 safe payload assertions: name, tenant id, user-agent present.
        entry.Message.Should().Contain(PlaybookByNameDeprecationTestConstants.KnownGoodName,
            "the playbook-name parameter is a stable identifier (tier-1 safe per ADR-015)");
        entry.Message.Should().Contain(PlaybookByNameDeprecationTestConstants.TenantA,
            "tenant id from JWT 'tid' claim must be in the payload for filtering");
        entry.Message.Should().Contain(PlaybookByNameDeprecationTestConstants.TestUserAgent,
            "User-Agent identifies the caller surface (PCF vs legacy client vs CLI)");

        // ADR-015 tier-1 negative audit: no user-content / memory-content / tokens / secrets.
        // The log format string only interpolates name, tenant id, and user-agent — no fields
        // exist on the log entry shape that could carry user content. We assert the message does
        // NOT contain known sensitive markers to guard against future drift.
        entry.Message.Should().NotContain("Bearer ",
            "JWT tokens MUST NOT appear in logs (ADR-015)");
        entry.Message.Should().NotContain("password",
            "secrets MUST NOT appear in logs (ADR-015)");
    }

    [Fact]
    public async Task ByName_SetsActivityTag_DeprecatedEndpoint_OnEveryCall()
    {
        // Arrange — start an ActivityListener so Activity.Current is populated under the
        // endpoint's ActivitySource scope. We use a permissive sampler so the tags propagate.
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        };

        var capturedTags = new List<KeyValuePair<string, object?>>();
        listener.ActivityStopped = activity =>
        {
            // Capture tags from any activity that has the deprecated marker.
            foreach (var tag in activity.TagObjects)
            {
                if (tag.Key.StartsWith("deprecated.", StringComparison.Ordinal))
                {
                    capturedTags.Add(new KeyValuePair<string, object?>(tag.Key, tag.Value));
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync(
            $"/api/ai/playbooks/by-name/{Uri.EscapeDataString(PlaybookByNameDeprecationTestConstants.KnownGoodName)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — both Activity tags set (endpoint marker + name).
        // The endpoint sets them on Activity.Current within the request scope; the test host's
        // default ActivitySource (Microsoft.AspNetCore) creates the activity that wraps the
        // request, so Activity.Current is the ASP.NET Core request activity.
        capturedTags.Should().Contain(kv =>
                kv.Key == "deprecated.endpoint"
             && string.Equals(kv.Value as string, "playbooks-by-name", StringComparison.Ordinal),
            "FR-03 requires the activity tag deprecated.endpoint = 'playbooks-by-name'");

        capturedTags.Should().Contain(kv =>
                kv.Key == "deprecated.name"
             && string.Equals(kv.Value as string, PlaybookByNameDeprecationTestConstants.KnownGoodName, StringComparison.Ordinal),
            "FR-03 requires the activity tag deprecated.name so dashboards can break out by playbook name");
    }
}
