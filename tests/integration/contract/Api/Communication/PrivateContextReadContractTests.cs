using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056b: the wire contract of the per-viewer private-message read, through the real auth
/// pipeline, filters and read service (<see cref="CommunicationRecordAuthorizationHost"/>).
/// <list type="bullet">
///   <item><c>GET /api/communications/threads?kind=direct&amp;about={table}:{id}</c>: a malformed <c>about</c>, or <c>about</c> with
///   any kind other than <c>direct</c>, is a 400 before any read; a well-formed one is a 200 in the 055 list shape.</item>
///   <item><c>GET /api/communications/threads/{id}/messages</c>: a malformed <c>about</c> is a 400; each message carries
///   <c>privateContext</c> as <c>{ entityType, id, name }</c> or <c>null</c>.</item>
/// </list>
/// </summary>
public class PrivateContextReadContractTests : IClassFixture<CommunicationRecordAuthorizationHost>
{
    private static readonly Guid Matter = Guid.Parse("05605600-0000-0000-0000-0000000000a1");

    private readonly CommunicationRecordAuthorizationHost _host;

    public PrivateContextReadContractTests(CommunicationRecordAuthorizationHost host)
    {
        _host = host;
        _host.Reset();
        // The schema scripts have run: sprk_communication has the reference and stamped-recipient columns.
        _host.Entities
            .Setup(e => e.GetColumnMetadataAsync("sprk_communication", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivateContextColumns.All.Append(DirectMessageRecipient.RecipientColumn)
                .ToDictionary(c => c, c => new ColumnMetadata(c, "Lookup", true, true, null), StringComparer.OrdinalIgnoreCase));
    }

    private Task<HttpResponseMessage> Get(string url) =>
        _host.SendAsync(CommunicationRecordAuthorizationHost.Request(HttpMethod.Get, url));

    [Theory]
    [InlineData("?kind=direct&about=garbage")]
    [InlineData("?kind=direct&about=sprk_invoice:05605600-0000-0000-0000-0000000000a1")]
    [InlineData("?kind=direct&about=sprk_matter:not-a-guid")]
    [InlineData("?about=sprk_matter:05605600-0000-0000-0000-0000000000a1")]
    [InlineData("?kind=all&about=sprk_matter:05605600-0000-0000-0000-0000000000a1")]
    public async Task ListThreads_AMalformedAboutOrAboutWithoutKindDirect_Returns400_BeforeAnyRead(string query)
    {
        var response = await Get($"/api/communications/threads{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _host.Query.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ListThreads_KindDirectWithAWellFormedAbout_Returns200_InThe055ListShape()
    {
        var response = await Get($"/api/communications/threads?kind=direct&about=sprk_matter:{Matter}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("threads").GetArrayLength().Should().Be(0);
        body.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        _host.Query.Calls.Should().Contain(c => c.Set == "sprk_communications"
            && c.OData!.Contains($"_{PrivateContextColumns.Matter}_value eq {Matter}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("sprk_matter:")]
    [InlineData("")]
    public async Task ReadThreadMessages_AMalformedAbout_Returns400_BeforeAnyRead(string about)
    {
        var response = await Get($"/api/communications/threads/{Guid.NewGuid()}/messages?about={about}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _host.Query.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadThreadMessages_EachMessageCarriesPrivateContext_AsEntityTypeIdAndName()
    {
        var thread = Guid.NewGuid();
        // The fake answers a query by the first id in its filter: the thread read names the thread, the record read the matter.
        _host.Query.Visible("sprk_communications", thread, JsonSerializer.Serialize(MessageRow(Guid.NewGuid(), thread, Matter)));
        _host.Query.Visible("sprk_matters", Matter, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["sprk_matterid"] = Matter.ToString(),
            ["sprk_mattername"] = "Acme v. Widget",
        }));

        var response = await Get($"/api/communications/threads/{thread}/messages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var message = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("messages")[0];
        var context = message.GetProperty("privateContext");
        context.GetProperty("entityType").GetString().Should().Be("sprk_matter");
        context.GetProperty("id").GetGuid().Should().Be(Matter);
        context.GetProperty("name").GetString().Should().Be("Acme v. Widget");
        _host.Query.Calls.Should().Contain(c => c.Set == "sprk_matters" && c.Caller == CommunicationRecordAuthorizationHost.CallerSystemUserId);
    }

    [Fact]
    public async Task ReadThreadMessages_AMessageAboutNoRecord_CarriesPrivateContextNull()
    {
        var thread = Guid.NewGuid();
        _host.Query.Visible("sprk_communications", thread, JsonSerializer.Serialize(MessageRow(Guid.NewGuid(), thread, matter: null)));

        var response = await Get($"/api/communications/threads/{thread}/messages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var message = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("messages")[0];
        message.GetProperty("privateContext").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static Dictionary<string, object?> MessageRow(Guid id, Guid thread, Guid? matter)
    {
        var row = new Dictionary<string, object?>
        {
            ["sprk_communicationid"] = id.ToString(),
            ["_sprk_communicationthread_value"] = thread.ToString(),
            ["_sprk_sentby_value"] = CommunicationRecordAuthorizationHost.CallerSystemUserId.ToString(),
            ["_sprk_directrecipient_value"] = Guid.NewGuid().ToString(),
            ["sprk_isinternalonly"] = true,
            ["createdon"] = "2026-11-22T10:00:00.000Z",
            [$"_{PrivateContextColumns.Matter}_value"] = matter?.ToString(),
            [$"_{PrivateContextColumns.Project}_value"] = null,
            [$"_{PrivateContextColumns.WorkAssignment}_value"] = null,
        };
        foreach (var field in RegardingFieldMap.AllRegardingFields)
            row[$"_{field}_value"] = null;
        return row;
    }
}
