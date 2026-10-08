using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// unified-access-control-r2 task 173 (owner round 81, GitHub #1423) — the shared stamp path writes the Access Permission a
/// To Do, Event, Communication or Document takes from what it is filed under, in the SAME payload, through the real
/// <see cref="CoreAncestorResolver"/> and the real <see cref="ParentLineageWalk"/> over an in-memory Dataverse
/// (<see cref="SecureChildShareWorld"/>, which evaluates each query).
/// </summary>
/// <remarks>
/// <para><b>The rule (goal 1):</b> the most restrictive own value of the records at the TOP of the row's filing (Restricted
/// over Limited over Standard; null = Standard); a row with no filing parent keeps its own value; a read that fails or a
/// filing deeper than <see cref="ParentLineageWalk.MaxDepth"/> writes nothing, logs a warning and never fails the write.</para>
/// <para>The owner's UAT (a To Do through <c>POST /api/office/todo</c>) is <c>OfficeTodoRegardingContractTests</c>; the
/// reconcile is <c>ChildAccessPermissionReconcileTests</c>.</para>
/// </remarks>
public class InheritedAccessPermissionStampTests
{
    private const int Standard = InheritedAccessPermission.Standard;
    private const int Limited = InheritedAccessPermission.Limited;
    private const int Restricted = InheritedAccessPermission.Restricted;
    private const string Column = InheritedAccessPermission.Column;

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly CapturingLogger _log = new();

    [Fact]
    public async Task StampAsync_TodoFiledUnderADocumentOfARestrictedMatter_WritesRestrictedInThePayload()
    {
        var matter = Root("sprk_matter", Restricted);
        var document = Row("sprk_document", ("sprk_matter", "sprk_matter", matter));
        var todo = new Entity("sprk_todo") { ["sprk_regardingdocument"] = new EntityReference("sprk_document", document) };

        var outcome = await Resolver().StampAsync(todo, "sprk_document", document);

        outcome.Succeeded.Should().BeTrue();
        todo.GetAttributeValue<OptionSetValue>(Column)!.Value.Should().Be(Restricted,
            "the To Do shows the value of the matter at the top of its filing, not the column's Standard default");
        outcome.InheritedAccessPermission.Should().Be(Restricted);
    }

    [Theory]
    [InlineData(Standard, Limited, Limited)]
    [InlineData(Limited, Restricted, Restricted)]
    [InlineData(null, null, Standard)]
    public async Task StampAsync_TodoFiledUnderTwoRoots_WritesTheMostRestrictiveOfThem(int? matterValue, int? projectValue, int expected)
    {
        var matter = Root("sprk_matter", matterValue);
        var project = Root("sprk_project", projectValue);
        var todo = new Entity("sprk_todo")
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
            ["sprk_regardingproject"] = new EntityReference("sprk_project", project),
        };

        await Resolver().StampAsync(todo, "sprk_matter", matter);

        todo.GetAttributeValue<OptionSetValue>(Column)!.Value.Should().Be(expected);
    }

    [Fact]
    public async Task StampAsync_TodoOnAParentlessDocumentHeldRestricted_WritesRestricted()
    {
        // A parentless child's own value is what a record filed under it inherits (it is the top of that filing).
        var document = Row("sprk_document", own: Restricted);
        var todo = new Entity("sprk_todo") { ["sprk_regardingdocument"] = new EntityReference("sprk_document", document) };

        await Resolver().StampAsync(todo, "sprk_document", document);

        todo.GetAttributeValue<OptionSetValue>(Column)!.Value.Should().Be(Restricted);
    }

    [Fact]
    public async Task StampAsync_TodoFiledUnderOnlyAContact_KeepsTheUsersOwnValue()
    {
        // A contact is not a filing parent (the lineage says so): the To Do is parentless, so the value the user chose stays.
        var contact = Guid.NewGuid();
        var todo = new Entity("sprk_todo")
        {
            ["sprk_regardingcontact"] = new EntityReference("contact", contact),
            [Column] = new OptionSetValue(Limited),
        };

        var outcome = await Resolver().StampAsync(todo, "contact", contact);

        outcome.Succeeded.Should().BeTrue();
        todo.GetAttributeValue<OptionSetValue>(Column)!.Value.Should().Be(Limited, "a parentless row's own value is never overwritten");
        outcome.InheritedAccessPermission.Should().BeNull();
    }

    [Fact]
    public async Task StampAsync_WhenTheParentCannotBeRead_WritesNothing_LogsAWarning_AndTheWriteProceeds()
    {
        var matter = Root("sprk_matter", Restricted);
        _world.FailingQueriesOf("sprk_matter");
        var todo = new Entity("sprk_todo") { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter) };

        var outcome = await Resolver().StampAsync(todo, "sprk_matter", matter);

        outcome.Succeeded.Should().BeTrue("the value is display only: an unreadable parent never fails the create");
        todo.Contains(Column).Should().BeFalse("no Standard is written by default");
        _log.Warnings.Should().Contain(w => w.Contains("without its inherited Access Permission"));
    }

    [Fact]
    public async Task StampAsync_WhenTheFilingRunsDeeperThanTheWalkFollows_WritesNothing_AndLogsAWarning()
    {
        // event ← event ← … ← event, one more level than the walk follows, with a matter at the very top.
        var top = Root("sprk_matter", Restricted);
        var below = Row("sprk_event", ("sprk_regardingmatter", "sprk_matter", top));
        for (var i = 0; i < ParentLineageWalk.MaxDepth; i++)
            below = Row("sprk_event", ("sprk_regardingevent", "sprk_event", below));
        var todo = new Entity("sprk_todo") { ["sprk_regardingevent"] = new EntityReference("sprk_event", below) };

        var outcome = await Resolver().StampAsync(todo, "sprk_event", below);

        outcome.Succeeded.Should().BeTrue();
        todo.Contains(Column).Should().BeFalse();
        _log.Warnings.Should().Contain(w => w.Contains($"deeper than {ParentLineageWalk.MaxDepth} levels"));
    }

    [Fact]
    public async Task StampAsync_WhenTheHostHasNoAccessPermissionColumn_WritesNothing()
    {
        // Deploy order: a BFF ahead of the schema must not fail every create by writing a column the table lacks.
        var matter = Root("sprk_matter", Restricted);
        var todo = new Entity("sprk_todo") { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter) };

        var outcome = await Resolver(tablesWithoutColumn: "sprk_todo").StampAsync(todo, "sprk_matter", matter);

        outcome.Succeeded.Should().BeTrue();
        todo.Contains(Column).Should().BeFalse();
    }

    [Fact]
    public async Task DeriveForHostAsync_EventFiledUnderAnInvoiceOfALimitedMatter_ReturnsLimitedForTheCallersPayload()
    {
        var matter = Root("sprk_matter", Limited);
        var invoice = Row("sprk_invoice", ("sprk_matter", "sprk_matter", matter));

        var outcome = await Resolver().DeriveForHostAsync("sprk_event", "sprk_invoice", invoice);

        outcome.Succeeded.Should().BeTrue();
        outcome.InheritedAccessPermission.Should().Be(Limited);
    }

    [Fact]
    public async Task ResolveInheritedAccessPermissionAsync_AnAddedStandardParent_DoesNotHideTheRestrictedParentTheRowAlreadyHas()
    {
        // The inbound association engine ADDS a regarding and never clears one: the row's stored matter still counts.
        var restrictedMatter = Root("sprk_matter", Restricted);
        var standardProject = Root("sprk_project", Standard);
        var communication = Row("sprk_communication", ("sprk_regardingmatter", "sprk_matter", restrictedMatter));
        var update = new Dictionary<string, object>
        {
            ["sprk_regardingproject"] = new EntityReference("sprk_project", standardProject),
        };

        var level = await Resolver().ResolveInheritedAccessPermissionAsync("sprk_communication", update, communication);

        level.Should().Be(Restricted);
    }

    [Fact]
    public async Task StampAsync_OnARefile_AFilingThatLeadsBackToTheRowItself_NeverBringsBackTheParentItIsLeaving()
    {
        // The to-do is stored under a Restricted matter and is being re-filed to a Standard project and a document whose
        // only link is back to the to-do. Reading the to-do's STORED row through the document would re-add the matter.
        var oldMatter = Root("sprk_matter", Restricted);
        var newProject = Root("sprk_project", Standard);
        var todo = Row("sprk_todo", Restricted, ("sprk_regardingmatter", "sprk_matter", oldMatter));
        var document = Row("sprk_document", ("sprk_relatedtodo", "sprk_todo", todo));
        var update = new Entity("sprk_todo", todo)
        {
            ["sprk_regardingmatter"] = null,
            ["sprk_regardingproject"] = new EntityReference("sprk_project", newProject),
            ["sprk_regardingdocument"] = new EntityReference("sprk_document", document),
        };

        await Resolver().StampAsync(update, "sprk_project", newProject);

        update.GetAttributeValue<OptionSetValue>(Column)!.Value.Should().Be(Standard,
            "the document is filed under the to-do itself, so it adds nothing; the old matter is not a parent any more");
    }

    [Fact]
    public async Task RefreshInheritedAccessPermissionAsync_AParentedRowShowingAStaleValue_IsSetToItsParentsValue()
    {
        // The client's generic create / re-file (/api/v1/child-records) does not stamp through StampAsync: it calls this
        // after the write, reading the row as stored.
        var matter = Root("sprk_matter", Restricted);
        var todo = Row("sprk_todo", Standard, ("sprk_regardingmatter", "sprk_matter", matter));

        var level = await Resolver().RefreshInheritedAccessPermissionAsync("sprk_todo", todo);

        level.Should().Be(Restricted);
        _world.AccessPermissionWrites.Should().ContainSingle().Which.Should().Be(("sprk_todo", todo, Restricted));
    }

    [Fact]
    public async Task RefreshInheritedAccessPermissionAsync_AParentlessRow_IsNeverWritten()
    {
        var todo = Row("sprk_todo", Limited);

        var level = await Resolver().RefreshInheritedAccessPermissionAsync("sprk_todo", todo);

        level.Should().BeNull();
        _world.AccessPermissionWrites.Should().BeEmpty("the value its creator chose stands (round 81)");
    }

    [Fact]
    public async Task TopsAsync_ADocumentWhoseOnlyLinkIsItsOwnCurrentVersion_IsParentless()
    {
        // sprk_currentversionid names the document's own file version (whose sprk_document names it again): not a parent.
        var document = Guid.NewGuid();
        var version = Guid.NewGuid();
        _world.Add("sprk_fileversion", version, ("sprk_document", new EntityReference("sprk_document", document)));
        _world.Add("sprk_document", document, ("sprk_currentversionid", new EntityReference("sprk_fileversion", version)),
            (Column, new OptionSetValue(Limited)));
        var walk = Walk();
        var row = (await World().RetrieveMultipleAsync(ById("sprk_document", document, await walk.ColumnsForAsync("sprk_document", default)))).Entities.Single();

        var parents = await walk.ParentsOfAsync(row, default);

        parents.Should().BeEmpty("a document with a file but no filing keeps its own value and stays editable");
    }

    [Fact]
    public async Task TopsAsync_TwoEventsFiledUnderEachOtherAndNothingElse_AreUndetermined_NeverEachOthersValue()
    {
        // A loop with no record above it: deciding either from the other would swap their values on every run.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _world.Add("sprk_event", a, ("sprk_regardingevent", new EntityReference("sprk_event", b)), (Column, new OptionSetValue(Restricted)));
        _world.Add("sprk_event", b, ("sprk_regardingevent", new EntityReference("sprk_event", a)), (Column, new OptionSetValue(Standard)));

        var answer = await InheritedAccessPermission.ResolveAsync(Walk(), [("sprk_event", b)], default);

        answer.Status.Should().Be(ParentTopsStatus.Undetermined);
        answer.Value.Should().BeNull();
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────────────────────

    private Guid Root(string table, int? value)
    {
        var id = Guid.NewGuid();
        _world.OrdinaryRoot(table, id);
        if (value is { } v)
            _world.Set(table, id, Column, new OptionSetValue(v));
        return id;
    }

    private Guid Row(string table, params (string Column, string Target, Guid Id)[] lookups) => Row(table, null, lookups);

    private Guid Row(string table, int? own, params (string Column, string Target, Guid Id)[] lookups)
    {
        var id = Guid.NewGuid();
        _world.OrdinaryChild(table, id, lookups);
        if (own is { } v)
            _world.Set(table, id, Column, new OptionSetValue(v));
        return id;
    }

    private IGenericEntityService World()
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        // CoreAncestorResolver reads an intermediate target's root columns by id; answer it from the same rows.
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken _) =>
                Task.FromResult(_world.Answer(ById(table, id, columns)).Entities.SingleOrDefault()
                                ?? throw new InvalidOperationException($"Test: {table} {id} does not exist.")));
        return entities.Object;
    }

    private static QueryExpression ById(string table, Guid id, IReadOnlyList<string> columns)
    {
        var query = new QueryExpression(table) { ColumnSet = new ColumnSet(columns.ToArray()) };
        query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
        return query;
    }

    private CoreAncestorResolver Resolver(params string[] tablesWithoutColumn) =>
        new(World(), Probe(tablesWithoutColumn), _log);

    private ParentLineageWalk Walk() => new(World(), Probe(), [Column]);

    /// <summary>Every lineage lookup, root column and stamp column exists; so does sprk_accesspermission, except where named.</summary>
    internal static CoreAncestorResolver.EntityColumnProbe Probe(params string[] tablesWithoutColumn) => (table, _) =>
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (SecureChildLineage.Children.TryGetValue(table, out var lineage))
            columns.UnionWith(lineage.Lookups.Keys);
        if (CoreAncestorResolver.IntermediateRootColumns.TryGetValue(table, out var roots))
            columns.UnionWith(roots.Select(r => r.Column));
        columns.UnionWith(CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute));
        if (!tablesWithoutColumn.Contains(table, StringComparer.OrdinalIgnoreCase))
            columns.Add(Column);
        return Task.FromResult<IReadOnlySet<string>>(columns);
    };

    private sealed class CapturingLogger : ILogger<CoreAncestorResolver>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
