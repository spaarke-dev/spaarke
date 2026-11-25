using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// GitHub #1571 (spaarkeai-word-add-in-r1 task 136): a broken DELEGATED context on the three
/// <c>/api/office/communications</c> routes answers 401 or 403 with an Office <c>errorCode</c>, never the generic 500
/// "Lookup Failed" — while a communication the caller cannot read stays the ordinary 404 (the existence-oracle rule of
/// task 127 / #1020), and a genuine fault stays a 500.
/// </summary>
/// <remarks>
/// The delegated client (<see cref="IDataverseUserClient"/>) is the module boundary these routes read through, so the
/// failures are programmed there exactly as <c>DataverseUserClient</c> reports them: Dataverse's own 401/403, or status 0
/// with the client's mapped code when the request never left the BFF (no caller token, a refused OBO exchange, missing
/// OBO configuration). Each test uses its own message id / communication id so the class's shared fixture cannot leak a
/// setup between tests.
/// </remarks>
public class CommunicationsDelegatedAuthFailureContractTests : IClassFixture<OfficeCommunicationsTestWebAppFactory>
{
    private const string OfficeAccessDenied = "OFFICE_009";

    private readonly OfficeCommunicationsTestWebAppFactory _factory;
    private readonly HttpClient _client;

    public CommunicationsDelegatedAuthFailureContractTests(OfficeCommunicationsTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public static TheoryData<string> Routes => new()
    {
        "lookup",
        "suggestions",
        "linked-todos",
    };

    /// <summary>Programs the delegated GET whose path contains <paramref name="fragment"/> to fail as given.</summary>
    private void FailUserQuery(string fragment, DataverseUserResponse failure)
        => _factory.UserClientMock
            .Setup(c => c.GetAsync(
                It.Is<string>(p => p.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failure);

    /// <summary>A fresh request for <paramref name="route"/> plus the fragment its delegated query path carries.</summary>
    private static (string Url, string Fragment) Request(string route)
    {
        switch (route)
        {
            case "linked-todos":
                {
                    var commId = Guid.NewGuid();
                    return ($"/api/office/communications/{commId}/linked-todos", commId.ToString());
                }
            default:
                {
                    var messageId = $"<{Guid.NewGuid():N}@contoso.com>";
                    var encoded = Uri.EscapeDataString(messageId);
                    var suffix = route == "suggestions" ? "/suggestions" : string.Empty;
                    return ($"/api/office/communications/by-message-id/{encoded}{suffix}", encoded);
                }
        }
    }

    private static async Task<JsonElement> ProblemOf(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task DataverseAnswering401_Answers401WithOfficeErrorCode_Not500(string route)
    {
        var (url, fragment) = Request(route);
        FailUserQuery(fragment, DataverseUserResponse.Fail(
            401, DataverseUserClientErrorCodes.AccessDenied, "The user is not authenticated."));

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await ProblemOf(response);
        problem.GetProperty("errorCode").GetString().Should().Be(OfficeAccessDenied);
        problem.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task OboExchangeRefused_Answers401WithOfficeErrorCode_Not404Or500(string route)
    {
        // Status 0: the request never reached Dataverse — the On-Behalf-Of exchange for the caller's token failed.
        var (url, fragment) = Request(route);
        FailUserQuery(fragment, DataverseUserResponse.Fail(
            0, DataverseUserClientErrorCodes.OboExchangeFailed, "AADSTS50013: assertion is not within its valid time range."));

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemOf(response)).GetProperty("errorCode").GetString().Should().Be(OfficeAccessDenied);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task NoCallerTokenForTheExchange_Answers401(string route)
    {
        var (url, fragment) = Request(route);
        FailUserQuery(fragment, DataverseUserResponse.Fail(
            0, DataverseUserClientErrorCodes.UserContextRequired, "No bearer token on the request."));

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemOf(response)).GetProperty("errorCode").GetString().Should().Be(OfficeAccessDenied);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task DataverseRefusingTheQueryWith403_Answers403WithOfficeErrorCode_Not500(string route)
    {
        // A 403 on a filtered QUERY is table- or identity-level (no read privilege, no Dataverse user): row-level
        // security answers by leaving rows out, never with a 403.
        var (url, fragment) = Request(route);
        FailUserQuery(fragment, DataverseUserResponse.Fail(
            403, DataverseUserClientErrorCodes.AccessDenied, "Principal user is missing prvReadsprk_communication privilege."));

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await ProblemOf(response);
        problem.GetProperty("errorCode").GetString().Should().Be(OfficeAccessDenied);
        // Fixed text: Dataverse's own message (privilege names, ids) never reaches the caller.
        problem.GetProperty("detail").GetString().Should().NotContain("prvRead");
    }

    [Theory]
    [InlineData("lookup")]
    [InlineData("suggestions")]
    public async Task ACommunicationTheCallerCannotRead_StaysAnOrdinary404_NeverA403(string route)
    {
        // The existence-oracle rule (task 127 / #1020): under the caller's context an unreadable row is simply absent
        // from the page, which is exactly what an uncaptured email looks like.
        var (url, fragment) = Request(route);
        _factory.SetupUserQuery(fragment, "{\"value\":[]}");

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemOf(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_COMM_NOT_FOUND");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AGenuineFault_StillAnswers500OfficeInternal(string route)
    {
        var (url, fragment) = Request(route);
        _factory.UserClientMock
            .Setup(c => c.GetAsync(
                It.Is<string>(p => p.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ProblemOf(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_INTERNAL");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task MissingOboConfiguration_IsTheServersFault_Answers500NotAnAuthFailure(string route)
    {
        var (url, fragment) = Request(route);
        FailUserQuery(fragment, DataverseUserResponse.Fail(
            0, DataverseUserClientErrorCodes.OboNotConfigured, "OBO client is not configured."));

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ProblemOf(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_INTERNAL");
    }
}
