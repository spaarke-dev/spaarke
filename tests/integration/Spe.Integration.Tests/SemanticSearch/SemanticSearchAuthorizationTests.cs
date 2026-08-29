using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Xunit;

namespace Spe.Integration.Tests.SemanticSearch;

/// <summary>
/// Authorization-focused integration tests for semantic search.
/// Verifies security boundaries and tenant isolation.
/// </summary>
public class SemanticSearchAuthorizationTests : IClassFixture<SemanticSearchAuthorizationTestFixture>
{
    private readonly SemanticSearchAuthorizationTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string TenantA = "tenant-A-123";
    private const string TenantB = "tenant-B-456";
    private const string TestEntityType = "matter";
    private const string TestEntityId = "00000000-0000-0000-0000-000000000001";
    private const string DocumentA = "00000000-0000-0000-0000-0000000000aa";
    private const string DocumentB = "00000000-0000-0000-0000-0000000000bb";

    public SemanticSearchAuthorizationTests(SemanticSearchAuthorizationTestFixture fixture)
    {
        _fixture = fixture;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    #region Tenant Isolation Tests

    [Fact]
    public async Task Search_WithValidTenantTokenAndParentAccess_Returns_Ok()
    {
        // Arrange — a tenant claim alone is no longer sufficient; the caller must hold Read on the
        // parent. That is the whole point of task 070, so this test now grants it explicitly.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_TenantIdFromToken_IsEnforced()
    {
        // Arrange - User from Tenant A makes request
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        // Assert - Request succeeds, tenant isolation enforced at query time
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content!.Metadata.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_WithoutTenantClaim_Returns_401()
    {
        // Arrange - Token without tenant ID claim
        var client = _fixture.CreateClientWithInvalidTenantClaim();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Scope Authorization Tests

    // unified-access-control-r2 task 070.
    //
    // The three tests this region replaced asserted that entity, documentIds AND `all` scopes were
    // each "IsAllowed" and returned 200 for any authenticated caller. That was an accurate description
    // of the code — every branch of the filter returned allow — which is precisely why they passed
    // while the route disclosed every document in the tenant. They were the vulnerability, written
    // down as an expectation. The tests below assert the caller's access decides the outcome.

    [Fact]
    public async Task Search_EntityScope_WhenCallerHasReadOnParent_ReturnsOk()
    {
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_EntityScope_WhenCallerHasNoAccessToParent_Returns403()
    {
        // The core regression. A caller with no rights on the parent matter must not receive its
        // documents — this is the disclosure proven end-to-end on 2026-08-25, where a non-admin denied
        // Read on all 442 documents by Dataverse still listed, opened and downloaded a matter's files.
        var callerId = Guid.NewGuid().ToString();
        // Deliberately no grant.
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Search_EntityScope_WhenParentTypeIsNotAuthorizable_Returns403()
    {
        var callerId = Guid.NewGuid().ToString();
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = "systemuser",
            EntityId = TestEntityId
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Search_DocumentIdsScope_WhenCallerCanReadNone_Returns403()
    {
        var callerId = Guid.NewGuid().ToString();
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "documentIds",
            DocumentIds = new List<string> { DocumentA, DocumentB }
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Search_DocumentIdsScope_WhenCallerCanReadSome_ReturnsOnlyReadableDocuments()
    {
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantDocument(callerId, DocumentA, AccessRights.Read);
        // DocumentB deliberately not granted.
        _fixture.Search.Results =
        [
            ResultFor(DocumentA),
            ResultFor(DocumentB)
        ];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "documentIds",
            DocumentIds = new List<string> { DocumentA, DocumentB }
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);

        content!.Results.Select(r => r.DocumentId).Should().BeEquivalentTo([DocumentA]);
        content.Metadata.ReturnedResults.Should().Be(1);
        // The count must not report the document the caller cannot read.
        content.Metadata.TotalResults.Should().Be(1);
    }

    [Fact]
    public async Task Search_ScopeAll_IsAcceptedButNeverABlanketAllow()
    {
        // SUPERSEDED BEHAVIOUR — was Search_ScopeAll_Returns403 (task 070).
        //
        // At HEAD this branch carried the comment "R3: scope=all is now supported for system-wide
        // document search" and returned allow, handing any authenticated non-admin every document in the
        // tenant. Task 070 refused the scope outright. Task 080 accepts it again and FILTERS it per row,
        // because cross-record search is a capability Spaarke offers — task 070's premise that no caller
        // needed it was false.
        //
        // What this test still guards is the invariant common to all three eras: the scope alone never
        // entitles a caller to anything. The full filtering suite lives in the "Cross-Record Search"
        // region below; this asserts only that acceptance is not entitlement.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Search.Results = [ResultFor(DocumentA)];   // the index matched
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);
        var request = new SemanticSearchRequest { Query = "test query", Scope = "all" };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(DocumentA,
            "the caller holds Read on nothing, so an accepted scope must still yield no documents");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-scope")]
    public async Task Search_WhenScopeIsEmptyOrUnknown_IsRefusedAndNeverExecutesTheSearch(string? scope)
    {
        // The `default:` branch previously returned ALLOW with "let endpoint handle validation", so an
        // absent or unrecognised scope was an unauthorized read whose only remaining gate was shape
        // validation. It is now refused in the filter.
        //
        // The status is 400 (malformed request), not 403 — only three scopes exist and all three are
        // handled explicitly, so reaching the default branch means the scope was not a scope. What
        // matters for security is asserted separately below: the search never runs.
        _fixture.Search.Results = [ResultFor(DocumentA)];
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest { Query = "test query", Scope = scope! };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The refusal must happen BEFORE the search executes — a 400 that still ran the query and
        // discarded the rows would be a different bug wearing the same status code.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(DocumentA);
    }

    [Fact]
    public async Task Search_EntityScope_WhenResultBelongsToADifferentParent_DropsIt()
    {
        // Result-level authorization. The Azure AI Search index is a separate data plane with no ACL
        // data and no freshness guarantee: if a document is reparented in Dataverse and the index still
        // carries the old parent, a parent-scoped query returns a row outside the authorized scope. A
        // filter expression is a query predicate, not an authorization decision.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        _fixture.Search.Results =
        [
            ResultFor(DocumentA, parentId: TestEntityId),
            ResultFor(DocumentB, parentId: Guid.NewGuid().ToString())
        ];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);

        content!.Results.Select(r => r.DocumentId).Should().BeEquivalentTo([DocumentA]);
    }

    [Fact]
    public async Task Search_WhenAuthorized_DoesNotReturnSpePointers()
    {
        // Broker-only: no client receives raw SPE pointers. File access goes through document-id-keyed
        // BFF routes that carry the standard gate; returning driveId/speFileId invites clients to
        // address SPE directly, which is how the ungated drive-keyed routes came to exist.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        _fixture.Search.Results =
        [
            ResultFor(DocumentA, parentId: TestEntityId, driveId: "drive-1", speFileId: "item-1")
        ];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content!.Results.Should().ContainSingle();
        content.Results[0].DriveId.Should().BeNull();
        content.Results[0].SpeFileId.Should().BeNull();
    }

    [Fact]
    public async Task Count_EntityScope_WhenCallerHasNoAccessToParent_Returns403()
    {
        // The count endpoint carries the same filter and must reach the same decision — a count is a
        // disclosure about content the caller cannot see.
        var callerId = Guid.NewGuid().ToString();
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync("/api/ai/search/count", EntityScopeRequest(), _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("documentIds")]
    [InlineData("documentids")]
    [InlineData("DOCUMENTIDS")]
    [InlineData("DocumentIds")]
    public async Task Search_DocumentIdsScope_IsMatchedCaseInsensitively(string scope)
    {
        // Regression for a defect the allow-by-default `default:` branch was hiding. The filter
        // lower-cased the incoming scope and switched over the SearchScope constants — but
        // SearchScope.DocumentIds is the camel-cased literal "documentIds", so a lower-cased value
        // could never match that label. Every scope=documentIds request fell into `default:`, which
        // returned allow, so nothing looked wrong. With `default:` denying, a match failure would
        // instead lock legitimate callers out. This pins the comparison as case-insensitive.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantDocument(callerId, DocumentA, AccessRights.Read);
        _fixture.Search.Results = [ResultFor(DocumentA)];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = scope,
            DocumentIds = new List<string> { DocumentA }
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static SemanticSearchRequest EntityScopeRequest() => new()
    {
        Query = "test query",
        Scope = "entity",
        EntityType = TestEntityType,
        EntityId = TestEntityId
    };

    private static SearchResult ResultFor(
        string documentId,
        string? parentId = null,
        string? driveId = null,
        string? speFileId = null) => new()
        {
            DocumentId = documentId,
            Name = $"{documentId}.pdf",
            CombinedScore = 0.5,
            ParentEntityType = TestEntityType,
            ParentEntityId = parentId ?? TestEntityId,
            DriveId = driveId,
            SpeFileId = speFileId
        };

    #endregion

    #region Cross-Record Search (scope=all) — task 080

    // scope=all was a live tenant-wide document disclosure until task 070 refused it, and is restored
    // here as a FILTERED capability. Filtering IS the security boundary now, so these tests are the
    // deliverable rather than a coda. They are written to fail if any single code path serves a
    // cross-record row without consulting the caller's access.
    //
    // All of them run against StubAccessDataSource, which DENIES BY DEFAULT — a grant has to be made
    // explicitly, so a test cannot pass by accident of a permissive stub.

    private const string MatterEntitySet = "sprk_matters";

    /// <summary>
    /// A cross-record row. Both parent fields are explicit with no defaults, deliberately: parentage is
    /// the entire subject of these tests, so no test may leave it implied.
    /// </summary>
    private static SearchResult CrossRecordRow(string documentId, string? parentType, string? parentId) => new()
    {
        DocumentId = documentId,
        Name = $"{documentId}.pdf",
        CombinedScore = 0.5,
        ParentEntityType = parentType,
        ParentEntityId = parentId
    };

    [Fact]
    public async Task Search_ScopeAll_ReturnsOnlyRowsWhoseParentTheCallerCanRead()
    {
        // Arrange — two matters, one readable. Rows interleaved so a bug that stops filtering after the
        // first denial, or filters only the head of the list, still fails.
        var callerId = Guid.NewGuid().ToString();
        var readable = Guid.NewGuid();
        var forbidden = Guid.NewGuid();

        _fixture.Access.GrantRecord(callerId, MatterEntitySet, readable, AccessRights.Read);
        // `forbidden` is deliberately never granted.

        _fixture.Search.Results =
        [
            CrossRecordRow("doc-allowed-1", "matter", readable.ToString()),
            CrossRecordRow("doc-denied-1", "matter", forbidden.ToString()),
            CrossRecordRow("doc-allowed-2", "matter", readable.ToString()),
            CrossRecordRow("doc-denied-2", "matter", forbidden.ToString())
        ];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/ai/search", new SemanticSearchRequest { Query = "test query", Scope = "all" }, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("doc-allowed-1").And.Contain("doc-allowed-2");
        body.Should().NotContain("doc-denied-1",
            "a document whose parent record the caller cannot read must never be served");
        body.Should().NotContain("doc-denied-2");
        body.Should().NotContain(forbidden.ToString(),
            "not even the unreadable parent's id may leak — that alone confirms the record exists");
    }

    [Fact]
    public async Task Search_ScopeAll_WithNoGrants_ReturnsNoRows_ThoughTheIndexMatched()
    {
        // The strongest single assertion here. The search DID match rows; the caller holds Read on
        // nothing. If ANY code path serves cross-record rows without consulting access — a branch that
        // forgets the per-row pass, an early return, a future refactor that restores the old
        // `AuthorizeResults` call for this scope — this is the test that fails.
        var callerId = Guid.NewGuid().ToString();

        _fixture.Search.Results =
        [
            CrossRecordRow("doc-x", "matter", Guid.NewGuid().ToString()),
            CrossRecordRow("doc-y", "project", Guid.NewGuid().ToString()),
            CrossRecordRow("doc-z", "workassignment", Guid.NewGuid().ToString())
        ];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync(
            "/api/ai/search", new SemanticSearchRequest { Query = "test query", Scope = "all" }, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an empty result is the correct answer for cross-record search — unlike scope=documentIds, "
            + "where the caller named specific documents and a 403 is the honest reply");

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        content!.Results.Should().BeEmpty();
        content.Metadata.TotalResults.Should().Be(0);
        content.Metadata.ReturnedResults.Should().Be(0);
    }

    [Fact]
    public async Task Search_ScopeAll_CallerEntitledToThreeOfFifty_ReceivesExactlyThree()
    {
        // The acceptance criterion, verbatim: "a caller entitled to 3 of 50 matches gets exactly 3".
        //
        // 50 matching documents across 5 parents; the caller may read exactly one parent, which owns
        // exactly 3 of them. The three are placed LAST so a naive implementation that authorizes only
        // the first page-worth of candidates and stops returns zero and fails.
        //
        // Distinct parents — not rows — are the unit of cost, and 5 is well inside the check budget, so
        // recall here is complete and the count is exact. The next test covers exceeding that budget.
        var callerId = Guid.NewGuid().ToString();
        var parents = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var entitled = parents[4];

        _fixture.Access.GrantRecord(callerId, MatterEntitySet, entitled, AccessRights.Read);

        var rows = new List<SearchResult>();
        for (var i = 0; i < 47; i++)
        {
            rows.Add(CrossRecordRow($"doc-denied-{i}", "matter", parents[i % 4].ToString()));
        }
        for (var i = 0; i < 3; i++)
        {
            rows.Add(CrossRecordRow($"doc-entitled-{i}", "matter", entitled.ToString()));
        }

        _fixture.Search.Results = rows;
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync(
            "/api/ai/search", new SemanticSearchRequest { Query = "test query", Scope = "all" }, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);

        content!.Results.Should().HaveCount(3);
        content.Results.Select(r => r.DocumentId).Should()
            .BeEquivalentTo(["doc-entitled-0", "doc-entitled-1", "doc-entitled-2"]);

        // The corpus was fully examined (the pool was not saturated) and the budget was not exhausted,
        // so this answer is COMPLETE and must not be labelled otherwise. A warning that fires here would
        // be the noise that makes the real signal ignorable.
        content.Metadata.Warnings?.Select(w => w.Code)
            .Should().NotContain(SearchWarningCode.PartialResults);
    }

    [Fact]
    public async Task Search_ScopeAll_WhenParentCheckBudgetIsExhausted_AnnouncesIncompleteness()
    {
        // The honest limit. Every row has a DIFFERENT parent, so the per-page check budget runs out
        // before the caller's readable parent — placed deliberately out of reach — is ever evaluated.
        //
        // The recall loss is real and this test asserts it rather than hiding it: the entitled document
        // does NOT come back. What must not happen is the response presenting that as "no matches",
        // because a short page is indistinguishable from an exhaustive one by inspection. That is the
        // failure mode task 080 exists to defeat, so the PARTIAL_RESULTS warning is the assertion that
        // matters most in this test.
        var callerId = Guid.NewGuid().ToString();
        var outOfReach = Guid.NewGuid();

        _fixture.Access.GrantRecord(callerId, MatterEntitySet, outOfReach, AccessRights.Read);

        var rows = new List<SearchResult>();
        for (var i = 0; i < 60; i++)
        {
            rows.Add(CrossRecordRow($"doc-unreachable-{i}", "matter", Guid.NewGuid().ToString()));
        }
        rows.Add(CrossRecordRow("doc-entitled-but-beyond-budget", "matter", outOfReach.ToString()));

        _fixture.Search.Results = rows;
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync(
            "/api/ai/search", new SemanticSearchRequest { Query = "test query", Scope = "all" }, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);

        content!.Results.Should().BeEmpty("the entitled parent ranks beyond the check budget");
        content.Metadata.Warnings.Should().NotBeNull();
        content.Metadata.Warnings!.Select(w => w.Code).Should().Contain(SearchWarningCode.PartialResults,
            "a page shortened by authorization MUST NOT be presented as an exhaustive result set");
    }

    [Theory]
    [InlineData("account", "a valid filters.entityTypes value that is NOT an authorizable parent")]
    [InlineData("contact", "same — valid as a filter, unmapped as a securable parent")]
    [InlineData("sprk_unknownthing", "an entirely unrecognised parent type")]
    [InlineData(null, "no parent type at all")]
    public async Task Search_ScopeAll_DropsRowsWhoseParentTypeIsNotAuthorizable(string? parentType, string why)
    {
        // Fail closed (ADR-003): a row whose parent cannot be resolved to an authorizable table is
        // DROPPED, never served and never checked against some fallback. `account` and `contact` are the
        // interesting cases — they ARE accepted in filters.entityTypes, so the vocabularies disagree and
        // a "valid" type can still be unauthorizable. Open item O-1 in the task 080 notes.
        var callerId = Guid.NewGuid().ToString();
        var parentId = Guid.NewGuid();

        // Granted on every set it could plausibly resolve to, so a pass cannot come from a missing grant.
        _fixture.Access.GrantRecord(callerId, MatterEntitySet, parentId, AccessRights.Read);
        _fixture.Access.GrantRecord(callerId, "accounts", parentId, AccessRights.Read);
        _fixture.Access.GrantRecord(callerId, "contacts", parentId, AccessRights.Read);

        _fixture.Search.Results = [CrossRecordRow("doc-unauthorizable-parent", parentType, parentId.ToString())];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync(
            "/api/ai/search", new SemanticSearchRequest { Query = "test query", Scope = "all" }, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("doc-unauthorizable-parent", why);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Search_ScopeAll_DropsRowsWithMissingOrMalformedParentId(string? parentId)
    {
        // Unknown parentage is precisely the case that must not be served. Guid.Empty is included
        // because it PARSES — a check that only did Guid.TryParse would let it through and then query
        // Dataverse for the all-zero record.
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, MatterEntitySet, Guid.Empty, AccessRights.Read);

        _fixture.Search.Results = [CrossRecordRow("doc-no-parentage", "matter", parentId)];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync(
            "/api/ai/search", new SemanticSearchRequest { Query = "test query", Scope = "all" }, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("doc-no-parentage");
    }

    [Fact]
    public async Task Search_ScopeAll_StripsSpePointersFromPermittedRows()
    {
        // Broker-only: no client needs driveId/speFileId, and returning them invites clients to address
        // SPE directly — the pattern that produced the ungated drive-keyed routes task 071 retired.
        var callerId = Guid.NewGuid().ToString();
        var readable = Guid.NewGuid();
        _fixture.Access.GrantRecord(callerId, MatterEntitySet, readable, AccessRights.Read);

        _fixture.Search.Results =
        [
            CrossRecordRow("doc-with-pointers", "matter", readable.ToString())
                with { DriveId = "drive-should-not-appear", SpeFileId = "spefile-should-not-appear" }
        ];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync(
            "/api/ai/search", new SemanticSearchRequest { Query = "test query", Scope = "all" }, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("doc-with-pointers", "the row itself is permitted");
        body.Should().NotContain("drive-should-not-appear");
        body.Should().NotContain("spefile-should-not-appear");
    }

    [Fact]
    public async Task Search_ScopeAll_TotalResultsMatchesWhatTheCallerCanActuallyReach()
    {
        // Paging contract (task 080 notes §1): totalResults is the count of PERMITTED rows in this
        // response. It must never report a number drawn from the over-fetched candidate pool, because
        // that is a page the caller cannot reach — and the client derives `hasMore` from it.
        var callerId = Guid.NewGuid().ToString();
        var readable = Guid.NewGuid();
        _fixture.Access.GrantRecord(callerId, MatterEntitySet, readable, AccessRights.Read);

        var rows = new List<SearchResult>
        {
            CrossRecordRow("doc-ok-1", "matter", readable.ToString()),
            CrossRecordRow("doc-ok-2", "matter", readable.ToString())
        };
        for (var i = 0; i < 10; i++)
        {
            rows.Add(CrossRecordRow($"doc-nope-{i}", "matter", Guid.NewGuid().ToString()));
        }

        _fixture.Search.Results = rows;
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync(
            "/api/ai/search", new SemanticSearchRequest { Query = "test query", Scope = "all" }, _jsonOptions);

        var content = await response.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);

        content!.Results.Should().HaveCount(2);
        content.Metadata.ReturnedResults.Should().Be(2);
        content.Metadata.TotalResults.Should().Be(2,
            "reporting 12 here would tell the client there are 10 more pages of documents it can never load");
    }

    [Theory]
    [InlineData("all")]
    [InlineData("All")]
    [InlineData("ALL")]
    public async Task Search_ScopeAll_IsCaseInsensitive_AndStillFiltered(string scope)
    {
        // Casing was a live defect on scope=documentIds: the filter lower-cased the input and compared it
        // against the camel-cased literal, so the case could never match and every request fell through
        // to a permissive default. Pinning every scope's casing is how that stays fixed.
        var callerId = Guid.NewGuid().ToString();

        _fixture.Search.Results = [CrossRecordRow("doc-casing", "matter", Guid.NewGuid().ToString())];

        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync(
            "/api/ai/search", new SemanticSearchRequest { Query = "test query", Scope = scope }, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("doc-casing",
            $"scope '{scope}' must be recognised as cross-record AND filtered — a casing that fell "
            + "through to a permissive branch would serve this row");
    }

    [Fact]
    public async Task Count_ScopeAll_IsRefused_BecauseACountCannotBeFiltered()
    {
        // /search serves scope=all by dropping rows; a count has nothing to drop. The only number it
        // could return is derived from the unfiltered corpus, which discloses how many documents exist
        // tenant-wide. The asymmetry between the two routes is intentional.
        var callerId = Guid.NewGuid().ToString();
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        var response = await client.PostAsJsonAsync(
            "/api/ai/search/count", new SemanticSearchRequest { Query = "test query", Scope = "all" },
            _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Multiple Tenant Tests

    [Fact]
    public async Task Search_DifferentTenants_AreIsolated()
    {
        // Arrange — both callers hold Read on the parent, so the only variable left is the tenant.
        var callerA = Guid.NewGuid().ToString();
        var callerB = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerA, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        _fixture.Access.GrantRecord(callerB, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);

        var clientTenantA = _fixture.CreateAuthenticatedClient(TenantA, callerA);
        var clientTenantB = _fixture.CreateAuthenticatedClient(TenantB, callerB);

        // Act
        var responseA = await clientTenantA.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);
        var responseB = await clientTenantB.PostAsJsonAsync("/api/ai/search", EntityScopeRequest(), _jsonOptions);

        // Assert - Both succeed but are isolated by tenant
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify requests were processed with correct tenant context
        var contentA = await responseA.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);
        var contentB = await responseB.Content.ReadFromJsonAsync<SemanticSearchResponse>(_jsonOptions);

        contentA!.Metadata.Should().NotBeNull();
        contentB!.Metadata.Should().NotBeNull();
    }

    #endregion

    #region Authentication Tests

    [Fact]
    public async Task Search_NoAuthHeader_Returns_401()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Search_InvalidToken_Returns_401()
    {
        // Arrange
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Search_ExpiredToken_Returns_401()
    {
        // Arrange
        var client = _fixture.CreateClientWithExpiredToken(TenantA);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Count Endpoint Authorization Tests

    [Fact]
    public async Task Count_WithValidAuthAndParentAccess_Returns_Ok()
    {
        // Arrange
        var callerId = Guid.NewGuid().ToString();
        _fixture.Access.GrantRecord(callerId, "sprk_matters", Guid.Parse(TestEntityId), AccessRights.Read);
        var client = _fixture.CreateAuthenticatedClient(TenantA, callerId);

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/count", EntityScopeRequest(), _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Count_WithoutAuth_Returns_401()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new SemanticSearchRequest
        {
            Scope = "entity",
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/search/count", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Malformed-input Tests

    [Fact]
    public async Task Search_EntityScope_WhenEntityIdIsNotAGuid_Returns400()
    {
        // Was `Search_EntityScope_AuthorizationGranted`, which passed `EntityId = "test-entity-id"` and
        // asserted 200 — a non-GUID entity id could not have identified any record, so the 200 it
        // asserted was evidence that nothing was being resolved or checked.
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = "matter",
            EntityId = "test-entity-id"
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_EntityScope_WhenEntityIdIsEmptyGuid_Returns400()
    {
        var client = _fixture.CreateAuthenticatedClient(TenantA);
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = "entity",
            EntityType = "matter",
            EntityId = Guid.Empty.ToString()
        };

        var response = await client.PostAsJsonAsync("/api/ai/search", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion
}

/// <summary>
/// Test fixture for semantic search authorization tests.
/// </summary>
public class SemanticSearchAuthorizationTestFixture : WebApplicationFactory<Program>
{
    /// <summary>
    /// The programmable access source. Tests grant rights explicitly; anything not granted is denied,
    /// so a test that forgets to grant sees a denial rather than an accidental allow. That default is
    /// the point — the bug this fixture now covers was an allow-by-default.
    /// </summary>
    public StubAccessDataSource Access { get; } = new();

    /// <summary>The search stub, so tests can control the rows the authorization layer must filter.</summary>
    public MockAuthTestSearchService Search { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        TestHostConfiguration.ConfigureTestHost(builder);
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Configure JWT authentication for testing
            services.AddAuthentication("Test")
                .AddScheme<TestAuthOptions, TestAuthorizationHandler>("Test", options => { });
        });

        // Use ConfigureTestServices to replace services AFTER the app's services are registered
        builder.ConfigureTestServices(services =>
        {
            // Apply shared test service mocks (Dataverse, IChatClient, hosted services, etc.)
            TestHostConfiguration.ConfigureSharedTestServices(services);

            // Override Microsoft Identity Web's PostConfigure which replaces our
            // DefaultAuthenticateScheme/DefaultChallengeScheme. This forces the
            // test authentication handler to be used throughout the request pipeline.
            services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            });

            // Replace the real semantic search service with mock
            services.RemoveAll<ISemanticSearchService>();
            services.AddSingleton<ISemanticSearchService>(Search);

            // Replace the access data source so tests can state, per caller and per record, exactly
            // what Dataverse would answer. Mocked at the module boundary (ADR-038 permits this; the
            // banned shape is transport-level mocking such as Mock<HttpMessageHandler>).
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(Access);
        });

        builder.UseEnvironment("Testing");
    }

    public HttpClient CreateAuthenticatedClient(string tenantId, string? userId = null)
    {
        var client = CreateClient();
        var token = GenerateTestJwt(tenantId, userId ?? Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateClientWithInvalidTenantClaim()
    {
        var client = CreateClient();
        // Token without tid claim
        var token = GenerateTestJwtWithoutTenant(Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateClientWithExpiredToken(string tenantId)
    {
        var client = CreateClient();
        var token = GenerateExpiredTestJwt(tenantId, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string GenerateTestJwt(string tenantId, string userId)
    {
        var claims = new[]
        {
            new Claim("tid", tenantId),
            new Claim("oid", userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-for-jwt-token-generation-minimum-32-chars"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "https://test.spaarke.local",
            audience: "api://spaarke-test",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateTestJwtWithoutTenant(string userId)
    {
        // Deliberately omit tid claim
        var claims = new[]
        {
            new Claim("oid", userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-for-jwt-token-generation-minimum-32-chars"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "https://test.spaarke.local",
            audience: "api://spaarke-test",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateExpiredTestJwt(string tenantId, string userId)
    {
        var claims = new[]
        {
            new Claim("tid", tenantId),
            new Claim("oid", userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-for-jwt-token-generation-minimum-32-chars"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Expired 1 hour ago
        var token = new JwtSecurityToken(
            issuer: "https://test.spaarke.local",
            audience: "api://spaarke-test",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// A programmable <see cref="IAccessDataSource"/>: tests state what Dataverse would answer for a given
/// caller and record. Anything not granted is <see cref="AccessRights.None"/>.
/// </summary>
/// <remarks>
/// Deny-by-default is deliberate. The defect this fixture exists to cover was an allow-by-default
/// authorization filter, so a stub that allowed unstated cases would reproduce the bug inside the test
/// harness and every negative test would pass for the wrong reason.
/// </remarks>
public sealed class StubAccessDataSource : IAccessDataSource
{
    private readonly Dictionary<string, AccessRights> _recordRights = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AccessRights> _documentRights = new(StringComparer.OrdinalIgnoreCase);

    public void GrantRecord(string userId, string entitySetName, Guid recordId, AccessRights rights) =>
        _recordRights[$"{userId}|{entitySetName}|{recordId}"] = rights;

    public void GrantDocument(string userId, string documentId, AccessRights rights) =>
        _documentRights[$"{userId}|{documentId}"] = rights;

    public Task<AccessSnapshot> GetUserAccessAsync(
        string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
    {
        var rights = _documentRights.TryGetValue($"{userId}|{resourceId}", out var r)
            ? r
            : AccessRights.None;

        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessRights = rights
        });
    }

    public Task<AccessSnapshot> GetRecordAccessAsync(
        string userId, string entitySetName, Guid recordId, string? userAccessToken,
        CancellationToken ct = default)
    {
        var rights = _recordRights.TryGetValue($"{userId}|{entitySetName}|{recordId}", out var r)
            ? r
            : AccessRights.None;

        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = recordId.ToString(),
            AccessRights = rights
        });
    }
}

/// <summary>
/// Mock search service for authorization tests. <see cref="Results"/> lets a test supply the rows the
/// authorization layer is then expected to filter, so result-level enforcement is observable.
/// </summary>
public class MockAuthTestSearchService : ISemanticSearchService
{
    public IReadOnlyList<SearchResult> Results { get; set; } = [];

    public Task<SemanticSearchResponse> SearchAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SemanticSearchResponse
        {
            Results = Results,
            Metadata = new SearchMetadata
            {
                TotalResults = Results.Count,
                ReturnedResults = Results.Count,
                SearchDurationMs = 5,
                ExecutedMode = request.Options?.HybridMode ?? "rrf",
                AppliedFilters = new AppliedFilters
                {
                    Scope = request.Scope,
                    EntityType = request.EntityType,
                    EntityId = request.EntityId,
                    DocumentIdCount = request.DocumentIds?.Count
                }
            }
        });
    }

    public Task<SemanticSearchCountResponse> CountAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SemanticSearchCountResponse
        {
            Count = 10,
            AppliedFilters = new AppliedFilters
            {
                Scope = request.Scope,
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                DocumentIdCount = request.DocumentIds?.Count
            }
        });
    }
}

/// <summary>
/// Test authentication handler for authorization tests.
/// Validates token expiration and tenant claims.
/// </summary>
internal class TestAuthorizationHandler : Microsoft.AspNetCore.Authentication.AuthenticationHandler<TestAuthOptions>
{
    public TestAuthorizationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<TestAuthOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
        }

        var token = authHeader["Bearer ".Length..].Trim();

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            // Check expiration
            if (jwtToken.ValidTo < DateTime.UtcNow)
            {
                return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail("Token expired"));
            }

            var claims = jwtToken.Claims.ToList();
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, "Test");

            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail(ex));
        }
    }
}

internal class TestAuthOptions : Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions
{
}
