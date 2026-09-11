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
