using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 140 r-final — the contact-side grant's colleague-by-email read
/// (<see cref="ExternalParticipationService.FindConferringMembersByEmailAsync"/>) on the WIRE: the REAL service over a
/// real HTTP transport (an in-memory ASP.NET Core server standing in for the Dataverse Web API — ADR-038 §7's
/// replacement for ban B1, the transport <c>OrganizationMembershipReadTests</c> uses).
/// </summary>
/// <remarks>
/// <para><b>Why the wire, not only the builder.</b> <c>ContactGrantAuthorizationTests</c> drives the read's PROJECTION
/// through a double that overrides the HTTP read, and asserts the filter BUILDER. Neither sees what the production call
/// site actually sends, how it follows <c>@odata.nextLink</c>, how it decodes the live JSON shape, or what a faulted page
/// does — the task-109 lesson (<c>OrganizationMembershipReadTests.AssertEveryJunctionReadSentExactlyTheWallFilter</c>): a
/// term appended after the builder call passes every builder assertion and every projection test.</para>
/// <para><b>What is pinned.</b> Each junction request carries EXACTLY the builder's <c>$filter</c> (the WALL state clause,
/// no date term — dates are decided in memory), its <c>$select</c> / <c>$expand</c> and the page-size preference; every
/// page is read; the live row shape (Date Only columns, the expanded <c>sprk_Organization</c> and <c>sprk_Contact</c>)
/// is projected by the conferring rule; the email reaches the server as ONE string literal even with a quote in it; a
/// faulted page is <see cref="ExternalParticipationService.ColleagueEmailMatch.Failed"/>, never "nobody"; and a grantor
/// in more organizations than one query names is read in chunks, each with its own exact filter.</para>
/// <para>KEEP path: <c>tests/integration/auth/**</c> (ADR-038 §2, security-auth).</para>
/// </remarks>
public sealed class ColleagueByEmailWireTests : IAsyncLifetime
{
    private const string FakeServiceUrl = "https://fake-dataverse.crm.dynamics.com";
    private const string Email = "o'brien@firm-a.example";

    private static readonly Guid FirmA = Guid.Parse("0a140000-0000-0000-0000-0000000000a1");
    private static readonly Guid Colleague = Guid.Parse("c1400000-0000-0000-0000-0000000000a1");
    private static readonly Guid FormerColleague = Guid.Parse("c1400000-0000-0000-0000-0000000000a2");
    private static readonly Guid InactiveContact = Guid.Parse("c1400000-0000-0000-0000-0000000000a3");
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private WebApplication _app = null!;
    private readonly List<SeenRequest> _requests = new();
    private readonly object _gate = new();

    /// <summary>Answers each junction request: (request, zero-based request number) → (status, rows, next link).</summary>
    private Func<SeenRequest, int, (int Status, IReadOnlyList<object> Rows, string? NextLink)> _answer =
        (_, _) => (StatusCodes.Status200OK, Array.Empty<object>(), null);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.MapGet("/api/data/v9.2/sprk_contactorganizations", HandleAsync);
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task TheRead_SendsExactlyTheBuiltFilter_ReadsEveryPage_AndProjectsTheLiveShape()
    {
        _answer = (request, n) => n == 0
            ? (200, new[] { Row(Colleague, FirmA, contactState: 0, email: Email) }, $"{FakeServiceUrl}/api/data/v9.2/sprk_contactorganizations?$skiptoken=page2")
            : (200, new[]
            {
                Row(FormerColleague, FirmA, contactState: 0, email: Email, end: Today.AddDays(-1)),  // membership ended
                Row(InactiveContact, FirmA, contactState: 1, email: Email),                         // contact inactive
            }, null);

        var match = await Service().FindConferringMembersByEmailAsync(new[] { FirmA }, $"  {Email} ", CancellationToken.None);

        match.Unreadable.Should().BeFalse();
        match.ContactIds.Should().Equal(Colleague);

        var seen = Requests();
        seen.Should().HaveCount(2, "the read follows @odata.nextLink to the end");
        var first = seen[0];
        first.Filter.Should().Be(
            Uri.UnescapeDataString(ExternalParticipationService.BuildColleagueByEmailFilter(new[] { FirmA }, Email)),
            "the server receives exactly the builder's filter — nothing appended, nothing inlined");
        first.Filter.Should().Contain(ExternalParticipationService.WallMembershipStateClause)
            .And.NotContain("sprk_enddate").And.NotContain("sprk_startdate");
        first.Filter.Should().Contain("sprk_Contact/emailaddress1 eq 'o''brien@firm-a.example'",
            "the email arrives as ONE OData string literal — the quote is doubled, so it cannot end the literal");
        first.Select.Should().Be(ExternalParticipationService.ColleagueByEmailSelect);
        first.Expand.Should().Be(ExternalParticipationService.ColleagueByEmailExpand);
        first.Prefer.Should().Contain($"odata.maxpagesize={ExternalParticipationService.OrganizationMemberPageSize}");
        seen[1].SkipToken.Should().Be("page2");
    }

    [Fact]
    public async Task AFaultedPage_IsUnreadable_NeverNobody_EvenAfterAMatchOnAnEarlierPage()
    {
        _answer = (_, n) => n == 0
            ? (200, new[] { Row(Colleague, FirmA, contactState: 0, email: Email) }, $"{FakeServiceUrl}/api/data/v9.2/sprk_contactorganizations?$skiptoken=page2")
            : (StatusCodes.Status503ServiceUnavailable, Array.Empty<object>(), null);

        var match = await Service().FindConferringMembersByEmailAsync(new[] { FirmA }, Email, CancellationToken.None);

        match.Should().Be(ExternalParticipationService.ColleagueEmailMatch.Failed,
            "a read that could not be completed is reported as a fault (503 to the caller), never as an answer");
    }

    [Fact]
    public async Task AGrantorInMoreOrganizationsThanOneQueryNames_IsReadInChunks_EachWithItsOwnExactFilter()
    {
        var organizations = Enumerable.Range(1, ExternalParticipationService.ColleagueOrganizationChunkSize + 1)
            .Select(i => Guid.Parse($"0a140000-0000-0000-0001-{i:D12}"))
            .ToArray();
        var lastOrganization = organizations[^1];
        _answer = (request, _) => request.Filter.Contains($"_sprk_organization_value eq {lastOrganization:D}", StringComparison.Ordinal)
            ? (200, new[] { Row(Colleague, lastOrganization, contactState: 0, email: Email) }, null)
            : (200, Array.Empty<object>(), null);

        var match = await Service().FindConferringMembersByEmailAsync(organizations, Email, CancellationToken.None);

        match.ContactIds.Should().Equal(new[] { Colleague }, "the colleague's organization is in the LAST chunk");
        Requests().Select(r => r.Filter).Should().Equal(
            Uri.UnescapeDataString(ExternalParticipationService.BuildColleagueByEmailFilter(
                organizations.Take(ExternalParticipationService.ColleagueOrganizationChunkSize).ToArray(), Email)),
            Uri.UnescapeDataString(ExternalParticipationService.BuildColleagueByEmailFilter(new[] { lastOrganization }, Email)));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Harness
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>One junction row in the LIVE shape (read-only GET on spaarkedev1, 2026-10-04).</summary>
    private static object Row(Guid contact, Guid organization, int contactState, string email, DateOnly? end = null)
        => new Dictionary<string, object?>
        {
            ["_sprk_contact_value"] = contact,
            ["_sprk_organization_value"] = organization,
            ["sprk_startdate"] = Today.AddDays(-30).ToString("yyyy-MM-dd"),
            ["sprk_enddate"] = end?.ToString("yyyy-MM-dd"),
            ["statecode"] = 0,
            ["sprk_Organization"] = new Dictionary<string, object?> { ["sprk_organizationid"] = organization, ["statecode"] = 0 },
            ["sprk_Contact"] = new Dictionary<string, object?>
            {
                ["contactid"] = contact,
                ["statecode"] = contactState,
                ["emailaddress1"] = email,
            },
        };

    private async Task HandleAsync(HttpContext context)
    {
        var seen = new SeenRequest(
            context.Request.Query["$filter"].ToString(),
            context.Request.Query["$select"].ToString(),
            context.Request.Query["$expand"].ToString(),
            context.Request.Headers["Prefer"].ToString(),
            context.Request.Query["$skiptoken"].ToString());

        int number;
        lock (_gate)
        {
            number = _requests.Count;
            _requests.Add(seen);
        }

        var (status, rows, next) = _answer(seen, number);
        context.Response.StatusCode = status;
        if (status != StatusCodes.Status200OK)
            return;

        var body = new Dictionary<string, object?> { ["value"] = rows };
        if (next is not null)
            body["@odata.nextLink"] = next;
        await context.Response.WriteAsJsonAsync(body);
    }

    private IReadOnlyList<SeenRequest> Requests()
    {
        lock (_gate) { return _requests.ToList(); }
    }

    private ExternalParticipationService Service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = FakeServiceUrl })
            .Build();
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);

        var client = _app.GetTestClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return new ExternalParticipationService(
            client, Mock.Of<ITenantCache>(), configuration, new StaticTokenCredential(), accessor.Object,
            NullLogger<ExternalParticipationService>.Instance,
            filing: Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities());
    }

    private sealed record SeenRequest(string Filter, string Select, string Expand, string Prefer, string SkipToken);

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
