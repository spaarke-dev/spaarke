// #2852 review (F2): DataverseWebApiClient.QueryStrictAsync refuses a 2xx collection body without a value array, so the grant
// core's root-row read (ExternalGrantLifecycle.QueryActiveRowsForRootAsync) can never turn a malformed answer into "no rows".
// The lenient QueryAsync keeps its behaviour exactly. Same seam as DataverseWebApiClientCreateFailureTests: the real client
// over its own HttpClient, answered at the transport.

using System.Net;
using System.Reflection;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

public class DataverseWebApiClientStrictQueryTests
{
    private static readonly Guid Root = Guid.Parse("28520000-0000-0000-0000-0000000000a1");
    private static readonly Guid Contact = Guid.Parse("28520000-0000-0000-0000-0000000000c1");

    private static DataverseWebApiClient ClientAnswering(string body)
    {
        var client = new DataverseWebApiClient(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                .Build(),
            NullLogger<DataverseWebApiClient>.Instance,
            new FixedCredential());
        var http = new HttpClient(new Stub(body)) { BaseAddress = new Uri("https://test.crm.dynamics.com/api/data/v9.2/") };
        typeof(DataverseWebApiClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, http);
        return client;
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"value\":null}")]
    public async Task QueryStrict_ASuccessWithoutAValueArray_Throws_WithAFixedMessage(string body)
    {
        var act = () => ClientAnswering(body).QueryStrictAsync<Row>("contacts", filter: "statecode eq 0");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Be("The Dataverse read succeeded without a value array.");
    }

    [Fact]
    public async Task QueryStrict_AnEmptyValueArray_IsNoRows_AndRowsAreRead()
    {
        (await ClientAnswering("{\"value\":[]}").QueryStrictAsync<Row>("contacts")).Should().BeEmpty();

        var rows = await ClientAnswering($"{{\"value\":[{{\"contactid\":\"{Contact}\"}}]}}").QueryStrictAsync<Row>("contacts");
        rows.Should().ContainSingle().Which.contactid.Should().Be(Contact);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"value\":null}")]
    [InlineData("{\"value\":[]}")]
    public async Task Query_TheLenientRead_IsUnchanged_AMissingOrNullValueIsStillNoRows(string body)
    {
        (await ClientAnswering(body).QueryAsync<Row>("contacts")).Should().NotBeNull().And.BeEmpty();
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"value\":null}", true)]
    [InlineData("{\"value\":[]}", false)]
    public async Task QueryActiveRowsForRoot_ThrowsOnAMissingOrNullValue_NeverOnAnEmptyArray(string body, bool throws)
    {
        var act = () => ExternalGrantLifecycle.QueryActiveRowsForRootAsync(
            ClientAnswering(body), ExternalGrantRootType.Matter, Root, top: 10, CancellationToken.None);

        if (throws)
            await act.Should().ThrowAsync<InvalidOperationException>("a 2xx without a value array is never \"no grant rows\"");
        else
            (await act()).Should().BeEmpty("control: an empty array is Dataverse's answer \"no grant rows\"");
    }

    private sealed class Row
    {
        public Guid? contactid { get; set; }
    }

    private sealed class Stub(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
