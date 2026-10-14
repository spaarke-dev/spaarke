using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 187 (owner round 99, D-123; #1857): the live-parent half of
/// <see cref="ExternalMultiRootRule"/> — <see cref="ChildRowRoots"/>, <see cref="ExternalMultiRootRule.ReadLiveParentsAsync"/>
/// (bound, faults), the message filter and the documents module, and the <c>$select</c> of every external child-row read.
/// </summary>
[Trait("category", "external-access")]
[Trait("status", "repaired")]
public sealed class ExternalLiveParentRuleTests
{
    private static readonly Guid M = Guid.Parse("a1870000-0000-4000-8000-00000000010a");
    private static readonly Guid X = Guid.Parse("a1870000-0000-4000-8000-0000000001dd");
    private static readonly Guid C = Guid.Parse("c1870000-0000-4000-8000-000000000101");

    private static CallerPrincipal Reader(params Guid[] matters) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = matters.ToDictionary(m => m, _ => AccessRights.Read),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    /// <summary>A reader that files every asked parent under <paramref name="root"/>.</summary>
    private static LiveParentRootReader FiledUnder(Guid root, List<int>? calls = null) => (parents, _) =>
    {
        calls?.Add(parents.Count);
        return Task.FromResult(new LiveParentRootsRead(parents.ToDictionary(
            p => p,
            _ => (IReadOnlyList<(ExternalGrantRootType, Guid)>)new[] { (ExternalGrantRootType.Matter, root) })));
    };

    /// <summary>A flag read that knows nothing is open: every unreadable root counts as secure.</summary>
    private static readonly NonSecureRootReader NothingOpen = (_, _) =>
        Task.FromResult(new NonSecureRootsRead(new HashSet<(ExternalGrantRootType, Guid)>()));

    private static LiveParentLookup[] CommunicationParent(Guid id) => new[] { new LiveParentLookup("sprk_communication", id.ToString()) };

    // ---- ChildRowRoots + ChildRowReadable ----

    [Fact]
    public async Task TheUnion_WithholdsARowWhoseParentIsUnderASecureRootTheCallerCannotRead()
    {
        var live = await ExternalMultiRootRule.ReadLiveParentsAsync(FiledUnder(X), new[] { CommunicationParent(C) }, NullLogger.Instance, default);
        var row = ChildRowRoots.Of(ExternalMultiRootRule.Stamps(null, M.ToString(), null), CommunicationParent(C), live);

        ExternalMultiRootRule.ChildRowReadable(Reader(M), SecureRootFlags.None, row).Should().BeFalse();
        ExternalMultiRootRule.ChildRowReadable(Reader(M, X), SecureRootFlags.None, row).Should().BeTrue();
        row.Should().Contain((ExternalGrantRootType.Matter, X.ToString("D")), "the live root joins the stamped one");
    }

    [Fact]
    public void ADefaultChildRowRoots_IsWithheld()
    {
        ExternalMultiRootRule.ChildRowReadable(Reader(M), SecureRootFlags.None, default).Should().BeFalse();
    }

    [Fact]
    public void AParentTheReadDidNotResolve_IsWithheld_AndARowNamingNoParent_IsDecidedOnItsStamps()
    {
        var unresolved = ChildRowRoots.Of(ExternalMultiRootRule.Stamps(null, M.ToString(), null), CommunicationParent(C), LiveParentRoots.Empty);
        var noParent = ChildRowRoots.Of(ExternalMultiRootRule.Stamps(null, M.ToString(), null), null, LiveParentRoots.Empty);

        ExternalMultiRootRule.ChildRowReadable(Reader(M), SecureRootFlags.None, unresolved).Should().BeFalse();
        ExternalMultiRootRule.ChildRowReadable(Reader(M), SecureRootFlags.None, noParent).Should().BeTrue();
    }

    // ---- ReadLiveParentsAsync: batching, bound, faults ----

    [Fact]
    public async Task ThePage_IsResolvedInOneCall_OverTheDistinctParents()
    {
        var calls = new List<int>();
        var rows = Enumerable.Range(0, 10).Select(_ => CommunicationParent(C)).ToList(); // ten rows, one shared parent

        var live = await ExternalMultiRootRule.ReadLiveParentsAsync(FiledUnder(M, calls), rows, NullLogger.Instance, default);

        calls.Should().Equal(1);
        live.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task NoParent_MeansNoRead()
    {
        var calls = new List<int>();
        var live = await ExternalMultiRootRule.ReadLiveParentsAsync(
            FiledUnder(M, calls), new[] { Array.Empty<LiveParentLookup>() }, NullLogger.Instance, default);

        calls.Should().BeEmpty();
        live.Should().BeSameAs(LiveParentRoots.Empty);
    }

    /// <summary>
    /// The RULE's own trim only: it never asks the reader for more than the bound. This fake reader resolves everything it is
    /// asked, so it says nothing about the walk's bound (fix round 1, R1: the real walk once withheld every row of a 251-row
    /// page while this test passed). The walk's bound is tested through the REAL walk in <c>ExternalLiveParentReadTests</c>
    /// (<c>APageOfTodos_EachUnderItsOwnCommunicationAndThread_ListsEveryRow</c>,
    /// <c>ThePartOfALevelThatFitsTheBound_IsRead_AndOnlyTheChainsItCuts_AreUndetermined</c>,
    /// <c>PastTheBound_OnlyTheRowWhoseParentsDoNotFit_IsWithheld_AndTheListSaysTruncated</c>).
    /// </summary>
    [Fact]
    public async Task TheRuleAsksTheReaderForAtMostTheBound_AndAParentPastIt_IsUnresolved()
    {
        var parents = Enumerable.Range(0, ExternalMultiRootRule.MaxLiveParentReads + 1)
            .Select(_ => CommunicationParent(Guid.NewGuid())).ToList();
        var calls = new List<int>();

        var live = await ExternalMultiRootRule.ReadLiveParentsAsync(FiledUnder(M, calls), parents, NullLogger.Instance, default);

        calls.Should().Equal(ExternalMultiRootRule.MaxLiveParentReads);
        live.IsComplete.Should().BeFalse();
        parents.Count(p => ExternalMultiRootRule.ChildRowReadable(
                Reader(M), SecureRootFlags.None, ChildRowRoots.Of(Array.Empty<(ExternalGrantRootType, string?)>(), p, live)))
            .Should().Be(ExternalMultiRootRule.MaxLiveParentReads, "the parent the rule did not ask for is unresolved");
    }

    [Fact]
    public async Task AReaderThatThrows_ResolvesNothing_FailClosed()
    {
        LiveParentRootReader throws = (_, _) => throw new TimeoutException("test");

        var live = await ExternalMultiRootRule.ReadLiveParentsAsync(throws, new[] { CommunicationParent(C) }, NullLogger.Instance, default);

        live.IsComplete.Should().BeFalse();
        live.Faulted.Should().BeTrue();
        ExternalMultiRootRule.ChildRowReadable(Reader(M), SecureRootFlags.None,
            ChildRowRoots.Of(Array.Empty<(ExternalGrantRootType, string?)>(), CommunicationParent(C), live)).Should().BeFalse();
    }

    [Fact]
    public async Task NoReader_ResolvesNothing_FailClosed()
    {
        var live = await ExternalMultiRootRule.ReadLiveParentsAsync(null, new[] { CommunicationParent(C) }, NullLogger.Instance, default);

        live.IsComplete.Should().BeFalse();
        ExternalMultiRootRule.ChildRowReadable(Reader(M), SecureRootFlags.None,
            ChildRowRoots.Of(Array.Empty<(ExternalGrantRootType, string?)>(), CommunicationParent(C), live)).Should().BeFalse();
    }

    [Fact]
    public async Task AnAnswerForAParentThatWasNotAsked_IsIgnored()
    {
        LiveParentRootReader liar = (_, _) => Task.FromResult(new LiveParentRootsRead(
            new Dictionary<(string, Guid), IReadOnlyList<(ExternalGrantRootType, Guid)>>
            {
                [("sprk_communication", Guid.NewGuid())] = Array.Empty<(ExternalGrantRootType, Guid)>(),
            }));

        var live = await ExternalMultiRootRule.ReadLiveParentsAsync(liar, new[] { CommunicationParent(C) }, NullLogger.Instance, default);

        live.IsComplete.Should().BeFalse();
        ExternalMultiRootRule.ChildRowReadable(Reader(M), SecureRootFlags.None,
            ChildRowRoots.Of(Array.Empty<(ExternalGrantRootType, string?)>(), CommunicationParent(C), live)).Should().BeFalse();
    }

    // ---- AC5: messages and notifications share SecureRootsFilter ----

    [Fact]
    public async Task AMessageWhoseLiveParentIsUnderSecureX_IsWithheld_FromAnMOnlyReader()
    {
        var plain = new MessageRootLookups(null, M.ToString(), null);
        var carrier = new MessageRootLookups(null, M.ToString(), null, new[] { new LiveParentLookup("sprk_invoice", C.ToString()) });

        var mOnly = await ExternalMessageEndpoints.SecureRootsFilter(Reader(M), NothingOpen, NullLogger.Instance, FiledUnder(X))
            (new[] { plain, carrier }, default);
        var both = await ExternalMessageEndpoints.SecureRootsFilter(Reader(M, X), NothingOpen, NullLogger.Instance, FiledUnder(X))
            (new[] { plain, carrier }, default);

        mOnly(plain).Should().BeTrue();
        mOnly(carrier).Should().BeFalse();
        both(carrier).Should().BeTrue();
    }

    [Fact]
    public async Task AMessageNamingAParent_WithNoLiveReader_IsWithheld_FailClosed()
    {
        var carrier = new MessageRootLookups(null, M.ToString(), null, new[] { new LiveParentLookup("sprk_invoice", C.ToString()) });

        var filter = await ExternalMessageEndpoints.SecureRootsFilter(Reader(M), NothingOpen, NullLogger.Instance)(new[] { carrier }, default);

        filter(carrier).Should().BeFalse();
    }

    // ---- AC6: the documents module (/fetch) ----

    [Fact]
    public async Task TheDocumentsModule_WithholdsARowWhoseRelatedEventIsUnderSecureX()
    {
        var module = ExternalModuleRegistryFactory().FindByEntity("sprk_document")!;
        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Guid.NewGuid(),
            Email = "external.user@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights> { [M] = AccessRights.Read },
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
        };
        IReadOnlyDictionary<string, object?> Row(string name, Guid? relatedEvent) => new Dictionary<string, object?>
        {
            ["@logicalName"] = "sprk_document",
            ["sprk_documentname"] = name,
            ["sprk_matter"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", M),
            ["sprk_relatedevent"] = relatedEvent is { } e ? new Microsoft.Xrm.Sdk.EntityReference("sprk_event", e) : null,
        };
        var rows = new[] { Row("Plain", null), Row("Related to secure X", C) };

        var live = await ExternalMultiRootRule.ReadLiveParentsAsync(FiledUnder(X), rows.Select(module.LiveParents), NullLogger.Instance, default);

        module.ScopeRows(caller, rows, SecureRootFlags.None, live).Select(r => r["sprk_documentname"]).Should().Equal("Plain");
        module.ScopeRows(caller, rows, SecureRootFlags.None).Select(r => r["sprk_documentname"])
            .Should().Equal(new object?[] { "Plain" }, "no live read resolves nothing: the related row is withheld (fail closed)");
    }

    // ---- The $select of every external child-row read carries the parent lookups the rule needs ----

    [Theory]
    [InlineData("sprk_todo")]
    [InlineData("sprk_event")]
    [InlineData("sprk_document")]
    [InlineData("sprk_communication")]
    public void EveryChildTable_HasLiveParentLookups_FromTheLineageMap_AndNoRootLookup(string table)
    {
        var lookups = ExternalMultiRootRule.LiveParentLookupsOf(table);

        lookups.Should().NotBeEmpty();
        lookups.Select(l => l.Table).Should().NotContain(new[] { "sprk_project", "sprk_matter", "sprk_workassignment" });
        lookups.Select(l => l.Column).Should().BeSubsetOf(Sprk.Bff.Api.Services.Dataverse.ParentLineage.ChildFiling[table].Keys);
    }

    [Fact]
    public void TheListAndSingleRowReads_SelectEveryLiveParentLookup()
    {
        var todo = Uri.UnescapeDataString(ExternalDataService.BuildTodoListUrl("https://org.test/api/data/v9.2", ExternalDataService.TodoRootKind.Matter, M));
        var events = Uri.UnescapeDataString(ExternalDataService.BuildEventsQuery(ExternalDataService.TodoRootKind.Matter, M));
        var documents = Uri.UnescapeDataString(ExternalDataService.BuildDocumentListUrl(
            "https://org.test/api/data/v9.2", ExternalDataService.DocumentParentKind.Matter, M));
        var todoCheck = ExternalDataService.BuildLiveParentCheckUrl("https://org.test/api/data/v9.2", ExternalDataService.DeletableKind.Todo, C);
        var eventCheck = ExternalDataService.BuildLiveParentCheckUrl("https://org.test/api/data/v9.2", ExternalDataService.DeletableKind.Event, C);

        todo.Should().ContainAll(ExternalMultiRootRule.LiveParentSelect("sprk_todo"));
        todoCheck.Should().ContainAll(ExternalMultiRootRule.LiveParentSelect("sprk_todo"));
        events.Should().ContainAll(ExternalMultiRootRule.LiveParentSelect("sprk_event"));
        eventCheck.Should().ContainAll(ExternalMultiRootRule.LiveParentSelect("sprk_event"));
        documents.Should().ContainAll(ExternalMultiRootRule.LiveParentSelect("sprk_document"));
        todo.Should().Contain("_sprk_regardingcommunication_value", "the carrier lookup of the D-123 finding");
        documents.Should().ContainAll("_sprk_relatedevent_value", "_sprk_relatedcommunication_value", "_sprk_relatedtodo_value", "_sprk_invoice_value");
    }

    private static ExternalModuleRegistry ExternalModuleRegistryFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();
        return services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();
    }
}
