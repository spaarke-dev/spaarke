using System.Net;
using System.Net.Http.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using WireShape = Sprk.Bff.Api.Tests.TestInfrastructure.CallerRecordAccessProbeDouble.WireShape;

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

    /// <summary>The owning team every payload is built with (task 146: a create without one refuses).</summary>
    private static readonly Guid OwnerTeam = Guid.Parse("0f0f0f0f-0146-4146-8146-000000000146");

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
        _fixture.Data.LastCreateCallerContactId.Should().Be(_fixture.Principal!.ContactId,
            "UAC-r2 task 152: the calling contact is the triggering person — it becomes Assigned To");
        _fixture.Data.LastCreateCreator.Should().BeSameAs(_fixture.Principal,
            "task 030 (FR-25): the creator stamp comes from the principal the route resolved, not one rebuilt downstream");
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

    // ---- UAC-r2 task 152: the external to-do names the calling contact in sprk_assignedto ----

    [Fact]
    public void BuildTodoCreatePayload_WithTheCallingContact_BindsAssignedTo()
    {
        var contactId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var body = ExternalDataService.BuildTodoCreatePayload(
            new CreateExternalTodoRequest { SprkName = "n" },
            ExternalDataService.TryGetRootBinding(ExternalDataService.TodoRootKind.Project)!,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Display", null, OwnerTeam, contactId);

        body.Should().ContainKey(ExternalDataService.AssignedToBindKey)
            .WhoseValue.Should().Be($"/contacts({contactId:D})");
        body.Keys.Should().NotContain(k => k.Contains("team", StringComparison.OrdinalIgnoreCase),
            "never a team");
    }

    [Fact]
    public void BuildTodoCreatePayload_WithNoCallingContact_LeavesAssignedToBlank()
    {
        var body = ExternalDataService.BuildTodoCreatePayload(
            new CreateExternalTodoRequest { SprkName = "n" },
            ExternalDataService.TryGetRootBinding(ExternalDataService.TodoRootKind.Matter)!,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Display", null, OwnerTeam, assignedToContactId: null);

        body.Should().NotContainKey(ExternalDataService.AssignedToBindKey);
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
            new CreateExternalTodoRequest { SprkName = "n" }, binding, rootId, "Display Name", null, OwnerTeam);

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
            new CreateExternalTodoRequest { SprkName = "n" }, binding, rootId, "The Parent", null, OwnerTeam);

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
            new CreateExternalTodoRequest { SprkName = "n" }, binding, rootId, "M", recordTypeRefId, OwnerTeam);

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
            new CreateExternalTodoRequest { SprkName = "n" }, binding, Guid.NewGuid(), "W", null, OwnerTeam);

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

    /// <summary>
    /// 🔴 A WRONGLY-CASED second parent must still be caught.
    ///
    /// <para>Found in code review. Dataverse requires the PascalCase navigation property, so a
    /// case-sensitive guard looks correct — but it would wave through the second parent whose casing
    /// was wrong, which is exactly the buggy write the guard exists to catch. This is not
    /// hypothetical: <c>spaarke-daily-update-service-r5</c>'s bind audit found a lowercase
    /// <c>sprk_regardingproject@odata.bind</c> in THIS FILE, and lowercase keys still appear in
    /// client code today.</para>
    /// </summary>
    [Fact]
    public void AssertSingleRegardingLookup_WhenTheSecondParentIsWronglyCased_StillThrows()
    {
        var body = new Dictionary<string, object?>
        {
            ["sprk_RegardingMatter@odata.bind"] = "/sprk_matters(44444444-4444-4444-4444-444444444444)",
            ["sprk_regardingproject@odata.bind"] = "/sprk_projects(11111111-1111-1111-1111-111111111111)",
        };

        var act = () => ExternalDataService.AssertSingleRegardingLookup(body);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ADR-024*", "casing must not be a way past the one-parent rule");
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

    // =====================================================================
    // Task 036 / FR-20 (owner round 59 item 2): with ExternalAccess:ImpersonatedRootSets:Enabled on, a systemuser's own
    // term is Read only, and EVERY write route confirms the write against Dataverse, as the caller, on the TARGET record
    // (CallerRecordAccessProbe, substituted). The load-bearing assertions are the write counters and the probe's calls.
    // Each flag-on case has a flag-off twin that asks Dataverse nothing (flag off is byte-equivalent).
    // =====================================================================

    private static readonly Guid Fr20SystemUser = Guid.Parse("0e036000-0000-4000-8000-000000000001");

    /// <summary>The Dataverse entity set of each root, as the probe needs it.</summary>
    private static string EntitySetOf(ExternalDataService.TodoRootKind kind) => kind switch
    {
        ExternalDataService.TodoRootKind.Project => "sprk_projects",
        ExternalDataService.TodoRootKind.Matter => "sprk_matters",
        _ => "sprk_workassignments",
    };

    /// <summary>
    /// A workforce systemuser holding <paramref name="rootId"/> at <paramref name="rights"/>. With
    /// <paramref name="dataverseDecides"/> it is the flag-on principal (its own term is Dataverse's Read; anything more came
    /// from the contact-grants union); without it, the flag-off principal.
    /// </summary>
    private static CallerPrincipal SystemUserHolding(
        ExternalDataService.TodoRootKind kind, Guid rootId, AccessRights rights, bool dataverseDecides) => new()
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            SystemUserId = Fr20SystemUser,
            Email = "staff@customer.test",
            ProjectAccess = kind == ExternalDataService.TodoRootKind.Project
            ? new[] { new CallerProjectAccess { ProjectId = rootId, Rights = rights } }
            : Array.Empty<CallerProjectAccess>(),
            MatterAccess = kind == ExternalDataService.TodoRootKind.Matter
            ? new Dictionary<Guid, AccessRights> { [rootId] = rights }
            : new Dictionary<Guid, AccessRights>(),
            WorkAssignmentAccess = kind == ExternalDataService.TodoRootKind.WorkAssignment
            ? new Dictionary<Guid, AccessRights> { [rootId] = rights }
            : new Dictionary<Guid, AccessRights>(),
            DataverseConfirmsWrites = dataverseDecides,
        };

    [Theory(DisplayName = "Task 036: flag ON, Dataverse grants Read only on the root — to-do create is 403 and nothing is written")]
    [MemberData(nameof(InScopeRoots))]
    public async Task Fr20_CreateTodo_ReadOnlyInDataverse_Is403_AndWritesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(kind, rootId, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, EntitySetOf(kind), rootId, AccessRights.Read);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.CreateCallCount.Should().Be(0, "a Read-only Dataverse answer must never reach the write");
        _fixture.Probe.AppOnlyCalls.Should().ContainSingle().Which.Should().Match<(Guid, string EntitySet, Guid RecordId)>(
            c => c.EntitySet == EntitySetOf(kind) && c.RecordId == rootId, "the TARGET root is asked about, as the caller");
    }

    [Theory(DisplayName = "Task 036: flag ON, Dataverse grants AppendTo on the root — to-do create is allowed")]
    [MemberData(nameof(InScopeRoots))]
    public async Task Fr20_CreateTodo_AppendToInDataverse_IsCreated(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(kind, rootId, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, EntitySetOf(kind), rootId, AccessRights.Read | AccessRights.AppendTo).HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fixture.Data.CreateCallCount.Should().Be(1);
        _fixture.Data.LastCreateArgs.Should().Be((kind, rootId));
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().Equal(new[] { (Fr20SystemUser, TodoCreatePrivilege) });
    }

    // Task 036 fix round 1 (F1): the to-do is written app-only, so AppendTo on the root is not enough — the caller must
    // also hold Create on sprk_todo, exactly as MDA would require of them.
    private const string TodoCreatePrivilege = "prvCreatesprk_Todo";
    private const string EventCreatePrivilege = "prvCreatesprk_Event";

    [Theory(DisplayName = "Task 036 F1: flag ON, AppendTo on the root but no prvCreatesprk_Todo — to-do create is 403 and nothing is written")]
    [MemberData(nameof(InScopeRoots))]
    public async Task Fr20_CreateTodo_AppendToButNoCreatePrivilege_Is403_AndWritesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(kind, rootId, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, EntitySetOf(kind), rootId,
            AccessRights.Read | AccessRights.Write | AccessRights.AppendTo | AccessRights.Delete);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "MDA would refuse a user without prvCreatesprk_Todo");
        _fixture.Data.CreateCallCount.Should().Be(0);
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().Equal(new[] { (Fr20SystemUser, TodoCreatePrivilege) });
    }

    // Task 036 fix round 3 (F3-2): Dataverse's own CreateAccess on an existing record speaks of the caller's create
    // privilege on the record's TABLE, not of attaching to this record (SurfaceRightsFromDataverse's remarks). It must never
    // be read as "may add a child": only AppendTo is.
    [Theory(DisplayName = "Task 036 F3-2: Dataverse's CreateAccess is not AppendTo — the surface's Create comes from AppendTo only")]
    [InlineData(AccessRights.Read | AccessRights.Create, AccessRights.Read)]
    [InlineData(AccessRights.Create, AccessRights.None)]
    [InlineData(AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Append | AccessRights.Share, AccessRights.Read | AccessRights.Write)]
    [InlineData(AccessRights.Read | AccessRights.AppendTo, AccessRights.Read | AccessRights.Create)]
    public void Fr20_SurfaceRights_DataverseCreateAccess_IsNotMayAddAChild(AccessRights dataverse, AccessRights expected)
        => Sprk.Bff.Api.Api.ExternalAccess.ExternalProjectDataEndpoints.SurfaceRightsFromDataverse(dataverse)
            .Should().Be(expected);

    [Theory(DisplayName = "Task 036 F3-2: flag ON, Dataverse grants Read | Create (no AppendTo) and prvCreatesprk_Todo is held — to-do create is 403 and nothing is written")]
    [MemberData(nameof(InScopeRoots))]
    public async Task Fr20_CreateTodo_DataverseCreateAccessWithoutAppendTo_Is403_AndWritesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(kind, rootId, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, EntitySetOf(kind), rootId, AccessRights.Read | AccessRights.Create)
            .HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "CreateAccess on the root is the table's create privilege, not leave to attach a to-do to it");
        _fixture.Data.CreateCallCount.Should().Be(0);
    }

    [Fact(DisplayName = "Task 036 F1 (NFR-01) + fix round 4: flag ON, a privilege read that FAILS refuses the to-do create as a retry-able 503")]
    public async Task Fr20_CreateTodo_PrivilegeReadFault_Is503_AndWritesNothing()
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, AccessRights.Read | AccessRights.AppendTo).HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);
        _fixture.Probe.AppOnlyPrivilegeFault = new HttpRequestException("RetrieveUserSetOfPrivilegesByNames failed");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/todos", ValidCreate());

        await ShouldBeUnavailable(response, DataverseUnavailableReasonCode);
        _fixture.Data.CreateCallCount.Should().Be(0);
    }

    [Theory(DisplayName = "Task 036 twin: flag OFF, the route decides exactly as before and asks Dataverse NOTHING")]
    [InlineData(false, HttpStatusCode.Forbidden, 0)]
    [InlineData(true, HttpStatusCode.Created, 1)]
    public async Task Fr20_CreateTodo_FlagOff_NeverAsksDataverse(bool collaborate, HttpStatusCode expected, int creates)
    {
        _fixture.Reset();
        var rights = collaborate ? ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate) : AccessRights.Read;
        _fixture.Principal = SystemUserHolding(ExternalDataService.TodoRootKind.Project, InScopeProject, rights, dataverseDecides: false);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, AccessRights.Read | AccessRights.AppendTo);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/todos", ValidCreate());

        response.StatusCode.Should().Be(expected);
        _fixture.Data.CreateCallCount.Should().Be(creates);
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty("flag off, no write consults Dataverse — the routes are unchanged");
    }

    [Fact(DisplayName = "Task 036 (NFR-01) + fix round 4: flag ON, a probe that FAILS refuses the write as a retry-able 503 — no fallback to a constant")]
    public async Task Fr20_CreateTodo_ProbeFault_Is503_AndWritesNothing()
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.AppOnlyFault = new HttpRequestException("RetrievePrincipalAccess failed");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/todos", ValidCreate());

        await ShouldBeUnavailable(response, DataverseUnavailableReasonCode);
        _fixture.Data.CreateCallCount.Should().Be(0);
    }

    // ── Task 036 fix round 4 (F1): over the production probe and a scripted Dataverse, a question Dataverse gives no answer
    //    to (a timeout, an UNKNOWN 403, a 5xx) is a retry-able 503 and writes nothing; a real denial stays a 403. ─────────

    private const string DataverseUnavailableReasonCode = "sdap.access.dataverse_unavailable";
    private const string TodoWriteUnavailableReasonCode = "sdap.access.todo_write_unavailable";

    /// <summary>Each way Dataverse can fail to answer: the rights question, then the child-create privilege question after
    /// Dataverse answered AppendTo.</summary>
    public static TheoryData<string, string> UnansweredShapes() => new()
    {
        { WireShape.Timeout, WireShape.Held },
        { WireShape.Unknown, WireShape.Held },
        { WireShape.ServerFault, WireShape.Held },
        { WireShape.AppendTo, WireShape.Timeout },
        { WireShape.AppendTo, WireShape.Unknown },
        { WireShape.AppendTo, WireShape.ServerFault },
    };

    [Theory(DisplayName = "Task 036 fix round 4 (F1): flag ON, a to-do create Dataverse gives no answer for is a 503 and writes nothing")]
    [MemberData(nameof(UnansweredShapes))]
    public async Task Fr20_CreateTodo_DataverseGivesNoAnswer_OverTheWire_Is503_AndWritesNothing(string rights, string privilege)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.UseAppOnlyWire(rights, privilege);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/todos", ValidCreate());

        await ShouldBeUnavailable(response, DataverseUnavailableReasonCode);
        _fixture.Data.CreateCallCount.Should().Be(0);
    }

    [Theory(DisplayName = "Task 036 fix round 4 (F1): flag ON, an event create Dataverse gives no answer for is a 503 and writes nothing")]
    [MemberData(nameof(UnansweredShapes))]
    public async Task Fr20_CreateEvent_DataverseGivesNoAnswer_OverTheWire_Is503_AndWritesNothing(string rights, string privilege)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.UseAppOnlyWire(rights, privilege);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/external/projects/{InScopeProject}/events", new { sprk_name = "Kickoff" });

        await ShouldBeUnavailable(response, DataverseUnavailableReasonCode);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    [Theory(DisplayName = "Task 036 fix round 4 (F1): flag ON, a real denial over the wire stays a 403 on the to-do and event creates")]
    [InlineData("todos", WireShape.Denial, WireShape.Held)]
    [InlineData("todos", WireShape.AppendTo, WireShape.NotHeld)]
    [InlineData("events", WireShape.Denial, WireShape.Held)]
    [InlineData("events", WireShape.AppendTo, WireShape.NotHeld)]
    public async Task Fr20_ChildCreate_ARealDenial_OverTheWire_StaysA403_AndWritesNothing(
        string route, string rights, string privilege)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.UseAppOnlyWire(rights, privilege);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = route == "todos"
            ? await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/todos", ValidCreate())
            : await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/events", new { sprk_name = "Kickoff" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        _fixture.Data.CreateCallCount.Should().Be(0);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    [Theory(DisplayName = "Task 036 fix round 4 (F1): flag ON, a to-do PATCH Dataverse gives no answer for is the PATCH's own 503 and changes nothing")]
    [InlineData(WireShape.Timeout)]
    [InlineData(WireShape.Unknown)]
    [InlineData(WireShape.ServerFault)]
    public async Task Fr20_PatchTodo_DataverseGivesNoAnswer_OverTheWire_Is503_AndChangesNothing(string shape)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, InScopeProject, "Existing to-do");
        _fixture.Probe.UseAppOnlyWire(shape);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        await ShouldBeUnavailable(response, TodoWriteUnavailableReasonCode, "nothing was changed");
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact(DisplayName = "Task 036 fix round 4 (F1): flag ON, a real denial on the to-do PATCH over the wire stays a 403")]
    public async Task Fr20_PatchTodo_ARealDenial_OverTheWire_StaysA403_AndChangesNothing()
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, InScopeProject, "Existing to-do");
        _fixture.Probe.UseAppOnlyWire(WireShape.Denial);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        _fixture.Data.UpdateCallCount.Should().Be(0);
    }

    [Fact(DisplayName = "Task 036 fix round 4 (F1): flag ON, the to-do list still lists with canCreate=false when Dataverse gives no answer")]
    public async Task Fr20_TodoList_DataverseGivesNoAnswer_OverTheWire_ReportsCanCreateFalse()
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.UseAppOnlyWire(WireShape.ServerFault);

        using var client = _fixture.CreateAuthenticatedClient();
        var json = await ListJsonAsync(client, $"/api/v1/external/projects/{InScopeProject}/todos");

        json.GetProperty("canCreate").GetBoolean().Should().BeFalse("canCreate is a UI affordance; an unanswered question offers no create");
    }

    private static async Task ShouldBeUnavailable(HttpResponseMessage response, string reasonCode, string nothing = "nothing was written")
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("reasonCode").GetString().Should().Be(reasonCode);
        body.Should().Contain(nothing).And.Contain("Try again");
    }

    [Fact(DisplayName = "Task 036 (B-17): flag ON, a contact-grant Collaborate on the root still allows the create when Dataverse grants nothing more")]
    public async Task Fr20_CreateTodo_ContactGrantUnion_StillAllows()
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(ExternalDataService.TodoRootKind.Project, InScopeProject,
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate), dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, AccessRights.Read);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "the linked contact's grant is its own term (B-17): Dataverse widens the rights, it never narrows them");
        _fixture.Data.CreateCallCount.Should().Be(1);
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().BeEmpty("the grant already carries Create; the privilege could not change it");
    }

    [Fact(DisplayName = "Task 036: flag ON, a root outside the composed set (or vetoed off it) is 403 WITHOUT a Dataverse call")]
    public async Task Fr20_CreateTodo_RootOutsideTheSet_Is403_AndAsksNothing()
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", OtherProject, AccessRights.Read | AccessRights.AppendTo);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{OtherProject}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.CreateCallCount.Should().Be(0);
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty("the deny list and the Restricted veto act on the composed set — Read comes first");
    }

    [Theory(DisplayName = "Task 036: flag ON, PATCH a to-do — confirmed on the TO-DO itself: Read-only is 403, Write is 204")]
    [InlineData(AccessRights.Read, HttpStatusCode.Forbidden, 0)]
    [InlineData(AccessRights.Read | AccessRights.Write, HttpStatusCode.NoContent, 1)]
    public async Task Fr20_PatchTodo_IsConfirmedOnTheTodo(AccessRights dataverse, HttpStatusCode expected, int updates)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, InScopeProject, "Existing to-do");
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_todos", TodoId, dataverse);
        // A Write on the ROOT must not stand in for Write on the to-do.
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, AccessRights.Read | AccessRights.Write | AccessRights.AppendTo);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(expected);
        _fixture.Data.UpdateCallCount.Should().Be(updates);
        _fixture.Probe.AppOnlyCalls.Select(c => (c.EntitySet, c.RecordId)).Should().Equal(new[] { ("sprk_todos", TodoId) });
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().BeEmpty("an update creates nothing, so no table Create privilege is asked");
    }

    [Fact(DisplayName = "Task 036 twin: flag OFF, a Read-only PATCH is 403 and Dataverse is never asked")]
    public async Task Fr20_PatchTodo_FlagOff_NeverAsksDataverse()
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: false);
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, InScopeProject, "Existing to-do");
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_todos", TodoId, AccessRights.Read | AccessRights.Write);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.UpdateCallCount.Should().Be(0);
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty();
    }

    [Theory(DisplayName = "Task 036 fix round 2 (F2): a confirmation that names no child privilege (PATCH to-do, delete-own) logs no warning and adds no Create")]
    [InlineData(AccessRights.Read | AccessRights.Write | AccessRights.AppendTo)]
    [InlineData(AccessRights.Read | AccessRights.Write | AccessRights.AppendTo | AccessRights.Delete)]
    public async Task Fr20_AConfirmationThatCreatesNothing_LogsNoWarning_AndAddsNoCreate(AccessRights dataverse)
    {
        // As UpdateTodo and delete-own (2b) call it: no child privilege and no privilege reader. Before fix round 2 every
        // such call where Dataverse answered AppendTo logged "[WF-AUTHZ] No child-table Create privilege ... fail closed".
        var log = new CapturingLogger();
        var principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);

        var rights = await Sprk.Bff.Api.Api.ExternalAccess.ExternalProjectDataEndpoints.WithDataverseRightsAsync(
            principal, AccessRights.Read, "sprk_todos", TodoId,
            (_, _, _) => Task.FromResult<AccessRights?>(dataverse), log, CancellationToken.None);

        rights.Rights.Should().Be(dataverse & (AccessRights.Read | AccessRights.Write | AccessRights.Delete));
        rights.Unanswered.Should().Be(AccessRights.None, "Dataverse answered");
        log.Levels.Should().NotContain(Microsoft.Extensions.Logging.LogLevel.Warning,
            "a route that creates nothing is not refused anything — the warning is for a real Create refusal");
        log.Levels.Should().NotContain(Microsoft.Extensions.Logging.LogLevel.Error);
    }

    [Fact(DisplayName = "Task 036 fix round 2 (F2) twin: a child create whose privilege cannot be confirmed still warns")]
    public async Task Fr20_AChildCreateWithNoPrivilegeReader_StillWarns_AndAddsNoCreate()
    {
        var log = new CapturingLogger();
        var principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);

        var rights = await Sprk.Bff.Api.Api.ExternalAccess.ExternalProjectDataEndpoints.WithDataverseRightsAsync(
            principal, AccessRights.Read, "sprk_projects", InScopeProject,
            (_, _, _) => Task.FromResult<AccessRights?>(AccessRights.Read | AccessRights.AppendTo), log, CancellationToken.None,
            childCreatePrivilege: "prvCreatesprk_Todo", readCallerPrivilege: null);

        rights.Rights.Should().Be(AccessRights.Read);
        rights.Unanswered.Should().Be(AccessRights.None, "no privilege reader is a wiring defect, answered as a denial");
        log.Levels.Should().Contain(Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<Microsoft.Extensions.Logging.LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    [Theory(DisplayName = "Task 036: flag ON, create event — needs AppendTo on the project AND (F1) prvCreatesprk_Event")]
    [InlineData(AccessRights.Read | AccessRights.Write, true, HttpStatusCode.Forbidden, 0)]
    [InlineData(AccessRights.Read | AccessRights.AppendTo, true, HttpStatusCode.Created, 1)]
    [InlineData(AccessRights.Read | AccessRights.Write | AccessRights.AppendTo, false, HttpStatusCode.Forbidden, 0)]
    // F3-2: Dataverse's CreateAccess with the event Create privilege held, but no AppendTo — refused.
    [InlineData(AccessRights.Read | AccessRights.Create, true, HttpStatusCode.Forbidden, 0)]
    public async Task Fr20_CreateEvent_IsConfirmedOnTheProject(
        AccessRights dataverse, bool holdsCreate, HttpStatusCode expected, int creates)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, dataverse);
        if (holdsCreate)
        {
            _fixture.Probe.HoldsAppOnly(Fr20SystemUser, EventCreatePrivilege);
        }

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/external/projects/{InScopeProject}/events", new { sprk_name = "Kickoff" });

        response.StatusCode.Should().Be(expected);
        _fixture.Data.CreateEventCallCount.Should().Be(creates);
    }

    [Fact(DisplayName = "Task 036 twin: flag OFF, a Read-only event create is 403 and Dataverse is never asked")]
    public async Task Fr20_CreateEvent_FlagOff_NeverAsksDataverse()
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: false);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, AccessRights.Read | AccessRights.AppendTo);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/external/projects/{InScopeProject}/events", new { sprk_name = "Kickoff" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty();
    }

    // =====================================================================
    // Task 036 fix round 1 (F4, open question g): a Dataverse-decided caller's /me level says only what they may see, so
    // the record's own lists say whether its create route would admit them (`canCreate`) — one probe for the record in
    // hand. Every other caller's list is byte-for-byte unchanged (no `canCreate` member).
    // =====================================================================

    private static async Task<System.Text.Json.JsonElement> ListJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public static TheoryData<string, ExternalDataService.TodoRootKind, Guid, AccessRights, bool, bool> Fr20TodoListCases()
    {
        var data = new TheoryData<string, ExternalDataService.TodoRootKind, Guid, AccessRights, bool, bool>();
        foreach (var row in InScopeRoots())
        {
            var (segment, kind, rootId) = ((string)row[0], (ExternalDataService.TodoRootKind)row[1], (Guid)row[2]);
            data.Add(segment, kind, rootId, AccessRights.Read | AccessRights.Write | AccessRights.AppendTo, true, true);
            data.Add(segment, kind, rootId, AccessRights.Read | AccessRights.Write | AccessRights.AppendTo, false, false);
            data.Add(segment, kind, rootId, AccessRights.Read, true, false);
        }

        return data;
    }

    [Theory(DisplayName = "Task 036 F4: flag ON, the to-do list's canCreate is the create route's own Dataverse answer")]
    [MemberData(nameof(Fr20TodoListCases))]
    public async Task Fr20_TodoList_CanCreate_IsTheCreateRoutesDataverseAnswer(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId, AccessRights dataverse, bool holdsCreate, bool expected)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(kind, rootId, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, EntitySetOf(kind), rootId, dataverse);
        if (holdsCreate)
        {
            _fixture.Probe.HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);
        }

        using var client = _fixture.CreateAuthenticatedClient();
        var json = await ListJsonAsync(client, $"/api/v1/external/{segment}/{rootId}/todos");

        json.GetProperty("canCreate").GetBoolean().Should().Be(expected);
        _fixture.Probe.AppOnlyCalls.Should().ContainSingle("one probe, for the record in hand");
    }

    [Fact(DisplayName = "Task 036 F4 (NFR-01): flag ON, a probe that FAILS reports canCreate=false and still lists")]
    public async Task Fr20_TodoList_AProbeThatFaults_ReportsCanCreateFalse()
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);
        _fixture.Probe.AppOnlyFault = new HttpRequestException("RetrievePrincipalAccess failed");

        using var client = _fixture.CreateAuthenticatedClient();
        var json = await ListJsonAsync(client, $"/api/v1/external/projects/{InScopeProject}/todos");

        json.GetProperty("canCreate").GetBoolean().Should().BeFalse();
    }

    [Theory(DisplayName = "Task 036 F4 twin: flag OFF (any level), the to-do list carries no canCreate and asks Dataverse nothing")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fr20_TodoList_FlagOff_IsUnchanged_AndAsksNothing(bool collaborate)
    {
        _fixture.Reset();
        var rights = collaborate ? ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate) : AccessRights.Read;
        _fixture.Principal = SystemUserHolding(ExternalDataService.TodoRootKind.Project, InScopeProject, rights, dataverseDecides: false);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, AccessRights.Read | AccessRights.AppendTo).HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);

        using var client = _fixture.CreateAuthenticatedClient();
        var json = await ListJsonAsync(client, $"/api/v1/external/projects/{InScopeProject}/todos");

        json.TryGetProperty("canCreate", out _).Should().BeFalse("flag off, the wire is byte-for-byte what it was");
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty();
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().BeEmpty();
    }

    [Theory(DisplayName = "Task 036 F4: flag ON, the PROJECT event list's canCreate is the event create route's Dataverse answer")]
    [InlineData(AccessRights.Read | AccessRights.AppendTo, true, true)]
    [InlineData(AccessRights.Read | AccessRights.AppendTo, false, false)]
    [InlineData(AccessRights.Read | AccessRights.Write, true, false)]
    public async Task Fr20_ProjectEventList_CanCreate_IsTheCreateRoutesDataverseAnswer(
        AccessRights dataverse, bool holdsCreate, bool expected)
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, dataverse);
        if (holdsCreate)
        {
            _fixture.Probe.HoldsAppOnly(Fr20SystemUser, EventCreatePrivilege);
        }

        using var client = _fixture.CreateAuthenticatedClient();
        var json = await ListJsonAsync(client, $"/api/v1/external/projects/{InScopeProject}/events");

        json.GetProperty("canCreate").GetBoolean().Should().Be(expected);
    }

    [Theory(DisplayName = "Task 036 F4 twin: no event create route on a matter, and flag OFF on a project — no canCreate, no probe")]
    [InlineData("matters", true)]
    [InlineData("projects", false)]
    public async Task Fr20_EventList_WithoutACreateRoute_OrFlagOff_IsUnchanged(string segment, bool dataverseDecides)
    {
        _fixture.Reset();
        var (kind, rootId) = segment == "matters"
            ? (ExternalDataService.TodoRootKind.Matter, InScopeMatter)
            : (ExternalDataService.TodoRootKind.Project, InScopeProject);
        _fixture.Principal = SystemUserHolding(kind, rootId, AccessRights.Read, dataverseDecides);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, EntitySetOf(kind), rootId, AccessRights.Read | AccessRights.AppendTo).HoldsAppOnly(Fr20SystemUser, EventCreatePrivilege);

        using var client = _fixture.CreateAuthenticatedClient();
        var json = await ListJsonAsync(client, $"/api/v1/external/{segment}/{rootId}/events");

        json.TryGetProperty("canCreate", out _).Should().BeFalse();
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty();
    }

    // =====================================================================
    // Task 036 owner decision (a), 2026-10-18: a WorkforceHome-linked systemuser (ADR-028 A9) signs in with a
    // customer-directory token, which cannot be exchanged OBO for Dataverse in Spaarke's tenant. Its confirmations ask the
    // SAME questions app-only, naming its systemuser (CallerRecordAccessProbe.GetPrincipalRightsAppOnlyAsync /
    // PrincipalHoldsPrivilegeAppOnlyAsync). The double answers the app-only questions separately from the OBO ones, so an
    // allow below can only come from the app-only path; before the change the OBO path found nothing and refused.
    // Task 036 option C (owner 2026-10-20) widens this to EVERY systemuser (the Fr20c rows; the Fr20 rows above are asked
    // app-only too): the OBO questions are never asked on /api/v1/external.
    // =====================================================================

    /// <summary>
    /// <see cref="SystemUserHolding"/>, as a WorkforceHome-linked systemuser reaches the plane. Since option C the resolved
    /// <see cref="CallerPrincipal"/> carries no sign-in marker (fix round 3 removed the dead <c>ResolvedThroughHomeLink</c>):
    /// a home-linked systemuser is confirmed exactly as any other, which is what these rows pin.
    /// </summary>
    private static CallerPrincipal HomeLinkedSystemUserHolding(
        ExternalDataService.TodoRootKind kind, Guid rootId, AccessRights rights, Guid? systemUserId = null)
    {
        var principal = SystemUserHolding(kind, rootId, rights, dataverseDecides: true);
        return new CallerPrincipal
        {
            Plane = principal.Plane,
            ContactId = principal.ContactId,
            SystemUserId = systemUserId ?? principal.SystemUserId,
            Email = principal.Email,
            ProjectAccess = principal.ProjectAccess,
            MatterAccess = principal.MatterAccess,
            WorkAssignmentAccess = principal.WorkAssignmentAccess,
            DataverseConfirmsWrites = true,
        };
    }

    [Theory(DisplayName = "Task 036 (a): a WorkforceHome-linked systemuser holding Write on the to-do edits it — asked app-only, naming its systemuser; Read only is refused")]
    [InlineData(AccessRights.Read | AccessRights.Write, HttpStatusCode.NoContent, 1)]
    [InlineData(AccessRights.Read, HttpStatusCode.Forbidden, 0)]
    public async Task Fr20a_HomeLinkedSystemUser_PatchTodo_IsConfirmedAppOnlyOnTheTodo(
        AccessRights dataverse, HttpStatusCode expected, int updates)
    {
        _fixture.Reset();
        _fixture.Principal = HomeLinkedSystemUserHolding(ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read);
        _fixture.Data.TodoLookupResult = (ExternalDataService.TodoRootKind.Project, InScopeProject, "Existing to-do");
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_todos", TodoId, dataverse);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", ValidPatch());

        response.StatusCode.Should().Be(expected);
        _fixture.Data.UpdateCallCount.Should().Be(updates);
        _fixture.Probe.AppOnlyCalls.Should().Equal(new[] { (Fr20SystemUser, "sprk_todos", TodoId) },
            "the to-do is asked about, app-only, naming the RESOLVED systemuser");
        _fixture.Probe.Calls.Should().BeEmpty("a customer-directory token is never tried OBO first — no fallback either way");
    }

    [Theory(DisplayName = "Task 036 (a): a WorkforceHome-linked systemuser with AppendTo plus prvCreatesprk_Todo creates a child to-do; either missing is refused")]
    [MemberData(nameof(InScopeRoots))]
    public async Task Fr20a_HomeLinkedSystemUser_CreateTodo_NeedsAppendToAndTheCreatePrivilege_AskedAppOnly(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        // Both held → created.
        _fixture.Reset();
        _fixture.Principal = HomeLinkedSystemUserHolding(kind, rootId, AccessRights.Read);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, EntitySetOf(kind), rootId, AccessRights.Read | AccessRights.AppendTo)
            .HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);

        using (var client = _fixture.CreateAuthenticatedClient())
        {
            var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/todos", ValidCreate());
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        _fixture.Data.CreateCallCount.Should().Be(1);
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().Equal(new[] { (Fr20SystemUser, TodoCreatePrivilege) },
            "the exact, case-sensitive privilege name the OBO path asks");
        _fixture.Probe.Calls.Should().BeEmpty();
        _fixture.Probe.PrivilegeCalls.Should().BeEmpty();

        // AppendTo without the privilege → refused, nothing written.
        _fixture.Reset();
        _fixture.Principal = HomeLinkedSystemUserHolding(kind, rootId, AccessRights.Read);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, EntitySetOf(kind), rootId,
            AccessRights.Read | AccessRights.Write | AccessRights.AppendTo);

        using (var client = _fixture.CreateAuthenticatedClient())
        {
            var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/todos", ValidCreate());
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _fixture.Data.CreateCallCount.Should().Be(0);

        // The privilege without AppendTo → refused, and the privilege is not even asked.
        _fixture.Reset();
        _fixture.Principal = HomeLinkedSystemUserHolding(kind, rootId, AccessRights.Read);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, EntitySetOf(kind), rootId, AccessRights.Read | AccessRights.Write)
            .HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);

        using (var client = _fixture.CreateAuthenticatedClient())
        {
            var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/todos", ValidCreate());
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _fixture.Data.CreateCallCount.Should().Be(0);
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().BeEmpty();
    }

    [Theory(DisplayName = "Task 036 (a) (NFR-01) + fix round 4: a fault on the app-only rights or privilege call refuses as a retry-able 503 — no fallback to OBO or a constant")]
    [InlineData("rights")]
    [InlineData("privilege")]
    public async Task Fr20a_HomeLinkedSystemUser_AnAppOnlyFault_Refuses(string faulting)
    {
        _fixture.Reset();
        _fixture.Principal = HomeLinkedSystemUserHolding(ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, AccessRights.Read | AccessRights.AppendTo)
            .HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);
        // The OBO path would allow, were it (wrongly) asked after the fault.
        _fixture.Probe.Answer("sprk_projects", InScopeProject, AccessRights.Read | AccessRights.AppendTo).Holds(TodoCreatePrivilege);
        if (faulting == "rights")
        {
            _fixture.Probe.AppOnlyFault = new HttpRequestException("RetrievePrincipalAccess failed");
        }
        else
        {
            _fixture.Probe.AppOnlyPrivilegeFault = new HttpRequestException("RetrieveUserSetOfPrivilegesByNames failed");
        }

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/todos", ValidCreate());

        await ShouldBeUnavailable(response, DataverseUnavailableReasonCode);
        _fixture.Data.CreateCallCount.Should().Be(0);
        _fixture.Probe.Calls.Should().BeEmpty("a fault on the app-only path is never retried OBO");
        _fixture.Probe.PrivilegeCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 036 (a): a WorkforceHome-linked principal with no systemuser id is refused, and asks nothing OBO")]
    public async Task Fr20a_HomeLinkedPrincipal_WithoutASystemUserId_FailsClosed()
    {
        _fixture.Reset();
        _fixture.Principal = HomeLinkedSystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, systemUserId: Guid.Empty);
        _fixture.Probe.Answer("sprk_projects", InScopeProject, AccessRights.Read | AccessRights.AppendTo).Holds(TodoCreatePrivilege);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.CreateCallCount.Should().Be(0);
        _fixture.Probe.AppOnlyCalls.Should().OnlyContain(c => c.SystemUserId == Guid.Empty,
            "the empty id reaches the app-only probe, which refuses it — it is never swapped for the caller's token");
        _fixture.Probe.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 036 option C: a Spaarke-tenant systemuser is confirmed app-only, naming its systemuser — the OBO probe is never called on /api/v1/external")]
    public async Task Fr20c_ANormalSystemUser_UsesTheAppOnlyPath_AndNeverObo()
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        // The OBO path would also allow, were it (wrongly) asked: only the app-only path may answer.
        _fixture.Probe.Answer("sprk_projects", InScopeProject, AccessRights.Read | AccessRights.AppendTo).Holds(TodoCreatePrivilege);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, AccessRights.Read | AccessRights.AppendTo)
            .HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fixture.Probe.AppOnlyCalls.Should().Equal(new[] { (Fr20SystemUser, "sprk_projects", InScopeProject) },
            "every systemuser is asked app-only, naming the RESOLVED systemuser (ADR-028: no OBO on this plane)");
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().Equal(new[] { (Fr20SystemUser, TodoCreatePrivilege) });
        _fixture.Probe.Calls.Should().BeEmpty("the caller's token is never exchanged on /api/v1/external");
        _fixture.Probe.PrivilegeCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 036 option C: the confirmation names every systemuser's resolved id; no systemuser id is Guid.Empty (fail closed)")]
    public void Fr20c_AppOnlyConfirmationPrincipal_IsEverySystemUser_AndEmptyFailsClosed()
    {
        var spaarkeTenant = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        var homeLinked = HomeLinkedSystemUserHolding(ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read);
        var homeLinkedWithoutId = HomeLinkedSystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, systemUserId: Guid.Empty);
        var contact = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Guid.NewGuid(),
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };

        Sprk.Bff.Api.Api.ExternalAccess.ExternalProjectDataEndpoints.AppOnlyConfirmationPrincipal(spaarkeTenant).Should().Be(Fr20SystemUser,
            "a Spaarke-tenant systemuser is confirmed app-only too (option C), not OBO");
        Sprk.Bff.Api.Api.ExternalAccess.ExternalProjectDataEndpoints.AppOnlyConfirmationPrincipal(homeLinked).Should().Be(Fr20SystemUser);
        Sprk.Bff.Api.Api.ExternalAccess.ExternalProjectDataEndpoints.AppOnlyConfirmationPrincipal(homeLinkedWithoutId).Should().Be(Guid.Empty);
        Sprk.Bff.Api.Api.ExternalAccess.ExternalProjectDataEndpoints.AppOnlyConfirmationPrincipal(contact).Should().Be(Guid.Empty);
        Sprk.Bff.Api.Api.ExternalAccess.ExternalProjectDataEndpoints.AppOnlyConfirmationPrincipal(null).Should().Be(Guid.Empty);
    }

    [Fact(DisplayName = "Task 036 option C: an app-only refusal for a Spaarke-tenant systemuser is not overturned by OBO")]
    public async Task Fr20c_ANormalSystemUser_AppOnlyRefusal_IsFinal()
    {
        _fixture.Reset();
        _fixture.Principal = SystemUserHolding(
            ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read, dataverseDecides: true);
        _fixture.Probe.Answer("sprk_projects", InScopeProject, AccessRights.Read | AccessRights.AppendTo).Holds(TodoCreatePrivilege);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, AccessRights.Read);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.CreateCallCount.Should().Be(0);
        _fixture.Probe.Calls.Should().BeEmpty();
        _fixture.Probe.PrivilegeCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 036 (a) twin: a contact caller is unaffected — its grants decide and neither path is asked")]
    public async Task Fr20a_AContactCaller_IsUnaffected()
    {
        _fixture.Reset();
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            Email = "contact@customer.test",
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(InScopeProject, ExternalAccessLevel.Collaborate) },
        };
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, AccessRights.Read)
            .HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/projects/{InScopeProject}/todos", ValidCreate());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fixture.Probe.Calls.Should().BeEmpty();
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty();
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().BeEmpty();
    }

    [Theory(DisplayName = "Task 036 (a): a WorkforceHome-linked systemuser's to-do list canCreate is the create route's app-only answer")]
    [InlineData(AccessRights.Read | AccessRights.Write | AccessRights.AppendTo, true, true)]
    [InlineData(AccessRights.Read | AccessRights.Write | AccessRights.AppendTo, false, false)]
    [InlineData(AccessRights.Read, true, false)]
    public async Task Fr20a_HomeLinkedSystemUser_TodoList_CanCreate_IsTheAppOnlyAnswer(
        AccessRights dataverse, bool holdsCreate, bool expected)
    {
        _fixture.Reset();
        _fixture.Principal = HomeLinkedSystemUserHolding(ExternalDataService.TodoRootKind.Project, InScopeProject, AccessRights.Read);
        _fixture.Probe.AnswerAppOnly(Fr20SystemUser, "sprk_projects", InScopeProject, dataverse);
        if (holdsCreate)
        {
            _fixture.Probe.HoldsAppOnly(Fr20SystemUser, TodoCreatePrivilege);
        }

        using var client = _fixture.CreateAuthenticatedClient();
        var json = await ListJsonAsync(client, $"/api/v1/external/projects/{InScopeProject}/todos");

        json.GetProperty("canCreate").GetBoolean().Should().Be(expected,
            "the SPA offers exactly what the create route will allow");
        _fixture.Probe.AppOnlyCalls.Should().ContainSingle("one app-only probe, for the record in hand");
        _fixture.Probe.Calls.Should().BeEmpty();
    }
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

    /// <summary>
    /// Task 146: the create routes resolve the to-do's owner from its root before writing. The scope tests here
    /// assert the AUTHORIZATION gate, so the resolver answers every root with one team at its module boundary. The
    /// ownership rules are pinned by RecordOwnershipResolverTests; the external routes' owners (to-do on each root type,
    /// event, document upload — secure, ordinary and refused) by the data-mutation SecureChildOwnershipExternalTests,
    /// which drives these same routes over the REAL resolver (task 146 r1, verifier item 7).
    /// </summary>
    public Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble Ownership { get; } = new();

    private readonly StubCallerPrincipalResolver _resolver = new();

    /// <summary>Task 036: the caller's own Dataverse rights, as the write routes ask a Dataverse-decided caller for them.</summary>
    public Sprk.Bff.Api.Tests.TestInfrastructure.CallerRecordAccessProbeDouble Probe { get; } = new();

    public CallerPrincipal? Principal
    {
        get => _resolver.Principal;
        set => _resolver.Principal = value;
    }

    /// <summary>Clears per-test state. The fixture is class-scoped, so tests must not leak into each other.</summary>
    public void Reset()
    {
        Data.Reset();
        Probe.Reset();
        Principal = null;
        Ownership.TeamId = Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble.DefaultTeamId;
        Ownership.Fault = null;
        Ownership.Requests.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>();
            services.AddSingleton<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>(Ownership);
            services.AddScoped<ICallerPrincipalResolver>(_ => _resolver);
            // Overrides the AddHttpClient<ExternalDataService> typed-client registration
            // (ExternalAccessModule.cs:59) — last registration wins for GetRequiredService.
            services.AddScoped(_ => (ExternalDataService)Data);
            // Task 036: the same for the typed-client CallerRecordAccessProbe.
            services.AddScoped<CallerRecordAccessProbe>(_ => Probe);
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

        /// <summary>Task 187: what the live-parent check answers (a to-do whose live parents are ordinary, unless a test says so).</summary>
        public ExternalDataService.LiveParentDecision LiveParents { get; set; } = ExternalDataService.LiveParentDecision.Readable;

        public int LiveParentChecks { get; private set; }

        public override Task<ExternalDataService.LiveParentDecision> CheckLiveParentsAsync(
            ExternalDataService.DeletableKind kind, Guid id, CallerPrincipal caller, CancellationToken ct = default)
        {
            LiveParentChecks++;
            return Task.FromResult(LiveParents);
        }

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

        /// <summary>Task 036: event creates reaching Dataverse (the load-bearing assertion on the event write gate).</summary>
        public int CreateEventCallCount { get; private set; }

        /// <summary>Task 146: the owning team the route handed to the create.</summary>
        public Guid? LastCreateOwningTeamId { get; private set; }

        public void Reset()
        {
            TodoLookupResult = (ExternalDataService.TodoRootKind.None, null, null);
            ThrowOnLookup = false;
            LiveParents = ExternalDataService.LiveParentDecision.Readable;
            LiveParentChecks = 0;
            UpdateCallCount = 0;
            LastUpdatedTodoId = null;
            LastRequest = null;
            ListCallCount = 0;
            LastListArgs = null;
            CreateCallCount = 0;
            LastCreateArgs = null;
            LastCreateRequest = null;
            LastCreateOwningTeamId = null;
            LastCreateCallerContactId = null;
            LastCreateCreator = null;
            CreateEventCallCount = 0;
        }

        public override Task<ExternalEventDto> CreateEventAsync(
            Guid projectId, CreateExternalEventRequest request, Guid owningTeamId, CallerPrincipal creator,
            CancellationToken ct = default)
        {
            CreateEventCallCount++;
            return Task.FromResult(new ExternalEventDto { SprkEventid = Guid.NewGuid().ToString(), SprkName = request.SprkName });
        }

        /// <summary>Task 030 review: the creator principal the route handed the create (FR-25 stamp source).</summary>
        public CallerPrincipal? LastCreateCreator { get; private set; }

        /// <summary>Task 152: the calling contact the endpoint handed the create (becomes Assigned To).</summary>
        public Guid? LastCreateCallerContactId { get; private set; }

        public override Task<ExternalCollectionResponse<ExternalTodoDto>> GetTodosAsync(
            ExternalDataService.TodoRootKind rootKind, Guid rootId, CallerPrincipal caller, CancellationToken ct = default)
        {
            ListCallCount++;
            LastListArgs = (rootKind, rootId);
            return Task.FromResult(new ExternalCollectionResponse<ExternalTodoDto>());
        }

        /// <summary>Task 036 fix round 1 (F4): the event list, offline (its canCreate is what the tests read).</summary>
        public override Task<ExternalCollectionResponse<ExternalEventDto>> GetEventsAsync(
            ExternalDataService.TodoRootKind rootKind, Guid rootId, CallerPrincipal caller, CancellationToken ct = default) =>
            Task.FromResult(new ExternalCollectionResponse<ExternalEventDto>());

        public override Task<ExternalTodoDto> CreateTodoAsync(
            ExternalDataService.TodoRootKind rootKind, Guid rootId,
            CreateExternalTodoRequest request, Guid owningTeamId, Guid? callerContactId, CallerPrincipal creator,
            CancellationToken ct = default)
        {
            CreateCallCount++;
            LastCreateArgs = (rootKind, rootId);
            LastCreateRequest = request;
            LastCreateOwningTeamId = owningTeamId;
            LastCreateCallerContactId = callerContactId;
            LastCreateCreator = creator;
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
