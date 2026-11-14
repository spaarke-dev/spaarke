using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// #2799 (unified-access-control-r2), chunk 3, C1-C3: the three generic update paths — the playbook update, the output
/// orchestrator and the field-mapping push — write a LOCKED invoice-family column (<see cref="InvoiceFamilyLockedWrite.LockedColumns"/>)
/// on a person's run through <see cref="InvoiceFamilyLockedWrite.UpdateExistingSplitAsync"/>: checked AS THAT PERSON (Write and
/// Append on the row, AppendTo on the record it names) and written AS THE APPLICATION, the other columns still as the person.
/// Must fire: a person without Write, or without AppendTo on the record named, is refused and nothing is written; an unanswered
/// question is refused and nothing is written; an unknown record answers exactly as one they may not use; the application's write
/// failing after the person's never answers success. Must not fire: an allowed write lands with its locked columns written by the
/// application; a write with no locked column, and a run with no person, keep their single write.
/// </summary>
public partial class InvoiceFamilyGenericUpdatePathTests
{
    private const AccessRights AllRights =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo;

    /// <summary>#2799: the person's rights on a record, by entity set and id (null: Dataverse gave no answer). All, unless a test says.</summary>
    private Func<string, Guid, AccessRights?> _lockedRights = (_, _) => AllRights;

    /// <summary>#2799: every rights question the locked-column check asked: (entity set, record, the person named).</summary>
    private readonly List<(string EntitySet, Guid Id, Guid Person)> _lockedAsked = [];

    /// <summary>The rights probe the three paths ask the locked-column check through (app-only, naming the person).</summary>
    private CallerRecordAccessProbe LockedProbe()
    {
        var probe = new Mock<CallerRecordAccessProbe>(
            new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!);
        probe.Setup(p => p.GetPrincipalRightsAppOnlyOrUnknownAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid person, string set, Guid id, CancellationToken _) =>
            {
                _lockedAsked.Add((set, id, person));
                return Task.FromResult(_lockedRights(set, id));
            });
        return probe.Object;
    }

    /// <summary>
    /// #2799 review 2 (R2-1): the person's <c>systemuser</c> row as the app-only state read answers it — enabled, Read-Write
    /// unless a test says. <see cref="_personStateReads"/> journals the id each read named.
    /// </summary>
    private Func<Task<Dictionary<string, object?>>> _personState =
        () => Task.FromResult(new Dictionary<string, object?> { ["isdisabled"] = false, ["accessmode"] = 0L });

    private readonly List<Guid> _personStateReads = [];

    public InvoiceFamilyGenericUpdatePathTests()
    {
        _writes.Setup(w => w.RetrieveRecordFieldsAsync("systemuser", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, string[] _, CancellationToken _) =>
            {
                _personStateReads.Add(id);
                return _personState();
            });
    }

    private const string LockedTable = "sprk_invoiceapproval";

    /// <summary>The row's own core lookup, re-named to the matter it already names (the guard allows it; nothing is derived).</summary>
    private Task<PathOutcome> RunLocked(GenericPath path, IReadOnlyDictionary<string, object?> plain, Guid? playbookActsAs = null) =>
        RunAsync(path, LockedTable, FamilyRow, Lookups(("sprk_regardingmatter", MatterRef(Matter))), plain,
            FamilyRowUnderAMatter(LockedTable).Object, playbookActsAs);

    public static TheoryData<GenericPath> EveryGenericPath()
    {
        var data = new TheoryData<GenericPath>();
        foreach (var path in Enum.GetValues<GenericPath>())
            data.Add(path);
        return data;
    }

    // ── Must not fire ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryGenericPath))]
    public async Task Locked_AllowedMixedWrite_OtherColumnsAsThePerson_ThenTheLockedColumnAsTheApplication(GenericPath path)
    {
        var outcome = await RunLocked(path, RenameOnly, playbookActsAs: Caller);

        outcome.Code.Should().BeNull(outcome.Message);
        var sent = Sent().ToList();
        sent.Should().HaveCount(2);
        sent[0].As.Should().Be(Caller, "the other columns are written as the person");
        sent[1].As.Should().BeNull("the locked column is written as the application");
        LockedColumnsWrittenOnlyByTheApp(LockedTable, FamilyRow);
        SentPayload(LockedTable, FamilyRow).Keys.Should().Contain("sprk_name");
        _lockedAsked.Should().Equal(("sprk_invoiceapprovals", FamilyRow, Caller), ("sprk_matters", Matter, Caller));
    }

    [Theory]
    [MemberData(nameof(EveryGenericPath))]
    public async Task Locked_AWriteWithNoLockedColumn_KeepsThePersonsSingleWrite_NothingAsked(GenericPath path)
    {
        var outcome = await RunAsync(path, LockedTable, FamilyRow, NoLookups, RenameOnly,
            FamilyRowUnderAMatter(LockedTable).Object, playbookActsAs: Caller);

        outcome.Code.Should().BeNull(outcome.Message);
        Sent().Should().ContainSingle().Which.As.Should().Be(Caller);
        _lockedAsked.Should().BeEmpty();
    }

    [Theory]
    [InlineData(GenericPath.PlaybookUpdate)]
    [InlineData(GenericPath.OutputOrchestrator)]
    public async Task Locked_ARunWithNoPerson_KeepsItsSingleApplicationWrite_NothingAsked(GenericPath path)
    {
        var outcome = await RunLocked(path, RenameOnly, playbookActsAs: null);

        outcome.Code.Should().BeNull(outcome.Message);
        Sent().Should().ContainSingle().Which.As.Should().BeNull();
        _lockedAsked.Should().BeEmpty();
    }

    // ── Must fire ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryGenericPath))]
    public async Task Locked_APersonWithoutWriteOnTheRow_IsRefused_NothingWritten(GenericPath path)
    {
        _lockedRights = (set, _) => set == "sprk_invoiceapprovals" ? AccessRights.Read | AccessRights.Append : AllRights;

        var outcome = await RunLocked(path, RenameOnly, playbookActsAs: Caller);

        outcome.Code.Should().Be(InvoiceFamilyLockedWrite.LockedWriteDeniedCode);
        NoGuid(outcome);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(EveryGenericPath))]
    public async Task Locked_APersonWithoutAppendToOnTheCoreRecord_IsRefused_NothingWritten(GenericPath path)
    {
        _lockedRights = (set, _) => set == "sprk_matters" ? AccessRights.Read : AllRights;

        var outcome = await RunLocked(path, RenameOnly, playbookActsAs: Caller);

        outcome.Code.Should().Be(InvoiceFamilyLockedWrite.LockedWriteDeniedCode);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(EveryGenericPath))]
    public async Task Locked_AnUnansweredQuestion_IsReadFailed_NothingWritten(GenericPath path)
    {
        _lockedRights = (set, _) => set == "sprk_matters" ? null : AllRights;

        var outcome = await RunLocked(path, RenameOnly, playbookActsAs: Caller);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(EveryGenericPath))]
    public async Task Locked_AnUnknownCoreRecord_AnswersExactlyAsOneThePersonMayNotUse(GenericPath path)
    {
        _lockedRights = (set, _) => set == "sprk_matters" ? AccessRights.None : AllRights; // RetrievePrincipalAccess's 404
        var unknown = await RunLocked(path, RenameOnly, playbookActsAs: Caller);
        _lockedRights = (set, _) => set == "sprk_matters" ? AccessRights.Read : AllRights;   // exists, no AppendTo
        var forbidden = await RunLocked(path, RenameOnly, playbookActsAs: Caller);

        unknown.Should().Be(forbidden);
        unknown.Code.Should().Be(InvoiceFamilyLockedWrite.LockedWriteDeniedCode);
        NothingWritten();
    }

    // ── Review 2 (R2-1): the person's own state ──────────────────────────────────────────────────────────────────────────

    public static TheoryData<GenericPath, bool, long> EveryGenericPathWithAStateThatDoesNotWrite()
    {
        var data = new TheoryData<GenericPath, bool, long>();
        foreach (var path in Enum.GetValues<GenericPath>())
        {
            data.Add(path, true, 0L);  // disabled
            data.Add(path, false, 2L); // Read
            data.Add(path, false, 1L); // Administrative
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryGenericPathWithAStateThatDoesNotWrite))]
    public async Task Locked_APersonWhoseStateDoesNotWrite_IsRefused_NothingWrittenOrAsked(GenericPath path, bool disabled, long accessMode)
    {
        _personState = () => Task.FromResult(new Dictionary<string, object?> { ["isdisabled"] = disabled, ["accessmode"] = accessMode });

        var outcome = await RunLocked(path, RenameOnly, playbookActsAs: Caller);

        outcome.Code.Should().Be(InvoiceFamilyLockedWrite.LockedWriteDeniedCode);
        NoGuid(outcome);
        NothingWritten();
        _lockedAsked.Should().BeEmpty("the person's state is asked before any rights question");
        _personStateReads.Should().Equal(Caller);
    }

    [Theory]
    [MemberData(nameof(EveryGenericPath))]
    public async Task Locked_ANonInteractivePerson_IsAllowed_StateAskedOnce(GenericPath path)
    {
        _personState = () => Task.FromResult(new Dictionary<string, object?> { ["isdisabled"] = false, ["accessmode"] = 4L });

        var outcome = await RunLocked(path, RenameOnly, playbookActsAs: Caller);

        outcome.Code.Should().BeNull(outcome.Message);
        LockedColumnsWrittenOnlyByTheApp(LockedTable, FamilyRow);
        _personStateReads.Should().Equal(Caller);
    }

    [Theory]
    [MemberData(nameof(EveryGenericPath))]
    public async Task Locked_APersonWithNoSystemUserRow_AnswersExactlyAsAnUnknownCoreRecord(GenericPath path)
    {
        _personState = () => Task.FromResult(new Dictionary<string, object?>()); // the app-only read's 404: no columns
        var missing = await RunLocked(path, RenameOnly, playbookActsAs: Caller);
        _personState = () => Task.FromResult(new Dictionary<string, object?> { ["isdisabled"] = false, ["accessmode"] = 0L });
        _lockedRights = (set, _) => set == "sprk_matters" ? AccessRights.None : AllRights; // RetrievePrincipalAccess's 404
        var unknown = await RunLocked(path, RenameOnly, playbookActsAs: Caller);

        missing.Should().Be(unknown);
        missing.Code.Should().Be(InvoiceFamilyLockedWrite.LockedWriteDeniedCode);
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(EveryGenericPath))]
    public async Task Locked_ThePersonsStateCannotBeRead_IsReadFailed_NothingWritten(GenericPath path)
    {
        _personState = () => Task.FromException<Dictionary<string, object?>>(new HttpRequestException("503 at 10.0.0.9"));

        var outcome = await RunLocked(path, RenameOnly, playbookActsAs: Caller);

        outcome.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        (outcome.Message ?? string.Empty).Should().NotContain("10.0.0.9");
        NothingWritten();
    }

    [Theory]
    [MemberData(nameof(EveryGenericPath))]
    public async Task Locked_TheApplicationsWriteFailsAfterThePersons_IsPartlySaved_NeverSuccess(GenericPath path)
    {
        _writes.Setup(w => w.UpdateExistingRecordFieldsAsync(LockedTable, FamilyRow,
                It.IsAny<Dictionary<string, object?>>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 503 at 10.0.0.9"));

        var outcome = await RunLocked(path, RenameOnly, playbookActsAs: Caller);

        outcome.Code.Should().Be(InvoiceFamilyLockedWrite.LockedWritePartlySavedCode);
        outcome.Message.Should().Be(InvoiceFamilyLockedWrite.PartlySavedMessage).And.NotContain("503").And.NotContain("10.0.0.9");
        Sent().Should().HaveCount(2, "the person's write landed; the application's was sent and failed");
    }
}
