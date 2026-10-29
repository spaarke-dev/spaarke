using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// ebilling-r1 ISS-010 (#1973), as unified-access-control-r2 answered it: the write core (<see cref="OwnedChildWrite"/>
/// create and re-file) invokes <see cref="InvoiceFamilyCoreRecordGuard"/> for every write of an invoice-family table, so
/// <c>POST /api/v1/child-records/{table}</c>, <c>PATCH /api/v1/child-records/sprk_invoice/{id}</c> and the chat create /
/// update tools are all guarded. An update is decided on the stored lookups (read as the caller) overlaid with the change;
/// a read the guard needs that fails refuses (fail closed); an invoice supersedes only an invoice of the same core record.
/// Driven through the REAL handlers and core over the scripted caller's Dataverse.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid GuardInvoice = Guid.Parse("a2470000-0000-4000-8000-0000000013a1");
    private static readonly Guid SupersededInvoice = Guid.Parse("a2470000-0000-4000-8000-0000000013a2");
    private static readonly Guid OrdinaryProject = Guid.Parse("a2470000-0000-4000-8000-0000000013a3");

    /// <summary>A stored invoice as the caller reads it: its core lookups (the scripted row answers the rest as null).</summary>
    private void StoredInvoice(Guid id, string column, Guid core)
    {
        _user.RowValues[id] = new Dictionary<string, object?> { [$"_{column}_value"] = core.ToString("D") };
        _world.WithRecord("sprk_invoice", id, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { [column] = new EntityReference(column, core) });
    }

    // ── Create: POST /api/v1/child-records/{table} ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_invoice", "sprk_Matter", "sprk_Project")]
    [InlineData("sprk_billingevent", "sprk_matter", "sprk_project")]
    public async Task ChildCreate_AnInvoiceFamilyRowNamingTwoCoreRecords_IsRefusedWithTheGuardsCode_AndCreatesNothing(
        string table, string matterNavigation, string projectNavigation)
    {
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await CreateChild(table, new()
        {
            ["sprk_name"] = "x",
            [$"{matterNavigation}@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            [$"{projectNavigation}@odata.bind"] = $"/sprk_projects({OrdinaryProject:D})",
        });

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        _appCreates.Should().BeEmpty("FR-05: refused, never picked");
    }

    [Fact]
    public async Task ChildCreate_ABillingEventNamingOnlyItsInvoice_IsRefusedNoCoreRecord_TheLinesCoreRecordIsItsOwn()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);

        var result = await CreateChild("sprk_billingevent", new()
        {
            ["sprk_name"] = "New billing event",
            ["sprk_Invoice@odata.bind"] = $"/sprk_invoices({GuardInvoice:D})",
        });

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.NoCoreRecordCode, "D-75: never derived from the invoice");
        _appCreates.Should().BeEmpty();
    }

    // ── Task 026 (fix-round-1 item 3b): a line belongs only to an invoice of its own core record ─────────────────

    private static readonly Guid GuardLine = Guid.Parse("a2470000-0000-4000-8000-0000000013a4");

    /// <summary>A stored line as the caller reads it: its own core lookup and its invoice.</summary>
    private void StoredLine(Guid id, Guid matter, Guid invoice)
    {
        _user.RowValues[id] = new Dictionary<string, object?>
        {
            ["_sprk_matter_value"] = matter.ToString("D"), ["_sprk_invoice_value"] = invoice.ToString("D"),
        };
        _world.WithRecord("sprk_billingevent", id, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", matter), ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice) });
    }

    [Fact]
    public async Task ChildCreate_ALineOfAnInvoiceOfItsOwnMatter_IsCreated()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);

        var result = await CreateChild("sprk_billingevent", new()
        {
            ["sprk_name"] = "x",
            ["sprk_matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_Invoice@odata.bind"] = $"/sprk_invoices({GuardInvoice:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _appCreates.Should().ContainSingle().Which.Table.Should().Be("sprk_billingevent");
    }

    [Fact]
    public async Task ChildCreate_ALineOfAnInvoiceOfAnotherMatter_IsRefusedLineMismatch_AndCreatesNothing()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", SecureMatter);

        var result = await CreateChild("sprk_billingevent", new()
        {
            ["sprk_name"] = "x",
            ["sprk_matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_Invoice@odata.bind"] = $"/sprk_invoices({GuardInvoice:D})",
        });

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_ALineOfAnInvoiceTheCallerCannotRead_IsTheUniformNotFound_AndCreatesNothing()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _user.UnreadableRows.Add(GuardInvoice);

        var result = await CreateChild("sprk_billingevent", new()
        {
            ["sprk_name"] = "x",
            ["sprk_matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_Invoice@odata.bind"] = $"/sprk_invoices({GuardInvoice:D})",
        });

        Status(result).Should().Be(StatusCodes.Status404NotFound, Detail(result));
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecordTool_ALineOfAnInvoiceOfAnotherMatter_IsRefusedLineMismatch()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", SecureMatter);

        var result = await CreateRecord("sprk_billingevent",
            ("sprk_name", JsonSerializer.SerializeToElement("x")),
            Lookup("sprk_matter", "sprk_matter", OrdinaryMatter),
            Lookup("sprk_invoice", "sprk_invoice", GuardInvoice));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecordTool_ALineMovedToAnInvoiceOfAnotherMatter_IsRefusedLineMismatch()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        StoredInvoice(SupersededInvoice, "sprk_matter", SecureMatter);
        StoredLine(GuardLine, OrdinaryMatter, GuardInvoice);

        var result = await UpdateRecord("sprk_billingevent", GuardLine, Lookup("sprk_invoice", "sprk_invoice", SupersededInvoice));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecordTool_ALineMovedToAnotherMatter_KeepingItsStoredInvoice_IsRefusedLineMismatch()
    {
        // The merge keeps the stored invoice (read from the invoice set, not the line's) while the change moves the line's
        // own core record: the invoice stays on the old matter.
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        StoredLine(GuardLine, OrdinaryMatter, GuardInvoice);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var result = await UpdateRecord("sprk_billingevent", GuardLine,
            ("sprk_matter", JsonNull),
            Lookup("sprk_project", "sprk_project", OrdinaryProject));

        result.Success.Should().BeFalse(result.ErrorMessage);
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        reads.Should().Contain(p => p.StartsWith($"sprk_invoices({GuardInvoice:D})?$select=", StringComparison.Ordinal));
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_AnInvoiceSupersedingAnInvoiceOfAnotherCoreRecord_IsRefusedMismatch_AndCreatesNothing()
    {
        StoredInvoice(SupersededInvoice, "sprk_matter", SecureMatter);

        var result = await CreateChild("sprk_invoice", new()
        {
            ["sprk_name"] = "x",
            ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_SupersedesInvoice@odata.bind"] = $"/sprk_invoices({SupersededInvoice:D})",
        });

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.SupersedesCoreRecordMismatchCode);
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_AnInvoiceSupersedingAnInvoiceOfTheSameCoreRecord_IsCreated()
    {
        StoredInvoice(SupersededInvoice, "sprk_matter", OrdinaryMatter);

        var result = await CreateChild("sprk_invoice", new()
        {
            ["sprk_name"] = "x",
            ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_SupersedesInvoice@odata.bind"] = $"/sprk_invoices({SupersededInvoice:D})",
        });

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _appCreates.Should().ContainSingle().Which.Table.Should().Be("sprk_invoice");
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_AnInvoiceSupersedingAnInvoiceTheCallerCannotRead_IsTheUniformNotFound_AndCreatesNothing()
    {
        StoredInvoice(SupersededInvoice, "sprk_matter", OrdinaryMatter);
        _user.UnreadableRows.Add(SupersededInvoice);

        var result = await CreateChild("sprk_invoice", new()
        {
            ["sprk_name"] = "x",
            ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_SupersedesInvoice@odata.bind"] = $"/sprk_invoices({SupersededInvoice:D})",
        });

        Status(result).Should().Be(StatusCodes.Status404NotFound, Detail(result));
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode, "fail closed, and nothing about the row is disclosed");
        _appCreates.Should().BeEmpty();
    }

    // ── Update: PATCH /api/v1/child-records/sprk_invoice/{id} — the stored lookups overlaid with the change ─────

    [Fact]
    public async Task ChildRefile_AnInvoiceFiledUnderAMatter_GivenAProjectToo_IsRefusedMultiple_AndNothingIsWritten()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await RefileChild("sprk_invoice", GuardInvoice,
            new() { ["sprk_Project@odata.bind"] = $"/sprk_projects({OrdinaryProject:D})" });

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode,
            "the stored matter stays, so the row would name a matter and a project");
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_AnInvoiceMovedFromItsMatterToAProject_ClearingTheMatter_IsWritten()
    {
        // D-96 (task 026 fix-round-2): the invoice's one line is already on the project, so the move splits nothing.
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_invoice", GuardInvoice, ("sprk_matter", "sprk_matter", OrdinaryMatter));
        AppLine(GuardInvoice, "sprk_project", "sprk_project", OrdinaryProject);

        var result = await RefileChild("sprk_invoice", GuardInvoice, MoveGuardInvoiceToTheProject());

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle("the clear and the bind overlay the stored matter: one core record");
        _appLineQueries.Should().ContainSingle("the lines are counted once, as the app");
    }

    // ── D-96 (task 026 fix-round-2; uac-r2 re-check F1): an invoice re-file must not split it from its lines ────────

    private readonly List<Entity> _appLines = new();
    private readonly List<QueryExpression> _appLineQueries = new();
    private Exception? _appLinesFault;

    /// <summary>The app-only reader (<see cref="IGenericEntityService"/>): its RetrieveMultiple evaluates the line query over
    /// <see cref="_appLines"/>, the rows the APP sees, whatever the caller can read.</summary>
    private IGenericEntityService AppEntities()
    {
        var app = new Mock<IGenericEntityService>(MockBehavior.Strict);
        app.Setup(a => a.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken _) =>
            {
                _appLineQueries.Add(q);
                return _appLinesFault is { } fault
                    ? Task.FromException<EntityCollection>(fault)
                    : Task.FromResult(new EntityCollection(
                        Sprk.Bff.Api.Tests.Services.Dataverse.QueryEvaluator.Run(q, _appLines).ToList()));
            });
        // UAC-r2 ruling (RecordFamilyEdges, 2026-10-28): the work assignment an invoice would move under, as the APP reads it.
        app.Setup(a => a.RetrieveAsync("sprk_workassignment", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                _appWorkAssignments.TryGetValue(id, out var row) ? row : null!);
        return app.Object;
    }

    private readonly Dictionary<Guid, Entity> _appWorkAssignments = new();

    /// <summary>A stored line of <paramref name="invoice"/> as the APP reads it (one core lookup, or none).</summary>
    private Guid AppLine(Guid invoice, string? column, string? core, Guid coreId)
    {
        var line = new Entity("sprk_billingevent", Guid.NewGuid()) { ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice) };
        if (column is not null && core is not null)
            line[column] = new EntityReference(core, coreId);
        _appLines.Add(line);
        return line.Id;
    }

    private static Dictionary<string, object?> MoveGuardInvoiceToTheProject() => new()
    {
        ["sprk_Matter@odata.bind"] = null,
        ["sprk_Project@odata.bind"] = $"/sprk_projects({OrdinaryProject:D})",
    };

    [Theory]
    [InlineData("sprk_matter", "sprk_matter")] // left on the old matter
    [InlineData(null, null)] // a line that names no core record
    public async Task ChildRefile_AnInvoiceMovedToAProject_WhileALineIsNotOnIt_IsRefusedLineMismatch_AndNothingIsWritten(
        string? column, string? core)
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        AppLine(GuardInvoice, "sprk_project", "sprk_project", OrdinaryProject);
        var stray = AppLine(GuardInvoice, column, core, OrdinaryMatter);

        var result = await RefileChild("sprk_invoice", GuardInvoice, MoveGuardInvoiceToTheProject());

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        Detail(result).Should().NotContain(stray.ToString("D"), "the refusal names no line");
        _user.Patches.Should().BeEmpty("D-75: the line is never re-filed from its invoice, and the invoice is not moved");
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_AnInvoiceWithNoLines_MovedToAProject_IsWritten()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_invoice", GuardInvoice, ("sprk_matter", "sprk_matter", OrdinaryMatter));
        AppLine(SupersededInvoice, "sprk_matter", "sprk_matter", OrdinaryMatter); // another invoice's line is not this one's

        var result = await RefileChild("sprk_invoice", GuardInvoice, MoveGuardInvoiceToTheProject());

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle();
    }

    // ── UAC-r2 ruling (RecordFamilyEdges, 2026-10-28): never an invoice and a work assignment each filed under the other ──

    private static readonly Guid GuardWorkAssignment = Guid.Parse("a2470000-0000-4000-8000-0000000013a5");

    /// <summary>The work assignment as the APP reads it: filed under <paramref name="invoice"/> (null = under no invoice).</summary>
    private void AppWorkAssignment(Guid? invoice)
    {
        var row = new Entity("sprk_workassignment", GuardWorkAssignment) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter) };
        if (invoice is { } filedUnder)
            row[InvoiceFamilyCoreRecordGuard.WorkAssignmentInvoiceColumn] = new EntityReference("sprk_invoice", filedUnder);
        _appWorkAssignments[GuardWorkAssignment] = row;
        _world.WithRecord("sprk_workassignment", GuardWorkAssignment, Directory.ChildBu, owningTeam: Directory.ChildTeam);
    }

    private static Dictionary<string, object?> MoveGuardInvoiceToTheWorkAssignment() => new()
    {
        ["sprk_Matter@odata.bind"] = null,
        ["sprk_RegardingWorkAssignment@odata.bind"] = $"/sprk_workassignments({GuardWorkAssignment:D})",
    };

    [Fact]
    public async Task ChildRefile_AnInvoiceMovedUnderAWorkAssignmentFiledUnderThatInvoice_IsRefusedWithTheCycleCode_AndNothingIsWritten()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        AppWorkAssignment(GuardInvoice);

        var result = await RefileChild("sprk_invoice", GuardInvoice, MoveGuardInvoiceToTheWorkAssignment());

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.WorkAssignmentCycleCode);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChildRefile_AnInvoiceMovedUnderAWorkAssignmentFiledUnderAnotherInvoiceOrNone_IsWritten(bool filedUnderAnotherInvoice)
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        ShareWorld.OrdinaryChild("sprk_invoice", GuardInvoice, ("sprk_matter", "sprk_matter", OrdinaryMatter));
        AppWorkAssignment(filedUnderAnotherInvoice ? SupersededInvoice : null);

        var result = await RefileChild("sprk_invoice", GuardInvoice, MoveGuardInvoiceToTheWorkAssignment());

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle();
    }

    [Fact]
    public async Task ChildRefile_AnInvoiceMoved_WhenItsLinesCannotBeRead_FailsClosedReadFailed_AndNothingIsWritten()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _appLinesFault = new TimeoutException("socket 10.0.0.7 timed out");

        var result = await RefileChild("sprk_invoice", GuardInvoice, MoveGuardInvoiceToTheProject());

        Status(result).Should().Be(StatusCodes.Status500InternalServerError, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, "a fault is never \"no lines\"");
        Detail(result).Should().NotContain("10.0.0.7");
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_AnInvoiceMoved_WhileALineTheCallerCannotReadStaysBehind_IsStillRefused_TheAppCounts()
    {
        // D-96 (owner: "App identity"): the line check counts every line, including one the caller cannot see; the
        // refusal is the same uniform code and names no line.
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        var hidden = AppLine(GuardInvoice, "sprk_matter", "sprk_matter", OrdinaryMatter);
        _user.UnreadableRows.Add(hidden);

        var result = await RefileChild("sprk_invoice", GuardInvoice, MoveGuardInvoiceToTheProject());

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        Detail(result).Should().NotContain(hidden.ToString("D"));
        // Fix-round-2: the only id in the refusal is the core record the caller's own write names (nothing read as the app).
        System.Text.RegularExpressions.Regex.Matches(Detail(result) ?? string.Empty,
                "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
            .Select(m => Guid.Parse(m.Value)).Should().OnlyContain(id => id == OrdinaryProject);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_AnInvoiceUpdateThatMovesNoCoreRecord_NeverCountsItsLines()
    {
        // Only a core-lookup write is a re-file: a supersedes-only change leaves stray lines (pre-existing data) alone.
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        StoredInvoice(SupersededInvoice, "sprk_matter", OrdinaryMatter);
        AppLine(GuardInvoice, "sprk_project", "sprk_project", OrdinaryProject);

        var result = await RefileChild("sprk_invoice", GuardInvoice,
            new() { ["sprk_SupersedesInvoice@odata.bind"] = $"/sprk_invoices({SupersededInvoice:D})" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _appLineQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecordTool_AnInvoiceMovedToAProject_WhileALineStaysOnTheMatter_IsRefusedLineMismatch()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        AppLine(GuardInvoice, "sprk_matter", "sprk_matter", OrdinaryMatter);

        var result = await UpdateRecord("sprk_invoice", GuardInvoice,
            ("sprk_matter", JsonSerializer.SerializeToElement<object?>(null)), Lookup("sprk_project", "sprk_project", OrdinaryProject));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.LineInvoiceCoreRecordMismatchCode);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_AnInvoiceGivenASupersededInvoiceOfAnotherCoreRecord_IsRefusedMismatch_AndNothingIsWritten()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        StoredInvoice(SupersededInvoice, "sprk_matter", SecureMatter);

        var result = await RefileChild("sprk_invoice", GuardInvoice,
            new() { ["sprk_SupersedesInvoice@odata.bind"] = $"/sprk_invoices({SupersededInvoice:D})" });

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.SupersedesCoreRecordMismatchCode,
            "the stored matter is the superseding invoice's core record");
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_WhenTheStoredLookupsCannotBeRead_FailsClosed_AndNothingIsWritten()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.GetOverride = path => path.StartsWith($"sprk_invoices({GuardInvoice:D})?$select=_sprk_matter_value", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "Service unavailable.")
            : null;

        var result = await RefileChild("sprk_invoice", GuardInvoice, new()
        {
            ["sprk_Matter@odata.bind"] = null,
            ["sprk_Project@odata.bind"] = $"/sprk_projects({OrdinaryProject:D})",
        });

        Status(result).Should().Be(StatusCodes.Status503ServiceUnavailable, Detail(result));
        _user.Patches.Should().BeEmpty("a read fault is never \"no stored lookup\"");
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_WhenTheStoredRowLacksAGuardedColumn_FailsClosedReadFailed_AndNothingIsWritten()
    {
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.GetOverride = path => path.StartsWith($"sprk_invoices({GuardInvoice:D})?$select=_sprk_matter_value", StringComparison.Ordinal)
            ? DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { _sprk_project_value = (string?)null }))
            : null;

        var result = await RefileChild("sprk_invoice", GuardInvoice,
            new() { ["sprk_Project@odata.bind"] = $"/sprk_projects({OrdinaryProject:D})" });

        Status(result).Should().Be(StatusCodes.Status500InternalServerError, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode, "a column not returned is never \"not set\"");
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_WhenTheStoredLookupReadThrows_FailsClosedReadFailed_WithoutTheFaultsText()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.GetOverride = path => path.StartsWith($"sprk_invoices({GuardInvoice:D})?$select=_sprk_matter_value", StringComparison.Ordinal)
            ? throw new TimeoutException("socket 10.0.0.7 timed out")
            : null;

        var result = await RefileChild("sprk_invoice", GuardInvoice, new()
        {
            ["sprk_Matter@odata.bind"] = null,
            ["sprk_Project@odata.bind"] = $"/sprk_projects({OrdinaryProject:D})",
        });

        Status(result).Should().Be(StatusCodes.Status500InternalServerError, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        Detail(result).Should().NotContain("10.0.0.7");
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_AnInvoiceUpdateThatLeavesItsGuardedLookupsAlone_IsNotReadOrRefused()
    {
        // A stored invoice that names no core record (two such rows live, task 013 T6): an edit that does not touch its
        // lookups cannot change what the guard decides, so it is neither read for them nor refused.
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var result = await RefileChild("sprk_invoice", GuardInvoice, new() { ["sprk_name"] = "Renamed" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle();
        reads.Should().NotContain(p => p.Contains("_sprk_matter_value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChildRefile_AnInvoiceGivenASupersededInvoiceTheCallerCannotRead_IsTheUniformNotFound_AndNothingIsWritten()
    {
        // On an update the guard reads the superseded invoice BEFORE any AppendTo check on it: as the caller, and a row they
        // cannot read answers exactly as one that does not exist.
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        StoredInvoice(SupersededInvoice, "sprk_matter", OrdinaryMatter);
        _user.UnreadableRows.Add(SupersededInvoice);

        var result = await RefileChild("sprk_invoice", GuardInvoice,
            new() { ["sprk_SupersedesInvoice@odata.bind"] = $"/sprk_invoices({SupersededInvoice:D})" });

        Status(result).Should().Be(StatusCodes.Status404NotFound, Detail(result));
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        Detail(result).Should().NotContain(SupersededInvoice.ToString("D")).And.NotContain("privilege");
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_WhenTheSupersededInvoiceReadFaults_FailsClosedWithTheCallersError_AndNothingIsWritten()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        StoredInvoice(SupersededInvoice, "sprk_matter", OrdinaryMatter);
        _user.GetOverride = path => path.StartsWith($"sprk_invoices({SupersededInvoice:D})?$select=", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(429, DataverseUserClientErrorCodes.ServiceError, "Too many requests.")
            : null;

        var result = await RefileChild("sprk_invoice", GuardInvoice,
            new() { ["sprk_SupersedesInvoice@odata.bind"] = $"/sprk_invoices({SupersededInvoice:D})" });

        Status(result).Should().Be(StatusCodes.Status429TooManyRequests, Detail(result));
        _user.Patches.Should().BeEmpty("a superseded invoice that could not be read is never a match");
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_AnInvoiceMovedToAnotherCoreRecord_KeepingItsStoredSupersedes_IsRefusedMismatch()
    {
        // The merge keeps the stored supersedes (read from the written row's own table) while the change moves the core
        // record: the superseded invoice stays on the old matter, so the pair no longer regards one record.
        StoredInvoice(SupersededInvoice, "sprk_matter", OrdinaryMatter);
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _user.RowValues[GuardInvoice]["_sprk_supersedesinvoice_value"] = SupersededInvoice.ToString("D");
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await RefileChild("sprk_invoice", GuardInvoice, new()
        {
            ["sprk_Matter@odata.bind"] = null,
            ["sprk_Project@odata.bind"] = $"/sprk_projects({OrdinaryProject:D})",
        });

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.SupersedesCoreRecordMismatchCode);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildCreate_AnInvoiceWhoseCoreLookupIsWrittenAsAPlainValue_IsRefused_NeverReadAsUnset()
    {
        var result = await CreateChild("sprk_invoice", new()
        {
            ["sprk_name"] = "x",
            ["sprk_Project@odata.bind"] = $"/sprk_projects({FlaggedProject:D})",
            ["sprk_matter"] = OrdinaryMatter.ToString("D"),
        });

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        ReasonCode(result).Should().Be(InvoiceFamilyCoreRecordGuard.LookupNotACoreRecordCode);
        _appCreates.Should().BeEmpty();
    }

    // ── The chat tools share the same core ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecordTool_AnInvoiceGivenASupersededInvoiceOfAnotherCoreRecord_IsRefusedMismatch()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        StoredInvoice(SupersededInvoice, "sprk_matter", SecureMatter);

        var result = await UpdateRecord("sprk_invoice", GuardInvoice,
            Lookup("sprk_supersedesinvoice", "sprk_invoice", SupersededInvoice));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.SupersedesCoreRecordMismatchCode);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRecordTool_ABillingEventNamingTwoCoreRecords_IsRefusedWithTheGuardsCode()
    {
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await CreateRecord("sprk_billingevent",
            ("sprk_name", JsonSerializer.SerializeToElement("x")),
            Lookup("sprk_matter", "sprk_matter", OrdinaryMatter),
            Lookup("sprk_project", "sprk_project", OrdinaryProject));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        _appCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateRecordTool_AnInvoiceFiledUnderAMatter_GivenAProjectToo_IsRefusedWithTheGuardsCode()
    {
        StoredInvoice(GuardInvoice, "sprk_matter", OrdinaryMatter);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await UpdateRecord("sprk_invoice", GuardInvoice, Lookup("sprk_project", "sprk_project", OrdinaryProject));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode);
        _user.Patches.Should().BeEmpty();
    }

    // ── The merge itself (pure) ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EffectiveLookups_OverlaysTheChangeOnTheStoredLookups()
    {
        var stored = new Dictionary<string, EntityReference?>
        {
            ["sprk_matter"] = new EntityReference("sprk_matter", OrdinaryMatter),
            ["sprk_project"] = null,
            ["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", SecureMatter),
        };
        var change = new DataverseWriteItemMapper.MappedItem("{}", new[] { "sprk_project", "sprk_regardingworkassignment" })
        {
            Lookups = new[] { new DataverseWriteItemMapper.MappedLookup("sprk_project", "sprk_project", "sprk_projects", OrdinaryProject) },
            ClearedColumns = new[] { "sprk_regardingworkassignment" },
        };

        var effective = OwnedChildWrite.EffectiveLookups(stored, change);

        effective["sprk_matter"].Should().BeEquivalentTo(new EntityReference("sprk_matter", OrdinaryMatter), "kept as stored");
        effective["sprk_project"].Should().BeEquivalentTo(new EntityReference("sprk_project", OrdinaryProject), "the bind wins");
        effective["sprk_regardingworkassignment"].Should().BeNull("the clear wins");
    }
}
