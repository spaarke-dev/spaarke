using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Integration tests for Office endpoints using WebApplicationFactory.
/// Tests the full HTTP request/response cycle including routing, filters, and serialization.
/// </summary>
public class OfficeEndpointsTests : IClassFixture<OfficeTestWebAppFactory>
{
    private readonly OfficeTestWebAppFactory _factory;
    private readonly HttpClient _client;

    public OfficeEndpointsTests(OfficeTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region Save Endpoint Tests

    [Fact(Skip = "Requires fully mocked Office services - ContainerId not configured in test")]
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
                EntityType = "sprk_matter",
                EntityId = Guid.NewGuid()
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/office/save", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var result = await response.Content.ReadFromJsonAsync<SaveResponse>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.JobId.Should().NotBe(Guid.Empty);
        result.StatusUrl.Should().Contain("/office/jobs/");
        result.StreamUrl.Should().Contain("/stream");
    }

    [Fact(Skip = "Requires fully mocked Office services - test auth handler always authenticates")]
    public async Task Post_OfficeSave_WithoutAuth_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Remove("Authorization");

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
        var response = await client.PostAsJsonAsync("/office/save", request);

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
        var response = await _client.GetAsync($"/office/jobs/{testJobId}");

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
        var response = await _client.GetAsync($"/office/jobs/{unknownJobId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Entity Search Endpoint Tests

    [Fact(Skip = "Requires fully mocked Dataverse search services")]
    public async Task Get_OfficeSearchEntities_ReturnsResults()
    {
        // Act
        var response = await _client.GetAsync("/office/search/entities?query=Acme");

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
        var response = await _client.GetAsync("/office/search/entities?query=Acme&entityTypes=Account");

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
        var response = await _client.GetAsync("/office/search/entities?query=Smith&skip=0&top=2");

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
        var response = await _client.GetAsync("/office/search/documents?query=Contract");

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
        var response = await _client.GetAsync("/office/search/documents?query=Report&contentType=pdf");

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
        var response = await _client.PostAsJsonAsync("/office/quickcreate/matter", request);

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
        var response = await _client.PostAsJsonAsync("/office/quickcreate/contact", request);

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
        var response = await _client.PostAsJsonAsync("/office/quickcreate/invalid", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
        var response = await _client.PostAsJsonAsync("/office/share/links", request);

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
        var response = await _client.PostAsJsonAsync("/office/share/links", request);

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
        var response = await _client.PostAsJsonAsync("/office/share/attach", request);

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
        var response = await _client.PostAsJsonAsync("/office/share/attach", request);

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
        var response = await _client.GetAsync("/office/recent");

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
        var response = await _client.GetAsync("/office/recent?top=2");

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
        var response = await _client.GetAsync("/office/health");

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
            $"/office/jobs/{testJobId}/stream",
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
            };
            config.AddInMemoryCollection(dict!);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
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

            // Mock IDataverseService to avoid real Dataverse connection
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);
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
