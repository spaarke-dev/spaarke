// ISS-005 (#1673 UAC round 3, K1): a failed DataverseWebApiClient.CreateAsync keeps the HTTP status AND the Dataverse
// error CODE (never the message), so a caller can tell 0x80040299 (the owner team cannot own the row) from any other 403.

using System.Net;
using System.Reflection;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

public class DataverseWebApiClientCreateFailureTests
{
    private const string MessageWithValues = "A record with the same email ZZ-SECRET-VALUE@example.test already exists. Read Privilege Check For Owner failed.";

    private static DataverseWebApiClient ClientAnswering(HttpStatusCode status, string? body, string contentType = "application/json")
    {
        var client = new DataverseWebApiClient(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                .Build(),
            NullLogger<DataverseWebApiClient>.Instance,
            new FixedCredential());
        var http = new HttpClient(new Stub(status, body, contentType)) { BaseAddress = new Uri("https://test.crm.dynamics.com/api/data/v9.2/") };
        typeof(DataverseWebApiClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, http);
        return client;
    }

    private static async Task<HttpRequestException> CreateFailure(HttpStatusCode status, string? body, string contentType = "application/json")
    {
        var act = () => ClientAnswering(status, body, contentType).CreateAsync("contacts", new { lastname = "x" });
        return (await act.Should().ThrowAsync<HttpRequestException>()).Which;
    }

    private static string ErrorBody(string code, string message) => $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"{message}\"}}}}";

    [Fact]
    public async Task A403WithTheOwnerPrivilegeCode_CarriesTheStatusAndTheCode()
    {
        var ex = await CreateFailure(HttpStatusCode.Forbidden, ErrorBody("0x80040299", MessageWithValues));

        ex.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        DataverseWebApiClient.ErrorCodeOf(ex).Should().Be("0x80040299");
        ex.Data[DataverseWebApiClient.ErrorCodeDataKey].Should().Be("0x80040299");
    }

    [Fact]
    public async Task ABare403Code0x80040220_CarriesItsOwnCode_NotTheOwnerCode()
    {
        var ex = await CreateFailure(HttpStatusCode.Forbidden, ErrorBody("0x80040220", "Principal user is missing prvCreateContact privilege"));

        ex.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        DataverseWebApiClient.ErrorCodeOf(ex).Should().Be("0x80040220");
    }

    [Fact]
    public async Task TheDataverseMessage_IsNeverKept_NotInTheMessageNorInData()
    {
        var ex = await CreateFailure(HttpStatusCode.PreconditionFailed, ErrorBody("0x80040237", MessageWithValues));

        ex.Message.Should().NotContain("ZZ-SECRET-VALUE").And.NotContain("Privilege Check");
        ex.ToString().Should().NotContain("ZZ-SECRET-VALUE");
        ex.Data.Values.Cast<object?>().Select(v => v?.ToString()).Should().OnlyContain(v => v == "0x80040237");
    }

    [Fact]
    public async Task TheMessageKeepsTheShapeEnsureSuccessStatusCodeGave()
    {
        var ex = await CreateFailure(HttpStatusCode.Forbidden, ErrorBody("0x80040299", "x"));

        ex.Message.Should().Be("Response status code does not indicate success: 403 (Forbidden).");
    }

    [Theory]
    [InlineData("not json at all", "text/plain")]
    [InlineData("", "application/json")]
    [InlineData("[1,2,3]", "application/json")]
    [InlineData("{\"error\":\"flat\"}", "application/json")]
    [InlineData("{\"error\":{\"code\":403,\"message\":\"m\"}}", "application/json")]
    [InlineData("{\"error\":{\"code\":\"free text with spaces and ZZ-SECRET-VALUE\",\"message\":\"m\"}}", "application/json")]
    [InlineData("{\"error\":{\"code\":\"0x8004029\",\"message\":\"m\"}}", "application/json")]
    [InlineData("{\"error\":{\"code\":\"0x80040G99\",\"message\":\"m\"}}", "application/json")]
    public async Task AnUnreadableOrUnexpectedBody_LeavesNoCode_ButStillThrowsTheStatus(string body, string contentType)
    {
        var ex = await CreateFailure(HttpStatusCode.Forbidden, body, contentType);

        ex.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        DataverseWebApiClient.ErrorCodeOf(ex).Should().BeNull();
        ex.Data.Count.Should().Be(0);
    }

    [Fact]
    public void ErrorCodeOf_ReadsAnInnerException_AndNullIsNull()
    {
        var inner = new HttpRequestException("x");
        inner.Data[DataverseWebApiClient.ErrorCodeDataKey] = "0x80040299";

        DataverseWebApiClient.ErrorCodeOf(new InvalidOperationException("wrap", inner)).Should().Be("0x80040299");
        DataverseWebApiClient.ErrorCodeOf(null).Should().BeNull();
        DataverseWebApiClient.ErrorCodeOf(new HttpRequestException("none")).Should().BeNull();
    }

    [Fact]
    public async Task ASuccessfulCreate_StillReturnsTheIdFromTheEntityIdHeader()
    {
        var id = Guid.NewGuid();
        var http = new HttpClient(new Stub(HttpStatusCode.NoContent, null, "application/json", $"https://test.crm.dynamics.com/api/data/v9.2/contacts({id})"))
        {
            BaseAddress = new Uri("https://test.crm.dynamics.com/api/data/v9.2/")
        };
        var client = ClientAnswering(HttpStatusCode.NoContent, null);
        typeof(DataverseWebApiClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, http);

        (await client.CreateAsync("contacts", new { lastname = "x" })).Should().Be(id);
    }

    private sealed class Stub(HttpStatusCode status, string? body, string contentType, string? entityId = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status);
            if (body is not null)
            {
                response.Content = new StringContent(body, Encoding.UTF8, contentType);
            }

            if (entityId is not null)
            {
                response.Headers.TryAddWithoutValidation("OData-EntityId", entityId);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
