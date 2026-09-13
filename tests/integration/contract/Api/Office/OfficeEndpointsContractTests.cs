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
    private static readonly byte[] InitialBytes = { 0x50, 0x4B, 0x03, 0x04, 0x01 };
    private static readonly byte[] RevisedBytes = { 0x50, 0x4B, 0x03, 0x04, 0x02, 0x02 };

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

    public static SaveRequest NewDocumentSave(string fileName, byte[] bytes) => new()
    {
        ContentType = SaveContentType.Document,
        TargetEntity = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() },
        Document = new DocumentMetadata { FileName = fileName, ContentBase64 = Convert.ToBase64String(bytes) },
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

    internal string CreateDocument(CreateDocumentRequest request)
    {
        lock (_gate)
        {
            DocumentCreates++;
            var id = Guid.NewGuid();
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
            var id = Guid.NewGuid();
            Jobs.Add((name, key));
            _jobIdsByKey[key] = id;
            return id;
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
            existing.Status = 2; // Completed
            existing.JobType = 0;
            existing.Progress = 100;
            return (object)existing;
        }
    }

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
    /// The one query shape the save path's content dedup issues (task 024): <c>ContentDedupDetector</c>'s
    /// canonical lookup — an ACTIVE <c>sprk_document</c> whose <c>sprk_canonicalhash</c> equals the hash and whose
    /// <c>sprk_canonicaldocument</c> is null (a true canonical, never a hash-linked copy), top 1. Every other
    /// query answers empty, exactly as this fixture always did. Seeded rows are active.
    /// </summary>
    internal Microsoft.Xrm.Sdk.EntityCollection RetrieveMultiple(Microsoft.Xrm.Sdk.Query.QueryExpression query)
    {
        var result = new Microsoft.Xrm.Sdk.EntityCollection();
        if (!string.Equals(query.EntityName, DocumentEntityName, StringComparison.Ordinal))
            return result;

        var conditions = query.Criteria.Conditions;
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
            spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                    It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Microsoft.AspNetCore.Http.HttpContext _, string driveId, string itemId, Stream content, CancellationToken _) =>
                    world.PutByItemId(driveId, itemId, OfficeVersionSaveWorld.ReadAll(content)));
            spe.Setup(s => s.GetQuickXorHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string itemId, CancellationToken _) => world.LiveHash(itemId));
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
    private static readonly byte[] InitialBytes = { 0x50, 0x4B, 0x03, 0x04, 0x31 };
    private static readonly byte[] Revision2 = { 0x50, 0x4B, 0x03, 0x04, 0x32, 0x32 };
    private static readonly byte[] Revision3 = { 0x50, 0x4B, 0x03, 0x04, 0x33, 0x33, 0x33 };

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
