using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 017 (phase 2b) — the service-side follow-ons the re-pointed callers rely on,
/// driven through the REAL <see cref="RecordCreationService"/> over this harness:
/// <list type="bullet">
/// <item>014 review R3-1: <see cref="RecordCreationRequest.ServerSetLookupColumns"/> is normalised ONCE, as the request is
/// built, so every reader (the K-2 / F-B guards, the mapping protection, step 5's AppendTo skip) sees the same set;</item>
/// <item>037 review K2: a copied default reads the row AFTER the adapter's post-mapping defaults are merged, and is
/// server-set only when its source is;</item>
/// <item>#1608 (decision 2b): a secure root's server-owned columns are refused in a caller's payload on every path, the same
/// set on a matter, a project and a work assignment;</item>
/// <item>a document's access record is refused for every principal; its Access Permission is the caller's, never a system
/// principal's;</item>
/// <item>coordinator item 8: every invoice-family table's core lookups are mapping-protected, so a mapping that fills a
/// second one is skipped with a warning instead of the create being refused.</item>
/// </list>
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    // =====================================================================================
    // 014 R3-1 — the server-set set is normalised once
    // =====================================================================================

    [Fact]
    public void Request_ServerSetLookupColumns_AreTrimmedLowerCasedAndCaseInsensitive_HoweverTheAdapterBuiltTheSet()
    {
        var request = new RecordCreationRequest("sprk_workassignment", new RecordCreationPayload.WebApi(default))
        {
            ServerSetLookupColumns = new HashSet<string>(StringComparer.Ordinal) { " SPRK_AssignedTo ", "   " },
        };

        request.ServerSetLookupColumns.Should().BeEquivalentTo(["sprk_assignedto"], "trimmed, lower-cased, blanks dropped");
        request.ServerSetLookupColumns!.Contains("SPRK_ASSIGNEDTO").Should().BeTrue("compared case-insensitively");

        var widened = request with
        {
            ServerSetLookupColumns = new HashSet<string>(StringComparer.Ordinal) { "sprk_assignedto", " Sprk_AssignedToInternal" },
        };
        widened.ServerSetLookupColumns.Should().BeEquivalentTo(["sprk_assignedto", "sprk_assignedtointernal"],
            "a set given through `with` is normalised the same way");
        new RecordCreationRequest("sprk_event", new RecordCreationPayload.WebApi(default)).ServerSetLookupColumns
            .Should().BeNull("no set stays no set");
    }

    /// <summary>
    /// Before R3-1, step 5's AppendTo skip compared the column with the adapter's own set as given: a padded, mixed-case name in
    /// an ordinal set was "server-set" to the mapping protection (which trimmed) but the caller's own to step 5, which asked
    /// AppendTo on the contact and refused the create.
    /// </summary>
    [Fact]
    public async Task CreateAsync_APaddedMixedCaseServerSetAssignedTo_InAnOrdinalSet_CostsNoAppendTo_AndIsProtectedFromMapping()
    {
        _scripted.NoAppendToIn.Add(("contacts", Assignee));
        var request = Request("sprk_workassignment", new Dictionary<string, object?>
        {
            ["sprk_name"] = "Review the lease",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_AssignedTo@odata.bind"] = $"/contacts({Assignee:D})",
        }, mapping: false) with
        {
            ServerSetLookupColumns = new HashSet<string>(StringComparer.Ordinal) { " SPRK_AssignedTo " },
        };

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_AssignedTo@odata.bind").Should().Be($"/contacts({Assignee:D})");
        RecordCreationService.ProtectedFromMapping(
                "sprk_workassignment", RecordTableRules.Get("sprk_workassignment"), request.ServerSetLookupColumns)
            .Should().Contain("sprk_assignedto", "the mapping protection reads the same, normalised set");
    }

    // =====================================================================================
    // 037 K2 — the copied default runs after the post-mapping defaults merge
    // =====================================================================================

    [Fact]
    public async Task CopiedDefault_ASourceOnlyAnAdapterDefaultFills_IsCopiedToo()
    {
        WorkAssignmentColumnsAndProfile();
        _profiles.Clear(); // nothing maps the Assigned To Internal: only the adapter's default fills it

        var outcome = await Service().CreateAsync(
            MappedWorkAssignment() with
            {
                PostMappingDefaults = new Dictionary<string, object?>
                {
                    ["sprk_assignedtointernal"] = new EntityReference("contact", NamedContact),
                },
            },
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_AssignedToInternal@odata.bind").Should().Be($"/contacts({NamedContact:D})", "the adapter's default");
        StringOf(fields, WaAssignedToBind).Should().Be($"/contacts({NamedContact:D})",
            "037 K2: the copy reads the row after the defaults merge, so it names the record its source will");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    /// <summary>Must not fire: a copy of an adapter's default is the caller's own, never server-set — AppendTo is asked.</summary>
    [Fact]
    public async Task CopiedDefault_ACopyOfAnAdapterDefault_IsNeverServerSet_ItIsCheckedAndDroppedWithItsSource()
    {
        WorkAssignmentColumnsAndProfile();
        _profiles.Clear();
        _scripted.NoAppendToIn.Add(("contacts", NamedContact));

        var outcome = await Service().CreateAsync(
            MappedWorkAssignment() with
            {
                PostMappingDefaults = new Dictionary<string, object?>
                {
                    ["sprk_assignedtointernal"] = new EntityReference("contact", NamedContact),
                },
            },
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Subject.Fields.Keys
            .Should().NotContain(k => k.StartsWith("sprk_AssignedTo", StringComparison.OrdinalIgnoreCase),
                "neither default costs nothing: the caller may not AppendTo the contact, so both are dropped");
        outcome.MappingWarnings.Should().Contain(w =>
            w.Code == RecordCreationWarning.DefaultValueDroppedCode && w.Column == "sprk_assignedto");
    }

    // =====================================================================================
    // #1608 (decision 2b) — a secure root's server-owned columns, refused on every path
    // =====================================================================================

    public static TheoryData<string, string, string> RootServerOwnedColumnsOfEachRoot()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (table, nameColumn) in new[]
                 {
                     ("sprk_matter", "sprk_mattername"), ("sprk_project", "sprk_projectname"), ("sprk_workassignment", "sprk_name"),
                 })
        {
            foreach (var column in new[]
                     {
                         "sprk_containerid", SecurableEntityRegistry.SecurityBusinessUnitAttribute, InheritedAccessPermission.Column,
                     })
            {
                data.Add(table, nameColumn, column);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RootServerOwnedColumnsOfEachRoot))]
    public async Task CreateAsync_ARootsServerOwnedColumnInTheCallersPayload_IsRefusedAtStep2_OnEveryRoot(
        string table, string nameColumn, string column)
    {
        var outcome = await Service().CreateAsync(
            Request(table, new Dictionary<string, object?>
            {
                [nameColumn] = "Acme v. Widget",
                [column] = column == InheritedAccessPermission.Column ? 100000002 : "b-0001",
            }, mapping: false),
            AsCaller(), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull();
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.BlockedColumn);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.BlockedColumnCode);
        outcome.Refusal.Detail.Should().Contain(column);
        _journal.Should().NotContain(["owner", "create"]);
        _creates.Should().BeEmpty();
    }

    // =====================================================================================
    // A document's access record (every principal) and its Access Permission (the caller's only)
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_ACallersDocumentNamingItsAccessInheritance_IsRefused()
    {
        var outcome = await Service().CreateAsync(
            Request("sprk_document", new Dictionary<string, object?>
            {
                ["sprk_documentname"] = "Brief.docx",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
                [AccessInheritance.Column] = 100000001,
            }, mapping: false),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.BlockedColumn);
        outcome.Refusal.Detail.Should().Contain(AccessInheritance.Column);
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ACallersDocumentNamingItsAccessPermission_IsCreated_WithIt()
    {
        var outcome = await Service().CreateAsync(
            Request("sprk_document", new Dictionary<string, object?>
            {
                ["sprk_documentname"] = "Brief.docx",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
                [InheritedAccessPermission.Column] = 100000002,
            }, mapping: false),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        ((JsonElement)_creates.Should().ContainSingle().Subject.Fields[InheritedAccessPermission.Column]!).GetInt32()
            .Should().Be(100000002, "UAC-r2 D7: a caller may fill a document's Access Permission at create (must not fire)");
    }

    [Theory]
    [InlineData("sprk_accessinheritance")]
    [InlineData("sprk_accesspermission")]
    [InlineData("sprk_restrictedbysubmission")]
    public async Task CreateAsync_ASystemDocumentNamingItsAccessRecordOrPermission_IsRefused_BeforeAnythingIsAsked(string column)
    {
        var request = await SystemDocument((column, column == "sprk_restrictedbysubmission" ? true : 100000002));

        var outcome = await Service().CreateAsync(
            request, RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull();
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.BlockedColumn);
        outcome.Refusal.Detail.Should().Contain(column);
        _journal.Should().NotContain(["owner", "create"]);
        _creates.Should().BeEmpty();
    }

    /// <summary>Must not fire: a system document that names no access column passes step 2.</summary>
    [Fact]
    public async Task CreateAsync_ASystemDocumentNamingNoAccessColumn_PassesStep2()
    {
        var outcome = await Service().CreateAsync(
            await SystemDocument(), RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        outcome.Refusal?.Kind.Should().NotBe(RecordCreationRefusalKind.BlockedColumn, outcome.Refusal?.Detail);
        outcome.Steps.Single(s => s.Step == RecordCreationStep.TableRules).State.Should().Be(RecordCreationStepState.Skipped,
            "the caller's column policy is still not asked of a system principal (FR-07)");
    }

    private async Task<RecordCreationRequest> SystemDocument(params (string Column, object Value)[] extra)
    {
        var body = new Dictionary<string, object?>
        {
            ["sprk_documentname"] = "Brief.docx",
            ["sprk_matter"] = new { relatedTable = "sprk_matter", recordId = OrdinaryMatter },
        };
        foreach (var (column, value) in extra)
            body[column] = value;
        var mapped = await DataverseWriteItemMapper.MapAsync(
            _scripted, "sprk_document", JsonSerializer.SerializeToElement(body), CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        return new RecordCreationRequest("sprk_document", new RecordCreationPayload.Mapped(mapped.Item!))
        {
            CorrelationId = FixedCorrelation,
        };
    }

    // =====================================================================================
    // Coordinator item 8 — every invoice-family table's core lookups are mapping-protected
    // =====================================================================================

    /// <summary>
    /// An invoice submission filed under a matter (one core record), with a mapping that would fill a SECOND core lookup (the
    /// regarding project): the rule is skipped with a warning and the create proceeds — never refused by the invoice-family
    /// rule as <c>invoice_family_multiple_core_records</c>. On this table the regarding trio is also an access column an
    /// app-only write is refused (<c>sdap.access.access_column_server_only</c>, which drops the rule first), so this pins
    /// the outcome whichever guard skips it; the registry test pins the declared protection.
    /// </summary>
    [Fact]
    public async Task CreateAsync_AnInvoiceSubmission_NeverMapsASecondCoreLookup_TheRuleIsSkipped_AndTheRowIsCreated()
    {
        _scripted.Held.UnionWith(["prvCreatesprk_invoicesubmission", "prvAppendsprk_invoicesubmission"]);
        _user.Columns["sprk_invoicesubmission"] = new()
        {
            ["sprk_regardingmatter"] = ("Lookup", true),
            ["sprk_regardingproject"] = ("Lookup", true),
            ["sprk_regardingworkassignment"] = ("Lookup", true),
            ["sprk_description"] = ("Memo", true),
        };
        Profile("sprk_matter", "sprk_invoicesubmission",
            Rule("sprk_primaryproject", "sprk_regardingproject", Lookup),
            Rule("sprk_matterdescription", "sprk_description", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_invoicesubmission", new Dictionary<string, object?>
            {
                ["sprk_name"] = "Submission 1",
                ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            }, mapping: true),
            AsCaller(), CancellationToken.None);

                outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_RegardingProject@odata.bind", "a second core lookup is never mapped (must fire)");
        StringOf(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})");
        StringOf(fields, "sprk_description").Should().Be("From the matter", "a non-core column is still filled (must not fire)");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Detail.Contains("sprk_regardingproject"));
    }
}
