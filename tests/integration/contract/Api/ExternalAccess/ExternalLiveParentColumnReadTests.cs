using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0 fix round 3c: a parent lookup column (<see cref="ExternalMultiRootRule.LiveParentLookupsOf"/>) that is ABSENT
/// from a Web API row was not read, so the row is treated as unreadable (the rule's existing fail-closed path for a malformed link) and
/// is refused (a single read, a write) or hidden (a list); an explicit JSON null is "not filed there". Every Web API read that feeds
/// <see cref="ExternalMultiRootRule.LiveParentsOf(string, IReadOnlyDictionary{string, JsonElement})"/> selects every one of those columns
/// (pinned below, the expected set derived from the registry method itself).
/// </summary>
/// <remarks>
/// The routes and service reads are the REAL ones over <see cref="ExternalRootEventReadTests.FakeDataverse"/>, which answers a selected
/// unset lookup as an explicit null and omits a <see cref="ExternalRootEventReadTests.FakeDataverse.MaskedColumns"/> column.
/// The FetchXML overload (module rows) is reached only with the columns projected (<c>ExternalModuleDescriptor.RootRuleAttributes</c>);
/// it keeps "absent = not filed" because the SDK omits a null attribute (pinned below).
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalLiveParentColumnReadTests : IClassFixture<ExternalRootEventReadTests.Fixture>
{
    private const string Api = "https://org.test/api/data/v9.2";

    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999187");
    private static readonly Guid M = Guid.Parse("a1873c00-0000-4000-8000-00000000000a");

    private readonly ExternalRootEventReadTests.Fixture _fixture;

    public ExternalLiveParentColumnReadTests(ExternalRootEventReadTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private ExternalRootEventReadTests.FakeDataverse Data => _fixture.Data;

    /// <summary>The Web API select column of every live-parent lookup of <paramref name="table"/>, from the registry method.</summary>
    internal static string[] ExpectedColumns(string table) =>
        ExternalMultiRootRule.LiveParentLookupsOf(table).Select(l => $"_{l.Column}_value").ToArray();

    public static TheoryData<string> TodoColumns => ColumnsOf("sprk_todo");
    public static TheoryData<string> EventColumns => ColumnsOf("sprk_event");
    public static TheoryData<string> DocumentColumns => ColumnsOf("sprk_document");

    private static TheoryData<string> ColumnsOf(string table)
    {
        var data = new TheoryData<string>();
        foreach (var column in ExpectedColumns(table))
            data.Add(column);
        return data;
    }

    // =====================================================================
    // The rule: an absent column is unreadable; an explicit null is not filed
    // =====================================================================

    [Theory(DisplayName = "130-0 fix3c LiveParentsOf (must fire): a column absent from the row enters as unreadable, one at a time")]
    [InlineData("sprk_todo")]
    [InlineData("sprk_event")]
    [InlineData("sprk_document")]
    [InlineData("sprk_communication")]
    public void LiveParentsOf_AnAbsentColumn_IsUnreadable(string table)
    {
        var allNull = ExpectedColumns(table).ToDictionary(c => c, _ => NullElement());
        foreach (var column in ExpectedColumns(table))
        {
            var row = allNull.Where(kv => kv.Key != column).ToDictionary(kv => kv.Key, kv => kv.Value);

            var parents = ExternalMultiRootRule.LiveParentsOf(table, row);

            parents.Should().ContainSingle($"only {column} was not read").Which.LookupValue.Should().Be(ExternalMultiRootRule.UnresolvedParent);
            Withheld(table, row).Should().BeTrue($"{column} was not read, so the row is withheld");
        }

        ExternalMultiRootRule.LiveParentsOf(table, (IReadOnlyDictionary<string, JsonElement>?)null).Should().HaveCount(ExpectedColumns(table).Length, "a null row read no column")
            .And.OnlyContain(p => p.LookupValue == ExternalMultiRootRule.UnresolvedParent);
        ExternalMultiRootRule.LiveParentsOf(table, new Dictionary<string, JsonElement>()).Should()
            .HaveCount(ExpectedColumns(table).Length);
    }

    [Theory(DisplayName = "130-0 fix3c LiveParentsOf (must not fire): explicit nulls name no parent; a GUID is kept; a non-string is still malformed")]
    [InlineData("sprk_todo")]
    [InlineData("sprk_event")]
    [InlineData("sprk_document")]
    [InlineData("sprk_communication")]
    public void LiveParentsOf_ExplicitNulls_NameNoParent(string table)
    {
        var allNull = ExpectedColumns(table).ToDictionary(c => c, _ => NullElement());

        ExternalMultiRootRule.LiveParentsOf(table, allNull).Should().BeEmpty();
        Withheld(table, allNull).Should().BeFalse("a row filed under no parent is not withheld by one");

        var first = ExpectedColumns(table)[0];
        var id = Guid.NewGuid().ToString();
        var withParent = new Dictionary<string, JsonElement>(allNull) { [first] = JsonSerializer.SerializeToElement(id) };
        ExternalMultiRootRule.LiveParentsOf(table, withParent).Should().ContainSingle().Which.LookupValue.Should().Be(id);

        var malformed = new Dictionary<string, JsonElement>(allNull) { [first] = JsonSerializer.SerializeToElement(42) };
        ExternalMultiRootRule.LiveParentsOf(table, malformed).Should().ContainSingle().Which.LookupValue.Should().Be(ExternalMultiRootRule.UnresolvedParent);
    }

    private static bool Withheld(string table, IReadOnlyDictionary<string, JsonElement> row)
    {
        var live = ExternalMultiRootRule.ReadLiveParentsAsync(
            (parents, _) => Task.FromResult(new LiveParentRootsRead(parents.ToDictionary(
                p => p, _ => (IReadOnlyList<(ExternalGrantRootType, Guid)>)Array.Empty<(ExternalGrantRootType, Guid)>()))),
            new[] { ExternalMultiRootRule.LiveParentsOf(table, row) }, NullLogger.Instance, default).GetAwaiter().GetResult();
        var roots = ChildRowRoots.Of(table, row, ExternalMultiRootRule.Stamps(null, null, null), live);
        return !ExternalMultiRootRule.ChildRowReadable(Holding(), SecureRootFlags.None, roots);
    }

    // =====================================================================
    // Lists: a row whose select left out a live-parent column is hidden
    // =====================================================================

    [Theory(DisplayName = "130-0 fix3c to-do list (must fire): a to-do whose read did not return a live-parent column is hidden; the control lists it")]
    [MemberData(nameof(TodoColumns))]
    public async Task TodoList_ColumnNotReturned_HidesTheRow(string maskedColumn)
    {
        _fixture.Principal = Holding();
        Data.AddTodo("matters", M, "Plain");
        (await Todos()).Value.Should().HaveCount(1, "control: every column returned, explicit nulls");

        Data.MaskedColumns.Add(maskedColumn);

        (await Todos()).Value.Should().BeEmpty($"{maskedColumn} was not returned");
    }

    [Theory(DisplayName = "130-0 fix3c event list (must fire): an event whose read did not return a live-parent column is hidden; the control lists it")]
    [MemberData(nameof(EventColumns))]
    public async Task EventList_ColumnNotReturned_HidesTheRow(string maskedColumn)
    {
        _fixture.Principal = Holding();
        Data.Add("matters", M, "Plain");
        (await Events()).Value.Should().HaveCount(1, "control: every column returned, explicit nulls");

        Data.MaskedColumns.Add(maskedColumn);

        (await Events()).Value.Should().BeEmpty($"{maskedColumn} was not returned");
    }

    [Theory(DisplayName = "130-0 fix3c document list (must fire): a document whose read did not return a live-parent column is hidden; the control lists it")]
    [MemberData(nameof(DocumentColumns))]
    public async Task DocumentList_ColumnNotReturned_HidesTheRow(string maskedColumn)
    {
        var caller = Holding();
        Data.AddDocument("matters", M, "Plain");
        var service = Data.NewService();
        (await service.GetDocumentsAsync(ExternalDataService.DocumentParentKind.Matter, M, caller)).Value.Should().HaveCount(1);

        Data.MaskedColumns.Add(maskedColumn);

        (await service.GetDocumentsAsync(ExternalDataService.DocumentParentKind.Matter, M, caller)).Value.Should().BeEmpty();
    }

    // =====================================================================
    // Single / write paths: refused
    // =====================================================================

    [Theory(DisplayName = "130-0 fix3c live-parent check (must fire): a row whose read did not return a live-parent column is withheld, for to-do, event and document")]
    [InlineData(ExternalDataService.DeletableKind.Todo)]
    [InlineData(ExternalDataService.DeletableKind.Event)]
    [InlineData(ExternalDataService.DeletableKind.Document)]
    public async Task LiveParentCheck_ColumnNotReturned_IsWithheld(ExternalDataService.DeletableKind kind)
    {
        var caller = Holding();
        var id = kind switch
        {
            ExternalDataService.DeletableKind.Todo => Data.AddTodo("matters", M, "Plain"),
            ExternalDataService.DeletableKind.Event => Data.Add("matters", M, "Plain"),
            _ => Data.AddDocument("matters", M, "Plain"),
        };
        var table = TableOf(kind);
        var service = Data.NewService();
        (await service.CheckLiveParentsAsync(kind, Guid.Parse(id), caller)).Should().Be(ExternalDataService.LiveParentDecision.Readable, "control");

        foreach (var column in ExpectedColumns(table))
        {
            Data.MaskedColumns.Clear();
            Data.MaskedColumns.Add(column);

            (await service.CheckLiveParentsAsync(kind, Guid.Parse(id), caller)).Should().Be(
                ExternalDataService.LiveParentDecision.Withheld, $"{column} was not returned");
        }
    }

    [Fact(DisplayName = "130-0 fix3c PATCH to-do (must fire / must not fire): refused when a live-parent column was not returned; applied with explicit nulls")]
    public async Task PatchTodo_ColumnNotReturned_IsRefused_AndExplicitNullsApply()
    {
        _fixture.Principal = Holding(AccessRights.Read | AccessRights.Write);
        var todo = Data.AddTodo("matters", M, "Plain");
        using var client = _fixture.CreateAuthenticatedClient();

        using (var applied = await client.PatchAsJsonAsync($"/api/v1/external/todos/{todo}", new { sprk_name = "renamed" }))
            applied.StatusCode.Should().Be(HttpStatusCode.NoContent, "control: explicit nulls are no parent");
        Data.Requests.Clear();
        Data.MaskedColumns.Add(ExpectedColumns("sprk_todo")[0]);

        using var refused = await client.PatchAsJsonAsync($"/api/v1/external/todos/{todo}", new { sprk_name = "again" });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Data.Requests.Should().NotContain(r => r.StartsWith("PATCH"), "nothing is written");
    }

    [Theory(DisplayName = "130-0 fix3c DELETE own (must fire / must not fire): refused when a live-parent column was not returned; deleted with explicit nulls")]
    [InlineData("todos")]
    [InlineData("events")]
    public async Task DeleteOwn_ColumnNotReturned_IsRefused_AndExplicitNullsDelete(string kind)
    {
        _fixture.Principal = Holding();
        var table = kind == "todos" ? "sprk_todo" : "sprk_event";
        var kept = SeedOwnRow(kind, "Kept");
        var plain = SeedOwnRow(kind, "Plain");
        using var client = _fixture.CreateAuthenticatedClient();

        using (var deleted = await client.DeleteAsync($"/api/v1/external/{kind}/{plain}"))
            deleted.StatusCode.Should().Be(HttpStatusCode.NoContent, "control: explicit nulls are no parent");
        Data.Requests.Clear();
        Data.MaskedColumns.Add(ExpectedColumns(table)[0]);

        using var refused = await client.DeleteAsync($"/api/v1/external/{kind}/{kept}");

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Data.Requests.Should().NotContain(r => r.StartsWith("DELETE"), "nothing is deleted");
    }

    [Theory(DisplayName = "130-0 fix3c single document (must fire): the scoping read without a live-parent column is not readable under the record; with every column it is")]
    [MemberData(nameof(DocumentColumns))]
    public async Task SingleDocument_ColumnNotReturned_IsNotReadableUnderTheRecord(string maskedColumn)
    {
        var caller = Holding();
        var id = Guid.Parse(Data.AddDocument("matters", M, "Plain"));
        var service = Data.NewService();
        var control = await service.GetDocumentParentAndNameAsync(ExternalDataService.DocumentParentKind.Matter, id);
        (await IsReadableUnderAsync(service, control, caller)).Should().BeTrue("control: every column returned");

        Data.MaskedColumns.Add(maskedColumn);
        var masked = await service.GetDocumentParentAndNameAsync(ExternalDataService.DocumentParentKind.Matter, id);

        (await IsReadableUnderAsync(service, masked, caller)).Should().BeFalse($"{maskedColumn} was not returned");
    }

    private static async Task<bool> IsReadableUnderAsync(
        ExternalDataService service, ExternalDataService.DocumentScopeRead document, CallerPrincipal caller)
    {
        var live = await service.ReadLiveParentRootsAsync(new[] { document.Roots.LiveParents });
        var roots = document.Roots.RuleInput(live);
        var flags = await service.ReadSecureRootFlagsAsync(
            caller, new[] { (IEnumerable<(ExternalGrantRootType, string?)>)roots });
        return document.IsReadableUnder(M, caller, flags, live);
    }

    // =====================================================================
    // The message filter: a communication row without a live-parent column is refused
    // =====================================================================

    [Fact(DisplayName = "130-0 fix3c message filter (must fire / must not fire): a row without a live-parent column is withheld; explicit nulls are listed")]
    public async Task MessageFilter_ColumnNotReturned_IsWithheld_ExplicitNullsAreListed()
    {
        // F3b fix round 1: the thread read also selects every filing edge column of a communication (the invoice ancestor walk's);
        // explicit nulls there are not filed, so the row needs no ancestor read.
        var allNull = ExpectedColumns("sprk_communication").Concat(InvoiceLinks.ParentValueColumnsOf("sprk_communication")).Distinct()
            .ToDictionary(c => c, _ => NullElement());
        var absent = allNull.Where(kv => kv.Key != ExpectedColumns("sprk_communication")[0]).ToDictionary(kv => kv.Key, kv => kv.Value);
        var filter = ExternalMessageEndpoints.SecureRootsFilter(
            Holding(), (_, _) => Task.FromResult(new NonSecureRootsRead(new HashSet<(ExternalGrantRootType, Guid)>())),
            NullLogger.Instance, (parents, _) => Task.FromResult(new LiveParentRootsRead(parents.ToDictionary(
                p => p, _ => (IReadOnlyList<(ExternalGrantRootType, Guid)>)Array.Empty<(ExternalGrantRootType, Guid)>()))));
        var explicitNulls = new MessageRootLookups(null, M.ToString(), null, allNull);
        var notRead = new MessageRootLookups(null, M.ToString(), null, absent);

        var readable = await filter(new[] { explicitNulls, notRead }, default);

        readable(explicitNulls).Should().BeTrue();
        readable(notRead).Should().BeFalse("a live-parent column of the communication was not read");
    }

    // =====================================================================
    // Pins: every Web API caller selects every live-parent column (expected set from LiveParentLookupsOf)
    // =====================================================================

    [Fact(DisplayName = "130-0 fix3c pin: every Web API caller of LiveParentsOf selects every LiveParentLookupsOf column of its table")]
    public async Task EveryCaller_SelectsEveryLiveParentColumn_OfItsTable()
    {
        var callers = new List<(string Caller, string Table, IEnumerable<string> Select)>();

        foreach (var kind in Enum.GetValues<ExternalDataService.TodoRootKind>().Where(k =>
                     k is ExternalDataService.TodoRootKind.Project or ExternalDataService.TodoRootKind.Matter
                         or ExternalDataService.TodoRootKind.WorkAssignment))
        {
            callers.Add(($"GetTodosAsync/{kind}", "sprk_todo", SelectOf(ExternalDataService.BuildTodoListUrl(Api, kind, M))));
            callers.Add(($"GetEventsAsync/{kind}", "sprk_event", SelectOf($"{Api}/{ExternalDataService.BuildEventsQuery(kind, M)}")));
        }

        foreach (var kind in Enum.GetValues<ExternalDataService.DocumentParentKind>())
        {
            callers.Add(($"GetDocumentsAsync/{kind}", "sprk_document", SelectOf(ExternalDataService.BuildDocumentListUrl(Api, kind, M))));
            Data.Urls.Clear();
            await Data.NewService().GetDocumentParentAndNameAsync(kind, Guid.NewGuid());
            callers.Add(($"GetDocumentParentAndNameAsync/{kind}", "sprk_document", SelectOf(Data.Urls.Single())));
        }

        foreach (var kind in new[] { ExternalDataService.DeletableKind.Todo, ExternalDataService.DeletableKind.Event, ExternalDataService.DeletableKind.Document })
            callers.Add(($"CheckLiveParentsAsync/{kind}", TableOf(kind), SelectOf(ExternalDataService.BuildLiveParentCheckUrl(Api, kind, M))));

        foreach (var includeContent in new[] { true, false })
        {
            callers.Add(($"CommunicationThreadReadService.ExternalSelect/content={includeContent}", "sprk_communication",
                CommunicationThreadReadService.ExternalSelect(new[] { "_sprk_regardingmatter_value" }, includeContent).Split(',')));
        }

        callers.Add(("ExternalInvoiceVisibility.DecideSourceDocumentsAsync", "sprk_document", ExternalInvoiceVisibility.SourceSelectColumns));

        callers.Should().NotBeEmpty();
        foreach (var (caller, table, select) in callers)
            select.Should().Contain(ExpectedColumns(table), $"{caller} feeds LiveParentsOf over {table}");
    }

    [Fact(DisplayName = "130-0 fix3c pin: the FetchXML overload is reached only with the live-parent attributes projected, for every secure-roots module")]
    public void EveryFetchXmlModule_ProjectsEveryLiveParentAttribute()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();
        var registry = services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();

        var modules = registry.Modules.Where(m => m.RequireReadOnSecureRoots).ToList();
        modules.Should().NotBeEmpty();
        foreach (var module in modules)
        {
            module.RootRuleAttributes.Should().Contain(
                ExternalMultiRootRule.LiveParentLookupsOf(module.RecordEntity).Select(l => l.Column),
                $"module {module.Name} projects the attributes its rows' live parents are read from");
        }
    }

    [Fact(DisplayName = "130-0 fix3c FetchXML overload (must not fire): an unset attribute is omitted by the SDK, so absent is not filed")]
    public void FetchXmlOverload_AbsentAttribute_IsNotFiled()
    {
        ExternalMultiRootRule.LiveParentsOf("sprk_todo", new Dictionary<string, object?>()).Should().BeEmpty();
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>A to-do or event under M, stamped as created by the caller (the delete-own stamp half).</summary>
    private string SeedOwnRow(string kind, string name)
    {
        var id = kind == "todos" ? Data.AddTodo("matters", M, name) : Data.Add("matters", M, name);
        Data.SetColumn(kind == "todos" ? "sprk_todos" : "sprk_events", kind == "todos" ? "sprk_todoid" : "sprk_eventid", id,
            "_sprk_createdbycontact_value", CallerContact.ToString());
        return id;
    }

    private static string TableOf(ExternalDataService.DeletableKind kind) => kind switch
    {
        ExternalDataService.DeletableKind.Todo => "sprk_todo",
        ExternalDataService.DeletableKind.Event => "sprk_event",
        _ => "sprk_document",
    };

    private static string[] SelectOf(string url) =>
        Uri.UnescapeDataString(url).Split("$select=")[1].Split('&')[0].Split(',');

    private static JsonElement NullElement() => JsonDocument.Parse("null").RootElement.Clone();

    private async Task<ExternalCollectionResponse<ExternalTodoDto>> Todos()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return (await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalTodoDto>>($"/api/v1/external/matters/{M}/todos"))!;
    }

    private async Task<ExternalCollectionResponse<ExternalEventDto>> Events()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return (await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalEventDto>>($"/api/v1/external/matters/{M}/events"))!;
    }

    private static CallerPrincipal Holding(AccessRights rights = AccessRights.Read) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [M] = rights },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };
}

/// <summary>
/// Fix round 3c: the single-document download and versions routes refuse a document whose scoping read did not return a live-parent
/// column (the same refusal as a document that is not under the record); an explicit null is served.
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalLiveParentColumnDocumentRouteTests : IClassFixture<ExternalMultiRootDocumentRouteTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999039");
    private static readonly Guid RecordA = Guid.Parse("a0390000-0000-4000-8000-00000000000a");
    private static readonly Guid RecordB = Guid.Parse("a0390000-0000-4000-8000-00000000000b");

    private readonly ExternalMultiRootDocumentRouteTests.Fixture _fixture;

    public ExternalLiveParentColumnDocumentRouteTests(ExternalMultiRootDocumentRouteTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public static TheoryData<string, string> Cases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var route in new[] { "content", "versions" })
                foreach (var column in ExternalLiveParentColumnReadTests.ExpectedColumns("sprk_document"))
                    data.Add(route, column);
            return data;
        }
    }

    [Theory(DisplayName = "130-0 fix3c document routes (must fire): a read that did not return a live-parent column is refused as not under the record, before any pointer is resolved")]
    [MemberData(nameof(Cases))]
    public async Task ADocumentReadWithoutALiveParentColumn_IsRefused(string route, string maskedColumn)
    {
        _fixture.Principal = Holding();
        var reference = await RefusalOf(route);
        _fixture.ResetCalls();
        var id = _fixture.Data.AddDocument("matters", RecordA, "Filed only here");

        using (var served = await Get(id, route))
            served.StatusCode.Should().Be(HttpStatusCode.OK, await served.Content.ReadAsStringAsync());
        _fixture.ResetCalls();

        _fixture.Data.MaskedColumns.Add(maskedColumn);
        using var response = await Get(id, route);

        (await Summarise(response)).Should().Be(reference, $"{maskedColumn} was not returned, so the document reads as not under this record");
        _fixture.PointerResolutions.Should().Be(0, "authorization precedes pointer resolution");
        _fixture.FileReads.Should().Be(0);
    }

    [Theory(DisplayName = "130-0 fix3c document routes (must not fire): explicit nulls for every live-parent column are served")]
    [InlineData("content")]
    [InlineData("versions")]
    public async Task ADocumentWithExplicitNullLiveParents_IsServed(string route)
    {
        _fixture.Principal = Holding();
        var id = _fixture.Data.AddDocument("matters", RecordA, "Filed only here");

        using var response = await Get(id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.PointerResolutions.Should().Be(1);
    }

    private static CallerPrincipal Holding() => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [RecordA] = AccessRights.Read },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    private async Task<string> RefusalOf(string route)
    {
        var elsewhere = _fixture.Data.AddDocument("matters", RecordB, "Another record's");
        using var response = await Get(elsewhere, route);
        var summary = await Summarise(response);
        summary.Should().StartWith("403");
        return summary;
    }

    private async Task<HttpResponseMessage> Get(string documentId, string route)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.GetAsync($"/api/v1/external/matters/{RecordA}/documents/{documentId}/{route}");
    }

    private static async Task<string> Summarise(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return $"{(int)response.StatusCode}";

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return string.Join("|", new[] { "403" }.Concat(doc.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance" or "correlationId" or "type"))
            .Select(p => $"{p.Name}={p.Value}")));
    }
}
