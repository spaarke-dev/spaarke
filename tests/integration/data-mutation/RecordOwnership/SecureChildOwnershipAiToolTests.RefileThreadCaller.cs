using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// #2143 round 7 (F1): an email re-filed through <c>PATCH /api/communications/{id}/filing</c> (the ONE re-file core,
/// <see cref="Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync"/>) has its thread follow the filing, and that thread move acts
/// for the PERSON who re-filed it — the REAL resolver decides both the re-file and the thread move, the REAL
/// <see cref="ThreadResolver"/> reconciles the thread inside the REAL reconciler's step after the re-file.
/// </summary>
/// <remarks>
/// Before round 7 the re-file of an email regarding secure S, sitting in S's own thread, asked no F3 (the thread kept S),
/// and the thread move after it acted for nobody: it was refused (no caller) and swallowed, so the email ended regarding the
/// ordinary record, in S's thread, Secure-owned for good.
/// </remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid RefiledEmail = Guid.Parse("a2143000-0000-4000-8000-0000000000c7");
    private static readonly Guid SecureMattersThread = Guid.Parse("a2143000-0000-4000-8000-0000000000e7");
    private static readonly Guid OrdinaryMattersThread = Guid.Parse("a2143000-0000-4000-8000-0000000000e8");
    private static readonly Guid SecondOrdinaryMatter = Guid.Parse("a2143000-0000-4000-8000-0000000000a9");
    private static readonly Guid SecondOrdinaryMattersThread = Guid.Parse("a2143000-0000-4000-8000-0000000000e9");

    [Fact(DisplayName = "#2143 r7 F1: a Full Access holder re-filing an email off secure S, in S's own thread, onto ordinary O leaves it owned by O's team in O's thread")]
    public async Task FilingRoute_AFullAccessHolderRefilesAnEmailOffItsSecureRecordsThread_ItEndsInTheOrdinaryRecordsThread_OwnedByItsTeam()
    {
        AnEmailIn(SecureMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);
        _user.FullAccessOn.Add(SecureMatter);

        var result = await RefileTo(OrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(OrdinaryMatter);
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id
            .Should().Be(OrdinaryMattersThread, "the thread move acted for the person who re-filed it, and was permitted");
        email.GetAttributeValue<EntityReference>("owningteam")?.Id.Should().Be(Directory.ChildTeam, "O's team");
        _world.Assignments.Should().Equal(new[] { ("sprk_communication", RefiledEmail, Directory.ChildTeam) },
            "one owner write, once the thread followed: never an ordinary owner while it sat in S's thread");
    }

    [Fact(DisplayName = "#2143 r7 F1: a caller without Full Access on S re-filing an email off S, in S's own thread, is refused 403 not_permitted and nothing is written")]
    public async Task FilingRoute_ACallerWithoutFullAccessRefilesAnEmailOffItsSecureRecordsThread_IsRefused_AndNothingIsWritten()
    {
        AnEmailIn(SecureMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);

        var result = await RefileTo(OrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden, Detail(result));
        ReasonCode(result).Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        _user.Patches.Should().BeEmpty("refused before the caller's PATCH");
        _world.Assignments.Should().BeEmpty();
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(SecureMatter);
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id.Should().Be(SecureMattersThread);
    }

    [Fact(DisplayName = "#2143 r7 F1: an ordinary-to-ordinary re-file whose thread moves is unaffected: no F3, it ends in the new record's thread")]
    public async Task FilingRoute_AnOrdinaryToOrdinaryRefile_ItsThreadMoves_AsBefore()
    {
        AnEmailIn(OrdinaryMatter, OrdinaryMattersThread, Directory.ChildBu, Directory.ChildTeam);

        var result = await RefileTo(SecondOrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id.Should().Be(SecondOrdinaryMattersThread);
        email.GetAttributeValue<EntityReference>("owningteam")?.Id.Should().Be(Directory.GeneralTeam, "the second matter's team");
        _user.FullAccessOn.Should().BeEmpty("no Full Access is needed: no secure record is left");
    }

    [Fact(DisplayName = "#2143 r7b: re-filing an email between ordinary records while it sits in secure S2's thread, by a caller without Full Access on S2, is refused 403 not_permitted and nothing is written")]
    public async Task FilingRoute_AnOrdinaryRefileOfAnEmailInASecureRecordsThread_ByACallerWithoutFullAccessOnIt_IsRefused()
    {
        AnEmailIn(OrdinaryMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);

        var result = await RefileTo(SecondOrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden, Detail(result));
        ReasonCode(result).Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        _user.Patches.Should().BeEmpty("refused before the caller's PATCH: the thread move it would cause leaves S2");
        _world.Assignments.Should().BeEmpty();
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(OrdinaryMatter);
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id.Should().Be(SecureMattersThread);
    }

    [Fact(DisplayName = "#2143 r7b: the same re-file by a Full Access holder on S2 ends in the new record's thread, owned by its team")]
    public async Task FilingRoute_AnOrdinaryRefileOfAnEmailInASecureRecordsThread_ByAFullAccessHolderOnIt_EndsInTheNewRecordsThread()
    {
        AnEmailIn(OrdinaryMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);
        _user.FullAccessOn.Add(SecureMatter);

        var result = await RefileTo(SecondOrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(SecondOrdinaryMatter);
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id.Should().Be(SecondOrdinaryMattersThread);
        email.GetAttributeValue<EntityReference>("owningteam")?.Id.Should().Be(Directory.GeneralTeam, "the second matter's team");
    }

    // ---- #2143 round 8 (F1-A): the thread move's #1629 grant survives the mirror's removal ----

    /// <summary>A member of the ordinary matters the email is re-filed onto, outside their owning teams: the #1629 grantee.</summary>
    private static readonly Guid RecordMember = Guid.Parse("a2143000-0000-4000-8000-0000000000b8");

    private const int ReadMask = 1;

    private int? EmailShareOf(Guid user) =>
        _shareTable.MaskOf("sprk_communication", RefiledEmail, DataversePrincipalRef.User(user));

    [Fact(DisplayName = "#2143 r8 F1-A must-fire: an email re-filed off secure S, in S's thread, onto ordinary O keeps the grant its thread move gives O's members; its mirror is still taken off")]
    public async Task FilingRoute_AnEmailRefiledOffItsSecureRecordsThread_KeepsTheGrantItsThreadMoveGives_AndLosesItsMirror()
    {
        // Before round 8 the email stayed Secure through the re-file (its thread still held S), left isolation inside the
        // thread move, was granted to O's members there — and then the mirror step, still seeing "was isolated", took every
        // direct share off it, that grant included: O's members saw the thread but could not read the email.
        AnEmailIn(SecureMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);
        _shareTable.Seed("sprk_communication", RefiledEmail, DataversePrincipalRef.User(Sharee), CollaborateMask); // its mirror
        _user.FullAccessOn.Add(SecureMatter);

        var result = await RefileTo(OrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        _world.Row("sprk_communication", RefiledEmail).GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)
            ?.Id.Should().Be(OrdinaryMattersThread);
        EmailShareOf(RecordMember).Should().Be(ReadMask, "the thread move's #1629 grant to O's members stands");
        EmailShareOf(Sharee).Should().BeNull("the email left isolation, so its secure-child mirror came off");
    }

    [Fact(DisplayName = "#2143 r8 F1-A must-fire: an email re-filed between ordinary records while in secure S2's thread keeps the grant its thread move gives")]
    public async Task FilingRoute_AnOrdinaryRefileOfAnEmailInASecureRecordsThread_KeepsTheGrantItsThreadMoveGives()
    {
        AnEmailIn(OrdinaryMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);
        _shareTable.Seed("sprk_communication", RefiledEmail, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _user.FullAccessOn.Add(SecureMatter);

        var result = await RefileTo(SecondOrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        EmailShareOf(RecordMember).Should().Be(ReadMask, "the thread move's #1629 grant stands");
        EmailShareOf(Sharee).Should().BeNull("its secure-child mirror came off");
    }

    [Fact(DisplayName = "#2143 r8 F1-A must-not-fire: an ordinary-to-ordinary re-file is granted to the new record's members and nothing is revoked")]
    public async Task FilingRoute_AnOrdinaryToOrdinaryRefile_IsGrantedToTheNewRecordsMembers_AndRevokesNothing()
    {
        AnEmailIn(OrdinaryMatter, OrdinaryMattersThread, Directory.ChildBu, Directory.ChildTeam);

        var result = await RefileTo(SecondOrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        EmailShareOf(RecordMember).Should().Be(ReadMask);
        _shareTable.WriteLog.Should().NotContain(w => w.Action == "RevokeAccess" && w.RecordId == RefiledEmail,
            "a row that was never isolated has no mirror to take off");
    }

    // ---- #2143 round 8 (F1-B): the Office-save filing acts for the person who saved ----

    /// <summary>The systemuser the Office save's bearer token names.</summary>
    private static readonly Guid OfficeSaver = Guid.Parse("a2143000-0000-4000-8000-0000000000b9");

    /// <summary>
    /// An UNFILED email sitting in secure S's thread (as an unlink leaves it, #2165), owned by the Secure team: what an Office
    /// save of the same email onto an ordinary matter reconciles to.
    /// </summary>
    private void AnUnfiledEmailInTheSecureRecordsThread()
    {
        AnEmailIn(SecureMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);
        _world.Row("sprk_communication", RefiledEmail).Attributes.Remove("sprk_regardingmatter");
    }

    /// <summary>
    /// The REAL <see cref="Sprk.Bff.Api.Services.Office.ReconciledEmailFiling"/> filing the email under
    /// <paramref name="matter"/>, as an Office save by <see cref="OfficeSaver"/> holding <paramref name="rightsOnS"/> on S
    /// (and Write on the email): the REAL association write (the resolver's reparent), the REAL reconciler with the REAL thread
    /// resolver after it.
    /// </summary>
    private Task<Sprk.Bff.Api.Services.Office.ReconciledFilingResult> OfficeSaveFiles(
        Guid matter, AccessRights rightsOnS)
    {
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(d => d.RetrieveAsync("sprk_communication", RefiledEmail, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _world.Row("sprk_communication", RefiledEmail));
        dataverse
            .Setup(d => d.UpdateAsync("sprk_communication", RefiledEmail, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) =>
            {
                foreach (var (column, value) in fields)
                    _world.Row("sprk_communication", RefiledEmail)[column] = value;
            })
            .Returns(Task.CompletedTask);

        var probe = new Mock<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>(
            new HttpClient(), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>.Instance, null!, null!)
        { CallBase = false };
        probe.Setup(p => p.GetCallerSystemUserIdAsync("office-token", It.IsAny<CancellationToken>())).ReturnsAsync(OfficeSaver);
        probe.Setup(p => p.GetCallerRightsAsync("office-token", "sprk_communications", RefiledEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read | AccessRights.Write);
        probe.Setup(p => p.GetCallerRightsAsync("office-token", "sprk_matters", SecureMatter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rightsOnS);

        var options = new Sprk.Bff.Api.Configuration.AutoFileOptions { Enabled = true, Threshold = 0.85 };
        var mapper = new Sprk.Bff.Api.Services.Communication.Engine.AssociationStatusMapper(
            new Sprk.Bff.Api.Services.Communication.Engine.AutoFileGate(
                Mock.Of<Microsoft.Extensions.Options.IOptionsMonitor<Sprk.Bff.Api.Configuration.AutoFileOptions>>(m => m.CurrentValue == options)),
            NullLogger<Sprk.Bff.Api.Services.Communication.Engine.AssociationStatusMapper>.Instance);
        var threads = ThreadsOverTheDirectory();
        var association = new IncomingAssociationResolver(
            Array.Empty<Sprk.Bff.Api.Services.Communication.Engine.IAssociationRung>(), dataverse.Object, dataverse.Object, mapper,
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(), _world.Resolver(),
            NullLogger<IncomingAssociationResolver>.Instance, threads);
        var filing = new Sprk.Bff.Api.Services.Office.ReconciledEmailFiling(
            association, dataverse.Object, probe.Object, Children(threads), Restamper(),
            NullLogger<Sprk.Bff.Api.Services.Office.ReconciledEmailFiling>.Instance);

        var target = new EntityReference("sprk_matter", matter);
        var decision = new Sprk.Bff.Api.Services.Communication.Engine.AssociationDecision
        {
            Status = Sprk.Bff.Api.Services.Communication.Engine.AssociationStatusCodes.Resolved,
            RegardingWrites = new Dictionary<string, EntityReference> { ["sprk_regardingmatter"] = target },
            AutoFiled = false,
            Provenance = new Sprk.Bff.Api.Services.Communication.Engine.AssociationProvenance
            {
                Direction = "Incoming",
                Decision = new Sprk.Bff.Api.Services.Communication.Engine.AssociationDecisionTrace
                {
                    Status = "Resolved",
                    AutoFiled = false,
                    KillSwitchEnabled = true,
                    AutoFileThreshold = 0.85,
                    TopDeterministicConfidence = 1,
                    TopConfidence = 1,
                    AiInvolved = false,
                    Reason = "caller-supplied",
                },
                RungsFired = Array.Empty<string>(),
                Candidates = Array.Empty<Sprk.Bff.Api.Services.Communication.Engine.CandidateTrace>(),
                Signals = Array.Empty<Sprk.Bff.Api.Services.Communication.Engine.SignalTrace>(),
            },
        };
        var request = new Sprk.Bff.Api.Models.Office.SaveRequest
        {
            ContentType = Sprk.Bff.Api.Models.Office.SaveContentType.Email,
            TargetEntity = new Sprk.Bff.Api.Models.Office.SaveEntityReference { EntityType = "Matter", EntityId = matter },
        };
        return filing.FileIfUnfiledAsync(
            request, new EmailCaptureOutcome(RefiledEmail, ReconciledToExisting: true, decision), "office-token",
            CancellationToken.None);
    }

    [Fact(DisplayName = "#2143 r8 F1-B must-fire: an Office save by a Full Access holder on S files an unfiled email in S's thread onto ordinary O, and it ends in O's thread owned by O's team")]
    public async Task OfficeSave_ByAFullAccessHolderOnTheSecureRecordItsThreadHolds_FilesTheUnfiledEmail_AndItsThreadFollows()
    {
        // Before round 8 the save passed no caller: R7b asks F3 about S (the thread the reconcile leaves), and with nobody to
        // ask F3 answers Unverifiable, so every such save was refused, logged, and the email left unfiled.
        AnUnfiledEmailInTheSecureRecordsThread();

        var result = await OfficeSaveFiles(OrdinaryMatter, FullAccessOnTheRecord);

        result.Should().Be(Sprk.Bff.Api.Services.Office.ReconciledFilingResult.Filed);
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(OrdinaryMatter);
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id
            .Should().Be(OrdinaryMattersThread, "the thread move acted for the person who saved");
        email.GetAttributeValue<EntityReference>("owningteam")?.Id.Should().Be(Directory.ChildTeam, "O's team");
    }

    [Fact(DisplayName = "#2143 r8 F1-B must-not-fire: an Office save by a caller with no rights on S leaves the unfiled email in S's thread unfiled")]
    public async Task OfficeSave_ByACallerWithNoRightsOnTheSecureRecordItsThreadHolds_IsNotFiled()
    {
        AnUnfiledEmailInTheSecureRecordsThread();

        var result = await OfficeSaveFiles(OrdinaryMatter, AccessRights.None);

        result.Should().Be(Sprk.Bff.Api.Services.Office.ReconciledFilingResult.Failed, "the reparent refused it before the write");
        var email = _world.Row("sprk_communication", RefiledEmail);
        email.GetAttributeValue<EntityReference>("sprk_regardingmatter").Should().BeNull("nothing is written");
        email.GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id.Should().Be(SecureMattersThread);
        _world.Assignments.Should().BeEmpty();
    }

    /// <summary>Full Access on a record, as <c>RetrievePrincipalAccess</c> reports it (Write and Delete among the rights).</summary>
    private const AccessRights FullAccessOnTheRecord =
        AccessRights.Read | AccessRights.Write
        | AccessRights.Append | AccessRights.AppendTo
        | AccessRights.Delete | AccessRights.Share;

    // ---- #2143 round 9 (F2): the mirror latch is set only by a removal that completed ----

    /// <summary>The reconciler's Warnings that the mirror came off after the thread move's grant, revoking it.</summary>
    private IReadOnlyList<string> GrantRevokedWarnings() => _reconcilerLog.Entries
        .Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("REVOKED", StringComparison.Ordinal))
        .Select(e => e.Message)
        .ToList();

    /// <summary>The share-table revokes of the email's shares, every principal.</summary>
    private int EmailRevokes() =>
        _shareTable.WriteLog.Count(w => w.Action == "RevokeAccess" && w.RecordId == RefiledEmail);

    /// <summary>
    /// The email re-filed off secure S, in S's thread, onto ordinary O by a Full Access holder on S, holding S's sharee's
    /// mirror; the share world's by-id reads of the email for which <paramref name="faults"/> answers <c>true</c> fault
    /// (counted from 1: the step's isolation read before the reconcile, then — on the hook — its isolation read, the mirror's
    /// read of the row, the removal's read).
    /// </summary>
    private async Task RefileOffTheSecureThreadWithReadFaults(Func<int, bool> faults)
    {
        AnEmailIn(SecureMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);
        _shareTable.Seed("sprk_communication", RefiledEmail, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _user.FullAccessOn.Add(SecureMatter);
        ShareWorld.TransientRowReadFaultsOf("sprk_communication", RefiledEmail, faults);

        var result = await RefileTo(OrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        _world.Row("sprk_communication", RefiledEmail).GetAttributeValue<EntityReference>("owningteam")?.Id
            .Should().Be(Directory.ChildTeam, "it left isolation with its thread");
    }

    [Fact(DisplayName = "#2143 r9 F2 must-fire: a transient fault reading isolation before the reconcile does not latch the mirror as removed: it comes off at the hook, and the grant stands")]
    public async Task FilingRoute_ATransientIsolationReadFaultBeforeTheReconcile_StillTakesTheMirrorOff_AndKeepsTheGrant()
    {
        // Before round 9: the faulted read answered "left isolation", the mirror step found the email still Secure and
        // re-mirrored it, and the latch was set anyway — so the hook skipped the removal, the last step ran as "was not
        // isolated", and S's sharee kept Collaborate on an ordinary email filed to O.
        await RefileOffTheSecureThreadWithReadFaults(read => read == 1);

        ShareWorld.TransientRowReadFaultsFired("sprk_communication", RefiledEmail).Should().Be(1,
            "round 10: the isolation read before the reconcile faulted — never vacuous");
        EmailShareOf(Sharee).Should().BeNull("the secure-child mirror came off once the email left isolation");
        EmailShareOf(RecordMember).Should().Be(ReadMask, "it came off at the hook, before the thread move's grant");
        GrantRevokedWarnings().Should().BeEmpty();
    }

    [Fact(DisplayName = "#2143 r9 F2 must-fire: a hook isolation-read fault followed by a mirror-read fault and failed removal reads leaves no secure share: the last step takes the mirror off (the grant with it)")]
    public async Task FilingRoute_AHookReadFaultThenAMirrorReadFault_TheLastStepTakesTheMirrorOff()
    {
        // Before round 9: the hook's faulted read answered "left", the mirror step could not read the row (Failed) and returned,
        // and the latch was set anyway: the last step ran as "was not isolated" and S's sharee kept Collaborate. Now the last
        // step removes the mirror — and, as the removal revokes every direct share, the grant the move gave (an under-share,
        // logged: fail closed). Round 10 (F2-b): a Failed mirror step goes on to the removal, so the hook's removal must fail
        // too for the last step to be the one: reads 2 (the hook's isolation read), 3 (the mirror's read of the row) and 4-6
        // (the removal's three attempts) fault.
        await RefileOffTheSecureThreadWithReadFaults(read => read is >= 2 and <= 6);
        ShareWorld.TransientRowReadFaultsFired("sprk_communication", RefiledEmail).Should().Be(5);

        EmailShareOf(Sharee).Should().BeNull("no secure-child share outlives the email's isolation");
        EmailShareOf(RecordMember).Should().BeNull("the removal after the grant revokes it too: an under-share, never an over-share");
        GrantRevokedWarnings().Should().ContainSingle("the under-share is said plainly");
    }

    [Fact(DisplayName = "#2143 r9 F2 must-not-fire: a removal left to the last step on a re-file with no thread move revokes no grant, and says nothing of one")]
    public async Task FilingRoute_NoThreadMove_TheLastStepTakesTheMirrorOff_WithNoRevokedGrantWarning()
    {
        // Already in O's thread, so the re-file to O moves no thread and runs no hook; the mirror's read of the row and (round
        // 10, F2-b: a Failed mirror step goes on to the removal) all three removal reads fault on the step before the
        // reconcile, so the removal is left to the last step — and there was no grant for it to revoke.
        AnEmailIn(SecureMatter, OrdinaryMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);
        _shareTable.Seed("sprk_communication", RefiledEmail, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _user.FullAccessOn.Add(SecureMatter);
        ShareWorld.TransientRowReadFaultsOf("sprk_communication", RefiledEmail, read => read is >= 2 and <= 5);

        var result = await RefileTo(OrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        ShareWorld.TransientRowReadFaultsFired("sprk_communication", RefiledEmail).Should().Be(4,
            "round 10: the removal before the reconcile faulted on every attempt — never vacuous");
        EmailRevokes().Should().Be(1, "the last step's removal");
        EmailShareOf(Sharee).Should().BeNull("the last step takes the mirror off");
        GrantRevokedWarnings().Should().BeEmpty("no thread move ran, so no grant was revoked");
        RemovalErrors().Should().BeEmpty(
            "round 11 (F2-1): the step before the reconcile is not the last try, so its exhausted removal is a Warning");
    }

    [Fact(DisplayName = "#2143 r11 F2-1 must-fire: when the LAST step's removal is exhausted too, it is still logged as an ERROR, once")]
    public async Task FilingRoute_NoThreadMove_WhenTheLastStepsRemovalIsExhausted_ItIsStillAnError()
    {
        // As above, but every read of the row after the first faults: the step before the reconcile and the last step both
        // exhaust their removals. Only the last one is an ERROR; S's sharee keeps the share, and the log says so.
        AnEmailIn(SecureMatter, OrdinaryMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);
        _shareTable.Seed("sprk_communication", RefiledEmail, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _user.FullAccessOn.Add(SecureMatter);
        ShareWorld.TransientRowReadFaultsOf("sprk_communication", RefiledEmail, read => read >= 2);

        await RefileTo(OrdinaryMatter);

        ShareWorld.TransientRowReadFaultsFired("sprk_communication", RefiledEmail).Should().BeGreaterThanOrEqualTo(8,
            "both steps' mirror reads and all six removal attempts faulted — never vacuous");
        EmailRevokes().Should().Be(0, "no removal got through");
        EmailShareOf(Sharee).Should().Be(CollaborateMask);
        RemovalErrors().Should().ContainSingle("the last step's exhausted removal is still an ERROR");
        _sharesLog.Entries.Count(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning
            && e.Message.Contains("not all removed", StringComparison.Ordinal))
            .Should().Be(1, "the step before the reconcile logged its exhausted removal as a Warning");
    }

    [Fact(DisplayName = "#2143 r9 F2 must-not-fire: with no fault the mirror is removed exactly once, at the hook, and the grant stands")]
    public async Task FilingRoute_WithNoFault_TheMirrorIsRemovedExactlyOnce_AndTheGrantStands()
    {
        await RefileOffTheSecureThreadWithReadFaults(_ => false);

        EmailRevokes().Should().Be(1, "one removal, of the one mirrored share");
        EmailShareOf(Sharee).Should().BeNull();
        EmailShareOf(RecordMember).Should().Be(ReadMask);
        GrantRevokedWarnings().Should().BeEmpty();
    }

    // ---- #2143 round 10 (K1 pin): a mirror step that THROWS does not latch the mirror as removed ----

    /// <summary>
    /// The synchronizer's logger, recording into <see cref="_sharesLog"/>, that throws the FIRST time the synchronizer logs its
    /// run summary (<c>[SECURE-CHILD-SHARES] scope=</c>) — the one fault that escapes <c>SyncChildAsync</c> to
    /// <c>AfterRefileAsync</c>'s catch.
    /// </summary>
    private sealed class ThrowsOnTheFirstRunSummary(Microsoft.Extensions.Logging.ILogger<Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer> inner)
        : Microsoft.Extensions.Logging.ILogger<Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer>
    {
        public int Thrown { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
            if (Thrown == 0 && formatter(state, exception).StartsWith("[SECURE-CHILD-SHARES] scope=", StringComparison.Ordinal))
            {
                Thrown++;
                throw new InvalidOperationException("Test: the run summary's log sink faulted.");
            }
        }
    }

    [Fact(DisplayName = "#2143 r10 K1 pin: a mirror step that throws (its run summary's log faults) before the reconcile does not latch the mirror as removed: it comes off at the hook, and the grant stands")]
    public async Task FilingRoute_AMirrorStepThatThrows_DoesNotLatchTheMirrorAsRemoved()
    {
        // The isolation read before the reconcile faults ("left"), the email is still Secure in S's thread, and mirroring it
        // throws out of SyncChildAsync: AfterRefileAsync's catch answers false (nothing removed), so the hook still takes the
        // mirror off. A catch answering true would latch it, and S's sharee would keep Collaborate on an ordinary email.
        var sink = new ThrowsOnTheFirstRunSummary(_sharesLog);
        _sharesLogger = sink;

        await RefileOffTheSecureThreadWithReadFaults(read => read == 1);

        sink.Thrown.Should().Be(1, "the mirror step threw");
        ShareWorld.TransientRowReadFaultsFired("sprk_communication", RefiledEmail).Should().Be(1);
        EmailShareOf(Sharee).Should().BeNull("the secure-child mirror came off once the email left isolation");
        EmailShareOf(RecordMember).Should().Be(ReadMask, "it came off at the hook, before the thread move's grant");
    }

    // ---- #2143 round 10 (F2-a): every thread-reconcile path that grants runs the hook first ----

    private static readonly Guid UsersOwnThread = Guid.Parse("a2143000-0000-4000-8000-0000000000eb");

    /// <summary>
    /// The email (filed to secure S, Secure-owned, holding S's sharee's mirror) re-filed by a Full Access holder on S onto
    /// ordinary O, with the share world's by-id reads 2-5 of it faulting: the mirror step before the reconcile (read 2) and its
    /// removal's three attempts (reads 3-5) — so the mirror is still on the email when the reconcile grants.
    /// </summary>
    private async Task RefileOntoTheOrdinaryRecordWithThePreReconcileRemovalFaulting()
    {
        _shareTable.Seed("sprk_communication", RefiledEmail, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _user.FullAccessOn.Add(SecureMatter);
        ShareWorld.TransientRowReadFaultsOf("sprk_communication", RefiledEmail, read => read is >= 2 and <= 5);

        var result = await RefileTo(OrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        ShareWorld.TransientRowReadFaultsFired("sprk_communication", RefiledEmail).Should().Be(4,
            "the removal before the reconcile faulted on every attempt");
        _world.Row("sprk_communication", RefiledEmail).GetAttributeValue<EntityReference>("owningteam")?.Id
            .Should().Be(Directory.ChildTeam, "the re-file itself took it out of isolation");
    }

    [Fact(DisplayName = "#2143 r10 F2-a must-fire: rule 2 (the email's ordinary thread follows it whole) takes the mirror off before its grant: the grant stands and no mirror remains")]
    public async Task FilingRoute_WholeThreadAnchor_TakesTheMirrorOffBeforeItsGrant()
    {
        // The email sits alone in a user's own Open, active, non-default thread: rule 2 anchors that thread to O and grants O's
        // members the email. Before round 10 that path ran no hook, so the mirror the faulted step left came off at the last
        // step — revoking the grant too, with no Warning.
        AnEmailAloneInAUsersOwnThread();

        await RefileOntoTheOrdinaryRecordWithThePreReconcileRemovalFaulting();

        _world.Row("sprk_communication", RefiledEmail).GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id
            .Should().Be(UsersOwnThread, "the thread followed the filing whole (rule 2), the email did not move");
        EmailShareOf(Sharee).Should().BeNull("no secure-child mirror outlives the email's isolation");
        EmailShareOf(RecordMember).Should().Be(ReadMask, "the mirror came off at the hook, before rule 2's grant");
        GrantRevokedWarnings().Should().BeEmpty();
    }

    [Fact(DisplayName = "#2143 r10 F2-a must-not-fire: with no fault, rule 2's hook finds the mirror already off: one removal, the grant stands, no Warning")]
    public async Task FilingRoute_WholeThreadAnchor_WithNoFault_RemovesTheMirrorOnce_AndTheGrantStands()
    {
        AnEmailAloneInAUsersOwnThread();
        _shareTable.Seed("sprk_communication", RefiledEmail, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _user.FullAccessOn.Add(SecureMatter);

        var result = await RefileTo(OrdinaryMatter);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        _world.Row("sprk_communication", RefiledEmail).GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id
            .Should().Be(UsersOwnThread);
        EmailRevokes().Should().Be(1, "removed once, before the reconcile; the hook is latched");
        EmailShareOf(Sharee).Should().BeNull();
        EmailShareOf(RecordMember).Should().Be(ReadMask);
        GrantRevokedWarnings().Should().BeEmpty();
    }

    /// <summary>The email filed to secure S, alone in a user's own Open, active, non-default (Direct) thread.</summary>
    private void AnEmailAloneInAUsersOwnThread()
    {
        AnEmailIn(SecureMatter, UsersOwnThread, Directory.SecureBu, Directory.SecureNamedTeam);
        _world.WithRecord("sprk_communicationthread", UsersOwnThread, Directory.ChildBu, extra: new()
        {
            ["ownerid"] = new EntityReference("systemuser", Guid.Parse("a2143000-0000-4000-8000-0000000000ef")),
            ["sprk_threadtype"] = new OptionSetValue(100000001), // Direct: no record above it
            ["sprk_privacystate"] = new OptionSetValue(100000000), // Open
            ["sprk_isdefaultthread"] = false,
            ["statecode"] = new OptionSetValue(0),
        });
    }

    [Fact(DisplayName = "#2143 r10 F2-a must-fire: a threadless email's join takes the mirror off before its grant: the grant stands and no mirror remains")]
    public async Task AfterRefileStep_ThreadlessJoin_TakesTheMirrorOffBeforeItsGrant()
    {
        // The email, re-filed off secure S onto ordinary O (ordinary-owned now, still holding S's sharee's mirror), has no
        // thread: the reconcile resolves one (O's default thread) and grants O's members the email. Before round 10 that path
        // ran no hook. The step after the re-file is driven directly, with an email key strategy that finds no conversation (so
        // the join descends to O's default thread, Tier 2).
        AnEmailIn(OrdinaryMatter, OrdinaryMattersThread, Directory.ChildBu, Directory.ChildTeam);
        _world.Row("sprk_communication", RefiledEmail).Attributes.Remove(ThreadResolver.ThreadLookupOnCommunication);
        _shareTable.Seed("sprk_communication", RefiledEmail, DataversePrincipalRef.User(Sharee), CollaborateMask);
        ShareWorld.TransientRowReadFaultsOf("sprk_communication", RefiledEmail, read => read is >= 2 and <= 5);
        var noConversation = new Mock<IThreadKeyStrategy>();
        noConversation.SetupGet(k => k.SupportedType).Returns(CommunicationType.Email);
        noConversation
            .Setup(k => k.ResolveAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThreadKeyResolution(null, false));

        _world.Row("sprk_communication", RefiledEmail).GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)
            .Should().BeNull("threadless before the step");
        await Children(ThreadsOverTheDirectory(new[] { noConversation.Object })).AfterRefileAsync(
            "sprk_communication", RefiledEmail, () => Task.FromResult(true), Sprk.Bff.Api.Services.Access.RefileThreadStep.Reconcile, CancellationToken.None);

        ShareWorld.TransientRowReadFaultsFired("sprk_communication", RefiledEmail).Should().Be(4,
            "the removal before the reconcile faulted on every attempt");
        _world.Row("sprk_communication", RefiledEmail).GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id
            .Should().Be(OrdinaryMattersThread, "the threadless email joined O's default thread");
        EmailShareOf(Sharee).Should().BeNull("no secure-child mirror outlives the email's isolation");
        EmailShareOf(RecordMember).Should().Be(ReadMask, "the mirror came off at the hook, before the join's grant");
        GrantRevokedWarnings().Should().BeEmpty();
    }

    // ---- #2143 round 10 (F2-b): a mirror step that could not decide still takes a row that left isolation through the removal ----

    /// <summary>
    /// A to-do filed under secure S (Secure-owned, holding S's sharee's mirror) re-filed by a Full Access holder on S onto
    /// <paramref name="matter"/>; the share world's FIRST by-id read of it (the mirror step's read of the row) faults.
    /// </summary>
    private async Task RefileTheSecureToDoWithItsMirrorReadFaulting(Guid matter)
    {
        _world.WithRecord("sprk_todo", Todo, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
        });
        ShareWorld.SecureChild("sprk_todo", Todo, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        _shareTable.Seed("sprk_todo", Todo, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _user.FullAccessOn.Add(SecureMatter);
        _user.OwningTeamOf[Todo] = Directory.SecureNamedTeam;
        ShareWorld.Set("sprk_todo", Todo, "sprk_regardingmatter", new EntityReference("sprk_matter", matter));
        ShareWorld.TransientRowReadFaultsOf("sprk_todo", Todo, read => read == 1);

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})",
        });

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        ShareWorld.TransientRowReadFaultsFired("sprk_todo", Todo).Should().Be(1, "the mirror step's read of the row faulted");
    }

    /// <summary>The synchronizer's Errors that a re-filed row's mirror removal failed (not its own read fault's Error).</summary>
    private IEnumerable<string> RemovalErrors() => _sharesLog.Entries
        .Where(e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error && e.Message.Contains("not all removed", StringComparison.Ordinal))
        .Select(e => e.Message);

    [Fact(DisplayName = "#2143 r10 F2-b must-fire: a to-do re-filed off secure S onto ordinary O, whose mirror step cannot read it, still loses S's mirrored shares")]
    public async Task ChildRefile_OffASecureMatter_WhenTheMirrorStepCannotReadTheRow_TheMirrorIsStillRemoved()
    {
        // Before round 10: the mirror step answered Failed and the re-file step returned before it asked whether the row was
        // isolated, so S's sharee kept Collaborate on an ordinary to-do for good (the two-minute reconcile sees only
        // Secure-owned rows).
        await RefileTheSecureToDoWithItsMirrorReadFaulting(OrdinaryMatter);

        _world.Assignments.Should().Equal(("sprk_todo", Todo, Directory.ChildTeam));
        _shareTable.MaskOf("sprk_todo", Todo, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "the removal reads the owner again: ordinary, so the mirror comes off");
        RemovalErrors().Should().BeEmpty();
    }

    [Fact(DisplayName = "#2143 r10 F2-b must-not-fire: a to-do re-filed from secure S onto secure S2, whose mirror step cannot read it, keeps its mirror, and no error is logged")]
    public async Task ChildRefile_BetweenSecureMatters_WhenTheMirrorStepCannotReadTheRow_ItKeepsItsMirror()
    {
        // Still owned by the Secure team: the removal refuses it (still isolated, not a fault) — it keeps its shares, and the
        // two-minute reconcile completes its mirror.
        _world.WithSecureRoot("sprk_matter", SecondSecureMatter);
        ShareWorld.SecureRoot("sprk_matter", SecondSecureMatter);

        await RefileTheSecureToDoWithItsMirrorReadFaulting(SecondSecureMatter);

        _world.Assignments.Should().BeEmpty("it stays with the Secure team");
        _shareTable.MaskOf("sprk_todo", Todo, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
        _shareTable.WriteLog.Should().NotContain(w => w.Action == "RevokeAccess" && w.RecordId == Todo);
        RemovalErrors().Should().BeEmpty("a row still isolated is not a failed removal");
    }

    private static readonly Guid SecondSecureMatter = Guid.Parse("a2143000-0000-4000-8000-0000000000a8");
    private static readonly Guid SecondSecureMattersThread = Guid.Parse("a2143000-0000-4000-8000-0000000000ea");

    [Fact(DisplayName = "#2143 r7c F2: step 1 of the two-step bypass - a plain PATCH moving an email off secure S2's thread, by an S1-only holder, is refused 403 not_permitted and nothing is written")]
    public async Task ChildRecordPatch_AThreadOnlyMoveOffASecureRecordsThread_ByACallerWithoutFullAccessOnIt_IsRefusedAtStepOne()
    {
        // The email regards secure S1 and sits in secure S2's thread. Step 1 moves it to an ordinary thread with a plain PATCH
        // of the thread lookup alone; step 2 would re-file its regarding off S1 (which the caller may). Before round 7c step 1
        // was an ordinary PATCH: no reparent, no F3 on S2.
        _world.WithSecureRoot("sprk_matter", SecondSecureMatter);
        AnEmailIn(SecureMatter, SecureMattersThread, Directory.SecureBu, Directory.SecureNamedTeam);
        RecordThread(SecondSecureMattersThread, SecondSecureMatter, Directory.SecureBu, Directory.SecureNamedTeam);
        _world.Row("sprk_communication", RefiledEmail)[ThreadResolver.ThreadLookupOnCommunication] =
            new EntityReference("sprk_communicationthread", SecondSecureMattersThread);
        _user.FullAccessOn.Add(SecureMatter); // S1 only

        var result = await Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync(
            "sprk_communication", RefiledEmail,
            Payload(new() { ["sprk_CommunicationThread@odata.bind"] = $"/sprk_communicationthreads({OrdinaryMattersThread:D})" }),
            _user, _world.Resolver(), Restamper(), Children(ThreadsOverTheDirectory()), HttpContextOfCaller(),
            NullLogger<Program>.Instance, CancellationToken.None);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden, Detail(result));
        ReasonCode(result).Should().Be(Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.NotPermittedReasonCode);
        _user.Patches.Should().BeEmpty("the thread-only PATCH is a re-file, refused before it is sent");
        _world.Assignments.Should().BeEmpty();
        _world.Row("sprk_communication", RefiledEmail).GetAttributeValue<EntityReference>(ThreadResolver.ThreadLookupOnCommunication)?.Id
            .Should().Be(SecondSecureMattersThread);
    }

    [Fact(DisplayName = "#2143 r7c F2: a plain PATCH moving an email between ordinary records' threads is a re-file that writes and asks no F3")]
    public async Task ChildRecordPatch_AThreadOnlyMoveBetweenOrdinaryThreads_IsWritten()
    {
        AnEmailIn(OrdinaryMatter, OrdinaryMattersThread, Directory.ChildBu, Directory.ChildTeam);

        var result = await Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync(
            "sprk_communication", RefiledEmail,
            Payload(new() { ["sprk_CommunicationThread@odata.bind"] = $"/sprk_communicationthreads({SecondOrdinaryMattersThread:D})" }),
            _user, _world.Resolver(), Restamper(), Children(ThreadsOverTheDirectory()), HttpContextOfCaller(),
            NullLogger<Program>.Instance, CancellationToken.None);

        Status(result).Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle();
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An email regarding <paramref name="matter"/>, sitting in <paramref name="thread"/> (that matter's default record thread),
    /// owned by <paramref name="owningTeam"/> in <paramref name="owningBu"/> — and the default record threads of the ordinary
    /// matters it can be re-filed onto. Every row is in the resolver's directory, which the thread resolver reads too.
    /// </summary>
    private void AnEmailIn(Guid matter, Guid thread, Guid owningBu, Guid owningTeam)
    {
        _world.WithRecord("sprk_matter", SecondOrdinaryMatter, Directory.GeneralBu, owningTeam: Directory.GeneralTeam);
        RecordThread(SecureMattersThread, SecureMatter, Directory.SecureBu, Directory.SecureNamedTeam);
        RecordThread(OrdinaryMattersThread, OrdinaryMatter, Directory.ChildBu, Directory.ChildTeam);
        RecordThread(SecondOrdinaryMattersThread, SecondOrdinaryMatter, Directory.GeneralBu, Directory.GeneralTeam);
        _world.WithRecord("sprk_communication", RefiledEmail, owningBu, owningTeam: owningTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
            [ThreadResolver.ThreadLookupOnCommunication] = new EntityReference("sprk_communicationthread", thread),
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email),
            ["createdby"] = new EntityReference("systemuser", Guid.Parse("a2143000-0000-4000-8000-0000000000ee")),
        });
        _user.OwningTeamOf[RefiledEmail] = owningTeam; // #2143 r8: as the caller reads it before the write
        if (owningTeam == Directory.SecureNamedTeam)
            ShareWorld.SecureChild("sprk_communication", RefiledEmail, ("sprk_regardingmatter", "sprk_matter", matter));
        else
            ShareWorld.OrdinaryChild("sprk_communication", RefiledEmail, ("sprk_regardingmatter", "sprk_matter", matter));
    }

    /// <summary>A matter's default record thread: Record-anchored, Open, active, owned by the matter's team.</summary>
    private void RecordThread(Guid thread, Guid matter, Guid owningBu, Guid owningTeam) =>
        _world.WithRecord("sprk_communicationthread", thread, owningBu, owningTeam: owningTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
            ["sprk_regardingrecordid"] = matter.ToString(),
            ["sprk_threadtype"] = new OptionSetValue(100000000),
            ["sprk_privacystate"] = new OptionSetValue(100000000),
            ["sprk_isdefaultthread"] = true,
            ["statecode"] = new OptionSetValue(0),
        });

    /// <summary>The filing route's re-file of the email's regarding onto <paramref name="matter"/>, as the caller.</summary>
    private Task<Microsoft.AspNetCore.Http.IResult> RefileTo(Guid matter) =>
        Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync(
            "sprk_communication", RefiledEmail,
            Payload(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})" }),
            _user, _world.Resolver(), Restamper(), Children(ThreadsOverTheDirectory()), HttpContextOfCaller(),
            NullLogger<Program>.Instance, CancellationToken.None, filingOnly: true);

    /// <summary>
    /// The REAL thread resolver over the resolver's directory: rows are read from it, a matter's default thread is found by its
    /// key, and the email's thread-lookup stamp lands on its row.
    /// </summary>
    private ThreadResolver ThreadsOverTheDirectory(IThreadKeyStrategy[]? strategies = null)
    {
        var entities = new Mock<IGenericEntityService>();
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => _world.Row(table, id));
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) => DefaultThreadOf(query));
        entities
            .Setup(e => e.UpdateAsync("sprk_communication", RefiledEmail, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) =>
            {
                foreach (var (column, value) in fields)
                    _world.Row("sprk_communication", RefiledEmail)[column] = value;
            })
            .Returns(Task.CompletedTask);
        // #2143 round 8 (F1-A): the REAL #1629 grant service, over the share world and the same share table the mirror writes,
        // so a grant the thread move gives and a mirror removal after it meet in one place. The ordinary matters' grantee is
        // RecordMember.
        var members = new Mock<Sprk.Bff.Api.Services.Communication.Membership.IThreadMembershipDerivationService>();
        members
            .Setup(m => m.DeriveAnchorGranteeUserIdsAsync(
                "sprk_matter", It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid matter, Guid? _, CancellationToken _) =>
                matter == OrdinaryMatter || matter == SecondOrdinaryMatter ? new[] { RecordMember } : Array.Empty<Guid>());
        var grants = new Sprk.Bff.Api.Services.Communication.Access.DirectThreadAccessService(
            SecureChildShareWorld.EntitiesOver(() => ShareWorld).Object, _shareTable,
            new Lazy<Sprk.Bff.Api.Services.Communication.Membership.IThreadMembershipDerivationService>(() => members.Object),
            SecureChildShareWorld.Configuration(),
            NullLogger<Sprk.Bff.Api.Services.Communication.Access.DirectThreadAccessService>.Instance, _gate.Sharing);
        return new ThreadResolver(
            strategies ?? Array.Empty<IThreadKeyStrategy>(), entities.Object, _world.Resolver(), NullLogger<ThreadResolver>.Instance,
            grants);
    }

    /// <summary>A default-thread lookup (FindDefaultThreadAsync: keyed on <c>sprk_regardingrecordid</c>) answers the matter's thread.</summary>
    private static EntityCollection DefaultThreadOf(QueryExpression query)
    {
        var key = query.Criteria.Conditions
            .FirstOrDefault(c => c.AttributeName == "sprk_regardingrecordid")?.Values.FirstOrDefault()?.ToString();
        var thread = key == OrdinaryMatter.ToString() ? OrdinaryMattersThread
            : key == SecondOrdinaryMatter.ToString() ? SecondOrdinaryMattersThread
            : key == SecureMatter.ToString() ? SecureMattersThread
            : (Guid?)null;
        return thread is { } id && query.EntityName == "sprk_communicationthread"
            ? new EntityCollection(new List<Entity> { new("sprk_communicationthread", id) })
            : new EntityCollection();
    }
}
