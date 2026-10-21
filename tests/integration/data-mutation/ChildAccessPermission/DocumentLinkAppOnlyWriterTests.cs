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
/// unified-access-control-r2 task 188 round 4e (D7): a document's two link columns, <c>sprk_parentdocument</c> and
/// <c>sprk_relatedcommunication</c>, are inputs of THE external document narrowing (rules 3 and 4). An app-only generic
/// writer never writes them (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>, reason
/// <see cref="InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode"/>): the Update Record playbook node and the
/// action seam acting for no person (<see cref="UpdateRecordActionCore"/>), a playbook's output mapping
/// (<see cref="DataverseUpdateHandler"/>), and the field-mapping push (a rule targeting one is skipped). Re-pointing or
/// clearing either link could drop a Restricted parent or an internal-only email from the walk and show the document to
/// partners. Must fire: set, cleared, the bind form and the <c>_value</c> form, any case. Must not fire: the document's other
/// columns (a similar name included) and lookups, and the same column names on other tables.
/// </summary>
public class DocumentLinkAppOnlyWriterTests
{
    private static readonly Guid Row = Guid.Parse("18800000-0000-0000-0000-00000000e401");
    private static readonly Guid Target = Guid.Parse("18800000-0000-0000-0000-00000000e402");
    private static readonly Guid Caller = Guid.Parse("18800000-0000-0000-0000-00000000e4ca");

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>Each link column, set (to a row id) and cleared (a null), in its plain and its odd-case form.</summary>
    public static TheoryData<string, string?> SetOrCleared() => new()
    {
        { "sprk_parentdocument", Target.ToString() },
        { "sprk_parentdocument", null },
        { "sprk_relatedcommunication", Target.ToString() },
        { "sprk_relatedcommunication", null },
        { " SPRK_ParentDocument ", Target.ToString() },
        { "_sprk_relatedcommunication_value", null },
    };

    // ── Update Record node / action seam (app-only) ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SetOrCleared))]
    public async Task APlaybookUpdate_SettingOrClearingADocumentLink_IsRefused_AndNothingIsWritten(string column, string? value)
    {
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_documentdescription"] = "x", [column] = value },
                Lookups: null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Theory]
    [InlineData("sprk_ParentDocument", "sprk_document")]
    [InlineData("sprk_RelatedCommunication", "sprk_communication")]
    [InlineData("sprk_parentdocument", "sprk_document")]
    public async Task APlaybookUpdate_BindingADocumentLink_IsRefused_AndNothingIsWritten(string field, string targetTable)
    {
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null, LegacyFields: null,
                Lookups: new[] { new RenderedLookup(field, targetTable, Target.ToString()) }),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Fact]
    public async Task APlaybookUpdate_OfADocumentsOtherColumnsAndANonLinkLookup_IsWritten()
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?>
                {
                    ["sprk_documentdescription"] = "x",
                    ["sprk_parentfilename"] = "mail.eml", // a similar name, not a link
                },
                Lookups: new[] { new RenderedLookup("sprk_contact", "contact", Target.ToString()) }),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_document", Row,
            It.Is<Dictionary<string, object?>>(f =>
                f.ContainsKey("sprk_documentdescription") && f.ContainsKey("sprk_parentfilename") && f.ContainsKey("sprk_contact@odata.bind")),
            It.IsAny<CancellationToken>(), null), Times.Once);
    }

    [Theory]
    [InlineData("sprk_todo", "sprk_parentdocument")]
    [InlineData("sprk_todo", "sprk_relatedcommunication")]
    [InlineData("sprk_event", "sprk_parentdocument")]
    public async Task APlaybookUpdate_OfTheSameColumnNameOnAnotherTable_IsWritten(string table, string column)
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null, LegacyFields: new Dictionary<string, string?> { [column] = "x" },
                Lookups: null),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey(column)), It.IsAny<CancellationToken>(), null),
            Times.Once);
    }

    // ── Output mapping (DataverseUpdateHandler, app-only) ────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SetOrCleared))]
    public async Task AnOutputMapping_SettingOrClearingADocumentLink_IsRefused_AndNothingIsWritten(string column, string? value)
    {
        var act = () => Handler().UpdateAsync(
            "sprk_document", Row, new Dictionary<string, object?> { ["sprk_documentdescription"] = "x", [column] = value },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Theory]
    [InlineData("sprk_ParentDocument@odata.bind", "/sprk_documents(18800000-0000-0000-0000-00000000e402)")]
    [InlineData("sprk_RelatedCommunication@odata.bind", "/sprk_communications(18800000-0000-0000-0000-00000000e402)")]
    [InlineData("SPRK_PARENTDOCUMENT@ODATA.BIND", null)]
    public async Task AnOutputMapping_BindingADocumentLink_IsRefused_AndNothingIsWritten(string key, string? bind)
    {
        var act = () => Handler().UpdateAsync(
            "sprk_document", Row, new Dictionary<string, object?> { [key] = bind },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Fact]
    public async Task AnOutputMapping_OfADocumentsOtherColumns_IsWritten()
    {
        await Handler().UpdateAsync(
            "sprk_document", Row,
            new Dictionary<string, object?> { ["sprk_documentdescription"] = "x", ["sprk_parentfilename"] = "mail.eml" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_document", Row,
            It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_documentdescription") && f.ContainsKey("sprk_parentfilename")),
            It.IsAny<CancellationToken>(), null), Times.Once);
    }

    [Theory]
    [InlineData("sprk_todo", "sprk_parentdocument")]
    [InlineData("sprk_event", "sprk_relatedcommunication")]
    public async Task AnOutputMapping_OfTheSameColumnNameOnAnotherTable_IsWritten(string table, string column)
    {
        await Handler().UpdateAsync(
            table, Row, new Dictionary<string, object?> { [column] = "x" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey(column)), It.IsAny<CancellationToken>(), null),
            Times.Once);
    }

    // ── Field-mapping push (a rule targeting a link is skipped; the rest of the push applies) ────────────────────

    [Theory]
    [InlineData("sprk_parentdocument", "sprk_name")] // set
    [InlineData("sprk_parentdocument", "sprk_empty")] // cleared (the source holds no value)
    [InlineData("sprk_relatedcommunication", "sprk_name")]
    [InlineData("sprk_relatedcommunication", "sprk_empty")]
    [InlineData("sprk_ParentDocument@odata.bind", "sprk_name")] // the bind form
    public async Task APushRule_TargetingADocumentLink_IsSkipped_AndTheOtherColumnsArePushed(string target, string source)
    {
        var (updated, failed, skipped, errors, results) = await Push(
            "sprk_document",
            Rule("sprk_name", "sprk_documentdescription"),
            Rule(source, target));

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_document", Row,
            It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_documentdescription")),
            It.IsAny<CancellationToken>(), Caller), Times.Once);
        results.Should().ContainSingle(r => r.TargetField == target)
            .Which.Should().Match<FieldMappingResultDto>(r =>
                r.Status == FieldMappingStatus.Skipped && r.ErrorMessage!.Contains("set by Spaarke"));
    }

    [Fact]
    public async Task APushOfOnlyADocumentLink_WritesNothing_AndCountsTheChildSkipped()
    {
        var (updated, failed, skipped, _, results) = await Push("sprk_document", Rule("sprk_name", "sprk_relatedcommunication"));

        (updated, failed, skipped).Should().Be((0, 0, 1));
        _writes.Invocations.Should().BeEmpty("a document's links are set by Spaarke");
        results.Should().ContainSingle().Which.Status.Should().Be(FieldMappingStatus.Skipped);
    }

    [Theory]
    [InlineData("sprk_todo", "sprk_parentdocument")]
    [InlineData("sprk_event", "sprk_relatedcommunication")]
    public async Task APushRule_TargetingTheSameColumnNameOnAnotherTable_IsPushed(string table, string target)
    {
        var (updated, failed, _, _, results) = await Push(table, Rule("sprk_name", target));

        (updated, failed).Should().Be((1, 0));
        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey(target)), It.IsAny<CancellationToken>(), Caller),
            Times.Once);
        results.Should().ContainSingle().Which.Status.Should().Be(FieldMappingStatus.Mapped);
    }

    // ── The column set is the predicate's ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheRefusedLinks_AreExactlyTheColumnsTheNarrowingWalks()
    {
        InheritedAccessPermission.DocumentLinkColumns.Should().BeEquivalentTo(
            ExternalDocumentNarrowing.ParentDocumentAttribute, ExternalDocumentNarrowing.RelatedCommunicationAttribute);
    }

    [Fact]
    public void AnAccessColumnBesideALink_IsRefusedAsTheAccessColumn()
    {
        // The own-narrowing refusal keeps its code when a payload names both (task 189's callers and tests read it).
        InheritedAccessPermission.RefuseAppOnlyWrite("sprk_document", ["sprk_parentdocument", "sprk_accesspermission"])
            .Should().Match<RecordOwnerResolution>(r => r.RefusalCode == InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
    }

    private static FieldMappingRuleDto Rule(string source, string target) =>
        new() { SourceField = source, TargetField = target, SourceFieldType = "Text", TargetFieldType = "Text" };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string table, params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _writes.Object,
            new StampWorld().Restamper,
            rules,
            new Dictionary<string, object?> { ["sprk_name"] = "Matter 188", ["sprk_empty"] = null },
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
