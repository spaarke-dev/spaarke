using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0 fix round 3d (closing sweep of the "absent lookup column" class): on a Web API (JSON) row an ABSENT lookup
/// column was not read, which is unreadable (each decision's existing fail-closed value); an explicit JSON null is "not filed". These are the
/// pure decisions; the routes over the Web API double are in <c>ExternalLookupAbsentColumnReadTests</c>.
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalLookupAbsentColumnUnitTests
{
    private static JsonElement Null() => JsonDocument.Parse("null").RootElement.Clone();

    private static JsonElement Text(string value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement Number(int value) => JsonSerializer.SerializeToElement(value);

    // =====================================================================
    // ExternalMultiRootRule.LookupOf / StampOf (the shared reader)
    // =====================================================================

    [Fact(DisplayName = "130-0 fix3d LookupOf (must fire): a column absent from the row, and a null row, are unreadable")]
    public void LookupOf_AnAbsentColumn_IsUnreadable()
    {
        var row = new Dictionary<string, JsonElement> { ["_other_value"] = Null() };

        ExternalMultiRootRule.LookupOf(row, "_sprk_matter_value").Should().Be(ExternalMultiRootRule.UnreadableLookup);
        ExternalMultiRootRule.LookupOf(null, "_sprk_matter_value").Should().Be(ExternalMultiRootRule.UnreadableLookup);
        ExternalMultiRootRule.LookupOf(row, "_sprk_matter_value").Should().NotBeNull().And.NotBe(string.Empty);
    }

    [Fact(DisplayName = "130-0 fix3d LookupOf (must not fire): an explicit null is not filed; a string is the value; a non-string is unreadable")]
    public void LookupOf_ExplicitNull_IsNotFiled()
    {
        var id = Guid.NewGuid().ToString();
        var row = new Dictionary<string, JsonElement>
        {
            ["_nothing_value"] = Null(),
            ["_text_value"] = Text(id),
            ["_number_value"] = Number(42),
        };

        ExternalMultiRootRule.LookupOf(row, "_nothing_value").Should().BeNull();
        ExternalMultiRootRule.LookupOf(row, "_text_value").Should().Be(id);
        ExternalMultiRootRule.LookupOf(row, "_number_value").Should().Be(ExternalMultiRootRule.UnreadableLookup);
    }

    [Fact(DisplayName = "130-0 fix3d StampOf: a column that was not read is unreadable whatever the held value; a read one is its value")]
    public void StampOf_ReadVersusNotRead()
    {
        ExternalMultiRootRule.StampOf(false, null).Should().Be(ExternalMultiRootRule.UnreadableLookup);
        ExternalMultiRootRule.StampOf(false, "x").Should().Be(ExternalMultiRootRule.UnreadableLookup);
        ExternalMultiRootRule.StampOf(true, null).Should().BeNull();
        ExternalMultiRootRule.StampOf(true, "x").Should().Be("x");
    }

    [Fact(DisplayName = "130-0 fix3d UnreadableLookup: the rule withholds a row carrying it as a root stamp (the fail-closed path of a malformed link)")]
    public void AnUnreadableStamp_IsWithheldByTheRule()
    {
        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Guid.NewGuid(),
            Email = "external.user@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights>(),
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
        };

        ExternalMultiRootRule.SecureRootsReadable(caller, SecureRootFlags.None, ExternalMultiRootRule.Stamps(null, null, null))
            .Should().BeTrue("control: no stamp is no root");
        ExternalMultiRootRule.SecureRootsReadable(
                caller, SecureRootFlags.None, ExternalMultiRootRule.Stamps(null, ExternalMultiRootRule.UnreadableLookup, null))
            .Should().BeFalse("an unread stamp is an unreadable root, not no root");
    }

    // =====================================================================
    // Delete-own target (ParseDeleteTarget): the parent lookups and the invoice links
    // =====================================================================

    public static TheoryData<ExternalDataService.DeletableKind> DeletableKinds => new()
    {
        ExternalDataService.DeletableKind.Todo,
        ExternalDataService.DeletableKind.Event,
        ExternalDataService.DeletableKind.Document,
    };

    private static string[] SelectOf(ExternalDataService.DeletableKind kind) =>
        Uri.UnescapeDataString(ExternalDataService.BuildDeleteTargetUrl("https://org.test/api/data/v9.2", kind, Guid.NewGuid()))
            .Split("$select=")[1].Split('&')[0].Split(',');

    [Theory(DisplayName = "130-0 fix3d ParseDeleteTarget (must fire): a parent or invoice-link column absent from the row refuses the delete, one column at a time")]
    [MemberData(nameof(DeletableKinds))]
    public void ParseDeleteTarget_AColumnNotReturned_IsUnreadable(ExternalDataService.DeletableKind kind)
    {
        var select = SelectOf(kind);
        var allNull = select.ToDictionary(c => c, _ => Null());
        var control = ExternalDataService.ParseDeleteTarget(kind, allNull);
        control.HasUnreadableParentLink.Should().BeFalse("control: explicit nulls are no parent");
        control.Parents.Should().BeEmpty();
        control.InvoiceLinks.Should().NotBeNull().And.BeEmpty();

        // F3b fix round 1: the read also selects every filing edge column the invoice ancestor walk follows; GetDeleteTargetAsync puts
        // the row to that walk (InvoiceLinks.FirstParentsOf), where an absent one is an unreadable parent. Control: explicit nulls walk
        // nothing.
        var table = TableOf(kind);
        InvoiceLinks.FirstParentsOf(table, Guid.NewGuid(), allNull).Should().BeEmpty("control: explicit nulls are not filed");

        var creatorStamps = new[] { "_sprk_createdbyperson_value", "_sprk_createdbycontact_value" };
        foreach (var column in select.Except(creatorStamps))
        {
            var row = allNull.Where(kv => kv.Key != column).ToDictionary(kv => kv.Key, kv => kv.Value);

            var target = ExternalDataService.ParseDeleteTarget(kind, row);
            var walked = InvoiceLinks.FirstParentsOf(table, Guid.NewGuid(), row);

            (target.HasUnreadableParentLink || target.InvoiceLinks is null || target.InvoiceLinks.Contains(ExternalDocumentNarrowing.UnreadableLink)
             || walked.Any(p => p.Parent == ExternalDocumentNarrowing.UnreadableLink))
                .Should().BeTrue($"{column} was not returned");
        }
    }

    private static string TableOf(ExternalDataService.DeletableKind kind) => kind switch
    {
        ExternalDataService.DeletableKind.Todo => "sprk_todo",
        ExternalDataService.DeletableKind.Event => "sprk_event",
        _ => "sprk_document",
    };

    [Theory(DisplayName = "130-0 fix3d ParseDeleteTarget (must not fire): a GUID parent is a parent; a blank or null one is none; a non-GUID is still unreadable")]
    [MemberData(nameof(DeletableKinds))]
    public void ParseDeleteTarget_ReturnedColumns_AreReadAsBefore(ExternalDataService.DeletableKind kind)
    {
        var select = SelectOf(kind);
        var allNull = select.ToDictionary(c => c, _ => Null());
        var creatorStamps = new[] { "_sprk_createdbyperson_value", "_sprk_createdbycontact_value" };
        var id = Guid.NewGuid();

        // The first column whose absence makes the parent link unreadable is a parent lookup.
        var parentColumn = select.Except(creatorStamps).First(c => ExternalDataService.ParseDeleteTarget(
            kind, allNull.Where(kv => kv.Key != c).ToDictionary(kv => kv.Key, kv => kv.Value)).HasUnreadableParentLink);

        var named = new Dictionary<string, JsonElement>(allNull) { [parentColumn] = Text(id.ToString()) };
        var target = ExternalDataService.ParseDeleteTarget(kind, named);
        target.HasUnreadableParentLink.Should().BeFalse();
        target.Parents.Should().ContainSingle().Which.Id.Should().Be(id);

        var blank = new Dictionary<string, JsonElement>(allNull) { [parentColumn] = Text(" ") };
        ExternalDataService.ParseDeleteTarget(kind, blank).HasUnreadableParentLink.Should().BeFalse("a blank value is not filed");

        var malformed = new Dictionary<string, JsonElement>(allNull) { [parentColumn] = Text("not-a-guid") };
        ExternalDataService.ParseDeleteTarget(kind, malformed).HasUnreadableParentLink.Should().BeTrue();
    }

    [Theory(DisplayName = "130-0 fix3d ParseDeleteTarget: a creator stamp that was not returned matches nobody")]
    [MemberData(nameof(DeletableKinds))]
    public void ParseDeleteTarget_AnAbsentCreatorStamp_MatchesNobody(ExternalDataService.DeletableKind kind)
    {
        var select = SelectOf(kind);
        var contact = Guid.NewGuid();
        var row = select.ToDictionary(c => c, _ => Null());
        row.Remove("_sprk_createdbyperson_value");
        row.Remove("_sprk_createdbycontact_value");
        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = contact,
            Email = "external.user@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights>(),
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
        };

        ExternalDataService.IsCreatedByCaller(ExternalDataService.ParseDeleteTarget(kind, row), caller).Should().BeFalse();

        row["_sprk_createdbycontact_value"] = Text(contact.ToString());
        ExternalDataService.IsCreatedByCaller(ExternalDataService.ParseDeleteTarget(kind, row), caller).Should().BeTrue("control: the stamp that was returned matches");
    }

    // =====================================================================
    // ExternalInvoiceVisibility.LinkOf (the invoice link reader every invoice-visibility read uses)
    // =====================================================================

    [Fact(DisplayName = "130-0 fix3d LinkOf (must fire): a column absent from the row is unreadable, never no link")]
    public void LinkOf_AnAbsentColumn_IsUnreadable()
    {
        var row = new Dictionary<string, JsonElement> { ["_other_value"] = Null() };

        ExternalInvoiceVisibility.LinkOf(row, "_sprk_invoice_value").Should().Be(ExternalDocumentNarrowing.UnreadableLink);
    }

    [Fact(DisplayName = "130-0 fix3d LinkOf (must not fire): an explicit null is no link; a GUID is the link; anything else is unreadable")]
    public void LinkOf_ExplicitNull_IsNoLink()
    {
        var id = Guid.NewGuid();
        var row = new Dictionary<string, JsonElement>
        {
            ["_nothing_value"] = Null(),
            ["_guid_value"] = Text(id.ToString()),
            ["_text_value"] = Text("not-a-guid"),
            ["_empty_value"] = Text(Guid.Empty.ToString()),
            ["_number_value"] = Number(7),
        };

        ExternalInvoiceVisibility.LinkOf(row, "_nothing_value").Should().BeNull();
        ExternalInvoiceVisibility.LinkOf(row, "_guid_value").Should().Be(id);
        ExternalInvoiceVisibility.LinkOf(row, "_text_value").Should().Be(ExternalDocumentNarrowing.UnreadableLink);
        ExternalInvoiceVisibility.LinkOf(row, "_empty_value").Should().Be(ExternalDocumentNarrowing.UnreadableLink);
        ExternalInvoiceVisibility.LinkOf(row, "_number_value").Should().Be(ExternalDocumentNarrowing.UnreadableLink);
    }

    // =====================================================================
    // ExternalDocumentNarrowing.FromWebApi (the document narrowing rows)
    // =====================================================================

    [Fact(DisplayName = "130-0 fix3d document narrowing (must fire): a link column absent from the row is unreadable, one at a time")]
    public void FromWebApi_ALinkColumnNotReturned_IsUnreadable()
    {
        var id = Guid.NewGuid();
        var allNull = ExternalDocumentNarrowing.SelectColumns.ToDictionary(c => c, _ => Null());

        foreach (var column in ExternalDocumentNarrowing.SelectColumns.Where(c => c != ExternalDocumentNarrowing.AccessPermissionField))
        {
            var row = allNull.Where(kv => kv.Key != column).ToDictionary(kv => kv.Key, kv => kv.Value);

            var narrowing = ExternalDocumentNarrowing.FromWebApi(id, row);

            new[] { narrowing.Communication, narrowing.Parent }.Should().Contain(ExternalDocumentNarrowing.UnreadableLink, $"{column} was not returned");
        }

        var none = ExternalDocumentNarrowing.FromWebApi(id, null);
        none.Communication.Should().Be(ExternalDocumentNarrowing.UnreadableLink);
        none.Parent.Should().Be(ExternalDocumentNarrowing.UnreadableLink);
    }

    [Fact(DisplayName = "130-0 fix3d document narrowing (must not fire): explicit nulls are not narrowed and link nowhere; a GUID is kept")]
    public void FromWebApi_ExplicitNulls_AreNotNarrowedAndLinkNowhere()
    {
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var allNull = ExternalDocumentNarrowing.SelectColumns.ToDictionary(c => c, _ => Null());

        var narrowing = ExternalDocumentNarrowing.FromWebApi(id, allNull);

        narrowing.Own.Should().Be(DocumentPermission.NotNarrowed);
        narrowing.Communication.Should().Be(Guid.Empty);
        narrowing.Parent.Should().Be(Guid.Empty);

        var withParent = new Dictionary<string, JsonElement>(allNull) { [ExternalDocumentNarrowing.ParentDocumentValue] = Text(parent.ToString()) };
        ExternalDocumentNarrowing.FromWebApi(id, withParent).Parent.Should().Be(parent);
    }

    // =====================================================================
    // CommunicationThreadReadService.RootsOf (the communication root stamps)
    // =====================================================================

    private static readonly string[] Stamps =
        { "_sprk_regardingproject_value", "_sprk_regardingmatter_value", "_sprk_regardingworkassignment_value" };

    [Fact(DisplayName = "130-0 fix3d communication stamps (must fire): a stamp column absent from the row is malformed, so the row is withheld; one at a time")]
    public void RootsOf_AStampNotReturned_IsMalformed()
    {
        var allNull = Stamps.ToDictionary(c => c, _ => Null());

        foreach (var column in Stamps)
        {
            var row = allNull.Where(kv => kv.Key != column).ToDictionary(kv => kv.Key, kv => kv.Value);

            var roots = CommunicationThreadReadService.RootsOf(row);

            new[] { roots.Project, roots.Matter, roots.WorkAssignment }.Should().ContainSingle(v => v == "malformed", $"{column} was not returned");
            ExternalMultiRootRule.SecureRootsReadable(
                    Caller(), SecureRootFlags.None,
                    ExternalMultiRootRule.Stamps(roots.Project, roots.Matter, roots.WorkAssignment))
                .Should().BeFalse("the multi-root rule withholds a malformed stamp");
        }
    }

    [Fact(DisplayName = "130-0 fix3d communication stamps (must not fire): explicit nulls are not filed; a GUID string is the stamp; a number is malformed")]
    public void RootsOf_ExplicitNulls_AreNotFiled()
    {
        var matter = Guid.NewGuid().ToString();
        var allNull = Stamps.ToDictionary(c => c, _ => Null());

        var none = CommunicationThreadReadService.RootsOf(allNull);
        none.Project.Should().BeNull();
        none.Matter.Should().BeNull();
        none.WorkAssignment.Should().BeNull();

        var filed = CommunicationThreadReadService.RootsOf(new Dictionary<string, JsonElement>(allNull) { [Stamps[1]] = Text(matter) });
        filed.Matter.Should().Be(matter);

        var malformed = CommunicationThreadReadService.RootsOf(new Dictionary<string, JsonElement>(allNull) { [Stamps[0]] = Number(1) });
        malformed.Project.Should().Be("malformed");
    }

    private static CallerPrincipal Caller() => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    // =====================================================================
    // ExternalCallerCommunicationGuard.ReadSourceDocumentIdsAsync (the assistant plane's source-document read)
    // =====================================================================

    private static IDataverseUserClient UserClientAnswering(string json)
    {
        var user = new Mock<IDataverseUserClient>();
        user.Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, JsonDocument.Parse(json).RootElement.Clone()));
        return user.Object;
    }

    private static string AnalysisRows(Guid analysis, params string[] omit)
    {
        var row = new Dictionary<string, object?>
        {
            ["sprk_analysisid"] = analysis.ToString("D"),
            ["_sprk_documentid_value"] = null,
            ["_sprk_outputfileid_value"] = null,
            ["_sprk_regardingdocument_value"] = null,
        };
        foreach (var column in omit)
            row.Remove(column);
        return JsonSerializer.Serialize(new { value = new[] { row } });
    }

    [Fact(DisplayName = "130-0 fix3d assistant source documents (must fire): a link column absent from the row is undecidable (the row is hidden), one column at a time")]
    public async Task SourceDocuments_ALinkColumnNotReturned_IsUnreadable()
    {
        var analysis = Guid.NewGuid();

        foreach (var column in new[] { "_sprk_documentid_value", "_sprk_outputfileid_value", "_sprk_regardingdocument_value" })
        {
            var sources = await ExternalCallerCommunicationGuard.ReadSourceDocumentIdsAsync(
                UserClientAnswering(AnalysisRows(analysis, column)), "sprk_analysis", new[] { analysis }, default);

            sources[analysis].Should().Contain(ExternalDocumentNarrowing.UnreadableLink, $"{column} was not returned");
        }
    }

    [Fact(DisplayName = "130-0 fix3d assistant source documents (must not fire): explicit nulls are no document; a GUID is the document")]
    public async Task SourceDocuments_ExplicitNulls_AreNoDocument()
    {
        var analysis = Guid.NewGuid();

        var none = await ExternalCallerCommunicationGuard.ReadSourceDocumentIdsAsync(
            UserClientAnswering(AnalysisRows(analysis)), "sprk_analysis", new[] { analysis }, default);

        none[analysis].Should().BeEmpty("explicit nulls: the analysis has no document");
    }

    // =====================================================================
    // NoAccessListReader.ParseRows (the deny-list veto read feeding an external caller's accessible set)
    // =====================================================================

    /// <summary>One deny-list page of one row carrying every selected column (explicit nulls), except <paramref name="omitColumn"/>.</summary>
    private static string DenyListBody(string? omitColumn = null)
    {
        var row = NoAccessListReader.RowSelect.Split(',').ToDictionary(c => c, _ => (object?)null);
        row["sprk_noaccessentryid"] = Guid.NewGuid();
        row["_sprk_subjectcontact_value"] = Guid.NewGuid();
        if (omitColumn is not null)
            row.Remove(omitColumn);
        return JsonSerializer.Serialize(new { value = new[] { row } });
    }

    [Fact(DisplayName = "130-0 fix3d deny list (must fire): a row that did not carry every selected column fails the chunk closed, one column at a time")]
    public void NoAccessParseRows_AColumnNotReturned_FailsClosed()
    {
        foreach (var column in NoAccessListReader.RowSelect.Split(','))
        {
            NoAccessListReader.ParseRows(DenyListBody(column), NullLogger.Instance)
                .Should().BeNull($"{column} was not returned, so which subject or object the entry names is unknown");
        }
    }

    [Fact(DisplayName = "130-0 fix3d deny list (must not fire): a row with explicit nulls is read; an empty page is empty")]
    public void NoAccessParseRows_ExplicitNulls_AreRead()
    {
        var rows = NoAccessListReader.ParseRows(DenyListBody(), NullLogger.Instance);

        rows.Should().ContainSingle().Which._sprk_subjectorganization_value.Should().BeNull();
        NoAccessListReader.ParseRows("{\"value\":[]}", NullLogger.Instance).Should().BeEmpty();
        NoAccessListReader.ParseRows("{}", NullLogger.Instance).Should().BeEmpty();
    }
}
