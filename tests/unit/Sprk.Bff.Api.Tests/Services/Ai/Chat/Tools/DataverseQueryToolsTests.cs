using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Tools;

/// <summary>
/// Unit tests for <see cref="DataverseQueryTools"/>.
///
/// Verifies:
/// - QueryEntities with valid entity type returns paged results
/// - QueryEntities with invalid entity type returns error result (not exception)
/// - QueryEntities top parameter is capped at 50
/// - GetEntityDetail returns full field map
/// - GetEntityDetail for unknown id returns not-found result (not exception)
/// - [Description] attributes present for AIFunctionFactory.Create compatibility
/// - Authorization: OBO token forwarded from HttpContext; null context → error result
/// - Access denied and error HTTP responses are handled gracefully
/// </summary>
public class DataverseQueryToolsTests
{
    private const string TestEnvironmentUrl = "https://spaarkedev1.crm.dynamics.com";
    private const string TestBearerToken    = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.test.token";

    // === [Description] attribute tests ===

    [Fact]
    public void QueryEntitiesAsync_EntityTypeParam_HasDescriptionAttribute()
    {
        var method = typeof(DataverseQueryTools).GetMethod(nameof(DataverseQueryTools.QueryEntitiesAsync));
        method.Should().NotBeNull();

        var param = method!.GetParameters().First(p => p.Name == "entityType");
        var desc  = param.GetCustomAttribute<DescriptionAttribute>();

        desc.Should().NotBeNull("AIFunctionFactory.Create requires [Description] on key parameters");
        desc!.Description.Should().Contain("sprk_matter");
    }

    [Fact]
    public void QueryEntitiesAsync_FilterParam_HasDescriptionAttribute()
    {
        var method = typeof(DataverseQueryTools).GetMethod(nameof(DataverseQueryTools.QueryEntitiesAsync));
        method.Should().NotBeNull();

        var param = method!.GetParameters().First(p => p.Name == "filter");
        var desc  = param.GetCustomAttribute<DescriptionAttribute>();

        desc.Should().NotBeNull("AIFunctionFactory.Create requires [Description] on filter parameter");
        desc!.Description.Should().Contain("OData");
    }

    [Fact]
    public void GetEntityDetailAsync_EntityTypeParam_HasDescriptionAttribute()
    {
        var method = typeof(DataverseQueryTools).GetMethod(nameof(DataverseQueryTools.GetEntityDetailAsync));
        method.Should().NotBeNull();

        var param = method!.GetParameters().First(p => p.Name == "entityType");
        var desc  = param.GetCustomAttribute<DescriptionAttribute>();

        desc.Should().NotBeNull("AIFunctionFactory.Create requires [Description] on key parameters");
        desc!.Description.Should().Contain("sprk_matter");
    }

    [Fact]
    public void GetEntityDetailAsync_IdentifierParam_HasDescriptionAttribute()
    {
        var method = typeof(DataverseQueryTools).GetMethod(nameof(DataverseQueryTools.GetEntityDetailAsync));
        method.Should().NotBeNull();

        var param = method!.GetParameters().First(p => p.Name == "identifier");
        var desc  = param.GetCustomAttribute<DescriptionAttribute>();

        desc.Should().NotBeNull("AIFunctionFactory.Create requires [Description] on identifier parameter");
        desc!.Description.Should().Contain("GUID");
    }

    // === QueryEntitiesAsync: valid entity type ===

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("contact")]
    [InlineData("account")]
    [InlineData("sprk_document")]
    public async Task QueryEntitiesAsync_ValidEntityType_CallsDataverseOData(string entityType)
    {
        // Arrange
        var json = BuildODataCollectionResponse(entityType, 2);
        var (sut, handler) = BuildSut(HttpStatusCode.OK, json);

        // Act
        var result = await sut.QueryEntitiesAsync(entityType);

        // Assert
        result.Should().NotContain("Unsupported entity type");
        result.Should().NotContain("unavailable");
        handler.LastRequestUri.Should().Contain(entityType == "contact" ? "contacts"
            : entityType == "account" ? "accounts"
            : $"{entityType}s");
    }

    [Fact]
    public async Task QueryEntitiesAsync_ValidType_ReturnsFormattedRecords()
    {
        // Arrange
        var json = """
            {
              "value": [
                { "sprk_matterid": "aaaaaaaa-0000-0000-0000-000000000001", "sprk_name": "Matter Alpha", "sprk_status": "Active", "sprk_mattertype": "Litigation", "sprk_clientid": "client-001" },
                { "sprk_matterid": "aaaaaaaa-0000-0000-0000-000000000002", "sprk_name": "Matter Beta",  "sprk_status": "Closed",  "sprk_mattertype": "Contract",   "sprk_clientid": "client-002" }
              ]
            }
            """;

        var (sut, _) = BuildSut(HttpStatusCode.OK, json);

        // Act
        var result = await sut.QueryEntitiesAsync("sprk_matter");

        // Assert
        result.Should().Contain("Found 2 'sprk_matter' record(s)");
        result.Should().Contain("Matter Alpha");
        result.Should().Contain("Matter Beta");
        result.Should().Contain("Active");
        result.Should().Contain("Litigation");
    }

    [Fact]
    public async Task QueryEntitiesAsync_EmptyCollection_ReturnsNoRecordsMessage()
    {
        // Arrange
        var json = """{ "value": [] }""";
        var (sut, _) = BuildSut(HttpStatusCode.OK, json);

        // Act
        var result = await sut.QueryEntitiesAsync("contact");

        // Assert
        result.Should().Contain("No 'contact' records found");
        result.Should().NotContain("Found");
    }

    // === QueryEntitiesAsync: invalid entity type ===

    [Theory]
    [InlineData("systemuser")]
    [InlineData("opportunity")]
    [InlineData("lead")]
    [InlineData("")]
    public async Task QueryEntitiesAsync_UnsupportedEntityType_ReturnsErrorString_NotException(string entityType)
    {
        // Arrange — HttpClient will never be called for unsupported types
        var (sut, handler) = BuildSut(HttpStatusCode.OK, "{}");

        if (string.IsNullOrEmpty(entityType))
        {
            // ArgumentException for empty string is acceptable (null guard)
            await Assert.ThrowsAsync<ArgumentException>(
                () => sut.QueryEntitiesAsync(entityType));
            return;
        }

        // Act
        var result = await sut.QueryEntitiesAsync(entityType);

        // Assert — returns descriptive error, no exception, no HTTP call
        result.Should().Contain("Unsupported entity type");
        result.Should().Contain(entityType);
        result.Should().Contain("sprk_matter"); // mentions allowed types
        handler.CallCount.Should().Be(0, "unsupported types must be rejected before any HTTP call");
    }

    // === QueryEntitiesAsync: top parameter capping ===

    [Fact]
    public async Task QueryEntitiesAsync_TopExceeds50_IsSilentlyCappedAt50()
    {
        // Arrange
        var json = """{ "value": [] }""";
        var (sut, handler) = BuildSut(HttpStatusCode.OK, json);

        // Act
        await sut.QueryEntitiesAsync("contact", top: 999);

        // Assert — URL must use $top=50
        handler.LastRequestUri.Should().Contain("$top=50");
        handler.LastRequestUri.Should().NotContain("$top=999");
    }

    [Fact]
    public async Task QueryEntitiesAsync_Top30_UsesExactValue()
    {
        // Arrange
        var json = """{ "value": [] }""";
        var (sut, handler) = BuildSut(HttpStatusCode.OK, json);

        // Act
        await sut.QueryEntitiesAsync("account", top: 30);

        // Assert
        handler.LastRequestUri.Should().Contain("$top=30");
    }

    [Fact]
    public async Task QueryEntitiesAsync_DefaultTop_Uses10()
    {
        // Arrange
        var json = """{ "value": [] }""";
        var (sut, handler) = BuildSut(HttpStatusCode.OK, json);

        // Act
        await sut.QueryEntitiesAsync("sprk_project");

        // Assert — default is 10
        handler.LastRequestUri.Should().Contain("$top=10");
    }

    [Fact]
    public async Task QueryEntitiesAsync_TopExactly50_IsAccepted()
    {
        // Arrange
        var json = """{ "value": [] }""";
        var (sut, handler) = BuildSut(HttpStatusCode.OK, json);

        // Act
        await sut.QueryEntitiesAsync("account", top: 50);

        // Assert — exactly 50 is within the cap
        handler.LastRequestUri.Should().Contain("$top=50");
    }

    // === QueryEntitiesAsync: filter forwarding ===

    [Fact]
    public async Task QueryEntitiesAsync_WithFilter_AppendsFilterToUrl()
    {
        // Arrange
        var json = """{ "value": [] }""";
        var (sut, handler) = BuildSut(HttpStatusCode.OK, json);

        // Act
        await sut.QueryEntitiesAsync("contact", filter: "contains(fullname,'Smith')");

        // Assert — filter appears URL-encoded in the query string
        handler.LastRequestUri.Should().Contain("$filter=");
        handler.LastRequestUri.Should().Contain("Smith");
    }

    [Fact]
    public async Task QueryEntitiesAsync_WithoutFilter_OmitsFilterParam()
    {
        // Arrange
        var json = """{ "value": [] }""";
        var (sut, handler) = BuildSut(HttpStatusCode.OK, json);

        // Act
        await sut.QueryEntitiesAsync("account");

        // Assert
        handler.LastRequestUri.Should().NotContain("$filter=");
    }

    // === QueryEntitiesAsync: auth and HTTP errors ===

    [Fact]
    public async Task QueryEntitiesAsync_NullHttpContext_ReturnsAuthUnavailableError()
    {
        // Arrange — no HTTP context
        var sut = BuildSutNoContext();

        // Act
        var result = await sut.QueryEntitiesAsync("sprk_matter");

        // Assert
        result.Should().Contain("unavailable");
        result.Should().Contain("authorization");
    }

    [Fact]
    public async Task QueryEntitiesAsync_DataverseReturns401_ReturnsAccessDeniedMessage()
    {
        // Arrange
        var (sut, _) = BuildSut(HttpStatusCode.Unauthorized, string.Empty);

        // Act
        var result = await sut.QueryEntitiesAsync("contact");

        // Assert
        result.Should().Contain("Access denied");
        result.Should().Contain("contact");
    }

    [Fact]
    public async Task QueryEntitiesAsync_DataverseReturns403_ReturnsAccessDeniedMessage()
    {
        // Arrange
        var (sut, _) = BuildSut(HttpStatusCode.Forbidden, string.Empty);

        // Act
        var result = await sut.QueryEntitiesAsync("account");

        // Assert
        result.Should().Contain("Access denied");
        result.Should().Contain("account");
    }

    [Fact]
    public async Task QueryEntitiesAsync_DataverseReturns500_ReturnsErrorMessage()
    {
        // Arrange
        var (sut, _) = BuildSut(HttpStatusCode.InternalServerError, string.Empty);

        // Act
        var result = await sut.QueryEntitiesAsync("sprk_project");

        // Assert
        result.Should().Contain("error");
        result.Should().Contain("500");
    }

    // === GetEntityDetailAsync: full field map ===

    [Fact]
    public async Task GetEntityDetailAsync_ValidRecord_ReturnsFullFieldMap()
    {
        // Arrange
        var id   = Guid.NewGuid();
        var json = $$"""
            {
              "sprk_matterid": "{{id}}",
              "sprk_name":     "Commercial Lease",
              "sprk_status":   "Active",
              "sprk_mattertype": "Contract",
              "sprk_clientid": "client-99"
            }
            """;

        var (sut, _) = BuildSut(HttpStatusCode.OK, json);

        // Act
        var result = await sut.GetEntityDetailAsync("sprk_matter", id.ToString());

        // Assert — all non-system fields appear
        result.Should().Contain("Commercial Lease");
        result.Should().Contain("Active");
        result.Should().Contain("Contract");
        result.Should().Contain("client-99");
        result.Should().Contain(id.ToString());
    }

    [Fact]
    public async Task GetEntityDetailAsync_ODataAnnotations_AreExcludedFromOutput()
    {
        // Arrange
        var id   = Guid.NewGuid();
        var json = $$"""
            {
              "contactid": "{{id}}",
              "fullname":  "Jane Smith",
              "@odata.etag": "W/\"12345\"",
              "_parentcustomerid_value": "some-guid",
              "emailaddress1": "jane@example.com"
            }
            """;

        var (sut, _) = BuildSut(HttpStatusCode.OK, json);

        // Act
        var result = await sut.GetEntityDetailAsync("contact", id.ToString());

        // Assert — OData annotations and underscore-prefixed fields suppressed
        result.Should().Contain("Jane Smith");
        result.Should().Contain("jane@example.com");
        result.Should().NotContain("@odata.etag");
        result.Should().NotContain("_parentcustomerid_value");
    }

    // === GetEntityDetailAsync: not found ===

    [Fact]
    public async Task GetEntityDetailAsync_RecordNotFound_ReturnsNotFoundMessage_NotException()
    {
        // Arrange
        var id = Guid.NewGuid();
        var (sut, _) = BuildSut(HttpStatusCode.NotFound, string.Empty);

        // Act
        var result = await sut.GetEntityDetailAsync("sprk_matter", id.ToString());

        // Assert — descriptive not-found string, not an exception
        result.Should().Contain("No 'sprk_matter' record found");
        result.Should().Contain(id.ToString());
    }

    // === GetEntityDetailAsync: invalid entity type ===

    [Fact]
    public async Task GetEntityDetailAsync_UnsupportedEntityType_ReturnsErrorString_NotException()
    {
        // Arrange
        var (sut, handler) = BuildSut(HttpStatusCode.OK, "{}");

        // Act
        var result = await sut.GetEntityDetailAsync("opportunity", Guid.NewGuid().ToString());

        // Assert
        result.Should().Contain("Unsupported entity type");
        result.Should().Contain("opportunity");
        handler.CallCount.Should().Be(0);
    }

    // === GetEntityDetailAsync: invalid GUID ===

    [Fact]
    public async Task GetEntityDetailAsync_InvalidGuid_ReturnsFormatErrorMessage_NotException()
    {
        // Arrange
        var (sut, handler) = BuildSut(HttpStatusCode.OK, "{}");

        // Act
        var result = await sut.GetEntityDetailAsync("contact", "not-a-guid");

        // Assert — validation error before any HTTP call
        result.Should().Contain("Invalid identifier format");
        result.Should().Contain("not-a-guid");
        handler.CallCount.Should().Be(0, "invalid GUID must be rejected before HTTP call");
    }

    // === GetEntityDetailAsync: auth errors ===

    [Fact]
    public async Task GetEntityDetailAsync_NullHttpContext_ReturnsAuthUnavailableError()
    {
        // Arrange
        var sut = BuildSutNoContext();

        // Act
        var result = await sut.GetEntityDetailAsync("contact", Guid.NewGuid().ToString());

        // Assert
        result.Should().Contain("unavailable");
        result.Should().Contain("authorization");
    }

    [Fact]
    public async Task GetEntityDetailAsync_DataverseReturns403_ReturnsAccessDeniedMessage()
    {
        // Arrange
        var (sut, _) = BuildSut(HttpStatusCode.Forbidden, string.Empty);

        // Act
        var result = await sut.GetEntityDetailAsync("sprk_document", Guid.NewGuid().ToString());

        // Assert
        result.Should().Contain("Access denied");
    }

    // === OBO bearer token forwarding ===

    [Fact]
    public async Task QueryEntitiesAsync_ForwardsCallerBearerToken_ToDataverseRequest()
    {
        // Arrange
        var json = """{ "value": [] }""";
        var (sut, handler) = BuildSut(HttpStatusCode.OK, json);

        // Act
        await sut.QueryEntitiesAsync("account");

        // Assert — token from HttpContext Authorization header forwarded to Dataverse
        handler.LastAuthorizationHeader.Should().Be($"Bearer {TestBearerToken}");
    }

    [Fact]
    public async Task GetEntityDetailAsync_ForwardsCallerBearerToken_ToDataverseRequest()
    {
        // Arrange
        var id   = Guid.NewGuid();
        var json = $"{{ \"accountid\": \"{id}\", \"name\": \"Acme\" }}";
        var (sut, handler) = BuildSut(HttpStatusCode.OK, json);

        // Act
        await sut.GetEntityDetailAsync("account", id.ToString());

        // Assert
        handler.LastAuthorizationHeader.Should().Be($"Bearer {TestBearerToken}");
    }

    // === Constructor guard ===

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenHttpClientIsNull()
    {
        var options = Options.Create(new DataverseOptions { EnvironmentUrl = TestEnvironmentUrl, ClientId = "x", ClientSecret = "x", TenantId = "x" });
        var action  = () => new DataverseQueryTools(null!, options, null, NullLogger<DataverseQueryTools>.Instance);
        action.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenDataverseOptionsIsNull()
    {
        var action = () => new DataverseQueryTools(new HttpClient(), null!, null, NullLogger<DataverseQueryTools>.Instance);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        var options = Options.Create(new DataverseOptions { EnvironmentUrl = TestEnvironmentUrl, ClientId = "x", ClientSecret = "x", TenantId = "x" });
        var action  = () => new DataverseQueryTools(new HttpClient(), options, null, null!);
        action.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // === Private helpers ===

    /// <summary>
    /// Builds a <see cref="DataverseQueryTools"/> with a fake HTTP handler and a real HttpContext
    /// carrying a Bearer token. Returns the SUT and the handler for call inspection.
    /// </summary>
    private (DataverseQueryTools Sut, FakeHttpHandler Handler) BuildSut(
        HttpStatusCode statusCode,
        string responseBody)
    {
        var handler    = new FakeHttpHandler(statusCode, responseBody);
        var httpClient = new HttpClient(handler);
        var options    = Options.Create(new DataverseOptions
        {
            EnvironmentUrl = TestEnvironmentUrl,
            ClientId       = "test-client-id",
            ClientSecret   = "test-secret",
            TenantId       = "test-tenant"
        });
        var httpContext = BuildHttpContext(TestBearerToken);
        var sut        = new DataverseQueryTools(httpClient, options, httpContext, NullLogger<DataverseQueryTools>.Instance);
        return (sut, handler);
    }

    /// <summary>
    /// Builds a SUT with no HttpContext — simulates background processing or missing auth.
    /// </summary>
    private DataverseQueryTools BuildSutNoContext()
    {
        var handler    = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler);
        var options    = Options.Create(new DataverseOptions
        {
            EnvironmentUrl = TestEnvironmentUrl,
            ClientId       = "test-client-id",
            ClientSecret   = "test-secret",
            TenantId       = "test-tenant"
        });
        return new DataverseQueryTools(httpClient, options, httpContext: null, NullLogger<DataverseQueryTools>.Instance);
    }

    private static HttpContext BuildHttpContext(string bearerToken)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {bearerToken}";
        return context;
    }

    private static string BuildODataCollectionResponse(string entityType, int count)
    {
        var idField   = entityType switch
        {
            "sprk_matter"   => "sprk_matterid",
            "sprk_project"  => "sprk_projectid",
            "contact"       => "contactid",
            "account"       => "accountid",
            "sprk_document" => "sprk_documentid",
            _               => "id"
        };
        var nameField = entityType switch
        {
            "contact" => "fullname",
            "account" => "name",
            _         => "sprk_name"
        };

        var records = Enumerable.Range(1, count).Select(i =>
            $"{{ \"{idField}\": \"{Guid.NewGuid()}\", \"{nameField}\": \"Record {i}\" }}");

        return $"{{ \"value\": [{string.Join(",", records)}] }}";
    }

    // === Fake HTTP handler ===

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public string LastRequestUri  { get; private set; } = string.Empty;
        public string LastAuthorizationHeader { get; private set; } = string.Empty;
        public int    CallCount       { get; private set; }

        public FakeHttpHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode   = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri           = request.RequestUri?.ToString() ?? string.Empty;
            LastAuthorizationHeader  = request.Headers.Authorization?.ToString() ?? string.Empty;

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
