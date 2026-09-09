using System.Net;
using System.Net.Http.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// FR-08 / finding A-7 — <c>PATCH /api/v1/external/todos/{id}</c> must scope-check the target
/// to-do against the caller's accessible root set BEFORE writing.
///
/// <para><b>What was wrong.</b> The handler applied the PATCH with no record-scope check at all and
/// said so in a comment ("we can't easily check project membership without looking up the to-do…
/// acceptable for now given the app's low blast radius"). The blast radius was not low: the route
/// takes an arbitrary to-do GUID, not one derived from the caller, so any caller who resolved to a
/// <see cref="CallerPrincipal"/> could rename, re-prioritise, re-date or close ANY to-do in the
/// tenant.</para>
///
/// <para><b>Why this file exists at all.</b> Task 001 could not pin A-7 — the PATCH handler was
/// unreachable offline behind <c>CallerPrincipalAuthorizationFilter</c> — so this task owns its
/// coverage entirely. The unlock is that the filter resolves through
/// <see cref="ICallerPrincipalResolver"/>, an interface registered <c>AddScoped</c>, so the
/// principal can be supplied directly with no Dataverse dependency.</para>
///
/// <para><b>The load-bearing assertion is <c>UpdateCallCount</c>, not the status code.</b> A 403
/// alone would pass even if the PATCH had already been issued before the check ran. Every deny test
/// asserts the write never happened. This is the task-017 lesson applied: when the deliverable is
/// "X must not happen", assert on X — not on the response that accompanies it.</para>
///
/// Placement: <c>tests/integration/auth/**</c> — the ADR-038 §2 security-auth KEEP path.
/// </summary>
public sealed class ExternalTodoScopeTests : IClassFixture<ExternalTodoScopeTestFixture>
{
    private readonly ExternalTodoScopeTestFixture _fixture;

    private static readonly Guid InScopeProject = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherProject = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TodoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid InScopeMatter = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherMatter = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid InScopeWorkAssignment = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid OtherWorkAssignment = Guid.Parse("77777777-7777-7777-7777-777777777777");

    public ExternalTodoScopeTests(ExternalTodoScopeTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static object ValidPatch() => new { sprk_name = "renamed" };

    /// <summary>Builds a resolvable principal holding exactly the supplied project participations.</summary>
    private static CallerPrincipal PrincipalWith(params (Guid ProjectId, ExternalAccessLevel Level)[] access) =>
        new()
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            Email = "external.user@example.test",
            ProjectAccess = access
                .Select(a => CallerProjectAccess.FromLevel(a.ProjectId, a.Level))
                .ToList()
        };

    /// <summary>Builds a principal across all three A-9 root sets.</summary>
    private static CallerPrincipal PrincipalWithRoots(
        (Guid ProjectId, ExternalAccessLevel Level)[]? projects = null,
        Guid[]? matters = null,
        Guid[]? workAssignments = null) =>
        new()
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            Email = "external.user@example.test",
            ProjectAccess = (projects ?? Array.Empty<(Guid, ExternalAccessLevel)>())
                .Select(a => CallerProjectAccess.FromLevel(a.ProjectId, a.Level))
                .ToList(),
            // Task 033: the id sets are DERIVED from the rights maps, so scope is expressed by
            // populating rights. These roots default to Collaborate — the level matter/WA membership
            // effectively carried before FR-19, so the pre-existing cases keep their meaning. The
            // ViewOnly-cannot-write cases below pass rights explicitly.
            MatterAccess = (matters ?? Array.Empty<Guid>())
                .ToDictionary(id => id, _ => ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate)),
            WorkAssignmentAccess = (workAssignments ?? Array.Empty<Guid>())
                .ToDictionary(id => id, _ => ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate))
        };

    // =====================================================================
    // Positive — no over-denial
    // =====================================================================

    [Fact]
    public async Task PatchExternalTodo_WhenTodoRootIsInCallerAccessibleSet_AppliesTheUpdate()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWith((InScopeProject, ExternalAccessLevel.Collaborate));
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, InScopeProject, "Existing to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "a caller whose accessible set covers the to-do's project may update it (FR-08 no-over-denial)");
        _fixture.Data.UpdateCallCount.Should().Be(1);
        _fixture.Data.LastUpdatedTodoId.Should().Be(TodoId,
            "the write must target the to-do named in the route, not a substitute");
    }

    [Fact]
    public async Task PatchExternalTodo_WhenCallerHasFullAccess_AppliesTheUpdate()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWith((InScopeProject, ExternalAccessLevel.FullAccess));
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, InScopeProject, "Existing to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.Data.UpdateCallCount.Should().Be(1);
    }

    // =====================================================================
    // Negative — the A-7 vulnerability itself
    // =====================================================================

    [Fact]
    public async Task PatchExternalTodo_WhenTodoRootIsOutsideCallerAccessibleSet_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        // Caller legitimately holds one project; the to-do belongs to a different one.
        _fixture.Principal = PrincipalWith((InScopeProject, ExternalAccessLevel.FullAccess));
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, OtherProject, "Someone else's to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "out-of-scope records get the same 403 the scoped read path returns");
        _fixture.Data.UpdateCallCount.Should().Be(0,
            "THE A-7 REGRESSION GUARD — the PATCH must never reach Dataverse for an out-of-scope to-do");

        // ⚠️ This assertion's REASON changed in task 033 (FR-19), though its text did not.
        //
        // It was written when the handler had TWO guards — a record-scope check followed by a rights
        // check — and it pinned WHICH one denied, because deleting the scope check alone would
        // otherwise have left every test green.
        //
        // There is now ONE guard. Rights accessors return None for a record outside the caller's set,
        // so "out of scope" and "insufficient rights" are the same expression and cannot drift apart.
        // That strictly strengthens the original perturbation property: the check this test protects
        // can no longer be half-removed, and removing it entirely fails the UpdateCallCount assertion
        // above. Re-verified by perturbation 2026-09-04 — see notes/task-033-consumer-propagation.md.
        //
        // The message is still asserted, for a different reason: an out-of-scope caller and an
        // under-privileged caller must receive the SAME response, so neither can infer which one they
        // are. A future edit that reintroduces a distinct "your access level..." message here would
        // leak that distinction, and this line catches it.
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("You do not have access to this to-do",
                "out-of-scope and insufficient-rights must be indistinguishable to the caller");
    }

    [Fact]
    public async Task PatchExternalTodo_WhenCallerHasZeroAccessibleRoots_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWith(); // resolvable identity, no participations at all
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, InScopeProject, "Existing to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "FR-08 acceptance: a caller with zero accessible roots can modify nothing");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenTodoHasNoResolvableProjectRoot_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWith((InScopeProject, ExternalAccessLevel.FullAccess));
        // Exists, but parented to one of the TEN regarding types with no accessible set
        // (document, invoice, communication, …). Matter and work assignment ARE scopeable
        // as of the 2026-08-24 owner decision and are covered separately below.
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.None, null, "To-do regarding a non-scopeable parent");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a parent with no accessible set ⇒ deny (ADR-003 fail closed)");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenTodoDoesNotExist_DeniesWithoutDisclosingAndDoesNotWrite()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWith((InScopeProject, ExternalAccessLevel.FullAccess));
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.None, null, null); // absent OR unreadable

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a nonexistent to-do denies cleanly rather than erroring unhandled");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenTodoLookupFails_FailsClosedAndDoesNotWrite()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWith((InScopeProject, ExternalAccessLevel.FullAccess));
        // ADR-003: GetTodoProjectAsync collapses a Dataverse fault to (null, null) — identical to
        // absent. The point of this test is that an ERRORED read can never fall through to a write.
        _fixture.Data.ThrowOnLookup = true;

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.IsSuccessStatusCode.Should().BeFalse(
            "ADR-003: an errored scope lookup must DENY, never apply the PATCH");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    // =====================================================================
    // Matter / work-assignment roots — owner decision 2026-08-24: same
    // functionality as project. Membership implies write for these two,
    // because neither accessible set carries an access level.
    // =====================================================================

    [Fact]
    public async Task PatchExternalTodo_WhenTodoRootIsAnAccessibleMatter_AppliesTheUpdate()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWithRoots(matters: new[] { InScopeMatter });
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Matter, InScopeMatter, "Matter to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "owner decision 2026-08-24: matter parents get the same functionality as project");
        _fixture.Data.UpdateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenTodoRootIsAnAccessibleWorkAssignment_AppliesTheUpdate()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWithRoots(workAssignments: new[] { InScopeWorkAssignment });
        _fixture.Data.TodoLookupResult =
            (ExternalDataService.TodoRootKind.WorkAssignment, InScopeWorkAssignment, "WA to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.Data.UpdateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenTodoRootIsAMatterOutsideTheAccessibleSet_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWithRoots(matters: new[] { InScopeMatter });
        _fixture.Data.TodoLookupResult =
            (ExternalDataService.TodoRootKind.Matter, OtherMatter, "Someone else's matter to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "widening to matter parents must not become 'any matter'");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenTodoRootIsAWorkAssignmentOutsideTheAccessibleSet_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWithRoots(workAssignments: new[] { InScopeWorkAssignment });
        _fixture.Data.TodoLookupResult =
            (ExternalDataService.TodoRootKind.WorkAssignment, OtherWorkAssignment, "Someone else's WA to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "widening to work-assignment parents must not become 'any work assignment' — without "
            + "this test, deleting the membership check entirely is invisible (verified by perturbation)");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenCallerHoldsTheProjectButTodoIsOnAnUnheldMatter_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        // Holds a project, holds NO matters. A matter-parented to-do must not ride in on project access.
        _fixture.Principal = PrincipalWith((InScopeProject, ExternalAccessLevel.FullAccess));
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Matter, InScopeMatter, "Matter to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the three root sets are independent — holding one must not confer access to another");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenTodoHasMoreThanOneRootLookupPopulated_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        // Caller holds everything; the to-do is still denied purely for being ambiguous.
        _fixture.Principal = PrincipalWithRoots(
            projects: new[] { (InScopeProject, ExternalAccessLevel.FullAccess) },
            matters: new[] { InScopeMatter },
            workAssignments: new[] { InScopeWorkAssignment });
        _fixture.Data.TodoLookupResult =
            (ExternalDataService.TodoRootKind.Ambiguous, null, "To-do with two parents");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "ADR-024 says one parent, but the lookups are independent columns and nothing enforces "
            + "it — honouring whichever root the caller happens to hold would let them write a record "
            + "that is also parented somewhere they do not");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    // =====================================================================
    // Rights — a PATCH needs Write, mirroring CreateTodo's Create gate
    // =====================================================================

    [Fact]
    public async Task PatchExternalTodo_WhenCallerIsViewOnlyOnTheProject_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWith((InScopeProject, ExternalAccessLevel.ViewOnly));
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, InScopeProject, "Existing to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "ViewOnly maps to AccessRights.Read — read access must not confer write access");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenCallerIsViewOnlyOnTheProject_DeniesWithAMachineReadableReasonCode()
    {
        // FR-19 acceptance: the deny is machine-readable, not just a status code. ADR-003 requires a
        // deny code on every authorization refusal; the three sibling POST routes carry the same one.
        _fixture.Reset();
        _fixture.Principal = PrincipalWith((InScopeProject, ExternalAccessLevel.ViewOnly));
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, InScopeProject, "Existing to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("sdap.access.deny.insufficient_rights");
    }

    // ── The asymmetry FR-19 removed: matter / WA membership no longer implies write ───────────────
    //
    // Until 2026-09-04 these two cases would have returned 204 and WRITTEN. Matter and work-assignment
    // access were bare id sets, so the handler had no level to honour and treated membership as
    // permission to write — documented in the handler as deliberate. Tasks 032/033 gave both root
    // types real per-record rights, so a ViewOnly grant is now honoured everywhere.

    [Fact]
    public async Task PatchExternalTodo_WhenCallerIsViewOnlyOnTheMatter_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWithRootRights(
            matterRights: new[] { (InScopeMatter, ExternalAccessLevel.ViewOnly) });
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Matter, InScopeMatter, "Matter to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a ViewOnly matter grant must not permit a write — this is the asymmetry FR-19 removed");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenCallerIsViewOnlyOnTheWorkAssignment_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWithRootRights(
            workAssignmentRights: new[] { (InScopeWorkAssignment, ExternalAccessLevel.ViewOnly) });
        _fixture.Data.TodoLookupResult =
            (ExternalDataService.TodoRootKind.WorkAssignment, InScopeWorkAssignment, "WA to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a ViewOnly work-assignment grant must not permit a write");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenCallerIsCollaborateOnTheMatter_AppliesTheUpdate()
    {
        // POSITIVE CONTROL for the two denials above. Without it, a bug that denied every matter-rooted
        // PATCH would leave them green while breaking the feature.
        _fixture.Reset();
        _fixture.Principal = PrincipalWithRootRights(
            matterRights: new[] { (InScopeMatter, ExternalAccessLevel.Collaborate) });
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Matter, InScopeMatter, "Matter to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.Data.UpdateCallCount.Should().Be(1);
    }

    /// <summary>A principal whose matter / work-assignment roots carry EXPLICIT levels (task 033).</summary>
    private static CallerPrincipal PrincipalWithRootRights(
        (Guid Id, ExternalAccessLevel Level)[]? matterRights = null,
        (Guid Id, ExternalAccessLevel Level)[]? workAssignmentRights = null) =>
        new()
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            Email = "external.user@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = (matterRights ?? Array.Empty<(Guid, ExternalAccessLevel)>())
                .ToDictionary(x => x.Id, x => ExternalAccessLevels.ToAccessRights(x.Level)),
            WorkAssignmentAccess = (workAssignmentRights ?? Array.Empty<(Guid, ExternalAccessLevel)>())
                .ToDictionary(x => x.Id, x => ExternalAccessLevels.ToAccessRights(x.Level)),
        };

    // =====================================================================
    // Surface pinning — the request is a closed DTO, not a property bag
    // =====================================================================

    [Fact]
    public async Task PatchExternalTodo_WithUnknownFieldsInBody_IgnoresThemAndDoesNotForwardThem()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWith((InScopeProject, ExternalAccessLevel.FullAccess));
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, InScopeProject, "Existing to-do");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", new
        {
            sprk_name = "renamed",
            // Attempts to re-parent the to-do or escalate ownership must not pass through.
            sprk_regardingproject = OtherProject,
            ownerid = Guid.NewGuid(),
            statecode = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Behavioural assertion (not a shape assertion): the request object the service received
        // carries the one named field and nothing else. `UpdateExternalTodoRequest` is a closed DTO,
        // so sprk_regardingproject / ownerid / statecode are dropped at deserialization and cannot
        // reach Dataverse. If someone later adds an open property bag or a Regarding member, the
        // re-parenting attempt above starts arriving and this fails.
        var received = _fixture.Data.LastRequest;
        received.Should().NotBeNull();
        received!.SprkName.Should().Be("renamed", "the named field is honoured");
        received.SprkNotes.Should().BeNull();
        received.SprkDuedate.Should().BeNull();
        received.SprkPriorityscore.Should().BeNull();
        received.SprkEffortscore.Should().BeNull();
        received.SprkTodocolumn.Should().BeNull(
            "an unknown-field PATCH must not smuggle a column value through");
        received.SprkTodopinned.Should().BeNull();
        received.Statuscode.Should().BeNull(
            "the statecode/statuscode the caller sent must NOT be honoured — re-opening or closing "
            + "a to-do is not part of the fields this PATCH accepts implicitly");
    }

    // =====================================================================
    // Task 029 — LIST + CREATE parity across the three accessible roots.
    //
    // Task 009 widened PATCH to matter- and work-assignment-parented to-dos and left list and create
    // project-only, so the plane's WRITE surface was wider than its READ surface. These tests pin the
    // symmetry in both directions: every root can be listed and created by a caller who holds it, and
    // by no one else.
    //
    // The load-bearing assertions are ListCallCount / CreateCallCount, not the status code — the
    // task-009 lesson. A 403 alone would pass even if the read or the write had already been issued.
    // =====================================================================

    private static object ValidCreate() => new { sprk_name = "new to-do" };

    public static TheoryData<string, ExternalDataService.TodoRootKind, Guid> InScopeRoots() => new()
    {
        { "projects", ExternalDataService.TodoRootKind.Project, InScopeProject },
        { "matters", ExternalDataService.TodoRootKind.Matter, InScopeMatter },
        { "workassignments", ExternalDataService.TodoRootKind.WorkAssignment, InScopeWorkAssignment },
    };

    public static TheoryData<string, Guid> OutOfScopeRoots() => new()
    {
        { "projects", OtherProject },
        { "matters", OtherMatter },
        { "workassignments", OtherWorkAssignment },
    };

    /// <summary>A caller holding all three roots at Collaborate.</summary>
    private static CallerPrincipal PrincipalHoldingAllRoots() =>
        PrincipalWithRoots(
            projects: new[] { (InScopeProject, ExternalAccessLevel.Collaborate) },
            matters: new[] { InScopeMatter },
            workAssignments: new[] { InScopeWorkAssignment });

    // ---- LIST: positive, one per root ----

    [Theory]
    [MemberData(nameof(InScopeRoots))]
    public async Task ListExternalTodos_WhenRootIsInCallerAccessibleSet_ListsThatRoot(
        string segment, ExternalDataService.TodoRootKind expectedKind, Guid rootId)
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalHoldingAllRoots();

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/v1/external/{segment}/{rootId}/todos");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Not just "it was called" — called for the RIGHT root. A handler that cross-wired matter to
        // the project kind would still return 200 with an empty list.
        _fixture.Data.ListCallCount.Should().Be(1);
        _fixture.Data.LastListArgs.Should().Be((expectedKind, rootId));
    }

    // ---- LIST: negative, one per root ----

    [Theory]
    [MemberData(nameof(OutOfScopeRoots))]
    public async Task ListExternalTodos_WhenRootIsOutsideAccessibleSet_IsDeniedAndDoesNotRead(
        string segment, Guid rootId)
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalHoldingAllRoots();

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/v1/external/{segment}/{rootId}/todos");

        // ADR-003: the denial is a DENIAL. An empty 200 would be indistinguishable from "this record
        // has no to-dos" — the denial would be invisible to the caller and to any auditor.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an out-of-scope root must deny, never return an empty collection");
        _fixture.Data.ListCallCount.Should().Be(0,
            "the scope check must run BEFORE the Dataverse read, not filter its results");
    }

    /// <summary>
    /// The negative counterpart that the empty-collection failure mode most needs: the deny path is
    /// asserted on the BODY, not only the status. If a future change made a denied list return 200
    /// with <c>[]</c>, the status assertion above would fail — but if it returned 403 while still
    /// having read Dataverse, only the call count catches it. Both are pinned.
    /// </summary>
    [Fact]
    public async Task ListExternalTodos_WhenDenied_ReturnsNoTodoCollectionAtAll()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalHoldingAllRoots();

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/v1/external/matters/{OtherMatter}/todos");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("\"value\"",
            "a denied list must not shape-match a successful empty collection");
    }

    // ---- CREATE: positive, one per root ----

    [Theory]
    [MemberData(nameof(InScopeRoots))]
    public async Task CreateExternalTodo_WhenRootIsInCallerAccessibleSet_CreatesAgainstThatRoot(
        string segment, ExternalDataService.TodoRootKind expectedKind, Guid rootId)
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalHoldingAllRoots();

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fixture.Data.CreateCallCount.Should().Be(1);
        _fixture.Data.LastCreateArgs.Should().Be((expectedKind, rootId),
            "the parent flows from the ROUTE — the root gated must be the root written");
    }

    // ---- CREATE: negative (out of scope), one per root ----

    [Theory]
    [MemberData(nameof(OutOfScopeRoots))]
    public async Task CreateExternalTodo_WhenRootIsOutsideAccessibleSet_IsDeniedAndDoesNotWrite(
        string segment, Guid rootId)
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalHoldingAllRoots();

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.CreateCallCount.Should().Be(0,
            "no Dataverse write may be issued for a root the caller cannot reach");
    }

    // ---- CREATE: the level decision, one per levelled root ----

    /// <summary>
    /// 🔴 The create-side level decision (task 029), pinned as behaviour.
    ///
    /// <para>The task file assumed matter and work-assignment access carried NO level, forcing a choice
    /// between "membership implies create" and blocking create on those roots. Tasks 032+033 removed
    /// that premise — <see cref="CallerPrincipal"/> carries per-record <c>AccessRights</c> for all
    /// three roots. So a ViewOnly matter participant is denied create, exactly as a ViewOnly project
    /// participant always was. If anyone later restores membership-implies-create for the levelless
    /// roots, these two fail.</para>
    /// </summary>
    [Fact]
    public async Task CreateExternalTodo_WhenCallerHoldsTheMatterViewOnly_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWithRootRights(
            matterRights: new[] { (InScopeMatter, ExternalAccessLevel.ViewOnly) });

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/matters/{InScopeMatter}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "membership in a matter must not by itself confer Create — the level is carried now");
        _fixture.Data.CreateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateExternalTodo_WhenCallerHoldsTheWorkAssignmentViewOnly_IsDeniedAndDoesNotWrite()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWithRootRights(
            workAssignmentRights: new[] { (InScopeWorkAssignment, ExternalAccessLevel.ViewOnly) });

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/external/workassignments/{InScopeWorkAssignment}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.CreateCallCount.Should().Be(0);
    }

    /// <summary>
    /// A ViewOnly holder can still LIST — the level gates the write, not the read. Without this, the
    /// two tests above would also pass if Read had been required for create by accident.
    /// </summary>
    [Fact]
    public async Task ListExternalTodos_WhenCallerHoldsTheMatterViewOnly_IsAllowed()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWithRootRights(
            matterRights: new[] { (InScopeMatter, ExternalAccessLevel.ViewOnly) });

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/v1/external/matters/{InScopeMatter}/todos");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.Data.ListCallCount.Should().Be(1);
    }

    /// <summary>
    /// Cross-wiring guard: holding a MATTER must not admit the same GUID as a work assignment. The
    /// three root sets are separate namespaces; a handler that consulted the wrong one would pass
    /// every single-root test above.
    /// </summary>
    [Fact]
    public async Task ListExternalTodos_WhenTheHeldIdIsAskedForUnderTheWrongRootType_IsDenied()
    {
        _fixture.Reset();
        _fixture.Principal = PrincipalWithRootRights(
            matterRights: new[] { (InScopeMatter, ExternalAccessLevel.Collaborate) });

        using var client = _fixture.CreateAuthenticatedClient();
        // Same GUID the caller holds AS A MATTER, asked for as a work assignment.
        var response = await client.GetAsync($"/api/v1/external/workassignments/{InScopeMatter}/todos");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.ListCallCount.Should().Be(0);
    }

    // =====================================================================
    // Task 029 — the OData and the payload, asserted where they are BUILT.
    //
    // The review constraint, restated: mocking at a seam proves the CALLER, never the CALLEE. Every
    // test above substitutes GetTodosAsync / CreateTodoAsync, so NONE of them can see which column
    // the filter names or which navigation property the body binds. Those live in pure functions and
    // are asserted directly here — otherwise the widening ships untested (the task-017 shape).
    // =====================================================================

    private const string ApiUrl = "https://example.crm.dynamics.com/api/data/v9.2";

    [Theory]
    [InlineData(ExternalDataService.TodoRootKind.Project, "_sprk_regardingproject_value")]
    [InlineData(ExternalDataService.TodoRootKind.Matter, "_sprk_regardingmatter_value")]
    [InlineData(ExternalDataService.TodoRootKind.WorkAssignment, "_sprk_regardingworkassignment_value")]
    public void BuildTodoListUrl_FiltersOnTheRootsOwnLookupColumn(
        ExternalDataService.TodoRootKind kind, string expectedAttribute)
    {
        var rootId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var url = ExternalDataService.BuildTodoListUrl(ApiUrl, kind, rootId);

        // The $filter is URL-escaped in the emitted URL, so assert on the decoded form.
        Uri.UnescapeDataString(url).Should().Contain($"$filter={expectedAttribute} eq {rootId}");

        // ...and on nothing else: a filter naming two columns would satisfy a Contain on either.
        FilterOf(url).Should().Be($"{expectedAttribute} eq {rootId}");
    }

    /// <summary>Decodes the single <c>$filter</c> clause out of an emitted query URL.</summary>
    private static string FilterOf(string url)
    {
        var decoded = Uri.UnescapeDataString(url);
        var start = decoded.IndexOf("$filter=", StringComparison.Ordinal) + "$filter=".Length;
        var end = decoded.IndexOf('&', start);
        return end < 0 ? decoded[start..] : decoded[start..end];
    }

    [Fact]
    public void BuildTodoListUrl_NamesADifferentColumnForEveryRoot()
    {
        var rootId = Guid.NewGuid();

        var filters = new[]
            {
                ExternalDataService.TodoRootKind.Project,
                ExternalDataService.TodoRootKind.Matter,
                ExternalDataService.TodoRootKind.WorkAssignment,
            }
            .Select(k => FilterOf(ExternalDataService.BuildTodoListUrl(ApiUrl, k, rootId)))
            .ToArray();

        filters.Should().OnlyHaveUniqueItems(
            "two roots sharing a filter column means one of them is silently listing the other's to-dos");
    }

    [Theory]
    [InlineData(ExternalDataService.TodoRootKind.None)]
    [InlineData(ExternalDataService.TodoRootKind.Ambiguous)]
    public void BuildTodoListUrl_ForADenyState_Throws(ExternalDataService.TodoRootKind kind)
    {
        // None and Ambiguous are deny states, not roots. Reaching the data layer with one means a
        // gate was skipped; failing loudly beats emitting a query with an empty or wrong filter.
        var act = () => ExternalDataService.BuildTodoListUrl(ApiUrl, kind, Guid.NewGuid());

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(ExternalDataService.TodoRootKind.Project, "sprk_RegardingProject@odata.bind", "/sprk_projects(")]
    [InlineData(ExternalDataService.TodoRootKind.Matter, "sprk_RegardingMatter@odata.bind", "/sprk_matters(")]
    [InlineData(ExternalDataService.TodoRootKind.WorkAssignment, "sprk_RegardingWorkAssignment@odata.bind", "/sprk_workassignments(")]
    public void BuildTodoCreatePayload_BindsExactlyTheRootsOwnNavigationProperty(
        ExternalDataService.TodoRootKind kind, string expectedBindKey, string expectedEntitySetPrefix)
    {
        var rootId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var binding = ExternalDataService.TryGetRootBinding(kind)!;

        var body = ExternalDataService.BuildTodoCreatePayload(
            new CreateExternalTodoRequest { SprkName = "n" }, binding, rootId, "Display Name", null);

        body.Should().ContainKey(expectedBindKey);
        body[expectedBindKey].Should().Be($"{expectedEntitySetPrefix}{rootId})");

        // And NO other root's lookup — the ADR-024 one-parent rule at the point of construction.
        var otherBinds = body.Keys
            .Where(k => k.StartsWith("sprk_Regarding", StringComparison.Ordinal) && k != expectedBindKey)
            .ToArray();
        otherBinds.Should().BeEmpty();
    }

    /// <summary>
    /// 🔴 The resolver fields must carry the PARENT's entity, not <c>sprk_project</c>.
    ///
    /// <para>This is the assertion that was impossible before task 029 moved the resolver-field
    /// construction onto the pure path. While it lived inside <c>CreateTodoAsync</c> — a substitution
    /// seam every endpoint test replaces — stamping <c>sprk_project</c> into a matter-parented create
    /// would have failed ZERO tests. A matter to-do carrying a project resolver renders a broken
    /// cross-entity link and mis-reports its own parent type to every consumer.</para>
    /// </summary>
    [Theory]
    [InlineData(ExternalDataService.TodoRootKind.Project, "sprk_project")]
    [InlineData(ExternalDataService.TodoRootKind.Matter, "sprk_matter")]
    [InlineData(ExternalDataService.TodoRootKind.WorkAssignment, "sprk_workassignment")]
    public void BuildTodoCreatePayload_StampsTheParentsOwnEntityIntoTheResolverFields(
        ExternalDataService.TodoRootKind kind, string expectedEntity)
    {
        var rootId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var binding = ExternalDataService.TryGetRootBinding(kind)!;

        var body = ExternalDataService.BuildTodoCreatePayload(
            new CreateExternalTodoRequest { SprkName = "n" }, binding, rootId, "The Parent", null);

        body["sprk_regardingrecordid"].Should().Be(rootId.ToString("D").ToLowerInvariant());
        body["sprk_regardingrecordname"].Should().Be("The Parent");
        body["sprk_regardingrecordurl"].As<string>().Should().Contain($"etn={expectedEntity}",
            "the resolver URL must point at the PARENT's entity");
        body["sprk_regardingrecordurl"].As<string>().Should()
            .Contain(rootId.ToString("D").ToLowerInvariant());
    }

    [Fact]
    public void BuildTodoCreatePayload_WhenTheRecordTypeRefResolves_BindsItAlongsideTheParent()
    {
        var rootId = Guid.NewGuid();
        var recordTypeRefId = Guid.NewGuid();
        var binding = ExternalDataService.TryGetRootBinding(ExternalDataService.TodoRootKind.Matter)!;

        var body = ExternalDataService.BuildTodoCreatePayload(
            new CreateExternalTodoRequest { SprkName = "n" }, binding, rootId, "M", recordTypeRefId);

        body["sprk_RegardingRecordType@odata.bind"].Should()
            .Be($"/sprk_recordtype_refs({recordTypeRefId})");
        // ADR-024 atomicity: all four resolver fields ride in the SAME payload as the lookup.
        body.Should().ContainKeys(
            "sprk_regardingrecordid", "sprk_regardingrecordname",
            "sprk_regardingrecordurl", "sprk_RegardingRecordType@odata.bind",
            binding.BindKey);
    }

    [Fact]
    public void BuildTodoCreatePayload_WhenTheRecordTypeRefIsUnresolved_StillWritesTheParentAndTheOtherThree()
    {
        // Non-fatal by design (mirrors the SDK path): a missing sprk_recordtype_ref row costs the
        // cross-entity-view icon, not the association. Failing the create instead would be worse.
        var binding = ExternalDataService.TryGetRootBinding(ExternalDataService.TodoRootKind.WorkAssignment)!;

        var body = ExternalDataService.BuildTodoCreatePayload(
            new CreateExternalTodoRequest { SprkName = "n" }, binding, Guid.NewGuid(), "W", null);

        body.Should().NotContainKey("sprk_RegardingRecordType@odata.bind");
        body.Should().ContainKey(binding.BindKey);
        body.Should().ContainKeys(
            "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl");
    }

    [Fact]
    public void AssertSingleRegardingLookup_WhenTwoParentsAreBound_Throws()
    {
        // A two-parent row is classified Ambiguous by GetTodoRootAsync and then denied to EVERY
        // caller on EVERY route, forever. This surface must not be able to mint one.
        var body = new Dictionary<string, object?>
        {
            ["sprk_RegardingProject@odata.bind"] = "/sprk_projects(11111111-1111-1111-1111-111111111111)",
            ["sprk_RegardingMatter@odata.bind"] = "/sprk_matters(44444444-4444-4444-4444-444444444444)",
        };

        var act = () => ExternalDataService.AssertSingleRegardingLookup(body);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ADR-024*");
    }

    [Fact]
    public void AssertSingleRegardingLookup_WhenNoParentIsBound_Throws()
    {
        // The other half of "exactly one". A to-do created with resolver fields but no parent lookup
        // resolves to TodoRootKind.None — also permanently unreachable.
        var body = new Dictionary<string, object?>
        {
            ["sprk_regardingrecordid"] = "11111111-1111-1111-1111-111111111111",
            ["sprk_RegardingRecordType@odata.bind"] = "/sprk_recordtype_refs(22222222-2222-2222-2222-222222222222)",
        };

        var act = () => ExternalDataService.AssertSingleRegardingLookup(body);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AssertSingleRegardingLookup_TreatsTheResolverTypeBindAsNotAParent()
    {
        // sprk_RegardingRecordType is the resolver's type reference and legitimately coexists with a
        // parent. If the guard counted it, every real create would throw.
        var body = new Dictionary<string, object?>
        {
            ["sprk_RegardingMatter@odata.bind"] = "/sprk_matters(44444444-4444-4444-4444-444444444444)",
            ["sprk_RegardingRecordType@odata.bind"] = "/sprk_recordtype_refs(22222222-2222-2222-2222-222222222222)",
        };

        var act = () => ExternalDataService.AssertSingleRegardingLookup(body);

        act.Should().NotThrow();
    }

    /// <summary>
    /// 🔴 Pins the live-metadata-verified names (queried 2026-09-09 against spaarkedev1 via
    /// <c>RelationshipDefinitions</c> + <c>EntityDefinitions</c>).
    ///
    /// <para>Every one of these is a name that CANNOT be derived: the navigation property is
    /// PascalCase and differs from the attribute; the display-name column is a different name on each
    /// root (<c>sprk_projectname</c> / <c>sprk_mattername</c> / <c>sprk_name</c>) and is NOT the
    /// primary-name attribute for project or matter. This project has found SIX stale-column defects;
    /// a silent edit here is how the seventh arrives.</para>
    /// </summary>
    [Theory]
    [InlineData(ExternalDataService.TodoRootKind.Project, "sprk_project", "sprk_projects",
        "sprk_RegardingProject", "sprk_regardingproject", "sprk_projectname")]
    [InlineData(ExternalDataService.TodoRootKind.Matter, "sprk_matter", "sprk_matters",
        "sprk_RegardingMatter", "sprk_regardingmatter", "sprk_mattername")]
    [InlineData(ExternalDataService.TodoRootKind.WorkAssignment, "sprk_workassignment", "sprk_workassignments",
        "sprk_RegardingWorkAssignment", "sprk_regardingworkassignment", "sprk_name")]
    public void TodoRootBinding_PinsTheLiveMetadataNames(
        ExternalDataService.TodoRootKind kind, string entity, string entitySet,
        string navProperty, string lookupAttribute, string displayNameAttribute)
    {
        var binding = ExternalDataService.TryGetRootBinding(kind);

        binding.Should().NotBeNull();
        binding!.EntityLogicalName.Should().Be(entity);
        binding.EntitySet.Should().Be(entitySet);
        binding.NavigationProperty.Should().Be(navProperty);
        binding.LookupAttribute.Should().Be(lookupAttribute);
        binding.DisplayNameAttribute.Should().Be(displayNameAttribute);
        binding.LookupValueAttribute.Should().Be($"_{lookupAttribute}_value");
        binding.BindKey.Should().Be($"{navProperty}@odata.bind");
    }

    [Theory]
    [InlineData(ExternalDataService.TodoRootKind.None)]
    [InlineData(ExternalDataService.TodoRootKind.Ambiguous)]
    public void TryGetRootBinding_ForADenyState_ReturnsNull(ExternalDataService.TodoRootKind kind) =>
        ExternalDataService.TryGetRootBinding(kind).Should().BeNull();
}

/// <summary>
/// Test host for the FR-08 scope check. Inherits <see cref="ExternalCollaborationTestFixture"/>'s
/// policy fix (which removes the 500-instead-of-401 test-host artifact on the
/// <c>/api/v1/external</c> group) and additionally substitutes the two seams needed to reach the
/// PATCH handler offline:
///
/// <list type="bullet">
///   <item><see cref="ICallerPrincipalResolver"/> — an interface registered <c>AddScoped</c>, so the
///     caller's accessible root set can be set per-test without any Dataverse participation data.</item>
///   <item><see cref="ExternalDataService"/> — a subclass overriding the two <c>virtual</c> members
///     the handler uses. <c>UpdateTodoAsync</c> records invocations so deny tests can assert the
///     write never happened.</item>
/// </list>
///
/// Both substitutions are module-boundary doubles, not transport mocks — <c>Mock&lt;HttpMessageHandler&gt;</c>
/// stays banned (ADR-038 §7 B1).
/// </summary>
public sealed class ExternalTodoScopeTestFixture : ExternalCollaborationTestFixture
{
    public StubExternalDataService Data { get; } = new();

    private readonly StubCallerPrincipalResolver _resolver = new();

    public CallerPrincipal? Principal
    {
        get => _resolver.Principal;
        set => _resolver.Principal = value;
    }

    /// <summary>Clears per-test state. The fixture is class-scoped, so tests must not leak into each other.</summary>
    public void Reset()
    {
        Data.Reset();
        Principal = null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<ICallerPrincipalResolver>(_ => _resolver);
            // Overrides the AddHttpClient<ExternalDataService> typed-client registration
            // (ExternalAccessModule.cs:59) — last registration wins for GetRequiredService.
            services.AddScoped(_ => (ExternalDataService)Data);
        });
    }

    public sealed class StubCallerPrincipalResolver : ICallerPrincipalResolver
    {
        public CallerPrincipal? Principal { get; set; }

        public Task<CallerPrincipalResolution> ResolveAsync(HttpContext httpContext, CancellationToken ct) =>
            Task.FromResult(Principal is null
                ? CallerPrincipalResolution.Denied(Results.Problem(
                    statusCode: 403, title: "Forbidden", detail: "No external principal (test stub)"))
                : CallerPrincipalResolution.Resolved(Principal));
    }

    public sealed class StubExternalDataService : ExternalDataService
    {
        public StubExternalDataService()
            : base(new HttpClient(),
                   new ConfigurationBuilder().Build(),
                   new StubCredential(),
                   NullLogger<ExternalDataService>.Instance)
        {
        }

        public (ExternalDataService.TodoRootKind Kind, Guid? RootId, string? TodoName) TodoLookupResult { get; set; }
            = (ExternalDataService.TodoRootKind.None, null, null);
        public bool ThrowOnLookup { get; set; }

        public int UpdateCallCount { get; private set; }
        public Guid? LastUpdatedTodoId { get; private set; }
        public UpdateExternalTodoRequest? LastRequest { get; private set; }

        // Task 029 — list + create seams. The call COUNTS are the load-bearing assertions on the deny
        // paths, exactly as UpdateCallCount is for the PATCH: a 403 alone would pass even if the read
        // or the write had already been issued.
        public int ListCallCount { get; private set; }
        public (ExternalDataService.TodoRootKind Kind, Guid RootId)? LastListArgs { get; private set; }

        public int CreateCallCount { get; private set; }
        public (ExternalDataService.TodoRootKind Kind, Guid RootId)? LastCreateArgs { get; private set; }
        public CreateExternalTodoRequest? LastCreateRequest { get; private set; }

        public void Reset()
        {
            TodoLookupResult = (ExternalDataService.TodoRootKind.None, null, null);
            ThrowOnLookup = false;
            UpdateCallCount = 0;
            LastUpdatedTodoId = null;
            LastRequest = null;
            ListCallCount = 0;
            LastListArgs = null;
            CreateCallCount = 0;
            LastCreateArgs = null;
            LastCreateRequest = null;
        }

        public override Task<IReadOnlyList<ExternalTodoDto>> GetTodosAsync(
            ExternalDataService.TodoRootKind rootKind, Guid rootId, CancellationToken ct = default)
        {
            ListCallCount++;
            LastListArgs = (rootKind, rootId);
            return Task.FromResult<IReadOnlyList<ExternalTodoDto>>(Array.Empty<ExternalTodoDto>());
        }

        public override Task<ExternalTodoDto> CreateTodoAsync(
            ExternalDataService.TodoRootKind rootKind, Guid rootId,
            CreateExternalTodoRequest request, CancellationToken ct = default)
        {
            CreateCallCount++;
            LastCreateArgs = (rootKind, rootId);
            LastCreateRequest = request;
            return Task.FromResult(new ExternalTodoDto
            {
                SprkTodoid = Guid.NewGuid().ToString(),
                SprkName = request.SprkName,
            });
        }

        public override Task<(ExternalDataService.TodoRootKind Kind, Guid? RootId, string? TodoName)> GetTodoRootAsync(
            Guid todoId, CancellationToken ct = default)
        {
            if (ThrowOnLookup)
                throw new InvalidOperationException("simulated Dataverse fault on the scope lookup");
            return Task.FromResult(TodoLookupResult);
        }

        public override Task UpdateTodoAsync(
            Guid todoId, UpdateExternalTodoRequest request, CancellationToken ct = default)
        {
            UpdateCallCount++;
            LastUpdatedTodoId = todoId;
            LastRequest = request;
            return Task.CompletedTask;
        }

        private sealed class StubCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken ct) =>
                new("stub-token", DateTimeOffset.UtcNow.AddHours(1));

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken ct) =>
                new(new AccessToken("stub-token", DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
