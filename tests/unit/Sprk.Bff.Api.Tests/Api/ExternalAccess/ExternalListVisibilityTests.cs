using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 191h (#2179 design v2 §3.3 "Callers", §3.10): the external SPA's to-do and event lists ask THE
/// external visibility predicate (<see cref="AppOnlyExternalVisibility"/>) for every row of the page, for every caller who is not
/// an internal systemuser, and keep only a Visible row.
/// </summary>
/// <remarks>
/// The page comes from the REAL <see cref="ExternalDataService"/> over <see cref="ExternalRootEventReadTests.FakeDataverse"/>
/// (plain rows under the held matter, so every rule the list already applied passes them); the predicate runs REAL over its own
/// in-memory Dataverse (<see cref="ExternalVisibilityLeakWorld"/>), in which the same row ids are filed to an internal-only email,
/// to a Restricted matter, or only to the open matter. Each case has a must-fire and a must-not-fire control.
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalListVisibilityTests
{
    private static readonly Guid M = Guid.Parse("19100000-0000-4000-8000-0000000000a1");
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999191");
    private static readonly Guid SystemUser = Guid.Parse("19100000-0000-4000-8000-0000000000c1");

    private readonly ExternalRootEventReadTests.FakeDataverse _data = new();
    private readonly ExternalVisibilityLeakWorld _leak = new();

    public static TheoryData<string> Tables => new() { "sprk_todo", "sprk_event" };

    private static CallerPrincipal Contact() => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [M] = AccessRights.Read },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    private static CallerPrincipal SystemUserCaller() => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = CallerContact,
        SystemUserId = SystemUser,
        Email = "flagged.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [M] = AccessRights.Read },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    private static ISystemUserIdentityResolver Identity(bool external)
    {
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity.Setup(i => i.IsExternalAsync(SystemUser, It.IsAny<CancellationToken>())).ReturnsAsync(external);
        return identity.Object;
    }

    /// <summary>Seeds one plain list row per kind and files the same id in the predicate's world; returns the ids by kind.</summary>
    private (string AboutTheEmail, string UnderRestricted, string Plain) Seed(string table)
    {
        string Row(string name) => table == "sprk_todo" ? _data.AddTodo("matters", M, name) : _data.Add("matters", M, name);

        var aboutTheEmail = Row("About an internal-only email");
        var underRestricted = Row("Under a Restricted matter");
        var plain = Row("Plain");
        _leak.ChildAboutTheEmail(table, Guid.Parse(aboutTheEmail));
        _leak.ChildUnderTheRestrictedMatter(table, Guid.Parse(underRestricted));
        _leak.PlainChild(table, Guid.Parse(plain));
        return (aboutTheEmail, underRestricted, plain);
    }

    private static async Task<(List<string> Ids, bool Truncated)> ListAsync(ExternalDataService service, string table, CallerPrincipal caller)
    {
        if (table == "sprk_todo")
        {
            var todos = await service.GetTodosAsync(ExternalDataService.TodoRootKind.Matter, M, caller);
            return (todos.Value.Select(t => t.SprkTodoid!).ToList(), todos.Truncated);
        }

        var events = await service.GetEventsAsync(ExternalDataService.TodoRootKind.Matter, M, caller);
        return (events.Value.Select(e => e.SprkEventid!).ToList(), events.Truncated);
    }

    [Theory(DisplayName = "191h SPA list must fire: a row about an internal-only email, or under a Restricted matter, is hidden from an external contact (not truncated); the control without the predicate lists it")]
    [MemberData(nameof(Tables))]
    public async Task AnExternalContact_DoesNotSeeARowThePredicateHides(string table)
    {
        var (aboutTheEmail, underRestricted, plain) = Seed(table);

        var control = await ListAsync(_data.NewService(visibility: ExternalVisibilitySeams.AllVisible), table, Contact());
        control.Ids.Should().BeEquivalentTo(new[] { aboutTheEmail, underRestricted, plain }, "control: every rule before 191h lists all three");

        var asked = new List<IReadOnlyCollection<(string Table, Guid Id)>>();
        var (ids, truncated) = await ListAsync(
            _data.NewService(visibility: ExternalVisibilitySeams.Recording(asked, _leak.Seam)), table, Contact());

        ids.Should().Equal(plain);
        truncated.Should().BeFalse("a row a rule hides is an answer, not a fault");
        asked.Should().ContainSingle("ONE predicate call for the page").Which.Should().HaveCount(3)
            .And.OnlyContain(r => r.Table == table);
    }

    [Theory(DisplayName = "191h SPA list must fire: a systemuser flagged external is narrowed like a contact")]
    [MemberData(nameof(Tables))]
    public async Task AFlaggedSystemUser_IsNarrowed(string table)
    {
        var (_, _, plain) = Seed(table);

        var (ids, _) = await ListAsync(_data.NewService(identity: Identity(external: true), visibility: _leak.Seam), table, SystemUserCaller());

        ids.Should().Equal(plain);
    }

    [Theory(DisplayName = "191h SPA list must not fire: an internal systemuser's list is unchanged and the predicate is never asked")]
    [MemberData(nameof(Tables))]
    public async Task AnInternalSystemUser_IsUnchanged_AndThePredicateIsNotAsked(string table)
    {
        var (aboutTheEmail, underRestricted, plain) = Seed(table);

        var (ids, truncated) = await ListAsync(
            _data.NewService(identity: Identity(external: false), visibility: ExternalVisibilitySeams.Throws), table, SystemUserCaller());

        ids.Should().BeEquivalentTo(new[] { aboutTheEmail, underRestricted, plain });
        truncated.Should().BeFalse();
    }

    [Theory(DisplayName = "191h SPA list must fire: a row the predicate cannot decide (it does not come back) is hidden AND the list says truncated (ruling D-70)")]
    [MemberData(nameof(Tables))]
    public async Task AnUndecidedRow_IsHidden_AndTheListIsTruncated(string table)
    {
        var (_, _, plain) = Seed(table);
        var undecided = table == "sprk_todo" ? _data.AddTodo("matters", M, "Not in the predicate's world") : _data.Add("matters", M, "Not in the predicate's world");

        var (ids, truncated) = await ListAsync(_data.NewService(visibility: _leak.Seam), table, Contact());

        ids.Should().Equal(plain).And.NotContain(undecided);
        truncated.Should().BeTrue("an Undecided row is a fault on a list");
    }

    [Theory(DisplayName = "191h SPA list must fire: a predicate that faults, throws, or is not configured hides every row and says truncated (fail closed)")]
    [InlineData("sprk_todo", "faulted")]
    [InlineData("sprk_todo", "throws")]
    [InlineData("sprk_todo", "none")]
    [InlineData("sprk_event", "faulted")]
    [InlineData("sprk_event", "throws")]
    [InlineData("sprk_event", "none")]
    public async Task APredicateFault_HidesEveryRow_AndTheListIsTruncated(string table, string fault)
    {
        Seed(table);
        var service = fault switch
        {
            "faulted" => _data.NewService(visibility: ExternalVisibilitySeams.Faulted),
            "throws" => _data.NewService(visibility: ExternalVisibilitySeams.Throws),
            _ => NoSeam(),
        };

        var (ids, truncated) = await ListAsync(service, table, Contact());

        ids.Should().BeEmpty();
        truncated.Should().BeTrue();
    }

    /// <summary>The service as a composition without the seam builds it (the optional parameter left out).</summary>
    private ExternalDataService NoSeam() => new(
        new HttpClient(_data),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build(),
        new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
        NullLogger<ExternalDataService>.Instance,
        _data.FilingReader);

    private static ExternalDataService.DeletableKind KindOf(string table) =>
        table == "sprk_todo" ? ExternalDataService.DeletableKind.Todo : ExternalDataService.DeletableKind.Event;

    [Theory(DisplayName = "191h fix round 1 (F1) write gate must fire: a to-do or event the lists hide is Withheld for an external contact (the route's same 403); the plain row is Readable")]
    [MemberData(nameof(Tables))]
    public async Task TheWriteGate_WithholdsARowTheListsHide(string table)
    {
        var (aboutTheEmail, underRestricted, plain) = Seed(table);
        var undecided = Guid.NewGuid(); // not in the predicate's world: Undecided, the same answer as Hidden
        var service = _data.NewService(visibility: _leak.Seam);

        (await service.CheckExternalVisibilityAsync(KindOf(table), Guid.Parse(aboutTheEmail), Contact()))
            .Should().Be(ExternalDataService.LiveParentDecision.Withheld);
        (await service.CheckExternalVisibilityAsync(KindOf(table), Guid.Parse(underRestricted), Contact()))
            .Should().Be(ExternalDataService.LiveParentDecision.Withheld);
        (await service.CheckExternalVisibilityAsync(KindOf(table), undecided, Contact()))
            .Should().Be(ExternalDataService.LiveParentDecision.Withheld, "Undecided and Hidden are one answer (NFR-01)");
        (await service.CheckExternalVisibilityAsync(KindOf(table), Guid.Parse(plain), Contact()))
            .Should().Be(ExternalDataService.LiveParentDecision.Readable, "control: a row the predicate shows may be written");
        (await _data.NewService(identity: Identity(external: true), visibility: _leak.Seam)
                .CheckExternalVisibilityAsync(KindOf(table), Guid.Parse(aboutTheEmail), SystemUserCaller()))
            .Should().Be(ExternalDataService.LiveParentDecision.Withheld, "a systemuser flagged external is gated like a contact");
    }

    [Theory(DisplayName = "191h fix round 1 (F1) write gate must not fire: an internal systemuser is Readable and the predicate is never asked")]
    [MemberData(nameof(Tables))]
    public async Task TheWriteGate_LeavesAnInternalSystemUserAlone(string table)
    {
        var (aboutTheEmail, _, _) = Seed(table);

        (await _data.NewService(identity: Identity(external: false), visibility: ExternalVisibilitySeams.Throws)
                .CheckExternalVisibilityAsync(KindOf(table), Guid.Parse(aboutTheEmail), SystemUserCaller()))
            .Should().Be(ExternalDataService.LiveParentDecision.Readable);
    }

    [Theory(DisplayName = "191h fix round 1 (F1) write gate must fire: a predicate that faults, throws, or is not configured is Unavailable (the route's 503, nothing written)")]
    [InlineData("sprk_todo", "faulted")]
    [InlineData("sprk_todo", "throws")]
    [InlineData("sprk_todo", "none")]
    [InlineData("sprk_event", "faulted")]
    [InlineData("sprk_event", "throws")]
    [InlineData("sprk_event", "none")]
    public async Task TheWriteGate_FailsClosed(string table, string fault)
    {
        var (_, _, plain) = Seed(table);
        var service = fault switch
        {
            "faulted" => _data.NewService(visibility: ExternalVisibilitySeams.Faulted),
            "throws" => _data.NewService(visibility: ExternalVisibilitySeams.Throws),
            _ => NoSeam(),
        };

        (await service.CheckExternalVisibilityAsync(KindOf(table), Guid.Parse(plain), Contact()))
            .Should().Be(ExternalDataService.LiveParentDecision.Unavailable);
    }

    /// <summary>
    /// The production composition (<c>AddExternalAccess</c>) registers the seam, and the typed-client factory hands it to
    /// <see cref="ExternalDataService"/> (an optional constructor parameter): without this the lists would fail closed (every row
    /// hidden) in production. The test doubles above pass the seam explicitly; this pins that production does too.
    /// </summary>
    [Fact]
    public async Task TheSeam_IsRegistered_AndReachesTheDataService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                .Build());
        services.AddSingleton<Azure.Core.TokenCredential>(
            new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential());
        services.AddExternalAccess();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var registered = scope.ServiceProvider.GetRequiredService<AppOnlyExternalVisibility>();
        var data = scope.ServiceProvider.GetRequiredService<ExternalDataService>();
        typeof(ExternalDataService)
            .GetField("_visibility", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(data).Should().BeSameAs(registered,
                "the typed-client factory hands the scope's registered seam to the optional parameter (no seam would hide every row)");
    }
}
