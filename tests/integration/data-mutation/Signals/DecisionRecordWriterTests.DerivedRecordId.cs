using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using KeyedEntry = Sprk.Bff.Api.Services.Signals.DecisionRecordWriter.KeyedEntry;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// ebilling-r1 task 084h-5 (EB-D-247; ontology-r1 option 1, rules 1-6): every KEYED entry point creates its Decision Record with a primary id
/// derived from its key alone (<see cref="DecisionRecordWriter.DecisionRecordIdOf"/>), so two attempts of one decision that both pass the
/// "already recorded" lookup end in one create and one AlreadyRecorded: Dataverse refuses the second create (DuplicateRecord), and the writer
/// re-runs the lookup and applies its rules. The harness is the writer's own (task 149): the lookup's conditions are evaluated against what it
/// holds, and its create refuses a taken primary id with the typed DuplicateRecord fault, as Dataverse does. Every id here comes from
/// <see cref="DecisionRecordWriter.DecisionRecordIdOf"/>, never recomputed, so a change of derivation changes no test. Known limit: derivable
/// record id, see uac-r2 thread.
/// </summary>
public sealed partial class DecisionRecordWriterTests
{
    private static readonly IReadOnlySet<KeyedEntry> NoDerivedRecordIds = new HashSet<KeyedEntry>();

    /// <summary>The record id the gate's record is created with, through the writer's one derivation function.</summary>
    private static Guid GateRecordId(bool authorize = true) => DecisionRecordWriter.DecisionRecordIdOf(GateDecisionKey.IdOf(CommunicationId, authorize));

    /// <summary>One keyed entry point as these tests drive it: its harness (with the given switch), its write, and its key.</summary>
    private sealed record KeyedCase(Func<IReadOnlySet<KeyedEntry>?, Harness> NewHarness, Func<Harness, Task<DecisionRecordWriteResult>> Write, Guid Key);

    private static KeyedCase CaseOf(KeyedEntry entry) => entry switch
    {
        KeyedEntry.Gate => new(
            ids => MatterFiledCommunication(ids ?? DerivedIdOptIn),
            h => h.Writer.WriteGateDecisionAsync(Gate(authorize: true)),
            GateDecisionKey.IdOf(CommunicationId, true)),
        KeyedEntry.ConditionCleared => new(
            ids => ChangedToDo(Colleague, SignalClosed.AddDays(-1), derivedRecordIds: ids),
            h => h.Writer.WriteConditionClearedAsync(Cleared()),
            ConditionClearedRecordKey.IdOf(ClosedSignal, 1)),
        KeyedEntry.Billing => new(
            ids => BillingHarness(derivedRecordIds: ids ?? DerivedIdOptIn),
            h => h.Writer.WriteBillingDecisionAsync(Billing()),
            BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, BillingDecision)),
        KeyedEntry.RecordProcedure => new(
            ids => RecordProcedureHarness(ids),
            h => h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure()),
            RecordProcedureDecisionKey.IdOf("sprk_matter", MatterId, RpDecision)),
        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
    };

    /// <summary>The four keyed entry points, by name (the enum is internal to the writer, so a public test takes its name).</summary>
    public static TheoryData<string> KeyedEntries() => new() { nameof(KeyedEntry.Gate), nameof(KeyedEntry.ConditionCleared), nameof(KeyedEntry.Billing), nameof(KeyedEntry.RecordProcedure) };

    /// <summary>Two attempts of the same decision at the same moment: both reach the lookup before either creates (the harness's barrier).</summary>
    private static async Task<DecisionRecordWriteResult[]> TwoAtOnce(KeyedCase c, Harness h)
    {
        h.HoldKeyedLookupsUntil(2);
        return await Task.WhenAll(Task.Run(() => c.Write(h)), Task.Run(() => c.Write(h)));
    }

    // ── The race: one create, one AlreadyRecorded, for every keyed entry point ──────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(KeyedEntries))]
    public async Task DerivedId_TwoConcurrentAttemptsOfOneDecision_BothPassTheLookup_GiveOneCreateAndOneAlreadyRecorded(string entryName)
    {
        var entry = Enum.Parse<KeyedEntry>(entryName);
        var c = CaseOf(entry);
        var h = c.NewHarness(null);

        var results = await TwoAtOnce(c, h);

        h.Created.Should().ContainSingle("Dataverse refuses the second create of the same derived id");
        h.DuplicateRefusals.Should().Be(1, "both attempts passed the lookup and reached the create");
        h.Row.Id.Should().Be(DecisionRecordWriter.DecisionRecordIdOf(c.Key), "the record carries the id derived from its key");
        results.Count(r => r.AlreadyRecorded).Should().Be(1, "the refused attempt is answered as the existing already-recorded case");
        results.Count(r => !r.AlreadyRecorded).Should().Be(1);
        results.Select(r => r.RecordId).Distinct().Should().Equal(new[] { DecisionRecordWriter.DecisionRecordIdOf(c.Key) },
            "both name the one record");
        h.Lookups.Count(q => q.EntityName == "sprk_decisionrecord").Should().Be(3, "two lookups first, then the refused attempt's re-read");
    }

    [Theory]
    [MemberData(nameof(KeyedEntries))]
    public async Task DerivedId_SwitchedOffForTheEntry_TwoConcurrentAttempts_AreTodaysBehaviour_TwoCreatesWithNoIdSet(string entryName)
    {
        // The switch both ways (ontology-r1): off, the entry creates with no id (Dataverse picks it), so the race writes two records, as before
        // this task. This is also the seeding proof: the race above is real, and only the derived id closes it.
        var entry = Enum.Parse<KeyedEntry>(entryName);
        var c = CaseOf(entry);
        var h = c.NewHarness(NoDerivedRecordIds);

        var results = await TwoAtOnce(c, h);

        h.Created.Should().HaveCount(2, "with no derived id nothing stops the second create");
        h.Created.Should().OnlyContain(e => e.Id == Guid.Empty, "no id is set: Dataverse picks it");
        h.DuplicateRefusals.Should().Be(0);
        results.Should().OnlyContain(r => !r.AlreadyRecorded);
    }

    /// <summary>EB-D-251 (owner option B): the gate and billing are off by default, so a test of their derived-id behaviour opts in through the
    /// writer's <c>DerivedRecordIdEntries</c> seam, with all four entries on.</summary>
    private static readonly IReadOnlySet<KeyedEntry> DerivedIdOptIn = new HashSet<KeyedEntry>
    {
        KeyedEntry.Gate, KeyedEntry.ConditionCleared, KeyedEntry.Billing, KeyedEntry.RecordProcedure,
    };

    [Fact]
    public void DerivedId_TheDefaultSwitch_IsExactlyConditionClearedAndProcedure()
    {
        // EB-D-251 (owner option B): the gate and billing entries keep random record ids and stay off this set. This pins the production default
        // to exactly these two entries, so a third entry cannot be switched on without changing this test and the owner decision it cites.
        // Known limit: derivable record id, see uac-r2 thread.
        DecisionRecordWriter.DerivedRecordIdEntriesByDefault.Should().BeEquivalentTo(
            new[] { KeyedEntry.ConditionCleared, KeyedEntry.RecordProcedure });
        DecisionRecordWriter.DerivedRecordIdEntriesByDefault.Should().NotContain(KeyedEntry.Gate).And.NotContain(KeyedEntry.Billing);
        new Harness().Writer.DerivedRecordIdEntries.Should().BeSameAs(DecisionRecordWriter.DerivedRecordIdEntriesByDefault);
    }

    /// <summary>The entries that keep random ids by default (EB-D-251), by name (the enum is internal to the writer).</summary>
    public static TheoryData<string> RandomIdEntries() => new() { nameof(KeyedEntry.Gate), nameof(KeyedEntry.Billing) };

    [Theory]
    [MemberData(nameof(RandomIdEntries))]
    public async Task DerivedId_MustNotFire_WithTheDefaultSet_AGateOrBillingCreateSetsNoId_AndTwoConcurrentAttemptsBehaveAsBefore(string entryName)
    {
        // EB-D-251 (owner option B): gate and billing records keep random ids. With the production default the create names no primary id, and two
        // attempts that both pass the lookup both create, exactly as before task 084h-5.
        var c = CaseOf(Enum.Parse<KeyedEntry>(entryName));
        var defaults = DecisionRecordWriter.DerivedRecordIdEntriesByDefault;

        var single = c.NewHarness(defaults);
        await c.Write(single);
        single.Row.Id.Should().Be(Guid.Empty, "Dataverse picks the record's id");

        var h = c.NewHarness(defaults);
        var results = await TwoAtOnce(c, h);

        h.Created.Should().HaveCount(2, "with no derived id nothing stops the second create");
        h.Created.Should().OnlyContain(e => e.Id == Guid.Empty);
        h.DuplicateRefusals.Should().Be(0);
        results.Should().OnlyContain(r => !r.AlreadyRecorded);
    }

    [Theory]
    [InlineData(nameof(KeyedEntry.ConditionCleared))]
    [InlineData(nameof(KeyedEntry.RecordProcedure))]
    public async Task DerivedId_TheTwoEntriesThatStayOn_KeepTheDerivedIdUnderTheDefaultSet(string entryName)
    {
        // The seeding proof under the production default: one create under the derived id, one AlreadyRecorded.
        var c = CaseOf(Enum.Parse<KeyedEntry>(entryName));
        var h = c.NewHarness(DecisionRecordWriter.DerivedRecordIdEntriesByDefault);

        var results = await TwoAtOnce(c, h);

        h.Created.Should().ContainSingle();
        h.Row.Id.Should().Be(DecisionRecordWriter.DecisionRecordIdOf(c.Key));
        results.Count(r => r.AlreadyRecorded).Should().Be(1);
    }

    // ── The derivation: the key alone, its own prefix, one function ─────────────────────────────────────────────────────────

    [Fact]
    public void DerivedId_IsAFunctionOfTheKeyAlone_NotTheRawKey_AndDiffersPerKey()
    {
        var key = BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, BillingDecision);

        DecisionRecordWriter.DecisionRecordIdOf(key).Should().Be(DecisionRecordWriter.DecisionRecordIdOf(key), "deterministic");
        DecisionRecordWriter.DecisionRecordIdOf(key).Should().NotBe(key, "never the raw key");
        DecisionRecordWriter.DecisionRecordIdOf(key).Should().NotBe(Guid.Empty);
        DecisionRecordWriter.DecisionRecordIdOf(key).Should().NotBe(
            DecisionRecordWriter.DecisionRecordIdOf(BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, Guid.NewGuid())), "one id per key");
    }

    [Fact]
    public async Task DerivedId_TwoCodesUnderOneKey_DeriveTheSameRecordId_SoTheSecondCodeGetsTheExistingOtherCodeRefusal()
    {
        // Rule 3: the id is derived from the key, never the code. A concurrent approve and reject under one decision id race for ONE id; the
        // loser re-reads and gets the lookup-first path's other-code refusal, never a second record.
        var h = BillingHarness(derivedRecordIds: DerivedIdOptIn);
        h.HoldKeyedLookupsUntil(2);
        var approve = Task.Run(() => h.Writer.WriteBillingDecisionAsync(Billing("approve-invoice")));
        var reject = Task.Run(() => h.Writer.WriteBillingDecisionAsync(Billing("reject-invoice", reason: "Duplicate invoice")));

        var outcomes = await Task.WhenAll(Settle(approve), Settle(reject));

        h.Created.Should().ContainSingle();
        h.Row.Id.Should().Be(DecisionRecordWriter.DecisionRecordIdOf(BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, BillingDecision)));
        var refused = outcomes.Single(o => o.Refusal is not null).Refusal!;
        refused.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        refused.Message.Should().Contain("another billing action");
    }

    private static async Task<(DecisionRecordWriteResult? Result, DecisionRecordRefusedException? Refusal)> Settle(Task<DecisionRecordWriteResult> write)
    {
        try
        {
            return (await write, null);
        }
        catch (DecisionRecordRefusedException ex)
        {
            return (null, ex);
        }
    }

    // ── Rule 4: the duplicate path applies EXACTLY the lookup-first rules ───────────────────────────────────────────────────

    /// <summary>A row a concurrent attempt stored under <paramref name="id"/> just before this attempt's create (the harness's create hook).</summary>
    private static void LandsFirst(Harness h, Func<Guid, Entity> row) => h.BeforeCreate = e =>
    {
        if (e.Id != Guid.Empty && !h.Seeded.Any(s => s.Id == e.Id))
        {
            h.Seeded.Add(row(e.Id));
        }
    };

    private static Entity WithId(Entity row, Guid id)
    {
        row.Id = id;
        return row;
    }

    [Fact]
    public async Task DerivedId_Billing_AConcurrentRowWithTheSameCodeAndHash_IsAlreadyRecorded()
    {
        var h = BillingHarness(derivedRecordIds: DerivedIdOptIn);
        LandsFirst(h, id => WithId(StoredBillingRow(DecisionRecordWriter.BillingDecisionHash(Billing())), id));

        var result = await h.Writer.WriteBillingDecisionAsync(Billing());

        result.AlreadyRecorded.Should().BeTrue();
        result.RecordId.Should().Be(DecisionRecordWriter.DecisionRecordIdOf(BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, BillingDecision)));
        h.Created.Should().BeEmpty();
        h.DuplicateRefusals.Should().Be(1);
    }

    [Fact]
    public async Task DerivedId_Billing_AConcurrentRowWithOtherContent_IsTheOtherContentRefusal_NeverCreateFailed()
    {
        var h = BillingHarness(derivedRecordIds: DerivedIdOptIn);
        LandsFirst(h, id => WithId(StoredBillingRow("1111111111111111111111111111111111111111111111111111111111111111"), id));

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing())).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        ex.Message.Should().Be(DecisionRecordWriter.BillingOtherContentRefusal);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task DerivedId_Billing_AConcurrentRowWithAnotherCode_IsTheOtherCodeRefusal_NeverCreateFailed()
    {
        var h = BillingHarness(derivedRecordIds: DerivedIdOptIn);
        LandsFirst(h, id => WithId(StoredBillingRow(DecisionRecordWriter.BillingDecisionHash(Billing()), code: "reject-invoice"), id));

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing())).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        ex.Message.Should().Contain("another billing action");
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task DerivedId_Billing_AConcurrentRowWithAnUnreadableHash_IsTheUnreadableRefusal_NeverCreateFailed()
    {
        var h = BillingHarness(derivedRecordIds: DerivedIdOptIn);
        LandsFirst(h, id => new Entity("sprk_decisionrecord", id)
        {
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", InvoiceId),
            ["sprk_steps"] = $"{{\"reviewId\":\"{BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, BillingDecision):D}\",\"steps\":[{{\"code\":\"approve-invoice\"}}]}}",
            ["sprk_actioncode"] = "approve-invoice",
            ["createdby"] = new EntityReference("systemuser", WriterUser),
        });

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing())).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        ex.Message.Should().Be(DecisionRecordWriter.BillingContentUnreadableRefusal);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task DerivedId_RecordProcedure_AConcurrentRowWithOtherContent_IsTheOtherContentRefusal_NeverCreateFailed()
    {
        // Two concurrent attempts of one Procedure decision id with different content: the loser gets the lookup-first other-content refusal.
        var h = RecordProcedureHarness();
        h.HoldKeyedLookupsUntil(2);
        var first = Task.Run(() => h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure()));
        var second = Task.Run(() => h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(reason: "a different reason")));

        var outcomes = await Task.WhenAll(Settle(first), Settle(second));

        h.Created.Should().ContainSingle();
        var refused = outcomes.Single(o => o.Refusal is not null).Refusal!;
        refused.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        refused.Message.Should().Be(DecisionRecordWriter.RecordProcedureOtherContentRefusal);
    }

    [Fact]
    public async Task DerivedId_Gate_AConcurrentRowOfTheDecision_IsAlreadyRecorded_WhateverItsContent()
    {
        var h = MatterFiledCommunication(DerivedIdOptIn);
        LandsFirst(h, id => ExistingRecord(id, CommunicationId, GateDecisionKey.IdOf(CommunicationId, true)));

        var result = await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        result.AlreadyRecorded.Should().BeTrue();
        result.RecordId.Should().Be(GateRecordId());
        h.Created.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(KeyedEntries))]
    public async Task DerivedId_TakenByARowTheLookupDoesNotFind_IsTheEntrysLookupRefusal_NeverCreateFailed_AndNothingIsWritten(string entryName)
    {
        // The id is taken but the entry's lookup (the writer's own rows with this key) does not find a record, so its rules cannot answer.
        // Refused with the entry's lookup reason (nothing written; a retry looks again), never CreateFailed, never AlreadyRecorded.
        var entry = Enum.Parse<KeyedEntry>(entryName);
        var c = CaseOf(entry);
        var h = c.NewHarness(null);
        LandsFirst(h, id => new Entity("sprk_decisionrecord", id));

        var ex = (await h.Writer.Invoking(w => c.Write(h)).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(entry switch
        {
            KeyedEntry.Gate => DecisionRecordRefusalReason.GateRecordLookupFailed,
            KeyedEntry.ConditionCleared => DecisionRecordRefusalReason.ConditionClearedLookupFailed,
            KeyedEntry.Billing => DecisionRecordRefusalReason.BillingRecordLookupFailed,
            _ => DecisionRecordRefusalReason.RecordProcedureLookupFailed,
        });
        ex.InnerException.Should().BeOfType<System.ServiceModel.FaultException<OrganizationServiceFault>>("the Dataverse fault is kept");
        h.Created.Should().BeEmpty();
    }

    // ── Rule 6: parity with the lookup-found path (no secure-owner read-back, no participant share) ─────────────────────────

    [Fact]
    public async Task DerivedId_TheDuplicatePath_LikeTheLookupFoundPath_DoesNoSecureOwnerReadBack_AndReportsNoSecureTeam()
    {
        var h = BillingHarness(derivedRecordIds: DerivedIdOptIn);
        h.Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(Secure());
        LandsFirst(h, id => WithId(StoredBillingRow(DecisionRecordWriter.BillingDecisionHash(Billing())), id));

        var result = await h.Writer.WriteBillingDecisionAsync(Billing());

        result.AlreadyRecorded.Should().BeTrue();
        result.SecureOwnerTeamId.Should().BeNull("the lookup-found path reports none either");
        h.Org.Verify(o => o.RetrieveAsync("sprk_decisionrecord", It.IsAny<Guid>(), It.IsAny<ColumnSet>(), It.IsAny<CancellationToken>()), Times.Never,
            "no read-back of a record this attempt did not create");
    }

    // ── Must-not-fire: the review path, and every other fault ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DerivedId_MustNotFire_TheReviewPath_CreatesWithNoIdSet()
    {
        // Rule 2: the review id is the client's, so a derived id there would let a client choose a record id.
        var h = new Harness();

        await h.Writer.WriteAsync(Request());

        h.Row.Id.Should().Be(Guid.Empty, "Dataverse picks the review record's id");
    }

    [Fact]
    public async Task DerivedId_MustNotFire_TheReviewPath_ADuplicateRecordFault_StaysCreateFailed()
    {
        var h = new Harness();
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(Harness.DuplicateRecordFault());

        var ex = (await h.Writer.Invoking(w => w.WriteAsync(Request())).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.CreateFailed);
    }

    [Theory]
    [MemberData(nameof(KeyedEntries))]
    public async Task DerivedId_MustNotFire_AnyOtherCreateFault_OnAKeyedEntry_StaysCreateFailed(string entryName)
    {
        var entry = Enum.Parse<KeyedEntry>(entryName);
        var c = CaseOf(entry);
        var h = c.NewHarness(null);
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        var ex = (await h.Writer.Invoking(w => c.Write(h)).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.CreateFailed);
    }

    [Fact]
    public async Task DerivedId_MustNotFire_APrivilegeFault_OnAKeyedEntry_StaysDataverseAccessDenied()
    {
        var c = CaseOf(KeyedEntry.Billing);
        var h = c.NewHarness(null);
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(
            new System.ServiceModel.FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040220), Message = "no privilege" },
                new System.ServiceModel.FaultReason("no privilege")));

        var ex = (await h.Writer.Invoking(w => c.Write(h)).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.DataverseAccessDenied);
    }

    [Fact]
    public async Task DerivedId_MustNotFire_ADuplicateRecordFault_OnAnEntrySwitchedOff_StaysCreateFailed()
    {
        var c = CaseOf(KeyedEntry.Gate);
        var h = c.NewHarness(NoDerivedRecordIds);
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(Harness.DuplicateRecordFault());

        var ex = (await h.Writer.Invoking(w => c.Write(h)).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.CreateFailed);
    }

    [Fact]
    public async Task DerivedId_TheDuplicateFault_IsRecognisedByItsCodeInTheMessage_WhenNotTyped()
    {
        // Typed first, then the code in the message (as IsDataverseAccessDenied classifies): an untyped wrapper of the same fault is the same answer.
        var c = CaseOf(KeyedEntry.Billing);
        var h = c.NewHarness(null);
        var landed = WithId(StoredBillingRow(DecisionRecordWriter.BillingDecisionHash(Billing())), c.Key);
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity e, CancellationToken _) =>
            {
                h.Seeded.Add(WithId(landed, e.Id));
                return Task.FromException<Guid>(new InvalidOperationException("Dataverse error 0x80040237: Cannot insert duplicate key."));
            });

        var result = await c.Write(h);

        result.AlreadyRecorded.Should().BeTrue();
    }

    // ── Review F1: a gate record whose create committed before the SDK's internal retry was refused still gets its participant share ──

    /// <summary>The gate harness of the D-169 tests: an unfiled communication owned by <see cref="CommunicationOwner"/> and shared with
    /// <see cref="CommunicationSharee"/>, so a created record is shared Read with the sharee (one grant).</summary>
    private static (Harness H, GateSharingWorld World) PrincipalOwnedGateHarness()
    {
        var directory = Standard()
            .WithUser(BasicColleague, Guid.NewGuid(), ChildBu)
            .WithRecord("sprk_communication", CommunicationId, ChildBu);
        var world = SharedCommunication();
        var h = new Harness(realResolver: directory.Resolver(), participantShares: world.Sharing, derivedRecordIds: DerivedIdOptIn);
        CommunicationOwnedBy(h, UserRef(CommunicationOwner));
        return (h, world);
    }

    private static int GrantsOn(GateSharingWorld world, Guid recordId) =>
        world.Calls.Count(c => c.StartsWith($"grant:{GateSharingWorld.Record}:{recordId:D}:", StringComparison.Ordinal));

    [Fact]
    public async Task DerivedId_Gate_TheDuplicatePath_AnsweringThisCallsOwnRecord_RunsTheParticipantShareExactlyOnce()
    {
        // The SDK retried a create that had already committed: the row (this call's own, under the derived id) is stored, the retry is
        // refused with DuplicateRecord, the re-read finds it. The create path never reached its share, so the duplicate path runs it.
        var (h, world) = PrincipalOwnedGateHarness();
        world.WithGateRecord(GateRecordId(), CommunicationId, CommunicationOwner);
        h.BeforeCreate = e =>
        {
            var committed = new Entity(e.LogicalName, e.Id);
            foreach (var attribute in e.Attributes)
            {
                committed[attribute.Key] = attribute.Value;
            }

            h.Created.Add(committed); // the first send committed; the harness then refuses the retry as a duplicate of it
        };

        var result = await h.Writer.WriteGateDecisionAsync(Gate());

        result.AlreadyRecorded.Should().BeTrue();
        result.RecordId.Should().Be(GateRecordId());
        h.DuplicateRefusals.Should().Be(1);
        GrantsOn(world, result.RecordId).Should().Be(1, "the share the create path never reached runs once, for the communication's sharee");
        world.RecordSharees(result.RecordId).Should().Equal(new[] { CommunicationSharee });
        result.SecureOwnerTeamId.Should().BeNull();
        h.Org.Verify(o => o.RetrieveAsync("sprk_decisionrecord", It.IsAny<Guid>(), It.IsAny<ColumnSet>(), It.IsAny<CancellationToken>()), Times.Never,
            "the secure-owner read-back stays skipped on the duplicate path");
    }

    [Fact]
    public async Task DerivedId_MustNotFire_Gate_TheDuplicatePath_AnsweringAnotherRecordsId_RunsNoShare()
    {
        // The derived id is taken by a row the lookup does not find; the lookup finds an older record under another (random) id. That record
        // is not this call's: whoever created it shared it, and this call shares nothing.
        var (h, world) = PrincipalOwnedGateHarness();
        var older = Guid.NewGuid();
        var olderRow = ExistingRecord(older, CommunicationId, GateDecisionKey.IdOf(CommunicationId, true));
        h.BeforeCreate = e =>
        {
            // Both land just before this create (so the lookup-first path found nothing): a row that merely holds the derived id, and the
            // older record the re-read then finds.
            h.Seeded.Add(new Entity("sprk_decisionrecord", e.Id));
            h.Seeded.Add(olderRow);
        };

        var result = await h.Writer.WriteGateDecisionAsync(Gate());

        result.AlreadyRecorded.Should().BeTrue();
        result.RecordId.Should().Be(older).And.NotBe(GateRecordId());
        GrantsOn(world, older).Should().Be(0);
        world.Calls.Should().NotContain(c => c.StartsWith("grant:", StringComparison.Ordinal));
    }

    // ── Review F2: a timeout that surfaces as a cancellation with an uncancelled token is a failed create, not an escape ──────────

    [Theory]
    [MemberData(nameof(KeyedEntries))]
    public async Task DerivedId_ATimeoutCancellation_WithAnUncancelledToken_OnAKeyedCreate_IsCreateFailed(string entryName)
    {
        var c = CaseOf(Enum.Parse<KeyedEntry>(entryName));
        var h = c.NewHarness(null);
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException("The request timed out."));

        var ex = (await h.Writer.Invoking(w => c.Write(h)).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.CreateFailed);
        ex.InnerException.Should().BeOfType<TaskCanceledException>("the cause is kept");
    }

    [Fact]
    public async Task DerivedId_ATimeoutCancellation_WithAnUncancelledToken_OnTheReviewPath_IsCreateFailed()
    {
        var h = new Harness();
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException("The request timed out."));

        var ex = (await h.Writer.Invoking(w => w.WriteAsync(Request())).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.CreateFailed);
    }

    [Fact]
    public async Task DerivedId_MustNotFire_ACancelledToken_OnAKeyedCreate_StillPropagatesTheCancellation()
    {
        var h = MatterFiledCommunication();
        using var cts = new CancellationTokenSource();
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity _, CancellationToken token) =>
            {
                cts.Cancel();
                return Task.FromException<Guid>(new TaskCanceledException("cancelled", null, token));
            });

        await h.Writer.Invoking(w => w.WriteGateDecisionAsync(Gate(), cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DerivedId_MustNotFire_ACancelledToken_OnTheReviewPath_StillPropagatesTheCancellation()
    {
        var h = new Harness();
        using var cts = new CancellationTokenSource();
        h.Org.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity _, CancellationToken token) =>
            {
                cts.Cancel();
                return Task.FromException<Guid>(new TaskCanceledException("cancelled", null, token));
            });

        await h.Writer.Invoking(w => w.WriteAsync(Request(), cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }
}
