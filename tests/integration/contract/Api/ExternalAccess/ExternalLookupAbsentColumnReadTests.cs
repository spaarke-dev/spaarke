using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0 fix round 3d (closing sweep of the "absent lookup column" class). The root stamp columns of a to-do and an event
/// (<c>_sprk_regarding{project,matter,workassignment}_value</c>), the delete-target parent lookups and the invoice lookups of the record gate
/// follow the rule of rounds 3 to 3c: a column that is ABSENT from a Web API row was not read, so the row is unreadable (hidden on a list,
/// refused on a single read or a write); an explicit JSON null is "not filed". Every Web API caller that feeds those decisions selects the
/// columns (pinned below, the expected sets derived from the registry and the bindings).
/// </summary>
/// <remarks>
/// The routes and service reads are the REAL ones over <see cref="ExternalRootEventReadTests.FakeDataverse"/>, which answers a selected
/// unset lookup as an explicit null and omits a <see cref="ExternalRootEventReadTests.FakeDataverse.MaskedColumns"/> column.
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalLookupAbsentColumnReadTests : IClassFixture<ExternalRootEventReadTests.Fixture>
{
    private const string Api = "https://org.test/api/data/v9.2";

    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-99999999113d");
    private static readonly Guid M = Guid.Parse("a113d000-0000-4000-8000-00000000000a");

    private static readonly string[] Stamps =
        { "_sprk_regardingproject_value", "_sprk_regardingmatter_value", "_sprk_regardingworkassignment_value" };

    private readonly ExternalRootEventReadTests.Fixture _fixture;

    public ExternalLookupAbsentColumnReadTests(ExternalRootEventReadTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private ExternalRootEventReadTests.FakeDataverse Data => _fixture.Data;

    public static TheoryData<string> StampColumns
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var column in Stamps)
                data.Add(column);
            return data;
        }
    }

    // =====================================================================
    // To-do and event lists: a row whose read did not return a root stamp column is hidden
    // =====================================================================

    [Theory(DisplayName = "130-0 fix3d to-do list (must fire): a to-do whose read did not return a root stamp column is hidden; the control lists it")]
    [MemberData(nameof(StampColumns))]
    public async Task TodoList_StampNotReturned_HidesTheRow(string maskedColumn)
    {
        _fixture.Principal = Holding();
        Data.AddTodo("matters", M, "Plain");
        (await Todos()).Value.Should().HaveCount(1, "control: every column returned, explicit nulls");

        Data.MaskedColumns.Add(maskedColumn);

        (await Todos()).Value.Should().BeEmpty($"{maskedColumn} was not returned");
    }

    [Theory(DisplayName = "130-0 fix3d event list (must fire): an event whose read did not return a root stamp column is hidden; the control lists it")]
    [MemberData(nameof(StampColumns))]
    public async Task EventList_StampNotReturned_HidesTheRow(string maskedColumn)
    {
        _fixture.Principal = Holding();
        Data.Add("matters", M, "Plain");
        (await Events()).Value.Should().HaveCount(1, "control: every column returned, explicit nulls");

        Data.MaskedColumns.Add(maskedColumn);

        (await Events()).Value.Should().BeEmpty($"{maskedColumn} was not returned");
    }

    // =====================================================================
    // Single reads and writes: refused
    // =====================================================================

    [Theory(DisplayName = "130-0 fix3d to-do root (must fire): a to-do whose read did not return a root stamp column is Ambiguous (a deny state); the control names its root")]
    [MemberData(nameof(StampColumns))]
    public async Task TodoRoot_StampNotReturned_IsAmbiguous(string maskedColumn)
    {
        var id = Guid.Parse(Data.AddTodo("matters", M, "Plain"));
        var service = Data.NewService();
        var control = await service.GetTodoRootAsync(id);
        control.Kind.Should().Be(ExternalDataService.TodoRootKind.Matter, "control: explicit nulls for the other roots");
        control.RootId.Should().Be(M);

        Data.MaskedColumns.Add(maskedColumn);

        (await service.GetTodoRootAsync(id)).Kind.Should().Be(ExternalDataService.TodoRootKind.Ambiguous, $"{maskedColumn} was not returned");
    }

    [Theory(DisplayName = "130-0 fix3d live-parent check (must fire): a to-do or event whose read did not return a root stamp column is withheld")]
    [InlineData(ExternalDataService.DeletableKind.Todo)]
    [InlineData(ExternalDataService.DeletableKind.Event)]
    public async Task LiveParentCheck_StampNotReturned_IsWithheld(ExternalDataService.DeletableKind kind)
    {
        var caller = Holding();
        var id = kind == ExternalDataService.DeletableKind.Todo ? Data.AddTodo("matters", M, "Plain") : Data.Add("matters", M, "Plain");
        var service = Data.NewService();
        (await service.CheckLiveParentsAsync(kind, Guid.Parse(id), caller)).Should().Be(ExternalDataService.LiveParentDecision.Readable, "control");

        foreach (var column in Stamps)
        {
            Data.MaskedColumns.Clear();
            Data.MaskedColumns.Add(column);

            (await service.CheckLiveParentsAsync(kind, Guid.Parse(id), caller)).Should().Be(
                ExternalDataService.LiveParentDecision.Withheld, $"{column} was not returned");
        }
    }

    [Theory(DisplayName = "130-0 fix3d PATCH to-do (must fire / must not fire): refused when a root stamp column was not returned; applied with explicit nulls")]
    [MemberData(nameof(StampColumns))]
    public async Task PatchTodo_StampNotReturned_IsRefused_AndExplicitNullsApply(string maskedColumn)
    {
        _fixture.Principal = Holding(AccessRights.Read | AccessRights.Write);
        var todo = Data.AddTodo("matters", M, "Plain");
        using var client = _fixture.CreateAuthenticatedClient();

        using (var applied = await client.PatchAsJsonAsync($"/api/v1/external/todos/{todo}", new { sprk_name = "renamed" }))
            applied.StatusCode.Should().Be(HttpStatusCode.NoContent, "control: explicit nulls for the roots the to-do is not filed to");
        Data.Requests.Clear();
        Data.MaskedColumns.Add(maskedColumn);

        using var refused = await client.PatchAsJsonAsync($"/api/v1/external/todos/{todo}", new { sprk_name = "again" });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{maskedColumn} was not returned");
        Data.Requests.Should().NotContain(r => r.StartsWith("PATCH"), "nothing is written");
    }

    // =====================================================================
    // The record gate's invoice lookups (GetInvoiceLookupsAsync): an unread lookup is passed on as unreadable
    // =====================================================================

    [Fact(DisplayName = "130-0 fix3d invoice lookups (must fire / must not fire): a lookup the read did not return is unreadable; an explicit null is omitted (not filed)")]
    public async Task InvoiceLookups_ColumnNotReturned_IsUnreadable_ExplicitNullIsNotFiled()
    {
        var invoice = Guid.NewGuid();
        Data.AddInvoice(invoice, matter: M, project: null);
        var service = Data.NewService();
        var lookups = new[] { "sprk_matter", "sprk_project", "sprk_regardingworkassignment" };

        var control = await service.GetInvoiceLookupsAsync(invoice, lookups);

        control.Should().ContainKey("sprk_matter").WhoseValue.Should().Be(M);
        control.Should().NotContainKey("sprk_project", "control: an explicit null is not filed");
        control.Should().NotContainKey("sprk_regardingworkassignment");

        foreach (var lookup in lookups)
        {
            Data.MaskedColumns.Clear();
            Data.MaskedColumns.Add($"_{lookup}_value");

            var read = await service.GetInvoiceLookupsAsync(invoice, lookups);

            read.Should().ContainKey(lookup, $"{lookup} was not returned").WhoseValue.Should().Be(ExternalMultiRootRule.UnreadableLookup);
            Guid.TryParse(read[lookup]?.ToString(), out _).Should().BeFalse("the gate reads a value that is not a GUID as an unreadable root");
        }
    }

    // =====================================================================
    // Pins: every Web API caller selects the columns its decision reads
    // =====================================================================

    [Fact(DisplayName = "130-0 fix3d pin: every Web API read of a to-do or event's root stamps selects all three stamp columns")]
    public async Task EveryStampReader_SelectsEveryStampColumn()
    {
        var callers = new List<(string Caller, IEnumerable<string> Select)>();
        var rootKinds = new[] { ExternalDataService.TodoRootKind.Project, ExternalDataService.TodoRootKind.Matter, ExternalDataService.TodoRootKind.WorkAssignment };

        foreach (var kind in rootKinds)
        {
            callers.Add(($"GetTodosAsync/{kind}", SelectOf(ExternalDataService.BuildTodoListUrl(Api, kind, M))));
            callers.Add(($"GetEventsAsync/{kind}", SelectOf($"{Api}/{ExternalDataService.BuildEventsQuery(kind, M)}")));
        }

        foreach (var kind in new[] { ExternalDataService.DeletableKind.Todo, ExternalDataService.DeletableKind.Event })
        {
            callers.Add(($"CheckLiveParentsAsync/{kind}", SelectOf(ExternalDataService.BuildLiveParentCheckUrl(Api, kind, M))));
            callers.Add(($"GetDeleteTargetAsync/{kind}", SelectOf(ExternalDataService.BuildDeleteTargetUrl(Api, kind, M))));
        }

        var todo = Guid.Parse(Data.AddTodo("matters", M, "Plain"));
        Data.Urls.Clear();
        await Data.NewService().GetTodoRootAsync(todo);
        callers.Add(("GetTodoRootAsync", SelectOf(Data.Urls.Single())));

        foreach (var includeContent in new[] { true, false })
        {
            callers.Add(($"CommunicationThreadReadService.ExternalSelect/content={includeContent}",
                CommunicationThreadReadService.ExternalSelect(new[] { "_sprk_regardingmatter_value" }, includeContent).Split(',')));
        }

        foreach (var (caller, select) in callers)
            select.Should().Contain(Stamps, $"{caller} feeds a decision over the root stamps");
    }

    [Theory(DisplayName = "130-0 fix3d pin: the delete-target read selects every parent lookup its parse reads, and every invoice link column of the table")]
    [InlineData(ExternalDataService.DeletableKind.Todo, "sprk_todo")]
    [InlineData(ExternalDataService.DeletableKind.Event, "sprk_event")]
    [InlineData(ExternalDataService.DeletableKind.Document, "sprk_document")]
    public void DeleteTargetRead_SelectsEveryLookupItsParseReads(ExternalDataService.DeletableKind kind, string table)
    {
        var select = SelectOf(ExternalDataService.BuildDeleteTargetUrl(Api, kind, M));

        select.Should().Contain(InvoiceLinks.LinkColumnsOf(table).Select(c => $"_{c}_value"));
        select.Should().Contain(new[] { "_sprk_createdbyperson_value", "_sprk_createdbycontact_value" });
        // Every other selected column is a parent lookup: removing it is exactly what the parse reads as unreadable.
        var allNull = select.ToDictionary(c => c, _ => JsonDocument.Parse("null").RootElement.Clone());
        var parents = select.Where(c => ExternalDataService.ParseDeleteTarget(
            kind, allNull.Where(kv => kv.Key != c).ToDictionary(kv => kv.Key, kv => kv.Value)).HasUnreadableParentLink).ToList();
        parents.Should().NotBeEmpty("the binding names at least one parent lookup");
    }

    [Fact(DisplayName = "130-0 fix3d pin: the record gate reads every rule attribute of a secure-roots module (the SDK read omits an unset lookup, so the projection is the proof)")]
    public async Task TheRecordGate_ReadsEveryRuleAttribute_OfEverySecureRootsModule()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();
        var registry = services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();
        var id = Guid.NewGuid();
        var principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = new[] { new CallerProjectAccess { ProjectId = id, Rights = AccessRights.Read } },
            MatterAccess = new Dictionary<Guid, AccessRights> { [id] = AccessRights.Read },
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [id] = AccessRights.Read },
        };

        var modules = registry.Modules.Where(m => m.RequireReadOnSecureRoots).ToList();
        modules.Should().NotBeEmpty();
        foreach (var module in modules)
        {
            string[]? asked = null;
            await ExternalMessageEndpoints.GetRecordRightsDecisionAsync(
                new ExternalMessageEndpoints.MessageRoot("record", module.RecordEntity), id, principal, registry,
                (_, _, attributes, _) =>
                {
                    asked = attributes;
                    return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
                },
                NullLogger.Instance, default);

            asked.Should().NotBeNull($"the gate read the {module.RecordEntity} record");
            asked.Should().Contain(module.RootRuleAttributes, $"{module.RecordEntity}: the SDK omits an unset attribute, so an unprojected one would read as not filed");
        }
    }

    [Fact(DisplayName = "130-0 fix3d pin: a documents module that narrows projects every attribute the narrowing reads (the SDK omits an unset attribute)")]
    public void EveryNarrowingModule_ProjectsEveryNarrowingAttribute()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();
        var registry = services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();

        var modules = registry.Modules.Where(m => m.NarrowsDocuments).ToList();
        modules.Should().NotBeEmpty();
        foreach (var module in modules)
            module.NarrowingAttributes.Should().Contain(ExternalDocumentNarrowing.FetchAttributes, $"module {module.Name} reads them in FromFetch");
    }

    [Fact(DisplayName = "130-0 fix3d pin: the firm-held invoice read projects the hold and the firm column (the SDK omits an unset attribute)")]
    public async Task TheFirmHeldInvoiceRead_ProjectsTheHoldAndTheFirm()
    {
        QueryExpression? sent = null;
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback((QueryExpression query, CancellationToken _) => sent = query)
            .ReturnsAsync(new EntityCollection());

        await ExternalInvoiceVisibility.ReadFirmHeldInvoicesAsync(entities.Object, new[] { Guid.NewGuid() }, default);

        sent.Should().NotBeNull();
        sent!.ColumnSet.AllColumns.Should().BeFalse();
        sent.ColumnSet.Columns.Should().Contain(new[] { InvoiceFirmHold.InvoiceHoldColumn, ExternalInvoiceVisibility.InvoiceFirmColumn });
    }

    [Fact(DisplayName = "130-0 fix3d pin: the grant reconciliation scan projects every root lookup, the organization and the issuer attribute it reads (the SDK omits an unset attribute)")]
    public void TheGrantScan_ProjectsEveryLookupItReads()
    {
        var xml = ExternalAccessReconciliationJob.BuildGrantScanFetchXml(1, null);

        var attributes = ExternalAccessReconciliationJob.RootLookupAttributes.Select(r => r.Attribute)
            .Concat(new[] { ExternalAccessReconciliationJob.OrganizationLookupAttribute, ExternalAccessReconciliationJob.GrantedByContactAttribute })
            .ToList();
        attributes.Should().HaveCountGreaterThan(3);
        foreach (var attribute in attributes)
            xml.Should().Contain($"name=\"{attribute}\"", $"{attribute} is read from the scan's rows");
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static string[] SelectOf(string url) =>
        Uri.UnescapeDataString(url).Split("$select=")[1].Split('&')[0].Split(',');

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
