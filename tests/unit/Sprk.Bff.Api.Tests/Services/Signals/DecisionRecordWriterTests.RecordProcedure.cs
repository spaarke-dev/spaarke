using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// ebilling-r1 task 084h-1 (EB-D-238; design notes/084h-procedure-change-decision-records-design.md, root CLAUDE.md 6.5 path B as D-100):
/// <see cref="DecisionRecordWriter.WriteRecordProcedureDecisionAsync(RecordProcedureDecisionRecordRequest, CancellationToken)"/>, the additive
/// per-record Procedure entry, and its catalog set <see cref="DecisionActionCatalog.RecordProcedureActions"/>. Each region proves one of the
/// design's tests W1 to W8 (conditions P1 to P5, filing, the two walls, the catalog); P7 (the existing writer tests are untouched) is checked
/// on the diff. The harness is the billing one (<see cref="WithBillingLookups"/>): the lookup's <c>In</c> on <c>sprk_actioncode</c> is evaluated.
/// </summary>
public sealed partial class DecisionRecordWriterTests
{
    private static readonly Guid RpDecision = Guid.Parse("084a1111-0000-4000-8000-0000000000d1");
    private static readonly Guid RpDecider = Guid.Parse("084a1111-0000-4000-8000-0000000000e1");
    private static readonly Guid RpOverride = Guid.Parse("084a1111-0000-4000-8000-0000000000f1");
    private static readonly Guid RpProcedure = Guid.Parse("084a1111-0000-4000-8000-0000000000a1");
    private static readonly Guid RpVersion = Guid.Parse("084a1111-0000-4000-8000-0000000000a2");
    private static readonly Guid RpRule = Guid.Parse("084a1111-0000-4000-8000-0000000000b1");
    private static readonly Guid RpFirm = Guid.Parse("084a1111-0000-4000-8000-0000000000c1");
    private static readonly Guid RpRecordRuleVersion = Guid.Parse("084a1111-0000-4000-8000-0000000000b2");
    private static readonly Guid RpBasedOnVersion = Guid.Parse("084a1111-0000-4000-8000-0000000000b3");

    /// <summary>The seven codes, in catalog order (design section 8).</summary>
    private static readonly string[] RecordProcedureCodes =
        ["assign-procedure", "pin-procedure", "unpin-procedure", "exclude-procedure", "exclude-rule", "change-rule", "remove-override"];

    private static Harness RecordProcedureHarness(IReadOnlySet<DecisionRecordWriter.KeyedEntry>? derivedRecordIds = null) =>
        WithBillingLookups(new Harness(derivedRecordIds: derivedRecordIds));

    /// <summary>The Values the 089 service builds for each code (design section 4.1): the declared parameter codes plus the undeclared ids.
    /// The override row id is always in Values (never in the key).</summary>
    private static Dictionary<string, object?> RecordProcedureValues(string code) => code switch
    {
        "assign-procedure" or "exclude-procedure" => new() { ["procedure"] = RpProcedure, ["override"] = RpOverride },
        "pin-procedure" or "unpin-procedure" => new() { ["procedure"] = RpProcedure, ["version"] = RpVersion, ["override"] = RpOverride },
        "exclude-rule" => new() { ["rule"] = RpRule, ["firm"] = RpFirm, ["override"] = RpOverride },
        "change-rule" => new()
        {
            ["rule"] = RpRule,
            ["firm"] = RpFirm,
            ["override"] = RpOverride,
            ["basedOnVersion"] = RpBasedOnVersion,
            ["recordRuleVersion"] = RpRecordRuleVersion,
        },
        "remove-override" => new() { ["procedure"] = RpProcedure, ["override"] = RpOverride, ["kind"] = "Procedure" },
        _ => new() { ["procedure"] = RpProcedure, ["override"] = RpOverride },
    };

    /// <summary>The POST rule codes carry their required reason; an unpin and a removal carry none (design section 4.3).</summary>
    private static string? RecordProcedureReasonFor(string code) => code is "exclude-rule" or "change-rule" ? "Agreed with the client" : null;

    private static RecordProcedureDecisionRecordRequest RecordProcedure(
        string code = "assign-procedure",
        DecisionRecordRef? subject = null,
        Guid? confirmer = null,
        string role = "procedure-editor",
        IReadOnlyDictionary<string, object?>? values = null,
        string? reason = null,
        bool noReason = false,
        Guid? decisionId = null,
        IReadOnlyDictionary<string, object?>? facts = null) => new(
        decisionId ?? RpDecision,
        code,
        subject ?? new DecisionRecordRef("sprk_matter", MatterId),
        confirmer ?? RpDecider,
        role,
        values ?? RecordProcedureValues(code),
        "Done",
        facts ?? new Dictionary<string, object?> { ["coreTable"] = "sprk_matter", ["recordId"] = MatterId },
        noReason ? null : reason ?? RecordProcedureReasonFor(code));

    private static Dictionary<string, object?> WithNote(string code, object? note)
    {
        var values = RecordProcedureValues(code);
        values["note"] = note;
        return values;
    }

    /// <summary>The written record's one step's values, as stored in <c>sprk_steps</c>.</summary>
    private static JsonElement StoredValues(Entity row)
    {
        using var steps = JsonDocument.Parse(row.GetAttributeValue<string>("sprk_steps"));
        return steps.RootElement.GetProperty("steps")[0].GetProperty("values").Clone();
    }

    /// <summary>A writer over <paramref name="h"/>'s Dataverse doubles with another record-type catalog (the harness's own is fixed).</summary>
    private static DecisionRecordWriter WriterWithRecordTypes(Harness h, ICommunicationDataverseService recordTypes)
    {
        var client = new OntologyWriterDataverseClient(() => h.Org.Object, NullLogger<OntologyWriterDataverseClient>.Instance);
        var coreAncestors = new CoreAncestorResolver(
            h.Entities.Object,
            (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
                CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute), StringComparer.OrdinalIgnoreCase)),
            NullLogger<CoreAncestorResolver>.Instance);
        return new DecisionRecordWriter(client, h.Ownership.Object, coreAncestors, h.Entities.Object, recordTypes, new FakeTimeProvider(Now), h.Log,
            new GateSharingWorld().Sharing);
    }

    // ── W1 (P1): the three entries take disjoint code sets ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RecordProcedureCodeData))]
    public async Task RecordProcedure_W1_AProcedureCodeThroughWriteAsync_IsRefused_AndNothingIsWritten(string code)
    {
        var h = RecordProcedureHarness();

        var ex = (await h.Writer.Invoking(w => w.WriteAsync(Request(steps: [Taken(code)]))).Should()
            .ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.RecordClassUnderivable, "WriteAsync's catalog (TryGet) does not know Procedure codes");
        h.Created.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RecordProcedureCodeData))]
    public async Task RecordProcedure_W1_AProcedureCodeThroughTheBillingEntry_IsRefused_BeforeAnyIo(string code)
    {
        var h = BillingHarness();

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing(code, subject: new DecisionRecordRef("sprk_invoice", InvoiceId))))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.RecordClassUnderivable, "the billing entry's catalog (TryGetBilling) does not know Procedure codes");
        NoIo(h);
    }

    [Theory]
    [InlineData("mark-complete")]       // Signals
    [InlineData("send-budget-inquiry")] // Signals
    [InlineData("approve-invoice")]     // billing
    [InlineData("assign-firm")]         // billing
    [InlineData("condition-cleared")]   // the closure's step, not a catalog action
    [InlineData("Assign-Procedure")]    // codes are case-sensitive
    [InlineData("not-a-code")]
    public async Task RecordProcedure_W1_ASignalsOrBillingOrUnknownCodeThroughTheNewEntry_IsRefused_BeforeAnyIo(string code)
    {
        var h = RecordProcedureHarness();

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure(code, values: RecordProcedureValues("assign-procedure"))))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.RecordClassUnderivable);
        NoIo(h);
    }

    public static TheoryData<string> RecordProcedureCodeData()
    {
        var data = new TheoryData<string>();
        foreach (var code in RecordProcedureCodes)
            data.Add(code);
        return data;
    }

    // ── W2 (P2): only matter, project and work assignment, checked before any I/O ────────────────────────────────────────

    [Theory]
    [InlineData("sprk_invoice")]
    [InlineData("sprk_billingevent")]
    [InlineData("sprk_todo")]
    [InlineData("sprk_servicerequest")]
    public async Task RecordProcedure_W2_ASubjectThatIsNotAFilingCoreRecord_IsRefused_WithZeroReaderCallsAndZeroCreates(string table)
    {
        var h = RecordProcedureHarness();

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure(subject: new DecisionRecordRef(table, Guid.NewGuid()))))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnsupported);
        NoIo(h);
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public async Task RecordProcedure_W2_AMatterProjectOrWorkAssignment_IsWritten(string table)
    {
        var h = RecordProcedureHarness();
        var id = Guid.NewGuid();

        var result = await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(subject: new DecisionRecordRef(table, id)));

        result.AlreadyRecorded.Should().BeFalse();
        result.RecordId.Should().Be(DecisionRecordWriter.DecisionRecordIdOf(RecordProcedureDecisionKey.IdOf(table, id, RpDecision)), "084h-5: the derived record id");
        h.Created.Should().ContainSingle();
    }

    // ── W3 (P3): exactly once per (core record, decision id); only the writer's own record counts ───────────────────────────

    [Fact]
    public async Task RecordProcedure_W3_ARepeatedDecisionId_ReturnsAlreadyRecorded_WithExactlyOneCreate()
    {
        var h = RecordProcedureHarness();

        var first = await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure());
        var second = await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure());

        first.AlreadyRecorded.Should().BeFalse();
        second.AlreadyRecorded.Should().BeTrue();
        second.RecordId.Should().Be(first.RecordId);
        second.ConfirmedByUserId.Should().Be(RpDecider);
        h.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordProcedure_W3_ARowWithTheKeyThatAnotherIdentityCreated_IsNotAlreadyRecorded()
    {
        // The writer's own record, as stored, but created by a person: it must not stop the writer's record being written.
        var source = RecordProcedureHarness();
        await source.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure());
        var foreign = new Entity("sprk_decisionrecord", Guid.NewGuid());
        foreach (var attribute in source.Row.Attributes)
            foreign[attribute.Key] = attribute.Value;
        foreign["createdby"] = new EntityReference("systemuser", Guid.NewGuid());

        var h = RecordProcedureHarness();
        h.Seeded.Add(foreign);

        var result = await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure());

        result.AlreadyRecorded.Should().BeFalse();
        h.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordProcedure_W3_TheSameDecisionIdWithAnotherCode_IsRefused_AndNoSecondRecordIsWritten()
    {
        var h = RecordProcedureHarness();
        await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure("assign-procedure"));

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure("exclude-procedure")))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        h.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordProcedure_W3_TheSameDecisionIdWithAnotherNote_IsRefused_AsOtherContent()
    {
        var h = RecordProcedureHarness();
        await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(values: WithNote("assign-procedure", "first note")));

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure(values: WithNote("assign-procedure", "second note"))))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        ex.Message.Should().Be(DecisionRecordWriter.RecordProcedureOtherContentRefusal);
        ex.IsOtherContent.Should().BeTrue();
        h.Created.Should().ContainSingle();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \n\t ")]
    public async Task RecordProcedure_W3_ANullOrBlankNote_IsTheSameAsNoNote_InTheHashAndInTheRecord(string? note)
    {
        var none = RecordProcedure();
        var blank = RecordProcedure(values: WithNote("assign-procedure", note));

        DecisionRecordWriter.RecordProcedureDecisionHash(blank).Should().Be(DecisionRecordWriter.RecordProcedureDecisionHash(none));
        DecisionRecordWriter.RecordProcedureValuesOf(blank).Should().NotContainKey("note", "a note key is never kept with a null or blank value");

        var h = RecordProcedureHarness();
        await h.Writer.WriteRecordProcedureDecisionAsync(blank);
        StoredValues(h.Row).TryGetProperty("note", out _).Should().BeFalse("no note key is written for a null or blank note");

        // And a repeat without the key is the same change, answered from the first record.
        (await h.Writer.WriteRecordProcedureDecisionAsync(none)).AlreadyRecorded.Should().BeTrue();
        h.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordProcedure_W3_AGivenNote_IsWrittenUnderNote_AndHashed()
    {
        var h = RecordProcedureHarness();

        await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(values: WithNote("assign-procedure", "Client asked for this")));

        StoredValues(h.Row).GetProperty("note").GetString().Should().Be("Client asked for this");
        DecisionRecordWriter.RecordProcedureDecisionHash(RecordProcedure(values: WithNote("assign-procedure", "Client asked for this")))
            .Should().NotBe(DecisionRecordWriter.RecordProcedureDecisionHash(RecordProcedure()));
    }

    [Fact]
    public async Task RecordProcedure_W3_TheOverrideRowId_StaysInValues_NeverInTheKey_SoARemoveAndReAddIsItsOwnRecord()
    {
        // Design 6.2: override row ids recur across episodes (remove, then re-add), so the key is per decision id and the row id is content.
        // Do not move "override" into RecordProcedureDecisionKey: the second episode would then read as "already recorded".
        RecordProcedureDecisionKey.Of("sprk_matter", MatterId, RpDecision)
            .Should().Be($"procedure-dr-sprk_matter-{MatterId:D}-{RpDecision:D}").And.NotContain(RpOverride.ToString("D"));

        var h = RecordProcedureHarness();
        await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure());
        StoredValues(h.Row).GetProperty("override").GetGuid().Should().Be(RpOverride);

        // Same row id, a new change (a new decision id): a second record.
        var secondEpisode = await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(decisionId: Guid.NewGuid()));
        secondEpisode.AlreadyRecorded.Should().BeFalse();
        h.Created.Should().HaveCount(2);
    }

    // ── W4 (P4): a lookup fault writes nothing; each lookup filters its own codes ─────────────────────────────────────────────

    [Fact]
    public async Task RecordProcedure_W4_ALookupFault_WritesNothing_AndIsRefusedAsRecordProcedureLookupFailed()
    {
        var h = RecordProcedureHarness();
        h.LookupFault = new TimeoutException("dataverse");

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure()))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.RecordProcedureLookupFailed);
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordProcedure_W4_AWriterIdentityFault_WritesNothing_AndIsRefusedAsRecordProcedureLookupFailed()
    {
        var h = RecordProcedureHarness();
        h.WhoAmIFault = new InvalidOperationException("whoami");

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure()))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.RecordProcedureLookupFailed);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordProcedure_W4_TheLookupFiltersTheProcedureCodesOnly_AndTheBillingLookupStillFiltersTheBillingCodesOnly()
    {
        var procedure = RecordProcedureHarness();
        await procedure.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure());
        var billing = BillingHarness();
        await billing.Writer.WriteBillingDecisionAsync(Billing());

        static string[] Conditions(QueryExpression q) => q.Criteria.Conditions.Select(c => $"{c.AttributeName} {c.Operator}").ToArray();
        static object[] Codes(QueryExpression q) => q.Criteria.Conditions.Single(c => c.AttributeName == "sprk_actioncode").Values.ToArray();

        var procedureLookup = procedure.Lookups.Should().ContainSingle().Subject;
        Codes(procedureLookup).Should().Equal(RecordProcedureCodes.Order(StringComparer.Ordinal).Cast<object>());
        Conditions(procedureLookup).Should().Equal("sprk_regardingmatter Equal", "sprk_steps Like", "createdby Equal", "createdonbehalfby Null", "sprk_actioncode In");
        procedureLookup.TopCount.Should().Be(10);

        // The billing query after its body moved into the shared helper: the same conditions, in the same order, on the billing codes only.
        var billingLookup = billing.Lookups.Should().ContainSingle().Subject;
        Codes(billingLookup).Should().Equal(DecisionActionCatalog.BillingActions.Select(a => a.Code).Order(StringComparer.Ordinal).Cast<object>());
        Conditions(billingLookup).Should().Equal("sprk_regardinginvoice Equal", "sprk_steps Like", "createdby Equal", "createdonbehalfby Null", "sprk_actioncode In");
        billingLookup.TopCount.Should().Be(10);
    }

    // ── Fix round 1, item 2: the refusals shared with the billing entry (AlreadyRecordedOrRefuse, FindKeyedRecordsAsync) ───────────

    /// <summary>A Procedure record under this test's key as the writer stored it, with <paramref name="hash"/> as its <c>reviewHash</c>
    /// (none when null: the hash cannot be read).</summary>
    private static Entity StoredRecordProcedureRow(string? hash, string code = "assign-procedure") => new("sprk_decisionrecord", Guid.NewGuid())
    {
        ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        ["sprk_steps"] = hash is null
            ? $"{{\"reviewId\":\"{RecordProcedureDecisionKey.IdOf("sprk_matter", MatterId, RpDecision):D}\",\"steps\":[{{\"code\":\"{code}\"}}]}}"
            : $"{{\"reviewId\":\"{RecordProcedureDecisionKey.IdOf("sprk_matter", MatterId, RpDecision):D}\",\"reviewHash\":\"{hash}\",\"steps\":[{{\"code\":\"{code}\"}}]}}",
        ["sprk_actioncode"] = code,
        ["createdby"] = new EntityReference("systemuser", WriterUser),
    };

    [Fact]
    public async Task RecordProcedure_FixRound1_AWriterRecordWithTheSameKeyAndCodeButNoReadableHash_IsTheContentUnreadableRefusal_AndWritesNothing()
    {
        var h = RecordProcedureHarness();
        h.Seeded.Add(StoredRecordProcedureRow(hash: null));

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure()))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        ex.Message.Should().Be(DecisionRecordWriter.RecordProcedureContentUnreadableRefusal, "an unreadable hash proves nothing either way");
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordProcedure_FixRound1_OfTwoRowsOfTheCode_OneUnreadable_TheRowWithThisHashAnswers()
    {
        var h = RecordProcedureHarness();
        var unreadable = StoredRecordProcedureRow(hash: null);
        var mine = StoredRecordProcedureRow(DecisionRecordWriter.RecordProcedureDecisionHash(RecordProcedure()));
        h.Seeded.Add(unreadable);
        h.Seeded.Add(mine);

        var result = await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure());

        result.AlreadyRecorded.Should().BeTrue();
        result.RecordId.Should().Be(mine.Id);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordProcedure_FixRound1_ALookupTimeoutSurfacingAsACancellationTheCallerDidNotAskFor_IsRecordProcedureLookupFailed_AndWritesNothing()
    {
        // As billing's adversarial K4: an HTTP timeout surfaces as TaskCanceledException with the caller's token NOT cancelled.
        var h = RecordProcedureHarness();
        h.LookupFault = new TaskCanceledException("http timeout");

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure(), CancellationToken.None))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.RecordProcedureLookupFailed);
        ex.InnerException.Should().BeOfType<TaskCanceledException>();
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordProcedure_FixRound1_TheCallersOwnCancellationInTheLookup_Propagates_AndWritesNothing()
    {
        var h = RecordProcedureHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        h.LookupFault = new OperationCanceledException(cts.Token);

        await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure(), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>("the caller's own cancellation is never turned into a refusal");

        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    // ── Fix round 1, item 3: Values read back from JSON hash as the inline ones; a JSON null or blank note is no note ──────────────

    /// <summary>The Values after a JSON round trip, as a payload (the 084h-3 repair job) reads them back: each value a JsonElement.</summary>
    private static Dictionary<string, object?> ThroughJsonAsElements(IReadOnlyDictionary<string, object?> values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!
            .ToDictionary(v => v.Key, v => (object?)v.Value, StringComparer.Ordinal);

    /// <summary>The same, as JsonNode values (a JSON null is a null node).</summary>
    private static Dictionary<string, object?> ThroughJsonAsNodes(IReadOnlyDictionary<string, object?> values) =>
        JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.Nodes.JsonNode?>>(JsonSerializer.Serialize(values))!
            .ToDictionary(v => v.Key, v => (object?)v.Value, StringComparer.Ordinal);

    [Theory]
    [InlineData("assign-procedure", false)]
    [InlineData("assign-procedure", true)]
    [InlineData("change-rule", false)]
    [InlineData("change-rule", true)]
    [InlineData("remove-override", true)]
    public void RecordProcedure_FixRound1_ValuesThroughAJsonRoundTrip_HashEqualToTheInlineOnes_WithAndWithoutANote(string code, bool withNote)
    {
        var inline = withNote ? WithNote(code, "Client asked for this") : RecordProcedureValues(code);
        var expected = DecisionRecordWriter.RecordProcedureDecisionHash(RecordProcedure(code, values: inline));

        DecisionRecordWriter.RecordProcedureDecisionHash(RecordProcedure(code, values: ThroughJsonAsElements(inline))).Should().Be(expected);
        DecisionRecordWriter.RecordProcedureDecisionHash(RecordProcedure(code, values: ThroughJsonAsNodes(inline))).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \n\t ")]
    public async Task RecordProcedure_FixRound1_AJsonNullOrBlankNote_IsNoNote_InTheHashAndInTheRecord(string? note)
    {
        var none = RecordProcedure();
        var expected = DecisionRecordWriter.RecordProcedureDecisionHash(none);

        foreach (var values in new[] { ThroughJsonAsElements(WithNote("assign-procedure", note)), ThroughJsonAsNodes(WithNote("assign-procedure", note)) })
        {
            var blank = RecordProcedure(values: values);
            DecisionRecordWriter.RecordProcedureValuesOf(blank).Should().NotContainKey("note", "a JSON null or blank note is no note");
            DecisionRecordWriter.RecordProcedureDecisionHash(blank).Should().Be(expected);
        }

        var h = RecordProcedureHarness();
        await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(values: ThroughJsonAsElements(WithNote("assign-procedure", note))));
        StoredValues(h.Row).TryGetProperty("note", out _).Should().BeFalse("no note key is written for a JSON null or blank note");
        (await h.Writer.WriteRecordProcedureDecisionAsync(none)).AlreadyRecorded.Should().BeTrue("the inline request without a note is the same change");
        h.Created.Should().ContainSingle();
    }

    [Fact]
    public void RecordProcedure_FixRound1_AJsonNoteWithText_IsKept()
    {
        var values = ThroughJsonAsElements(WithNote("assign-procedure", "Client asked for this"));

        DecisionRecordWriter.RecordProcedureValuesOf(RecordProcedure(values: values)).Should().ContainKey("note");
        DecisionRecordWriter.RecordProcedureDecisionHash(RecordProcedure(values: values))
            .Should().NotBe(DecisionRecordWriter.RecordProcedureDecisionHash(RecordProcedure()));
    }

    // ── W5 (P5): human-only; the record carries its confirmer, class and gate tier ───────────────────────────────────────────

    [Fact]
    public async Task RecordProcedure_W5_AnEmptyConfirmer_IsRefused_BeforeAnyIo()
    {
        var h = RecordProcedureHarness();

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure(confirmer: Guid.Empty)))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
    }

    [Fact]
    public async Task RecordProcedure_W5_AnAutomatedDecider_IsRefused_BeforeAnyIo()
    {
        var h = RecordProcedureHarness();

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure(role: "automated: procedure repair")))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
    }

    [Theory]
    [InlineData("assign-procedure", 100000001)] // Routine
    [InlineData("unpin-procedure", 100000001)]  // Routine
    [InlineData("exclude-rule", 100000000)]     // Judgement
    [InlineData("remove-override", 100000000)]  // Judgement
    public async Task RecordProcedure_W5_AUserDecision_RecordsTheConfirmerTheClassTheGateTierAndTheCode(string code, int recordClass)
    {
        var h = RecordProcedureHarness();

        await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(code));

        h.Row.GetAttributeValue<EntityReference>("sprk_confirmedby").Should().BeEquivalentTo(new EntityReference("systemuser", RpDecider));
        h.OwnerAsks.Should().ContainSingle().Which.RequestedBy!.SystemUserId.Should().Be(RpDecider, "the deciding user is the person who asked");
        Option(h.Row, "sprk_recordclass").Should().Be(recordClass);
        Option(h.Row, "sprk_decisionoutcome").Should().Be(100000000, "the change was made");
        h.Row.GetAttributeValue<string>("sprk_gatetier").Should().Be("Tier2b");
        h.Row.GetAttributeValue<string>("sprk_actioncode").Should().Be(code);
        h.Row.GetAttributeValue<string>("sprk_reason").Should().Be(RecordProcedureReasonFor(code));
    }

    [Theory]
    [InlineData("assign-procedure", "procedureId")] // the round-0 draft's key: refused, the declared code is "procedure"
    [InlineData("pin-procedure", "pinnedVersionId")]
    [InlineData("exclude-rule", "policyId")]
    public async Task RecordProcedure_W5_ARequiredParameterUnderAnotherName_IsRefused_BeforeAnyIo(string code, string wrongKey)
    {
        var h = RecordProcedureHarness();
        var declared = code switch { "pin-procedure" => "version", "exclude-rule" => "rule", _ => "procedure" };
        var values = RecordProcedureValues(code);
        values[wrongKey] = values[declared];
        values.Remove(declared);

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure(code, values: values)))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
        h.Writer.Invoking(w => w.PreflightRecordProcedureDecision(RecordProcedure(code, values: values)))
            .Should().Throw<DecisionRecordRefusedException>("the preflight gives the same refusal with no I/O");
    }

    [Theory]
    [InlineData("exclude-rule")]
    [InlineData("change-rule")]
    public async Task RecordProcedure_W5_ARuleExcludeOrChangeWithoutItsReason_IsRefused_BeforeAnyIo(string code)
    {
        var h = RecordProcedureHarness();

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure(code, noReason: true)))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
    }

    [Fact]
    public async Task RecordProcedure_W5_NoFacts_IsRefusedAsFactSnapshotMissing_BeforeAnyIo()
    {
        var h = RecordProcedureHarness();

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure(facts: new Dictionary<string, object?>())))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.FactSnapshotMissing);
        NoIo(h);
    }

    [Fact]
    public async Task RecordProcedure_W5_TheDeciderContact_IsReadOnlyForANewRecord_AndWrittenToAssignedToInternal()
    {
        var h = RecordProcedureHarness();
        var contact = Guid.NewGuid();
        var calls = 0;
        Task<Guid?> Supplier(CancellationToken _) { calls++; return Task.FromResult<Guid?>(contact); }

        await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(), Supplier, CancellationToken.None);
        var repeat = await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(), Supplier, CancellationToken.None);

        calls.Should().Be(1, "a repeat is answered before the contact is read");
        repeat.AlreadyRecorded.Should().BeTrue();
        h.Row.GetAttributeValue<EntityReference>("sprk_assignedtointernal").Should().BeEquivalentTo(new EntityReference("contact", contact));
    }

    // ── W6 (filing): filed under the core record, which is its own core; no ancestor read; one cached type read ──────────────

    [Theory]
    [InlineData("sprk_matter", "sprk_regardingmatter")]
    [InlineData("sprk_project", "sprk_regardingproject")]
    [InlineData("sprk_workassignment", "sprk_regardingworkassignment")]
    public async Task RecordProcedure_W6_TheRecordIsFiledUnderTheCoreRecord_WithItsTypedCorePair_AndTheDerivationReadsNothing(string table, string regarding)
    {
        var h = RecordProcedureHarness();
        var id = Guid.NewGuid();

        await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(subject: new DecisionRecordRef(table, id)));

        h.Row.GetAttributeValue<EntityReference>(regarding).Should().BeEquivalentTo(new EntityReference(table, id), "filed and stamped under the subject");
        h.Row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1[table]);
        h.Row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(id.ToString("D"));
        h.Row.GetAttributeValue<EntityReference>(table).Should().BeEquivalentTo(new EntityReference(table, id), "the typed core column is the decision context");
        h.Entities.Verify(e => e.RetrieveAsync(table, id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never,
            "a core subject is its own core: the CORE branch derives with no read");
        // Fix round 2 (F3-2): the check above rules out only RetrieveAsync(table, id); a derivation read through RetrieveMultipleAsync, or of
        // another table or id, would have passed it. Every reader call the write makes is the one already-recorded lookup, and there is one.
        h.Entities.Invocations.Select(i => i.Method.Name).Should().OnlyContain(
            name => name == nameof(IGenericEntityService.RetrieveMultipleAsync), "the only reader calls are RetrieveMultipleAsync lookups");
        h.Lookups.Should().ContainSingle("the already-recorded lookup is the only read: the CORE derivation reads nothing");
    }

    [Fact]
    public async Task RecordProcedure_W6_RepeatedWritesForOneCoreType_ReadTheRecordTypeCatalogOnce()
    {
        var h = RecordProcedureHarness();

        await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(decisionId: Guid.NewGuid()));
        await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure(decisionId: Guid.NewGuid(), subject: new DecisionRecordRef("sprk_matter", Guid.NewGuid())));

        h.Created.Should().HaveCount(2);
        h.RecordTypes.Queried.Should().Equal(["sprk_matter"], "the shared regarding builder caches a found row for the writer's life");
    }

    [Fact]
    public async Task RecordProcedure_W6_ARecordTypeReadFault_IsFilingFailed_AndWritesNothing()
    {
        var h = RecordProcedureHarness();
        var writer = WriterWithRecordTypes(h, new Tests.TestInfrastructure.RecordTypeCatalogStub(
            (_, _) => Task.FromException<Entity?>(new TimeoutException("record types"))));

        var ex = (await writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure()))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.FilingFailed);
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordProcedure_W6_ACoreTypeWithNoCatalogRow_IsFilingFailed_AndWritesNothing()
    {
        var h = RecordProcedureHarness();
        var writer = WriterWithRecordTypes(h, new Tests.TestInfrastructure.RecordTypeCatalogStub(new Dictionary<string, Guid>()));

        var ex = (await writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedure()))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.FilingFailed);
        h.Created.Should().BeEmpty();
    }

    // ── W7 (two walls): no Signal, no policy version; the owner's parents are exactly the core record ──────────────────────

    [Fact]
    public void RecordProcedure_W7_TheRequestTypeHasNoSignalPolicyVersionOrPrivilegeMember()
    {
        typeof(RecordProcedureDecisionRecordRequest).GetProperties().Select(p => p.Name).Should()
            .NotContain(n => n.Contains("Signal", StringComparison.OrdinalIgnoreCase)
                || n.Contains("PolicyVersion", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Privilege", StringComparison.OrdinalIgnoreCase),
                "design 5 / ontology-r1 Q2: C9 holds by construction; adding one is a contract change ontology-r1 reviews");
    }

    [Fact]
    public async Task RecordProcedure_W7_TheRecordResolvesNoSignal_NamesNoPolicyVersion_AndItsOwnerIsAskedAboutTheCoreRecordOnly()
    {
        var h = RecordProcedureHarness();

        var result = await h.Writer.WriteRecordProcedureDecisionAsync(RecordProcedure("change-rule"));

        result.ResolvedSignalIds.Should().BeEmpty();
        using (var steps = JsonDocument.Parse(h.Row.GetAttributeValue<string>("sprk_steps")))
        {
            steps.RootElement.GetProperty("resolvedSignalIds").GetArrayLength().Should().Be(0);
        }

        h.Row.Contains("sprk_policyversion").Should().BeFalse("ontology-r1 Q4: the new record-rule version is in Values, not the context column");
        h.OwnerAsks.Should().ContainSingle().Which.Parents.Should().Equal(Parent("sprk_matter", MatterId));
    }

    // ── W8 (catalog): the set, its gates and classes, and the Values the service builds pass the preflight ────────────────────

    [Fact]
    public void RecordProcedure_W8_TheSetIsExactlyTheSevenCodes_DisjointFromSignalsAndBilling_EachInternalWithAGate()
    {
        DecisionActionCatalog.RecordProcedureActions.Select(a => a.Code).Should().Equal(RecordProcedureCodes);
        DecisionActionCatalog.RecordProcedureActions.Select(a => a.Code).Should()
            .NotIntersectWith(DecisionActionCatalog.All.Select(a => a.Code))
            .And.NotIntersectWith(DecisionActionCatalog.BillingActions.Select(a => a.Code));

        foreach (var action in DecisionActionCatalog.RecordProcedureActions)
        {
            action.Gate.Should().NotBeNull(because: $"{action.Code}: the writer refuses a code with no gate");
            action.Gate!.ExternallyVisible.Should().BeFalse(because: $"{action.Code}: the record stays internal (design 10)");
            action.PlanLane.Should().BeNull(because: $"{action.Code}: no Signals plan offers it");
            action.IsNextStep.Should().BeFalse(because: action.Code);
            action.WorkType.Should().BeNull(because: action.Code);
            action.Excludes.Should().BeEmpty(because: action.Code);
            action.Parameters.Should().Contain(p => p.Code == "note" && !p.Required, because: $"{action.Code}: the optional note (EB-D-236)");
            action.Code.Should().NotContainAny(["_", "%", "["], "the lookup matches the key inside a LIKE pattern");
        }
    }

    [Theory]
    [InlineData("assign-procedure", DecisionRecordClass.Routine)]
    [InlineData("pin-procedure", DecisionRecordClass.Routine)]
    [InlineData("unpin-procedure", DecisionRecordClass.Routine)]
    [InlineData("exclude-procedure", DecisionRecordClass.Judgement)]
    [InlineData("exclude-rule", DecisionRecordClass.Judgement)]
    [InlineData("change-rule", DecisionRecordClass.Judgement)]
    [InlineData("remove-override", DecisionRecordClass.Judgement)]
    public void RecordProcedure_W8_EachCode_HasTheProposedClass_AndTier2b(string code, DecisionRecordClass recordClass)
    {
        DecisionActionCatalog.TryGetRecordProcedure(code, out var action).Should().BeTrue();

        action.RecordClass.Should().Be(recordClass, "design 8 (owner question Q7, as drafted)");
        DecisionGateTier.Resolve(action.Gate!).Should().Be(GateRiskTier.Tier2b);
        action.Gate!.Reversible.Should().BeTrue();
        action.Gate.RecordOfTruthImpact.Should().BeTrue();
    }

    [Theory]
    [InlineData("assign-procedure", "procedure")]
    [InlineData("pin-procedure", "procedure", "version")]
    [InlineData("unpin-procedure", "procedure", "version")]
    [InlineData("exclude-procedure", "procedure")]
    [InlineData("exclude-rule", "rule", "reason")]
    [InlineData("change-rule", "rule", "reason")]
    [InlineData("remove-override")]
    public void RecordProcedure_W8_EachCode_RequiresExactlyTheStatedParameters_AndTheServicesValuesCarryThemUnderTheirCodes(string code, params string[] required)
    {
        DecisionActionCatalog.TryGetRecordProcedure(code, out var action).Should().BeTrue();

        action.Parameters.Where(p => p.Required).Select(p => p.Code).Should().Equal(required);
        foreach (var parameter in required.Where(r => r != "reason"))
            RecordProcedureValues(code).Should().ContainKey(parameter, "one name each: the Values key is the declared code");
        new Harness().Writer.Invoking(w => w.PreflightRecordProcedureDecision(RecordProcedure(code)))
            .Should().NotThrow("the Values design 4.1 builds for the code pass the writer's own preflight");
    }

    [Fact]
    public void RecordProcedure_W8_TryGetRecordProcedure_KnowsOnlyTheSevenCodes_AndTheOtherSetsDoNotKnowThem()
    {
        foreach (var code in RecordProcedureCodes)
        {
            DecisionActionCatalog.TryGetRecordProcedure(code, out _).Should().BeTrue(code);
            DecisionActionCatalog.TryGet(code, out _).Should().BeFalse(code);
            DecisionActionCatalog.TryGetBilling(code, out _).Should().BeFalse(code);
        }

        DecisionActionCatalog.TryGetRecordProcedure("mark-complete", out _).Should().BeFalse();
        DecisionActionCatalog.TryGetRecordProcedure("approve-invoice", out _).Should().BeFalse();
        DecisionActionCatalog.TryGetRecordProcedure("Assign-Procedure", out _).Should().BeFalse();
        DecisionActionCatalog.TryGetRecordProcedure(null, out _).Should().BeFalse();
    }

    // ── Fix round 2 (ontology-r1 review) ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The writer's <c>ReasonMaxLength</c> (a private constant: sprk_reason's column size).</summary>
    private const int RecordProcedureReasonMaxLength = 2000;

    /// <summary>A request that breaks exactly one of the pre-I/O checks, by case name.</summary>
    private static RecordProcedureDecisionRecordRequest RecordProcedureBreaking(string brokenCheck) => brokenCheck switch
    {
        "reason-too-long" => RecordProcedure(reason: new string('r', RecordProcedureReasonMaxLength + 1)),
        "decision-id-empty" => RecordProcedure(decisionId: Guid.Empty),
        "role-empty" => RecordProcedure(role: ""),
        "role-blank" => RecordProcedure(role: "   "),
        "outcome-empty" => RecordProcedure() with { Outcome = "" },
        "outcome-blank" => RecordProcedure() with { Outcome = " \t " },
        _ => throw new ArgumentOutOfRangeException(nameof(brokenCheck), brokenCheck, null),
    };

    // F3-1: each pre-I/O refusal is its own test. Without the reason-length check a 2001-char reason fails AFTER I/O, in the create, as a
    // retryable CreateFailed; the others would reach the lookup. Each case must answer ReviewInvalid with the writer having touched nothing.
    [Theory]
    [InlineData("reason-too-long")]
    [InlineData("decision-id-empty")]
    [InlineData("role-empty")]
    [InlineData("role-blank")]
    [InlineData("outcome-empty")]
    [InlineData("outcome-blank")]
    public async Task RecordProcedure_FixRound2_APreIoRefusal_IsReviewInvalid_WithNoIo_AndThePreflightRefusesIt_TheSame(string brokenCheck)
    {
        var h = RecordProcedureHarness();

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(RecordProcedureBreaking(brokenCheck)))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
        h.Writer.Invoking(w => w.PreflightRecordProcedureDecision(RecordProcedureBreaking(brokenCheck)))
            .Should().Throw<DecisionRecordRefusedException>().Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
    }

    [Fact]
    public void RecordProcedure_FixRound2_ThePreflight_RefusesAReasonOverTheColumnSize_AndPassesOneAtTheLimit()
    {
        var h = RecordProcedureHarness();

        h.Writer.Invoking(w => w.PreflightRecordProcedureDecision(RecordProcedure(reason: new string('r', RecordProcedureReasonMaxLength + 1))))
            .Should().Throw<DecisionRecordRefusedException>().Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        h.Writer.Invoking(w => w.PreflightRecordProcedureDecision(RecordProcedure(reason: new string('r', RecordProcedureReasonMaxLength))))
            .Should().NotThrow("the limit itself is a valid reason");
        NoIo(h);
    }

    // Item 3: a note is text. A JSON value that is not a string is not a blank note (and must not throw while it is classified), and it is not
    // a valid note: it is refused as ReviewInvalid before any I/O, never written and hashed as if it were text.
    public static TheoryData<string> NonStringNoteKinds => new() { "json-number", "json-object", "json-array", "json-bool", "node-number", "node-guid", "node-object", "clr-int" };

    private static object NonStringNote(string kind) => kind switch
    {
        "json-number" => JsonDocument.Parse("5").RootElement.Clone(),
        "json-object" => JsonDocument.Parse("""{"text":"x"}""").RootElement.Clone(),
        "json-array" => JsonDocument.Parse("""["x"]""").RootElement.Clone(),
        "json-bool" => JsonDocument.Parse("true").RootElement.Clone(),
        "node-number" => System.Text.Json.Nodes.JsonValue.Create(5),
        // A JsonValue over a Guid reports the String kind (it serializes as a string) but is not a string: GetValue<string>() throws on it.
        "node-guid" => System.Text.Json.Nodes.JsonValue.Create(Guid.NewGuid())!,
        "node-object" => new System.Text.Json.Nodes.JsonObject { ["text"] = "x" },
        "clr-int" => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    [Theory]
    [MemberData(nameof(NonStringNoteKinds))]
    public void RecordProcedure_FixRound2_ANonStringNote_IsNotBlank_AndDoesNotThrowWhileItIsClassified(string kind)
    {
        var request = RecordProcedure(values: WithNote("assign-procedure", NonStringNote(kind)));

        var kept = () => DecisionRecordWriter.RecordProcedureValuesOf(request);

        kept.Should().NotThrow().Which.Should().ContainKey("note", "a non-string note is not a blank one");
    }

    [Theory]
    [MemberData(nameof(NonStringNoteKinds))]
    public async Task RecordProcedure_FixRound2_ANonStringNote_IsRefusedAsReviewInvalid_BeforeAnyIo_AndByThePreflight(string kind)
    {
        var h = RecordProcedureHarness();
        var request = RecordProcedure(values: WithNote("assign-procedure", NonStringNote(kind)));

        var ex = (await h.Writer.Invoking(w => w.WriteRecordProcedureDecisionAsync(request))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
        h.Writer.Invoking(w => w.PreflightRecordProcedureDecision(request))
            .Should().Throw<DecisionRecordRefusedException>().Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
    }

    [Fact]
    public async Task RecordProcedure_FixRound2_ATextNote_ThroughAJsonValue_IsStillWritten()
    {
        var h = RecordProcedureHarness();

        await h.Writer.WriteRecordProcedureDecisionAsync(
            RecordProcedure(values: WithNote("assign-procedure", System.Text.Json.Nodes.JsonValue.Create("Client asked for this"))));

        StoredValues(h.Row).GetProperty("note").GetString().Should().Be("Client asked for this");
    }
}
