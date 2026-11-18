using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ChildAccessPermission;

/// <summary>
/// fm task 054b: a communication's participants (<c>sprk_sentby</c>, <c>sprk_directrecipient</c>) and thread lookup
/// (<c>sprk_communicationthread</c>), and its thread's type and default flag (<c>sprk_threadtype</c>,
/// <c>sprk_isdefaultthread</c>), are written only by the BFF's own typed writers
/// (<see cref="InheritedAccessPermission.BffOwnedCommunicationColumns"/>): they decide who may read a message. Every generic
/// writer refuses a payload naming one, whatever its identity: <see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>
/// (app-only and caller), the one update-side check <see cref="RecordTableRules.RefuseFixedAfterCreateColumns"/> (every
/// update path, a confirmed proposal written as its user included), the create service's caller column policy
/// (<see cref="RecordCreationService.CallerBlockedColumnRefusal"/>, except the "for" person the server stamps itself), and the
/// field-mapping push (the rule is skipped). <c>scripts/Set-ThreadFlagFieldSecurity.ps1</c> secures the same five columns.
/// Must fire: each column, in its plain, case-variant, <c>@odata.bind</c> and <c>_{column}_value</c> forms. Must not fire:
/// neighbouring names, the same names on other tables, a server-stamped "for" person, and the typed thread-filing writer.
/// </summary>
public class CommunicationBffOwnedColumnRefusalTests
{
    private static readonly Guid Row = Guid.Parse("054b0000-0000-4000-8000-000000000001");
    private static readonly Guid Caller = Guid.Parse("054b0000-0000-4000-8000-0000000000ca");
    private static readonly Guid Thread = Guid.Parse("054b0000-0000-4000-8000-0000000000e1");

    private static readonly (string Table, string Column)[] Owned =
    [
        ("sprk_communicationthread", "sprk_threadtype"),
        ("sprk_communicationthread", "sprk_isdefaultthread"),
        ("sprk_communication", "sprk_sentby"),
        ("sprk_communication", "sprk_directrecipient"),
        ("sprk_communication", "sprk_communicationthread"),
    ];

    private readonly Mock<IFieldMappingDataverseService> _writes = new();

    /// <summary>Each owned column in each form a payload key can take, with its table and the column it names.</summary>
    public static TheoryData<string, string, string> EveryForm()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (table, column) in Owned)
        {
            data.Add(table, column, column);
            data.Add(table, $" {column.ToUpperInvariant()} ", column);
            data.Add(table, $"{column}@odata.bind", column);
            data.Add(table, $"_{column}_value", column);
        }

        return data;
    }

    // ── The set and the refusals themselves ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheOwnedSet_IsExactlyTheseColumns()
    {
        InheritedAccessPermission.BffOwnedCommunicationColumns.Keys.Should()
            .BeEquivalentTo(Owned.Select(o => o.Table).Distinct());
        foreach (var group in Owned.GroupBy(o => o.Table))
        {
            InheritedAccessPermission.BffOwnedCommunicationColumns[group.Key].Should()
                .BeEquivalentTo(group.Select(o => o.Column), group.Key);
        }
    }

    [Fact]
    public void TheOwnedSet_IsBuiltFromTheTypedWritersOwnConstants()
    {
        // One source of truth with the writers: a renamed writer constant drifts here.
        InheritedAccessPermission.BffOwnedCommunicationColumns["sprk_communication"].Should()
            .Contain(ThreadResolver.ThreadLookupOnCommunication)
            .And.Contain(Sprk.Bff.Api.Services.Ai.Handlers.EmailDraftToolHandler.SenderLookupColumn);
    }

    [Theory]
    [MemberData(nameof(EveryForm))]
    public void AnOwnedColumn_InAnyForm_IsRefusedForEveryIdentity(string table, string key, string column)
    {
        foreach (var identity in new[] { InheritedAccessPermission.WriteIdentity.AppOnly, InheritedAccessPermission.WriteIdentity.Caller })
        {
            var refusal = InheritedAccessPermission.RefuseAppOnlyWrite(table, ["sprk_name", key], identity);

            refusal.Should().NotBeNull($"{key} on {table} as {identity}");
            // A thread's type is also an app-only access column (#2253), which answers first for the app-only identity.
            refusal!.RefusalCode.Should().Be(
                identity == InheritedAccessPermission.WriteIdentity.AppOnly && column == "sprk_threadtype"
                    ? InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode
                    : InheritedAccessPermission.BffOwnedCommunicationColumnReasonCode);
            refusal.Reason.Should().StartWith(column + " ");
        }
    }

    [Theory]
    [MemberData(nameof(EveryForm))]
    public void AnOwnedColumn_InAnyForm_IsRefusedByTheOneUpdateSideCheck(string table, string key, string column)
    {
        var refusal = RecordTableRules.RefuseFixedAfterCreateColumns(table, ["sprk_name", key]);

        refusal.Should().NotBeNull();
        refusal!.RefusalCode.Should().Be(InheritedAccessPermission.BffOwnedCommunicationColumnReasonCode);
        refusal.Reason.Should().StartWith(column + " ").And.Contain("set by Spaarke");
    }

    [Theory]
    [InlineData("sprk_communication", "sprk_sentbyname")]
    [InlineData("sprk_communication", "sprk_sentbyx")]
    [InlineData("sprk_communication", "xsprk_sentby")]
    [InlineData("sprk_communication", "sprk_directrecipients")]
    [InlineData("sprk_communication", "sprk_communicationthreadname")]
    [InlineData("sprk_communication", "sprk_subject")]
    [InlineData("sprk_communication", "sprk_regardingmatter")]
    [InlineData("sprk_communication", "sprk_threadtype")]
    [InlineData("sprk_communicationthread", "sprk_isdefaultthreadx")]
    [InlineData("sprk_communicationthread", "sprk_name")]
    [InlineData("sprk_communicationthread", "sprk_sentby")]
    public void ANeighbouringName_OrAnotherOwnedTablesColumn_IsNotRefused(string table, string key)
    {
        InheritedAccessPermission.BffOwnedCommunicationColumnIn(table, [key]).Should().BeNull();
        RecordTableRules.RefuseFixedAfterCreateColumns(table, [key]).Should().BeNull();
        InheritedAccessPermission.RefuseAppOnlyWrite(table, [key], InheritedAccessPermission.WriteIdentity.Caller).Should().BeNull();
    }

    [Theory]
    [InlineData("sprk_todo")]
    [InlineData("sprk_event")]
    [InlineData("sprk_document")]
    [InlineData("sprk_matter")]
    public void EveryOwnedColumn_IsNotRefusedOnAnotherTable(string table)
    {
        var columns = Owned.Select(o => o.Column).ToArray();
        InheritedAccessPermission.BffOwnedCommunicationColumnIn(table, columns).Should().BeNull();
        RecordTableRules.RefuseFixedAfterCreateColumns(table, columns).Should().BeNull();
        InheritedAccessPermission.RefuseAppOnlyWrite(table, columns, InheritedAccessPermission.WriteIdentity.Caller).Should().BeNull();
    }

    // ── The create service's caller column policy ─────────────────────────────────────────────────────────────────

    /// <summary><see cref="EveryForm"/> on the communication: the thread has no create rule (the service never creates one).</summary>
    public static TheoryData<string, string, string> EveryCommunicationForm()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var row in EveryForm().Where(r => (string)r[0] == "sprk_communication"))
            data.Add((string)row[0], (string)row[1], (string)row[2]);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryCommunicationForm))]
    public void ACallersCreatePayload_NamingAnOwnedColumn_IsRefused(string table, string key, string column)
    {
        RecordTableRules.Find("sprk_communicationthread").Should().BeNull("the create service never creates a thread");
        var refusal = RecordCreationService.CallerBlockedColumnRefusal(RuleFor(table), "record", ["sprk_name", key]);

        refusal.Should().NotBeNull();
        refusal!.Code.Should().Be(RecordCreationRefusal.BlockedColumnCode);
        refusal.Detail.Should().Contain($"'{column}'");
    }

    [Fact]
    public void TheForPersonTheServerStampsItself_IsNotRefused_ButTheOtherOwnedColumnsStillAre()
    {
        var rule = RuleFor("sprk_communication");
        var serverSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_sentby" };

        RecordCreationService.CallerBlockedColumnRefusal(rule, "communication", ["sprk_subject", "sprk_SentBy@odata.bind"], serverSet)
            .Should().BeNull("the server stamped the 'for' person itself");
        RecordCreationService.CallerBlockedColumnRefusal(rule, "communication", ["sprk_sentby"], serverSet).Should().BeNull();
        RecordCreationService.CallerBlockedColumnRefusal(
                rule, "communication", ["sprk_sentby", "sprk_DirectRecipient@odata.bind"], serverSet)!
            .Detail.Should().Contain("'sprk_directrecipient'");
        RecordCreationService.CallerBlockedColumnRefusal(rule, "communication", ["sprk_subject"]).Should().BeNull();
    }

    // ── Update Record node / action seam: every payload source and identity ───────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryForm))]
    public async Task APlaybookUpdate_NamingAnOwnedColumn_IsRefused_AndNothingIsWritten(string table, string key, string column)
    {
        _ = column;
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "x", [key] = Thread.ToString("D") },
                Lookups: null),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().BeOneOf(
            InheritedAccessPermission.BffOwnedCommunicationColumnReasonCode,
            InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Theory]
    [MemberData(nameof(EveryForm))]
    public async Task AConfirmedProposalWrittenAsItsUser_NamingAnOwnedColumn_IsRefused_AndNothingIsWritten(
        string table, string key, string column)
    {
        _ = column;
        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_name"] = "x", [key] = Thread.ToString("D") },
                Lookups: null, ImpersonateSystemUserId: Caller, PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>())
            .Which.RefusalCode.Should().Be(InheritedAccessPermission.BffOwnedCommunicationColumnReasonCode,
                "the impersonated write inherits the refusal from the one update-side check");
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    [Theory]
    [InlineData("sprk_communication", "sprk_subject")]
    [InlineData("sprk_communicationthread", "sprk_name")]
    public async Task AConfirmedProposalWrittenAsItsUser_OfAnotherColumn_IsWritten(string table, string column)
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                table, Row, FieldMappings: null, LegacyFields: new Dictionary<string, string?> { [column] = "x" },
                Lookups: null, ImpersonateSystemUserId: Caller, PayloadSource: UpdateRecordPayloadSource.ConfirmedProposal),
            CancellationToken.None);

        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.ContainsKey(column)), Caller, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Output mapping (DataverseUpdateHandler, app-only) ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryForm))]
    public async Task AnOutputMapping_NamingAnOwnedColumn_IsRefused_AndNothingIsWritten(string table, string key, string column)
    {
        _ = column;
        var act = () => Handler().UpdateAsync(
            table, Row, new Dictionary<string, object?> { ["sprk_name"] = "x", [key] = Thread.ToString("D") },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        (await act.Should().ThrowAsync<RecordOwnerUnresolvedException>()).Which.RefusalCode.Should().BeOneOf(
            InheritedAccessPermission.BffOwnedCommunicationColumnReasonCode,
            InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode);
        _writes.Invocations.Should().BeEmpty("nothing of the payload is written");
    }

    // ── Field-mapping push (a rule targeting an owned column is skipped; the rest of the push applies) ────────────

    [Theory]
    [InlineData("sprk_communication", "sprk_sentby")]
    [InlineData("sprk_communication", "sprk_directrecipient")]
    [InlineData("sprk_communication", "SPRK_CommunicationThread")]
    [InlineData("sprk_communicationthread", "sprk_isdefaultthread")]
    public async Task APushRule_TargetingAnOwnedColumn_IsSkipped_AndTheOtherColumnsArePushed(string table, string target)
    {
        var (updated, failed, skipped, errors, results) = await Push(
            table, Rule("sprk_name", "sprk_description"), Rule("sprk_name", target));

        (updated, failed, skipped).Should().Be((1, 0, 0));
        errors.Should().BeEmpty();
        _writes.Verify(w => w.UpdateExistingRecordFieldsAsync(
            table, Row, It.Is<Dictionary<string, object?>>(f => f.Count == 1 && f.ContainsKey("sprk_description")),
            Caller, It.IsAny<CancellationToken>()), Times.Once);
        results.Should().ContainSingle(r => r.TargetField == target)
            .Which.Should().Match<FieldMappingResultDto>(r =>
                r.Status == FieldMappingStatus.Skipped && r.ErrorMessage!.Contains("set by Spaarke"));
    }

    // ── The typed writers are not generic writers (must not fire) ─────────────────────────────────────────────────

    [Fact]
    public async Task TheTypedThreadWriters_StillCreateTheThreadsTypeAndFileTheMessage_ThroughTheResolver_AppOnly()
    {
        // The real typed path: a message the channel strategy answers CreateNew for, with no regarding, gets a new Direct thread
        // (its type written on create) and is filed into it (its thread lookup written by the one filing write). The same
        // payloads, sent to a generic writer, are refused — so this proves the typed writers do not go through the refusal.
        var entities = new Mock<IGenericEntityService>();
        entities
            .Setup(e => e.RetrieveAsync("sprk_communication", Row, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication") { Id = Row });
        var created = new List<Entity>();
        entities
            .Setup(e => e.CreateAsync(It.Is<Entity>(x => x.LogicalName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((x, _) => created.Add(x))
            .ReturnsAsync(Thread);
        var filings = new List<Dictionary<string, object>>();
        entities
            .Setup(e => e.UpdateAsync("sprk_communication", Row, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => filings.Add(f))
            .Returns(Task.CompletedTask);
        var resolver = new ThreadResolver(
            new Sprk.Bff.Api.Services.Communication.Threads.IThreadKeyStrategy[] { new CreateNewStrategy() }, entities.Object, new RecordOwnershipResolverDouble(),
            NullLogger<ThreadResolver>.Instance);

        var threadId = await resolver.ResolveAndAssignThreadAsync(
            new Sprk.Bff.Api.Services.Communication.ThreadResolutionRequest
            {
                CommunicationId = Row,
                ChannelType = Sprk.Bff.Api.Services.Communication.Models.CommunicationType.Message,
                Direction = Sprk.Bff.Api.Services.Communication.Models.CommunicationDirection.Outgoing,
                Message = new Sprk.Bff.Api.Services.Communication.Models.NormalizedMessage
                {
                    Direction = Sprk.Bff.Api.Services.Communication.Models.CommunicationDirection.Outgoing,
                },
            },
            CancellationToken.None);

        threadId.Should().Be(Thread);
        var thread = created.Should().ContainSingle().Subject;
        thread.Contains("sprk_threadtype").Should().BeTrue("the typed create writes the thread's type");
        var filing = filings.Should().ContainSingle().Subject;
        ((EntityReference)filing[ThreadResolver.ThreadLookupOnCommunication]).Id.Should().Be(Thread);

        InheritedAccessPermission.RefuseAppOnlyWrite("sprk_communicationthread", thread.Attributes.Keys)
            .Should().NotBeNull("a generic writer refuses the thread's type");
        InheritedAccessPermission.RefuseAppOnlyWrite("sprk_communication", filing.Keys)
            .Should().NotBeNull("a generic writer refuses the message's thread lookup");
    }

    private sealed class CreateNewStrategy : Sprk.Bff.Api.Services.Communication.Threads.IThreadKeyStrategy
    {
        public Sprk.Bff.Api.Services.Communication.Models.CommunicationType SupportedType =>
            Sprk.Bff.Api.Services.Communication.Models.CommunicationType.Message;

        public Task<Sprk.Bff.Api.Services.Communication.Threads.ThreadKeyResolution> ResolveAsync(
            Sprk.Bff.Api.Services.Communication.ThreadResolutionRequest request, CancellationToken ct) =>
            Task.FromResult(Sprk.Bff.Api.Services.Communication.Threads.ThreadKeyResolution.CreateNew);

        public Task OnThreadCreatedAsync(
            Guid threadId, Sprk.Bff.Api.Services.Communication.ThreadResolutionRequest request, CancellationToken ct) =>
            Task.CompletedTask;
    }

    // ── The solution source secures the same columns (F3) ─────────────────────────────────────────────────────────

    /// <summary>The solution folder of each owned table.</summary>
    private static readonly IReadOnlyDictionary<string, string> SolutionEntityFolder = new Dictionary<string, string>
    {
        ["sprk_communication"] = "sprk_Communication",
        ["sprk_communicationthread"] = "sprk_CommunicationThread",
    };

    /// <summary>Exists only live (added by <c>scripts/Set-DirectRecipientSchema.ps1</c>); not defined in the solution source.</summary>
    private const string LiveOnlyColumn = "sprk_directrecipient";

    [Fact]
    public void TheSolutionSource_SecuresEveryOwnedColumnItDefines()
    {
        var profiles = File.ReadAllText(SolutionProfilesPath());

        foreach (var (table, column) in Owned)
        {
            var secured = SolutionSecured(File.ReadAllText(SolutionEntityPath(table)));
            if (column == LiveOnlyColumn)
            {
                // Known limit: a live-only column; Set-ThreadFlagFieldSecurity.ps1 -Verify after each SpaarkeMaster import.
                if (secured.TryGetValue(column, out var exported))
                {
                    exported.Should().BeTrue($"{table}.{column} once exported must be IsSecured=1");
                    ProfileRowGaps(profiles, table, column).Should().BeEmpty($"{table}.{column} once exported is read by every user");
                }

                continue;
            }

            secured.Should().ContainKey(column, $"{table}.{column} is defined in the solution source");
            secured[column].Should().BeTrue($"{table}.{column} is a BFF-owned column, so the solution source secures it");

            // A secured column with no reader row is hidden from every user; the solution ships both profiles' rows.
            ProfileRowGaps(profiles, table, column).Should().BeEmpty(
                $"{table}.{column} is secured, so both BFF-managed profiles carry its row in the solution source");
        }
    }

    [Fact]
    public void TheProfileRowValues_AreThePrecedentsOnTheSameTable()
    {
        // The rows copy sprk_createdbyperson's: readers read only, writers read, create and update.
        ProfileRowGaps(File.ReadAllText(SolutionProfilesPath()), "sprk_communication", "sprk_createdbyperson").Should().BeEmpty();
    }

    [Fact]
    public void TheProfileRowCheck_FiresOnASecuredColumnWithoutReaderRows_OrWithAReaderThatMayWrite()
    {
        // Must-fire controls for the profile-row half of the solution-source test.
        var xml = File.ReadAllText(SolutionProfilesPath());
        ProfileRowGaps(xml, "sprk_communication", "sprk_sentby").Should().BeEmpty("the faithful source has both rows");

        var withoutReader = System.Xml.Linq.XDocument.Parse(xml);
        RowsOn(withoutReader, ReaderProfile, "sprk_communication", "sprk_sentby").Remove();
        ProfileRowGaps(withoutReader.ToString(), "sprk_communication", "sprk_sentby")
            .Should().ContainSingle().Which.Should().StartWith(ReaderProfile);

        var readerMayUpdate = System.Xml.Linq.XDocument.Parse(xml);
        RowsOn(readerMayUpdate, ReaderProfile, "sprk_communicationthread", "sprk_threadtype").Single().Element("CanUpdate")!.Value = "4";
        ProfileRowGaps(readerMayUpdate.ToString(), "sprk_communicationthread", "sprk_threadtype")
            .Should().ContainSingle().Which.Should().StartWith(ReaderProfile);

        var withoutWriter = System.Xml.Linq.XDocument.Parse(xml);
        RowsOn(withoutWriter, WriterProfile, "sprk_communicationthread", "sprk_isdefaultthread").Remove();
        ProfileRowGaps(withoutWriter.ToString(), "sprk_communicationthread", "sprk_isdefaultthread")
            .Should().ContainSingle().Which.Should().StartWith(WriterProfile);
    }

    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string WriterProfile = "Spaarke BFF-Managed Field Writers";

    /// <summary>The row each BFF-managed profile carries on a secured owned column (CanRead, CanCreate, CanUpdate).</summary>
    private static readonly (string Profile, string Read, string Create, string Update)[] ProfileRows =
    [
        (ReaderProfile, "4", "0", "0"),
        (WriterProfile, "4", "4", "4"),
    ];

    /// <summary>What the profiles XML lacks for <paramref name="table"/>.<paramref name="column"/>: one line per profile at fault.</summary>
    private static IReadOnlyList<string> ProfileRowGaps(string profilesXml, string table, string column)
    {
        var doc = System.Xml.Linq.XDocument.Parse(profilesXml);
        var gaps = new List<string>();
        foreach (var (profile, read, create, update) in ProfileRows)
        {
            var rows = RowsOn(doc, profile, table, column).ToArray();
            if (rows.Length != 1)
            {
                gaps.Add($"{profile}: {rows.Length} rows on {table}.{column}, expected 1");
                continue;
            }

            var actual = (Read: Value(rows[0], "CanRead"), Create: Value(rows[0], "CanCreate"), Update: Value(rows[0], "CanUpdate"));
            if (actual != (read, create, update))
                gaps.Add($"{profile}: {table}.{column} has read/create/update {actual}, expected ({read}, {create}, {update})");
        }

        return gaps;
    }

    private static IEnumerable<System.Xml.Linq.XElement> RowsOn(System.Xml.Linq.XDocument doc, string profile, string table, string column) =>
        doc.Descendants("FieldSecurityProfile")
            .Where(p => (string?)p.Attribute("name") == profile)
            .SelectMany(p => p.Descendants("FieldPermission"))
            .Where(r => string.Equals(Value(r, "EntityName"), table, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Value(r, "AttributeName"), column, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static string? Value(System.Xml.Linq.XElement row, string name) => row.Element(name)?.Value.Trim();

    private static string SolutionProfilesPath() => Path.Combine(
        RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Other", "FieldSecurityProfiles.xml");

    [Fact]
    public void TheSolutionSourceCheck_FiresOnAnOwnedColumnLeftUnsecured()
    {
        // Must-fire control for the test above: the communication's sprk_sentby with IsSecured flipped to 0.
        var xml = File.ReadAllText(SolutionEntityPath("sprk_communication"));
        var unsecured = Regex.Replace(
            xml, @"(<LogicalName>sprk_sentby</LogicalName>(?:(?!</attribute>).)*?<IsSecured>)1(</IsSecured>)", "${1}0${2}",
            RegexOptions.Singleline);

        unsecured.Should().NotBe(xml, "the control flips the column");
        SolutionSecured(unsecured)["sprk_sentby"].Should().BeFalse();
        SolutionSecured(xml)["sprk_sentby"].Should().BeTrue();
    }

    /// <summary>Each attribute of an <c>Entity.xml</c> by logical name, with whether it is <c>IsSecured=1</c>.</summary>
    private static IReadOnlyDictionary<string, bool> SolutionSecured(string entityXml) =>
        System.Xml.Linq.XDocument.Parse(entityXml).Descendants("attribute")
            .Where(a => a.Element("LogicalName") is not null)
            .GroupBy(a => a.Element("LogicalName")!.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.All(a => a.Element("IsSecured")?.Value.Trim() == "1"), StringComparer.OrdinalIgnoreCase);

    private static string SolutionEntityPath(string table) => Path.Combine(
        RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", SolutionEntityFolder[table], "Entity.xml");

    // ── The field-security script secures exactly this set ────────────────────────────────────────────────────────

    [Fact]
    public void TheFieldSecurityScript_SecuresExactlyTheOwnedSet()
    {
        var listed = ScriptTargets(File.ReadAllText(ScriptPath()));

        listed.Should().HaveCount(Owned.Length, "the script's target lines are parsed, not missed");
        listed.Should().BeEquivalentTo(Owned, "the script secures exactly the columns the BFF refuses");
    }

    [Fact]
    public void TheScriptCheck_FiresOnAColumnTheOwnedSetLacks()
    {
        // Must-fire control for the test above: the script's list with one column the set does not hold.
        var widened = Regex.Replace(
            File.ReadAllText(ScriptPath()), @"(Table\s*=\s*'sprk_communication'\s*;\s*Columns\s*=\s*@\()", "$1'sprk_notbffowned', ");

        ScriptTargets(widened).Except(Owned).Should().Equal(("sprk_communication", "sprk_notbffowned"));
    }

    /// <summary>The (table, column) pairs of the script's target lines (<c>@{ Table = '...'; Columns = @('...', ...) }</c>).</summary>
    private static IReadOnlyList<(string Table, string Column)> ScriptTargets(string script) =>
        Regex.Matches(script, @"@\{\s*Table\s*=\s*'([^']+)'\s*;\s*Columns\s*=\s*@\(([^)]*)\)\s*\}")
            .SelectMany(m => Regex.Matches(m.Groups[2].Value, @"'([^']+)'").Select(c => (m.Groups[1].Value, c.Groups[1].Value)))
            .ToArray();

    private static string ScriptPath() => Path.Combine(RepoRoot(), "scripts", "Set-ThreadFlagFieldSecurity.ps1");

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

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static RecordTableRule RuleFor(string table) =>
        RecordTableRules.Find(table) ?? throw new InvalidOperationException($"No create rule for {table}.");

    private static FieldMappingRuleDto Rule(string source, string target) =>
        new() { SourceField = source, TargetField = target, SourceFieldType = "Text", TargetFieldType = "Text" };

    private Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> Push(
        string table, params FieldMappingRuleDto[] rules) =>
        FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping.PermissivePushSourceGate.AllowingEveryChild(),
            _writes.Object,
            new StampWorld().Restamper,
            rules,
            new Dictionary<string, object?> { ["sprk_name"] = "Thread 054b" },
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
