using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Services.Compose;
using Sprk.Bff.Api.Tests.Shared.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Integration tests for Office endpoints using WebApplicationFactory.
/// Tests the full HTTP request/response cycle including routing, filters, and serialization.
/// </summary>
[Trait("status", "repaired")]
public class OfficeEndpointsContractTests : IClassFixture<OfficeTestWebAppFactory>
{
    private readonly OfficeTestWebAppFactory _factory;
    private readonly HttpClient _client;

    public OfficeEndpointsContractTests(OfficeTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region Save Endpoint Tests

    // Un-skipped by task 016. Root cause per notes/016-fixture-diagnosis.md §1: the fixture had NO
    // working stand-in for the save pipeline's Dataverse/SPE/Service-Bus collaborators
    // (RecordContainerResolver → ISecurableEntityRegistry, SpeFileStore upload, OfficeJobQueue's
    // ServiceBusClient, and IDataverseService's CreateDocumentAsync/CreateProcessingJobAsync), all
    // now doubled in OfficeTestWebAppFactory.ConfigureTestServices. A SEPARATE, previously-unrecorded
    // defect was also found empirically (§F.3): "sprk_matter" fails ValidateSaveRequest's association
    // check, which accepts only the FRIENDLY entity names ("matter", "project", ...) — see the type
    // comment on OfficeEndpoints.ValidateSaveRequest. "matter" below is the corrected arrangement, not
    // a weakened assertion; the response-shape assertions are unchanged.
    [Fact]
    public async Task Post_OfficeSave_WithValidRequest_Returns202Accepted()
    {
        // Arrange
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            Email = new EmailMetadata
            {
                Subject = "Test Email Subject",
                SenderEmail = "sender@test.com",
                SenderName = "Test Sender"
            },
            TargetEntity = new SaveEntityReference
            {
                EntityType = "matter",
                EntityId = Guid.NewGuid()
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/office/save", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var result = await response.Content.ReadFromJsonAsync<SaveResponse>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.JobId.Should().NotBe(Guid.Empty);
        result.StatusUrl.Should().Contain("/api/office/jobs/");
        result.StreamUrl.Should().Contain("/stream");
    }

    // Un-skipped by task 016. The recorded skip reason ("test auth handler always authenticates") was
    // STALE (§F.3): TestAuthHandler below has authenticated unconditionally UNLESS the caller sends
    // "X-Test-Unauthenticated" ever since task 073 added that branch — Post_OfficeCreateTodo_WhenUnauthenticated_Returns401
    // already exercises it successfully in this same file. The original arrangement removed the
    // "Authorization" header, which TestAuthHandler never reads, so it authenticated anyway. Switching
    // to the header the handler DOES check is the fix; the 401 assertion is unchanged and now exercises
    // the real RequireAuthorization() pipeline rather than bypassing it.
    [Fact]
    public async Task Post_OfficeSave_WithoutAuth_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");

        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            Email = new EmailMetadata
            {
                Subject = "Test Email",
                SenderEmail = "sender@test.com"
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/office/save", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Job Status Endpoint Tests

    [Fact]
    public async Task Get_OfficeJobStatus_WithKnownJob_Returns200()
    {
        // Arrange
        var testJobId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        // Act
        var response = await _client.GetAsync($"/api/office/jobs/{testJobId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JobStatusResponse>();
        result.Should().NotBeNull();
        result!.JobId.Should().Be(testJobId);
        result.Status.Should().Be(JobStatus.Running);
    }

    [Fact]
    public async Task Get_OfficeJobStatus_WithUnknownJob_Returns404()
    {
        // Arrange
        var unknownJobId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/office/jobs/{unknownJobId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Entity Search Endpoint Tests

    [Fact(Skip = "Requires fully mocked Dataverse search services")]
    public async Task Get_OfficeSearchEntities_ReturnsResults()
    {
        // Act
        var response = await _client.GetAsync("/api/office/search/entities?query=Acme");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<EntitySearchResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().NotBeEmpty();
    }

    [Fact(Skip = "Requires fully mocked Dataverse search services")]
    public async Task Get_OfficeSearchEntities_WithEntityTypeFilter_FiltersResults()
    {
        // Act
        var response = await _client.GetAsync("/api/office/search/entities?query=Acme&entityTypes=Account");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<EntitySearchResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().OnlyContain(r => r.EntityType == AssociationEntityType.Account);
    }

    [Fact(Skip = "Requires fully mocked Dataverse search services")]
    public async Task Get_OfficeSearchEntities_WithPagination_RespectsSkipTop()
    {
        // Act
        var response = await _client.GetAsync("/api/office/search/entities?query=Smith&skip=0&top=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<EntitySearchResponse>();
        result.Should().NotBeNull();
        result!.Results.Count.Should().BeLessOrEqualTo(2);
    }

    #endregion

    #region Document Search Endpoint Tests

    [Fact(Skip = "Requires fully mocked Dataverse search services")]
    public async Task Get_OfficeSearchDocuments_ReturnsResults()
    {
        // Act
        var response = await _client.GetAsync("/api/office/search/documents?query=Contract");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<DocumentSearchResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().NotBeEmpty();
    }

    [Fact(Skip = "Requires fully mocked Dataverse search services")]
    public async Task Get_OfficeSearchDocuments_WithContentTypeFilter_FiltersResults()
    {
        // Act
        var response = await _client.GetAsync("/api/office/search/documents?query=Report&contentType=pdf");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<DocumentSearchResponse>();
        result.Should().NotBeNull();
    }

    #endregion

    #region Quick Create Endpoint Tests

    [Fact(Skip = "Requires fully mocked Dataverse services for quick create")]
    public async Task Post_OfficeQuickCreate_Matter_Returns201Created()
    {
        // Arrange
        var request = new QuickCreateRequest
        {
            Name = "New Test Matter",
            Description = "Created from Office add-in test"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/office/quickcreate/matter", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        result.Should().NotBeNull();
        result!.EntityType.Should().Be(QuickCreateEntityType.Matter);
        result.Name.Should().Be("New Test Matter");
        result.Id.Should().NotBe(Guid.Empty);
    }

    [Fact(Skip = "Requires fully mocked Dataverse services for quick create")]
    public async Task Post_OfficeQuickCreate_Contact_Returns201Created()
    {
        // Arrange
        var request = new QuickCreateRequest
        {
            FirstName = "Jane",
            LastName = "Doe"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/office/quickcreate/contact", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        result.Should().NotBeNull();
        result!.EntityType.Should().Be(QuickCreateEntityType.Contact);
        result.Name.Should().Be("Jane Doe");
    }

    [Fact(Skip = "Requires fully mocked Dataverse services for quick create")]
    public async Task Post_OfficeQuickCreate_InvalidEntityType_Returns400()
    {
        // Arrange
        var request = new QuickCreateRequest
        {
            Name = "Test"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/office/quickcreate/invalid", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Create To Do Endpoint Tests

    // email-communication-intelligence-r2 #3 — POST /api/office/todo creates a first-class sprk_todo
    // regarding the filed record. The name-validation + auth paths are exercised here without a real
    // Dataverse write (both return before the create); the happy path is Skip'd like the sibling
    // QuickCreate tests (needs a fully-mocked IGenericEntityService).

    [Fact]
    public async Task Post_OfficeCreateTodo_WithMissingName_Returns400()
    {
        // Arrange — a To Do with no name fails validation (OFFICE_007) BEFORE any create is attempted.
        var request = new CreateTodoRequest
        {
            RegardingEntityType = "Matter",
            RegardingRecordId = Guid.NewGuid(),
            PriorityScore = 50,
            EffortScore = 50
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/office/todo", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_OfficeCreateTodo_WhenUnauthenticated_Returns401()
    {
        // Arrange — X-Test-Unauthenticated makes the test auth handler fail the caller, so the group's
        // RequireAuthorization returns 401 before the handler runs.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");
        var request = new CreateTodoRequest { Name = "Test To Do" };

        // Act
        var response = await client.PostAsJsonAsync("/api/office/todo", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(Skip = "Requires a fully mocked IGenericEntityService for the sprk_todo create")]
    public async Task Post_OfficeCreateTodo_WithValidRequest_Returns201Created()
    {
        // Arrange
        var request = new CreateTodoRequest
        {
            Name = "Review NDA red-lines",
            Description = "From the Acme email",
            RegardingEntityType = "Matter",
            RegardingRecordId = Guid.NewGuid(),
            RegardingRecordName = "Acme Corp — NDA",
            PriorityScore = 75,
            EffortScore = 50
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/office/todo", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CreateTodoResponse>();
        result.Should().NotBeNull();
        result!.Name.Should().Be("Review NDA red-lines");
        result.TodoId.Should().NotBe(Guid.Empty);
    }

    #endregion

    #region Share Links Endpoint Tests

    [Fact]
    public async Task Post_OfficeShareLinks_ReturnsLinks()
    {
        // Arrange
        var request = new ShareLinksRequest
        {
            DocumentIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/office/share/links", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ShareLinksResponse>();
        result.Should().NotBeNull();
        result!.Links.Should().HaveCount(2);
        result.Links.Should().OnlyContain(link => link.Url.Contains("https://"));
    }

    [Fact]
    public async Task Post_OfficeShareLinks_WithGrantAccess_ProcessesInvitations()
    {
        // Arrange
        var request = new ShareLinksRequest
        {
            DocumentIds = new List<Guid> { Guid.NewGuid() },
            GrantAccess = true,
            Recipients = new List<string> { "external@partner.com" },
            Role = ShareLinkRole.ViewOnly
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/office/share/links", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ShareLinksResponse>();
        result.Should().NotBeNull();
        result!.Invitations.Should().NotBeNull();
        result.Invitations!.Count.Should().BeGreaterThan(0);
    }

    #endregion

    #region Share Attach Endpoint Tests

    [Fact]
    public async Task Post_OfficeShareAttach_ReturnsAttachments()
    {
        // Arrange
        var request = new ShareAttachRequest
        {
            DocumentIds = new[] { Guid.NewGuid(), Guid.NewGuid() },
            DeliveryMode = AttachmentDeliveryMode.Url
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/office/share/attach", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ShareAttachResponse>();
        result.Should().NotBeNull();
        result!.Attachments.Should().HaveCount(2);
        result.TotalSize.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Post_OfficeShareAttach_Base64Mode_IncludesContent()
    {
        // Arrange
        var request = new ShareAttachRequest
        {
            DocumentIds = new[] { Guid.NewGuid() },
            DeliveryMode = AttachmentDeliveryMode.Base64
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/office/share/attach", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ShareAttachResponse>();
        result.Should().NotBeNull();
        result!.Attachments.First().ContentBase64.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Recent Endpoint Tests

    [Fact]
    public async Task Get_OfficeRecent_ReturnsRecentItems()
    {
        // Act
        var response = await _client.GetAsync("/api/office/recent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RecentDocumentsResponse>();
        result.Should().NotBeNull();
        result!.RecentAssociations.Should().NotBeEmpty();
        result.RecentDocuments.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Get_OfficeRecent_WithTopParameter_LimitsResults()
    {
        // Act
        var response = await _client.GetAsync("/api/office/recent?top=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RecentDocumentsResponse>();
        result.Should().NotBeNull();
        result!.RecentAssociations.Count.Should().BeLessOrEqualTo(2);
    }

    #endregion

    #region Health Endpoint Tests

    [Fact]
    public async Task Get_OfficeHealth_Returns200()
    {
        // Act
        var response = await _client.GetAsync("/api/office/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<OfficeHealthResponse>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("healthy");
    }

    #endregion

    #region SSE Stream Endpoint Tests

    [Fact]
    public async Task Get_OfficeJobStream_ReturnsSSEContentType()
    {
        // Arrange
        var testJobId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var response = await _client.GetAsync(
            $"/api/office/jobs/{testJobId}/stream",
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
    }

    #endregion
}

/// <summary>
/// Custom WebApplicationFactory for Office endpoint tests.
/// Configures test authentication and mocked services.
/// </summary>
public class OfficeTestWebAppFactory : WebApplicationFactory<Program>
{
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
                ["Graph:ManagedIdentity:Enabled"] = "false",
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
                // spaarke-redis-cache-remediation-r1 task 003 (FR-02 fail-fast): CacheModule now
                // throws unless either Redis is enabled OR AllowInMemoryFallback is set AND env
                // is Development. Opt the test host into the in-memory fallback branch so the
                // host can build. See bff-extensions.md §F.2 (Fixture-Config-FIRST).
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
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

                // SpeAdmin options (required by SpeAdminModule — added to Program.cs)
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",

                // ManagedIdentity options (required by DataverseWebApiClient in SpeAdminModule)
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",

                // CosmosPersistence options (required by AiPersistenceModule — raw config read,
                // not bound to Options class). Mirrors CustomWebAppFactory.cs keys (task 018, Wave 1.3).
                // Repair for task 071 (Api.Office.* cluster, 10 failures, root cause same as Workspace).
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",

                // AgentService options (required by AgentServiceOptions ValidateDataAnnotations + ValidateOnStart)
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",

                // task 016 (§F.2 Fixture-Config-FIRST): the original skip reason on the save-happy-path
                // test named this key by name — "ContainerId not configured in test". OfficeService.
                // ResolveContainerAsync falls back to this when the target record is not securable (see
                // ISecurableEntityRegistry override below) and the caller supplied no explicit container.
                // "b!"-prefixed so SpeFileStore.ResolveDriveIdAsync's non-virtual short-circuit applies —
                // it returns a "b!" id unchanged without calling Graph (see the NOTE beside the SpeFileStore
                // mock below).
                ["EmailProcessing:DefaultContainerId"] = "b!test-office-save-drive",
            };
            config.AddInMemoryCollection(dict!);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // spaarke-redis-cache-remediation-r1 task 003 (FR-02): CacheModule's in-memory fallback
        // branch requires IHostEnvironment.IsDevelopment(). "Testing" environment trips Branch (c)
        // and throws at startup. Switch to "Development" so the fallback path runs.
        builder.UseEnvironment("Development");

        // Development environment defaults to ValidateScopes=true, which catches pre-existing
        // singleton→scoped DI lifetime issues in the production codebase (not introduced by this
        // PR). Original "Testing" environment defaulted ValidateScopes=false. Restore that
        // behavior explicitly so the existing tests pass unchanged.
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            // Test hosts must not authenticate for real — see TestTokenCredential.
            services.UseStubTokenCredential();

            // Add test authentication
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });

            // Use in-memory cache for testing
            services.AddDistributedMemoryCache();

            // Configure rate limiting for tests
            services.Configure<OfficeRateLimitOptions>(options =>
            {
                options.Enabled = false;
            });

            // Override Microsoft Identity Web's PostConfigure for auth scheme
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            });

            // Remove hosted services to prevent SemaphoreSlim dispose race in Release mode
            services.RemoveAll<IHostedService>();

            // Register Office rate limit service
            services.AddSingleton<IOfficeRateLimitService, OfficeRateLimitService>();

            // Mock IDataverseService to avoid real Dataverse connection. IDataverseService is the ONE
            // composite implementation DI hands out for IDocumentDataverseService, IGenericEntityService
            // AND IProcessingJobService too (GraphModule.cs registers all three as
            // sp.GetRequiredService<IDataverseService>()), so the two Setups below satisfy
            // OfficeDocumentPersistence's document create AND OfficeService's job create with one mock —
            // no separate IProcessingJobService/IDocumentDataverseService override needed (task 016 §F.2).
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            dataverseServiceMock
                .Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Guid.NewGuid().ToString());
            dataverseServiceMock
                .Setup(d => d.CreateProcessingJobAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Guid.NewGuid());
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // task 016 (§F.2): RecordContainerResolver.ResolveForRecordAsync consults this BEFORE ever
            // touching IGenericEntityService — an entity absent from the securable set short-circuits to
            // "non-secure, no own container", which is what lets the save fall through to the
            // EmailProcessing:DefaultContainerId configured above. An empty set here means every
            // TargetEntity in this test class is treated as non-secure (this file tests the SAVE
            // endpoint's HTTP contract, not record-security semantics — those have their own coverage
            // under RecordContainerResolver's and SecureContainerDecision's own unit tests).
            var securableEntitiesMock = new Mock<ISecurableEntityRegistry>();
            securableEntitiesMock
                .Setup(r => r.IsSecurableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            securableEntitiesMock
                .Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal));
            services.RemoveAll<ISecurableEntityRegistry>();
            services.AddSingleton(securableEntitiesMock.Object);

            // task 016 (§F.2): module-boundary double of the SpeFileStore facade (ADR-007, ADR-038 §4) —
            // the established idiom, matching tests/integration/data-mutation/SpeUploadPaths/
            // SpeFlatUploadPathTests.cs BuildSpeMock. Only UploadSmallAsync (virtual) is exercised by the
            // save path here.
            //
            // NOTE: ResolveDriveIdAsync is deliberately NOT set up — it is non-virtual, so Moq cannot
            // intercept it. It does not need to be: the real implementation returns its argument unchanged
            // when it already starts with "b!" (SharePoint drive ids do), short-circuiting before any
            // Graph call — see EmailProcessing:DefaultContainerId above and SpeFileStore.cs
            // ResolveDriveIdAsync.
            var graphClientFactory = Mock.Of<IGraphClientFactory>();
            var containerOps = new ContainerOperations(graphClientFactory, Mock.Of<ILogger<ContainerOperations>>());
            var driveItemOps = new DriveItemOperations(graphClientFactory, Mock.Of<ILogger<DriveItemOperations>>());
            var uploadMgr = new UploadSessionManager(
                graphClientFactory, Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>());
            var userOps = new UserOperations(graphClientFactory, Mock.Of<ILogger<UserOperations>>());
            var speFileStoreMock = new Mock<SpeFileStore>(
                MockBehavior.Loose, containerOps, driveItemOps, uploadMgr, userOps, null!);
            speFileStoreMock
                .Setup(s => s.UploadSmallAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string path, Stream _, CancellationToken _) =>
                    (FileHandleDto?)new FileHandleDto(
                        Id: $"item-{Guid.NewGuid():N}",
                        Name: path,
                        ParentId: null,
                        Size: 3,
                        CreatedDateTime: DateTimeOffset.UtcNow,
                        LastModifiedDateTime: DateTimeOffset.UtcNow,
                        ETag: null,
                        IsFolder: false,
                        WebUrl: "https://contoso.sharepoint.com/sites/test/Shared%20Documents/test.eml"));
            // Task 025: OfficeStorageUploader now always calls the EXPLICIT-conflictBehavior overload
            // (Fail by default). This fixture models no collisions at all — it exists to test the SAVE
            // endpoint's HTTP contract, not filename-collision semantics (that is OfficeVersionSaveWorld's
            // job, via OfficeVersionSaveTestWebAppFactory below) — so every conflictBehavior still succeeds.
            speFileStoreMock
                .Setup(s => s.UploadSmallAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string path, Stream _, ConflictBehavior _, CancellationToken _) =>
                    (FileHandleDto?)new FileHandleDto(
                        Id: $"item-{Guid.NewGuid():N}",
                        Name: path,
                        ParentId: null,
                        Size: 3,
                        CreatedDateTime: DateTimeOffset.UtcNow,
                        LastModifiedDateTime: DateTimeOffset.UtcNow,
                        ETag: null,
                        IsFolder: false,
                        WebUrl: "https://contoso.sharepoint.com/sites/test/Shared%20Documents/test.eml"));
            services.RemoveAll<SpeFileStore>();
            services.AddScoped(_ => speFileStoreMock.Object);

            // task 016 (§F.2): OfficeJobQueue ALWAYS queues a finalization job on a successful save (it is
            // not best-effort — an unhandled exception here was falling through to OfficeService.SaveAsync's
            // outer catch and turning the response into Success=false). ServiceBusClient/ServiceBusSender are
            // Azure-SDK client types designed to be mocked (virtual members, protected parameterless ctor) —
            // same idiom already used by MembershipEventPublisherTests. OfficeJobQueue itself has no virtual
            // members, so it is constructed for real with this fake Service Bus client rather than mocked.
            var serviceBusSenderMock = new Mock<ServiceBusSender>();
            serviceBusSenderMock
                .Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var serviceBusClientMock = new Mock<ServiceBusClient>();
            serviceBusClientMock.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(serviceBusSenderMock.Object);
            services.RemoveAll<OfficeJobQueue>();
            services.AddScoped(sp => new OfficeJobQueue(
                serviceBusClientMock.Object,
                Options.Create(new ServiceBusOptions()),
                sp.GetRequiredService<ILogger<OfficeJobQueue>>()));

            // task 016 (§F.2 / §F.3 — discovered empirically, not from the recorded skip reason): the save
            // route ALSO carries EntityAccessFilter ("entity.associate_document"), which is a SEPARATE
            // authorization decision from the ones the recorded skip reason named — it asks whether the
            // caller may APPEND a document to the TargetEntity (the matter), via CallerRecordAccessProbe,
            // which runs an OBO delegation check against Dataverse. TestAuthHandler's claims carry no real
            // bearer token, so the real probe fails closed (AccessRights.None) and the save 403s before
            // ever reaching OfficeService — this was NOT in the original skip comment and was found only by
            // running the test and reading the 403 (§F.3 empirical-reproduction-first). Reused verbatim:
            // ComposeServiceCollaborators.Probe() is CallerRecordAccessProbe's own designated test seam
            // (its type doc: "public virtual precisely so tests can substitute the authorization answer
            // without mocking its HttpClient transport" — ADR-038 §4), already built for exactly this.
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddScoped(_ => ComposeServiceCollaborators.Probe().Object);
        });
    }
}

/// <summary>
/// Test authentication handler that always authenticates requests.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Task 073 - lets tests exercise the unauthenticated path (RequireAuthorization /
        // OfficeAuthFilter). Requests that do NOT send this header authenticate as usual,
        // so existing tests are unaffected.
        if (Request.Headers.ContainsKey("X-Test-Unauthenticated"))
        {
            return Task.FromResult(AuthenticateResult.Fail("Test: unauthenticated caller"));
        }

        var claims = new[]
        {
            new Claim("oid", "test-user-oid"),
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim("tid", "test-tenant-id")
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

// =====================================================================================================
// FR-11 VERSION SAVE — spaarkeai-word-add-in-r1 task 023
// =====================================================================================================

/// <summary>
/// HTTP contract of the FR-11 version save on <c>POST /api/office/save</c>: a <c>ContentType=Document</c>
/// save carrying <c>document.existingDocumentId</c> writes a new SPE version of that document's own item and
/// creates no row; every refusal returns a distinct ProblemDetails code and writes nothing; a save without the
/// field, and every Email/Attachment save, behaves as before.
/// </summary>
/// <remarks>
/// <para><b>Fixture.</b> <see cref="OfficeVersionSaveTestWebAppFactory"/> layers ONE stateful in-memory world
/// (<see cref="OfficeVersionSaveWorld"/>) over task 016's <see cref="OfficeTestWebAppFactory"/>, at the same
/// module boundaries that fixture already doubles — <see cref="IDataverseService"/>, the <see cref="SpeFileStore"/>
/// facade (ADR-007), the <see cref="OfficeJobQueue"/>'s <see cref="ServiceBusClient"/> — plus
/// <see cref="IAccessDataSource"/>, the authorization decision's data seam (the same seam
/// <c>DocumentIdentityContractTests</c> uses). No <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038 B1). A fresh
/// factory per test, because each test seeds its own world.</para>
/// <para><b>"Wrote nothing"</b> is asserted against the world, not inferred from a status code: a 4xx alone
/// would pass even if the bytes had been written first.</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeVersionSaveContractTests
{
    // FR-02 (task 014): REAL minimal .docx bytes. A bare PK signature classifies CORRUPT once the save path
    // stamps document identity into the uploaded bytes, so every Document save here would be refused with
    // OFFICE_021. Dropping the PK prefix would also turn these green — by making the stamper pass the bytes
    // through as a non-OOXML payload — and that is the false green the migration exists to avoid.
    private static readonly byte[] InitialBytes = MinimalDocx.Create("initial");
    private static readonly byte[] RevisedBytes = MinimalDocx.Create("revised");

    // ── AC3: no existingDocumentId → a new document, exactly as today ─────────────────────────────

    [Fact]
    public async Task Post_OfficeSave_DocumentWithoutExistingDocumentId_CreatesANewDocumentAsBefore()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("New brief.docx", InitialBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.DocumentCreates.Should().Be(1, "a save without existingDocumentId still creates its own sprk_document");
        world.UploadSmallCalls.Should().Be(1, "it still uploads through the path-keyed container upload");
        world.ReplaceCalls.Should().Be(0, "no version of any existing item is written");
        world.AccessChecks.Should().BeEmpty("the version-save gate is not engaged for a non-version save");
        world.DocumentReads.Should().BeEmpty();
        world.Jobs.Should().ContainSingle().Which.Name.Should().StartWith("Document Save - ");
    }

    // ── Task 020 (FR-06): document.title -> sprk_documentname, independent of the SPE filename ─────

    [Fact]
    public async Task Post_OfficeSave_DocumentWithDifferentTitleAndFileName_WritesTitleToDocumentNameAndSanitizedFileNameToSpe()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save",
            OfficeVersionSaveWorld.NewDocumentSave(
                "Acme Merger Agreement.docx", InitialBytes, title: "Acme Merger Agreement - Execution Copy"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        // sprk_documentname is the user-facing title the pane sent...
        world.CreatedDocumentNames.Should().ContainSingle()
            .Which.Should().Be("Acme Merger Agreement - Execution Copy");
        // ...never carried into sprk_documentdescription (the other half of the inverted-mapping defect)...
        world.CreatedDocumentDescriptions.Should().ContainSingle().Which.Should().BeNull();
        // ...and the SPE blob keeps the sanitized, unedited .docx filename, independently of the title above.
        world.SpeItems.Values.Should().ContainSingle().Which.Name.Should().Be("Acme Merger Agreement.docx");
    }

    [Fact]
    public async Task Post_OfficeSave_DocumentWithNoTitle_FallsBackToTheSanitizedFileNameForDocumentName()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        // NewDocumentSave leaves Title unset — the negative case: sprk_documentname must still never be empty.
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", InitialBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.CreatedDocumentNames.Should().ContainSingle().Which.Should().Be("Brief.docx");
    }

    [Fact]
    public async Task Post_OfficeSave_DocumentWithTitleOver850Characters_IsBoundedTo850AndSaveSucceeds()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var longTitle = new string('A', 900);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save",
            OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", InitialBytes, title: longTitle));

        response.StatusCode.Should().Be(
            HttpStatusCode.Accepted, "sprk_documentname is bounded at the write boundary, never an unhandled Dataverse length error");
        world.CreatedDocumentNames.Should().ContainSingle().Which.Should().HaveLength(850)
            .And.Be(new string('A', 850));
    }

    // ── The version path, HTTP shape ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_OfficeSave_VersionSave_Returns202_AuthorizesWriteOnTheTarget_AndWritesAVersionOfItsItem()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, RevisedBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var result = await response.Content.ReadFromJsonAsync<SaveResponse>();
        result!.Success.Should().BeTrue();
        result.Duplicate.Should().BeFalse();

        // ADR-044: the id reaches the access data source bare-lowercase.
        world.AccessChecks.Should().ContainSingle().Which.Should().Be(documentId.ToString("D"));
        world.ReplaceCalls.Should().Be(1);
        world.SpeItems[itemId].Versions.Should().HaveCount(2);
        world.UploadSmallCalls.Should().Be(0, "a version is never a path-keyed upload into the derived container");
        world.DocumentCreates.Should().Be(0);
        world.Jobs.Should().ContainSingle().Which.Name.Should().StartWith("Document Version Save - ");
    }

    // ── AC5: an existingDocumentId that resolves to no row ───────────────────────────────────────

    [Fact]
    public async Task Post_OfficeSave_VersionSave_WhenTheDocumentDoesNotResolve_Returns404Office016_AndWritesNothing()
    {
        // Authorization answered "allowed" (e.g. the row was deleted between the gate and the read), so the
        // refusal is the SERVICE's own. With a real access data source an unknown id is refused one step
        // earlier, by the filter — see the 403 theory below.
        var world = new OfficeVersionSaveWorld();
        var unknownId = Guid.NewGuid();
        world.AccessByDocumentId[unknownId] = AccessRights.Read | AccessRights.Write;
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(unknownId, RevisedBytes));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await ErrorCodeOf(response)).Should().Be("OFFICE_016");
        world.ShouldHaveWrittenNothing();
    }

    // ── AC6: a caller without write on the target ────────────────────────────────────────────────

    [Theory]
    [InlineData(AccessRights.Read)] // can open the document, may not version it
    [InlineData(AccessRights.None)] // also what a real, fail-closed access source answers for an UNKNOWN id
    public async Task Post_OfficeSave_VersionSave_WhenTheCallerLacksWrite_IsRefusedByTheFilter_AndWritesNothing(
        AccessRights rights)
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes, rights);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, RevisedBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        world.AccessChecks.Should().ContainSingle("the endpoint filter decided");
        world.DocumentReads.Should().BeEmpty("the handler never ran — the refusal is the filter's (ADR-008)");
        world.SpeItems[itemId].Versions.Should().HaveCount(1, "no bytes reached SPE");
        world.ShouldHaveWrittenNothing();
    }

    // ── AC7: the target row carries no SPE pointers ──────────────────────────────────────────────

    [Fact]
    public async Task Post_OfficeSave_VersionSave_WhenTheDocumentHasNoSpePointers_Returns409Office017_AndNeverFallsBackToANewItem()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, _) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes, withPointers: false);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, RevisedBytes));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeOf(response)).Should().Be("OFFICE_017");
        world.ShouldHaveWrittenNothing();
    }

    // ── IsNewVersion is part of the contract, not decoration ─────────────────────────────────────

    [Fact]
    public async Task Post_OfficeSave_ExistingDocumentIdWithIsNewVersionFalse_Returns400Office018_AndWritesNothing()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, RevisedBytes, isNewVersion: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeOf(response)).Should().Be("OFFICE_018");
        world.SpeItems[itemId].Versions.Should().HaveCount(1);
        world.ShouldHaveWrittenNothing();
    }

    // ── SPE refuses the write: the item is locked ────────────────────────────────────────────────

    [Fact]
    public async Task Post_OfficeSave_VersionSave_WhenTheItemIsLocked_Returns423Office019_AndCreatesNoRow()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        world.LockedItemIds.Add(itemId);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, RevisedBytes));

        response.StatusCode.Should().Be((HttpStatusCode)423);
        (await ErrorCodeOf(response)).Should().Be("OFFICE_019");
        world.SpeItems[itemId].Versions.Should().HaveCount(1);
        world.DocumentCreates.Should().Be(0);
        world.UploadSmallCalls.Should().Be(0, "a refused version write never falls back to a fresh upload");
        world.DocumentUpdates.Should().BeEmpty();
    }

    // ── AC8: Email and Attachment never act on the field ─────────────────────────────────────────

    [Theory]
    [InlineData(SaveContentType.Email)]
    [InlineData(SaveContentType.Attachment)]
    public async Task Post_OfficeSave_EmailOrAttachmentCarryingExistingDocumentId_IgnoresIt_AndBehavesAsBefore(
        SaveContentType contentType)
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var request = new SaveRequest
        {
            ContentType = contentType,
            TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
            Email = contentType == SaveContentType.Email
                ? new EmailMetadata { Subject = "Filing", SenderEmail = "sender@test.com" }
                : null,
            Attachment = contentType == SaveContentType.Attachment
                ? new AttachmentMetadata
                {
                    AttachmentId = "att-1",
                    FileName = "Exhibit A.pdf",
                    ContentBase64 = Convert.ToBase64String(RevisedBytes),
                }
                : null,
            // The field the Email/Attachment branches must never act on.
            Document = new DocumentMetadata
            {
                FileName = "Brief.docx",
                IsNewVersion = true,
                ExistingDocumentId = documentId,
            },
        };

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/save", request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.DocumentCreates.Should().Be(1, "the Email/Attachment save creates its own document, as before");
        world.UploadSmallCalls.Should().Be(1);
        world.ReplaceCalls.Should().Be(0);
        world.AccessChecks.Should().BeEmpty("the version-save gate never engages for Email/Attachment");
        world.DocumentReads.Should().BeEmpty("the existing document is never resolved");
        world.SpeItems[itemId].Versions.Should().HaveCount(1, "the named document's item is untouched");
        world.DocumentUpdates.Should().NotContain(u => u.DocumentId == documentId.ToString("D"));
        world.Jobs.Should().ContainSingle().Which.Name.Should().StartWith($"{contentType} Save - ");
    }

    private static async Task<string?> ErrorCodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }
}

/// <summary>
/// One in-memory world behind the version-save fixture: <c>sprk_document</c> rows, ProcessingJob rows, an
/// SPE drive whose items keep a VERSION HISTORY, per-document caller rights, and the finalization messages sent.
/// </summary>
/// <remarks>
/// SPE semantics reproduced, because they are what the tests are about: <c>UploadSmallAsync</c> is PATH-keyed
/// (same name → a new version of that item; new name → a NEW item), and <c>ReplaceFileContentAsUserAsync</c>
/// is ITEM-keyed (a new version of that item; unknown item → <c>null</c>, the facade's 404 shape). The live
/// hash stands in for SPE's <c>quickXorHash</c>: equal bytes, equal hash.
/// </remarks>
public sealed class OfficeVersionSaveWorld
{
    public sealed class DocumentRow
    {
        public required Guid Id { get; init; }
        public string? DriveId { get; set; }
        public string? ItemId { get; set; }
        public string? FileName { get; set; }
        public long? FileSize { get; set; }
        public string? FilePath { get; set; }
        public string? CanonicalHash { get; set; }
        public Guid? CanonicalDocumentId { get; set; }
    }

    public sealed class SpeItem
    {
        public required string DriveId { get; init; }
        public required string Id { get; init; }
        public required string Name { get; init; }
        public List<byte[]> Versions { get; } = new();
        public string WebUrl => $"https://contoso.sharepoint.com/contentstorage/{DriveId}/{Uri.EscapeDataString(Name)}";
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Guid> _jobIdsByKey = new(StringComparer.Ordinal);

    public Dictionary<Guid, DocumentRow> Documents { get; } = new();
    public Dictionary<string, SpeItem> SpeItems { get; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, AccessRights> AccessByDocumentId { get; } = new();
    public HashSet<string> LockedItemIds { get; } = new(StringComparer.Ordinal);
    public List<string> AccessChecks { get; } = new();
    public List<string> DocumentReads { get; } = new();
    public List<(string DocumentId, UpdateDocumentRequest Update)> DocumentUpdates { get; } = new();
    public List<(Guid Id, Dictionary<string, object> Fields)> GenericUpdates { get; } = new();
    public List<string> DeletedItemIds { get; } = new();
    public List<(string Name, string IdempotencyKey)> Jobs { get; } = new();
    public List<JsonElement> FinalizationPayloads { get; } = new();
    public int DocumentCreates { get; private set; }
    public int UploadSmallCalls { get; private set; }
    public int ReplaceCalls { get; private set; }

    /// <summary>Stand-in for SPE's quickXorHash: equal bytes → equal hash.</summary>
    public static string Hash(byte[] bytes) => Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>Seeds an existing Spaarke document: an SPE item with ONE version, and its row.</summary>
    public (Guid DocumentId, string ItemId) SeedDocument(
        string driveId,
        string fileName,
        byte[] bytes,
        AccessRights rights = AccessRights.Read | AccessRights.Write,
        bool withPointers = true,
        Guid? linkedToCanonical = null,
        string? linkedHash = null)
    {
        var itemId = $"item-{Guid.NewGuid():N}";
        var item = new SpeItem { DriveId = driveId, Id = itemId, Name = fileName };
        item.Versions.Add(bytes);
        SpeItems[itemId] = item;

        var id = Guid.NewGuid();
        Documents[id] = new DocumentRow
        {
            Id = id,
            DriveId = withPointers ? driveId : null,
            ItemId = withPointers ? itemId : null,
            FileName = fileName,
            FileSize = bytes.Length,
            FilePath = item.WebUrl,
            CanonicalHash = linkedHash ?? Hash(bytes),
            CanonicalDocumentId = linkedToCanonical,
        };
        AccessByDocumentId[id] = rights;
        return (id, itemId);
    }

    public static SaveRequest NewDocumentSave(string fileName, byte[] bytes, string? title = null) => new()
    {
        ContentType = SaveContentType.Document,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
        Document = new DocumentMetadata { FileName = fileName, Title = title, ContentBase64 = Convert.ToBase64String(bytes) },
    };

    public static SaveRequest VersionSave(
        Guid existingDocumentId, byte[] bytes, bool isNewVersion = true, string fileName = "Brief.docx",
        string? comment = "Second draft") => new()
    {
        ContentType = SaveContentType.Document,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
        Document = new DocumentMetadata
        {
            FileName = fileName,
            ContentBase64 = Convert.ToBase64String(bytes),
            IsNewVersion = isNewVersion,
            ExistingDocumentId = existingDocumentId,
            VersionComment = comment,
        },
    };

    /// <summary>The refusal invariant: no row, no SPE item, no SPE version, no job, no finalization.</summary>
    public void ShouldHaveWrittenNothing()
    {
        DocumentCreates.Should().Be(0, "a refused version save creates no sprk_document");
        UploadSmallCalls.Should().Be(0, "a refused version save uploads no SPE item");
        ReplaceCalls.Should().Be(0, "a refused version save writes no SPE version");
        DocumentUpdates.Should().BeEmpty();
        Jobs.Should().BeEmpty("the refusal precedes the ProcessingJob");
        FinalizationPayloads.Should().BeEmpty();
    }

    // ── Dataverse ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>sprk_documentname</c> each <c>CreateDocumentAsync</c> wrote, in order (task 046 (b)). The row's
    /// <see cref="DocumentRow.FileName"/> is later overwritten by the pointer update's <c>sprk_filename</c>, so it
    /// cannot tell the two names apart.
    /// </summary>
    public List<string> CreatedDocumentNames { get; } = new();

    /// <summary>
    /// The <c>sprk_documentdescription</c> each <c>CreateDocumentAsync</c> wrote, in order (task 020). <c>null</c>
    /// entries are real — a Document save sends no description as of task 020 (FR-06/FR-07).
    /// </summary>
    public List<string?> CreatedDocumentDescriptions { get; } = new();

    internal string CreateDocument(CreateDocumentRequest request)
    {
        lock (_gate)
        {
            if (FailNextDocumentCreate)
            {
                FailNextDocumentCreate = false;
                throw new InvalidOperationException("Test: Dataverse refused the sprk_document create.");
            }

            DocumentCreates++;
            CreatedDocumentNames.Add(request.Name);
            CreatedDocumentDescriptions.Add(request.Description);
            // FR-02 (task 014): Dataverse accepts a caller-supplied primary key on Create, and the Office
            // document-create path now supplies one so the row's id matches the id stamped into the bytes it
            // uploaded. Honouring it here is what lets a test read the stamp out of the stored item and compare
            // it against the created row — a world that always minted its own id could not observe the link.
            var id = request.Id ?? Guid.NewGuid();
            Documents[id] = new DocumentRow { Id = id, FileName = request.Name };
            return id.ToString("D");
        }
    }

    internal void ApplyUpdate(string id, UpdateDocumentRequest update)
    {
        lock (_gate)
        {
            DocumentUpdates.Add((id, update));
            if (!Documents.TryGetValue(Guid.Parse(id), out var row))
                return;
            row.DriveId = update.GraphDriveId ?? row.DriveId;
            row.ItemId = update.GraphItemId ?? row.ItemId;
            row.FileName = update.FileName ?? row.FileName;
            row.FileSize = update.FileSize ?? row.FileSize;
            row.FilePath = update.FilePath ?? row.FilePath;
            row.CanonicalHash = update.CanonicalHash ?? row.CanonicalHash;
        }
    }

    internal DocumentEntity? ReadDocument(string id)
    {
        lock (_gate)
        {
            DocumentReads.Add(id);
            return Documents.TryGetValue(Guid.Parse(id), out var row)
                ? new DocumentEntity
                {
                    Id = row.Id.ToString("D"),
                    Name = row.FileName ?? "document",
                    FileName = row.FileName,
                    GraphDriveId = row.DriveId,
                    GraphItemId = row.ItemId,
                }
                : null;
        }
    }

    internal Guid RecordJob(object job)
    {
        lock (_gate)
        {
            var type = job.GetType();
            var name = (string?)type.GetProperty("Name")?.GetValue(job) ?? string.Empty;
            var key = (string?)type.GetProperty("IdempotencyKey")?.GetValue(job) ?? string.Empty;
            var status = type.GetProperty("Status")?.GetValue(job) as int? ?? 0;
            var id = Guid.NewGuid();
            Jobs.Add((name, key));
            JobStatuses[id] = status;
            // Last write wins, so a key maps to its NEWEST job — the row the real query returns first
            // (DataverseServiceClientImpl.GetProcessingJobByIdempotencyKeyAsync orders by createdon desc, task 039).
            _jobIdsByKey[key] = id;
            return id;
        }
    }

    /// <summary>
    /// The Dataverse <c>sprk_status</c> each ProcessingJob row holds (0 Queued, 1 Running, 2 Completed, 3 Failed,
    /// 4 Cancelled), as written by <c>CreateProcessingJobAsync</c> and every <c>UpdateProcessingJobAsync</c> since.
    /// </summary>
    public Dictionary<Guid, int> JobStatuses { get; } = new();

    internal void UpdateJob(Guid id, object update)
    {
        lock (_gate)
        {
            if (update.GetType().GetProperty("Status")?.GetValue(update) is int status)
                JobStatuses[id] = status;
        }
    }

    internal object? FindJobByIdempotencyKey(string key)
    {
        lock (_gate)
        {
            if (!_jobIdsByKey.TryGetValue(key, out var id))
                return null;
            dynamic existing = new System.Dynamic.ExpandoObject();
            existing.Id = id;
            // The row's REAL status (task 039, finding 2). This fixture used to answer Completed for every key,
            // which hid the failed-job replay: a same-key retry after a failure was answered from a row the
            // fixture claimed had succeeded.
            existing.Status = JobStatuses.TryGetValue(id, out var status) ? status : 0;
            existing.JobType = 0;
            existing.Progress = 100;
            return (object)existing;
        }
    }

    /// <summary>
    /// When set, the NEXT <c>CreateDocumentAsync</c> throws — a save that fails AFTER its ProcessingJob row and
    /// SPE upload exist, i.e. one that leaves its job neither Completed nor explicitly Failed unless the save
    /// path marks it (task 039, finding 2).
    /// </summary>
    public bool FailNextDocumentCreate { get; set; }

    internal Microsoft.Xrm.Sdk.Entity RetrieveDocumentByItemId(string itemId)
    {
        lock (_gate)
        {
            var row = Documents.Values.FirstOrDefault(r => string.Equals(r.ItemId, itemId, StringComparison.Ordinal))
                // The real client signals "no row for this alternate key" by THROWING.
                ?? throw new InvalidOperationException("sprk_document not found with provided alternate key values.");

            var entity = new Microsoft.Xrm.Sdk.Entity("sprk_document", row.Id);
            entity["sprk_canonicalhash"] = row.CanonicalHash;
            if (row.CanonicalDocumentId is { } canonical)
                entity["sprk_canonicaldocument"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_document", canonical);
            return entity;
        }
    }

    internal void ApplyGenericUpdate(Guid id, Dictionary<string, object> fields)
    {
        lock (_gate)
        {
            GenericUpdates.Add((id, fields));
            if (!Documents.TryGetValue(id, out var row))
                return;
            if (fields.TryGetValue("sprk_canonicaldocument", out var link))
            {
                // DBNull = the graduation sever; an EntityReference = the editable-copy LINK (task 028), which
                // task 024's override test reads back from the row.
                if (link is DBNull)
                    row.CanonicalDocumentId = null;
                else if (link is Microsoft.Xrm.Sdk.EntityReference reference)
                    row.CanonicalDocumentId = reference.Id;
            }
            if (fields.TryGetValue("sprk_canonicalhash", out var hash) && hash is string h)
                row.CanonicalHash = h;
        }
    }

    /// <summary>
    /// When set, the task-046 file-reference lookup (the <c>sprk_graphitemid</c> query answered by
    /// <see cref="RetrieveMultiple"/>) throws — a Dataverse read that fails.
    /// </summary>
    public bool FailDocumentReferenceLookup { get; set; }

    /// <summary>
    /// The three query shapes the save path issues; every other query answers empty, exactly as this fixture
    /// always did. Seeded rows are active.
    /// <list type="bullet">
    /// <item>Task 024 — <c>ContentDedupDetector</c>'s canonical lookup: an ACTIVE <c>sprk_document</c> whose
    /// <c>sprk_canonicalhash</c> equals the hash and whose <c>sprk_canonicaldocument</c> is null (a true canonical,
    /// never a hash-linked copy), top 1.</item>
    /// <item>Task 046 — the file-reference lookup: every <c>sprk_document</c> whose <c>sprk_graphitemid</c> equals the
    /// item id, matched case-insensitively as Dataverse string equality is.</item>
    /// <item>Task 025 — the collision-target lookup (<c>FindDocumentIdByLocationAsync</c>): the <c>sprk_document</c>
    /// whose <c>sprk_graphdriveid</c> AND <c>sprk_filename</c> both equal the collision's, top 1.</item>
    /// </list>
    /// </summary>
    internal Microsoft.Xrm.Sdk.EntityCollection RetrieveMultiple(Microsoft.Xrm.Sdk.Query.QueryExpression query)
    {
        var result = new Microsoft.Xrm.Sdk.EntityCollection();
        if (!string.Equals(query.EntityName, DocumentEntityName, StringComparison.Ordinal))
            return result;

        var conditions = query.Criteria.Conditions;

        var driveCondition = conditions.FirstOrDefault(c =>
            c.AttributeName == "sprk_graphdriveid" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal);
        var fileNameCondition = conditions.FirstOrDefault(c =>
            c.AttributeName == "sprk_filename" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal);
        if (driveCondition is not null && fileNameCondition is not null)
        {
            if (driveCondition.Values.Count == 1 && driveCondition.Values[0] is string wantedDrive
                && fileNameCondition.Values.Count == 1 && fileNameCondition.Values[0] is string wantedFileName)
            {
                lock (_gate)
                {
                    var match = Documents.Values.FirstOrDefault(r =>
                        string.Equals(r.DriveId, wantedDrive, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.FileName, wantedFileName, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                        result.Entities.Add(new Microsoft.Xrm.Sdk.Entity(DocumentEntityName, match.Id));
                }
            }

            return result;
        }

        var itemCondition = conditions.FirstOrDefault(c =>
            c.AttributeName == "sprk_graphitemid" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal);
        if (itemCondition is not null && conditions.All(c => c.AttributeName != "sprk_canonicalhash"))
        {
            if (FailDocumentReferenceLookup)
                throw new InvalidOperationException("Test: the sprk_document file-reference lookup failed.");

            if (itemCondition.Values.Count == 1 && itemCondition.Values[0] is string itemId)
            {
                lock (_gate)
                {
                    foreach (var row in Documents.Values.Where(r =>
                                 string.Equals(r.ItemId, itemId, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Entities.Add(new Microsoft.Xrm.Sdk.Entity(DocumentEntityName, row.Id));
                    }
                }
            }

            return result;
        }

        var hashCondition = conditions.FirstOrDefault(c =>
            c.AttributeName == "sprk_canonicalhash" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal);
        var excludesLinkedCopies = conditions.Any(c =>
            c.AttributeName == "sprk_canonicaldocument" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.Null);
        if (hashCondition is null || hashCondition.Values.Count != 1 || hashCondition.Values[0] is not string hash
            || !excludesLinkedCopies)
        {
            return result;
        }

        lock (_gate)
        {
            var canonical = Documents.Values.FirstOrDefault(r =>
                string.Equals(r.CanonicalHash, hash, StringComparison.Ordinal) && r.CanonicalDocumentId is null);
            if (canonical is not null)
                result.Entities.Add(new Microsoft.Xrm.Sdk.Entity(DocumentEntityName, canonical.Id));
        }

        return result;
    }

    private const string DocumentEntityName = "sprk_document";

    // ── SPE ───────────────────────────────────────────────────────────────────────────────────────

    internal FileHandleDto PutByPath(string driveId, string path, byte[] bytes)
    {
        lock (_gate)
        {
            UploadSmallCalls++;
            var item = SpeItems.Values.FirstOrDefault(i =>
                i.DriveId == driveId && string.Equals(i.Name, path, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                item = new SpeItem { DriveId = driveId, Id = $"item-{Guid.NewGuid():N}", Name = path };
                SpeItems[item.Id] = item;
            }
            item.Versions.Add(bytes);
            return Handle(item, bytes.Length);
        }
    }

    /// <summary>Task 025: how many times an upload was refused with a name collision (ConflictBehavior.Fail
    /// against an existing item) — the refusal writes nothing, so it is counted separately from
    /// <see cref="UploadSmallCalls"/>.</summary>
    public int CollisionRefusals { get; private set; }

    /// <summary>
    /// Task 025: path-keyed upload with an EXPLICIT conflict behaviour, mirroring the real Fail/Rename/Replace
    /// semantics <c>UploadSessionManager.PutContentWithConflictBehaviorAsync</c> implements against Graph.
    /// <c>Fail</c> on an existing name THROWS the same typed exception the facade does (no bytes written, no
    /// version added, no new item created) — the collision this method exists to reproduce. <c>Rename</c> on an
    /// existing name mints a NEW item under a non-colliding name (mirroring Graph's own auto-rename), leaving the
    /// existing item completely untouched. Both behave exactly like <see cref="PutByPath"/> when there is no
    /// collision, or under <c>Replace</c>.
    /// </summary>
    internal FileHandleDto PutByPathWithConflictBehavior(
        string driveId, string path, byte[] bytes, ConflictBehavior conflictBehavior)
    {
        lock (_gate)
        {
            var existing = SpeItems.Values.FirstOrDefault(i =>
                i.DriveId == driveId && string.Equals(i.Name, path, StringComparison.OrdinalIgnoreCase));

            if (existing is not null && conflictBehavior == ConflictBehavior.Fail)
            {
                CollisionRefusals++;
                throw new SpaarkeStorageException(
                    $"A file named '{path}' already exists in this location.",
                    statusCode: 409,
                    errorCode: "nameAlreadyExists");
            }

            if (existing is not null && conflictBehavior == ConflictBehavior.Rename)
            {
                UploadSmallCalls++;
                var renamed = NextAvailableName(driveId, path);
                var newItem = new SpeItem { DriveId = driveId, Id = $"item-{Guid.NewGuid():N}", Name = renamed };
                newItem.Versions.Add(bytes);
                SpeItems[newItem.Id] = newItem;
                return Handle(newItem, bytes.Length);
            }

            UploadSmallCalls++;
            var item = existing ?? new SpeItem { DriveId = driveId, Id = $"item-{Guid.NewGuid():N}", Name = path };
            if (existing is null)
                SpeItems[item.Id] = item;
            item.Versions.Add(bytes);
            return Handle(item, bytes.Length);
        }
    }

    /// <summary>Mirrors Graph's own conflictBehavior=rename: the first "name (n).ext" that does not collide.</summary>
    private string NextAvailableName(string driveId, string path)
    {
        var dot = path.LastIndexOf('.');
        var stem = dot >= 0 ? path[..dot] : path;
        var ext = dot >= 0 ? path[dot..] : string.Empty;
        for (var n = 1; ; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (!SpeItems.Values.Any(i => i.DriveId == driveId && string.Equals(i.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    internal FileHandleDto? PutByItemId(string driveId, string itemId, byte[] bytes)
    {
        lock (_gate)
        {
            ReplaceCalls++;
            if (LockedItemIds.Contains(itemId))
                throw new DocumentLockedByWordException(itemId);
            if (!SpeItems.TryGetValue(itemId, out var item) || item.DriveId != driveId)
                return null; // the facade's 404 shape
            item.Versions.Add(bytes);
            return Handle(item, bytes.Length);
        }
    }

    internal string? LiveHash(string itemId)
    {
        lock (_gate)
        {
            return SpeItems.TryGetValue(itemId, out var item) ? Hash(item.Versions[^1]) : null;
        }
    }

    /// <summary>Task 047: how many times the save path READ an item's current content (the app-only download).</summary>
    public int DownloadCalls { get; private set; }

    /// <summary>Task 047: when set, every content read fails the way the facade does on a Graph error — it throws.</summary>
    public bool FailDownloads { get; set; }

    /// <summary>Task 047: the item's CURRENT content (its newest version); <c>null</c> for an unknown item (the facade's 404 shape).</summary>
    internal Stream? Download(string itemId)
    {
        lock (_gate)
        {
            DownloadCalls++;
            if (FailDownloads)
                throw new InvalidOperationException("Test: Failed to download file: Graph refused the read.");
            return SpeItems.TryGetValue(itemId, out var item) ? new MemoryStream(item.Versions[^1].ToArray()) : null;
        }
    }

    internal bool Delete(string itemId)
    {
        lock (_gate)
        {
            DeletedItemIds.Add(itemId);
            return SpeItems.Remove(itemId);
        }
    }

    private static FileHandleDto Handle(SpeItem item, long size) => new(
        Id: item.Id,
        Name: item.Name,
        ParentId: null,
        Size: size,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: $"\"{{{item.Id}}},{item.Versions.Count}\"",
        IsFolder: false,
        WebUrl: item.WebUrl);

    // ── Authorization + Service Bus ───────────────────────────────────────────────────────────────

    internal AccessSnapshot Access(string userId, string resourceId)
    {
        lock (_gate)
        {
            AccessChecks.Add(resourceId);
            var rights = Guid.TryParse(resourceId, out var id) && AccessByDocumentId.TryGetValue(id, out var granted)
                ? granted
                : AccessRights.None;
            return new AccessSnapshot { UserId = userId, ResourceId = resourceId, AccessRights = rights };
        }
    }

    internal void RecordFinalization(string messageJson)
    {
        using var document = JsonDocument.Parse(messageJson);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "Payload", StringComparison.OrdinalIgnoreCase))
            {
                lock (_gate)
                {
                    FinalizationPayloads.Add(property.Value.Clone());
                }
            }
        }
    }

    internal static byte[] ReadAll(Stream content)
    {
        using var buffer = new MemoryStream();
        if (content.CanSeek)
            content.Position = 0;
        content.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Case-insensitive property read on a finalization payload.</summary>
    public static string? PayloadValue(JsonElement payload, string name)
    {
        foreach (var property in payload.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
        }

        return null;
    }
}

/// <summary>
/// <see cref="OfficeTestWebAppFactory"/> with its Dataverse, SPE, Service Bus and access-data doubles bound to
/// one <see cref="OfficeVersionSaveWorld"/>. Everything else (auth handler, config, the
/// <see cref="CallerRecordAccessProbe"/> grant, the non-securable registry) is task 016's fixture, unchanged.
/// </summary>
public sealed class OfficeVersionSaveTestWebAppFactory : OfficeTestWebAppFactory
{
    private readonly OfficeVersionSaveWorld _world;

    public OfficeVersionSaveTestWebAppFactory(OfficeVersionSaveWorld world)
    {
        _world = world;
    }

    /// <summary>
    /// Every client this factory creates carries a caller bearer token.
    /// </summary>
    /// <remarks>
    /// §F.2 (fixture-config-first), found by running the suite: <c>AuthorizationService</c> FAILS CLOSED with
    /// <c>sdap.access.deny.no_caller_token</c> when a caller-scoped check has no bearer token — correct
    /// production behaviour (it refuses to degrade to app-only evaluation). The base fixture's
    /// <see cref="TestAuthHandler"/> authenticates WITHOUT an Authorization header, so without this every
    /// version save 403'd before the access data source was even consulted. Same arrangement as
    /// <c>DocumentIdentityContractTests</c>. The token's value is never validated by the test host.
    /// </remarks>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-caller-token");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var world = _world;

            var dataverse = new Mock<IDataverseService>();
            dataverse.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            dataverse
                .Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateDocumentRequest r, CancellationToken _) => world.CreateDocument(r));
            dataverse
                .Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .Callback((string id, UpdateDocumentRequest u, CancellationToken _) => world.ApplyUpdate(id, u))
                .Returns(Task.CompletedTask);
            dataverse
                .Setup(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) => world.ReadDocument(id));
            dataverse
                .Setup(d => d.CreateProcessingJobAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((object job, CancellationToken _) => world.RecordJob(job));
            dataverse
                .Setup(d => d.GetProcessingJobByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, CancellationToken _) => world.FindJobByIdempotencyKey(key));
            // Task 039: the job's status is state the save path WRITES (Failed / Completed) and later READS back
            // through the idempotency lookup, so the world records it rather than letting the loose mock drop it.
            dataverse
                .Setup(d => d.UpdateProcessingJobAsync(It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Callback((Guid id, object update, CancellationToken _) => world.UpdateJob(id, update))
                .Returns(Task.CompletedTask);
            dataverse
                .Setup(d => d.RetrieveByAlternateKeyAsync(
                    "sprk_document", It.IsAny<Microsoft.Xrm.Sdk.KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Microsoft.Xrm.Sdk.KeyAttributeCollection keys, string[] _, CancellationToken _) =>
                    world.RetrieveDocumentByItemId((string)keys["sprk_graphitemid"]));
            dataverse
                .Setup(d => d.UpdateAsync("sprk_document", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Callback((string _, Guid id, Dictionary<string, object> fields, CancellationToken _) => world.ApplyGenericUpdate(id, fields))
                .Returns(Task.CompletedTask);
            // Task 024: answers the content-dedup canonical lookup from the world (empty for every other query,
            // as before), so the editable LINK a byte-identical "Save as new document" writes is observable.
            dataverse
                .Setup(d => d.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression query, CancellationToken _) => world.RetrieveMultiple(query));
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverse.Object);

            // Task 025: the collision-target lookup (OfficeDocumentPersistence.FindDocumentIdByLocationAsync)
            // reads through IGenericEntityService, not IDataverseService — but per GraphModule.cs,
            // IGenericEntityService (along with IDocumentDataverseService and IProcessingJobService) is
            // registered as `sp.GetRequiredService<IDataverseService>()`, and IDataverseService itself
            // INHERITS IGenericEntityService (the composite-interface pattern this factory's base class
            // already documents above, "IDataverseService is the ONE composite implementation..."). The
            // `dataverse` mock just above therefore ALREADY answers IGenericEntityService.RetrieveMultipleAsync
            // via its RetrieveMultipleAsync setup — no separate registration needed (an explicit one was
            // tried and found to be an unnecessary, behavior-preserving-in-theory-but-NOT-in-Moq-practice
            // duplicate: it is NOT wired here, deliberately).

            var graphClientFactory = Mock.Of<IGraphClientFactory>();
            var spe = new Mock<SpeFileStore>(
                MockBehavior.Loose,
                new ContainerOperations(graphClientFactory, Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(graphClientFactory, Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(graphClientFactory, Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(graphClientFactory, Mock.Of<ILogger<UserOperations>>()),
                null!);
            spe.Setup(s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string driveId, string path, Stream content, CancellationToken _) =>
                    (FileHandleDto?)world.PutByPath(driveId, path, OfficeVersionSaveWorld.ReadAll(content)));
            // Task 025: OfficeStorageUploader now always calls THIS explicit-conflictBehavior overload (the
            // create path passes Fail by default, Rename for "Keep both") — the 4-arg mock above is kept for
            // any other caller, but is no longer reachable from the Office save path.
            spe.Setup(s => s.UploadSmallAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                    It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string driveId, string path, Stream content, ConflictBehavior conflictBehavior, CancellationToken _) =>
                    (FileHandleDto?)world.PutByPathWithConflictBehavior(driveId, path, OfficeVersionSaveWorld.ReadAll(content), conflictBehavior));
            spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                    It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Microsoft.AspNetCore.Http.HttpContext _, string driveId, string itemId, Stream content, CancellationToken _) =>
                    world.PutByItemId(driveId, itemId, OfficeVersionSaveWorld.ReadAll(content)));
            spe.Setup(s => s.GetQuickXorHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string itemId, CancellationToken _) => world.LiveHash(itemId));
            // Task 047: a version save's duplicate check reads the item's CURRENT content to confirm the document still
            // holds what the completed job wrote.
            spe.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string itemId, CancellationToken _) => world.Download(itemId));
            spe.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string itemId, CancellationToken _) => world.Delete(itemId));
            services.RemoveAll<SpeFileStore>();
            services.AddScoped(_ => spe.Object);

            var access = new Mock<IAccessDataSource>();
            access
                .Setup(a => a.GetUserAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string userId, string resourceId, string? _, CancellationToken _) => world.Access(userId, resourceId));
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton(access.Object);

            var sender = new Mock<ServiceBusSender>();
            sender
                .Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
                .Callback((ServiceBusMessage message, CancellationToken _) => world.RecordFinalization(message.Body.ToString()))
                .Returns(Task.CompletedTask);
            var serviceBus = new Mock<ServiceBusClient>();
            serviceBus.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(sender.Object);
            services.RemoveAll<OfficeJobQueue>();
            services.AddScoped(sp => new OfficeJobQueue(
                serviceBus.Object,
                Options.Create(new ServiceBusOptions()),
                sp.GetRequiredService<ILogger<OfficeJobQueue>>()));
        });
    }
}

// =====================================================================================================
// FR-11 — THE WORD PANE'S WIRE SHAPE (spaarkeai-word-add-in-r1 task 024)
// =====================================================================================================

/// <summary>
/// The contract between the Word pane's <c>useSaveFlow</c> and <c>POST /api/office/save</c> for FR-11, posted as
/// the RAW JSON the pane builds — camelCase, with the members it sends that the server model does not name
/// (<c>documentMetadata</c>, <c>aiOptions</c>, <c>document.contentType</c>) — rather than as a server-side
/// <see cref="SaveRequest"/>, so a drift in either side's field names fails here.
/// </summary>
/// <remarks>
/// <para><b>The default (an identified document)</b> sends <c>document.existingDocumentId</c> (bare-lowercase,
/// ADR-044) with <c>document.isNewVersion: true</c>, no <c>targetEntity</c>, and its idempotency key in BOTH the body
/// and the <c>X-Idempotency-Key</c> header. The body key matters: the server's persistent job dedupe reads
/// <c>SaveRequest.IdempotencyKey</c>, while the header only reaches the <c>IdempotencyFilter</c> response cache.</para>
/// <para><b>The override</b> ("A new document") sends neither version field and a header key only — the create path,
/// byte-for-byte as before task 024.</para>
/// </remarks>
[Trait("status", "repaired")]
public class OfficeSaveAddInWireContractTests
{
    // FR-02 (task 014): REAL minimal .docx bytes — see the note on OfficeVersionSaveContractTests' fixtures.
    private static readonly byte[] InitialBytes = MinimalDocx.Create("wire initial");
    private static readonly byte[] Revision2 = MinimalDocx.Create("wire revision 2");
    private static readonly byte[] Revision3 = MinimalDocx.Create("wire revision 3");

    /// <summary>The body useSaveFlow builds for a Word Document save.</summary>
    private static string PaneBody(byte[] bytes, string? existingDocumentId, string? bodyKey, bool withTarget = false)
    {
        var document = new Dictionary<string, object?>
        {
            ["fileName"] = "Brief.docx",
            ["title"] = "Brief",
            ["contentType"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ["contentBase64"] = Convert.ToBase64String(bytes),
        };
        if (existingDocumentId is not null)
        {
            document["existingDocumentId"] = existingDocumentId;
            document["isNewVersion"] = true;
        }

        var body = new Dictionary<string, object?>
        {
            ["contentType"] = "Document",
            ["triggerAiProcessing"] = true,
            ["aiOptions"] = new { profileSummary = true, ragIndex = true, deepAnalysis = false },
            ["documentMetadata"] = new { name = "Brief" },
            ["document"] = document,
        };
        if (withTarget)
        {
            body["targetEntity"] = new { entityType = "Matter", entityId = Guid.NewGuid(), displayName = "Acme v. Beta" };
        }
        if (bodyKey is not null)
        {
            body["idempotencyKey"] = bodyKey;
        }

        return JsonSerializer.Serialize(body);
    }

    private static HttpRequestMessage PanePost(string json, string headerKey)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("X-Idempotency-Key", headerKey);
        return message;
    }

    private static string Key(string label) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(label))).ToLowerInvariant();

    [Fact]
    public async Task Post_OfficeSave_PaneVersionSaveBody_Returns202_WritesAVersion_AndLeavesExactlyOneRow()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var key = Key("revision-2");

        var response = await factory.CreateClient().SendAsync(
            PanePost(PaneBody(Revision2, documentId.ToString("D"), key), key));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.Documents.Should().ContainSingle("an identified document's save produces no second row")
            .Which.Key.Should().Be(documentId);
        world.DocumentCreates.Should().Be(0);
        world.SpeItems[itemId].Versions.Should().HaveCount(2);
        world.AccessChecks.Should().ContainSingle().Which.Should().Be(documentId.ToString("D"));
        var job = world.Jobs.Should().ContainSingle().Subject;
        job.Name.Should().StartWith("Document Version Save - ");
        job.IdempotencyKey.Should().Be(key, "the body key the pane sends is the server's persistent dedupe key");
    }

    [Fact]
    public async Task Post_OfficeSave_PaneVersionSave_WithAnUppercaseId_IsCanonicalizedAtTheServerBoundary()
    {
        // ADR-044: the pane canonicalizes before sending, and the server does again — a typed Guid formatted "D".
        var world = new OfficeVersionSaveWorld();
        var (documentId, _) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var key = Key("uppercase");

        var response = await factory.CreateClient().SendAsync(
            PanePost(PaneBody(Revision2, documentId.ToString("D").ToUpperInvariant(), key), key));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.AccessChecks.Should().ContainSingle().Which.Should().Be(documentId.ToString("D"));
        world.DocumentCreates.Should().Be(0);
    }

    [Fact]
    public async Task SuccessivePaneRevisions_EachWithItsOwnKey_AreEachWritten_AndAnIdenticalResendIsNot()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var id = documentId.ToString("D");

        (await client.SendAsync(PanePost(PaneBody(Revision2, id, Key("r2")), Key("r2"))))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.SendAsync(PanePost(PaneBody(Revision3, id, Key("r3")), Key("r3"))))
            .StatusCode.Should().Be(HttpStatusCode.Accepted, "a different revision is a different operation");
        world.SpeItems[itemId].Versions.Should().HaveCount(3);

        // The pane re-sending the same revision sends the same key — it is de-duplicated, never re-written.
        var resend = await client.SendAsync(PanePost(PaneBody(Revision3, id, Key("r3")), Key("r3")));
        resend.IsSuccessStatusCode.Should().BeTrue();
        world.SpeItems[itemId].Versions.Should().HaveCount(3, "an identical re-send writes nothing new");
        world.Documents.Should().HaveCount(1);
        world.DocumentCreates.Should().Be(0);
    }

    [Fact]
    public async Task PaneRetryAfterALockedRefusal_WithTheNextAttemptKey_WritesTheVersion()
    {
        // The pane counts the failed attempt into its next key, so the retry is not answered with the FAILED job
        // that the refused attempt left behind under the first key.
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        world.LockedItemIds.Add(itemId);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var id = documentId.ToString("D");

        var refused = await client.SendAsync(PanePost(PaneBody(Revision2, id, Key("attempt-0")), Key("attempt-0")));
        refused.StatusCode.Should().Be((HttpStatusCode)423);
        world.SpeItems[itemId].Versions.Should().HaveCount(1);

        world.LockedItemIds.Remove(itemId); // the user closed the other editor
        var retried = await client.SendAsync(PanePost(PaneBody(Revision2, id, Key("attempt-1")), Key("attempt-1")));

        retried.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.SpeItems[itemId].Versions.Should().HaveCount(2);
        world.Documents.Should().HaveCount(1);
        world.DocumentCreates.Should().Be(0);
    }

    [Fact]
    public async Task Post_OfficeSave_PaneOverrideBody_CreatesANewRow_AndNeverEngagesTheVersionGate()
    {
        var world = new OfficeVersionSaveWorld();
        var (_, seededItemId) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().SendAsync(
            PanePost(PaneBody(Revision2, existingDocumentId: null, bodyKey: null, withTarget: true), Key("override")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.DocumentCreates.Should().Be(1, "the override creates a new sprk_document");
        world.AccessChecks.Should().BeEmpty("no existingDocumentId → the version-save gate does not engage");
        world.ReplaceCalls.Should().Be(0);
        world.SpeItems[seededItemId].Versions.Should().HaveCount(1, "the identified document's file is untouched");
        world.Jobs.Should().ContainSingle().Which.Name.Should().StartWith("Document Save - ");
    }
}

// =====================================================================================================
// SAVE-SPINE DEFECTS — spaarkeai-word-add-in-r1 task 039 (findings 1 and 3, plus the key-shape pins)
// =====================================================================================================

/// <summary>
/// The idempotency-key contract and the job-status result of <c>POST /api/office/save</c>, after task 039:
/// <list type="bullet">
/// <item><b>Finding 1 — one authoritative source.</b> Whether a save runs is decided by the key in the request
/// BODY (<c>idempotencyKey</c>), else by the server's own key, which is content-aware for every Document save.
/// The <c>X-Idempotency-Key</c> header is never that key: it only names the 24-hour response-replay cache
/// (<c>IdempotencyFilter</c>), and for a Document save that cache entry is bound to the exact request body, so a
/// reused header can never replay a response for a different document. Email and Attachment keep today's
/// header-keyed replay (their header already names the immutable message/attachment the server key names).</item>
/// <item><b>Finding 3 — a completed job names its document.</b> Every save that completes carries
/// <c>result.artifact.id</c>: the created document, the existing document a version was written to, or the
/// canonical an immutable duplicate resolved to.</item>
/// </list>
/// </summary>
/// <remarks>Same world and fixture as task 023/024 (<see cref="OfficeVersionSaveWorld"/>): module-boundary doubles
/// only, and the REAL route — filters, <c>IdempotencyFilter</c> over the in-memory distributed cache, handler,
/// <c>OfficeService</c>. No <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038 B1).</remarks>
[Trait("status", "repaired")]
public class OfficeSaveSpineIdempotencyContractTests
{
    // FR-02 (task 014): REAL minimal .docx bytes — see the note on OfficeVersionSaveContractTests' fixtures.
    // The ContentHash assertions below are unaffected: they hash the REQUEST's base64, which the stamper
    // never touches (it works on a decoded copy and never writes back to the request).
    private static readonly byte[] InitialBytes = MinimalDocx.Create("spine initial");
    private static readonly byte[] Revision2 = MinimalDocx.Create("spine revision 2");
    private static readonly byte[] Revision3 = MinimalDocx.Create("spine revision 3");

    private static HttpRequestMessage Post(SaveRequest body, string? headerKey)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/office/save") { Content = JsonContent.Create(body) };
        if (headerKey is not null)
            message.Headers.Add("X-Idempotency-Key", headerKey);
        return message;
    }

    /// <summary>The server's key for a canonical string — <c>OfficeService.GenerateIdempotencyKey</c>'s final step.</summary>
    internal static string ServerKey(string canonical) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

    /// <summary>The content component — SHA-256 (upper hex) of the base64 the request carries (task 023's hashing).</summary>
    internal static string ContentHash(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Convert.ToBase64String(bytes))));

    private static async Task<JobStatusResponse> PollAsync(HttpClient client, SaveResponse saved)
    {
        var response = await client.GetAsync($"/api/office/jobs/{saved.JobId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JobStatusResponse>())!;
    }

    // ── Finding 1: which key decides ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VersionSaves_WhoseHeaderAndBodyKeysDisagree_AreDecidedByTheBodyKey_NotReplayedUnderTheReusedHeader()
    {
        // A client that keeps ONE header across revisions but sends a fresh body key per revision. Before task
        // 039 the header answered the second revision from the response cache — its body key, the key the save
        // spine treats as authoritative, was never consulted, and the new bytes were never written.
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var target = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() };

        var second = OfficeVersionSaveWorld.VersionSave(documentId, Revision2) with { TargetEntity = target, IdempotencyKey = "body-r2" };
        var third = OfficeVersionSaveWorld.VersionSave(documentId, Revision3) with { TargetEntity = target, IdempotencyKey = "body-r3" };

        (await client.SendAsync(Post(second, "one-header"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var response = await client.SendAsync(Post(third, "one-header"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.GetValues("X-Idempotency-Status").Should().Equal(new[] { "new" },
            "a reused header never replays a response for a different Document body");
        world.SpeItems[itemId].Versions.Should().HaveCount(3, "the third revision is written");
        // FR-02 (task 014): the stored version carries this document's identity stamp, so the content claim is
        // made against body text rather than raw bytes.
        MinimalDocx.ReadBodyText(world.SpeItems[itemId].Versions[^1]).Should().Be("spine revision 3");
        world.Jobs.Select(j => j.IdempotencyKey).Should().Equal(new[] { "body-r2", "body-r3" },
            "the BODY key is the persistent key; the header never becomes it");
    }

    [Fact]
    public async Task DocumentSave_WithAHeaderKeyOnly_IsKeyedByTheServer_NeverByTheHeader()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var response = await factory.CreateClient().SendAsync(
            Post(OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", InitialBytes), "pane-header-key"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.Jobs.Should().ContainSingle().Which.IdempotencyKey.Should().NotBe("pane-header-key");
    }

    [Fact]
    public async Task EmailSave_UnderAReusedHeader_WithADifferentBody_IsStillReplayedFromTheResponseCache_AsBefore()
    {
        // Email/Attachment replay is deliberately UNCHANGED by task 039: the pane's Outlook header already names
        // the immutable message (and record) the server key names, so a header hit is a request the persistent
        // key would also have de-duplicated. The subject is the only thing that differs here.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var target = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() };
        SaveRequest Email(string subject) => new()
        {
            ContentType = SaveContentType.Email,
            TargetEntity = target,
            Email = new EmailMetadata { Subject = subject, SenderEmail = "sender@test.com", InternetMessageId = "<m1@test>" },
        };

        (await client.SendAsync(Post(Email("Filing"), "outlook-header"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var replay = await client.SendAsync(Post(Email("Filing (renamed)"), "outlook-header"));

        replay.StatusCode.Should().Be(HttpStatusCode.Accepted);
        replay.Headers.GetValues("X-Idempotency-Status").Should().Equal(new[] { "cached" });
        world.DocumentCreates.Should().Be(1);
        world.Jobs.Should().ContainSingle();
    }

    // ── The server key: create gains content, everything else is byte-for-byte unchanged ────────────

    [Fact]
    public async Task DocumentCreateKey_CarriesTheContentHash()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var request = OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", InitialBytes);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", request))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var canonical = $"Document|matter|{request.TargetEntity!.EntityId}|||Brief.docx||create-content:{ContentHash(InitialBytes)}";
        world.Jobs.Should().ContainSingle().Which.IdempotencyKey.Should().Be(ServerKey(canonical));
    }

    [Fact]
    public async Task VersionSaveKey_IsUnchangedByTask039()
    {
        // Task 023's shape, pinned: the create-key change must not move it.
        var world = new OfficeVersionSaveWorld();
        var (documentId, _) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var request = OfficeVersionSaveWorld.VersionSave(documentId, Revision2);

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", request))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var canonical = $"Document|matter|{request.TargetEntity!.EntityId}|||Brief.docx|{documentId}|version-content:{ContentHash(Revision2)}";
        world.Jobs.Should().ContainSingle().Which.IdempotencyKey.Should().Be(ServerKey(canonical));
    }

    [Theory]
    [InlineData(SaveContentType.Email)]
    [InlineData(SaveContentType.Attachment)]
    public async Task EmailAndAttachmentKeys_AreUnchangedByTask039(SaveContentType contentType)
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var target = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() };
        var request = new SaveRequest
        {
            ContentType = contentType,
            TargetEntity = target,
            Email = contentType == SaveContentType.Email
                ? new EmailMetadata { Subject = "Filing", SenderEmail = "sender@test.com" }
                : null,
            Attachment = contentType == SaveContentType.Attachment
                ? new AttachmentMetadata { AttachmentId = "att-1", FileName = "Exhibit A.pdf", ContentBase64 = Convert.ToBase64String(Revision2) }
                : null,
        };

        (await factory.CreateClient().PostAsJsonAsync("/api/office/save", request))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var canonical = contentType == SaveContentType.Email
            ? $"Email|matter|{target.EntityId}|Filing|||"
            : $"Attachment|matter|{target.EntityId}||att-1||";
        world.Jobs.Should().ContainSingle().Which.IdempotencyKey.Should().Be(ServerKey(canonical),
            "the Email/Attachment canonical string carries no content component, before or after task 039");
    }

    // ── Finding 3: a completed job names its document ─────────────────────────────────────────────

    [Fact]
    public async Task CompletedCreateSave_JobStatusCarriesTheCreatedDocumentId()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/office/save", OfficeVersionSaveWorld.NewDocumentSave("Brief.docx", InitialBytes));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = world.Documents.Values.Should().ContainSingle().Subject;

        var job = await PollAsync(client, (await response.Content.ReadFromJsonAsync<SaveResponse>())!);

        job.Status.Should().Be(JobStatus.Completed);
        job.Result.Should().NotBeNull("the pane can only reach its success state when the completed job names the document");
        job.Result!.Artifact!.Id.Should().Be(created.Id);
        job.Result.Artifact.Type.Should().Be(ArtifactType.Document);
        job.Result.Artifact.SpeFileId.Should().Be(created.ItemId);
        job.Result.Artifact.WebUrl.Should().Be(created.FilePath);
    }

    [Fact]
    public async Task CompletedVersionSave_JobStatusCarriesTheExistingDocumentId()
    {
        var world = new OfficeVersionSaveWorld();
        var (documentId, itemId) = world.SeedDocument("b!doc-drive", "Brief.docx", InitialBytes);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/office/save", OfficeVersionSaveWorld.VersionSave(documentId, Revision2));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var job = await PollAsync(client, (await response.Content.ReadFromJsonAsync<SaveResponse>())!);

        job.Status.Should().Be(JobStatus.Completed);
        job.Result!.Artifact!.Id.Should().Be(documentId, "a version is written to the EXISTING document");
        job.Result.Artifact.Type.Should().Be(ArtifactType.Document);
        job.Result.Artifact.SpeFileId.Should().Be(itemId);
    }

    [Fact]
    public async Task CompletedEmailSave_JobStatusCarriesItsDocumentId()
    {
        // Finding 3 is equally a defect for Outlook: the same pane hook needs the id to reach its success state.
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
            Email = new EmailMetadata { Subject = "Filing", SenderEmail = "sender@test.com" },
        };

        var response = await client.PostAsJsonAsync("/api/office/save", request);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = world.Documents.Values.Should().ContainSingle().Subject;

        var job = await PollAsync(client, (await response.Content.ReadFromJsonAsync<SaveResponse>())!);

        job.Result.Should().NotBeNull("the Outlook pane completes on result.artifact.id too");
        job.Result!.Artifact!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task AttachmentDeduplicatedToAnExistingCanonical_JobStatusCarriesTheCanonicalId()
    {
        // The IMMUTABLE suppress branch (Email/Attachment only since task 028): no row is created, the transient
        // upload is deleted, and the job completes "DeduplicatedToExisting". It now names the canonical it
        // resolved to, so the pane can open that document instead of stalling on the job card.
        var world = new OfficeVersionSaveWorld();
        var (canonicalId, _) = world.SeedDocument("b!doc-drive", "Exhibit A.pdf", Revision2);
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var client = factory.CreateClient();
        var request = new SaveRequest
        {
            ContentType = SaveContentType.Attachment,
            TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
            Attachment = new AttachmentMetadata { AttachmentId = "att-1", FileName = "Exhibit A.pdf", ContentBase64 = Convert.ToBase64String(Revision2) },
        };

        var response = await client.PostAsJsonAsync("/api/office/save", request);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        world.DocumentCreates.Should().Be(0, "precondition: the immutable duplicate branch ran");
        world.DeletedItemIds.Should().ContainSingle();

        var job = await PollAsync(client, (await response.Content.ReadFromJsonAsync<SaveResponse>())!);

        job.CurrentPhase.Should().Be("DeduplicatedToExisting");
        job.Result.Should().NotBeNull("the pane completes on result.artifact.id");
        job.Result!.Artifact!.Id.Should().Be(canonicalId);
        job.Result.Artifact.SpeFileId.Should().BeNull("the transient upload was deleted; the canonical's own file is not re-read");
    }
}
