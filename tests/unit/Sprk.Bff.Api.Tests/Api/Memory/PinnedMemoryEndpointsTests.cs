using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Memory;
using Sprk.Bff.Api.Models.Memory;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Memory;

/// <summary>
/// Integration tests for the R6 Pillar 7 / Q7 Pinned Memory CRUD endpoint pair
/// (<c>/api/memory/pins</c>, R6 task 070 PART A).
/// </summary>
/// <remarks>
/// <para>
/// Test pattern mirrors <see cref="Workspace.WorkspaceStateEndpointsTests"/>:
/// in-process <see cref="WebApplicationFactory{TEntryPoint}"/>, fake auth handler
/// emitting <c>oid</c> + <c>tid</c>, mocked <see cref="IPinnedContextRepository"/>.
/// </para>
/// <para>
/// Coverage by POML acceptance criterion:
/// <list type="bullet">
///   <item>200 GET path with seed data + filter</item>
///   <item>201 POST path; 400 when title missing or pinType invalid</item>
///   <item>200 PUT path; 404 when pin not found; 403 when caller does not own</item>
///   <item>204 DELETE path; 404 when pin not found; 403 when caller does not own</item>
///   <item>Per-tenant isolation: caller from tenant A cannot see tenant B pins
///   (verified at GET level — tenantId is the partition key and the endpoint scopes by
///   the caller's tid claim, so a tenant-A caller's GetByUserAsync is invoked with
///   tenant-A only; the repository contract makes cross-tenant queries impossible)</item>
///   <item>ADR-015: telemetry counter dimensions deterministic-IDs only (no title/content leakage)</item>
/// </list>
/// </para>
/// </remarks>
public class PinnedMemoryEndpointsTests : IClassFixture<PinnedMemoryEndpointsTestFixture>
{
    private readonly PinnedMemoryEndpointsTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public PinnedMemoryEndpointsTests(PinnedMemoryEndpointsTestFixture fixture)
    {
        _fixture = fixture;
    }

    // -------------------------------------------------------------------------
    // 401 UNAUTHENTICATED
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListPins_Unauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/api/memory/pins");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListPins_MissingTidClaim_Returns401()
    {
        _fixture.RepositoryMock.Reset();
        using var client = _fixture.CreateAuthenticatedClientWithoutTenantClaim();

        var response = await client.GetAsync("/api/memory/pins");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("tid");
        _fixture.RepositoryMock.Verify(
            r => r.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -------------------------------------------------------------------------
    // 200 GET happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListPins_Authenticated_Returns200WithItems()
    {
        // Arrange
        _fixture.RepositoryMock.Reset();
        var pins = new List<PinnedContextItem>
        {
            BuildPin("pin-1", PinnedMemoryEndpointsTestFixture.TestTenantId, PinnedMemoryEndpointsTestFixture.TestUserOid, PinType.UserPreference),
            BuildPin("pin-2", PinnedMemoryEndpointsTestFixture.TestTenantId, PinnedMemoryEndpointsTestFixture.TestUserOid, PinType.SystemRule),
        };
        _fixture.RepositoryMock
            .Setup(r => r.GetByUserAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, PinnedMemoryEndpointsTestFixture.TestUserOid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pins);

        using var client = _fixture.CreateAuthenticatedTenantClient();

        // Act
        var response = await client.GetAsync("/api/memory/pins");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("count").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(2);
        doc.RootElement.GetProperty("items")[0].GetProperty("pinId").GetString().Should().Be("pin-1");
        doc.RootElement.GetProperty("items")[0].GetProperty("pinType").GetString().Should().Be("user-preference");

        _fixture.RepositoryMock.Verify(
            r => r.GetByUserAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, PinnedMemoryEndpointsTestFixture.TestUserOid, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListPins_MatterIdFilter_NarrowsMatterFactPinsOnly()
    {
        _fixture.RepositoryMock.Reset();
        var pins = new List<PinnedContextItem>
        {
            BuildPin("p-user", PinnedMemoryEndpointsTestFixture.TestTenantId, PinnedMemoryEndpointsTestFixture.TestUserOid, PinType.UserPreference),
            BuildPin("p-mf-a", PinnedMemoryEndpointsTestFixture.TestTenantId, PinnedMemoryEndpointsTestFixture.TestUserOid, PinType.MatterFact, matterId: "matter-a"),
            BuildPin("p-mf-b", PinnedMemoryEndpointsTestFixture.TestTenantId, PinnedMemoryEndpointsTestFixture.TestUserOid, PinType.MatterFact, matterId: "matter-b"),
        };
        _fixture.RepositoryMock
            .Setup(r => r.GetByUserAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, PinnedMemoryEndpointsTestFixture.TestUserOid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pins);

        using var client = _fixture.CreateAuthenticatedTenantClient();

        var response = await client.GetAsync("/api/memory/pins?matterId=matter-a");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(2, "user-preference pin always included; matter-fact narrows to matter-a");
        var pinIds = items.EnumerateArray().Select(e => e.GetProperty("pinId").GetString()).ToList();
        pinIds.Should().Contain("p-user");
        pinIds.Should().Contain("p-mf-a");
        pinIds.Should().NotContain("p-mf-b");
    }

    // -------------------------------------------------------------------------
    // 201 CREATE happy path + counter telemetry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreatePin_Authenticated_Returns201AndEmitsCounter()
    {
        _fixture.RepositoryMock.Reset();
        PinnedContextItem? captured = null;
        _fixture.RepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()))
            .Callback<PinnedContextItem, CancellationToken>((p, _) => captured = p)
            .Returns(Task.CompletedTask);

        using var client = _fixture.CreateAuthenticatedTenantClient();

        // Counter capture (ADR-015 binding verification)
        var capturedMeasurements = new List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PinnedMemoryEndpoints.MeterName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            lock (capturedMeasurements)
            {
                capturedMeasurements.Add((instrument.Name, value, tags.ToArray()));
            }
        });
        listener.Start();

        var request = new
        {
            title = "Always respond in terse style",
            content = "Never use bullets or markdown headers.",
            pinType = "user-preference",
        };

        var response = await client.PostAsJsonAsync("/api/memory/pins", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var item = doc.RootElement.GetProperty("item");
        item.GetProperty("pinId").GetString().Should().NotBeNullOrWhiteSpace();
        item.GetProperty("pinType").GetString().Should().Be("user-preference");
        item.GetProperty("title").GetString().Should().Be("Always respond in terse style");

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(PinnedMemoryEndpointsTestFixture.TestTenantId);
        captured.UserId.Should().Be(PinnedMemoryEndpointsTestFixture.TestUserOid);
        captured.PinType.Should().Be(PinType.UserPreference);

        listener.Dispose();

        // ADR-015 verification — counter emitted, tags = deterministic IDs only.
        var createMeasurement = capturedMeasurements.FirstOrDefault(m => m.Name == "memory.pin_created");
        createMeasurement.Should().NotBe(default((string, long, KeyValuePair<string, object?>[])),
            "memory.pin_created counter was not emitted");
        createMeasurement.Value.Should().Be(1);
        var tagDictionary = createMeasurement.Tags.ToDictionary(kv => kv.Key, kv => kv.Value);
        tagDictionary.Should().ContainKey("tenantId").WhoseValue.Should().Be(PinnedMemoryEndpointsTestFixture.TestTenantId);
        tagDictionary.Should().ContainKey("userId").WhoseValue.Should().Be(PinnedMemoryEndpointsTestFixture.TestUserOid);
        tagDictionary.Should().ContainKey("pinType").WhoseValue.Should().Be("user-preference");
        tagDictionary.Should().ContainKey("decision").WhoseValue.Should().Be("created");
        // ADR-015 BINDING: no key carries the title or content text.
        tagDictionary.Should().NotContainKey("title");
        tagDictionary.Should().NotContainKey("content");
        tagDictionary.Values.Where(v => v is string s).Cast<string>()
            .Should().NotContain(s => s.Contains("terse", StringComparison.OrdinalIgnoreCase) || s.Contains("bullets", StringComparison.OrdinalIgnoreCase),
                "ADR-015 binding: title/content body MUST NEVER appear in counter dimensions");
    }

    [Fact]
    public async Task CreatePin_MissingTitle_Returns400()
    {
        _fixture.RepositoryMock.Reset();
        using var client = _fixture.CreateAuthenticatedTenantClient();

        var request = new { content = "body without title", pinType = "user-preference" };

        var response = await client.PostAsJsonAsync("/api/memory/pins", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.RepositoryMock.Verify(r => r.CreateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreatePin_InvalidPinType_Returns400()
    {
        _fixture.RepositoryMock.Reset();
        using var client = _fixture.CreateAuthenticatedTenantClient();

        var request = new { title = "ok", content = "ok", pinType = "not-a-valid-type" };

        var response = await client.PostAsJsonAsync("/api/memory/pins", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.RepositoryMock.Verify(r => r.CreateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreatePin_MatterFactWithoutMatterId_Returns400()
    {
        _fixture.RepositoryMock.Reset();
        using var client = _fixture.CreateAuthenticatedTenantClient();

        var request = new { title = "Matter clause Y", content = "...", pinType = "matter-fact" };

        var response = await client.PostAsJsonAsync("/api/memory/pins", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.RepositoryMock.Verify(r => r.CreateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // 200 / 404 / 403 UPDATE
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdatePin_Authenticated_Returns200()
    {
        _fixture.RepositoryMock.Reset();
        var existing = BuildPin("pin-99", PinnedMemoryEndpointsTestFixture.TestTenantId, PinnedMemoryEndpointsTestFixture.TestUserOid, PinType.UserPreference);
        _fixture.RepositoryMock
            .Setup(r => r.GetByIdAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, "pin-99", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _fixture.RepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var client = _fixture.CreateAuthenticatedTenantClient();

        var request = new { title = "Updated title", content = "Updated content", pinType = "user-preference" };

        var response = await client.PutAsJsonAsync("/api/memory/pins/pin-99", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("item").GetProperty("title").GetString().Should().Be("Updated title");
        _fixture.RepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePin_NotFound_Returns404()
    {
        _fixture.RepositoryMock.Reset();
        _fixture.RepositoryMock
            .Setup(r => r.GetByIdAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, "pin-missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PinnedContextItem?)null);

        using var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { title = "x", content = "y", pinType = "user-preference" };

        var response = await client.PutAsJsonAsync("/api/memory/pins/pin-missing", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _fixture.RepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePin_NotOwned_Returns403()
    {
        _fixture.RepositoryMock.Reset();
        var existing = BuildPin("pin-other", PinnedMemoryEndpointsTestFixture.TestTenantId, "DIFFERENT-USER-OID", PinType.UserPreference);
        _fixture.RepositoryMock
            .Setup(r => r.GetByIdAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, "pin-other", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        using var client = _fixture.CreateAuthenticatedTenantClient();
        var request = new { title = "x", content = "y", pinType = "user-preference" };

        var response = await client.PutAsJsonAsync("/api/memory/pins/pin-other", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.RepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // 204 / 404 / 403 DELETE + counter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeletePin_Authenticated_Returns204AndEmitsCounter()
    {
        _fixture.RepositoryMock.Reset();
        var existing = BuildPin("pin-del", PinnedMemoryEndpointsTestFixture.TestTenantId, PinnedMemoryEndpointsTestFixture.TestUserOid, PinType.SystemRule);
        _fixture.RepositoryMock
            .Setup(r => r.GetByIdAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, "pin-del", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _fixture.RepositoryMock
            .Setup(r => r.DeleteAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, "pin-del", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var client = _fixture.CreateAuthenticatedTenantClient();

        var capturedMeasurements = new List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PinnedMemoryEndpoints.MeterName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            lock (capturedMeasurements)
            {
                capturedMeasurements.Add((instrument.Name, value, tags.ToArray()));
            }
        });
        listener.Start();

        var response = await client.DeleteAsync("/api/memory/pins/pin-del");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.RepositoryMock.Verify(
            r => r.DeleteAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, "pin-del", It.IsAny<CancellationToken>()),
            Times.Once);

        listener.Dispose();

        var deleteMeasurement = capturedMeasurements.FirstOrDefault(m => m.Name == "memory.pin_deleted");
        deleteMeasurement.Should().NotBe(default((string, long, KeyValuePair<string, object?>[])));
        var tagDictionary = deleteMeasurement.Tags.ToDictionary(kv => kv.Key, kv => kv.Value);
        tagDictionary["decision"].Should().Be("deleted");
        tagDictionary["pinType"].Should().Be("system-rule");
        tagDictionary["tenantId"].Should().Be(PinnedMemoryEndpointsTestFixture.TestTenantId);
        // ADR-015: no title/content keys present.
        tagDictionary.Should().NotContainKey("title");
        tagDictionary.Should().NotContainKey("content");
    }

    [Fact]
    public async Task DeletePin_NotFound_Returns404()
    {
        _fixture.RepositoryMock.Reset();
        _fixture.RepositoryMock
            .Setup(r => r.GetByIdAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, "pin-missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PinnedContextItem?)null);

        using var client = _fixture.CreateAuthenticatedTenantClient();

        var response = await client.DeleteAsync("/api/memory/pins/pin-missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _fixture.RepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeletePin_NotOwned_Returns403()
    {
        _fixture.RepositoryMock.Reset();
        var existing = BuildPin("pin-stranger", PinnedMemoryEndpointsTestFixture.TestTenantId, "OTHER-USER", PinType.UserPreference);
        _fixture.RepositoryMock
            .Setup(r => r.GetByIdAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, "pin-stranger", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        using var client = _fixture.CreateAuthenticatedTenantClient();

        var response = await client.DeleteAsync("/api/memory/pins/pin-stranger");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.RepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -------------------------------------------------------------------------
    // Per-tenant isolation (NFR-16)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListPins_ScopesByCallerTidClaim_NeverAcceptsTenantQuery()
    {
        // Verify that even if a query parameter "tenantId" is supplied, the endpoint
        // ignores it and uses the caller's tid claim ONLY.
        _fixture.RepositoryMock.Reset();
        _fixture.RepositoryMock
            .Setup(r => r.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        using var client = _fixture.CreateAuthenticatedTenantClient();

        var response = await client.GetAsync("/api/memory/pins?tenantId=ATTEMPTED-OTHER-TENANT");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Repository was called with the CALLER's tid, never "ATTEMPTED-OTHER-TENANT".
        _fixture.RepositoryMock.Verify(
            r => r.GetByUserAsync(PinnedMemoryEndpointsTestFixture.TestTenantId, PinnedMemoryEndpointsTestFixture.TestUserOid, It.IsAny<CancellationToken>()),
            Times.Once);
        _fixture.RepositoryMock.Verify(
            r => r.GetByUserAsync("ATTEMPTED-OTHER-TENANT", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================
    // Test helpers
    // =========================================================================

    private static PinnedContextItem BuildPin(
        string pinId,
        string tenantId,
        string userId,
        PinType pinType,
        string? matterId = null)
    {
        return new PinnedContextItem
        {
            Id = PinnedContextRepository.BuildDocumentId(tenantId, pinId),
            DocumentType = PinnedContextRepository.DocumentTypeValue,
            TenantId = tenantId,
            UserId = userId,
            PinType = pinType,
            Title = $"Title for {pinId}",
            Content = $"Content for {pinId}",
            MatterId = matterId,
            CreatedAt = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero),
            CreatedBy = userId,
        };
    }
}

// =============================================================================
// Test fixture — mirrors WorkspaceStateEndpointsTestFixture pattern
// =============================================================================

public class PinnedMemoryEndpointsTestFixture : WebApplicationFactory<Program>
{
    public Mock<IPinnedContextRepository> RepositoryMock { get; } = new(MockBehavior.Loose);

    public const string TestTenantId = "00000000-0000-0000-0000-0000000ten070";
    public const string TestUserOid = "test-user-00000000-0000-0000-0000-0000pm00070";
    public const string TestBearerToken = "pinned-memory-test-token";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["Analysis:Enabled"] = "true",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                // spaarke-redis-cache-remediation-r1 task 003 (FR-02): opt into in-memory fallback for tests.
                ["Redis:AllowInMemoryFallback"] = "true",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
                ["CosmosPersistence:Endpoint"] = "https://test-cosmos.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",
                ["ModelSelector:IntentClassification"] = "gpt-4o-mini",
                ["ModelSelector:PlanGeneration"] = "o1-mini",
                ["ModelSelector:NodeGeneration"] = "gpt-4o",
                ["ModelSelector:ClarificationGeneration"] = "gpt-4o-mini",
                ["ModelSelector:AnalysisGeneration"] = "gpt-4o",
                ["ModelSelector:ExtractionGeneration"] = "gpt-4o-mini",
                ["ModelSelector:EmbeddingGeneration"] = "text-embedding-3-large",
                ["ModelSelector:FallbackGeneration"] = "gpt-4o",
                ["PowerBi:TenantId"] = "test-powerbi-tenant-id",
                ["PowerBi:ClientId"] = "test-powerbi-client-id",
                ["PowerBi:ClientSecret"] = "test-powerbi-client-secret",
                ["PowerBi:ApiUrl"] = "https://api.powerbi.com",
                ["PowerBi:Scope"] = "https://analysis.windows.net/.default",
                ["Reporting:ModuleEnabled"] = "false",
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // spaarke-redis-cache-remediation-r1 task 003 (FR-02): switch to Development for in-memory
        // cache fallback; disable ValidateScopes to preserve pre-existing test behavior.
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            var cacheDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDistributedCache));
            if (cacheDescriptor != null)
                services.Remove(cacheDescriptor);
            services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
            services.AddSingleton<IMemoryCache, MemoryCache>(sp =>
                new MemoryCache(Options.Create(new MemoryCacheOptions())));

            services.RemoveAll<IPinnedContextRepository>();
            services.AddSingleton(RepositoryMock.Object);

            services.RemoveAll<IHostedService>();

            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);
        });
    }

    public HttpClient CreateAuthenticatedTenantClient()
        => CreateClientWithClaims(includeTid: true, includeAuth: true);

    public HttpClient CreateAuthenticatedClientWithoutTenantClaim()
        => CreateClientWithClaims(includeTid: false, includeAuth: true);

    public HttpClient CreateUnauthenticatedClient()
        => CreateClientWithClaims(includeTid: true, includeAuth: false);

    private HttpClient CreateClientWithClaims(bool includeTid, bool includeAuth)
    {
        var factory = this.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new PinnedMemoryAuthOptions(includeTid));

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = PinnedMemoryFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = PinnedMemoryFakeAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, PinnedMemoryFakeAuthHandler>(
                    PinnedMemoryFakeAuthHandler.SchemeName, _ => { });

                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = PinnedMemoryFakeAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = PinnedMemoryFakeAuthHandler.SchemeName;
                });
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        if (includeAuth)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestBearerToken);
        }

        return client;
    }
}

internal sealed class PinnedMemoryAuthOptions
{
    public bool IncludeTid { get; }
    public PinnedMemoryAuthOptions(bool includeTid) => IncludeTid = includeTid;
}

internal sealed class PinnedMemoryFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PinnedMemoryFakeAuth";

    private readonly PinnedMemoryAuthOptions _opts;

    public PinnedMemoryFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        PinnedMemoryAuthOptions opts)
        : base(options, logger, encoder)
    {
        _opts = opts;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
            return Task.FromResult(AuthenticateResult.Fail("Empty Authorization header"));

        var claims = new List<Claim>
        {
            new("oid", PinnedMemoryEndpointsTestFixture.TestUserOid),
            new(ClaimTypes.NameIdentifier, PinnedMemoryEndpointsTestFixture.TestUserOid),
            new(ClaimTypes.Name, "Pinned Memory Test User"),
            new("name", "Pinned Memory Test User"),
        };

        if (_opts.IncludeTid)
        {
            claims.Add(new Claim("tid", PinnedMemoryEndpointsTestFixture.TestTenantId));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
