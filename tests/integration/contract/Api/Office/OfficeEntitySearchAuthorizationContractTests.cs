using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Per-caller authorization contract of <c>GET /api/office/search/entities</c>
/// (spaarkeai-word-add-in-r1 task 062, Fable review finding <b>F1</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>What was wrong.</b> The route ran an app-only Dataverse query, so any authenticated caller
/// received every Matter, Project, Invoice, Account and Contact matching a two-character substring —
/// names, numbers, descriptions, GUIDs and <c>modifiedon</c> — regardless of their rights. The
/// reproduce-first probe and its verbatim failure output are recorded in
/// <c>projects/spaarkeai-word-add-in-r1/notes/062-entity-search-trim.md</c> §2.
/// </para>
/// <para>
/// <b>What the fix is.</b> The handler resolves the caller's Dataverse <c>systemuserid</c> and the
/// search query is issued IMPERSONATED as that user (<c>MSCRMCallerID</c>), so Dataverse applies
/// row-level security inside the query — for all five entity types, on every page.
/// </para>
/// <para>
/// <b>What is doubled, and what is not.</b> Only the two module boundaries: the impersonated read seam
/// (standing in for Dataverse, which is the component that does the trimming — so the stub models
/// Dataverse's behaviour, returning only rows the impersonated user may read) and the caller→systemuser
/// resolver. Everything between is the shipped code: the real route, the real filters, the real
/// <c>OfficeService</c>, the real ranking, the real paging. No <c>Mock&lt;HttpMessageHandler&gt;</c>, no
/// DI-registration assertion, no ctor null-check (ADR-038 bans B1/B16/B17). The app-only
/// <see cref="DataverseWebApiClient"/> is registered as a strict double that FAILS the test if the
/// entity search touches it — the regression channel, not a mechanism.
/// </para>
/// <para>
/// Assertion shape copied from <c>tests/integration/contract/Api/Ai/VisualizationRowAuthorizationContractTests.cs</c>
/// (task 032, same class of defect): denied record absent, permitted neighbour present, count matches
/// the trimmed row set.
/// </para>
/// </remarks>
[Trait("status", "new")]
public class OfficeEntitySearchAuthorizationContractTests : IClassFixture<OfficeTestWebAppFactory>
{
    private readonly OfficeTestWebAppFactory _factory;

    public OfficeEntitySearchAuthorizationContractTests(OfficeTestWebAppFactory factory)
    {
        _factory = factory;
    }

    // ── The world. Two records per entity type: one the caller may read, one they may not. ──────────
    private static readonly Guid CallerSystemUserId = new("aaaaaaaa-0000-0000-0000-00000000000a");

    private static readonly Guid DeniedMatterId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PermittedMatterId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DeniedProjectId = new("11111111-2222-1111-1111-111111111111");
    private static readonly Guid PermittedProjectId = new("22222222-3333-2222-2222-222222222222");
    private static readonly Guid DeniedInvoiceId = new("11111111-3333-1111-1111-111111111111");
    private static readonly Guid PermittedInvoiceId = new("22222222-4444-2222-2222-222222222222");
    private static readonly Guid DeniedAccountId = new("11111111-4444-1111-1111-111111111111");
    private static readonly Guid PermittedAccountId = new("22222222-5555-2222-2222-222222222222");
    private static readonly Guid DeniedContactId = new("11111111-5555-1111-1111-111111111111");
    private static readonly Guid PermittedContactId = new("22222222-6666-2222-2222-222222222222");

    private static readonly IReadOnlyList<Guid> AllDeniedIds = new[]
    {
        DeniedMatterId, DeniedProjectId, DeniedInvoiceId, DeniedAccountId, DeniedContactId
    };

    private static readonly IReadOnlyList<Guid> AllPermittedIds = new[]
    {
        PermittedMatterId, PermittedProjectId, PermittedInvoiceId, PermittedAccountId, PermittedContactId
    };

    #region THE GATE — a denied record is absent, a permitted neighbour is present, the count matches

    [Fact]
    public async Task SearchEntities_WhenCallerIsDeniedReadOnAMatter_ServesNeitherThatMatterNorItsCount()
    {
        var dataverse = StubDataverse.WithFullWorld().Permitting(PermittedMatterId);

        var body = await SearchAsync(dataverse, "?q=Ac&type=Matter&top=10");

        body.Results.Select(r => r.Id).Should().NotContain(
            DeniedMatterId,
            "a caller denied Read on a matter must not receive that matter from the entity picker");

        body.Results.Select(r => r.Id).Should().Contain(
            PermittedMatterId,
            "the trim must remove only what the caller cannot read — a permitted neighbour is still served");

        body.Results.Should().HaveCount(1);
        body.TotalCount.Should().Be(
            1, "the count is a disclosure too — it must describe the trimmed set, not the pre-trim one");
    }

    #endregion

    #region All five entity types

    [Fact]
    public async Task SearchEntities_TrimsAllFiveEntityTypes_NotJustTheSprkOnes()
    {
        // No `type` parameter ⇒ all five are searched. Account and contact matter specifically: the
        // shared post-trim allow-list (SemanticSearchAuthorizationFilter.AuthorizableEntitySets) has no
        // entry for either, so a row-by-row trim would have had to leave two of five types uncovered.
        var dataverse = StubDataverse.WithFullWorld().Permitting(AllPermittedIds.ToArray());

        var body = await SearchAsync(dataverse, "?q=Ac&top=50");

        body.Results.Select(r => r.Id).Should().NotIntersectWith(
            AllDeniedIds,
            "every one of the five entity types is trimmed — four of five is a gap");

        body.Results.Select(r => r.Id).Should().Contain(
            AllPermittedIds,
            "each type's permitted record is still served");

        body.Results.Select(r => r.EntityType).Distinct().Should().HaveCount(
            5, "all five types are represented in the permitted set");

        dataverse.QueriedEntitySets.Should().BeEquivalentTo(
            new[] { "sprk_matters", "sprk_projects", "sprk_invoices", "accounts", "contacts" },
            "all five types go through the impersonated seam");
    }

    #endregion

    #region The paging path

    [Fact]
    public async Task SearchEntities_WithSkipBeyondPageOne_ServesNoUnauthorizedRow()
    {
        // Five permitted records + five denied ones, paged two at a time. If the trim applied only to
        // the first page, page 2 and page 3 would start serving denied rows.
        var dataverse = StubDataverse.WithFullWorld().Permitting(AllPermittedIds.ToArray());

        var seen = new List<Guid>();
        for (var skip = 0; skip < 6; skip += 2)
        {
            var page = await SearchAsync(dataverse, $"?q=Ac&top=2&skip={skip}");
            page.Results.Select(r => r.Id).Should().NotIntersectWith(
                AllDeniedIds, $"page at skip={skip} must be trimmed exactly like page 1");
            seen.AddRange(page.Results.Select(r => r.Id));
        }

        seen.Should().OnlyContain(id => AllPermittedIds.Contains(id));
        seen.Distinct().Should().HaveCount(
            5, "walking the pages reaches every permitted record and no other");
    }

    #endregion

    #region The mechanism: impersonated, never app-only

    [Fact]
    public async Task SearchEntities_IssuesEveryQueryAsTheCaller_AndNeverTouchesTheAppOnlyClient()
    {
        var dataverse = StubDataverse.WithFullWorld().Permitting(AllPermittedIds.ToArray());

        await SearchAsync(dataverse, "?q=Ac&top=50");

        dataverse.ObservedCallers.Should().NotBeEmpty();
        dataverse.ObservedCallers.Should().OnlyContain(
            id => id == CallerSystemUserId,
            "every query carries the caller's systemuserid — that is what makes Dataverse trim it");

        // The app-only DataverseWebApiClient double throws if the entity search reaches it. Getting a
        // 200 above is the assertion; this restates it so the intent survives a refactor.
        dataverse.QueriedEntitySets.Should().NotBeEmpty("the search ran through the impersonated seam");
    }

    #endregion

    #region Fail-closed

    [Fact]
    public async Task SearchEntities_WhenCallerHasNoDataverseSystemUser_Returns403_AndQueriesNothing()
    {
        var dataverse = StubDataverse.WithFullWorld().Permitting(AllPermittedIds.ToArray());

        using var scoped = BuildHost(dataverse, resolveCaller: false);
        var response = await scoped.CreateClient().GetAsync("/api/office/search/entities?q=Ac&top=50");

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "an unresolvable caller has no row-level security context, so there is no trimmed answer to give");

        dataverse.QueriedEntitySets.Should().BeEmpty(
            "nothing is read before the caller identity is established — no app-only fallback");
    }

    [Fact]
    public async Task SearchEntities_WhenCallerCanReadNothing_ReturnsAnEmptySetAndAZeroCount()
    {
        var dataverse = StubDataverse.WithFullWorld(); // permits nothing

        var body = await SearchAsync(dataverse, "?q=Ac&top=50");

        body.Results.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
        body.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task SearchEntities_WhenEveryTypeQueryFails_ReportsAFailure_NotAnEmptyPicker()
    {
        // The most likely production cause is the go-live prerequisite: the BFF application user does
        // not hold prvActOnBehalfOfAnotherUser, so Dataverse rejects every impersonated read. "No
        // results" would read to the user as "you have access to nothing", which is a different and
        // false statement.
        var dataverse = StubDataverse.ThatAlwaysFails();

        using var scoped = BuildHost(dataverse, resolveCaller: true);
        var response = await scoped.CreateClient().GetAsync("/api/office/search/entities?q=Ac&top=50");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    #endregion

    #region The picker still works (task 062 acceptance criterion 9, server half)

    [Fact]
    public async Task SearchEntities_ForAnAuthorizedUser_StillReturnsTheRowsThePickerNeeds()
    {
        // The "Related to" picker (SaveFlow.tsx / useEntitySearch.ts) asks for ONE type at a time with
        // top=10, and renders name + displayInfo. A trim that returns nothing, or that drops the fields
        // the picker binds to, breaks a shipped surface.
        var dataverse = StubDataverse.WithFullWorld().Permitting(PermittedMatterId, PermittedContactId);

        var matters = await SearchAsync(dataverse, "?q=Ac&type=Matter&top=10");
        matters.Results.Should().ContainSingle();
        matters.Results[0].Id.Should().Be(PermittedMatterId);
        matters.Results[0].Name.Should().NotBeNullOrWhiteSpace();
        matters.Results[0].DisplayInfo.Should().NotBeNullOrWhiteSpace();
        matters.Results[0].EntityType.Should().Be(AssociationEntityType.Matter);

        var contacts = await SearchAsync(dataverse, "?q=Ac&type=Contact&top=10");
        contacts.Results.Should().ContainSingle();
        contacts.Results[0].Id.Should().Be(PermittedContactId);
        contacts.Results[0].EntityType.Should().Be(AssociationEntityType.Contact);
    }

    #endregion

    // ── Harness ─────────────────────────────────────────────────────────────────────────────────────

    private async Task<EntitySearchResponse> SearchAsync(StubDataverse dataverse, string queryString)
    {
        using var scoped = BuildHost(dataverse, resolveCaller: true);
        var response = await scoped.CreateClient().GetAsync("/api/office/search/entities" + queryString);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EntitySearchResponse>();
        body.Should().NotBeNull();
        return body!;
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> BuildHost(
        StubDataverse dataverse, bool resolveCaller)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IImpersonatedCommunicationQuery>();
                services.AddSingleton<IImpersonatedCommunicationQuery>(dataverse);

                services.RemoveAll<ICallerSystemUserResolver>();
                services.AddSingleton<ICallerSystemUserResolver>(
                    new StubCallerSystemUserResolver(resolveCaller ? CallerSystemUserId : null));

                // The app-only client the search used to run on. Strict: any entity-search call through
                // it fails the test rather than quietly re-opening the enumeration.
                services.RemoveAll<DataverseWebApiClient>();
                services.AddSingleton(BuildForbiddenAppOnlyClient());
            });
        });
    }

    private static DataverseWebApiClient BuildForbiddenAppOnlyClient()
    {
        var mock = new Mock<DataverseWebApiClient>(
            MockBehavior.Loose,
            Mock.Of<IConfiguration>(c => c["Dataverse:ServiceUrl"] == "https://test.crm.dynamics.com"),
            Mock.Of<ILogger<DataverseWebApiClient>>(),
            new NoOpTokenCredential(),
            (IConfidentialClientProvider)null!);

        mock.Setup(c => c.QueryAsync<Dictionary<string, JsonElement>>(
                It.IsIn("sprk_matters", "sprk_projects", "sprk_invoices", "accounts", "contacts"),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "The entity search must not issue an app-only query for a searchable entity set "
                + "(finding F1). It must go through the impersonated seam."));

        return mock.Object;
    }

    /// <summary>
    /// Stands in for Dataverse behind the impersonated read seam. Deny-by-default: a row is returned
    /// only when the impersonated caller is permitted on it — which is exactly the trimming the real
    /// Dataverse does natively when the query carries <c>MSCRMCallerID</c>.
    /// </summary>
    private sealed class StubDataverse : IImpersonatedCommunicationQuery
    {
        private readonly Dictionary<string, List<(Guid Id, Dictionary<string, JsonElement> Row)>> _world = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<Guid> _permitted = new();
        private readonly bool _alwaysFails;

        public ConcurrentBag<string> QueriedEntitySets { get; } = new();
        public ConcurrentBag<Guid> ObservedCallers { get; } = new();

        private StubDataverse(bool alwaysFails) => _alwaysFails = alwaysFails;

        public static StubDataverse ThatAlwaysFails() => new(alwaysFails: true);

        public static StubDataverse WithFullWorld()
        {
            var stub = new StubDataverse(alwaysFails: false);
            stub.Add("sprk_matters", DeniedMatterId, Matter(DeniedMatterId, "Acme v. Zenith (SEALED)", "M-9001", "2026-09-01"));
            stub.Add("sprk_matters", PermittedMatterId, Matter(PermittedMatterId, "Acme Onboarding", "M-1001", "2026-09-02"));
            stub.Add("sprk_projects", DeniedProjectId, Project(DeniedProjectId, "Acme Carve-out (SEALED)", "P-9001", "2026-09-03"));
            stub.Add("sprk_projects", PermittedProjectId, Project(PermittedProjectId, "Acme Migration", "P-1001", "2026-09-04"));
            stub.Add("sprk_invoices", DeniedInvoiceId, Invoice(DeniedInvoiceId, "Acme Settlement Fees", "I-9001", "2026-09-05"));
            stub.Add("sprk_invoices", PermittedInvoiceId, Invoice(PermittedInvoiceId, "Acme Retainer", "I-1001", "2026-09-06"));
            stub.Add("accounts", DeniedAccountId, Account(DeniedAccountId, "Acme Holdings (restricted)", "A-9001", "2026-09-07"));
            stub.Add("accounts", PermittedAccountId, Account(PermittedAccountId, "Acme Supplies", "A-1001", "2026-09-08"));
            stub.Add("contacts", DeniedContactId, Contact(DeniedContactId, "Ac Whistleblower", "2026-09-09"));
            stub.Add("contacts", PermittedContactId, Contact(PermittedContactId, "Ac Buyer", "2026-09-10"));
            return stub;
        }

        public StubDataverse Permitting(params Guid[] ids)
        {
            foreach (var id in ids) _permitted.Add(id);
            return this;
        }

        private void Add(string entitySet, Guid id, Dictionary<string, JsonElement> row)
        {
            if (!_world.TryGetValue(entitySet, out var rows))
            {
                rows = new List<(Guid, Dictionary<string, JsonElement>)>();
                _world[entitySet] = rows;
            }
            rows.Add((id, row));
        }

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            // Mirrors DataverseWebApiService.RetrieveMultipleImpersonatedAsync's own guard: an
            // impersonated read with no caller is refused, never degraded to app-only.
            if (callerSystemUserId == Guid.Empty)
                throw new ArgumentException("An impersonated read requires a non-empty caller systemuserid.");

            QueriedEntitySets.Add(entitySetName);
            ObservedCallers.Add(callerSystemUserId);

            if (_alwaysFails)
                throw new HttpRequestException("Dataverse rejected the impersonated read (simulated: no prvActOnBehalfOfAnotherUser).");

            var rows = _world.TryGetValue(entitySetName, out var all)
                ? all.Where(r => _permitted.Contains(r.Id)).Select(r => r.Row).ToList()
                : new List<Dictionary<string, JsonElement>>();

            return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(rows);
        }

        private static Dictionary<string, JsonElement> Matter(Guid id, string name, string number, string modified) =>
            Row($$"""{ "sprk_matterid": "{{id}}", "sprk_mattername": "{{name}}", "sprk_matternumber": "{{number}}", "modifiedon": "{{modified}}T00:00:00Z" }""");

        private static Dictionary<string, JsonElement> Project(Guid id, string name, string number, string modified) =>
            Row($$"""{ "sprk_projectid": "{{id}}", "sprk_projectname": "{{name}}", "sprk_projectnumber": "{{number}}", "modifiedon": "{{modified}}T00:00:00Z" }""");

        private static Dictionary<string, JsonElement> Invoice(Guid id, string name, string number, string modified) =>
            Row($$"""{ "sprk_invoiceid": "{{id}}", "sprk_name": "{{name}}", "sprk_invoicenumber": "{{number}}", "modifiedon": "{{modified}}T00:00:00Z" }""");

        private static Dictionary<string, JsonElement> Account(Guid id, string name, string number, string modified) =>
            Row($$"""{ "accountid": "{{id}}", "name": "{{name}}", "accountnumber": "{{number}}", "modifiedon": "{{modified}}T00:00:00Z" }""");

        private static Dictionary<string, JsonElement> Contact(Guid id, string name, string modified) =>
            Row($$"""{ "contactid": "{{id}}", "fullname": "{{name}}", "jobtitle": "Buyer", "modifiedon": "{{modified}}T00:00:00Z" }""");

        private static Dictionary<string, JsonElement> Row(string json) =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }

    /// <summary>Resolves the caller to a fixed systemuserid, or honestly to none.</summary>
    private sealed class StubCallerSystemUserResolver : ICallerSystemUserResolver
    {
        private readonly Guid? _systemUserId;

        public StubCallerSystemUserResolver(Guid? systemUserId) => _systemUserId = systemUserId;

        public Task<CallerSystemUserResolution> ResolveAsync(ClaimsPrincipal? caller, CancellationToken ct) =>
            Task.FromResult(_systemUserId is { } id
                ? CallerSystemUserResolution.Resolved(id.ToString("D"))
                : CallerSystemUserResolution.Unresolved("no-matching-systemuser"));
    }

    /// <summary>A <see cref="TokenCredential"/> that answers instantly and never touches the network.</summary>
    private sealed class NoOpTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("stub-token-not-a-real-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
