using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.UnifiedAccessControl;

/// <summary>
/// Pins the user-isolation guarantee that <c>WorkspaceLayoutService</c> documents:
/// <i>"All Dataverse queries are filtered by ownerid for user isolation."</i>
///
/// <para><b>Why this file exists.</b> That sentence was false in three independent ways, and the
/// suite could not see any of them:</para>
/// <list type="number">
///   <item><description><b>The list query dropped its own filter.</b> <c>QueryUserLayoutsAsync</c>
///   added the <c>ownerid</c> condition inside <c>if (Guid.TryParse(userId, …))</c>. The caller
///   identifier was the Entra <c>sub</c> — a pairwise, non-GUID string — so the parse always failed
///   and the condition was never added. The query runs on the app identity, so Dataverse row-level
///   security did not trim it either: every caller received every user's layouts.</description></item>
///   <item><description><b>The by-id guard could not execute.</b> It compared
///   <c>entity.GetAttributeValue&lt;EntityReference&gt;("ownerid")</c>, but <c>ownerid</c> was absent
///   from <c>SelectColumns</c>, so the value was always null and <c>ownerId.HasValue</c>
///   short-circuited the check. Since <c>UpdateLayoutAsync</c> and <c>DeleteLayoutAsync</c> both gate
///   on that method, any caller could modify or delete any user's layout.</description></item>
///   <item><description><b>Nothing was ever owned.</b> <c>CreateLayoutAsync</c> set no <c>ownerid</c>
///   and writes through an app-only connection, so Dataverse assigned every row to the service
///   principal. Not one user-owned layout existed.</description></item>
/// </list>
///
/// <para><b>Why the old suite stayed green.</b> The fixture's owner assignment was itself wrapped in
/// <c>if (Guid.TryParse(TestUserId, …))</c> — and <c>TestUserId</c> is not GUID-shaped — so it never
/// set <c>ownerid</c> at all, while its comment claimed it did. Absent column, inert guard, passing
/// test. And every scenario was an allow-path scenario: there was no fixture in which the caller did
/// NOT own the layout, so no test could distinguish an enforced guard from an absent one.</para>
///
/// <para>Hence the shape here: a layout owned by <b>someone else</b>, with every mutation wired to
/// succeed, asserting both the status code AND that no write reached Dataverse. A test that only
/// checked the status code would pass against code that denied for the wrong reason.</para>
/// </summary>
public sealed class WorkspaceLayoutOwnershipIsolationTests
{
    private static readonly Guid ForeignLayoutId = Guid.Parse("7c9e6b21-3f4d-4a8e-b25c-1f0a9d3e6c74");

    // ── read ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLayoutById_LayoutOwnedByAnotherUser_Returns404()
    {
        using var fixture = WorkspaceLayoutTestFixture.WithForeignOwnedLayout(ForeignLayoutId);
        using var client = fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/workspace/layouts/{ForeignLayoutId}");

        // 404 rather than 403: revealing that the id exists would leak another user's layout
        // inventory. The endpoint deliberately collapses "not found" and "not yours".
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a layout owned by another systemuser must not be readable");
    }

    // ── write ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateLayout_LayoutOwnedByAnotherUser_IsDeniedAndWritesNothing()
    {
        using var fixture = WorkspaceLayoutTestFixture.WithForeignOwnedLayout(ForeignLayoutId);
        using var client = fixture.CreateAuthenticatedClient();

        var body = new StringContent(
            """{"name":"Hijacked","layoutTemplateId":"2-column","sectionsJson":"[]","isDefault":false}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PutAsync($"/api/workspace/layouts/{ForeignLayoutId}", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // THE assertion. UpdateAsync is mocked to succeed, so if authorization let the request
        // through, the write lands and this fails. Status code alone would not prove the row
        // was untouched.
        fixture.EntityServiceMock.Verify(
            s => s.UpdateAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "no write may reach Dataverse for a layout the caller does not own");
    }

    [Fact]
    public async Task DeleteLayout_LayoutOwnedByAnotherUser_IsDeniedAndWritesNothing()
    {
        using var fixture = WorkspaceLayoutTestFixture.WithForeignOwnedLayout(ForeignLayoutId);
        using var client = fixture.CreateAuthenticatedClient();

        var response = await client.DeleteAsync($"/api/workspace/layouts/{ForeignLayoutId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Delete is a soft delete (statecode=1) issued via UpdateAsync — same seam as above.
        fixture.EntityServiceMock.Verify(
            s => s.UpdateAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "a caller must not be able to soft-delete another user's layout");
    }

    // ── the list query carries its filter ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLayouts_QueryIsScopedByOwnerid()
    {
        // The structural assertion, and the one that maps directly to the disclosure: inspect the
        // QueryExpression the service actually built. Asserting on the RESULT would not catch this —
        // the mock returns whatever it is told to, so an unfiltered query looks identical to a
        // filtered one from the outside. The defect was in the query, so the query is what we read.
        var captured = new List<QueryExpression>();

        using var fixture = WorkspaceLayoutTestFixture.CapturingQueries(captured);
        using var client = fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/workspace/layouts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var userQuery = captured.FirstOrDefault(q =>
            q.Criteria.Conditions.Any(c =>
                c.AttributeName == "sprk_issystem"
                && c.Operator == ConditionOperator.NotEqual));

        userQuery.Should().NotBeNull("the service must issue a non-system (user-owned) layout query");

        var ownerCondition = userQuery!.Criteria.Conditions
            .FirstOrDefault(c => c.AttributeName == "ownerid");

        ownerCondition.Should().NotBeNull(
            "the user-layout query MUST constrain ownerid — without it the endpoint returns every "
            + "user's layouts, because this query runs on the app identity and Dataverse row-level "
            + "security never trims it");

        ownerCondition!.Values.Should().ContainSingle()
            .Which.Should().Be(
                Guid.Parse(WorkspaceTestConstants.TestSystemUserId),
                "ownerid holds a Dataverse systemuserid; filtering by the caller's Entra oid would "
                + "match nothing and silently empty the list");
    }

    [Fact]
    public async Task GetLayouts_CallerWithNoSystemuser_ReturnsNoUserLayouts_RatherThanAll()
    {
        // Fail-closed. The original construct meant an unresolvable caller ran an UNFILTERED query;
        // the whole point of the fix is that the same condition now denies instead.
        var captured = new List<QueryExpression>();

        using var fixture = WorkspaceLayoutTestFixture.CapturingQueries(captured, resolvesCaller: false);
        using var client = fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/workspace/layouts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var layouts = await response.Content.ReadFromJsonAsync<Sprk.Bff.Api.Api.Workspace.WorkspaceLayoutDto[]>();
        layouts.Should().NotBeNull();
        layouts!.Should().OnlyContain(l => l.IsSystem,
            "an unresolvable caller may still see system layouts, but must receive no user-owned "
            + "rows — least of all everyone's");

        captured.Should().NotContain(
            q => q.Criteria.Conditions.Any(c =>
                     c.AttributeName == "sprk_issystem" && c.Operator == ConditionOperator.NotEqual)
                 && q.Criteria.Conditions.All(c => c.AttributeName != "ownerid"),
            "an unscoped user-layout query must never be issued — that is the disclosure itself");
    }
}
