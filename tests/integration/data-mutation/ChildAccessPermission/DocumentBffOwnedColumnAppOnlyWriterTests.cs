using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// #2240 (uac-r2): a document's BFF-owned columns (<see cref="InheritedAccessPermission.BffOwnedDocumentColumns"/>: the
/// file pointer, its binding and the legacy item id, the relocation record, the file path and the email-archive key) are
/// written only by the BFF's typed writers. An app-only generic writer never writes them (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>,
/// reason <see cref="InheritedAccessPermission.AppOnlyDocumentBffColumnWriteReasonCode"/>): the Update Record playbook node
/// and the action seam acting for no person (<see cref="UpdateRecordActionCore"/>), a playbook's output mapping
/// (<see cref="DataverseUpdateHandler"/>), and the field-mapping push (a rule targeting one is skipped). These writers run
/// as the BFF, so the field security on the pointer columns does not stop them. A configured <c>sprk_graphitemidbound</c>
/// alone makes every app-only read of the document refuse it; with <c>sprk_graphitemid</c> it forges a pointer.
/// Must fire: each column, in its plain, case-variant, <c>@odata.bind</c> and <c>_{column}_value</c> forms. Must not fire:
/// neighbouring names on a document, and the same names on other tables.
/// </summary>
public class DocumentBffOwnedColumnAppOnlyWriterTests
{
    private static readonly Guid Row = Guid.Parse("22400000-0000-0000-0000-00000000e401");
    private static readonly Guid Caller = Guid.Parse("22400000-0000-0000-0000-00000000e4ca");

    private static readonly string[] Owned =
    [
        "sprk_graphdriveid",
        "sprk_graphitemid",
        "sprk_graphitemidbound",
        "sprk_driveitemid",
        "sprk_relocationpending",
        "sprk_relocatedversions",
        "sprk_filepath",
        "sprk_emailmessageid",
        "sprk_isemailarchive",
    ];

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>Each owned column in each form a payload key can take, with the column it names.</summary>
    public static TheoryData<string, string> EveryForm()
    {
        var data = new TheoryData<string, string>();
        foreach (var column in Owned)
        {
            data.Add(column, column);
            data.Add($" {column.ToUpperInvariant()} ", column);
            data.Add($"{column}@odata.bind", column);
            data.Add($"_{column}_value", column);
        }

        return data;
    }

    // ── The refusal itself ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryForm))]
    public void AnOwnedColumn_InAnyForm_IsRefusedOnADocument(string key, string column)
    {
        var refusal = InheritedAccessPermission.RefuseAppOnlyWrite("sprk_document", ["sprk_documentdescription", key]);

        refusal.Should().NotBeNull();
        refusal!.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentBffColumnWriteReasonCode);
        refusal.Reason.Should().StartWith(column + " ");
    }

    [Theory]
    [InlineData("sprk_filepathnote")]
    [InlineData("sprk_graphitemidx")]
    [InlineData("sprk_driveitemidx")]
    [InlineData("sprk_graphitemid_old")]
    [InlineData("sprk_graphdriveidname")]
    [InlineData("sprk_relocationpendingnote")]
    [InlineData("sprk_relocatedversionscount")]
    [InlineData("sprk_emailmessageids")]
    [InlineData("sprk_isemailarchived")]
    [InlineData("xsprk_filepath")]
    [InlineData("sprk_parentgraphitemid")]
    [InlineData("sprk_documentdescription")]
    public void ANeighbouringName_IsNotRefusedOnADocument(string key)
    {
        InheritedAccessPermission.RefuseAppOnlyWrite("sprk_document", [key]).Should().BeNull();
    }

    [Theory]
    [InlineData("sprk_todo")]
    [InlineData("sprk_event")]
    [InlineData("sprk_communication")]
    [InlineData("sprk_matter")]
    // #2253 fix round 1 (F1-a): a null table is no longer "another table"; it is refused as no table name at all
    // (AccessTableAppOnlyWriterTests.ATableThatIsNotAnIdentifier_IsRefusedFirst_ForEveryIdentity).
    public void EveryOwnedColumn_IsNotRefusedOnAnotherTable(string? table)
    {
        InheritedAccessPermission.RefuseAppOnlyWrite(table, Owned).Should().BeNull();
    }

    [Fact]
    public void AnAttachmentsFilePointer_IsRefusedAsAnAccessColumn_NotAsADocumentColumn()
    {
        // #2253 fix round 3 (K): sprk_communicationattachment's sprk_graphdriveid / sprk_graphitemid name the bytes the row
        // points at, so they are refused per column on that table (AccessColumns); its other columns are not.
        InheritedAccessPermission.RefuseAppOnlyWrite("sprk_communicationattachment", ["sprk_graphitemid"])?.RefusalCode
            .Should().Be(InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        InheritedAccessPermission.RefuseAppOnlyWrite("sprk_communicationattachment", ["sprk_filepath"]).Should().BeNull();
    }

    [Fact]
    public void TheOwnedSet_IsExactlyTheseColumns()
    {
        InheritedAccessPermission.BffOwnedDocumentColumns.Should().BeEquivalentTo(Owned);
    }

    [Fact]
    public void TheOwnedSet_HoldsEveryColumnTheFieldSecurityScriptListsForTheBff()
    {
        // The DocumentPointers target of scripts/Set-DocumentPointerFieldSecurity.ps1 (the BFF-only writers' profile), read
        // from the script itself: a column added there and not here fails this test.
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "Set-DocumentPointerFieldSecurity.ps1"));
        var listed = DocumentPointerColumns(script);

        listed.Should().HaveCountGreaterThanOrEqualTo(5, "the DocumentPointers target's column list is parsed, not missed");
        listed.Where(c => !InheritedAccessPermission.BffOwnedDocumentColumns.Contains(c))
            .Should().BeEmpty("every column the script locks for the BFF is one the app-only refusal refuses");
    }

    [Fact]
    public void TheScriptCheck_FiresOnAColumnTheOwnedSetLacks()
    {
        // Must-fire control for the test above: the script's list with one column the set does not hold.
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "Set-DocumentPointerFieldSecurity.ps1"));
        var widened = Regex.Replace(
            script, @"(DocumentPointers\s*=\s*@\{[^\r\n]*?Columns\s*=\s*@\()", "$1'sprk_notbffowned', ", RegexOptions.Multiline);

        DocumentPointerColumns(widened).Where(c => !InheritedAccessPermission.BffOwnedDocumentColumns.Contains(c))
            .Should().Equal("sprk_notbffowned");
    }

    /// <summary>The columns of the script's DocumentPointers target (<c>DocumentPointers = @{ Table = ...; Columns = @(...) }</c>).</summary>
    private static IReadOnlyList<string> DocumentPointerColumns(string script)
    {
        var target = Regex.Match(
            script, @"^\s*DocumentPointers\s*=\s*@\{\s*Table\s*=\s*'([^']+)'\s*;\s*Columns\s*=\s*@\(([^)]*)\)", RegexOptions.Multiline);
        if (!target.Success)
            return [];

        target.Groups[1].Value.Should().Be("sprk_document");
        return Regex.Matches(target.Groups[2].Value, @"'([^']+)'").Select(m => m.Groups[1].Value).ToArray();
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void AnAccessColumnOrALinkBesideAnOwnedColumn_KeepsItsOwnCode()
    {
        InheritedAccessPermission.RefuseAppOnlyWrite("sprk_document", ["sprk_graphitemidbound", "sprk_accesspermission"])
            .Should().Match<RecordOwnerResolution>(r => r.RefusalCode == InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        InheritedAccessPermission.RefuseAppOnlyWrite("sprk_document", ["sprk_graphitemidbound", "sprk_parentdocument"])
            .Should().Match<RecordOwnerResolution>(r => r.RefusalCode == InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
    }

    // ── Update Record node / action seam (app-only) ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryForm))]
    public async Task APlaybookUpdate_NamingAnOwnedColumn_IsRefused_AndNothingIsWritten(string key, string column)
    {
        _ = column;
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_documentdescription"] = "x", [key] = "01FORGEDITEM" },
                Lookups: null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentBffColumnWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Fact]
    public async Task APlaybookUpdate_ForgingThePointerAndItsBinding_IsRefused_AndNothingIsWritten()
    {
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?>
                {
                    ["sprk_graphitemid"] = "01FORGEDITEM",
                    ["sprk_graphitemidbound"] = "01FORGEDITEM",
                },
                Lookups: null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentBffColumnWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Fact]
    public async Task AnImpersonatedUpdate_OfAnOwnedColumn_IsNotAskedTheAppOnlyRefusal()
    {
        // A user's own write runs as that user, so Dataverse's field security decides it (the refusal is for app-only writers).
        var user = Guid.Parse("22400000-0000-0000-0000-00000000e4ff");
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_document", Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_filepath"] = "https://example/x.pdf" },
                Lookups: null, ImpersonateSystemUserId: user, PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_document", Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_filepath")),
            It.IsAny<CancellationToken>(), user), Times.Once);
    }

    [Theory]
    [InlineData("sprk_todo", "sprk_graphitemidbound")]
    [InlineData("sprk_communication", "sprk_emailmessageid")]
    [InlineData("sprk_event", "sprk_filepath")]
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
    [MemberData(nameof(EveryForm))]
    public async Task AnOutputMapping_NamingAnOwnedColumn_IsRefused_AndNothingIsWritten(string key, string column)
    {
        _ = column;
        var act = () => Handler().UpdateAsync(
            "sprk_document", Row, new Dictionary<string, object?> { ["sprk_documentdescription"] = "x", [key] = "01FORGEDITEM" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.AppOnlyDocumentBffColumnWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Fact]
    public async Task AnOutputMapping_OfADocumentsOtherColumns_IsWritten()
    {
        await Handler().UpdateAsync(
            "sprk_document", Row,
            new Dictionary<string, object?> { ["sprk_documentdescription"] = "x", ["sprk_filepathnote"] = "y" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Verify(w => w.UpdateRecordFieldsAsync(
            "sprk_document", Row,
            It.Is<Dictionary<string, object?>>(f => f.ContainsKey("sprk_documentdescription") && f.ContainsKey("sprk_filepathnote")),
            It.IsAny<CancellationToken>(), null), Times.Once);
    }

    // ── Field-mapping push (a rule targeting an owned column is skipped; the rest of the push applies) ───────────

    [Theory]
    [InlineData("sprk_graphitemidbound")]
    [InlineData("sprk_filepath")]
    [InlineData("SPRK_EmailMessageId")]
    [InlineData("sprk_isemailarchive")]
    public async Task APushRule_TargetingAnOwnedColumn_IsSkipped_AndTheOtherColumnsArePushed(string target)
    {
        var (updated, failed, skipped, errors, results) = await Push(
            "sprk_document",
            Rule("sprk_name", "sprk_documentdescription"),
            Rule("sprk_name", target));

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

    private static FieldMappingRuleDto Rule(string source, string target) =>
        new() { SourceField = source, TargetField = target, SourceFieldType = "Text", TargetFieldType = "Text" };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string table, params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            _writes.Object,
            new StampWorld().Restamper,
            rules,
            new Dictionary<string, object?> { ["sprk_name"] = "Matter 2240" },
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
