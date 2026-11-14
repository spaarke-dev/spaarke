using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using FakeDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// uac-r2 task 197a (#2722, owner decision 2026-11-12): the owner of a communication a re-file leaves filed under nothing that is
/// not an eligible Direct message. It moves to its recorded creator's business-unit team (D-11), through the resolver's one
/// creator-team path (191c's External Collaborators rule included), written by the re-file's settle. It keeps its former owner
/// (round 87, a re-file never loosens) when a parent it leaves is Secure, Restricted or owned in the Secure Record unit, when the
/// message itself is held there, Secure or Restricted, when its creator is not recorded or cannot be resolved, and when a fact
/// cannot be read.
/// </summary>
/// <remarks>The REAL <see cref="RecordOwnershipResolver"/> over <see cref="FakeDirectory"/>; the Direct rule is off (or the message is
/// an email), so every case here is a non-Direct unfile.</remarks>
public class RecordOwnershipResolverUnfileOwnerTests
{
    private static readonly Guid MessageId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b3");
    private static readonly Guid MatterId = Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000a1");
    private static readonly Guid SecureMatterId = Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000a2");
    private static readonly Guid RestrictedMatterId = Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000a3");
    private static readonly Guid SecureOwnedUnflaggedMatterId = Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000a4");
    private static readonly Guid FlaggedOrdinaryMatterId = Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000a5");
    private static readonly Guid MaskedFlagMatterId = Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000a6");
    private static readonly Guid CreatorId = Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000c1");
    private static readonly Guid CreatorOid = Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000c2");
    private const int Restricted = 100000002;

    private readonly RecordingLogger _log = new();

    // ── Must fire ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CommunicationType.Email)]
    [InlineData(CommunicationType.Message)]
    public async Task Unfile_FromAStandardMatter_MovesToTheCreatorsBusinessUnitTeam_WrittenByTheSettle(CommunicationType type)
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: type)
            .WithRecordedCreator();

        var resolution = await Resolver(directory).ReparentAsync(Unfile(), Applies(directory, Unfile()), CancellationToken.None);

        resolution.IsOwned.Should().BeTrue(resolution.Reason);
        resolution.OwningTeamId.Should().Be(FakeDirectory.CustomerChildTeam, "the creator's business unit's default team (D-11)");
        Owner(directory).Should().Be(FakeDirectory.CustomerChildTeam);
        directory.ConditionalWrites.Should().ContainSingle(w => w.Applied && w.Fields.ContainsKey("ownerid"),
            "the owner is written conditionally, at the version read after the change");
        resolution.ParentsUnchanged.Should().BeFalse();
        resolution.UnfiledContentFollows.Should().Be(
            new Sprk.Bff.Api.Services.Communication.Access.UnfiledContentMove(FakeDirectory.ChildTeam, FakeDirectory.CustomerChildTeam),
            "its content rows that had the matter team follow it (round 90 decision)");
        resolution.DirectUnfile.Should().BeNull();
    }

    [Fact]
    public async Task ClearingTheLastContact_OfAnEmailLinkedToNothingElse_Settles_ToTheCreatorsTeam()
    {
        var directory = World()
            .WithMessageLinkedOnlyToAContact(FakeDirectory.ChildBu, FakeDirectory.ChildTeam, CommunicationType.Email)
            .WithRecordedCreator();
        var request = Unfile() with
        {
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_regardingperson"] = null },
        };

        var resolution = await Resolver(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(FakeDirectory.CustomerChildTeam, resolution.Reason);
        Owner(directory).Should().Be(FakeDirectory.CustomerChildTeam, "the regarding-only path settles the owner too");
        directory.Row("sprk_communication", MessageId).GetAttributeValue<EntityReference>("sprk_regardingperson").Should().BeNull();
        resolution.ParentsUnchanged.Should().BeFalse("the writer runs the step after a re-file for a message that moved");
    }

    [Fact]
    public async Task Unfile_ByACreatorInExternalCollaborators_FollowsThe191cRule_TheCustomersUnit()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam,
                type: CommunicationType.Email)
            .WithRecordedCreator(FakeDirectory.ChildBu)
            .WithExternalFlag(CreatorId, flagged: true)
            .WithUserInExternalCollaborators(CreatorId);
        directory.CustomerBusinessUnitSetting = FakeDirectory.CustomerChildBu.ToString();

        var resolution = await Resolver(directory).ReparentAsync(Unfile(), Applies(directory, Unfile()), CancellationToken.None);

        resolution.OwningTeamId.Should().Be(FakeDirectory.CustomerChildTeam, "never the External Collaborators unit's team");
        Owner(directory).Should().NotBe(FakeDirectory.ExternalCollaboratorsTeam);
    }

    // ── Must not fire: what it leaves ────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> ParentsItMayNotLeaveLooser => new()
    {
        "secure", "restricted", "owned by the Secure team", "flagged secure in an ordinary unit", "secure flag empty (task 150)",
    };

    [Theory]
    [MemberData(nameof(ParentsItMayNotLeaveLooser))]
    public async Task Unfile_FromASecureOrRestrictedParent_KeepsTheFormerOwner(string parent)
    {
        var matter = parent switch
        {
            "secure" => SecureMatterId,
            "restricted" => RestrictedMatterId,
            "flagged secure in an ordinary unit" => FlaggedOrdinaryMatterId,
            "secure flag empty (task 150)" => MaskedFlagMatterId,
            _ => SecureOwnedUnflaggedMatterId,
        };
        // The message is still owned in an ordinary unit (a stale owner), so only the parent can keep it.
        var directory = World().WithMessageFiledUnder(matter, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email)
            .WithRecordedCreator();
        var request = Unfile() with { SecureExitCaller = CallerHoldingFullAccess() };

        var resolution = await Resolver(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.Reason.Should().Contain("keeps its former owner").And.Contain(matter.ToString("D"));
        Owner(directory).Should().Be(FakeDirectory.ChildTeam);
        directory.ConditionalWrites.Should().BeEmpty();
        directory.Assignments.Should().BeEmpty();
    }

    public static TheoryData<string> MessagesHeldSecureOrRestricted => new() { "owned by the Secure team", "flagged secure", "restricted", "no owning unit" };

    [Theory]
    [MemberData(nameof(MessagesHeldSecureOrRestricted))]
    public async Task Unfile_OfAMessageItselfSecureOrRestricted_KeepsTheFormerOwner(string held)
    {
        var directory = held == "owned by the Secure team"
            ? World().WithMessageFiledUnder(MatterId, FakeDirectory.SecureBu, FakeDirectory.SecureNamedTeam, type: CommunicationType.Email)
            : World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email);
        directory.WithRecordedCreator();
        if (held == "flagged secure")
            directory.Write("sprk_communication", MessageId, new Dictionary<string, object?> { ["sprk_issecure"] = true });
        if (held == "restricted")
            directory.Write("sprk_communication", MessageId, new Dictionary<string, object?> { ["sprk_accesspermission"] = new OptionSetValue(Restricted) });
        if (held == "no owning unit")
            directory.Write("sprk_communication", MessageId, new Dictionary<string, object?> { ["owningbusinessunit"] = null });
        var ownerBefore = Owner(directory);

        var resolution = await Resolver(directory).ReparentAsync(Unfile(), Applies(directory, Unfile()), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.Reason.Should().Contain("the communication itself");
        Owner(directory).Should().Be(ownerBefore);
        directory.ConditionalWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("faults")]
    [InlineData("does not come back")]
    public async Task Unfile_WhenAParentItLeavesCannotBeRead_KeepsTheFormerOwner(string failure)
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email)
            .WithRecordedCreator();
        directory.BeforeAnswer = query =>
        {
            // Only the read of what the message leaves (it alone selects the Access Permission of a matter here).
            if (query.EntityName != "sprk_matter" || !query.ColumnSet.Columns.Contains("sprk_accesspermission"))
                return;
            if (failure == "faults")
                throw new InvalidOperationException("Test: the parent read faulted.");
            query.Criteria.Conditions[0].Values[0] = Guid.NewGuid();
        };

        var resolution = await Resolver(directory).ReparentAsync(Unfile(), Applies(directory, Unfile()), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse("the unfile is never refused for it");
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        Owner(directory).Should().Be(FakeDirectory.ChildTeam);
        resolution.Reason.Should().Contain("could not be read");
        directory.ConditionalWrites.Should().BeEmpty();
        _log.Entries.Should().Contain(e => e.Event.Id == RecordOwnershipResolver.UnfileKeepsOwnerEvent.Id);
    }

    // ── Must not fire: still linked after the change (adversarial pass 1) ─────────────────────────────────────────────────

    [Fact]
    public async Task ClearingOneOfTwoLinks_LeavesTheEmailLinked_SoItIsNotUnfiled_AndKeepsItsOwner()
    {
        var directory = World()
            .WithMessageLinkedOnlyToAContact(FakeDirectory.ChildBu, FakeDirectory.ChildTeam, CommunicationType.Email)
            .WithRecordedCreator();
        directory.Write("sprk_communication", MessageId, new Dictionary<string, object?>
        {
            ["sprk_regardingorganization"] = new EntityReference("sprk_organization", Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000b1")),
        });
        var request = Unfile() with
        {
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_regardingperson"] = null },
        };

        var resolution = await Resolver(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        Owner(directory).Should().Be(FakeDirectory.ChildTeam, "it is still linked to an organization: not unfiled");
        directory.ConditionalWrites.Should().BeEmpty();
        directory.Row("sprk_communication", MessageId).GetAttributeValue<EntityReference>("sprk_regardingperson").Should().BeNull();
    }

    [Fact]
    public async Task ClearingTheMatter_OfAnEmailStillLinkedToAContact_IsNotAnUnfile_AndKeepsItsOwner()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email)
            .WithRecordedCreator();
        directory.Write("sprk_communication", MessageId, new Dictionary<string, object?>
        {
            ["sprk_regardingperson"] = new EntityReference("contact", Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000b2")),
        });

        var resolution = await Resolver(directory).ReparentAsync(Unfile(), Applies(directory, Unfile()), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.Reason.Should().Contain("still linked");
        Owner(directory).Should().Be(FakeDirectory.ChildTeam);
        directory.ConditionalWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearingTheLastContact_WhenTheSettleFails_KeepsTheChangeAndTheFormerOwner_NeverRefused()
    {
        var directory = World()
            .WithMessageLinkedOnlyToAContact(FakeDirectory.ChildBu, FakeDirectory.ChildTeam, CommunicationType.Email)
            .WithRecordedCreator();
        directory.AssignmentFault = new InvalidOperationException("Test: the owner write faulted.");
        var request = Unfile() with
        {
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_regardingperson"] = null },
        };

        var resolution = await Resolver(directory).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.ParentsUnchanged.Should().BeFalse("the change was written; the writer still runs the step after a re-file");
        Owner(directory).Should().Be(FakeDirectory.ChildTeam);
        directory.Row("sprk_communication", MessageId).GetAttributeValue<EntityReference>("sprk_regardingperson").Should().BeNull();
        _log.Entries.Should().Contain(e => e.Event.Id == RecordOwnershipResolver.UnfileKeepsOwnerEvent.Id && e.Level == LogLevel.Warning
                                           && e.Message.Contains(MessageId.ToString()));
    }

    // ── The content-row mark (round 90, F1-2; scoped re-check F2/F3) ────────────────────────────────────────────────────

    [Fact]
    public async Task Unfile_WhoseOwnerWriteLandedButReportedAFailure_StillTakesItsContentRows()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email)
            .WithRecordedCreator();
        directory.BeforeWrite = (w, fields) =>
        {
            if (fields.ContainsKey("ownerid"))
            {
                w.AssignmentFault = new InvalidOperationException("Test: the response to the owner write was lost.");
                w.AssignmentLandsBeforeFault = true;
            }
        };

        var resolution = await Resolver(directory).ReparentAsync(Unfile(), Applies(directory, Unfile()), CancellationToken.None);

        Owner(directory).Should().Be(FakeDirectory.CustomerChildTeam, resolution.Reason);
        resolution.OwningTeamId.Should().Be(FakeDirectory.CustomerChildTeam);
        resolution.UnfiledContentFollows.Should().Be(
            new Sprk.Bff.Api.Services.Communication.Access.UnfiledContentMove(FakeDirectory.ChildTeam, FakeDirectory.CustomerChildTeam),
            "the move landed, so its content rows follow it");
    }

    [Fact]
    public async Task Unfile_SettledFromAnotherWritersFiling_CarriesNoContentMark()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email)
            .WithRecordedCreator();
        var changed = false;
        var refiledByAnother = false;
        directory.BeforeAnswer = query =>
        {
            // After this call's change, before its settle reads the row: another writer files it under the secure matter.
            if (changed && !refiledByAnother && query.EntityName == "sprk_communication" && query.ColumnSet.AllColumns)
            {
                refiledByAnother = true;
                directory.Write("sprk_communication", MessageId, new Dictionary<string, object?>
                {
                    ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatterId),
                });
            }
        };
        Func<CancellationToken, Task> applies = async ct =>
        {
            await Applies(directory, Unfile())(ct);
            changed = true;
        };

        var resolution = await Resolver(directory).ReparentAsync(Unfile(), applies, CancellationToken.None);

        refiledByAnother.Should().BeTrue("the concurrent filing must land for this test to mean anything");
        resolution.UnfiledContentFollows.Should().BeNull(
            "the owner came from another writer's filing; no step after this re-file moves content rows for it");
    }

    // ── Must not fire: owned by a person (round 90 decision, pass-2 F1-3) ───────────────────────────────────────────────

    [Fact]
    public async Task Unfile_OfAMessageOwnedByAPerson_KeepsThatOwner_NeverItsCreatorsWholeTeam()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email)
            .WithRecordedCreator();
        var person = new EntityReference("systemuser", FakeDirectory.CallerUserId);
        directory.Write("sprk_communication", MessageId, new Dictionary<string, object?>
        {
            ["ownerid"] = person,
            ["owninguser"] = person,
            ["owningteam"] = null,
        });

        var resolution = await Resolver(directory).ReparentAsync(Unfile(), Applies(directory, Unfile()), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse(resolution.Reason);
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.Reason.Should().Contain("owned by a person");
        directory.Row("sprk_communication", MessageId).GetAttributeValue<EntityReference>("owninguser")?.Id
            .Should().Be(FakeDirectory.CallerUserId);
        Owner(directory).Should().BeNull("never the creator's business-unit team");
        directory.ConditionalWrites.Should().BeEmpty();
        directory.Assignments.Should().BeEmpty();
    }

    // ── Must not fire: who created it ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unfile_WithNoRecordedCreator_KeepsTheFormerOwner_NeverFallingBackToCreatedBy()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email)
            .CreatedBy(FakeDirectory.CallerUserId);

        var resolution = await Resolver(directory).ReparentAsync(Unfile(), Applies(directory, Unfile()), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.Reason.Should().Contain("creator is not recorded");
        Owner(directory).Should().Be(FakeDirectory.ChildTeam, "createdby (the caller, in the general unit) is never used");
        directory.ConditionalWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("unknown")]
    [InlineData("external, no customer unit configured")]
    public async Task Unfile_WhenTheCreatorCannotBeResolved_KeepsTheFormerOwner_LoggedWithIdsOnly(string creator)
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type: CommunicationType.Email);
        directory.Write("sprk_communication", MessageId, new Dictionary<string, object?>
        {
            [RecordCreatorPerson.Column] = new EntityReference("systemuser", CreatorId),
        });
        if (creator == "disabled")
            directory.WithDisabledUser(CreatorId, CreatorOid, FakeDirectory.CustomerChildBu);
        if (creator.StartsWith("external", StringComparison.Ordinal))
        {
            directory.WithUser(CreatorId, CreatorOid, FakeDirectory.ChildBu).WithExternalFlag(CreatorId, flagged: true)
                .WithUserInExternalCollaborators(CreatorId);
            directory.CustomerBusinessUnitSetting = null;
        }

        var resolution = await Resolver(directory).ReparentAsync(Unfile(), Applies(directory, Unfile()), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse();
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        Owner(directory).Should().Be(FakeDirectory.ChildTeam);
        directory.ConditionalWrites.Should().BeEmpty();
        _log.Entries.Should().Contain(e => e.Event.Id == RecordOwnershipResolver.UnfileKeepsOwnerEvent.Id
                                           && e.Message.Contains(CreatorId.ToString()));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Guid? Owner(FakeDirectory directory) =>
        directory.Row("sprk_communication", MessageId).GetAttributeValue<EntityReference>("owningteam")?.Id;

    private static FakeDirectory World() => FakeDirectory.Standard()
        .WithOrdinaryRoot("sprk_matter", MatterId)
        .WithSecureRoot("sprk_matter", SecureMatterId)
        .WithRecord("sprk_matter", RestrictedMatterId, FakeDirectory.ChildBu, isSecure: false, owningTeam: FakeDirectory.ChildTeam,
            extra: new() { ["sprk_accesspermission"] = new OptionSetValue(Restricted) })
        .WithRecord("sprk_matter", SecureOwnedUnflaggedMatterId, FakeDirectory.SecureBu, isSecure: false,
            owningTeam: FakeDirectory.SecureNamedTeam)
        .WithRecord("sprk_matter", FlaggedOrdinaryMatterId, FakeDirectory.ChildBu, isSecure: true, owningTeam: FakeDirectory.ChildTeam)
        .WithMaskedSecureFlag("sprk_matter", MaskedFlagMatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);

    /// <summary>The unfile a writer sends: the matter cleared, for a person, with the unfiled-communication rule.</summary>
    private static RecordReparent Unfile() => new()
    {
        EntityLogicalName = "sprk_communication",
        RecordId = MessageId,
        ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_regardingmatter"] = null },
        CallerSystemUserId = FakeDirectory.CallerUserId,
        WhenUnfiled = UnfiledOwnership.UnfiledCommunication,
    };

    private static Func<CancellationToken, Task> Applies(FakeDirectory directory, RecordReparent request) =>
        _ =>
        {
            var written = request.ParentChanges.ToDictionary(change => change.Key, change => (object?)change.Value);
            directory.Write(request.EntityLogicalName, request.RecordId, written);
            return Task.CompletedTask;
        };

    private static Sprk.Bff.Api.Services.Access.SecureRemovalCaller CallerHoldingFullAccess() =>
        new(_ => Task.FromResult<Guid?>(FakeDirectory.CallerUserId),
            (_, _) => Task.FromResult(
                Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write | Spaarke.Dataverse.AccessRights.Append
                | Spaarke.Dataverse.AccessRights.AppendTo | Spaarke.Dataverse.AccessRights.Share | Spaarke.Dataverse.AccessRights.Delete));

    private RecordOwnershipResolver Resolver(FakeDirectory directory) =>
        directory.Resolver(logger: _log, directMessageOwnerTeam: DirectMessageOwnerTeamDouble.Unconfigured());

    private sealed class RecordingLogger : ILogger<RecordOwnershipResolver>
    {
        public List<(LogLevel Level, EventId Event, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}

/// <summary>uac-r2 task 197a: the message's recorded creator person.</summary>
internal static class UnfileOwnerWorldExtensions
{
    private static readonly Guid MessageId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b3");
    private static readonly Guid CreatorId = Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000c1");
    private static readonly Guid CreatorOid = Guid.Parse("d1d1d1d1-0197-4197-8197-0000000000c2");

    /// <summary>A creator in <paramref name="businessUnit"/> (the customer's Litigation unit by default), recorded as the
    /// message's <c>sprk_createdbyperson</c>.</summary>
    public static OwnershipDirectory WithRecordedCreator(this OwnershipDirectory directory, Guid? businessUnit = null)
    {
        directory.WithUser(CreatorId, CreatorOid, businessUnit ?? OwnershipDirectory.CustomerChildBu);
        directory.Write("sprk_communication", MessageId, new Dictionary<string, object?>
        {
            [RecordCreatorPerson.Column] = new EntityReference("systemuser", CreatorId),
        });
        return directory;
    }
}
