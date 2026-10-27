using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// #2247 (uac-r2): a row's owner (<see cref="InheritedAccessPermission.OwnerColumns"/>) and its creator
/// (<see cref="InheritedAccessPermission.CreatorColumns"/>) are written only by the BFF's typed writers. An app-only generic
/// writer never writes them, on ANY table (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>, reasons
/// <see cref="InheritedAccessPermission.AppOnlyOwnerWriteReasonCode"/> and
/// <see cref="InheritedAccessPermission.AppOnlyCreatorWriteReasonCode"/>): the Update Record playbook node and the action seam
/// acting for no person (<see cref="UpdateRecordActionCore"/>), a playbook's output mapping
/// (<see cref="DataverseUpdateHandler"/>), and the field-mapping push (a rule targeting one is skipped). The BFF app user is a
/// System Administrator, so before this an <c>ownerid@odata.bind</c> re-owned any row and a <c>sprk_CreatedByPerson@odata.bind</c>
/// forged the creator the move-out-of-secure check reads, and a <c>sprk_RequestedBy@odata.bind</c> handed a service request
/// to another contact (its requester scope, message and document access and hard delete). Must fire: each column, in its
/// plain, case-variant, <c>@odata.bind</c> and <c>_{column}_value</c> forms, on a document, a matter, an event, a work
/// assignment and a service request. Must not fire: neighbouring names. Drift: every column the external plane's creator
/// list names (<c>ExternalDataService.CreatorColumns</c>) is refused here too.
/// </summary>
public class OwnerCreatorColumnAppOnlyWriterTests
{
    private static readonly Guid Row = Guid.Parse("22470000-0000-0000-0000-00000000e401");
    private static readonly Guid Caller = Guid.Parse("22470000-0000-0000-0000-00000000e4ca");
    private static readonly Guid Forged = Guid.Parse("22470000-0000-0000-0000-00000000e4ff");

    private static readonly string[] Owner = ["ownerid", "owninguser", "owningteam", "owningbusinessunit"];
    private static readonly string[] Creator = ["sprk_createdbyperson", "sprk_createdbycontact", "sprk_requestedby"];
    private static readonly string[] Tables = ["sprk_document", "sprk_matter", "sprk_event", "sprk_workassignment", "sprk_servicerequest"];

    /// <summary>
    /// Platform audit columns Dataverse itself refuses to take from a create or update payload (they are read-only; the
    /// platform sets them from the calling identity), so <see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/> need not
    /// list them. The external plane's creator list strips them from client input all the same.
    /// </summary>
    private static readonly string[] PlatformAuditColumns = ["createdby", "modifiedby", "createdonbehalfby"];

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>Each column in each form a payload key can take, on each table, with the column it names and its code.</summary>
    public static TheoryData<string, string, string, string> EveryForm()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var table in Tables)
        {
            foreach (var (columns, code) in new[]
                     {
                         (Owner, InheritedAccessPermission.AppOnlyOwnerWriteReasonCode),
                         (Creator, InheritedAccessPermission.AppOnlyCreatorWriteReasonCode),
                     })
            {
                foreach (var column in columns)
                {
                    data.Add(table, column, column, code);
                    data.Add(table, $" {column.ToUpperInvariant()} ", column, code);
                    data.Add(table, $"{column}@odata.bind", column, code);
                    data.Add(table, $"_{column}_value", column, code);
                }
            }
        }

        // The navigation-property spellings a configuration writes for the two lookups.
        foreach (var table in Tables)
        {
            data.Add(table, "OwnerId@odata.bind", "ownerid", InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
            data.Add(table, "sprk_CreatedByPerson@odata.bind", "sprk_createdbyperson", InheritedAccessPermission.AppOnlyCreatorWriteReasonCode);
            data.Add(table, "sprk_CreatedByContact@odata.bind", "sprk_createdbycontact", InheritedAccessPermission.AppOnlyCreatorWriteReasonCode);
            data.Add(table, ExternalDataService.RequestedByBindKey, "sprk_requestedby", InheritedAccessPermission.AppOnlyCreatorWriteReasonCode);
        }

        return data;
    }

    /// <summary>A column of each set in each form, on the tables the end-to-end writers are driven over.</summary>
    public static TheoryData<string, string, string> WriterCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var table in Tables)
        {
            data.Add(table, "ownerid@odata.bind", InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
            data.Add(table, "_ownerid_value", InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
            data.Add(table, "OwningTeam", InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
            data.Add(table, "sprk_CreatedByPerson@odata.bind", InheritedAccessPermission.AppOnlyCreatorWriteReasonCode);
            data.Add(table, "sprk_createdbycontact", InheritedAccessPermission.AppOnlyCreatorWriteReasonCode);
            data.Add(table, "sprk_RequestedBy@odata.bind", InheritedAccessPermission.AppOnlyCreatorWriteReasonCode);
        }

        return data;
    }

    // ── The refusal itself ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryForm))]
    public void AnOwnerOrCreatorColumn_InAnyForm_IsRefusedOnEveryTable(string table, string key, string column, string code)
    {
        var refusal = InheritedAccessPermission.RefuseAppOnlyWrite(table, ["sprk_name", key]);

        refusal.Should().NotBeNull();
        refusal!.RefusalCode.Should().Be(code);
        refusal.Reason.Should().StartWith(column + " ");
    }

    [Theory]
    [InlineData("ownerid_note")]
    [InlineData("owneridx")]
    [InlineData("xownerid")]
    [InlineData("owneridtype")]
    [InlineData("owninguserx")]
    [InlineData("owningteamname")]
    [InlineData("owningbusinessunitname")]
    [InlineData("sprk_createdbypersonname")]
    [InlineData("sprk_createdbyperson_old")]
    [InlineData("sprk_createdbycontactname")]
    [InlineData("xsprk_createdbycontact")]
    [InlineData("sprk_requestedbyname")]
    [InlineData("sprk_requestedby_old")]
    [InlineData("sprk_requestedon")]
    [InlineData("sprk_name")]
    public void ANeighbouringName_IsNotRefused(string key)
    {
        foreach (var table in Tables)
            InheritedAccessPermission.RefuseAppOnlyWrite(table, [key]).Should().BeNull($"'{key}' is not refused on {table}");
    }

    [Fact]
    public void TheOwnerSet_IsExactlyTheseColumns() =>
        InheritedAccessPermission.OwnerColumns.Should().BeEquivalentTo(Owner);

    [Fact]
    public void TheCreatorSet_IsExactlyTheseColumns() =>
        InheritedAccessPermission.CreatorColumns.Should().BeEquivalentTo(Creator);

    [Fact]
    public void EveryExternalCreatorColumn_IsRefused_OrIsAPlatformAuditColumn() =>
        NotRefused(ExternalDataService.CreatorColumns).Should().BeEmpty(
            "every column the external plane says only the server writes is refused to the app-only generic writers too");

    [Fact]
    public void TheDriftCheck_FiresOnAnExternalCreatorColumnTheRefusalDoesNotList() =>
        NotRefused([.. ExternalDataService.CreatorColumns, "sprk_submittedbycontact"]).Should().Equal("sprk_submittedbycontact");

    /// <summary>The columns of <paramref name="creatorColumns"/> that some table does not refuse as an owner or creator
    /// column, less the platform audit columns.</summary>
    private static IEnumerable<string> NotRefused(IEnumerable<string> creatorColumns) =>
        creatorColumns
            .Where(c => !PlatformAuditColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .Where(c => Tables.Any(t => Code(t, c) is not (InheritedAccessPermission.AppOnlyOwnerWriteReasonCode
                or InheritedAccessPermission.AppOnlyCreatorWriteReasonCode)))
            .ToList();

    [Fact]
    public void OnADocument_TheDocumentsCodesComeFirst_ThenTheOwner_ThenTheCreator()
    {
        Code("sprk_document", "sprk_createdbyperson", "ownerid", "sprk_accesspermission")
            .Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        Code("sprk_document", "sprk_createdbyperson", "ownerid", "sprk_parentdocument")
            .Should().Be(InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
        Code("sprk_document", "sprk_createdbyperson", "ownerid", "sprk_graphitemidbound")
            .Should().Be(InheritedAccessPermission.AppOnlyDocumentBffColumnWriteReasonCode);
        Code("sprk_document", "sprk_createdbyperson", "ownerid").Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
        Code("sprk_matter", "sprk_createdbyperson", "ownerid").Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode);
    }

    private static string? Code(string table, params string[] keys) =>
        InheritedAccessPermission.RefuseAppOnlyWrite(table, keys)?.RefusalCode;

    // ── Update Record node / action seam (app-only) ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(WriterCases))]
    public async Task APlaybookUpdate_NamingAnOwnerOrCreatorColumn_IsRefused_AndNothingIsWritten(string table, string key, string code)
    {
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "x", [key] = $"/systemusers({Forged:D})" },
                Lookups: null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(code);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Theory]
    [InlineData("sprk_matter", "ownerid", "systemuser", InheritedAccessPermission.AppOnlyOwnerWriteReasonCode)]
    [InlineData("sprk_event", "ownerid", "team", InheritedAccessPermission.AppOnlyOwnerWriteReasonCode)]
    [InlineData("sprk_workassignment", "sprk_CreatedByPerson", "systemuser", InheritedAccessPermission.AppOnlyCreatorWriteReasonCode)]
    [InlineData("sprk_document", "sprk_CreatedByPerson", "systemuser", InheritedAccessPermission.AppOnlyCreatorWriteReasonCode)]
    [InlineData("sprk_servicerequest", "sprk_RequestedBy", "contact", InheritedAccessPermission.AppOnlyCreatorWriteReasonCode)]
    public async Task APlaybookLookup_OntoTheOwnerOrTheCreator_IsRefused_AndNothingIsWritten(
        string table, string field, string target, string code)
    {
        // The node's lookup config: the core builds "{field}@odata.bind" itself, then asks the refusal over it.
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null, LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "x" },
                Lookups: [new RenderedLookup(field, target, Forged.ToString("D"))]),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(code);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Fact]
    public async Task AnImpersonatedUpdate_OfTheOwner_IsNotAskedTheAppOnlyRefusal()
    {
        // A user's own write runs as that user, so Dataverse's privileges decide it (the refusal is for app-only writers).
        var user = Guid.Parse("22470000-0000-0000-0000-00000000e4ee");
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_event", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["ownerid@odata.bind"] = $"/systemusers({user:D})" },
                Lookups: null, ImpersonateSystemUserId: user, PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_event", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("ownerid@odata.bind")),
            It.IsAny<CancellationToken>(), user), Times.Once);
    }

    [Fact]
    public async Task APlaybookUpdate_OfANeighbouringName_IsWritten()
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_event", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_createdbypersonname"] = "x", ["ownerid_note"] = "y" },
                Lookups: null),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_event", Row,
            It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_createdbypersonname") && f.ContainsKey("ownerid_note")),
            It.IsAny<CancellationToken>(), null), Times.Once);
    }

    // ── Output mapping (DataverseUpdateHandler, app-only) ────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(WriterCases))]
    public async Task AnOutputMapping_NamingAnOwnerOrCreatorColumn_IsRefused_AndNothingIsWritten(string table, string key, string code)
    {
        var act = () => Handler().UpdateAsync(
            table, Row, new Dictionary<string, object?> { ["sprk_name"] = "x", [key] = $"/systemusers({Forged:D})" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().Be(code);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Fact]
    public async Task AnOutputMapping_OfANeighbouringName_IsWritten()
    {
        await Handler().UpdateAsync(
            "sprk_event", Row,
            new Dictionary<string, object?> { ["sprk_createdbypersonname"] = "x", ["ownerid_note"] = "y" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_event", Row,
            It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_createdbypersonname") && f.ContainsKey("ownerid_note")),
            It.IsAny<CancellationToken>(), null), Times.Once);
    }

    // ── Field-mapping push (a rule targeting the owner or the creator is skipped; the rest of the push applies) ──

    [Theory]
    [InlineData("sprk_event", "ownerid")]
    [InlineData("sprk_matter", "OwningUser")]
    [InlineData("sprk_workassignment", "sprk_createdbyperson")]
    [InlineData("sprk_document", "sprk_createdbycontact")]
    [InlineData("sprk_servicerequest", "sprk_requestedby")]
    public async Task APushRule_TargetingTheOwnerOrTheCreator_IsSkipped_AndTheOtherColumnsArePushed(string table, string target)
    {
        var (updated, failed, skipped, errors, results) = await Push(
            table,
            Rule("sprk_name", "sprk_description"),
            Rule("sprk_name", target));

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row,
            It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_description")),
            It.IsAny<CancellationToken>(), Caller), Times.Once);
        results.Should().ContainSingle(r => r.TargetField == target)
            .Which.Should().Match<FieldMappingResultDto>(r =>
                r.Status == FieldMappingStatus.Skipped && r.ErrorMessage!.Contains("set by Spaarke"));
    }

    private static FieldMappingRuleDto Rule(string source, string target) =>
        new() { SourceField = source, TargetField = target, SourceFieldType = "Text", TargetFieldType = "Text" };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string table, params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _writes.Object,
            new StampWorld().Restamper,
            rules,
            new Dictionary<string, object?> { ["sprk_name"] = "Matter 2247" },
            table,
            new[] { Row },
            Caller,
            NullLogger.Instance,
            CancellationToken.None);

    private UpdateRecordActionCore Core() => new(
        _writes.Object,
        new ServiceCollection().AddSingleton(EntitySetNameFixtures.Service()).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(),
        NullLogger.Instance);

    private DataverseUpdateHandler Handler()
    {
        var world = new StampWorld();
        return new DataverseUpdateHandler(
            _writes.Object, world.Service, world.Restamper, new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            SecureChildReconcilerFixtures.OverEmptyWorld());
    }
}
