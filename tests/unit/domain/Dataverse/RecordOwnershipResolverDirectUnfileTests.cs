using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using FakeDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3b: the unfile of a communication for a writer that chose
/// <see cref="UnfiledOwnership.UnfiledCommunication"/>. An eligible Direct message (a Message with a valid stamped pair, in a
/// thread typed Direct that is not the per-user master thread) is decided BEFORE the change is written: the lookup resolves → the
/// change is written, no owner is written here, and the answer carries the move for the writer; the lookup refuses → refused
/// (<see cref="RecordOwnerRefusal.DirectTeamUnusable"/>, 409); the lookup faults → refused
/// (<see cref="RecordOwnerRefusal.DirectTeamUnavailable"/>, 503); no Direct setting → today's answer, from configuration alone.
/// Every other unfiled communication is Unchanged pending #2722, and a re-file to a record follows the record.
/// </summary>
/// <remarks>The REAL <see cref="RecordOwnershipResolver"/> over <see cref="FakeDirectory"/>, with the REAL
/// <see cref="DirectMessageOwnerTeam"/> over <see cref="DirectMessageOwnerTeamDouble"/>.</remarks>
public class RecordOwnershipResolverDirectUnfileTests
{
    private static readonly Guid DirectUnit = DirectMessageOwnerTeamDouble.DirectUnitId;
    private static readonly Guid DirectTeam = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b1");
    private static readonly Guid DirectUnitDefaultTeam = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b2");
    private static readonly Guid MessageId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b3");
    private static readonly Guid ThreadId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b4");
    private static readonly Guid MatterId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b5");
    private static readonly Guid SecureMatterId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b6");
    private static readonly Guid OtherMatterId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b7");
    private static readonly Guid Sender = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b8");

    private const int DirectThreadType = 100000001;
    private const int RecordThreadType = 100000000;

    private readonly RecordingLogger _log = new();

    // ── Must fire ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unfile_OfADirectMessage_FromAnOrdinaryRecord_WritesTheChange_WritesNoOwner_AndCarriesTheMoveForTheWriter()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var applied = false;

        var resolution = await Resolver(directory, Resolved()).ReparentAsync(Unfile(), Applies(directory, () => applied = true),
            CancellationToken.None);

        applied.Should().BeTrue();
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.DirectUnfile.Should().Be(new DirectUnfileDue(ThreadId, LeavesSecureOwner: false));
        directory.Assignments.Should().BeEmpty("the writer's step moves the message after the change, never the resolver");
        directory.ConditionalWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Unfile_OfADirectMessage_FromASecureRecord_CarriesTheMove_LeavingTheContentRowsToTheSecureChildReconcile()
    {
        var directory = World().WithMessageFiledUnder(SecureMatterId, FakeDirectory.SecureBu, FakeDirectory.SecureNamedTeam);

        var resolution = await Resolver(directory, Resolved()).ReparentAsync(
            Unfile() with { SecureExitCaller = CallerHoldingFullAccess() }, Applies(directory), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.DirectUnfile.Should().Be(new DirectUnfileDue(ThreadId, LeavesSecureOwner: true));
        resolution.SecureExitPermitted.Should().NotBeNull("the F3 permission still travels with the answer");
        directory.Assignments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task Unfile_OfADirectMessage_WithOnlyOneSettingSet_IsRefused409_BeforeAnythingIsWritten(string onlySet)
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var applied = false;

        var resolution = await Resolver(directory, DirectMessageOwnerStepTests.PartiallyConfigured(onlySet))
            .ReparentAsync(Unfile(), Applies(directory, () => applied = true), CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.DirectTeamUnusable);
        resolution.Reason.Should().Contain(DirectMessageOwnerTeamRefusal.PartiallyConfigured);
        applied.Should().BeFalse("an unfile that cannot reach the Direct team is refused before the change");
        directory.Assignments.Should().BeEmpty();
        _log.Entries.Should().Contain(e => e.Event.Id == DirectMessageOwnerStep.UnfileRefusedEvent.Id && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Unfile_OfADirectMessage_WhenTheLookupRefuses_IsRefused409_NeverADefaultTeam()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam).WithUserInDirectUnit(Sender).Lookup();
        var applied = false;

        var resolution = await Resolver(directory, lookup).ReparentAsync(Unfile(), Applies(directory, () => applied = true),
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.DirectTeamUnusable);
        resolution.OwningTeamId.Should().BeNull();
        applied.Should().BeFalse();
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Unfile_OfADirectMessage_WhenTheLookupFaults_IsRefused503_BeforeAnythingIsWritten()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        lookup.Faults = true;
        var applied = false;

        var resolution = await Resolver(directory, lookup.Lookup()).ReparentAsync(Unfile(), Applies(directory, () => applied = true),
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.DirectTeamUnavailable);
        applied.Should().BeFalse();
        directory.Assignments.Should().BeEmpty();
    }

    // ── Today's answer ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unfile_OfADirectMessage_WithNoDirectSetting_IsUnchanged_AndReadsNothingMore()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);

        var resolution = await Resolver(directory, DirectMessageOwnerTeamDouble.Unconfigured()).ReparentAsync(Unfile(),
            Applies(directory), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.DirectUnfile.Should().BeNull();
        directory.QueriedEntities.Count(e => e == "sprk_communicationthread").Should().Be(1, "only the reparent's own thread read");
        directory.Assignments.Should().BeEmpty();
    }

    // ── Must not fire ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unfile_OfAnEmail_IsUnchangedPending2722_NoOwnerWritten_AndNeverRefused()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam,
            type: CommunicationType.Email);

        var resolution = await Resolver(directory, DirectMessageOwnerStepTests.PartiallyConfigured(
            DirectMessageOwnerTeam.BusinessUnitNameConfigKey)).ReparentAsync(Unfile(), Applies(directory), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.DirectUnfile.Should().BeNull();
        resolution.Reason.Should().Contain("#2722");
        directory.Assignments.Should().BeEmpty("not to the creator, not to the acting user's team");
        directory.ConditionalWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Unfile_OfAMessageWithAnEmptyRecipient_IsUnchanged_AndNeverRefused()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, withRecipient: false);

        var resolution = await Resolver(directory, DirectMessageOwnerStepTests.PartiallyConfigured(
            DirectMessageOwnerTeam.OwnerTeamNameConfigKey)).ReparentAsync(Unfile(), Applies(directory), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.DirectUnfile.Should().BeNull();
        directory.Assignments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(RecordThreadType, false)]
    [InlineData(DirectThreadType, true)]
    public async Task Unfile_IntoAThreadNotTypedDirect_OrThePerUserMaster_IsUnchanged(int threadType, bool isDefault)
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam,
            threadType: threadType, isDefaultThread: isDefault);

        var resolution = await Resolver(directory, Resolved()).ReparentAsync(Unfile(), Applies(directory), CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.DirectUnfile.Should().BeNull();
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Unfile_ForAWriterThatKeepsTheCreator_NeverAsksTheDirectRule()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var applied = false;

        var resolution = await Resolver(directory, DirectMessageOwnerStepTests.PartiallyConfigured(
                DirectMessageOwnerTeam.BusinessUnitNameConfigKey))
            .ReparentAsync(Unfile() with { WhenUnfiled = UnfiledOwnership.KeepCreator }, Applies(directory, () => applied = true),
                CancellationToken.None);

        applied.Should().BeTrue();
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.DirectUnfile.Should().BeNull();
    }

    [Fact]
    public async Task Refile_OfADirectMessage_ToAnotherRecord_FollowsTheRecord()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam)
            .WithRecord("sprk_matter", OtherMatterId, FakeDirectory.CustomerChildBu, isSecure: false,
                owningTeam: FakeDirectory.CustomerChildTeam);
        var request = Unfile() with
        {
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherMatterId),
            },
        };

        var resolution = await Resolver(directory, Resolved()).ReparentAsync(request, Applies(directory, request),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(FakeDirectory.CustomerChildTeam);
        resolution.DirectUnfile.Should().BeNull();
    }

    [Fact]
    public async Task Filing_ADirectMessage_UnderASecureRecord_GoesToTheSecureTeam()
    {
        var directory = World().WithUnfiledDirectMessage(FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var request = Unfile() with
        {
            ParentChanges = new Dictionary<string, EntityReference?>
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatterId),
            },
        };

        var resolution = await Resolver(directory, Resolved()).ReparentAsync(request, Applies(directory, request),
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(FakeDirectory.SecureNamedTeam);
        resolution.DirectUnfile.Should().BeNull();
    }

    // ── Piece 3b fix round: judged on the row as the change leaves it, after F3 ──────────────────────────────────────────

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task Refile_FromAMatterToAContact_WithAPartialConfig_IsNotRefused_AndTheOwnerIsUnchanged(string onlySet)
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var request = ToAContact();
        var applied = false;

        var resolution = await Resolver(directory, DirectMessageOwnerStepTests.PartiallyConfigured(onlySet))
            .ReparentAsync(request, Applies(directory, request, () => applied = true), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse("a message re-filed to a contact is still linked to a record: no unfile");
        applied.Should().BeTrue();
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.DirectUnfile.Should().BeNull();
        directory.Assignments.Should().BeEmpty();
        _log.Entries.Should().NotContain(e => e.Event.Id == DirectMessageOwnerStep.UnfileRefusedEvent.Id);
    }

    [Fact]
    public async Task Refile_FromAMatterToAContact_WithAResolvedLookup_CarriesNoMove()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var request = ToAContact();

        var resolution = await Resolver(directory, Resolved()).ReparentAsync(request, Applies(directory, request),
            CancellationToken.None);

        resolution.DirectUnfile.Should().BeNull("no move is due, so the writer grants nothing and moves nothing");
        directory.Assignments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClearingTheMatter_OfAMessageAlsoLinkedToAContact_IsNotAnUnfile(bool resolved)
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        directory.Write("sprk_communication", MessageId, new Dictionary<string, object?>
        {
            ["sprk_regardingperson"] = new EntityReference("contact", ContactId),
        });
        var applied = false;

        var resolution = await Resolver(directory, resolved
                ? Resolved()
                : DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.BusinessUnitNameConfigKey))
            .ReparentAsync(Unfile(), Applies(directory, () => applied = true), CancellationToken.None);

        resolution.IsRefused.Should().BeFalse();
        applied.Should().BeTrue();
        resolution.DirectUnfile.Should().BeNull();
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Unfile_OutOfASecureRecord_ByACallerWithoutF3_IsForbidden_BeforeTheDirectTeamIsAsked()
    {
        var directory = World().WithMessageFiledUnder(SecureMatterId, FakeDirectory.SecureBu, FakeDirectory.SecureNamedTeam);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        lookup.Faults = true;
        var applied = false;

        var resolution = await Resolver(directory, lookup.Lookup()).ReparentAsync(
            Unfile() with { SecureExitCaller = CallerHoldingNoAccess() }, Applies(directory, () => applied = true),
            CancellationToken.None);

        resolution.IsForbidden.Should().BeTrue("a caller without F3 is told that first, never a 503 then a 403");
        resolution.RefusalCode.Should().NotBe(RecordOwnerRefusal.DirectTeamUnavailable);
        lookup.Reads.Should().Be(0, "the Direct team is not asked for a change the caller may not make");
        applied.Should().BeFalse();
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Unfile_OfADirectMessage_WhenTheSecureRecordUnitCannotBeDecided_IsRefused409_BeforeAnythingIsWritten()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var secureUnitReads = 0;
        directory.BeforeAnswer = query =>
        {
            if (query.EntityName == "businessunit"
                && query.Criteria.Conditions.Any(c => c.AttributeName == "name" && Equals(c.Values.FirstOrDefault(), FakeDirectory.SecureBuName))
                && ++secureUnitReads == 2)
            {
                // A second unit takes the Secure Record name between the F3 check and the Direct decision.
                directory.WithBusinessUnit(Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003ba"), FakeDirectory.SecureBuName);
            }
        };
        var applied = false;

        var resolution = await Resolver(directory, Resolved()).ReparentAsync(Unfile(), Applies(directory, () => applied = true),
            CancellationToken.None);

        secureUnitReads.Should().Be(2, "the F3 check, then the Direct decision");
        resolution.IsRefused.Should().BeTrue("an undecidable Secure Record unit is never guessed");
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.DirectTeamUnusable);
        resolution.DirectUnfile.Should().BeNull();
        applied.Should().BeFalse();
        directory.Assignments.Should().BeEmpty();
    }

    // ── Pass 2 (F1): clearing the last link that files nothing is an unfile too ───────────────────────────────────────────

    [Fact]
    public async Task ClearingTheLastContact_OfADirectMessageAnOrdinaryTeamOwns_IsAnUnfile_DecidedBeforeTheWrite()
    {
        var directory = World().WithMessageLinkedOnlyToAContact(FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var request = ClearTheContact();
        var applied = false;

        var resolution = await Resolver(directory, Resolved()).ReparentAsync(request, Applies(directory, request, () => applied = true),
            CancellationToken.None);

        applied.Should().BeTrue();
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.DirectUnfile.Should().Be(new DirectUnfileDue(ThreadId, LeavesSecureOwner: false));
        resolution.ParentsUnchanged.Should().BeFalse("the writer runs the step after a re-file for a message that moves");
        directory.Assignments.Should().BeEmpty("the writer's step moves the message after the change, never the resolver");
    }

    [Fact]
    public async Task ClearingTheLastContact_OfADirectMessageTheSecureTeamOwns_LeavesItsContentRowsToTheSecureChildReconcile()
    {
        var directory = World().WithMessageLinkedOnlyToAContact(FakeDirectory.SecureBu, FakeDirectory.SecureNamedTeam);
        var request = ClearTheContact();

        var resolution = await Resolver(directory, Resolved()).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.DirectUnfile.Should().Be(new DirectUnfileDue(ThreadId, LeavesSecureOwner: true));
        resolution.ParentsUnchanged.Should().BeFalse();
    }

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task ClearingTheLastContact_WithOnlyOneSettingSet_IsRefused409_BeforeAnythingIsWritten(string onlySet)
    {
        var directory = World().WithMessageLinkedOnlyToAContact(FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var request = ClearTheContact();
        var applied = false;

        var resolution = await Resolver(directory, DirectMessageOwnerStepTests.PartiallyConfigured(onlySet))
            .ReparentAsync(request, Applies(directory, request, () => applied = true), CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.DirectTeamUnusable);
        applied.Should().BeFalse();
        directory.Row("sprk_communication", MessageId).GetAttributeValue<EntityReference>("sprk_regardingperson")?.Id
            .Should().Be(ContactId, "nothing was written");
        directory.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearingTheLastContact_WhenTheLookupFaults_IsRefused503_BeforeAnythingIsWritten()
    {
        var directory = World().WithMessageLinkedOnlyToAContact(FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        lookup.Faults = true;
        var request = ClearTheContact();
        var applied = false;

        var resolution = await Resolver(directory, lookup.Lookup()).ReparentAsync(request, Applies(directory, request, () => applied = true),
            CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.DirectTeamUnavailable);
        applied.Should().BeFalse();
    }

    [Theory]
    [InlineData(CommunicationType.Email, true)]
    [InlineData(CommunicationType.Message, false)]
    public async Task ClearingAContact_OfAMessageThatIsNotAnEligibleDirectMessage_WritesNoOwner_AndReadsNothingMore(
        CommunicationType type, bool withRecipient)
    {
        var directory = World().WithMessageLinkedOnlyToAContact(FakeDirectory.ChildBu, FakeDirectory.ChildTeam, type, withRecipient);
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam);
        lookup.Faults = true;
        var request = ClearTheContact();
        var applied = false;

        var resolution = await Resolver(directory, lookup.Lookup()).ReparentAsync(request, Applies(directory, request, () => applied = true),
            CancellationToken.None);

        applied.Should().BeTrue();
        resolution.IsRefused.Should().BeFalse();
        resolution.DirectUnfile.Should().BeNull();
        resolution.ParentsUnchanged.Should().BeTrue("nothing it is filed under moved and no move is due");
        lookup.Reads.Should().Be(0, "a communication that is not an eligible Direct message costs no lookup read");
        directory.QueriedEntities.Count(e => e == "sprk_communicationthread").Should().Be(1, "only the reparent's own thread read");
        directory.Assignments.Should().BeEmpty();
        directory.ConditionalWrites.Should().BeEmpty();
    }

    // ── Pass 2 (K2): every column the rule reads, as the write leaves it ─────────────────────────────────────────────────

    [Theory]
    [InlineData(DirectMessageOwnerTeam.BusinessUnitNameConfigKey)]
    [InlineData(DirectMessageOwnerTeam.OwnerTeamNameConfigKey)]
    public async Task AWriteThatStampsTheRecipient_AndClearsTheMatter_IsJudgedEligible_SoAPartialConfigRefusesIt(string onlySet)
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, withRecipient: false);
        var request = Unfile() with { UnfileRuleColumnsWritten = StampsTheRecipient() };
        var applied = false;

        var resolution = await Resolver(directory, DirectMessageOwnerStepTests.PartiallyConfigured(onlySet))
            .ReparentAsync(request, Applies(directory, request, () => applied = true), CancellationToken.None);

        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.DirectTeamUnusable, "the row the write leaves carries a valid pair");
        applied.Should().BeFalse();
    }

    [Fact]
    public async Task AWriteThatStampsTheRecipient_AndClearsTheMatter_CarriesTheMove_ForTheRowItLeaves()
    {
        var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam, withRecipient: false);
        var request = Unfile() with { UnfileRuleColumnsWritten = StampsTheRecipient() };

        var resolution = await Resolver(directory, Resolved()).ReparentAsync(request, Applies(directory, request), CancellationToken.None);

        resolution.DirectUnfile.Should().Be(new DirectUnfileDue(ThreadId, LeavesSecureOwner: false));
        DirectMessageOwnerStep.WhyNotAnEligibleUnfile(directory.Row("sprk_communication", MessageId), ThreadId)
            .Should().BeNull("the judgement before the write is the one made of the row after it");
    }

    public static TheoryData<string> WritesThatLeaveNoEligibleMessage => new() { "recipient cleared", "type set to Email", "sender unreadable" };

    [Theory]
    [MemberData(nameof(WritesThatLeaveNoEligibleMessage))]
    public async Task AWriteThatClearsTheMatter_AndLeavesNoEligibleMessage_IsNotRefused_AndCarriesNoMove(string write)
    {
        var columns = write switch
        {
            "recipient cleared" => new Dictionary<string, object?> { ["sprk_directrecipient"] = null },
            "type set to Email" => new Dictionary<string, object?> { ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email) },
            _ => new Dictionary<string, object?> { ["sprk_sentby"] = null },
        };
        foreach (var resolved in new[] { true, false })
        {
            var directory = World().WithMessageFiledUnder(MatterId, FakeDirectory.ChildBu, FakeDirectory.ChildTeam);
            var request = Unfile() with { UnfileRuleColumnsWritten = columns };
            var applied = false;

            var resolution = await Resolver(directory, resolved
                    ? Resolved()
                    : DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.OwnerTeamNameConfigKey))
                .ReparentAsync(request, Applies(directory, request, () => applied = true), CancellationToken.None);

            resolution.IsRefused.Should().BeFalse($"{write}: the row the write leaves is no eligible Direct message");
            applied.Should().BeTrue();
            resolution.DirectUnfile.Should().BeNull();
            DirectMessageOwnerStep.WhyNotAnEligibleUnfile(directory.Row("sprk_communication", MessageId), ThreadId)
                .Should().NotBeNull("the judgement before the write is the one made of the row after it");
        }
    }

    [Fact]
    public void UnfileRuleColumnsWrittenIn_ReadsTheTypeAndThePair_InEverySpelling_AndAClearOrAnUnreadableValueAsNull()
    {
        var recipient = Guid.NewGuid();
        var sender = Guid.NewGuid();

        var written = RecordReparent.UnfileRuleColumnsWrittenIn("sprk_communication", new Dictionary<string, object?>
        {
            ["sprk_DirectRecipient@odata.bind"] = $"/systemusers({recipient:D})",
            ["sprk_sentby"] = new EntityReference("systemuser", sender),
            ["sprk_communicationtype"] = System.Text.Json.JsonSerializer.SerializeToElement((int)CommunicationType.Message),
            ["sprk_name"] = "x",
        });

        written.Keys.Should().BeEquivalentTo("sprk_directrecipient", "sprk_sentby", "sprk_communicationtype");
        written["sprk_directrecipient"].Should().Be(new EntityReference("systemuser", recipient));
        written["sprk_sentby"].Should().Be(new EntityReference("systemuser", sender));
        ((OptionSetValue)written["sprk_communicationtype"]!).Value.Should().Be((int)CommunicationType.Message);

        var cleared = RecordReparent.UnfileRuleColumnsWrittenIn("sprk_communication", new Dictionary<string, object?>
        {
            ["sprk_directrecipient"] = null,
            ["sprk_SentBy@odata.bind"] = $"/contacts({sender:D})",
            ["sprk_communicationtype"] = "not a number",
        });
        cleared.Should().HaveCount(3).And.OnlyContain(entry => entry.Value == null);

        RecordReparent.UnfileRuleColumnsWrittenIn("sprk_event", new Dictionary<string, object?> { ["sprk_sentby"] = null })
            .Should().BeEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> StampsTheRecipient() => new()
    {
        ["sprk_directrecipient"] = new EntityReference("systemuser", Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b9")),
    };

    /// <summary>The second step of a re-file to a contact: the contact cleared, nothing else.</summary>
    private static RecordReparent ClearTheContact() => Unfile() with
    {
        ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_regardingperson"] = null },
    };

    private static readonly Guid ContactId = DirectUnfileWorldExtensions.ContactId;

    /// <summary>The re-file from the matter to a contact: the matter cleared and the contact set in one change.</summary>
    private static RecordReparent ToAContact() => Unfile() with
    {
        RegardingLinksSet = new Dictionary<string, EntityReference>
        {
            ["sprk_regardingperson"] = new EntityReference("contact", ContactId),
        },
    };

    private static Sprk.Bff.Api.Services.Access.SecureRemovalCaller CallerHoldingNoAccess() =>
        new(_ => Task.FromResult<Guid?>(FakeDirectory.CallerUserId),
            (_, _) => Task.FromResult(Spaarke.Dataverse.AccessRights.Read));

    /// <summary>The standard directory plus the Direct business unit and its teams, an ordinary and a secure matter, and the
    /// message's thread.</summary>
    private static FakeDirectory World() => FakeDirectory.Standard()
        .WithBusinessUnit(DirectUnit, DirectMessageOwnerTeamDouble.BusinessUnitName, parent: FakeDirectory.GeneralBu)
        .WithTeam(DirectUnitDefaultTeam, DirectUnit, isDefault: true, teamType: 0, DirectMessageOwnerTeamDouble.BusinessUnitName)
        .WithTeam(DirectTeam, DirectUnit, isDefault: false, teamType: 0, DirectMessageOwnerTeamDouble.OwnerTeamName)
        .WithOrdinaryRoot("sprk_matter", MatterId)
        .WithSecureRoot("sprk_matter", SecureMatterId);

    /// <summary>The unfile a writer sends: the matter cleared, for a person, with the unfiled-communication rule.</summary>
    private static RecordReparent Unfile() => new()
    {
        EntityLogicalName = "sprk_communication",
        RecordId = MessageId,
        ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_regardingmatter"] = null },
        CallerSystemUserId = FakeDirectory.CallerUserId,
        WhenUnfiled = UnfiledOwnership.UnfiledCommunication,
    };

    private static Func<CancellationToken, Task> Applies(FakeDirectory directory, Action? alsoDone = null) =>
        Applies(directory, Unfile(), alsoDone);

    private static Func<CancellationToken, Task> Applies(FakeDirectory directory, RecordReparent request, Action? alsoDone = null) =>
        _ =>
        {
            var written = request.ParentChanges.ToDictionary(change => change.Key, change => (object?)change.Value);
            foreach (var (column, link) in request.RegardingLinksSet)
                written[column] = link;
            foreach (var (column, value) in request.UnfileRuleColumnsWritten)
                written[column] = value;
            directory.Write(request.EntityLogicalName, request.RecordId, written);
            alsoDone?.Invoke();
            return Task.CompletedTask;
        };

    private static Sprk.Bff.Api.Services.Access.SecureRemovalCaller CallerHoldingFullAccess() =>
        new(_ => Task.FromResult<Guid?>(FakeDirectory.CallerUserId),
            (_, _) => Task.FromResult(
                Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write | Spaarke.Dataverse.AccessRights.Append | Spaarke.Dataverse.AccessRights.AppendTo
                | Spaarke.Dataverse.AccessRights.Share | Spaarke.Dataverse.AccessRights.Delete));

    private RecordOwnershipResolver Resolver(FakeDirectory directory, DirectMessageOwnerTeam lookup) =>
        directory.Resolver(logger: _log, directMessageOwnerTeam: lookup);

    private static DirectMessageOwnerTeam Resolved() => new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectTeam).Lookup();

    private sealed class RecordingLogger : ILogger<RecordOwnershipResolver>
    {
        public List<(LogLevel Level, EventId Event, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}

/// <summary>Seeds the message and its thread the unfile tests ask about.</summary>
internal static class DirectUnfileWorldExtensions
{
    private static readonly Guid MessageId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b3");
    private static readonly Guid ThreadId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b4");
    private static readonly Guid Sender = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b8");
    private static readonly Guid Recipient = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b9");

    /// <summary>A message filed under <paramref name="matter"/> in its thread, owned as that record's content is.</summary>
    public static OwnershipDirectory WithMessageFiledUnder(
        this OwnershipDirectory directory, Guid matter, Guid owningBusinessUnit, Guid owningTeam,
        CommunicationType type = CommunicationType.Message, bool withRecipient = true,
        int threadType = 100000001, bool isDefaultThread = false) =>
        directory.WithThread(threadType, isDefaultThread).WithMessage(owningBusinessUnit, owningTeam, type, withRecipient, matter);

    /// <summary>A Direct message filed under nothing, in its Direct thread.</summary>
    public static OwnershipDirectory WithUnfiledDirectMessage(this OwnershipDirectory directory, Guid owningBusinessUnit, Guid owningTeam) =>
        directory.WithThread(100000001, false).WithMessage(owningBusinessUnit, owningTeam, CommunicationType.Message, true, null);

    /// <summary>The contact a message is linked to after a re-file from a record to a contact.</summary>
    public static readonly Guid ContactId = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003bb");

    /// <summary>A message in its Direct thread linked only to <see cref="ContactId"/>, still owned as the record it left owned it.</summary>
    public static OwnershipDirectory WithMessageLinkedOnlyToAContact(
        this OwnershipDirectory directory, Guid owningBusinessUnit, Guid owningTeam,
        CommunicationType type = CommunicationType.Message, bool withRecipient = true)
    {
        directory.WithThread(100000001, false).WithMessage(owningBusinessUnit, owningTeam, type, withRecipient, null);
        directory.Write("sprk_communication", MessageId, new Dictionary<string, object?>
        {
            ["sprk_regardingperson"] = new EntityReference("contact", ContactId),
        });
        return directory;
    }

    private static OwnershipDirectory WithThread(this OwnershipDirectory directory, int threadType, bool isDefault) =>
        directory.WithRecord("sprk_communicationthread", ThreadId, OwnershipDirectory.ChildBu, extra: new()
        {
            ["sprk_threadtype"] = new OptionSetValue(threadType),
            ["sprk_isdefaultthread"] = isDefault,
        });

    private static OwnershipDirectory WithMessage(
        this OwnershipDirectory directory, Guid owningBusinessUnit, Guid owningTeam, CommunicationType type, bool withRecipient, Guid? matter)
    {
        var extra = new Dictionary<string, object>
        {
            ["sprk_communicationtype"] = new OptionSetValue((int)type),
            ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", ThreadId),
            ["sprk_sentby"] = new EntityReference("systemuser", Sender),
        };
        if (withRecipient)
            extra["sprk_directrecipient"] = new EntityReference("systemuser", Recipient);
        if (matter is { } filed)
            extra["sprk_regardingmatter"] = new EntityReference("sprk_matter", filed);
        return directory.WithRecord("sprk_communication", MessageId, owningBusinessUnit, owningTeam: owningTeam, extra: extra);
    }
}
