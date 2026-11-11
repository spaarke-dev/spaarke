using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 198 Parts C/D, strengthened for the <c>mutation-nightly</c> quality alert (#1821): what
/// <see cref="RestrictedFilingShareBar"/> REPORTS and DOES on each of its paths, called directly over the same in-memory
/// Dataverse (<see cref="SecureChildShareWorld"/>), share table (<see cref="FakeRecordShareTable"/>) and user-flag read
/// (<see cref="UserFlagDirectoryDouble"/>) the re-file and job tests use. <see cref="RestrictedFilingShareBarTests"/> drives the
/// bar through its callers and asserts the share state; these tests pin the contract the callers act on:
/// <list type="bullet">
/// <item>the <see cref="RestrictedFilingShareBar.Outcome"/> (Restricted / not / undecided, what was removed, whether it is
/// complete — an incomplete outcome is what makes the job carry the message and try again);</item>
/// <item>the fail-closed paths: an undecidable target, a missing seam, a read fault, an unread user, a capped read, a revoke that
/// faults or is not confirmed — each removes or grants nothing it cannot justify, reports incomplete and logs the record;</item>
/// <item>"never throws (cancellation aside)": a fault, and a timeout that is not the caller's cancellation, are contained; the
/// caller's own cancellation propagates.</item>
/// </list>
/// The log assertions are on the structured properties of the fail-closed logs: an operator learns that a share was left in
/// place only from them.
/// </summary>
public class RestrictedFilingShareBarOutcomeTests
{
    private const int Read = 1;

    private static readonly Guid External = Guid.Parse("e0001821-0000-4000-8000-0000000000e1");
    private static readonly Guid Internal = Guid.Parse("e0001821-0000-4000-8000-0000000000a1");
    private static readonly Guid BlankFlag = Guid.Parse("e0001821-0000-4000-8000-0000000000b1");
    private static readonly Guid SecondInternal = Guid.Parse("e0001821-0000-4000-8000-0000000000a2");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly Mock<IGenericEntityService> _entities;
    private readonly FakeRecordShareTable _shares = new();
    private readonly UserFlagDirectoryDouble _users = new();
    private readonly CapturingLogger _log = new();

    public RestrictedFilingShareBarOutcomeTests()
    {
        _entities = SecureChildShareWorld.EntitiesOver(() => _world);
        _users.Flags[External] = true;
        _users.Flags[Internal] = false;
        _users.Flags[SecondInternal] = false;
        _users.Flags[BlankFlag] = null;
    }

    /// <summary>The bar as its callers compose it; each seam can be withheld as a caller without it does.</summary>
    private RestrictedFilingShareBar Bar(bool probe = true, bool shares = true, bool webApi = true) =>
        new(_entities.Object, probe ? SecureChildShareWorld.ColumnProbe() : null, shares ? _shares : null,
            webApi ? _users : null, _log);

    // ── Arrange helpers ──

    private Guid Matter(int accessPermission)
    {
        var matter = Guid.NewGuid();
        _world.OrdinaryRoot("sprk_matter", matter);
        _world.Set("sprk_matter", matter, InheritedAccessPermission.Column, new OptionSetValue(accessPermission));
        return matter;
    }

    private static (string Column, string Target, Guid Id) FiledUnder(Guid matter) => ("sprk_regardingmatter", "sprk_matter", matter);

    /// <summary>A message filed through <paramref name="filing"/> with one row of each content table.</summary>
    private (Guid Message, IReadOnlyList<(string Table, Guid Id)> Rows) Message(params (string Column, string Target, Guid Id)[] filing)
    {
        var message = Guid.NewGuid();
        _world.OrdinaryChild("sprk_communication", message, filing);
        var rows = new List<(string Table, Guid Id)> { ("sprk_communication", message) };
        foreach (var (table, lookup) in RestrictedFilingShareBar.ContentRows)
            rows.Add((table, ContentRow(table, lookup, message)));
        return (message, rows);
    }

    private Guid ContentRow(string table, string lookup, Guid message)
    {
        var id = Guid.NewGuid();
        _world.OrdinaryChild(table, id, (lookup, "sprk_communication", message));
        return id;
    }

    private void ShareWith(IEnumerable<(string Table, Guid Id)> rows, params Guid[] users)
    {
        foreach (var (table, id) in rows)
            foreach (var user in users)
                _shares.Seed(table, id, DataversePrincipalRef.User(user), Read);
    }

    private bool Holds(string table, Guid id, Guid user) => _shares.MaskOf(table, id, DataversePrincipalRef.User(user)) is > 0;

    private int RevokeCount => _shares.WriteLog.Count(w => w.Action == "RevokeAccess");

    private static IReadOnlyList<(string, Guid, Guid)> RemovalsOf(IEnumerable<(string Table, Guid Id)> rows, Guid user) =>
        rows.Select(r => (r.Table, r.Id, user)).ToList();

    // ── Faults: an error, a timeout that is not the caller's cancellation, the caller's own cancellation ──

    public enum FaultKind
    {
        /// <summary>A Dataverse / HTTP fault.</summary>
        Error,

        /// <summary>An HttpClient timeout: a <see cref="TaskCanceledException"/> while the caller's token is NOT cancelled.</summary>
        TimeoutWithoutCallerCancellation,

        /// <summary>The caller cancelled: its token is cancelled and the operation throws <see cref="OperationCanceledException"/>.</summary>
        CallerCancelled,
    }

    private static Exception FaultOf(FaultKind kind) => kind switch
    {
        FaultKind.Error => new InvalidOperationException("Test: Dataverse fault."),
        FaultKind.TimeoutWithoutCallerCancellation => new TaskCanceledException("Test: the HTTP call timed out."),
        _ => new OperationCanceledException("Test: the caller cancelled."),
    };

    private static CancellationToken TokenFor(FaultKind kind) =>
        kind == FaultKind.CallerCancelled ? new CancellationToken(canceled: true) : CancellationToken.None;

    // ════════════════════════ AfterFilingAsync: whether the target is Restricted ════════════════════════

    [Fact]
    public async Task AfterFiling_WhenFiledUnderNothing_ReportsNotRestrictedAndComplete_AndKeepsEveryShare()
    {
        var (message, rows) = Message();
        ShareWith(rows, External);

        var outcome = await Bar().AfterFilingAsync(message, CancellationToken.None);

        outcome.TargetRestricted.Should().BeFalse("a message filed under nothing has no target, so no bar");
        outcome.Complete.Should().BeTrue();
        outcome.Removed.Should().BeEmpty();
        rows.Should().OnlyContain(r => Holds(r.Table, r.Id, External));
        _users.Queries.Should().BeEmpty("no user is read when nothing can be barred");
    }

    [Fact]
    public async Task AfterFiling_WhenFiledUnderAStandardRecord_ReportsNotRestrictedAndComplete_AndKeepsEveryShare()
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));
        ShareWith(rows, External);

        var outcome = await Bar().AfterFilingAsync(message, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new { TargetRestricted = (bool?)false, Removed = Array.Empty<object>(), Complete = true });
        RevokeCount.Should().Be(0);
    }

    [Fact]
    public async Task AfterFiling_WhenOwnValueIsRestrictedButTheLiveFilingIsStandard_DecidesFromTheFiling()
    {
        // After a re-file the stored value may still be the OLD target's; the after-filing bar decides from the live filing.
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));
        _world.Set("sprk_communication", message, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted));
        ShareWith(rows, External);

        var outcome = await Bar().AfterFilingAsync(message, CancellationToken.None);

        outcome.TargetRestricted.Should().BeFalse();
        Holds("sprk_communication", message, External).Should().BeTrue();
    }

    [Fact]
    public async Task AfterFiling_WhenFiledUnderARestrictedRecord_ReportsEveryConfirmedRemoval_AndIsComplete()
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        ShareWith(rows, External, Internal);

        var outcome = await Bar().AfterFilingAsync(message, CancellationToken.None);

        outcome.TargetRestricted.Should().BeTrue();
        outcome.Complete.Should().BeTrue();
        outcome.Removed.Should().BeEquivalentTo(RemovalsOf(rows, External), "the message and each content row lose the external share");
        rows.Should().OnlyContain(r => !Holds(r.Table, r.Id, External) && Holds(r.Table, r.Id, Internal));
        _log.Single(LogLevel.Information, message, ("Removed", 5), ("Complete", true));
    }

    [Fact]
    public async Task AfterFiling_WithoutAColumnProbe_IsUndecided_RemovesNothing_AndWarns()
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        ShareWith(rows, External);

        var outcome = await Bar(probe: false).AfterFilingAsync(message, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new { TargetRestricted = (bool?)null, Removed = Array.Empty<object>(), Complete = false });
        RevokeCount.Should().Be(0);
        _log.Single(LogLevel.Warning, message);
    }

    [Fact]
    public async Task AfterFiling_WhenTheMessageDoesNotExist_IsUndecided_AndWarnsThatItWasNotFound()
    {
        var missing = Guid.NewGuid();

        var outcome = await Bar().AfterFilingAsync(missing, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new { TargetRestricted = (bool?)null, Removed = Array.Empty<object>(), Complete = false });
        _log.Single(LogLevel.Warning, missing);
        _log.At(LogLevel.Error).Should().BeEmpty("a missing message is a decided 'not found', not a fault");
    }

    [Fact]
    public async Task AfterFiling_WhenTheRecordAboveCannotBeRead_IsUndecided_RemovesNothing_AndWarnsWithTheReason()
    {
        var matter = Matter(InheritedAccessPermission.Restricted);
        var (message, rows) = Message(FiledUnder(matter));
        ShareWith(rows, External);
        _world.FailingRowReadsOf("sprk_matter", matter).FailingIdListReadsOf("sprk_matter", matter);

        var outcome = await Bar().AfterFilingAsync(message, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new { TargetRestricted = (bool?)null, Removed = Array.Empty<object>(), Complete = false });
        RevokeCount.Should().Be(0);
        _log.Single(LogLevel.Warning, message).Properties.Should().ContainKey("Reason")
            .WhoseValue.Should().BeOfType<string>().Which.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(FaultKind.Error)]
    [InlineData(FaultKind.TimeoutWithoutCallerCancellation)]
    [InlineData(FaultKind.CallerCancelled)]
    public async Task AfterFiling_WhenTheMessageReadFaults_IsUndecidedAndLogged_ButTheCallersCancellationPropagates(FaultKind kind)
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        ShareWith(rows, External);
        _world.FailingQueriesOf("sprk_communication").QueryFault = () => FaultOf(kind);

        var act = () => Bar().AfterFilingAsync(message, TokenFor(kind));

        if (kind == FaultKind.CallerCancelled)
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
            return;
        }

        var outcome = await act();
        outcome.Should().BeEquivalentTo(new { TargetRestricted = (bool?)null, Removed = Array.Empty<object>(), Complete = false });
        RevokeCount.Should().Be(0);
        _log.Single(LogLevel.Error, message).Exception.Should().NotBeNull();
    }

    // ════════════════════════ EligibleForGrantAsync: who may be granted ════════════════════════

    [Fact]
    public async Task EligibleForGrant_WithNoRealCandidate_IsCompleteAndEmpty_AndReadsNoUser()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));

        var (eligible, complete) = await Bar().EligibleForGrantAsync(message, [Guid.Empty], CancellationToken.None);

        eligible.Should().BeEmpty();
        complete.Should().BeTrue("there was nobody to decide");
        _users.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task EligibleForGrant_WithoutAUserReader_GrantsNobody_IsIncomplete_AndWarns()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));

        var (eligible, complete) = await Bar(webApi: false).EligibleForGrantAsync(message, [Internal], CancellationToken.None);

        eligible.Should().BeEmpty();
        complete.Should().BeFalse();
        _log.Single(LogLevel.Warning, message);
    }

    [Theory]
    [InlineData(FaultKind.Error)]
    [InlineData(FaultKind.TimeoutWithoutCallerCancellation)]
    [InlineData(FaultKind.CallerCancelled)]
    public async Task EligibleForGrant_WhenTheUserReadFaults_GrantsNobody_IsIncomplete_ButTheCallersCancellationPropagates(FaultKind kind)
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));
        _users.Fault = () => FaultOf(kind);

        var act = () => Bar().EligibleForGrantAsync(message, [Internal, External], TokenFor(kind));

        if (kind == FaultKind.CallerCancelled)
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
            return;
        }

        var (eligible, complete) = await act();
        eligible.Should().BeEmpty();
        complete.Should().BeFalse();
        _log.Single(LogLevel.Error, message).Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task EligibleForGrant_WhenACandidateIsNotReturnedByTheRead_ItIsNotEligible_IsIncomplete_AndWarnsWithTheUser()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));
        var unread = Guid.NewGuid();

        var (eligible, complete) = await Bar().EligibleForGrantAsync(message, [Internal, unread], CancellationToken.None);

        eligible.Should().Equal(Internal);
        complete.Should().BeFalse();
        _log.Single(LogLevel.Warning, message, ("UserId", unread));
    }

    [Fact]
    public async Task EligibleForGrant_TheThreeArgumentOverload_DoesNotExcludeDisabledUsers()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));
        _users.Disabled[Internal] = true;

        var (eligible, complete) = await Bar().EligibleForGrantAsync(message, [Internal], CancellationToken.None);

        eligible.Should().Equal(Internal);
        complete.Should().BeTrue();
    }

    [Fact]
    public async Task EligibleForGrant_ExcludingDisabled_ADisabledUserIsSettledAndNotGranted_AndTheCountIsLogged()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));
        _users.Disabled[BlankFlag] = true; // one disabled, two enabled

        var (eligible, complete) = await Bar().EligibleForGrantAsync(
            message, [Internal, SecondInternal, BlankFlag], excludeDisabled: true, CancellationToken.None);

        eligible.Should().BeEquivalentTo(new[] { Internal, SecondInternal });
        complete.Should().BeTrue("a disabled user is a settled answer, not an unread one");
        _log.Single(LogLevel.Information, message, ("Disabled", 1));
    }

    [Fact]
    public async Task EligibleForGrant_ExcludingDisabled_WhenNobodyIsDisabled_LogsNoDisabledCount()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));

        var (eligible, _) = await Bar().EligibleForGrantAsync(message, [Internal, SecondInternal], excludeDisabled: true, CancellationToken.None);

        eligible.Should().BeEquivalentTo(new[] { Internal, SecondInternal });
        _log.At(LogLevel.Information).Should().NotContain(l => l.Properties.ContainsKey("Disabled"));
    }

    [Fact]
    public async Task EligibleForGrant_ExcludingDisabled_WhenIsDisabledCameBackBlank_TheUserIsNotGranted_IsIncomplete_AndWarns()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));
        _users.Disabled[SecondInternal] = null; // returned without isdisabled

        var (eligible, complete) = await Bar().EligibleForGrantAsync(
            message, [Internal, SecondInternal], excludeDisabled: true, CancellationToken.None);

        eligible.Should().Equal(Internal);
        complete.Should().BeFalse();
        _log.Single(LogLevel.Warning, message, ("UserId", SecondInternal));
    }

    [Fact]
    public async Task EligibleForGrant_OnlyExternalCandidates_UnderARestrictedRecord_NobodyIsEligible_CompleteAndBarredCountLogged()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));

        var (eligible, complete) = await Bar().EligibleForGrantAsync(message, [External], CancellationToken.None);

        eligible.Should().BeEmpty("a user flagged external is barred on a Restricted record");
        complete.Should().BeTrue("the target was decided: the bar is a settled answer");
        _log.Single(LogLevel.Information, message, ("Barred", 1), ("Restricted", true));
    }

    [Fact]
    public async Task EligibleForGrant_InternalAndBlankFlagCandidates_AreEligible_WithoutReadingTheMessage()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));

        var (eligible, complete) = await Bar().EligibleForGrantAsync(message, [Internal, BlankFlag], CancellationToken.None);

        eligible.Should().BeEquivalentTo(new[] { Internal, BlankFlag }, "only a user flagged external can be barred (a blank flag is internal)");
        complete.Should().BeTrue();
        _world.QueriedTables.Should().BeEmpty("the message's Access Permission is read only when an external user is a candidate");
    }

    [Fact]
    public async Task EligibleForGrant_AnExternalCandidate_UnderAStandardRecord_IsEligible_AndNothingIsLoggedAsBarred()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));

        var (eligible, complete) = await Bar().EligibleForGrantAsync(message, [External, Internal], CancellationToken.None);

        eligible.Should().BeEquivalentTo(new[] { External, Internal });
        complete.Should().BeTrue();
        _log.All.Should().NotContain(l => l.Properties.ContainsKey("Barred"));
    }

    [Fact]
    public async Task EligibleForGrant_WhenWhetherItIsRestrictedCannotBeDecided_TheExternalCandidateIsBarred_AndItIsIncomplete()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));

        var (eligible, complete) = await Bar(probe: false).EligibleForGrantAsync(message, [External, Internal], CancellationToken.None);

        eligible.Should().Equal(Internal);
        complete.Should().BeFalse("undecided is retried");
    }

    [Theory]
    [InlineData(FaultKind.Error)]
    [InlineData(FaultKind.TimeoutWithoutCallerCancellation)]
    [InlineData(FaultKind.CallerCancelled)]
    public async Task EligibleForGrant_WhenTheTargetReadFaults_TheExternalCandidateIsBarred_ButTheCallersCancellationPropagates(FaultKind kind)
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));
        _world.FailingQueriesOf("sprk_communication").QueryFault = () => FaultOf(kind);

        var act = () => Bar().EligibleForGrantAsync(message, [External, Internal], TokenFor(kind));

        if (kind == FaultKind.CallerCancelled)
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
            return;
        }

        var (eligible, complete) = await act();
        eligible.Should().Equal(Internal);
        complete.Should().BeFalse();
        _log.Single(LogLevel.Error, message).Exception.Should().NotBeNull();
    }

    // ════════════════════════ RestrictedForGrantAsync: must a grant pass the bar ════════════════════════

    [Theory]
    [InlineData(InheritedAccessPermission.Standard, null, false)]
    [InlineData(InheritedAccessPermission.Restricted, null, true)]
    [InlineData(InheritedAccessPermission.Standard, InheritedAccessPermission.Restricted, true)]
    public async Task RestrictedForGrant_IsTheOwnValueOrTheInheritedOne(int filedUnder, int? ownValue, bool expected)
    {
        var (message, _) = Message(FiledUnder(Matter(filedUnder)));
        if (ownValue is { } own)
            _world.Set("sprk_communication", message, InheritedAccessPermission.Column, new OptionSetValue(own));

        var restricted = await Bar().RestrictedForGrantAsync(message, CancellationToken.None);

        restricted.Should().Be(expected);
    }

    [Theory]
    [InlineData(FaultKind.Error)]
    [InlineData(FaultKind.TimeoutWithoutCallerCancellation)]
    [InlineData(FaultKind.CallerCancelled)]
    public async Task RestrictedForGrant_WhenTheReadFaults_IsUndecided_ButTheCallersCancellationPropagates(FaultKind kind)
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Standard)));
        _world.FailingQueriesOf("sprk_communication").QueryFault = () => FaultOf(kind);

        var act = () => Bar().RestrictedForGrantAsync(message, TokenFor(kind));

        if (kind == FaultKind.CallerCancelled)
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
            return;
        }

        (await act()).Should().BeNull("a fault is undecided, which the caller treats as 'clear the candidates'");
        _log.Single(LogLevel.Error, message).Exception.Should().NotBeNull();
    }

    // ════════════════════════ RemoveBarredSharesAsync: the removal ════════════════════════

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task RemoveBarred_WithoutTheShareSeamOrTheUserReader_ReadsAndRemovesNothing_IsIncomplete_AndWarns(bool shares, bool webApi)
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        ShareWith(rows, External);

        var outcome = await Bar(shares: shares, webApi: webApi).RemoveBarredSharesAsync(message, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new { TargetRestricted = (bool?)true, Removed = Array.Empty<object>(), Complete = false });
        rows.Should().OnlyContain(r => Holds(r.Table, r.Id, External));
        _world.QueriedTables.Should().BeEmpty("no content row is read when nothing could be removed");
        _shares.StrictReads.Should().Be(0);
        _users.Queries.Should().BeEmpty();
        _log.Single(LogLevel.Warning, message);
        _log.At(LogLevel.Error).Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveBarred_OnlyThisMessagesContentRows_AreTouched()
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        var (other, otherRows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        ShareWith(rows, External);
        ShareWith(otherRows, External);

        var outcome = await Bar().RemoveBarredSharesAsync(message, CancellationToken.None);

        outcome.Removed.Should().BeEquivalentTo(RemovalsOf(rows, External));
        otherRows.Should().OnlyContain(r => Holds(r.Table, r.Id, External), $"message {other} was not asked about");
    }

    [Theory]
    [InlineData(RestrictedFilingShareBar.MaxRowsPerTable - 1)]
    [InlineData(RestrictedFilingShareBar.MaxRowsPerTable)]
    [InlineData(RestrictedFilingShareBar.MaxRowsPerTable + 1)]
    public async Task RemoveBarred_ReadsAtMostTheCapOfOneContentTable_AndAReadThatReachesTheCapIsIncomplete(int attachments)
    {
        var (message, messageRows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        var (table, lookup) = RestrictedFilingShareBar.ContentRows[0];
        // The Message helper made one row of each content table; add the rest, so the message has `attachments` rows of it.
        var attachmentIds = messageRows.Where(r => r.Table == table).Select(r => r.Id)
            .Concat(Enumerable.Range(0, attachments - 1).Select(_ => ContentRow(table, lookup, message)))
            .ToList();
        ShareWith(attachmentIds.Select(id => (table, id)), External);

        var outcome = await Bar().RemoveBarredSharesAsync(message, CancellationToken.None);

        var reachedCap = attachments >= RestrictedFilingShareBar.MaxRowsPerTable;
        outcome.Complete.Should().Be(!reachedCap, "a read that reaches the cap may have left rows unchecked");
        var left = attachmentIds.Count(id => Holds(table, id, External));
        left.Should().Be(Math.Max(0, attachments - RestrictedFilingShareBar.MaxRowsPerTable), "rows beyond the cap are not read");
        var capWarnings = _log.At(LogLevel.Warning).Where(l => l.Has("Content", table) && l.Has("Cap", RestrictedFilingShareBar.MaxRowsPerTable));
        if (reachedCap)
            capWarnings.Should().ContainSingle().Which.Has("Id", message).Should().BeTrue();
        else
            capWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveBarred_WhenAContentReadReturnsNoCollection_ItIsTreatedAsNoRows_AndTheRestIsStillRemoved()
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        ShareWith(rows, External);
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_emailreviewlog"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityCollection)null!);

        var outcome = await Bar().RemoveBarredSharesAsync(message, CancellationToken.None);

        outcome.Complete.Should().BeTrue("no collection is no rows, not a fault");
        outcome.Removed.Should().BeEquivalentTo(RemovalsOf(rows.Where(r => r.Table != "sprk_emailreviewlog"), External));
        _log.At(LogLevel.Error).Should().BeEmpty();
    }

    [Theory]
    [InlineData(FaultKind.Error)]
    [InlineData(FaultKind.TimeoutWithoutCallerCancellation)]
    [InlineData(FaultKind.CallerCancelled)]
    public async Task RemoveBarred_WhenOneContentReadFaults_ThoseRowsAreLeft_TheRestIsRemoved_ItIsIncomplete(FaultKind kind)
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        ShareWith(rows, External);
        _world.FailingQueriesOf("sprk_emailreviewlog").QueryFault = () => FaultOf(kind);

        var act = () => Bar().RemoveBarredSharesAsync(message, TokenFor(kind));

        if (kind == FaultKind.CallerCancelled)
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
            return;
        }

        var outcome = await act();
        outcome.TargetRestricted.Should().BeTrue();
        outcome.Complete.Should().BeFalse();
        outcome.Removed.Should().BeEquivalentTo(RemovalsOf(rows.Where(r => r.Table != "sprk_emailreviewlog"), External));
        rows.Where(r => r.Table == "sprk_emailreviewlog").Should().OnlyContain(r => Holds(r.Table, r.Id, External));
        _log.Single(LogLevel.Error, message, ("Content", "sprk_emailreviewlog")).Exception.Should().NotBeNull();
    }

    [Theory]
    [InlineData(FaultKind.Error)]
    [InlineData(FaultKind.TimeoutWithoutCallerCancellation)]
    [InlineData(FaultKind.CallerCancelled)]
    public async Task RemoveBarred_WhenOneRowsShareReadFaults_ThatRowIsLeft_TheRestIsRemoved_ItIsIncomplete(FaultKind kind)
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        ShareWith(rows, External);
        var (faultyTable, faultyRow) = rows[1];
        _shares.BeforeStrictRead = (table, id) =>
        {
            if (id == faultyRow)
                throw FaultOf(kind);
        };

        var act = () => Bar().RemoveBarredSharesAsync(message, TokenFor(kind));

        if (kind == FaultKind.CallerCancelled)
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
            return;
        }

        var outcome = await act();
        outcome.Complete.Should().BeFalse();
        outcome.Removed.Should().BeEquivalentTo(RemovalsOf(rows.Where(r => r.Id != faultyRow), External));
        Holds(faultyTable, faultyRow, External).Should().BeTrue("a row whose shares cannot be read is left as it is");
        _log.Single(LogLevel.Error, message, ("RowId", faultyRow), ("Table", faultyTable)).Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveBarred_WhenNobodyHoldsAShare_IsRestrictedComplete_AndReadsNoUser()
    {
        var (message, _) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));

        var outcome = await Bar().RemoveBarredSharesAsync(message, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new { TargetRestricted = (bool?)true, Removed = Array.Empty<object>(), Complete = true });
        _users.Queries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(FaultKind.Error)]
    [InlineData(FaultKind.TimeoutWithoutCallerCancellation)]
    [InlineData(FaultKind.CallerCancelled)]
    public async Task RemoveBarred_WhenTheHoldersCannotBeRead_RemovesNothing_IsIncomplete_ButTheCallersCancellationPropagates(FaultKind kind)
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        ShareWith(rows, External);
        _users.Fault = () => FaultOf(kind);

        var act = () => Bar().RemoveBarredSharesAsync(message, TokenFor(kind));

        if (kind == FaultKind.CallerCancelled)
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
            return;
        }

        var outcome = await act();
        outcome.Should().BeEquivalentTo(new { TargetRestricted = (bool?)true, Removed = Array.Empty<object>(), Complete = false });
        RevokeCount.Should().Be(0);
        _log.Single(LogLevel.Error, message).Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveBarred_AHolderTheReadDoesNotReturn_IsLeft_ItIsIncomplete_AndWarnsWithTheUser()
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        var unread = Guid.NewGuid();
        ShareWith(rows, External, unread);

        var outcome = await Bar().RemoveBarredSharesAsync(message, CancellationToken.None);

        outcome.Complete.Should().BeFalse();
        outcome.Removed.Should().BeEquivalentTo(RemovalsOf(rows, External));
        rows.Should().OnlyContain(r => Holds(r.Table, r.Id, unread), "an unread user is never decided");
        _log.Single(LogLevel.Warning, message, ("UserId", unread));
    }

    [Fact]
    public async Task RemoveBarred_ARevokeTheReadBackDoesNotConfirm_IsNotReportedRemoved_ItIsIncomplete_AndLogged()
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        ShareWith(rows, External);
        _shares.IgnoreWrites = true; // Dataverse accepts the revoke and keeps the share

        var outcome = await Bar().RemoveBarredSharesAsync(message, CancellationToken.None);

        outcome.Complete.Should().BeFalse();
        outcome.Removed.Should().BeEmpty("only a removal the strict read-back confirms is reported");
        foreach (var (table, id) in rows)
            _log.Single(LogLevel.Error, message, ("UserId", External), ("RowId", id), ("Table", table));
    }

    [Theory]
    [InlineData(FaultKind.Error)]
    [InlineData(FaultKind.TimeoutWithoutCallerCancellation)]
    [InlineData(FaultKind.CallerCancelled)]
    public async Task RemoveBarred_WhenARevokeFaults_ItIsNotReportedRemoved_ItIsIncomplete_ButTheCallersCancellationPropagates(FaultKind kind)
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        ShareWith(rows, External);
        _shares.WriteFailure = FaultOf(kind);

        var act = () => Bar().RemoveBarredSharesAsync(message, TokenFor(kind));

        if (kind == FaultKind.CallerCancelled)
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
            return;
        }

        var outcome = await act();
        outcome.TargetRestricted.Should().BeTrue();
        outcome.Complete.Should().BeFalse();
        outcome.Removed.Should().BeEmpty();
        foreach (var (table, id) in rows)
            _log.Single(LogLevel.Error, message, ("UserId", External), ("RowId", id), ("Table", table)).Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveBarred_ATeamShareAndAZeroMaskUserShare_AreNotUserHolders()
    {
        var (message, rows) = Message(FiledUnder(Matter(InheritedAccessPermission.Restricted)));
        var team = Guid.NewGuid();
        _shares.Seed("sprk_communication", message, DataversePrincipalRef.Team(team), Read);
        _shares.Seed(rows[1].Table, rows[1].Id, DataversePrincipalRef.User(External), 0);

        var outcome = await Bar().RemoveBarredSharesAsync(message, CancellationToken.None);

        outcome.Should().BeEquivalentTo(new { TargetRestricted = (bool?)true, Removed = Array.Empty<object>(), Complete = true },
            "only a system user holding some right is a holder");
        RevokeCount.Should().Be(0);
        _users.Queries.Should().BeEmpty();
    }

    // ── Log capture: level, formatted message, exception and the structured properties ──

    internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Properties)
    {
        public bool Has(string key, object expected) => Properties.TryGetValue(key, out var value) && Equals(value, expected);
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> All
        {
            get
            {
                lock (_entries)
                    return _entries.ToList();
            }
        }

        public IEnumerable<LogEntry> At(LogLevel level) => All.Where(e => e.Level == level);

        /// <summary>
        /// The one log at <paramref name="level"/> about <paramref name="communicationId"/> (its <c>Id</c> property) carrying
        /// every property in <paramref name="properties"/>.
        /// </summary>
        public LogEntry Single(LogLevel level, Guid communicationId, params (string Key, object Value)[] properties)
        {
            var matches = At(level)
                .Where(e => e.Has("Id", communicationId) && properties.All(p => e.Has(p.Key, p.Value)))
                .ToList();
            matches.Should().ContainSingle(
                $"one {level} log about {communicationId} with [{string.Join(", ", properties.Select(p => $"{p.Key}={p.Value}"))}] " +
                $"is expected; logged: {string.Join(" | ", All.Select(e => $"{e.Level}: {e.Message}"))}");
            return matches[0];
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = (state as IEnumerable<KeyValuePair<string, object?>>)?
                .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value)
                ?? new Dictionary<string, object?>();
            lock (_entries)
                _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception, properties));
        }
    }
}
