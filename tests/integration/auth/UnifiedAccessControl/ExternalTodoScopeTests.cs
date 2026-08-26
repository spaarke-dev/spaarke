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
                .Select(a => new CallerProjectAccess { ProjectId = a.ProjectId, AccessLevel = a.Level })
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
                .Select(a => new CallerProjectAccess { ProjectId = a.ProjectId, AccessLevel = a.Level })
                .ToList(),
            AccessibleMatterIds = (matters ?? Array.Empty<Guid>()).ToHashSet(),
            AccessibleWorkAssignmentIds = (workAssignments ?? Array.Empty<Guid>()).ToHashSet()
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

        // Assert WHICH guard denied, not merely that something did. The record-scope check and the
        // rights check are deliberately redundant for this case (GetEffectiveRights returns None for
        // a project outside ProjectAccess, so the rights gate would also deny). Without this
        // assertion, deleting the scope check entirely leaves all tests green — verified by
        // perturbation, see notes/task-009-external-todo-scope.md §Perturbation.
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("You do not have access to this to-do",
                "the RECORD-SCOPE guard must be the one that denies an out-of-scope to-do — if this "
                + "reports the access-level message instead, HasProjectAccess has been bypassed and "
                + "only the rights check is left standing");
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

        public void Reset()
        {
            TodoLookupResult = (ExternalDataService.TodoRootKind.None, null, null);
            ThrowOnLookup = false;
            UpdateCallCount = 0;
            LastUpdatedTodoId = null;
            LastRequest = null;
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
