using System.Data;
using System.Net;
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
/// unified-access-control-r2 task 140 (session 27 round 42 item 1) — the grant ROW on the wire: the REAL
/// <see cref="DataverseWebApiClient"/> and <see cref="ExternalGrantLifecycle"/> over a real HTTP transport (a loopback
/// ASP.NET Core server standing in for the Dataverse Web API — ADR-038 §7's replacement for ban B1; this client builds its
/// own <see cref="HttpClient"/>, so an in-memory test server cannot be injected and a loopback port is used instead).
/// </summary>
/// <remarks>
/// <para><b>Why the wire.</b> The contact-side tests run the grant core over an in-memory table that serializes rows in the
/// SHAPE THE TEST CHOOSES. Two facts only the wire shows: (1) Dataverse returns <c>sprk_expiresdate</c> — DateOnly format,
/// TimeZoneIndependent behaviour — as <c>"2026-12-10T00:00:00Z"</c> (read live, read-only, spaarkedev1 2026-10-05), which
/// System.Text.Json's own <see cref="DateOnly"/> converter refuses, so every read of a dated grant row threw; and (2) the
/// row version is <c>@odata.etag</c>, which the conditional writes send back as <c>If-Match</c>. The JSON served below is the
/// live response, verbatim but for the ids.</para>
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
    private readonly List<(string Method, string Path, string? IfMatch, string Body)> _requests = new();
    private readonly object _gate = new();

    /// <summary>How the server answers a PATCH to a row: (row id, If-Match sent) → status.</summary>
    private Func<Guid, string?, int> _patchStatus = (_, _) => StatusCodes.Status204NoContent;

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
            Record(http, string.Empty);
            http.Response.ContentType = "application/json";
            await http.Response.WriteAsync(_readBody);
        });
        _app.MapGet("/api/data/v9.2/sprk_externalrecordaccesses({id})", async (HttpContext http, string id) =>
        {
            Record(http, string.Empty);
            using var doc = System.Text.Json.JsonDocument.Parse(_readBody);
            var row = doc.RootElement.GetProperty("value").EnumerateArray()
                .FirstOrDefault(r => r.GetProperty("sprk_externalrecordaccessid").GetString() == id);
            if (row.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            http.Response.ContentType = "application/json";
            await http.Response.WriteAsync(row.GetRawText());
        });
        _app.MapMethods("/api/data/v9.2/sprk_externalrecordaccesses({id})", new[] { "PATCH" }, async (HttpContext http, string id) =>
        {
            using var reader = new StreamReader(http.Request.Body);
            Record(http, await reader.ReadToEndAsync());
            http.Response.StatusCode = _patchStatus(Guid.Parse(id), http.Request.Headers.IfMatch.ToString());
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Reading the live row shape
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheGrantRowRead_DecodesTheLiveShape_ATimeZoneIndependentExpiryAndTheRowVersion()
    {
        var rows = await ExternalGrantLifecycle.QueryActiveRowsAsync(
            _client, ExternalGrantKey.ForContact(ExternalGrantRootType.Project, Project, Contact), CancellationToken.None);

        rows.Select(r => (r.Id, r.ExpiresDate, r.ETag)).Should().Equal(
            (RowB, (DateOnly?)new DateOnly(2026, 12, 21), "W/\"26027740\""),
            (RowA, (DateOnly?)new DateOnly(2026, 12, 10), "W/\"25734128\""));
    }

    [Fact]
    public async Task TheGrantRowReadById_DecodesTheLiveShape()
    {
        var row = await ExternalGrantLifecycle.RetrieveRowAsync(_client, RowA, CancellationToken.None);

        row!.ExpiresDate.Should().Be(new DateOnly(2026, 12, 10), "the calendar date as written — never shifted by a time zone");
        row.ETag.Should().Be("W/\"25734128\"");
        row.AccessLevel.Should().Be(100000001);
    }

    /// <summary>The other shape Dataverse uses for a date (a DateOnly-BEHAVIOUR column), and an undated row, still read.</summary>
    [Theory]
    [InlineData("\"2026-12-10\"", "2026-12-10")]
    [InlineData("null", null)]
    public async Task ABareDateOrNoDate_StillReads(string wireValue, string? expected)
    {
        _readBody = "{\"value\":[{\"@odata.etag\":\"W/\\\"7\\\"\",\"sprk_expiresdate\":" + wireValue +
                    $",\"statecode\":0,\"_sprk_contact_value\":\"{Contact}\",\"sprk_externalrecordaccessid\":\"{RowA}\"}}]}}";

        var rows = await ExternalGrantLifecycle.QueryActiveRowsAsync(
            _client, ExternalGrantKey.ForContact(ExternalGrantRootType.Project, Project, Contact), CancellationToken.None);

        rows.Single().ExpiresDate.Should().Be(expected is null ? null : DateOnly.Parse(expected));
    }

    /// <summary>
    /// <c>set-record-share-expiry</c>'s read: every active row on one record (master #1297; kept beside task 140's set at
    /// the batch-4 integration).
    /// </summary>
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

    /// <summary>A value that is not a date is a fault, never a guessed date (master #1297).</summary>
    [Theory]
    [InlineData("\"12/10/2026\"")]
    [InlineData("\"2026-12-10 00:00:00\"")]
    [InlineData("\"2026-13-40T00:00:00Z\"")]
    [InlineData("20261210")]
    public async Task AValueThatIsNotADate_Throws(string wireValue)
    {
        _readBody = "{\"value\":[{\"@odata.etag\":\"W/\\\"7\\\"\",\"sprk_expiresdate\":" + wireValue +
                    $",\"statecode\":0,\"_sprk_contact_value\":\"{Contact}\",\"_sprk_project_value\":\"{Project}\"," +
                    $"\"sprk_externalrecordaccessid\":\"{RowA}\"}}]}}";

        var act = () => ExternalGrantLifecycle.QueryActiveRowsAsync(
            _client, ExternalGrantKey.ForContact(ExternalGrantRootType.Project, Project, Contact), CancellationToken.None);

        await act.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The conditional write
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateIfMatch_SendsTheVersionAsRead_AsIfMatch_AndAppliesWhenItStillMatches()
    {
        await _client.UpdateIfMatchAsync(ExternalGrantLifecycle.EntitySet, RowA, new { sprk_accesslevel = 100000002 },
            "W/\"25734128\"");

        var patch = Patches().Should().ContainSingle().Subject;
        patch.Path.Should().EndWith($"sprk_externalrecordaccesses({RowA})");
        patch.IfMatch.Should().Be("W/\"25734128\"", "the version exactly as the read returned it");
        patch.Body.Should().Contain("\"sprk_accesslevel\":100000002");
    }

    [Fact]
    public async Task UpdateIfMatch_WhenTheRowChanged_Is412_ThrowsDBConcurrency_AndIsNeverRetried()
    {
        _patchStatus = (_, _) => StatusCodes.Status412PreconditionFailed;

        var act = () => _client.UpdateIfMatchAsync(ExternalGrantLifecycle.EntitySet, RowA, new { statecode = 1, statuscode = 2 },
            "W/\"25734128\"");

        await act.Should().ThrowAsync<DBConcurrencyException>();
        Patches().Should().ContainSingle("a refused conditional write is never retried");
    }

    [Fact]
    public async Task UpdateIfMatch_WhenTheRowIsGone_Is404_AndNothingIsCreated()
    {
        _patchStatus = (_, _) => StatusCodes.Status404NotFound;

        var act = () => _client.UpdateIfMatchAsync(ExternalGrantLifecycle.EntitySet, RowA, new { statecode = 1 }, "W/\"1\"");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateIfMatch_WithNoVersion_SendsNothing(string etag)
    {
        var act = () => _client.UpdateIfMatchAsync(ExternalGrantLifecycle.EntitySet, RowA, new { statecode = 1 }, etag);

        await act.Should().ThrowAsync<ArgumentException>();
        Patches().Should().BeEmpty("an unconditional write is never sent in place of a conditional one");
    }

    /// <summary>
    /// The contact revoke's deactivation over the wire: each row carries ITS OWN version; a row that changed is left and
    /// reported, the other is ended.
    /// </summary>
    [Fact]
    public async Task DeactivateIfUnchanged_SendsEachRowsOwnVersion_EndsTheUnchanged_AndReportsTheChanged()
    {
        var rows = await ExternalGrantLifecycle.QueryActiveRowsAsync(
            _client, ExternalGrantKey.ForContact(ExternalGrantRootType.Project, Project, Contact), CancellationToken.None);
        _patchStatus = (id, _) => id == RowB ? StatusCodes.Status412PreconditionFailed : StatusCodes.Status204NoContent;

        var outcome = await ExternalGrantLifecycle.DeactivateIfUnchangedAsync(_client, rows, NullLogger.Instance, CancellationToken.None);

        outcome.Deactivated.Should().Equal(RowA);
        outcome.ChangedSinceRead.Should().Equal(RowB);
        Patches().Select(p => (p.Path[(p.Path.LastIndexOf('(') + 1)..^1], p.IfMatch)).Should().BeEquivalentTo(new[]
        {
            (RowA.ToString(), "W/\"25734128\""),
            (RowB.ToString(), "W/\"26027740\""),
        });
        Patches().Should().OnlyContain(p => p.Body.Contains("\"statecode\":1") && p.Body.Contains("\"statuscode\":2"));
    }

    private void Record(HttpContext http, string body)
    {
        lock (_gate)
        {
            _requests.Add((http.Request.Method, http.Request.Path.Value ?? string.Empty,
                http.Request.Headers.IfMatch.Count > 0 ? http.Request.Headers.IfMatch.ToString() : null, body));
        }
    }

    private List<(string Method, string Path, string? IfMatch, string Body)> Patches()
    {
        lock (_gate)
        {
            return _requests.Where(r => r.Method == "PATCH").ToList();
        }
    }

    private sealed class StaticCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("wire-test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
