using System.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 016 (spec FR-14, FR-25, NFR-03) — Repair mode: steps 3 (mapping), 7 (owner), 8
/// (search-index default) and 10 (I-1 stamp) of the unified create, applied to an EXISTING row, fill-only, against its
/// current values, in at most ONE conditional update and as the system principal.
/// </summary>
/// <remarks>
/// Only the Dataverse module boundary is doubled: the row store is <see cref="Directory"/> (whole-row reads, the conditional
/// update applied with its version check), the stamp sources are a <see cref="StampWorld"/>, and the owner resolver, the
/// restamper and the service are the real code. Every update the service sends is recorded in <see cref="RepairWorld.Writes"/>.
/// </remarks>
[Trait("status", "new")]
public sealed class RecordRepairModeTests
{
    private readonly RepairWorld _world = new();

    // =====================================================================================
    // Mapping (FR-02 against the row's current values)
    // =====================================================================================

    [Fact]
    public async Task AnUnmappedColumn_IsFilledFromTheMapping_AndABlankStringCountsAsNotSupplied()
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "   ", priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient);
        write.Fields["sprk_practicearea"].Should().Be(RepairWorld.MatterPracticeArea, "a blank string is not supplied (FR-02)");
        write.Fields["sprk_priorityname"].Should().Be(RepairWorld.DefaultPriority);
        Field(outcome, RecordRepairPart.Mapping, "sprk_practicearea").State.Should().Be(RecordRepairFieldState.Repaired);
    }

    [Fact]
    public async Task APresentValue_IsNeverOverwritten_EvenWhenItDiffersFromTheMapping()
    {
        var id = _world.EventUnderTheMatter(clientName: "Someone Else Ltd", practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys)
            .Should().NotContain(["sprk_clientname", "sprk_practicearea", "sprk_priorityname"]);
        var row = _world.Directory.Row("sprk_event", id);
        row.GetAttributeValue<string>("sprk_clientname").Should().Be("Someone Else Ltd");
        row.GetAttributeValue<string>("sprk_priorityname").Should().Be("Urgent");
        Field(outcome, RecordRepairPart.Mapping, "sprk_clientname").State.Should().Be(RecordRepairFieldState.Present);
    }

    [Fact]
    public async Task AMappedLookupThatWouldFileTheRowUnderARecord_IsNotWritten()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent");
        _world.MapMattersProjectOntoTheEvent();

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("sprk_regardingproject");
        Field(outcome, RecordRepairPart.Mapping, "sprk_regardingproject").ReasonCode
            .Should().Be(RecordRepairReason.MappedValueWouldRefile);
    }

    // =====================================================================================
    // Idempotency (NFR-03)
    // =====================================================================================

    [Fact]
    public async Task TwoConsecutiveRepairs_WriteNothingTheSecondTime()
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var first = await _world.RepairAsync("sprk_event", id);
        var second = await _world.RepairAsync("sprk_event", id);

        first.Status.Should().Be(RecordRepairStatus.Repaired);
        second.Status.Should().Be(RecordRepairStatus.NothingToRepair);
        second.Wrote.Should().BeFalse();
        _world.Writes.Should().HaveCount(1, "the second repair finds nothing missing and writes nothing");
    }

    [Fact]
    public async Task ARowChangedAfterItWasRead_IsNotWritten_AndNotRetried()
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "Tax", priority: "Urgent");
        _world.BeforeWrite = () => _world.Directory.Write("sprk_event", id,
            new Dictionary<string, object?> { ["sprk_clientname"] = "Set By Another Writer" });

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.RowChanged);
        outcome.Wrote.Should().BeFalse();
        _world.Writes.Should().ContainSingle().Which.Applied.Should().BeFalse();
        _world.Directory.Row("sprk_event", id).GetAttributeValue<string>("sprk_clientname").Should().Be("Set By Another Writer");
        outcome.Fields.Should().NotContain(f => f.State == RecordRepairFieldState.Repaired);
    }

    // =====================================================================================
    // Owner (FR-09; only a systemuser-owned row, never an exception)
    // =====================================================================================

    [Fact]
    public async Task AUserOwnedRow_TakesTheOwnerItsParentGives()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields["ownerid"]
            .Should().Be(new EntityReference("team", Directory.ChildTeam));
        _world.Directory.Row("sprk_event", id).GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Directory.ChildTeam);
        Field(outcome, RecordRepairPart.Owner, "ownerid").State.Should().Be(RecordRepairFieldState.Repaired);
        _world.Evictions.Should().ContainSingle().Which.Should().Be(("sprk_event", "sprk_events", id));
    }

    [Fact]
    public async Task ATeamOwnedRow_KeepsItsOwner_EvenOneTheRuleWouldNotGive()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("team", Directory.GeneralTeam), owningUnit: Directory.GeneralBu, indexed: true);

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.NothingToRepair);
        _world.Writes.Should().BeEmpty();
        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerTeamOwned);
        _world.Evictions.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnfiledCommunication_KeepsItsCreatorOwner_TheRegistryException()
    {
        var id = _world.UnfiledCommunicationOwnedBy(RepairWorld.PersonInChild);

        var outcome = await _world.RepairAsync("sprk_communication", id);

        _world.Writes.Should().BeEmpty();
        _world.Directory.Row("sprk_communication", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerKeptByException);
    }

    [Fact]
    public async Task AnUnresolvableOwner_IsLeftUnchanged_WithATypedRefusal_AndNoSearchIndexIsFilled()
    {
        // An APPLICATION-owned document filed under nothing: there is no person to act (I-6), so no owner can be decided —
        // and it is never given the application user's own team.
        var id = _world.UnfiledDocumentOwnedBy(RepairWorld.AppUser, Directory.GeneralBu);

        var outcome = await _world.RepairAsync("sprk_document", id);

        outcome.OwnerRefusal.Should().NotBeNull();
        outcome.OwnerRefusal!.Code.Should().Be(RecordOwnerRefusal.NoOwnerSource);
        Field(outcome, RecordRepairPart.Owner, "ownerid").State.Should().Be(RecordRepairFieldState.Refused);
        // UAC-r2 condition (b) F2-1: the owner refused, so the search index is not filled from the row's current unit.
        _world.Writes.Should().BeEmpty();
        Field(outcome, RecordRepairPart.SearchIndex, "sprk_searchindexname").ReasonCode
            .Should().Be(RecordRepairReason.SearchIndexOwnerUnsettled);
        Field(outcome, RecordRepairPart.SearchIndex, "sprk_ai_search_index").ReasonCode
            .Should().Be(RecordRepairReason.SearchIndexOwnerUnsettled);
        _world.Directory.Row("sprk_document", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.AppUser));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnUnfiledRowWhoseOwnersStatusCannotBeTold_IsNotReOwned_NoOwnerSource(bool readFaults)
    {
        // UAC-r2 condition (b) F3-2: the application-user check answers "unknown" (the read faults, or no row comes back) —
        // that is no person to act for (I-6), never "a person".
        var id = _world.UnfiledDocumentOwnedBy(RepairWorld.PersonInChild, Directory.ChildBu);
        if (readFaults)
            _world.SystemUserReadFault = new InvalidOperationException("Test: systemuser read faulted.");
        else
            _world.MissingSystemUsers.Add(RepairWorld.PersonInChild);

        var outcome = await _world.RepairAsync("sprk_document", id);

        outcome.OwnerRefusal.Should().NotBeNull();
        outcome.OwnerRefusal!.Code.Should().Be(RecordOwnerRefusal.NoOwnerSource);
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("ownerid");
        _world.Directory.Row("sprk_document", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
    }

    [Fact]
    public async Task AnOwnerKeptInItsUnitByTheBusinessUnitSkip_GetsNoSearchIndex()
    {
        // UAC-r2 condition (b) F2-1: a user-owned document in the GENERAL unit, filed under the matter of the CHILD unit — the
        // move is refused (trigger 2), so the owner is not settled and the general unit's index is never written.
        var id = _world.RowOf("sprk_document", Directory.GeneralBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", Directory.CallerUserId),
            ["sprk_matter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
        });

        var outcome = await _world.RepairAsync("sprk_document", id);

        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerMovesBusinessUnit);
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(["sprk_searchindexname", "sprk_ai_search_index"]);
        Field(outcome, RecordRepairPart.SearchIndex, "sprk_searchindexname").ReasonCode
            .Should().Be(RecordRepairReason.SearchIndexOwnerUnsettled);
    }

    [Fact]
    public async Task AUserOwnedRowReOwnedInThisPatch_GetsTheSearchIndexOfItsNewOwnersUnit()
    {
        // Must not fire (F2-1): the owner is settled on a team in this same patch.
        var id = _world.RowOf("sprk_document", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild),
            ["sprk_matter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
        });

        var outcome = await _world.RepairAsync("sprk_document", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam));
        write.Fields["sprk_searchindexname"].Should().Be(RepairWorld.ChildIndexName);
        Field(outcome, RecordRepairPart.SearchIndex, "sprk_searchindexname").State.Should().Be(RecordRepairFieldState.Repaired);
    }

    [Fact]
    public async Task AnOwnerInAnotherBusinessUnit_IsNotWritten_EscalationTrigger2()
    {
        // The person who owns the event sits in the GENERAL unit; the matter it is filed under is in the CHILD unit.
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("systemuser", Directory.CallerUserId), owningUnit: Directory.GeneralBu);

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().BeEmpty();
        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerMovesBusinessUnit);
    }

    // =====================================================================================
    // Search-index default (FR-10) and the I-1 stamp, fill-only
    // =====================================================================================

    [Fact]
    public async Task TheSearchIndexDefault_IsFilledOnlyWhereUnset()
    {
        var id = _world.DocumentUnderTheMatter(indexName: "kept-index", index: null);

        var outcome = await _world.RepairAsync("sprk_document", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields.Should().NotContainKey("sprk_searchindexname");
        write.Fields["sprk_ai_search_index"].Should().Be(new EntityReference("sprk_aisearchindex", RepairWorld.ChildIndex));
        Field(outcome, RecordRepairPart.SearchIndex, "sprk_searchindexname").State.Should().Be(RecordRepairFieldState.Present);
        _world.Directory.Row("sprk_document", id).GetAttributeValue<string>("sprk_searchindexname").Should().Be("kept-index");
    }

    [Fact]
    public async Task TheI1Stamp_IsAddedWhenMissing()
    {
        var id = _world.EventUnderTheCommunication(heldMatterStamp: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields["sprk_regardingmatter"]
            .Should().Be(new EntityReference("sprk_matter", RepairWorld.Matter));
        Field(outcome, RecordRepairPart.CoreAncestorStamp, "sprk_regardingmatter").State.Should().Be(RecordRepairFieldState.Repaired);
    }

    [Fact]
    public async Task AHeldI1Stamp_IsNeverOverwritten_EvenAStaleOne()
    {
        var id = _world.EventUnderTheCommunication(heldMatterStamp: RepairWorld.OtherMatter);

        await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("sprk_regardingmatter");
        _world.Directory.Row("sprk_event", id).GetAttributeValue<EntityReference>("sprk_regardingmatter").Id
            .Should().Be(RepairWorld.OtherMatter, "a stale copy is the reconciliation job's, never Repair's");
    }

    // =====================================================================================
    // System principal; the invoice-family seam (ebilling-r1 #1911)
    // =====================================================================================

    [Fact]
    public async Task Repair_RunsAsTheSystemPrincipal_NoCallerIdentity_NoWhoAmI()
    {
        typeof(RecordCreationService).GetMethod(nameof(RecordCreationService.RepairAsync),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetParameters().Select(p => p.ParameterType)
            .Should().Equal([typeof(string), typeof(Guid), typeof(CancellationToken)], "no caller client, token or principal");

        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);
        await _world.RepairAsync("sprk_event", id);

        _world.OwnerContexts.Should().NotBeEmpty();
        _world.OwnerContexts.Should().OnlyContain(c => c.CallerObjectId == null, "the owner is never resolved as a caller");
        _world.Directory.UserKeyColumns.Should().NotContain("azureactivedirectoryobjectid", "no caller is looked up by token");
    }

    [Fact]
    public async Task AnInvoiceFamilyRow_IsNotWritten_UntilTheInvoiceGuardIsOnThisBuild()
    {
        var id = _world.InvoiceUnderTheMatter();

        var outcome = await _world.RepairAsync("sprk_invoice", id);

        outcome.Status.Should().Be(RecordRepairStatus.Skipped);
        outcome.ReasonCode.Should().Be(RecordRepairReason.InvoiceFamilyGuardUnavailable);
        _world.Writes.Should().BeEmpty();
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Owner
            && f.State == RecordRepairFieldState.Skipped && f.ReasonCode == RecordRepairReason.InvoiceFamilyGuardUnavailable);
    }

    [Fact]
    public async Task ARowOutsideTheInvoiceFamily_IsWritten_TheGuardDoesNotFire()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        _world.Writes.Should().ContainSingle();
    }

    [Fact]
    public async Task ATableThisServiceDoesNotCreate_IsSkipped_WithNothingRead()
    {
        // Task 017-d2: "account" is now registered (the chat create's company table), so an unregistered platform table is used.
        var outcome = await _world.RepairAsync("phonecall", Guid.NewGuid());

        outcome.Status.Should().Be(RecordRepairStatus.Skipped);
        outcome.ReasonCode.Should().Be(RecordRepairReason.TableUnsupported);
        _world.Directory.RecordQueries.Should().BeEmpty();
    }

    // =====================================================================================
    // Review pass 1 — F1-a: a secure-flagged root is never re-owned (empty counts as flagged)
    // =====================================================================================

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task AUserOwnedSecureFlaggedRoot_KeepsItsOwner_AndGetsNoSearchIndexUntilItsOwnerIsSettled(bool? flag)
    {
        var id = _world.MatterOwnedBy(RepairWorld.PersonInChild, flag);

        var outcome = await _world.RepairAsync("sprk_matter", id);

        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerRootSecure);
        // A secure root is provisioned by task 144, never re-owned (owner S6); UAC-r2 condition (b) F2-1: with its owner
        // unsettled, its search index is not filled either.
        _world.Writes.Should().BeEmpty();
        Field(outcome, RecordRepairPart.SearchIndex, "sprk_searchindexname").ReasonCode
            .Should().Be(RecordRepairReason.SearchIndexOwnerUnsettled);
        _world.Directory.Row("sprk_matter", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
        _world.Evictions.Should().BeEmpty();
    }

    [Fact]
    public async Task AUserOwnedRootFlaggedExplicitlyNotSecure_IsReOwnedToItsUnitsTeam()
    {
        var id = _world.MatterOwnedBy(RepairWorld.PersonInChild, isSecure: false);

        var outcome = await _world.RepairAsync("sprk_matter", id);

        _world.Writes.Should().ContainSingle().Which.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam));
        Field(outcome, RecordRepairPart.Owner, "ownerid").State.Should().Be(RecordRepairFieldState.Repaired);
    }

    // =====================================================================================
    // Review pass 1 — F1-b: the owner is decided BEFORE the mapping; a secure source never maps into a row it does not own
    // =====================================================================================

    [Fact]
    public async Task AUserOwnedEventUnderASecureMatter_GetsNothingMapped_FromTheSecureMatter()
    {
        _world.SecureMatterHolding("Secret Client", "Secret Practice");
        var id = _world.EventUnderTheSecureMatter(new EntityReference("systemuser", RepairWorld.PersonInChild), Directory.ChildBu);

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(["sprk_clientname", "sprk_practicearea", "sprk_priorityname"]);
        var row = _world.Directory.Row("sprk_event", id);
        row.Attributes.Should().NotContainKey("sprk_clientname");
        row.Attributes.Should().NotContainKey("sprk_practicearea");
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping && f.Column == null
            && f.ReasonCode == RecordRepairReason.MappingSourceSecure);
        outcome.Fields.Should().NotContain(f => f.Part == RecordRepairPart.Mapping && f.State == RecordRepairFieldState.Repaired);
    }

    [Fact]
    public async Task AnEventAlreadyOwnedByTheSecureTeam_IsStillMapped_TheGateDoesNotFire()
    {
        _world.SecureMatterHolding("Secret Client", "Secret Practice");
        var id = _world.EventUnderTheSecureMatter(new EntityReference("team", Directory.SecureNamedTeam), Directory.SecureBu);

        var outcome = await _world.RepairAsync("sprk_event", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields["sprk_clientname"].Should().Be("Secret Client");
        write.Fields["sprk_practicearea"].Should().Be("Secret Practice");
        write.Fields.Should().NotContainKey("ownerid");
        Field(outcome, RecordRepairPart.Mapping, "sprk_clientname").State.Should().Be(RecordRepairFieldState.Repaired);
    }

    // =====================================================================================
    // Review pass 1 — F1-c: a blank mapped string is no value (NFR-03)
    // =====================================================================================

    [Fact]
    public async Task ATemplateThatYieldsABlankString_IsNeverWritten_TwoRepairsWriteNothing()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("team", Directory.ChildTeam), owningUnit: Directory.ChildBu, indexed: true);
        _world.MapABlankTemplateOntoTheEvent();

        var first = await _world.RepairAsync("sprk_event", id);
        var second = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().BeEmpty("a blank string reads as unset again, so writing it would write on every pass");
        first.Status.Should().Be(RecordRepairStatus.NothingToRepair);
        second.Status.Should().Be(RecordRepairStatus.NothingToRepair);
        Field(first, RecordRepairPart.Mapping, RepairWorld.BlankTemplateTarget).ReasonCode.Should().Be(RecordRepairReason.MappingNoValue);
    }

    // =====================================================================================
    // Review pass 1 — F3: the stamped child's mapping, the fault paths, the leaf-table move
    // =====================================================================================

    [Fact]
    public async Task AStampedChild_IsMappedFromItsSource_ItsStampIsACopyNotASecondFiling()
    {
        // Filed under the communication, holding its I-1 stamp of the matter: read as two filings, the source would be
        // undetermined and nothing mapped.
        var id = _world.EventUnderTheCommunication(heldMatterStamp: RepairWorld.Matter);
        _world.MapTheCommunicationOntoTheEvent();

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields[RepairWorld.CommunicationMappedTarget]
            .Should().Be(RepairWorld.CommunicationSubject);
        Field(outcome, RecordRepairPart.Mapping, RepairWorld.CommunicationMappedTarget).State.Should().Be(RecordRepairFieldState.Repaired);
    }

    [Fact]
    public async Task AWriteThatFaults_WithTheOwnerInIt_IsEvictedFor_AndReportsNoExceptionText()
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "Tax", priority: "Urgent");
        _world.WriteFault = new InvalidOperationException("SECRET-DETAIL from Dataverse");

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.Failed);
        outcome.ReasonCode.Should().Be(RecordRepairReason.WriteFailed);
        outcome.Wrote.Should().BeFalse();
        _world.Writes.Should().ContainSingle().Which.Fields.Should().ContainKey("ownerid");
        // The contract: the write may have landed (a lost response), so an owner change is evicted for anyway.
        _world.Evictions.Should().ContainSingle().Which.Should().Be(("sprk_event", "sprk_events", id));
        outcome.ReasonCode.Should().NotContain("SECRET");
        outcome.Fields.Should().OnlyContain(f => f.ReasonCode == null || !f.ReasonCode.Contains("SECRET"), "ADR-019");
        outcome.Fields.Should().NotContain(f => f.State == RecordRepairFieldState.Repaired);
    }

    [Fact]
    public async Task AnOwnerResolverFault_LeavesTheOwner_AndMapsNothing_FailClosed()
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);
        _world.ResolverFault = new InvalidOperationException("SECRET-DETAIL resolver");

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.OwnerRefusal.Should().NotBeNull();
        outcome.OwnerRefusal!.Code.Should().Be(RecordCreationRefusal.OwnerUnavailableCode);
        outcome.OwnerRefusal.Detail.Should().NotContain("SECRET");
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping
            && f.ReasonCode == RecordRepairReason.MappingFilingUndecided);
        _world.Writes.Should().BeEmpty("an undecided filing could be a secure one");
        _world.Directory.Row("sprk_event", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AMappingFault_MapsNothing_AndTheOwnerIsStillRepaired(bool profileUnreadable)
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null, indexed: true);
        _world.MappingFault = profileUnreadable ? RepairWorld.MappingFaultKind.ProfileUnreadable : RepairWorld.MappingFaultKind.RuleFaults;

        var outcome = await _world.RepairAsync("sprk_event", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields.Keys.Should().Equal("ownerid");
        write.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam));
        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Mapping)
            .Which.ReasonCode.Should().Be(RecordRepairReason.MappingUnavailable);
    }

    [Fact]
    public async Task ALeafRowInAnotherBusinessUnit_IsReOwned_NoRowInheritsAccessThroughIt()
    {
        // A Signal is no ownership parent: nothing is filed under it, so moving it moves no one else's access.
        var id = _world.SignalUnderTheMatterOwnedBy(Directory.CallerUserId, Directory.GeneralBu);

        var outcome = await _world.RepairAsync("sprk_signal", id);

        _world.Writes.Should().ContainSingle().Which.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam));
        Field(outcome, RecordRepairPart.Owner, "ownerid").State.Should().Be(RecordRepairFieldState.Repaired);
        _world.Evictions.Should().ContainSingle().Which.Should().Be(("sprk_signal", "sprk_signals", id));
    }

    // =====================================================================================
    // Review pass 2 — F1-e: the mapping copies only from what the owner was decided over, and never from secure data
    // =====================================================================================

    [Fact]
    public async Task APairOnlyEventNamingASecureMatter_CopiesNothingFromIt_ProbeP1()
    {
        _world.SecureMatterHolding("Secret Client", "Secret Practice");
        _world.SeedMatterType();
        var id = _world.RowOf("sprk_event", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild),
            ["sprk_regardingrecordid"] = RepairWorld.SecureMatter.ToString("D"),
            ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", RepairWorld.MatterType),
        });

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingSecretWasWritten();
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping
            && f.ReasonCode == RecordRepairReason.MappingSourceUndetermined);
    }

    [Fact]
    public async Task ATypedOrdinaryMatterPlusAPairNamingASecureMatter_CopiesNothing_ProbeP2()
    {
        _world.SecureMatterHolding("Secret Client", "Secret Practice");
        _world.SeedMatterType();
        var id = _world.RowOf("sprk_event", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
            ["sprk_regardingrecordid"] = RepairWorld.SecureMatter.ToString("D"),
            ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", RepairWorld.MatterType),
        });

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingSecretWasWritten();
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping
            && f.ReasonCode == RecordRepairReason.MappingSourceUndetermined);
    }

    [Fact]
    public async Task APairNamingAnotherOrdinaryRecord_IsNotCopiedFrom_TheOwnerWasNotDecidedOverIt()
    {
        // Not secure, so only F1-e (i) stops it: the pair's record is not what the owner was decided over.
        _world.SeedMatterType();
        _world.Directory.Row("sprk_matter", RepairWorld.OtherMatter)["sprk_clientname"] = "Other Client";
        var id = _world.RowOf("sprk_event", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
            ["sprk_regardingrecordid"] = RepairWorld.OtherMatter.ToString("D"),
            ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", RepairWorld.MatterType),
        });

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(["sprk_clientname", "sprk_practicearea", "sprk_priorityname"]);
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping
            && f.ReasonCode == RecordRepairReason.MappingSourceUndetermined);
    }

    [Fact]
    public async Task ASourceWhoseOwnerDidNotComeBack_CopiesNothing_FailClosed()
    {
        var source = _world.MatterHolding(Directory.ChildBu, Directory.ChildTeam, isSecure: false, "Secret Client");
        var matter = _world.Directory.Row("sprk_matter", source);
        matter.Attributes.Remove("ownerid");
        var id = EventOwnedByThePersonUnder(source);

        await _world.RepairAsync("sprk_event", id);

        NothingSecretWasWritten();
    }

    [Fact]
    public async Task ATypedOrdinaryMatterThePairAgreesWith_IsStillTheSource_AndMaps()
    {
        _world.SeedMatterType();
        var id = _world.RowOf("sprk_event", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
            ["sprk_regardingrecordid"] = RepairWorld.Matter.ToString("D"),
            ["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", RepairWorld.MatterType),
        });

        await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient);
    }

    [Fact]
    public async Task ASecureFlaggedMatterNotYetIsolated_IsNotCopiedIntoAUserOwnedEvent()
    {
        var source = _world.MatterHolding(Directory.ChildBu, Directory.ChildTeam, isSecure: true, "Secret Client");
        var id = EventOwnedByThePersonUnder(source);

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingSecretWasWritten();
        outcome.Fields.Should().NotContain(f => f.Part == RecordRepairPart.Mapping && f.State == RecordRepairFieldState.Repaired);
    }

    [Fact]
    public async Task AMatterOwnedByTheSecureTeam_FlaggedFalse_IsNotCopiedIntoAUserOwnedEvent()
    {
        var source = _world.MatterHolding(Directory.SecureBu, Directory.SecureNamedTeam, isSecure: false, "Secret Client");
        var id = EventOwnedByThePersonUnder(source);

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingSecretWasWritten();
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping && f.ReasonCode == RecordRepairReason.MappingSourceSecure);
    }

    [Fact]
    public async Task ASourceOwnedByTheSecureTeam_ThatIsNoRoot_IsNotCopiedIntoARowItDoesNotIsolate()
    {
        // No secure flag anywhere, and a unit the resolver reads as ordinary: only the source's owner (the Secure Record
        // owner team) says its data is secure — the belt-and-braces check, independent of the owner gate.
        var communication = _world.CommunicationOwnedByTheSecureTeam("Secret subject");
        _world.MapTheCommunicationOntoTheEvent();
        var id = _world.RowOf("sprk_event", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild),
            ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", communication),
        });

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(RepairWorld.CommunicationMappedTarget);
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping && f.ReasonCode == RecordRepairReason.MappingSourceSecure);
    }

    [Fact]
    public async Task ATeamOwnedSource_WhenTheSecureTeamIsMissing_CopiesNothing_FailClosed()
    {
        // Review pass 3: with no Secure Record owner team to compare against, the source's team owner cannot be told
        // not to be it.
        _world.ShareWorld = SecureChildShareWorld.WithoutSecureBusinessUnit;
        var source = _world.MatterHolding(Directory.ChildBu, Directory.ChildTeam, isSecure: false, "Secret Client");
        var id = EventOwnedByThePersonUnder(source);

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingSecretWasWritten();
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping && f.ReasonCode == RecordRepairReason.MappingSourceSecure);
    }

    [Fact]
    public async Task ASourceReadThatFaults_CopiesNothing_FailClosed()
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);
        _world.SourceReadFault = new InvalidOperationException("Test: source read faulted.");

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(["sprk_clientname", "sprk_practicearea", "sprk_priorityname"]);
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping
            && f.ReasonCode == RecordRepairReason.MappingSourceUnreadable);
        outcome.Warnings.Should().Contain(RecordCreationService.RepairSourceUnreadableWarning);
        outcome.Warnings.Should().NotContain(w => w.Contains("you", StringComparison.OrdinalIgnoreCase),
            "Repair runs as the system principal; no caller is addressed (K4)");
    }

    [Fact]
    public async Task DecidingTheSecureTeamFaults_CopiesNothing_FailClosed()
    {
        _world.ShareWorld = () => SecureChildShareWorld.Standard().FailingQueriesOf("businessunit");
        var source = _world.MatterHolding(Directory.ChildBu, Directory.ChildTeam, isSecure: false, "Secret Client");
        var id = EventOwnedByThePersonUnder(source);

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingSecretWasWritten();
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping && f.ReasonCode == RecordRepairReason.MappingSourceSecure);
    }

    // The source gate's own branches, with the owner gate held open (the resolver answers an ordinary owner), so only the
    // gate itself can stop the copy.

    [Fact]
    public async Task ASourceWhoseFlagIsEmpty_IsNotCopiedIntoAnOrdinaryTeamsRow_TheGateAlone()
    {
        _world.ResolverAnswer = RecordOwnerResolution.Owned(Directory.ChildTeam);
        var source = _world.MatterHolding(Directory.ChildBu, Directory.ChildTeam, isSecure: false, "Secret Client");
        _world.Directory.Row("sprk_matter", source).Attributes.Remove("sprk_issecure"); // masked: reads as flagged
        var id = _world.RowOf("sprk_event", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("team", Directory.ChildTeam),
            ["owningteam"] = new EntityReference("team", Directory.ChildTeam),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", source),
        });

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingSecretWasWritten();
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping && f.ReasonCode == RecordRepairReason.MappingSourceSecure);
    }

    [Fact]
    public async Task ASecureTeamOwnedSource_IsNotCopiedIntoAnOrdinaryTeamsRow_TheGateAlone()
    {
        _world.ResolverAnswer = RecordOwnerResolution.Owned(Directory.ChildTeam);
        var communication = _world.CommunicationOwnedByTheSecureTeam("Secret subject");
        _world.MapTheCommunicationOntoTheEvent();
        var id = _world.RowOf("sprk_event", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("team", Directory.ChildTeam),
            ["owningteam"] = new EntityReference("team", Directory.ChildTeam),
            ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", communication),
        });

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(RepairWorld.CommunicationMappedTarget);
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping && f.ReasonCode == RecordRepairReason.MappingSourceSecure);
    }

    [Fact]
    public async Task AnOrdinaryMatterFlaggedFalse_OwnedByItsUnitsTeam_StillMaps()
    {
        var source = _world.MatterHolding(Directory.ChildBu, Directory.ChildTeam, isSecure: false, "Ordinary Client");
        var id = EventOwnedByThePersonUnder(source);

        await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields["sprk_clientname"].Should().Be("Ordinary Client");
    }

    // =====================================================================================
    // Review pass 2 — F1-f / UAC-r2: a Decision Record is append-only; Repair does not touch it
    // =====================================================================================

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADecisionRecord_IsNeverRepaired_NoReadNoWrite(bool underTheFiledCommunication)
    {
        // P3: a user-owned D-144 gate Decision Record about an unfiled user-owned communication; or one about the filed
        // communication, its I-1 stamp missing and a profile that would fill a column.
        var communication = underTheFiledCommunication
            ? RepairWorld.Communication
            : _world.UnfiledCommunicationOwnedBy(RepairWorld.PersonInChild);
        _world.Map("sprk_communication", "sprk_decisionrecord", RepairWorld.DefaultText("sprk_name", "filled", 1));
        var id = _world.RowOf("sprk_decisionrecord", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild),
            ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", communication),
        });
        var before = _world.Directory.Version("sprk_decisionrecord", id);

        var outcome = await _world.RepairAsync("sprk_decisionrecord", id);

        outcome.Status.Should().Be(RecordRepairStatus.Skipped);
        outcome.ReasonCode.Should().Be(RecordRepairReason.TableAppendOnly);
        _world.Directory.RecordQueries.Should().BeEmpty("an append-only row is not even read");
        _world.UpdateCalls.Should().Be(0);
        var row = _world.Directory.Row("sprk_decisionrecord", id);
        row.GetAttributeValue<EntityReference>("ownerid").Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
        row.Attributes.Should().NotContainKey("sprk_name");
        row.Attributes.Should().NotContainKey("sprk_regardingmatter");
        _world.Directory.Version("sprk_decisionrecord", id).Should().Be(before);
    }

    // =====================================================================================
    // UAC-r2: a document's server-owned columns are never written by Repair's mapping
    // =====================================================================================

    [Fact]
    public async Task ADocumentsAccessRecordAndD7Links_AreNeverMapped()
    {
        _world.MatterLinksTo();
        _world.Map("sprk_matter", "sprk_document", ServerOwnedRules());
        var id = _world.DocumentUnderTheMatter(indexName: "kept-index", index: RepairWorld.ChildIndex);

        var outcome = await _world.RepairAsync("sprk_document", id);

        _world.Writes.Should().BeEmpty();
        Field(outcome, RecordRepairPart.Mapping, "sprk_accesspermission").ReasonCode
            .Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        Field(outcome, RecordRepairPart.Mapping, "sprk_accessinheritance").ReasonCode
            .Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
        Field(outcome, RecordRepairPart.Mapping, "sprk_parentdocument").ReasonCode
            .Should().Be(InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
        Field(outcome, RecordRepairPart.Mapping, "sprk_relatedcommunication").ReasonCode
            .Should().Be(InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
    }

    [Fact]
    public async Task ADocumentsAccessPermission_IsStillRefusedByRepair_TheCreateTimeFillOnlyExemptionDoesNotLeak()
    {
        // 018b-K2 (UAC-r2 condition 2): create-time mapping fills a document's Access Permission (D7: no own narrowing to
        // lower on a create). Repair runs on an EXISTING document, where a fill could lower its narrowing: refused through
        // DocumentServerOwnedRefusal (must not fire for the create exemption).
        _world.MatterLinksTo();
        _world.Map("sprk_matter", "sprk_document", RepairWorld.DefaultText("sprk_accesspermission", "100000001", 1));
        var id = _world.DocumentUnderTheMatter(indexName: "kept-index", index: RepairWorld.ChildIndex);

        var outcome = await _world.RepairAsync("sprk_document", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("sprk_accesspermission");
        Field(outcome, RecordRepairPart.Mapping, "sprk_accesspermission").ReasonCode
            .Should().Be(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode);
    }

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_todo")]
    public async Task TheSameColumnsOnAnotherTable_AreNotRefusedAsADocuments(string table)
    {
        _world.MatterLinksTo();
        _world.Map("sprk_matter", table, ServerOwnedRules());
        var id = _world.RowOf(table, Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("team", Directory.ChildTeam),
            ["owningteam"] = new EntityReference("team", Directory.ChildTeam),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
        });

        var outcome = await _world.RepairAsync(table, id);

        outcome.Fields.Should().NotContain(f => f.ReasonCode == InheritedAccessPermission.AppOnlyDocumentWriteReasonCode
            || f.ReasonCode == InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
        // UAC-r2 condition (b) F1-1: the access columns are Repair's never-mapped set on every table (not the document gate).
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(["sprk_accessinheritance", "sprk_accesspermission"]);
        Field(outcome, RecordRepairPart.Mapping, "sprk_accessinheritance").ReasonCode.Should().Be(RecordRepairReason.MappedValueConfersAccess);
        Field(outcome, RecordRepairPart.Mapping, "sprk_accesspermission").ReasonCode.Should().Be(RecordRepairReason.MappedValueConfersAccess);
        // The links are lookups to ownership parents here: not written because Repair never re-files a row — the
        // document gate is not what stops them.
        Field(outcome, RecordRepairPart.Mapping, "sprk_parentdocument").ReasonCode.Should().Be(RecordRepairReason.MappedValueWouldRefile);
        Field(outcome, RecordRepairPart.Mapping, "sprk_relatedcommunication").ReasonCode.Should().Be(RecordRepairReason.MappedValueWouldRefile);
    }

    // =====================================================================================
    // #2247: Repair decides the owner itself; a mapping rule never writes the owner or the creator
    // =====================================================================================

    [Fact]
    public async Task AMappingRuleTargetingTheOwnerOrTheCreator_IsSkipped_WhileRepairsOwnOwnerDecisionIsWritten()
    {
        // A user-owned event under the ordinary matter: Repair's owner step re-owns it to the matter's team (its OWN decision,
        // which the write does not ask the app-only refusal over). The profile also targets the owner and the creator: the
        // per-rule app-only refusal skips each of those rules, so none reaches the write.
        var matter = _world.Directory.Row("sprk_matter", RepairWorld.Matter);
        matter["sprk_createdbyperson"] = new EntityReference("systemuser", RepairWorld.AppUser);
        matter["sprk_createdbycontact"] = new EntityReference("contact", Guid.Parse("22470000-0000-0000-0000-0000000c0c01"));
        _world.Map("sprk_matter", "sprk_event",
            RepairWorld.Expression(CreateTimeFieldMapping.MappingTypeTemplate, "{sprk_clientname}", "sprk_clientname", 1),
            RepairWorld.CopyLookup("ownerid", "ownerid", 2),
            RepairWorld.CopyLookup("sprk_createdbyperson", "sprk_createdbyperson", 3),
            RepairWorld.CopyLookup("sprk_createdbycontact", "sprk_createdbycontact", 4));
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam), "Repair's own owner decision is written");
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient, "the other rules still map");
        write.Fields.Keys.Should().NotContain(["sprk_createdbyperson", "sprk_createdbycontact"]);
        Field(outcome, RecordRepairPart.Owner, "ownerid").State.Should().Be(RecordRepairFieldState.Repaired);
        Field(outcome, RecordRepairPart.Mapping, "ownerid").ReasonCode
            .Should().Be(InheritedAccessPermission.AppOnlyOwnerWriteReasonCode, "the per-rule app-only refusal skips it");
        Field(outcome, RecordRepairPart.Mapping, "sprk_createdbyperson").ReasonCode
            .Should().Be(InheritedAccessPermission.AppOnlyCreatorWriteReasonCode);
        Field(outcome, RecordRepairPart.Mapping, "sprk_createdbycontact").ReasonCode
            .Should().Be(InheritedAccessPermission.AppOnlyCreatorWriteReasonCode);
    }

    // =====================================================================================
    // UAC-r2 condition (b) F1-1: columns that decide access or filing are never mapped, on any table
    // =====================================================================================

    [Fact]
    public async Task AMattersBlankSecureFlag_IsNeverFilledFromItsProject_ScenarioA()
    {
        var id = _world.MatterUnderAnOrdinaryProject();
        _world.Map("sprk_project", "sprk_matter", RepairWorld.CopyBoolean("sprk_issecure", "sprk_issecure", 1));

        var outcome = await _world.RepairAsync("sprk_matter", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("sprk_issecure",
            "a blank flag reads as secure; a mapped false would expose the matter's children");
        // #2251: a secure root's secure flag is refused to every app-only generic writer, so the per-rule app-only refusal
        // skips it first and its code is the reason reported (Repair's own MappedValueConfersAccess still holds the rest).
        Field(outcome, RecordRepairPart.Mapping, "sprk_issecure").ReasonCode
            .Should().Be(InheritedAccessPermission.AppOnlySecureRootWriteReasonCode);
        _world.Directory.Row("sprk_matter", id).Attributes.Should().NotContainKey("sprk_issecure");
    }

    [Fact]
    public async Task AMattersEmptyAssignedLawFirm_IsNeverFilledFromItsProject_ScenarioB()
    {
        var id = _world.MatterUnderAnOrdinaryProject();
        _world.Map("sprk_project", "sprk_matter", RepairWorld.CopyLookup("sprk_assignedlawfirm1", "sprk_assignedlawfirm1", 1));

        var outcome = await _world.RepairAsync("sprk_matter", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("sprk_assignedlawfirm1",
            "an Assigned outside-counsel value is a Collaborate grant (AssignedAccessMaterializer)");
        Field(outcome, RecordRepairPart.Mapping, "sprk_assignedlawfirm1").ReasonCode.Should().Be(RecordRepairReason.MappedValueAssignsAccess);
    }

    [Fact]
    public async Task AnOrdinaryColumn_IsStillFilledFromTheProject_F1_1MustNotFire()
    {
        var id = _world.MatterUnderAnOrdinaryProject();
        _world.Map("sprk_project", "sprk_matter",
            RepairWorld.CopyBoolean("sprk_issecure", "sprk_issecure", 1),
            RepairWorld.Expression(CreateTimeFieldMapping.MappingTypeTemplate, "{sprk_clientname}", "sprk_clientname", 2));

        var outcome = await _world.RepairAsync("sprk_matter", id);

        var written = _world.Writes.Should().ContainSingle().Subject.Fields;
        written["sprk_clientname"].Should().Be(RepairWorld.ProjectClient);
        written.Should().NotContainKey("sprk_issecure");
        Field(outcome, RecordRepairPart.Mapping, "sprk_clientname").State.Should().Be(RecordRepairFieldState.Repaired);
    }

    // =====================================================================================
    // Review pass 2 — F3: the mutants that survived pass 1
    // =====================================================================================

    [Fact]
    public async Task AnEventOwnedByAnOrdinaryTeam_FiledUnderASecureRecord_GetsNoMapping()
    {
        // Filed under a communication of the secure matter that is NOT isolated yet: the source itself is not secure, so
        // only the owner gate (the resolution is the Secure team, which does not own the event) stops the copy.
        _world.CommunicationUnderTheSecureMatterNotIsolated("Secret subject");
        _world.MapTheCommunicationOntoTheEvent();
        var id = _world.RowOf("sprk_event", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("team", Directory.ChildTeam),
            ["owningteam"] = new EntityReference("team", Directory.ChildTeam),
            ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", RepairWorld.SecureCommunication),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", RepairWorld.SecureMatter),
        });
        _world.SecureMatterHolding("Secret Client", "Secret Practice");

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(RepairWorld.CommunicationMappedTarget);
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping && f.ReasonCode == RecordRepairReason.MappingSourceSecure);
    }

    [Fact]
    public async Task AFiledRowTheResolverCannotOwn_GetsNoMapping()
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);
        _world.ResolverAnswer = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "Test: no owner.");

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().BeEmpty();
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping
            && f.ReasonCode == RecordRepairReason.MappingFilingUndecided);
    }

    [Fact]
    public async Task AConcatThatYieldsOnlyASpace_IsNeverWritten_TwoRepairsWriteNothing()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("team", Directory.ChildTeam), owningUnit: Directory.ChildBu, indexed: true);
        _world.Map("sprk_matter", "sprk_event",
            RepairWorld.Expression(CreateTimeFieldMapping.MappingTypeConcat, "{sprk_nosuch1} {sprk_nosuch2}", "sprk_eventdescription", 1));

        await _world.RepairAsync("sprk_event", id);
        await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public async Task EverySecureFlaggedRootTable_KeepsAUserOwner(string table)
    {
        var id = _world.RootOwnedBy(table, RepairWorld.PersonInChild, isSecure: true);

        var outcome = await _world.RepairAsync(table, id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("ownerid");
        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerRootSecure);
    }

    [Fact]
    public async Task AWorkAssignmentFiledOnlyUnderAnEvent_IsReOwnedInTheUnitOfTheEventsMatter_NeverTheEventsOwn()
    {
        // Owner decision 2026-10-29: a work assignment belongs to its core record. Filed only under an event owned in the
        // GENERAL unit whose matter is in the CHILD unit, it is owned with the matter (the parents the un-secure cascade names),
        // so this user-owned row in the child unit is re-owned there — not escalated as a move to the event's general unit.
        var @event = _world.RowOf("sprk_event", Directory.GeneralBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("team", Directory.GeneralTeam),
            ["owningteam"] = new EntityReference("team", Directory.GeneralTeam),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
        });
        var id = _world.RowOf("sprk_workassignment", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild),
            ["sprk_issecure"] = false,
            ["sprk_regardingevent"] = new EntityReference("sprk_event", @event),
        });

        var outcome = await _world.RepairAsync("sprk_workassignment", id);

        _world.OwnerContexts.Should().ContainSingle().Which.Parents.Should().Equal(
            [new RecordOwnershipParent("sprk_matter", RepairWorld.Matter), new RecordOwnershipParent("sprk_event", @event)],
            "the event's matter first, the event kept among the parents (secure-if-any)");
        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam),
            $"the matter's unit's team; never the event's ({Directory.GeneralTeam:D})");
        Field(outcome, RecordRepairPart.Owner, "ownerid").State.Should().Be(RecordRepairFieldState.Repaired);
    }

    [Fact]
    public async Task AWorkAssignmentWithAnAssignedToInternalAndNoAssignedTo_IsNeverGivenTheCopiedDefault_CreateOnly()
    {
        // Task 037 (FR-36, UAC-r2 condition 1): the copied default (Assigned To from Assigned To Internal) is a create-time
        // fill; the repair pass never back-fills it.
        var id = _world.RowOf("sprk_workassignment", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("team", Directory.ChildTeam),
            ["sprk_assignedtointernal"] = new EntityReference("contact", Guid.Parse("a0370000-0000-4000-8000-0000000000c1")),
        });

        await _world.RepairAsync("sprk_workassignment", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("sprk_assignedto");
        _world.Directory.Row("sprk_workassignment", id).Attributes.Should().NotContainKey("sprk_assignedto");
    }

    private static FieldMappingRuleEntity[] ServerOwnedRules() =>
    [
        RepairWorld.DefaultText("sprk_accesspermission", "100000001", 1),
        RepairWorld.DefaultText("sprk_accessinheritance", "from-matter", 2),
        RepairWorld.CopyLookup("sprk_sourcedocument", "sprk_parentdocument", 3),
        RepairWorld.CopyLookup("sprk_sourcecommunication", "sprk_relatedcommunication", 4),
    ];

    private Guid EventOwnedByThePersonUnder(Guid matter) =>
        _world.RowOf("sprk_event", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", RepairWorld.PersonInChild),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
        });

    private void NothingSecretWasWritten()
    {
        var values = _world.Writes.SelectMany(w => w.Fields.Values).OfType<string>().ToArray();
        values.Should().NotContain(["Secret Client", "Secret Practice"]);
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(["sprk_clientname", "sprk_practicearea"]);
    }

    private static RecordRepairField Field(RecordRepairOutcome outcome, RecordRepairPart part, string column) =>
        outcome.Fields.Should().ContainSingle(f => f.Part == part && f.Column == column).Subject;
}

/// <summary>
/// The world of <see cref="RecordRepairModeTests"/>: an ordinary matter in the CHILD unit (owned by its default team) with a
/// matter → event mapping profile; a person in the child unit who owns rows natively; an application user; a communication
/// filed under the matter (its stamp source); each unit's search-index defaults.
/// </summary>
internal sealed class RepairWorld
{
    public static readonly Guid Matter = Guid.Parse("a0160000-0000-4000-8000-000000000001");
    public static readonly Guid OtherMatter = Guid.Parse("a0160000-0000-4000-8000-000000000002");
    public static readonly Guid Project = Guid.Parse("a0160000-0000-4000-8000-000000000003");
    public static readonly Guid Communication = Guid.Parse("a0160000-0000-4000-8000-000000000004");
    public static readonly Guid PersonInChild = Guid.Parse("a0160000-0000-4000-8000-000000000005");
    public static readonly Guid AppUser = Guid.Parse("a0160000-0000-4000-8000-000000000006");
    public static readonly Guid GeneralIndex = Guid.Parse("a0160000-0000-4000-8000-0000000000a1");
    public static readonly Guid ChildIndex = Guid.Parse("a0160000-0000-4000-8000-0000000000a2");
    public const string GeneralIndexName = "spaarke-general-index";
    public const string ChildIndexName = "spaarke-child-index";
    public const string MatterClient = "Acme Corp";
    public const string MatterPracticeArea = "Litigation";
    public const string DefaultPriority = "Normal";
    public static readonly Guid SecureMatter = Guid.Parse("a0160000-0000-4000-8000-000000000007");
    public const string CommunicationSubject = "Re: the Acme hearing";
    public const string CommunicationMappedTarget = "sprk_description";
    public const string BlankTemplateTarget = "sprk_eventdescription";

    /// <summary>How the mapping step faults: the profile read throws, or a rule the mapping cannot read throws inside it.</summary>
    public enum MappingFaultKind
    {
        None,
        ProfileUnreadable,
        RuleFaults,
    }

    private readonly Dictionary<(string Source, string Target), FieldMappingProfileEntity> _profiles = new();
    private readonly StampWorld _stamps = new();

    public RepairWorld()
    {
        Directory = Directory.Standard()
            .WithUser(PersonInChild, Guid.NewGuid(), Directory.ChildBu)
            .WithUser(AppUser, Guid.NewGuid(), Directory.GeneralBu)
            .WithRecord("sprk_matter", Matter, Directory.ChildBu, isSecure: false, owningTeam: Directory.ChildTeam,
                extra: new Dictionary<string, object>
                {
                    ["sprk_clientname"] = MatterClient,
                    ["sprk_practicearea"] = MatterPracticeArea,
                    ["sprk_project"] = new EntityReference("sprk_project", Project),
                })
            .WithOrdinaryRoot("sprk_matter", OtherMatter)
            .WithRecord("sprk_communication", Communication, Directory.ChildBu, owningTeam: Directory.ChildTeam,
                extra: new Dictionary<string, object>
                {
                    ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter),
                    ["sprk_subject"] = CommunicationSubject,
                });
        _stamps.Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", Matter)]);

        SearchIndexDefaults[Directory.GeneralBu] = (GeneralIndexName, GeneralIndex);
        SearchIndexDefaults[Directory.ChildBu] = (ChildIndexName, ChildIndex);

        _profiles[("sprk_matter", "sprk_event")] = Profile("sprk_matter", "sprk_event",
            Copy("sprk_clientname", "sprk_clientname", 1),
            Copy("sprk_practicearea", "sprk_practicearea", 2),
            new FieldMappingRuleEntity
            {
                Id = Guid.NewGuid(), Name = "priority", SourceField = string.Empty, TargetField = "sprk_priorityname", TargetFieldType = 0,
                MappingType = CreateTimeFieldMapping.MappingTypeDefault, DefaultValue = DefaultPriority, ExecutionOrder = 3,
                IsActive = true,
            });
    }

    public OwnershipDirectory Directory { get; }

    /// <summary>Every conditional update the service sent: the version it was conditioned on, and whether it applied.</summary>
    public List<(string Table, Guid Id, string Version, Dictionary<string, object> Fields, bool Applied)> Writes { get; } = [];

    /// <summary>Every owner decision asked of the resolver.</summary>
    public List<RecordOwnershipContext> OwnerContexts { get; } = [];

    /// <summary>Every owner-change eviction: (table, entity set, id).</summary>
    public List<(string Table, string Set, Guid Id)> Evictions { get; } = [];

    /// <summary>Per business unit: its search-index defaults.</summary>
    public Dictionary<Guid, (string? Name, Guid? Index)> SearchIndexDefaults { get; } = [];

    /// <summary>Runs once, just before the update reaches the row (another writer's change lands in between).</summary>
    public Action? BeforeWrite { get; set; }

    /// <summary>When set, the repair's update is recorded and then throws it (not applied).</summary>
    public Exception? WriteFault { get; set; }

    /// <summary>When set, every owner decision asked of the resolver throws it.</summary>
    public Exception? ResolverFault { get; set; }

    /// <summary>How the mapping step faults, if at all.</summary>
    public MappingFaultKind MappingFault { get; set; }

    /// <summary>When set, the resolver answers this instead of deciding (an owner decision the test dictates).</summary>
    public RecordOwnerResolution? ResolverAnswer { get; set; }

    /// <summary>The share world the synchronizer reads the Secure Record owner team from (standard: present).</summary>
    public Func<SecureChildShareWorld> ShareWorld { get; set; } = SecureChildShareWorld.Standard;

    /// <summary>When set, reading a systemuser row (the application-user check, I-6) throws it.</summary>
    public Exception? SystemUserReadFault { get; set; }

    /// <summary>Systemuser ids whose row reads back as none (the application-user check cannot tell).</summary>
    public HashSet<Guid> MissingSystemUsers { get; } = [];

    /// <summary>When set, reading a mapping source's columns throws it.</summary>
    public Exception? SourceReadFault { get; set; }

    /// <summary>Every update call the service made (applied or not).</summary>
    public int UpdateCalls => Writes.Count;

    public static readonly Guid MatterType = Guid.Parse("a0160000-0000-4000-8000-0000000000c1");
    public static readonly Guid SecureCommunication = Guid.Parse("a0160000-0000-4000-8000-000000000008");
    public static readonly Guid SourceDocument = Guid.Parse("a0160000-0000-4000-8000-000000000009");

    public Task<RecordRepairOutcome> RepairAsync(string table, Guid id) => Service().RepairAsync(table, id, CancellationToken.None);

    // ── Rows ────────────────────────────────────────────────────────────────────────────────────────────────────

    public Guid EventUnderTheMatter(
        string? clientName, string? practiceArea, string? priority, EntityReference? owner = null, Guid? owningUnit = null,
        bool indexed = false)
    {
        var id = Guid.NewGuid();
        var extra = new Dictionary<string, object>
        {
            ["ownerid"] = owner ?? new EntityReference("systemuser", PersonInChild),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter),
        };
        if (indexed)
        {
            // Task 039 (FR-38): an event is search-indexed now, so Repair fills its two index columns where unset. A test about
            // something else gives the row both values already, so its owner/mapping assertions stay about the owner/mapping.
            extra["sprk_searchindexname"] = ChildIndexName;
            extra["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", ChildIndex);
        }
        if (owner is { LogicalName: "team" })
            extra["owningteam"] = owner;
        if (clientName is not null)
            extra["sprk_clientname"] = clientName;
        if (practiceArea is not null)
            extra["sprk_practicearea"] = practiceArea;
        if (priority is not null)
            extra["sprk_priorityname"] = priority;
        Directory.WithRecord("sprk_event", id, owningUnit ?? Directory.ChildBu, extra: extra);
        return id;
    }

    /// <summary>An event filed under the communication (a stamp source), owned by the child unit's team already.</summary>
    public Guid EventUnderTheCommunication(Guid? heldMatterStamp)
    {
        var id = Guid.NewGuid();
        var extra = new Dictionary<string, object>
        {
            ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", Communication),
        };
        if (heldMatterStamp is { } held)
            extra["sprk_regardingmatter"] = new EntityReference("sprk_matter", held);
        Directory.WithRecord("sprk_event", id, Directory.ChildBu, owningTeam: Directory.ChildTeam, extra: extra);
        return id;
    }

    public Guid UnfiledCommunicationOwnedBy(Guid person)
    {
        var id = Guid.NewGuid();
        Directory.WithRecord("sprk_communication", id, Directory.ChildBu,
            extra: new Dictionary<string, object> { ["ownerid"] = new EntityReference("systemuser", person) });
        return id;
    }

    public Guid UnfiledDocumentOwnedBy(Guid user, Guid owningUnit)
    {
        var id = Guid.NewGuid();
        Directory.WithRecord("sprk_document", id, owningUnit,
            extra: new Dictionary<string, object> { ["ownerid"] = new EntityReference("systemuser", user) });
        return id;
    }

    /// <summary>A document filed under the matter, owned by the child unit's team, with the search-index values given.</summary>
    public Guid DocumentUnderTheMatter(string? indexName, Guid? index)
    {
        var id = Guid.NewGuid();
        var extra = new Dictionary<string, object> { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) };
        if (indexName is not null)
            extra["sprk_searchindexname"] = indexName;
        if (index is { } catalog)
            extra["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", catalog);
        Directory.WithRecord("sprk_document", id, Directory.ChildBu, owningTeam: Directory.ChildTeam, extra: extra);
        return id;
    }

    /// <summary>A natively created invoice filed under the matter, owned by the person in the child unit.</summary>
    public Guid InvoiceUnderTheMatter()
    {
        var id = Guid.NewGuid();
        Directory.WithRecord("sprk_invoice", id, Directory.ChildBu, extra: new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", PersonInChild),
            ["sprk_matter"] = new EntityReference("sprk_matter", Matter),
        });
        return id;
    }

    /// <summary>A matter in the child unit owned by a user (natively created), with the secure flag given (null: empty).</summary>
    public Guid MatterOwnedBy(Guid user, bool? isSecure)
    {
        var id = Guid.NewGuid();
        Directory.WithRecord("sprk_matter", id, Directory.ChildBu, isSecure: isSecure ?? false,
            extra: new Dictionary<string, object> { ["ownerid"] = new EntityReference("systemuser", user) });
        if (isSecure is null)
            Directory.Row("sprk_matter", id).Attributes.Remove("sprk_issecure"); // masked, as WithMaskedSecureFlag
        return id;
    }

    /// <summary>The secure matter, isolated (Secure Record unit, its named team, flagged), holding the mapped columns.</summary>
    public void SecureMatterHolding(string clientName, string practiceArea)
    {
        Directory.WithRecord("sprk_matter", SecureMatter, Directory.SecureBu, isSecure: true, owningTeam: Directory.SecureNamedTeam,
            extra: new Dictionary<string, object> { ["sprk_clientname"] = clientName, ["sprk_practicearea"] = practiceArea });
    }

    /// <summary>An event filed under the secure matter, with nothing mapped yet.</summary>
    public Guid EventUnderTheSecureMatter(EntityReference owner, Guid owningUnit)
    {
        var id = Guid.NewGuid();
        var extra = new Dictionary<string, object>
        {
            ["ownerid"] = owner,
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
        };
        if (owner is { LogicalName: "team" })
            extra["owningteam"] = owner;
        Directory.WithRecord("sprk_event", id, owningUnit, extra: extra);
        return id;
    }

    /// <summary>A Signal (a leaf: no ownership parent) filed under the matter, owned by a user in the unit given.</summary>
    public Guid SignalUnderTheMatterOwnedBy(Guid user, Guid owningUnit)
    {
        var id = Guid.NewGuid();
        Directory.WithRecord("sprk_signal", id, owningUnit, extra: new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", user),
            ["sprk_matter"] = new EntityReference("sprk_matter", Matter),
        });
        return id;
    }

    /// <summary>The record-type catalog row naming the matter table (the ADR-024 pair's type).</summary>
    public void SeedMatterType() =>
        Directory.WithRecord("sprk_recordtype_ref", MatterType, null,
            extra: new Dictionary<string, object> { ["sprk_recordlogicalname"] = "sprk_matter" });

    /// <summary>A row of <paramref name="table"/> in the unit given, with the columns given.</summary>
    public Guid RowOf(string table, Guid owningUnit, Dictionary<string, object> columns)
    {
        var id = Guid.NewGuid();
        Directory.WithRecord(table, id, owningUnit, extra: columns);
        return id;
    }

    /// <summary>A root of <paramref name="table"/> in the child unit owned by a user, with the secure flag given.</summary>
    public Guid RootOwnedBy(string table, Guid user, bool isSecure)
    {
        var id = Guid.NewGuid();
        Directory.WithRecord(table, id, Directory.ChildBu, isSecure: isSecure,
            extra: new Dictionary<string, object> { ["ownerid"] = new EntityReference("systemuser", user) });
        return id;
    }

    /// <summary>A matter in the unit given, owned by the team given, with the flag given, holding the mapped columns.</summary>
    public Guid MatterHolding(Guid owningUnit, Guid owningTeam, bool isSecure, string clientName)
    {
        var id = Guid.NewGuid();
        Directory.WithRecord("sprk_matter", id, owningUnit, isSecure: isSecure, owningTeam: owningTeam,
            extra: new Dictionary<string, object> { ["sprk_clientname"] = clientName, ["sprk_practicearea"] = "Secret Practice" });
        return id;
    }

    /// <summary>A communication filed under the secure matter but NOT isolated (owned by the child unit's team).</summary>
    public void CommunicationUnderTheSecureMatterNotIsolated(string subject)
    {
        Directory.WithRecord("sprk_communication", SecureCommunication, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new Dictionary<string, object>
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
                ["sprk_subject"] = subject,
            });
        _stamps.Row("sprk_communication", SecureCommunication, [("sprk_regardingmatter", "sprk_matter", SecureMatter)]);
    }

    /// <summary>
    /// A communication filed under nothing, owned by the Secure Record owner team, with a subject — its business unit read
    /// as the child unit, so the resolver (which reads the unit) does not see it as secure and only the source gate's own
    /// owner check (review F1-e (ii)) can. Dataverse keeps the two consistent; the gate does not rely on it.
    /// </summary>
    public Guid CommunicationOwnedByTheSecureTeam(string subject)
    {
        var id = Guid.NewGuid();
        Directory.WithRecord("sprk_communication", id, Directory.ChildBu, owningTeam: Directory.SecureNamedTeam,
            extra: new Dictionary<string, object> { ["sprk_subject"] = subject });
        _stamps.Row("sprk_communication", id, []);
        return id;
    }

    /// <summary>Replaces (or adds) the <paramref name="source"/> → <paramref name="target"/> profile.</summary>
    public void Map(string source, string target, params FieldMappingRuleEntity[] rules) =>
        _profiles[(source, target)] = Profile(source, target, rules);

    /// <summary>A Concat or Template rule into a text column.</summary>
    public static FieldMappingRuleEntity Expression(int mappingType, string expression, string target, int order) => new()
    {
        Id = Guid.NewGuid(), Name = $"expr -> {target}", SourceField = string.Empty, TargetField = target,
        TargetFieldType = CreateTimeFieldMapping.FieldTypeText, MappingType = mappingType, Expression = expression,
        ExecutionOrder = order, IsActive = true,
    };

    /// <summary>A Copy rule of a lookup.</summary>
    public static FieldMappingRuleEntity CopyLookup(string source, string target, int order) => new()
    {
        Id = Guid.NewGuid(), Name = $"{source} -> {target}", SourceField = source, SourceFieldType = 1, TargetField = target,
        TargetFieldType = CreateTimeFieldMapping.FieldTypeLookup, MappingType = CreateTimeFieldMapping.MappingTypeCopy,
        ExecutionOrder = order, IsActive = true,
    };

    /// <summary>A Default rule of a literal into a text column.</summary>
    public static FieldMappingRuleEntity DefaultText(string target, string value, int order) => new()
    {
        Id = Guid.NewGuid(), Name = $"default -> {target}", SourceField = string.Empty, TargetField = target,
        TargetFieldType = CreateTimeFieldMapping.FieldTypeText, MappingType = CreateTimeFieldMapping.MappingTypeDefault,
        DefaultValue = value, ExecutionOrder = order, IsActive = true,
    };

    public static readonly Guid OrdinaryProject = Guid.Parse("a0160000-0000-4000-8000-00000000000a");
    public static readonly Guid OutsideCounsel = Guid.Parse("a0160000-0000-4000-8000-00000000000b");
    public const string ProjectClient = "Project Client";

    /// <summary>
    /// UAC-r2 condition (b) F1-1: an ordinary project (flag false, owned by the child unit's team) holding an outside-counsel
    /// firm and a client name, and a matter filed under it, owned by the same team, whose secure flag is BLANK.
    /// </summary>
    public Guid MatterUnderAnOrdinaryProject()
    {
        Directory.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, isSecure: false, owningTeam: Directory.ChildTeam,
            extra: new Dictionary<string, object>
            {
                ["sprk_clientname"] = ProjectClient,
                ["sprk_assignedlawfirm1"] = new EntityReference("sprk_organization", OutsideCounsel),
            });
        var id = Guid.NewGuid();
        Directory.WithRecord("sprk_matter", id, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new Dictionary<string, object> { ["sprk_project"] = new EntityReference("sprk_project", OrdinaryProject) });
        Directory.Row("sprk_matter", id).Attributes.Remove("sprk_issecure"); // blank: the resolver reads it as secure
        return id;
    }

    /// <summary>A Copy rule of a two-option (boolean) column.</summary>
    public static FieldMappingRuleEntity CopyBoolean(string source, string target, int order) => new()
    {
        Id = Guid.NewGuid(), Name = $"{source} -> {target}", SourceField = source,
        SourceFieldType = CreateTimeFieldMapping.FieldTypeBoolean, TargetField = target,
        TargetFieldType = CreateTimeFieldMapping.FieldTypeBoolean, MappingType = CreateTimeFieldMapping.MappingTypeCopy,
        ExecutionOrder = order, IsActive = true,
    };

    /// <summary>The matter holds lookups to a document and the communication (sources of the D7-link rules).</summary>
    public void MatterLinksTo()
    {
        var matter = Directory.Row("sprk_matter", Matter);
        matter["sprk_sourcedocument"] = new EntityReference("sprk_document", SourceDocument);
        matter["sprk_sourcecommunication"] = new EntityReference("sprk_communication", Communication);
    }

    /// <summary>A communication → event profile copying the communication's subject.</summary>
    public void MapTheCommunicationOntoTheEvent() =>
        _profiles[("sprk_communication", "sprk_event")] = Profile("sprk_communication", "sprk_event",
            Copy("sprk_subject", CommunicationMappedTarget, 1));

    /// <summary>Adds a Template rule whose placeholder the matter does not hold: it yields an empty string.</summary>
    public void MapABlankTemplateOntoTheEvent()
    {
        var profile = _profiles[("sprk_matter", "sprk_event")];
        profile.Rules = [.. profile.Rules!, new FieldMappingRuleEntity
        {
            Id = Guid.NewGuid(), Name = "blank", SourceField = string.Empty, TargetField = BlankTemplateTarget,
            TargetFieldType = CreateTimeFieldMapping.FieldTypeText, MappingType = CreateTimeFieldMapping.MappingTypeTemplate,
            Expression = "{sprk_nosuchfield}", ExecutionOrder = 5, IsActive = true,
        }];
    }

    /// <summary>Adds a rule copying the matter's project lookup into the event's project filing column.</summary>
    public void MapMattersProjectOntoTheEvent()
    {
        var profile = _profiles[("sprk_matter", "sprk_event")];
        profile.Rules = [.. profile.Rules!, new FieldMappingRuleEntity
        {
            Id = Guid.NewGuid(), Name = "project", SourceField = "sprk_project", SourceFieldType = 1,
            TargetField = "sprk_regardingproject", TargetFieldType = CreateTimeFieldMapping.FieldTypeLookup,
            MappingType = CreateTimeFieldMapping.MappingTypeCopy, ExecutionOrder = 4, IsActive = true,
        }];
    }

    // ── The service, over the doubled boundary ───────────────────────────────────────────────────────────────────

    private RecordCreationService Service()
    {
        var entities = Entities();
        var appOnly = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        appOnly
            .Setup(a => a.GetFieldMappingProfileWithRulesAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Returns((string source, string target, bool _, CancellationToken _) => MappingFault switch
            {
                MappingFaultKind.ProfileUnreadable =>
                    Task.FromException<FieldMappingProfileEntity?>(new InvalidOperationException("Test: profile read faulted.")),
                // A rule the mapping cannot read: it throws inside the mapping step, after nothing was mapped.
                MappingFaultKind.RuleFaults => Task.FromResult<FieldMappingProfileEntity?>(
                    Profile(source, target, Copy("sprk_clientname", "sprk_clientname", 1), null!)),
                _ => Task.FromResult(_profiles.TryGetValue((source, target), out var profile) ? profile : null),
            });

        var invalidator = new Mock<IMembershipCacheInvalidator>();
        invalidator
            .Setup(i => i.InvalidateRecordOwnerChangeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Guid, string?, CancellationToken>((table, set, id, _, _) => Evictions.Add((table, set, id)))
            .Returns(Task.CompletedTask);
        var scopes = new ServiceCollection().AddSingleton(invalidator.Object).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new RecordCreationService(
            new CapturingResolver(Directory.Resolver(), OwnerContexts, () => ResolverFault, () => ResolverAnswer), appOnly.Object, entities,
            SecureRootFilingGateFixtures.Over(entities),
            new CoreAncestorRestamper(_stamps.Service, _stamps.Resolver, NullLogger<CoreAncestorRestamper>.Instance),
            SecureChildShareWorld.SynchronizerOver(ShareWorld, new FakeRecordShareTable()),
            scopes, new Mock<IMembershipEventPublisher>().Object, NullLogger<RecordCreationService>.Instance);
    }

    /// <summary>
    /// The app-only Dataverse (STRICT: any call not set up here fails the test): whole-row and catalog queries answered by the
    /// directory, a team's and a user's row, a business unit's defaults, a mapping source's columns, and the conditional
    /// update — applied to the row only while it is still at the version the repair read.
    /// </summary>
    private IGenericEntityService Entities()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) => Task.FromResult(Directory.Answer(query)));
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken _) => Task.FromResult(Read(table, id, columns)));
        entities
            .Setup(e => e.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) =>
            {
                BeforeWrite?.Invoke();
                BeforeWrite = null;
                var current = Directory.Version(table, id).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var applied = current == version;
                Writes.Add((table, id, version, new Dictionary<string, object>(fields), applied && WriteFault is null));
                if (WriteFault is { } fault)
                    throw fault;
                if (!applied)
                    throw new DBConcurrencyException($"Test: {table} {id} is at version {current}, not {version}.");
                Directory.Write(table, id, fields.ToDictionary(f => f.Key, f => (object?)f.Value));
                return Task.CompletedTask;
            });
        return entities.Object;
    }

    private Entity Read(string table, Guid id, string[] columns)
    {
        switch (table)
        {
            case "team":
            {
                var query = new QueryExpression("team") { ColumnSet = new ColumnSet("businessunitid") };
                query.Criteria.AddCondition("teamid", ConditionOperator.Equal, id);
                return Directory.Answer(query).Entities.Single();
            }

            case "systemuser":
            {
                if (SystemUserReadFault is { } userFault)
                    throw userFault;
                if (MissingSystemUsers.Contains(id))
                    return null!; // no such row: the application-user check cannot tell
                var user = new Entity("systemuser", id);
                if (id == AppUser)
                    user[ApplicationUserCheck.ApplicationIdAttribute] = Guid.Parse("a0160000-0000-4000-8000-0000000000ff");
                return user;
            }

            case "businessunit":
            {
                var unit = new Entity("businessunit", id);
                if (SearchIndexDefaults.TryGetValue(id, out var defaults))
                {
                    if (defaults.Name is { } name)
                        unit["sprk_searchindexname"] = name;
                    if (defaults.Index is { } index)
                        unit["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", index);
                }

                return unit;
            }

            default:
            {
                // A mapping source's columns (the system principal's app-only read).
                if (SourceReadFault is { } readFault)
                    throw readFault;
                var row = Directory.Row(table, id);
                var copy = new Entity(table, id);
                foreach (var column in columns)
                {
                    if (row.Attributes.TryGetValue(column, out var value))
                        copy[column] = value;
                }

                return copy;
            }
        }
    }

    private static FieldMappingProfileEntity Profile(string source, string target, params FieldMappingRuleEntity[] rules) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"{source} to {target}",
        SourceEntity = source,
        TargetEntity = target,
        IsActive = true,
        Rules = [.. rules],
    };

    private static FieldMappingRuleEntity Copy(string source, string target, int order) => new()
    {
        Id = Guid.NewGuid(), Name = $"{source} -> {target}", SourceField = source, SourceFieldType = 0, TargetField = target,
        TargetFieldType = CreateTimeFieldMapping.FieldTypeText, MappingType = CreateTimeFieldMapping.MappingTypeCopy,
        ExecutionOrder = order, IsActive = true,
    };

    /// <summary>The REAL resolver, recording every owner decision asked of it; throwing the fault given, when one is.</summary>
    private sealed class CapturingResolver(
        IRecordOwnershipResolver inner, List<RecordOwnershipContext> asked, Func<Exception?> fault,
        Func<RecordOwnerResolution?> answer) : IRecordOwnershipResolver
    {
        public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct)
        {
            asked.Add(context);
            return fault() is { } thrown ? Task.FromException<Guid?>(thrown) : inner.ResolveOwningTeamAsync(context, ct);
        }

        public Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct)
        {
            asked.Add(context);
            if (fault() is { } thrown)
                return Task.FromException<RecordOwnerResolution>(thrown);
            return answer() is { } dictated ? Task.FromResult(dictated) : inner.ResolveOwnerAsync(context, ct);
        }

        public Task<RecordOwnerResolution> ReparentAsync(
            RecordReparent reparent, Func<CancellationToken, Task> applyChange, CancellationToken ct) =>
            inner.ReparentAsync(reparent, applyChange, ct);
    }
}
