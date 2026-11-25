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

    // The 401 code and type OfficeAuthFilter answers an unusable sign-in with (#1571 review F4: OFFICE_009 is the 403).
    private const string SignInNotUsable = "OFFICE_AUTH_001";
    private const string SignInNotUsableType = "https://tools.ietf.org/html/rfc7235#section-3.1";
    private const string ForbiddenType = "https://spaarke.com/errors/office/forbidden";

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
        problem.GetProperty("errorCode").GetString().Should().Be(SignInNotUsable);
        problem.GetProperty("type").GetString().Should().Be(SignInNotUsableType);
        problem.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task OboExchangeTheCallerMustSignInAgainFor_Answers401WithOfficeErrorCode_Not404Or500(string route)
    {
        // Status 0: the request never reached Dataverse — Entra refused the On-Behalf-Of exchange with
        // MsalUiRequiredException for the caller's own grant (expired, revoked), which a fresh sign-in fixes.
        var (url, fragment) = Request(route);
        FailUserQuery(fragment, DataverseUserResponse.Fail(
            0, DataverseUserClientErrorCodes.OboExchangeFailed, "invalid_grant") with
        { IsCallerFixable = true });

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemOf(response)).GetProperty("errorCode").GetString().Should().Be(SignInNotUsable);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task OboExchangeRefusedByPolicy_Answers403WithOfficeErrorCode_NeverSignInAgain(string route)
    {
        // #1571 fix round 2: admin consent missing, Conditional Access (MessageOnly) or a claims challenge. MSAL.NET
        // raises MsalUiRequiredException for these too, but signing in again cannot fix them.
        var (url, fragment) = Request(route);
        FailUserQuery(fragment, DataverseUserResponse.Fail(
            0, DataverseUserClientErrorCodes.OboExchangeFailed, "invalid_grant") with
        { IsRefusedByPolicy = true });

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await ProblemOf(response);
        problem.GetProperty("errorCode").GetString().Should().Be(OfficeAccessDenied);
        problem.GetProperty("type").GetString().Should().Be(ForbiddenType);
        var detail = problem.GetProperty("detail").GetString();
        detail.Should().Contain("sign-in policy").And.Contain("administrator");
        detail.Should().NotContainEquivalentOf("sign in again");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task OboExchangeFailingForTheServersOwnReasons_Answers500_NotASignInProblem(string route)
    {
        // #1571 review F1: the BFF's own credential rejected (invalid_client, a federated-credential mismatch), an
        // Entra timeout or temporarily_unavailable. Signing in again cannot fix any of them, so it is not a 401.
        var (url, fragment) = Request(route);
        FailUserQuery(fragment, DataverseUserResponse.Fail(
            0, DataverseUserClientErrorCodes.OboExchangeFailed, "invalid_client"));

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ProblemOf(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_INTERNAL");
    }

    public static TheoryData<string, int, string, HttpStatusCode> ServerFaults
    {
        get
        {
            var data = new TheoryData<string, int, string, HttpStatusCode>();
            foreach (var route in new[] { "lookup", "suggestions", "linked-todos" })
            {
                data.Add(route, 429, DataverseUserClientErrorCodes.RateLimited, HttpStatusCode.ServiceUnavailable);
                data.Add(route, 503, DataverseUserClientErrorCodes.ServiceError, HttpStatusCode.ServiceUnavailable);
                data.Add(route, 500, DataverseUserClientErrorCodes.ServiceError, HttpStatusCode.InternalServerError);
                // Status 0 + ServiceError: the transport failed before Dataverse answered.
                data.Add(route, 0, DataverseUserClientErrorCodes.ServiceError, HttpStatusCode.InternalServerError);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ServerFaults))]
    public async Task AFailedRead_IsTheServersFault_NeverNotFoundOrAnEmptyList(
        string route, int dataverseStatus, string clientErrorCode, HttpStatusCode expected)
    {
        // #1571 review F2: throttling, Dataverse 5xx and transport failures used to read as "email not captured"
        // (lookup/suggestions 404) or "no linked to-dos" (an empty 200).
        var (url, fragment) = Request(route);
        FailUserQuery(fragment, DataverseUserResponse.Fail(dataverseStatus, clientErrorCode, "Dataverse failed."));

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(expected);
        (await ProblemOf(response)).GetProperty("errorCode").GetString().Should().Be("OFFICE_INTERNAL");
    }

    [Fact]
    public async Task LinkedTodos_TheCallerCanReadNone_StaysAnEmpty200()
    {
        // The non-oracle rule for this route: an unreadable or absent to-do is left out of the page, not refused.
        var (url, fragment) = Request("linked-todos");
        _factory.SetupUserQuery(fragment, "{\"value\":[]}");

        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("count").GetInt32().Should().Be(0);
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
        (await ProblemOf(response)).GetProperty("errorCode").GetString().Should().Be(SignInNotUsable);
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
        problem.GetProperty("type").GetString().Should().Be(ForbiddenType);
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
