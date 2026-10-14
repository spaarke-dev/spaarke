// -----------------------------------------------------------------------------
// DataverseWebApiCommunicationAccountStoreTests.cs
//
// Task 263: the HTTP half of the sprk_communicationaccount store (CommunicationAccountWebApi) against a hand-written
// HttpMessageHandler (ADR-038 — never Mock<HttpMessageHandler>). Pins the get-before-set rules:
//   none → one POST with the documented fields; one Verified → NOTHING written (a second run writes nothing);
//   one Pending / empty → PATCH (If-Match: *) of the three verification fields only; one Failed → VerificationFailed,
//   nothing written; inactive / another type / several → Conflict, nothing written; HTTP error → Failure, never thrown.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class DataverseWebApiCommunicationAccountStoreTests
{
    private static readonly Uri Env = new("https://spaarke-acme.crm.dynamics.com/");
    private static readonly Guid RowId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly CommunicationAccountSpec Spec = new(
        Name: "Acme Corporation", EmailAddress: "acme@contoso.com", DisplayName: "Acme Corporation",
        SecurityGroupId: "77777777-8888-9999-0000-111111111111", VerificationMessage: "Verified by provisioning (H14m)");

    [Fact]
    public async Task NoRow_PostsOneSharedVerifiedDefaultSenderRow()
    {
        var handler = new DataverseHandler(Rows());

        var outcome = await Api(handler).EnsureVerifiedAsync(Spec, Now, CancellationToken.None);

        outcome.Should().Be(new CommunicationAccountEnsureOutcome.Ready(RowId, Written: true));
        handler.Requests.Select(r => r.Method).Should().Equal(HttpMethod.Get, HttpMethod.Post);
        handler.Requests[0].Uri.Should().Contain("$filter=sprk_emailaddress eq 'acme%40contoso.com'");
        handler.Requests[0].Authorization.Should().Be("Bearer tok");
        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        var r = body.RootElement;
        r.GetProperty("sprk_name").GetString().Should().Be("Acme Corporation");
        r.GetProperty("sprk_emailaddress").GetString().Should().Be("acme@contoso.com");
        r.GetProperty("sprk_accounttype").GetInt32().Should().Be(CommunicationAccountValues.SharedAccount);
        r.GetProperty("sprk_authmethod").GetInt32().Should().Be(CommunicationAccountValues.AppOnly);
        r.GetProperty("sprk_sendenabled").GetBoolean().Should().BeTrue();
        r.GetProperty("sprk_receiveenabled").GetBoolean().Should().BeTrue();
        r.GetProperty("sprk_isdefaultsender").GetBoolean().Should().BeTrue();
        r.GetProperty("sprk_securitygroupid").GetString().Should().Be(Spec.SecurityGroupId);
        r.GetProperty("sprk_verificationstatus").GetInt32().Should().Be(CommunicationAccountValues.Verified);
        r.GetProperty("sprk_verificationmessage").GetString().Should().Be(Spec.VerificationMessage);
    }

    [Fact]
    public async Task OneVerifiedRow_WritesNothing()
    {
        var handler = new DataverseHandler(Rows(Row(status: CommunicationAccountValues.Verified)));

        var outcome = await Api(handler).EnsureVerifiedAsync(Spec, Now, CancellationToken.None);

        outcome.Should().Be(new CommunicationAccountEnsureOutcome.Ready(RowId, Written: false));
        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
    }

    [Theory]
    [InlineData(CommunicationAccountValues.Pending)]
    [InlineData(null)]
    public async Task OnePendingOrUnsetRow_PatchesOnlyTheVerificationFields(int? status)
    {
        var handler = new DataverseHandler(Rows(Row(status: status)));

        var outcome = await Api(handler).EnsureVerifiedAsync(Spec, Now, CancellationToken.None);

        outcome.Should().Be(new CommunicationAccountEnsureOutcome.Ready(RowId, Written: true));
        var patch = handler.Requests.Should().HaveCount(2).And.Subject.Last();
        patch.Method.Should().Be(HttpMethod.Patch);
        patch.Uri.Should().EndWith($"sprk_communicationaccounts({RowId})");
        patch.IfMatch.Should().Be("*", "update only — never an upsert");
        using var body = JsonDocument.Parse(patch.Body!);
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            new[] { "sprk_verificationstatus", "sprk_lastverified", "sprk_verificationmessage" },
            "operator flags on an adopted row (send / receive / default sender) are never rewritten");
    }

    [Fact]
    public async Task FailedVerification_IsReported_NeverOverwritten()
    {
        var handler = new DataverseHandler(Rows(Row(status: CommunicationAccountValues.Failed, message: "Send test failed")));

        var outcome = await Api(handler).EnsureVerifiedAsync(Spec, Now, CancellationToken.None);

        outcome.Should().Be(new CommunicationAccountEnsureOutcome.VerificationFailed(RowId, "Send test failed"));
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(1, CommunicationAccountValues.SharedAccount, "inactive")]
    [InlineData(0, 100000002, "not Shared Account")]
    public async Task InactiveOrOtherTypeRow_IsAConflict_NothingWritten(int state, int type, string expected)
    {
        var handler = new DataverseHandler(Rows(Row(state: state, type: type, status: CommunicationAccountValues.Verified)));

        var outcome = await Api(handler).EnsureVerifiedAsync(Spec, Now, CancellationToken.None);

        outcome.Should().BeOfType<CommunicationAccountEnsureOutcome.Conflict>().Which.Diagnostic.Should().Contain(expected);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SeveralRows_IsAConflict_NothingWritten()
    {
        var handler = new DataverseHandler(Rows(Row(), Row(id: Guid.NewGuid())));

        var outcome = await Api(handler).EnsureVerifiedAsync(Spec, Now, CancellationToken.None);

        outcome.Should().BeOfType<CommunicationAccountEnsureOutcome.Conflict>().Which.Diagnostic.Should().Contain("exactly one");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task HttpError_IsAFailure_NotAnException()
    {
        var handler = new DataverseHandler(Rows()) { GetStatus = HttpStatusCode.Forbidden };

        var ensure = await Api(handler).EnsureVerifiedAsync(Spec, Now, CancellationToken.None);
        var read = await Api(handler).ReadAsync("acme@contoso.com", CancellationToken.None);

        ensure.Should().BeOfType<CommunicationAccountEnsureOutcome.Failure>().Which.Diagnostic.Should().Contain("403");
        read.Should().BeOfType<CommunicationAccountReadOutcome.Failure>();
    }

    [Fact]
    public async Task Read_ReturnsEveryRowWithTheAddress_IncludingInactiveOnes()
    {
        var handler = new DataverseHandler(Rows(Row(state: 1), Row(id: Guid.NewGuid())));

        var read = await Api(handler).ReadAsync("acme@contoso.com", CancellationToken.None);

        read.Should().BeOfType<CommunicationAccountReadOutcome.Found>().Which.Rows.Select(r => r.StateCode).Should().Equal(1, 0);
        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task Address_WithAQuote_IsEscapedInTheFilter()
    {
        var handler = new DataverseHandler(Rows());

        await Api(handler).ReadAsync("o'brien@contoso.com", CancellationToken.None);

        handler.Requests[0].Uri.Should().Contain("eq 'o%27%27brien%40contoso.com'", "an OData literal doubles the quote, then it is URL-encoded");
    }

    // ---------- helpers ----------

    private static CommunicationAccountWebApi Api(DataverseHandler handler)
        => new(new HttpClient(handler), Env, _ => ValueTask.FromResult("tok"), NullLogger.Instance);

    private static string Row(Guid? id = null, int state = 0, int? type = CommunicationAccountValues.SharedAccount, int? status = CommunicationAccountValues.Verified, string? message = null)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["sprk_communicationaccountid"] = (id ?? RowId).ToString(),
            ["sprk_emailaddress"] = "acme@contoso.com",
            ["statecode"] = state,
            ["sprk_accounttype"] = type,
            ["sprk_verificationstatus"] = status,
            ["sprk_verificationmessage"] = message,
            ["sprk_sendenabled"] = true,
            ["sprk_receiveenabled"] = true,
        });

    private static string Rows(params string[] rows) => $$"""{ "value": [{{string.Join(",", rows)}}] }""";

    private sealed record Seen(HttpMethod Method, string Uri, string? Body, string? Authorization, string? IfMatch);

    /// <summary>GET answers the given rows; POST answers 204 with OData-EntityId; PATCH answers 204.</summary>
    private sealed class DataverseHandler(string getBody) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = new();
        public HttpStatusCode GetStatus { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Seen(request.Method, request.RequestUri!.OriginalString, body,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("If-Match", out var m) ? string.Join(",", m) : null));
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(GetStatus) { Content = new StringContent(getBody, Encoding.UTF8, "application/json") };
            }
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Post)
            {
                response.Headers.Add("OData-EntityId", $"{Env}api/data/v9.2/sprk_communicationaccounts({RowId})");
            }
            return response;
        }
    }
}
