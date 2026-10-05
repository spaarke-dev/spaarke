using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The grant ROW on the wire: the REAL <see cref="DataverseWebApiClient"/> and <see cref="ExternalGrantLifecycle"/> over a
/// real HTTP transport (a loopback ASP.NET Core server standing in for the Dataverse Web API — ADR-038 §7's replacement
/// for ban B1; this client builds its own <see cref="HttpClient"/>, so an in-memory test server cannot be injected and a
/// loopback port is used instead).
/// </summary>
/// <remarks>
/// <para><b>Why the wire</b> (unified-access-control-r2 task 140; fixed on master ahead of it). The grant tests run the
/// grant core over an in-memory table that serializes rows in the SHAPE THE TEST CHOOSES. Only the wire shows that
/// Dataverse returns <c>sprk_expiresdate</c> — DateOnly format, TimeZoneIndependent behaviour — as
/// <c>"2026-12-10T00:00:00Z"</c> (read live, read-only, spaarkedev1 2026-10-05), which System.Text.Json's own
/// <see cref="DateOnly"/> converter refuses, so every read of a dated grant row threw: the re-grant, <c>/revoke</c> and
/// <c>set-record-share-expiry</c>. The JSON served below is the live response, verbatim but for the ids.</para>
/// <para>KEEP path: <c>tests/integration/auth/**</c> (ADR-038 §2, security-auth).</para>
/// </remarks>
public sealed class GrantRowWireTests : IAsyncLifetime
{
    private static readonly Guid RowA = Guid.Parse("9ec4e19c-5aad-f111-aaab-000d3a9cc3c2");
    private static readonly Guid RowB = Guid.Parse("839c2aa7-88b6-f111-aaad-0022482913fc");
    private static readonly Guid Contact = Guid.Parse("c1400000-0000-0000-0000-0000000000c1");
    private static readonly Guid Project = Guid.Parse("14014014-0140-0140-0140-0140140140c1");

    /// <summary>The live response to a grant-row read (the BFF's Prefer header asks for formatted values too).</summary>
    private static readonly string LiveCollection =
        "{\"@odata.context\":\"https://spaarkedev1.crm.dynamics.com/api/data/v9.2/$metadata#sprk_externalrecordaccesses\"," +
        "\"value\":[" +
        $"{{\"@odata.etag\":\"W/\\\"25734128\\\"\",\"sprk_expiresdate@OData.Community.Display.V1.FormattedValue\":\"12/10/2026\"," +
        $"\"sprk_expiresdate\":\"2026-12-10T00:00:00Z\",\"statecode\":0,\"sprk_accesslevel\":100000001," +
        $"\"_sprk_contact_value\":\"{Contact}\",\"_sprk_project_value\":\"{Project}\",\"sprk_externalrecordaccessid\":\"{RowA}\"}}," +
        $"{{\"@odata.etag\":\"W/\\\"26027740\\\"\",\"sprk_expiresdate\":\"2026-12-21T00:00:00Z\",\"statecode\":0," +
        $"\"_sprk_contact_value\":\"{Contact}\",\"_sprk_project_value\":\"{Project}\",\"sprk_externalrecordaccessid\":\"{RowB}\"}}" +
        "]}";

    private WebApplication _app = null!;
    private DataverseWebApiClient _client = null!;

    /// <summary>What the server answers a read with.</summary>
    private string _readBody = LiveCollection;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.MapGet("/api/data/v9.2/sprk_externalrecordaccesses", async (HttpContext http) =>
        {
            http.Response.ContentType = "application/json";
            await http.Response.WriteAsync(_readBody);
        });
        _app.MapGet("/api/data/v9.2/sprk_externalrecordaccesses({id})", async (HttpContext http, string id) =>
        {
            using var doc = JsonDocument.Parse(_readBody);
            var row = doc.RootElement.GetProperty("value").EnumerateArray()
                .FirstOrDefault(r => r.GetProperty("sprk_externalrecordaccessid").GetString() == id);
            if (row.ValueKind == JsonValueKind.Undefined)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            http.Response.ContentType = "application/json";
            await http.Response.WriteAsync(row.GetRawText());
        });

        await _app.StartAsync();
        var baseUrl = _app.Urls.Single();

        _client = new DataverseWebApiClient(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = baseUrl })
                .Build(),
            NullLogger<DataverseWebApiClient>.Instance,
            new StaticCredential());
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>The re-grant's and the revoke sweep's read: one logical grant's active rows.</summary>
    [Fact]
    public async Task TheGrantRowRead_DecodesTheLiveTimeZoneIndependentExpiry()
    {
        var rows = await ExternalGrantLifecycle.QueryActiveRowsAsync(
            _client, ExternalGrantKey.ForContact(ExternalGrantRootType.Project, Project, Contact), CancellationToken.None);

        rows.Select(r => (r.Id, r.ExpiresDate)).Should().Equal(
            (RowB, (DateOnly?)new DateOnly(2026, 12, 21)),
            (RowA, (DateOnly?)new DateOnly(2026, 12, 10)));
    }

    /// <summary><c>/revoke</c>'s target read and its delegation-rule pre-read.</summary>
    [Fact]
    public async Task TheGrantRowReadById_DecodesTheLiveShape()
    {
        var row = await ExternalGrantLifecycle.RetrieveRowAsync(_client, RowA, CancellationToken.None);

        row!.ExpiresDate.Should().Be(new DateOnly(2026, 12, 10), "the calendar date as written — never shifted by a time zone");
        row.AccessLevel.Should().Be(100000001);
    }

    /// <summary><c>set-record-share-expiry</c>'s read: every active row on one record.</summary>
    [Fact]
    public async Task TheRecordWideRead_DecodesTheLiveShape()
    {
        var rows = await ExternalGrantLifecycle.QueryActiveRowsForRootAsync(
            _client, ExternalGrantRootType.Project, Project, top: 51, CancellationToken.None);

        rows.Select(r => r.ExpiresDate).Should().BeEquivalentTo(new DateOnly?[]
        {
            new DateOnly(2026, 12, 10),
            new DateOnly(2026, 12, 21),
        });
    }

    /// <summary>The other shape Dataverse uses for a date (a DateOnly-BEHAVIOUR column), and an undated row, still read.</summary>
    [Theory]
    [InlineData("\"2026-12-10\"", "2026-12-10")]
    [InlineData("null", null)]
    public async Task ABareDateOrNoDate_StillReads(string wireValue, string? expected)
    {
        _readBody = RowWithExpiry(wireValue);

        var rows = await ExternalGrantLifecycle.QueryActiveRowsAsync(
            _client, ExternalGrantKey.ForContact(ExternalGrantRootType.Project, Project, Contact), CancellationToken.None);

        rows.Single().ExpiresDate.Should().Be(expected is null ? null : DateOnly.Parse(expected));
    }

    /// <summary>A value that is not a date is a fault, never a guessed date.</summary>
    [Theory]
    [InlineData("\"12/10/2026\"")]
    [InlineData("\"2026-12-10 00:00:00\"")]
    [InlineData("\"2026-13-40T00:00:00Z\"")]
    [InlineData("20261210")]
    public async Task AValueThatIsNotADate_Throws(string wireValue)
    {
        _readBody = RowWithExpiry(wireValue);

        var act = () => ExternalGrantLifecycle.QueryActiveRowsAsync(
            _client, ExternalGrantKey.ForContact(ExternalGrantRootType.Project, Project, Contact), CancellationToken.None);

        await act.Should().ThrowAsync<JsonException>();
    }

    private static string RowWithExpiry(string wireValue) =>
        "{\"value\":[{\"@odata.etag\":\"W/\\\"7\\\"\",\"sprk_expiresdate\":" + wireValue +
        $",\"statecode\":0,\"_sprk_contact_value\":\"{Contact}\",\"_sprk_project_value\":\"{Project}\"," +
        $"\"sprk_externalrecordaccessid\":\"{RowA}\"}}]}}";

    private sealed class StaticCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("wire-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
