using System.Net;
using System.Reflection;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="AnalysisPersonaService"/> resolution methods
/// (<c>ResolvePersonaForChatAsync</c> + <c>GetEffectivePersonaAsync</c>).
/// Tests the Q1 most-specific-wins precedence: playbook-attached &gt; tenant CUST- &gt; global SYS-.
/// Uses mocked HttpMessageHandler to intercept Dataverse Web API calls.
/// </summary>
/// <remarks>
/// R6 Pillar 1 (D-A-03). Covers FR-03 (resolution order), FR-04 (SYS- default fallback),
/// NFR-14 (SYS-/CUST- ownership boundary in queries).
/// </remarks>
[Trait("status", "passing")]
[Trait("task", "r6-task-003")]
public class PersonaResolutionTests : IDisposable
{
    private const string TestTenantId = "spaarke-dev-tenant";
    private const string DataverseBaseUrl = "https://test.crm.dynamics.com/api/data/v9.2/";

    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<AnalysisPersonaService>> _loggerMock;
    private readonly AnalysisPersonaService _service;

    public PersonaResolutionTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri(DataverseBaseUrl)
        };
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<AnalysisPersonaService>>();

        _configurationMock.Setup(c => c["Dataverse:ServiceUrl"]).Returns("https://test.crm.dynamics.com");

        _service = new AnalysisPersonaService(
            _httpClient,
            _configurationMock.Object,
            new DefaultAzureCredential(),
            _loggerMock.Object);

        // Bypass Azure AD authentication by setting _currentToken via reflection.
        // Mirrors ScopeResolverServiceResolveScopesTests pattern.
        var fakeToken = new AccessToken("fake-token", DateTimeOffset.UtcNow.AddHours(1));
        var field = typeof(DataverseHttpServiceBase)
            .GetField("_currentToken", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(_service, fakeToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // -----------------------------------------------------------------------------------------
    // FR-04: when no override exists, SYS- default returned — preserves identical behavior to
    // today's BuildDefaultSystemPrompt(). This is the headline acceptance criterion.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetEffectivePersonaAsync_NoOverrides_ReturnsSysDefault()
    {
        // Arrange: no playbookId; tenant query returns empty; SYS- query returns the seeded default.
        var sysPersonaId = Guid.NewGuid();
        SetupResponses(new Dictionary<string, object?>
        {
            // Tenant CUST- query (scopetype=100000001)
            ["sprk_scopetype eq 100000001"] = EmptyResult(),
            // Global SYS- query (scopetype=100000000)
            ["sprk_scopetype eq 100000000"] = SinglePersonaResult(
                id: sysPersonaId,
                name: "SYS-DEFAULT",
                systemPrompt: "You are an AI assistant that analyzes documents.",
                scopeType: 100000000)
        });

        // Act
        var result = await _service.GetEffectivePersonaAsync(TestTenantId, playbookId: null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(sysPersonaId);
        result.Name.Should().Be("SYS-DEFAULT");
        result.OwnerType.Should().Be(ScopeOwnerType.System);
        result.IsImmutable.Should().BeTrue();
        result.ScopeType.Should().Be(PersonaScopeType.Global);
        result.SystemPrompt.Should().Be("You are an AI assistant that analyzes documents.");
    }

    // -----------------------------------------------------------------------------------------
    // FR-03: tenant CUST- wins over global SYS- (most-specific-wins precedence layer 2).
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetEffectivePersonaAsync_TenantOverride_ReturnsTenantCustOverSys()
    {
        // Arrange: tenant CUST- query returns a custom persona; SYS- query never reached.
        var tenantPersonaId = Guid.NewGuid();
        SetupResponses(new Dictionary<string, object?>
        {
            ["sprk_scopetype eq 100000001"] = SinglePersonaResult(
                id: tenantPersonaId,
                name: "CUST-ACME-LEGAL",
                systemPrompt: "Custom Acme legal persona",
                scopeType: 100000001)
        });

        // Act
        var result = await _service.GetEffectivePersonaAsync(TestTenantId, playbookId: null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(tenantPersonaId);
        result.Name.Should().Be("CUST-ACME-LEGAL");
        result.OwnerType.Should().Be(ScopeOwnerType.Customer);
        result.IsImmutable.Should().BeFalse();
        result.ScopeType.Should().Be(PersonaScopeType.Tenant);
    }

    // -----------------------------------------------------------------------------------------
    // FR-03: playbook-attached wins over tenant CUST- AND global SYS- (precedence layer 1).
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetEffectivePersonaAsync_PlaybookOverride_WinsOverTenantAndSys()
    {
        // Arrange: playbook-attached query returns a persona; tenant + SYS- queries never reached.
        var playbookPersonaId = Guid.NewGuid();
        SetupResponses(new Dictionary<string, object?>
        {
            ["sprk_scopetype eq 100000002"] = SinglePersonaResult(
                id: playbookPersonaId,
                name: "CUST-PLAYBOOK-SUMMARIZE",
                systemPrompt: "Playbook-specific persona",
                scopeType: 100000002)
        });

        // Act
        var result = await _service.GetEffectivePersonaAsync(
            TestTenantId, playbookId: Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(playbookPersonaId);
        result.ScopeType.Should().Be(PersonaScopeType.PlaybookAttached);
    }

    // -----------------------------------------------------------------------------------------
    // Falls-through when playbook-attached row missing → tenant CUST- wins.
    // (Validates resolver does not break when a playbook is bound but no PlaybookAttached
    // persona row exists yet — Q1 most-specific-wins fall-through case.)
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetEffectivePersonaAsync_PlaybookBoundButMissing_FallsThroughToTenant()
    {
        // Arrange: playbook query returns empty; tenant query returns CUST-; SYS- never reached.
        var tenantPersonaId = Guid.NewGuid();
        SetupResponses(new Dictionary<string, object?>
        {
            ["sprk_scopetype eq 100000002"] = EmptyResult(),  // playbook-attached: empty
            ["sprk_scopetype eq 100000001"] = SinglePersonaResult(
                id: tenantPersonaId,
                name: "CUST-FALLBACK",
                systemPrompt: "Tenant fallback",
                scopeType: 100000001)
        });

        // Act
        var result = await _service.GetEffectivePersonaAsync(
            TestTenantId, playbookId: Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(tenantPersonaId);
        result.ScopeType.Should().Be(PersonaScopeType.Tenant);
    }

    // -----------------------------------------------------------------------------------------
    // NFR-14: SYS-/CUST- ownership boundary honored in queries. A row with sprk_scopetype=Tenant
    // but a SYS- name prefix should NOT be returned by the Tenant query (the OData filter
    // includes startswith(sprk_name, 'CUST-')). We verify the OData URL includes CUST- filter.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetEffectivePersonaAsync_TenantQuery_FiltersOnCustPrefix()
    {
        // Arrange: empty responses everywhere; we only inspect the requested URL.
        SetupResponses(new Dictionary<string, object?>());

        // Act
        await _service.GetEffectivePersonaAsync(TestTenantId, playbookId: null, CancellationToken.None);

        // Assert: the Tenant query URL must include both scopetype eq Tenant AND startswith CUST-.
        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.AtLeastOnce(),
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.RequestUri!.ToString().Contains("sprk_scopetype eq 100000001") &&
                    r.RequestUri.ToString().Contains("startswith(sprk_name, 'CUST-')")),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetEffectivePersonaAsync_SysQuery_FiltersOnSysPrefix()
    {
        // Arrange
        SetupResponses(new Dictionary<string, object?>());

        // Act
        await _service.GetEffectivePersonaAsync(TestTenantId, playbookId: null, CancellationToken.None);

        // Assert: SYS- query URL must include both scopetype eq Global AND startswith SYS-.
        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.AtLeastOnce(),
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.RequestUri!.ToString().Contains("sprk_scopetype eq 100000000") &&
                    r.RequestUri.ToString().Contains("startswith(sprk_name, 'SYS-')")),
                ItExpr.IsAny<CancellationToken>());
    }

    // -----------------------------------------------------------------------------------------
    // No playbookId means we skip the playbook-attached precedence layer entirely
    // (avoids a wasted Dataverse round-trip when no playbook is bound).
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetEffectivePersonaAsync_NoPlaybookId_SkipsPlaybookQuery()
    {
        // Arrange
        SetupResponses(new Dictionary<string, object?>());

        // Act
        await _service.GetEffectivePersonaAsync(TestTenantId, playbookId: null, CancellationToken.None);

        // Assert: NO query for scopetype eq 100000002 (PlaybookAttached) should fire.
        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.RequestUri!.ToString().Contains("sprk_scopetype eq 100000002")),
                ItExpr.IsAny<CancellationToken>());
    }

    // -----------------------------------------------------------------------------------------
    // No candidate at any layer → null. Used by callers needing explicit "no persona" handling.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetEffectivePersonaAsync_NoCandidateAtAnyLayer_ReturnsNull()
    {
        // Arrange
        SetupResponses(new Dictionary<string, object?>());

        // Act
        var result = await _service.GetEffectivePersonaAsync(TestTenantId, playbookId: null, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // -----------------------------------------------------------------------------------------
    // FR-04 binding: ResolvePersonaForChatAsync throws when no SYS- default exists.
    // This is a catastrophic seed-data failure (task 004 owns seeding).
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task ResolvePersonaForChatAsync_NoSysDefault_ThrowsInvalidOperationException()
    {
        // Arrange
        SetupResponses(new Dictionary<string, object?>());

        // Act + Assert
        var act = async () => await _service.ResolvePersonaForChatAsync(
            TestTenantId, playbookId: null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("No persona resolved"))
            .Where(ex => ex.Message.Contains(TestTenantId));
    }

    [Fact]
    public async Task ResolvePersonaForChatAsync_SysDefaultPresent_ReturnsIt()
    {
        // Arrange
        var sysPersonaId = Guid.NewGuid();
        SetupResponses(new Dictionary<string, object?>
        {
            ["sprk_scopetype eq 100000000"] = SinglePersonaResult(
                id: sysPersonaId,
                name: "SYS-DEFAULT",
                systemPrompt: "Default prompt",
                scopeType: 100000000)
        });

        // Act
        var result = await _service.ResolvePersonaForChatAsync(
            TestTenantId, playbookId: null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(sysPersonaId);
        result.SystemPrompt.Should().Be("Default prompt");
    }

    // -----------------------------------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolvePersonaForChatAsync_BlankTenantId_Throws(string tenantId)
    {
        var act = async () => await _service.ResolvePersonaForChatAsync(
            tenantId, playbookId: null, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResolvePersonaForChatAsync_NullTenantId_Throws()
    {
        var act = async () => await _service.ResolvePersonaForChatAsync(
            null!, playbookId: null, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // -----------------------------------------------------------------------------------------
    // Test infrastructure
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Maps OData filter fragments to mock JSON responses. The mock HTTP handler matches the
    /// request URI against each fragment; first match wins. Empty dictionary → all responses
    /// return empty result sets.
    /// </summary>
    private void SetupResponses(Dictionary<string, object?> urlFragmentToBody)
    {
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var uri = request.RequestUri!.ToString();
                foreach (var (fragment, body) in urlFragmentToBody)
                {
                    if (uri.Contains(fragment))
                    {
                        var bodyJson = JsonSerializer.Serialize(body ?? EmptyResult());
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json")
                        };
                    }
                }
                // Default: empty result so resolver falls through cleanly.
                var emptyJson = JsonSerializer.Serialize(EmptyResult());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(emptyJson, System.Text.Encoding.UTF8, "application/json")
                };
            });
    }

    private static object EmptyResult() => new { value = Array.Empty<object>() };

    private static object SinglePersonaResult(Guid id, string name, string systemPrompt, int scopeType) => new
    {
        value = new[]
        {
            new
            {
                sprk_aipersonaid = id,
                sprk_name = name,
                sprk_description = (string?)null,
                sprk_systemprompt = systemPrompt,
                sprk_scopetype = scopeType,
                sprk_tags = (string?)null,
                sprk_availableadhoc = false,
                _sprk_parentpersonaid_value = (Guid?)null
            }
        }
    };
}
