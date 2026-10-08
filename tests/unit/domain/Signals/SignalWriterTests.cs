using System.Diagnostics.Metrics;
using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.PowerPlatform.Dataverse.Client;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Ownership = Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // reuse CapturingLogger<T>/LogEntry (internal, same assembly)
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Unit tests for <see cref="SignalWriter"/> (task 030; rework 2026-10-04 after the independent review's
/// live-verified findings — see <c>notes/030-signal-writer-progress.md</c>).
/// </summary>
/// <remarks>
/// <para>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): pure logic over two narrow seams — the
/// Dataverse SDK's OWN <see cref="IOrganizationServiceAsync2"/> (via <see cref="OntologyWriterDataverseClient"/>'s
/// internal test constructor) and the existing, already-multiply-consumed <see cref="IGenericEntityService"/>
/// for the sysadmin BU read. Both faked with small in-memory directories, not mocks — same style as
/// <c>RecordOwnershipResolverTests</c>.</para>
/// <para>The create-first + reconcile-on-duplicate flow is the load-bearing behavior under test: a resolved
/// Signal must STAY resolved after re-evaluation (only <c>sprk_lastevaluated</c> may change), and
/// <c>sprk_firstdetected</c> must be set in the SAME create call as everything else, never a second
/// round-trip.</para>
/// </remarks>
public class SignalWriterTests
{
    private static readonly Guid PolicyId = Guid.Parse("4d204810-61bf-f111-aaaf-0022482913fc");
    private static readonly Guid PolicyVersionId = Guid.Parse("42b3e716-61bf-f111-aaaf-0022482913fc");
    private static readonly Guid MatterId = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");
    private static readonly Guid CommunicationId = Guid.Parse("f00b6389-8fbf-f111-aaaf-0022482913fc");
    private static readonly Guid BusinessUnitId = Guid.Parse("06fbf21c-1872-f011-b4cb-7c1e52671ad0");

    private static readonly DateTimeOffset FirstRun = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondRun = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    // =====================================================================================
    // Create-first (F1/F4/F5/F6)
    // =====================================================================================

    [Fact]
    public async Task WriteAsync_FirstRun_Creates_SetsFirstDetectedInTheSameCreateCall()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var result = await writer.WriteAsync(MatterSubjectRequest());

        result.Created.Should().BeTrue();
        writerOrg.CreateCallCount.Should().Be(1, "firstdetected must be set in the SAME create call, never a second round-trip");
        var row = writerOrg.Rows[result.SignalId];
        row.GetAttributeValue<DateTime?>("sprk_firstdetected").Should().Be(FirstRun.UtcDateTime);
        row.GetAttributeValue<DateTime?>("sprk_lastevaluated").Should().Be(FirstRun.UtcDateTime);
        row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(100000000, "sprk_signalstatus: Open");
    }

    [Fact]
    public async Task WriteAsync_ConcurrentDuplicateKeyCreate_BecomesAnUpdate_NotAThrow()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var request = MatterSubjectRequest();
        var dedupeKey = SignalWriter.BuildDedupeKey(
            request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant());
        var existingId = writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun));
        var result = await writer.WriteAsync(request);

        result.Created.Should().BeFalse("the create lost the dedupe race; this must reconcile, not throw");
        result.SignalId.Should().Be(existingId);
    }

    [Fact]
    public async Task WriteAsync_ReEvaluation_UpdatesOnlyLastEvaluated_NeverResendsDetectionTimeFields()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var request = MatterSubjectRequest();
        var dedupeKey = SignalWriter.BuildDedupeKey(
            request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant());
        writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun));
        await writer.WriteAsync(request);

        writerOrg.UpdateCalls.Should().HaveCount(1);
        writerOrg.UpdateCalls[0].Fields.Keys.Should().BeEquivalentTo(new[] { "sprk_lastevaluated" },
            "re-evaluation must never re-send status/firstdetected/owningbusinessunit/policyversion/factsnapshot/sentence — those are detection-time facts");
    }

    [Fact]
    public async Task WriteAsync_ReEvaluation_AResolvedSignalStaysResolved()
    {
        const int resolved = 100000002; // sprk_signalstatus: Resolved
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var request = MatterSubjectRequest();
        var dedupeKey = SignalWriter.BuildDedupeKey(
            request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant());
        var existingId = writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_signalstatus"] = new OptionSetValue(resolved),
            ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun));
        await writer.WriteAsync(request);

        writerOrg.Rows[existingId].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(resolved,
            "re-fire-after-resolution is the EVALUATOR's decision (task 031), never this writer's");
    }

    // =====================================================================================
    // ADR-024 subject write: typed lookup + denormalized trio, together
    // =====================================================================================

    [Fact]
    public async Task WriteAsync_MatterSubject_SetsRegardingMatterAndMatterToTheSameId()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var result = await writer.WriteAsync(MatterSubjectRequest());
        var row = writerOrg.Rows[result.SignalId];

        row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId, "the subject IS the matter");
        row.GetAttributeValue<string>("sprk_regardingrecordtype").Should().Be("sprk_matter");
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(MatterId.ToString("D").ToLowerInvariant());
    }

    [Fact]
    public async Task WriteAsync_CommunicationSubject_DerivesMatterFromRegardingMatter_SetsTypedLookupAndTrio()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        writerOrg.StubRetrieve("sprk_communication", CommunicationId, new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        });
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var result = await writer.WriteAsync(CommunicationSubjectRequest());
        var row = writerOrg.Rows[result.SignalId];

        result.GroupingMatterId.Should().Be(MatterId, "a Signal about a communication on matter X groups under X");
        row.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(CommunicationId);
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);
        row.Attributes.Keys.Should().NotContain("sprk_regardingmatter", "ADR-024: at most one specific regarding lookup");
    }

    // =====================================================================================
    // FR-14 (F3/F22): owningbusinessunit, never ownerid=team
    // =====================================================================================

    [Fact]
    public async Task WriteAsync_SetsOwningBusinessUnitFromGroupingMatter_OwnerLeftDefault()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var result = await writer.WriteAsync(MatterSubjectRequest());
        var row = writerOrg.Rows[result.SignalId];

        row.GetAttributeValue<EntityReference>("owningbusinessunit").Id.Should().Be(BusinessUnitId);
        row.Attributes.Keys.Should().NotContain("ownerid", "team ownership is CONFIRMED unworkable live (403 0x80040299); owner stays the writer (the default)");
        result.OwningBusinessUnitId.Should().Be(BusinessUnitId);
    }

    [Fact]
    public async Task WriteAsync_ReEvaluation_ReportsEmptyOwningBusinessUnit_OwnershipNotTouched()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var request = MatterSubjectRequest();
        var dedupeKey = SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant());
        // R1: the reconcile path now re-checks owningbusinessunit, so the seeded row must carry the CORRECT
        // BU for this test's premise (ownership is not TOUCHED on reconcile, but it IS still verified) — a
        // row with no BU at all would (correctly) escalate under the test right below this one.
        writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun));
        var result = await writer.WriteAsync(request);

        result.OwningBusinessUnitId.Should().Be(Guid.Empty, "ownership is not RE-ASSIGNED on reconcile, even though it was just verified");
    }

    [Fact]
    public async Task WriteAsync_ReconcileFindsWrongBusinessUnit_Escalates_NotSuccess()
    {
        // R1 (second independent review): the wrong-BU row was never re-checked on reconcile in the first
        // rework -- only on the original create. This proves the fix: a Signal whose stored BU has drifted
        // (e.g. its matter was reassigned) since its first create is caught on the NEXT re-evaluation too.
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var request = MatterSubjectRequest();
        var dedupeKey = SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant());
        var wrongBusinessUnitId = Guid.NewGuid();
        writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["owningbusinessunit"] = new EntityReference("businessunit", wrongBusinessUnitId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, failureCount) = ListenFailuresScoped(scope);
        using var _ = listener;
        var logger = new CapturingLogger<SignalWriter>();

        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun), logger);
        var act = async () => await writer.WriteAsync(request);

        await act.Should().ThrowAsync<SignalWriterEscalationException>("a wrong-BU row must escalate, not report a quiet success");
        failureCount().Should().Be(1);
        writerOrg.UpdateCalls.Should().BeEmpty("lastevaluated must not be touched when the BU check fails -- the row is wrong, not merely stale");

        var errorEntry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        errorEntry.EventId.Id.Should().Be(OntologyWriterEvents.WriteRefused.Id);
        errorEntry.Field("Reason").Should().Be(OntologyWriterFailureReason.OwningBusinessUnitMismatch);
    }

    [Fact]
    public async Task WriteAsync_CreatedRowOwningBusinessUnitMismatch_Escalates()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService
        {
            ForcedOwningBusinessUnitOnVerifyRetrieve = Guid.NewGuid(), // simulates RecordOwnershipAcrossBusinessUnits disabled
        };
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var act = async () => await writer.WriteAsync(MatterSubjectRequest());

        await act.Should().ThrowAsync<SignalWriterEscalationException>();
    }

    [Fact]
    public async Task WriteAsync_MatterWithNoOwningBusinessUnit_Escalates()
    {
        var sysadmin = new FakeSysadminClient(); // no BU stubbed for MatterId
        var writerOrg = new FakeOrganizationService();
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var act = async () => await writer.WriteAsync(MatterSubjectRequest());

        await act.Should().ThrowAsync<SignalWriterEscalationException>();
        writerOrg.Rows.Should().BeEmpty();
    }

    // =====================================================================================
    // Escalation: grouping matter cannot be derived
    // =====================================================================================

    [Fact]
    public async Task WriteAsync_SubjectTypeWithNoVerifiedMatterDerivation_Escalates_AndWritesNothing()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));
        var request = MatterSubjectRequest() with { SubjectEntityLogicalName = "sprk_project", SubjectId = Guid.NewGuid() };

        var act = async () => await writer.WriteAsync(request);

        (await act.Should().ThrowAsync<SignalWriterEscalationException>()).WithMessage("*sprk_project*");
        writerOrg.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_CommunicationWithNoRegardingMatter_Escalates_AndWritesNothing()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        writerOrg.StubRetrieve("sprk_communication", CommunicationId, new Entity("sprk_communication", CommunicationId));
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var act = async () => await writer.WriteAsync(CommunicationSubjectRequest());

        await act.Should().ThrowAsync<SignalWriterEscalationException>();
        writerOrg.Rows.Should().BeEmpty();
    }

    // =====================================================================================
    // §0.3 (F8/F9): exactly three checks
    // =====================================================================================

    [Fact]
    public void RenderSentence_AllTokensPresentInFactSnapshot_Substitutes()
    {
        var facts = new Dictionary<string, object?> { ["category"] = "Fee / rate change", ["count"] = 3 };

        var sentence = SignalWriter.RenderSentence("A {{category}} signal fired ({{count}} evidence items).", facts);

        sentence.Should().Be("A Fee / rate change signal fired (3 evidence items).");
    }

    [Fact]
    public void RenderSentence_TokenNotInFactSnapshot_Throws()
    {
        var facts = new Dictionary<string, object?> { ["category"] = "Fee / rate change" };

        var act = () => SignalWriter.RenderSentence("No revision since {{windowStart}}.", facts);

        act.Should().Throw<SignalSentenceTemplateException>().WithMessage("*windowStart*");
    }

    [Fact]
    public void RenderSentence_NullFactValue_Throws()
    {
        var facts = new Dictionary<string, object?> { ["category"] = null };

        var act = () => SignalWriter.RenderSentence("Category: {{category}}.", facts);

        act.Should().Throw<SignalSentenceTemplateException>().WithMessage("*null*");
    }

    [Fact]
    public void RenderSentence_SubstitutedValueLooksLikeALeftoverToken_Throws()
    {
        // The substituted VALUE itself contains "{{...}}" text, so the rendered output still matches the token
        // pattern even though every key was resolved — the third §0.3 check exists exactly for this case.
        var facts = new Dictionary<string, object?> { ["note"] = "see {{other}}" };

        var act = () => SignalWriter.RenderSentence("Note: {{note}}", facts);

        act.Should().Throw<SignalSentenceTemplateException>().WithMessage("*unsubstituted*");
    }

    // Task 022 rework round 2, finding F5: the "both sides" half of review finding #3 -- the RENDERER refuses the
    // same malformed shapes PolicyVersionValidator refuses, even when every well-formed key is present, so a
    // policy version that skipped validation still cannot render stray brace text into a Signal (F12 adds the
    // lone-literal-brace and full-width-brace shapes).
    [Theory]
    [InlineData("Flagged by {{sprk-x}}.")]
    [InlineData("Flagged by {{a b}}.")]
    [InlineData("Flagged by {{}}.")]
    [InlineData("Flagged by {{x")]
    [InlineData("Flagged by {{{x}}}")]
    [InlineData("Flagged (see {policy}).")]
    [InlineData("Flagged by \uFF5B\uFF5Bx\uFF5D\uFF5D.")]
    public void RenderSentence_MalformedPlaceholder_Throws(string template)
    {
        var facts = new Dictionary<string, object?> { ["x"] = "v", ["a"] = "v", ["b"] = "v", ["policy"] = "v" };

        var act = () => SignalWriter.RenderSentence(template, facts);

        act.Should().Throw<SignalSentenceTemplateException>().WithMessage("*malformed*");
    }

    [Fact]
    public async Task WriteAsync_TemplateReferencesUnreadField_Escalates_AndWritesNothing()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));
        var request = MatterSubjectRequest() with { MessageTemplate = "Saw {{neverRead}}." };

        var act = async () => await writer.WriteAsync(request);

        await act.Should().ThrowAsync<SignalSentenceTemplateException>();
        writerOrg.Rows.Should().BeEmpty();
    }

    // =====================================================================================
    // F21: schema length guards
    // =====================================================================================

    [Fact]
    public async Task WriteAsync_PolicyCodeTooLong_ThrowsArgumentException()
    {
        var writer = Build(new FakeOrganizationService(), new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId), new FakeTimeProvider(FirstRun));
        var request = MatterSubjectRequest() with { PolicyCode = new string('x', 51) };

        var act = async () => await writer.WriteAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_ShortHeadlineTooLong_ThrowsArgumentException()
    {
        var writer = Build(new FakeOrganizationService(), new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId), new FakeTimeProvider(FirstRun));
        var request = MatterSubjectRequest() with { ShortHeadline = new string('x', 201) };

        var act = async () => await writer.WriteAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_RenderedSentenceTooLong_ThrowsArgumentException()
    {
        var writer = Build(new FakeOrganizationService(), new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId), new FakeTimeProvider(FirstRun));
        var request = MatterSubjectRequest() with
        {
            MessageTemplate = "{{longFact}}",
            FactValues = new Dictionary<string, object?> { ["longFact"] = new string('x', 2001) },
        };

        var act = async () => await writer.WriteAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // =====================================================================================
    // Argument guards (not escalations — caller bugs)
    // =====================================================================================

    [Theory]
    [InlineData(999999999)]
    [InlineData(0)]
    public async Task WriteAsync_InvalidLane_ThrowsArgumentException(int invalidLane)
    {
        var writer = Build(new FakeOrganizationService(), new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId), new FakeTimeProvider(FirstRun));
        var request = MatterSubjectRequest() with { Lane = invalidLane };

        var act = async () => await writer.WriteAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_InvalidSeverity_ThrowsArgumentException()
    {
        var writer = Build(new FakeOrganizationService(), new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId), new FakeTimeProvider(FirstRun));
        var request = MatterSubjectRequest() with { Severity = 999999999 };

        var act = async () => await writer.WriteAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // =====================================================================================
    // sprk_factsnapshot — detection-time facts, written at create
    // =====================================================================================

    [Fact]
    public async Task WriteAsync_WritesFactSnapshotVerbatim_OnCreate()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var result = await writer.WriteAsync(MatterSubjectRequest());

        writerOrg.Rows[result.SignalId].GetAttributeValue<string>("sprk_factsnapshot").Should().Contain("Fee / rate change");
    }

    // =====================================================================================
    // Task 039 (D-33): ownership from uac-r2's resolver — secure, not secure, refused
    // =====================================================================================

    [Fact]
    public async Task WriteAsync_SecureOwner_SetsTheSecureTeamInTheCreate_SendsNoOwningBusinessUnit_ReadsTheTeamBack()
    {
        var sysadmin = new FakeSysadminClient(); // no matter BU stubbed: the secure path must not read it
        var writerOrg = new FakeOrganizationService();
        writerOrg.StubRetrieve("sprk_communication", CommunicationId, new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        });
        var ownership = new FakeOwnershipResolver { Answer = SecureAnswer() };
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: ownership);

        var result = await writer.WriteAsync(CommunicationSubjectRequest());

        var context = ownership.Contexts.Should().ContainSingle().Subject;
        context.TargetEntityLogicalName.Should().Be("sprk_matter");
        context.TargetRecordId.Should().Be(MatterId);
        context.Parents.Should().Equal(new Ownership.RecordOwnershipParent("sprk_communication", CommunicationId));

        writerOrg.CreateCallCount.Should().Be(1);
        var created = writerOrg.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", SecureTeamId),
            "the owner is set IN the create, never by a later Assign");
        created.Attributes.Keys.Should().NotContain("owningbusinessunit", "it derives from the Secure Record Owners team");
        writerOrg.RetrievedColumns.Should().Contain(c => c.Entity == "sprk_signal" && c.Columns.Contains("owningteam"),
            "the owner is verified by reading owningteam back");
        result.SecureOwnerTeamId.Should().Be(SecureTeamId);
        result.OwningBusinessUnitId.Should().Be(SecureBusinessUnitId, "reported from the read-back, not computed");
    }

    [Fact]
    public async Task WriteAsync_SecureOwner_ReadBackShowsAnotherOwner_Escalates()
    {
        // Contract item "verify by reading back owningteam": a read-back that disagrees must refuse, not pass.
        var writerOrg = new FakeOrganizationService { ForcedOwningTeamOnVerifyRetrieve = Guid.NewGuid() };
        var writer = Build(writerOrg, new FakeSysadminClient(), new FakeTimeProvider(FirstRun),
            ownership: new FakeOwnershipResolver { Answer = SecureAnswer() });

        var act = async () => await writer.WriteAsync(MatterSubjectRequest());

        (await act.Should().ThrowAsync<SignalWriterEscalationException>())
            .Which.Reason.Should().Be(OntologyWriterFailureReason.SecureOwnerMismatch);
    }

    [Fact]
    public async Task WriteAsync_SecureOwner_ReEvaluation_ExistingRowOwnedByTheSecureTeam_RefreshesLastEvaluated()
    {
        // Review B-2: the reconcile path checks a secure row by owningteam. The secure path resolves no matter BU, so a
        // business-unit check here (secureTeamId not passed) would compare against Guid.Empty and refuse this row.
        var writerOrg = new FakeOrganizationService();
        var request = MatterSubjectRequest();
        var dedupeKey = SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant());
        var existingId = writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["owningteam"] = new EntityReference("team", SecureTeamId),
            ["owningbusinessunit"] = new EntityReference("businessunit", SecureBusinessUnitId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;
        var writer = Build(writerOrg, new FakeSysadminClient(), new FakeTimeProvider(SecondRun),
            ownership: new FakeOwnershipResolver { Answer = SecureAnswer() });

        var result = await writer.WriteAsync(request);

        result.Created.Should().BeFalse();
        result.SignalId.Should().Be(existingId);
        writerOrg.UpdateCalls.Should().ContainSingle().Which.Fields.Keys.Should().BeEquivalentTo(new[] { "sprk_lastevaluated" });
    }

    [Fact]
    public async Task WriteAsync_SecureOwner_ReEvaluation_ExistingRowOwnedByAnotherTeam_EscalatesSecureOwnerMismatch_NoUpdate()
    {
        // Review B-2: a secure Signal that reads back with another owner is refused as secure_owner_mismatch (not a
        // business-unit mismatch) and is not touched.
        var writerOrg = new FakeOrganizationService();
        var request = MatterSubjectRequest();
        var dedupeKey = SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant());
        writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["owningteam"] = new EntityReference("team", DefaultTeamId),
            ["owningbusinessunit"] = new EntityReference("businessunit", SecureBusinessUnitId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;
        var writer = Build(writerOrg, new FakeSysadminClient(), new FakeTimeProvider(SecondRun),
            ownership: new FakeOwnershipResolver { Answer = SecureAnswer() });

        var act = async () => await writer.WriteAsync(request);

        (await act.Should().ThrowAsync<SignalWriterEscalationException>())
            .Which.Reason.Should().Be(OntologyWriterFailureReason.SecureOwnerMismatch);
        writerOrg.UpdateCalls.Should().BeEmpty("a mis-owned secure row is never touched");
    }

    [Fact]
    public async Task WriteAsync_NotSecure_KeepsTheFr14Path_OwnerLeftAsWriter_BusinessUnitFromTheMatter()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        // An ordinary answer (the BU's default team): the writer must NOT adopt it (F3: 0x80040299 on most BUs).
        var ownership = new FakeOwnershipResolver { Answer = Ownership.RecordOwnerResolution.Owned(DefaultTeamId) };
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: ownership);

        var result = await writer.WriteAsync(MatterSubjectRequest());

        ownership.Contexts.Should().ContainSingle().Which.Parents.Should().BeEmpty("the subject IS the matter");
        var row = writerOrg.Rows[result.SignalId];
        row.Attributes.Keys.Should().NotContain("ownerid");
        row.GetAttributeValue<EntityReference>("owningbusinessunit").Id.Should().Be(BusinessUnitId);
        result.Created.Should().BeTrue();
        result.SecureOwnerTeamId.Should().BeNull();
        result.IsSkipped.Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_OwnerRefused_WritesNothing_LogsWarningWithStableEventId_MetersOwnerRefused()
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, reasons) = ListenFailureReasonsScoped(scope);
        using var _ = listener;

        var logger = new CapturingLogger<SignalWriter>();
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver
        {
            Answer = Ownership.RecordOwnerResolution.Refused(
                Ownership.RecordOwnerRefusal.SecureParentNotIsolated, "flagged sprk_issecure but not isolated"),
        };
        var writer = Build(writerOrg, new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId),
            new FakeTimeProvider(FirstRun), logger, ownership);

        var result = await writer.WriteAsync(MatterSubjectRequest());

        result.IsSkipped.Should().BeTrue();
        result.SkippedRefusalCode.Should().Be(Ownership.RecordOwnerRefusal.SecureParentNotIsolated);
        result.SignalId.Should().Be(Guid.Empty);
        writerOrg.CreateCallCount.Should().Be(0, "a refusal never writes");
        writerOrg.UpdateCalls.Should().BeEmpty();
        reasons().Should().Equal(OntologyWriterFailureReason.OwnerRefused);
        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.EventId.Id.Should().Be(OntologyWriterEvents.WriteSkippedOwnerRefused.Id);
        warning.Field("Reason").Should().Be(OntologyWriterFailureReason.OwnerRefused);
        warning.Field("RefusalCode").Should().Be(Ownership.RecordOwnerRefusal.SecureParentNotIsolated);
        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
    }

    // =====================================================================================
    // Observability (owner directive, task 030 rework, 2026-10-04): "the writer fails closed by design, so
    // a broken credential or a refused write must not look like 'no conditions found'". Every refusal logs
    // OntologyWriterEvents.WriteRefused at Error and increments ontology.writer.failures{reason}, then
    // rethrows. Nothing is logged as success on a refused write.
    // =====================================================================================

    /// <summary>Correlation token for the scoped metric listener below — same AsyncLocal pattern as
    /// <c>TenantCacheMetricsTests</c> (Infrastructure/Cache), required because
    /// <see cref="OntologyWriterTelemetry"/>'s Meter/Counter are process-global statics and a MeterListener
    /// subscribes by instrument, not by emitter: without this token, a concurrently-running test that also
    /// touches this counter would be double-counted into this test's tally.</summary>
    private static readonly AsyncLocal<Guid> FailureMetricScope = new();

    private static (MeterListener Listener, Func<long> Count) ListenFailuresScoped(Guid scope)
    {
        long count = 0;
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == "ontology.writer.failures")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            if (FailureMetricScope.Value != scope) return;
            Interlocked.Add(ref count, value);
        });
        listener.Start();
        return (listener, () => Interlocked.Read(ref count));
    }

    private static (MeterListener Listener, Func<IReadOnlyList<string?>> Reasons) ListenFailureReasonsScoped(Guid scope)
    {
        var reasons = new System.Collections.Concurrent.ConcurrentQueue<string?>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == "ontology.writer.failures")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (FailureMetricScope.Value != scope) return;
            foreach (var tag in tags)
            {
                if (tag.Key == "reason") reasons.Enqueue(tag.Value as string);
            }
        });
        listener.Start();
        return (listener, () => reasons.ToArray());
    }

    [Fact]
    public async Task WriteAsync_RefusedWrite_LogsWriteRefusedEventId_IncrementsFailureMetric_AndRethrows()
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, failureCount) = ListenFailuresScoped(scope);
        using var _ = listener;

        var logger = new CapturingLogger<SignalWriter>();
        var sysadmin = new FakeSysadminClient(); // no BU stubbed -> OwningBusinessUnitNotFound escalation
        var writer = Build(new FakeOrganizationService(), sysadmin, new FakeTimeProvider(FirstRun), logger);

        var act = async () => await writer.WriteAsync(MatterSubjectRequest());

        await act.Should().ThrowAsync<SignalWriterEscalationException>(); // F13/F14: rethrown, never swallowed
        failureCount().Should().Be(1, "exactly one refusal occurred");

        var errorEntry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        errorEntry.EventId.Id.Should().Be(OntologyWriterEvents.WriteRefused.Id);
        errorEntry.Field("Reason").Should().Be(OntologyWriterFailureReason.OwningBusinessUnitNotFound);
        errorEntry.Field("PolicyCode").Should().Be("POL-COMMIT-BUDGET");

        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Information,
            "nothing may be logged as success on a refused write");
    }

    [Fact]
    public async Task WriteAsync_DataverseAccessDenied_IsClassified_LoggedAndMetered_AndRethrown()
    {
        // F26: the exact live fault shape found by the seam run (AppendToAccess denial), reproduced here as a
        // pure classification test so IsDataverseAccessDenied is proven without a live Dataverse call.
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, failureCount) = ListenFailuresScoped(scope);
        using var _ = listener;

        var logger = new CapturingLogger<SignalWriter>();
        var writerOrg = new FakeOrganizationService
        {
            ThrowAccessDeniedOnNextCreate = true,
        };
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), logger);

        var act = async () => await writer.WriteAsync(MatterSubjectRequest());

        await act.Should().ThrowAsync<FaultException<OrganizationServiceFault>>();
        failureCount().Should().Be(1);

        var errorEntry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        errorEntry.EventId.Id.Should().Be(OntologyWriterEvents.WriteRefused.Id);
        errorEntry.Field("Reason").Should().Be(OntologyWriterFailureReason.DataverseAccessDenied);
    }

    // =====================================================================================
    // Static invariants
    // =====================================================================================

    [Fact]
    public void VerifiedMatterDerivationKeys_AreAllPresentInRegardingLookupByEntity()
    {
        SignalWriter.VerifiedMatterDerivation.Keys.Should().BeSubsetOf(SignalWriter.RegardingLookupByEntity.Keys);
    }

    [Fact]
    public void BuildDedupeKey_ComposesPolicyCodeSubjectTypeAndCleanId()
    {
        var key = SignalWriter.BuildDedupeKey("POL-COMMIT-BUDGET", "sprk_matter", "2444af6d-e1f2-f011-8406-7ced8d1dc988");

        key.Should().Be("POL-COMMIT-BUDGET|sprk_matter|2444af6d-e1f2-f011-8406-7ced8d1dc988");
    }

    // =====================================================================================
    // OntologyWriterDataverseClient — credential guard (F7)
    // =====================================================================================

    [Fact]
    public async Task OntologyWriterDataverseClient_EmptyCredentialKey_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://spaarkedev1.crm.dynamics.com",
                ["Ontology:Writer:ManagedIdentityClientId"] = "",
            })
            .Build();
        var client = new OntologyWriterDataverseClient(configuration, NullLogger<OntologyWriterDataverseClient>.Instance);

        var act = async () => await client.CreateAsync(new Entity("sprk_signal"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void OntologyWriterDataverseClient_NeverTakesTheSharedSysadminClientAsADependency()
    {
        // F7/F13/F14 structural guard: there is no constructor parameter type through which a future change
        // could hand this class the shared sysadmin IGenericEntityService / IDataverseService /
        // DataverseServiceClientImpl — the writer's connection must come from exactly one place
        // (OntologyWriterCredentialFactory inside BuildClient), never from an injected shared instance.
        var forbidden = new[] { typeof(IGenericEntityService), typeof(IDataverseService), typeof(DataverseServiceClientImpl) };

        foreach (var ctor in typeof(OntologyWriterDataverseClient).GetConstructors(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
        {
            ctor.GetParameters().Select(p => p.ParameterType).Should().NotContain(forbidden);
        }
    }

    // =====================================================================================
    // Fixtures
    // =====================================================================================

    private static SignalWriter Build(
        FakeOrganizationService writerOrg, FakeSysadminClient sysadmin, FakeTimeProvider clock, ILogger<SignalWriter>? logger = null,
        FakeOwnershipResolver? ownership = null) =>
        new(new OntologyWriterDataverseClient(() => writerOrg, NullLogger<OntologyWriterDataverseClient>.Instance),
            sysadmin, ownership ?? new FakeOwnershipResolver(), clock, logger ?? NullLogger<SignalWriter>.Instance);

    private static readonly Guid SecureTeamId = Guid.Parse("6eabc7f9-13be-f111-a05b-0022482913fc");
    private static readonly Guid SecureBusinessUnitId = Guid.Parse("d9ec0b6f-80a0-f111-aaac-000d3a99d1d7");
    private static readonly Guid DefaultTeamId = Guid.Parse("09fbf21c-1872-f011-b4cb-7c1e52671ad0");

    private static Ownership.RecordOwnerResolution SecureAnswer() =>
        Ownership.RecordOwnerResolution.Owned(SecureTeamId) with { IsSecureOwner = true };

    private static SignalWriteRequest MatterSubjectRequest() => new(
        PolicyId: PolicyId,
        PolicyCode: "POL-COMMIT-BUDGET",
        PolicyVersionId: PolicyVersionId,
        SubjectEntityLogicalName: "sprk_matter",
        SubjectId: MatterId,
        SubjectDisplayName: "REAL-2026-123456.01",
        ShortHeadline: "Budget commitment without a revision",
        Lane: SignalWriter.LaneDecide,
        Severity: SignalWriter.SeverityWarning,
        MessageTemplate: "A {{category}} communication was raised with no revision.",
        FactValues: new Dictionary<string, object?> { ["category"] = "Fee / rate change" });

    private static SignalWriteRequest CommunicationSubjectRequest() => new(
        PolicyId: PolicyId,
        PolicyCode: "POL-COMMENT-FLAG",
        PolicyVersionId: PolicyVersionId,
        SubjectEntityLogicalName: "sprk_communication",
        SubjectId: CommunicationId,
        SubjectDisplayName: "2027 rate schedule update",
        ShortHeadline: "Communication flagged",
        Lane: SignalWriter.LaneDecide,
        Severity: null,
        MessageTemplate: "A {{category}} communication was raised.",
        FactValues: new Dictionary<string, object?> { ["category"] = "Fee / rate change" });

    // =====================================================================================
    // Fakes
    // =====================================================================================

    /// <summary>uac-r2's resolver seam (<see cref="Ownership.IRecordOwnershipResolver"/>): answers what the test set and
    /// records every context, so a test can check the writer named the matter and the subject as parents.</summary>
    private sealed class FakeOwnershipResolver : Ownership.IRecordOwnershipResolver
    {
        public Ownership.RecordOwnerResolution Answer { get; set; } = Ownership.RecordOwnerResolution.Owned(DefaultTeamId);
        public List<Ownership.RecordOwnershipContext> Contexts { get; } = new();

        public Task<Ownership.RecordOwnerResolution> ResolveOwnerAsync(Ownership.RecordOwnershipContext context, CancellationToken ct)
        {
            Contexts.Add(context);
            return Task.FromResult(Answer);
        }

        public Task<Guid?> ResolveOwningTeamAsync(Ownership.RecordOwnershipContext context, CancellationToken ct) => throw new NotImplementedException();
        public Task<Ownership.RecordOwnerResolution> ReparentAsync(
            Ownership.RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeSysadminClient : IGenericEntityService
    {
        private readonly Dictionary<Guid, EntityReference> _matterBusinessUnits = new();

        public FakeSysadminClient WithMatterBusinessUnit(Guid matterId, Guid businessUnitId)
        {
            _matterBusinessUnits[matterId] = new EntityReference("businessunit", businessUnitId);
            return this;
        }

        public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default)
        {
            var entity = new Entity(entityLogicalName, id);
            if (entityLogicalName == "sprk_matter" && _matterBusinessUnits.TryGetValue(id, out var bu))
            {
                entity["owningbusinessunit"] = bu;
            }

            return Task.FromResult(entity);
        }

        public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(Guid Id, bool Created)> UpsertAsync(Entity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default) => throw new NotImplementedException();
        public Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AssociateAsync(string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct = default) => throw new NotImplementedException();
    }

    /// <summary>
    /// A fake of the Dataverse SDK's OWN <see cref="IOrganizationServiceAsync2"/> — not a custom interface of
    /// this project's (ADR-010 F10). Implements CreateAsync/RetrieveAsync/ExecuteAsync(RetrieveRequest)/UpdateAsync
    /// meaningfully; everything else throws (unused by <see cref="SignalWriter"/>).
    /// </summary>
    private sealed class FakeOrganizationService : IOrganizationServiceAsync2
    {
        public readonly Dictionary<Guid, Entity> Rows = new();
        private readonly Dictionary<string, Guid> _byDedupeKey = new(StringComparer.Ordinal);
        public readonly List<(Guid Id, Dictionary<string, object> Fields)> UpdateCalls = new();
        private readonly Dictionary<(string Entity, Guid Id), Entity> _retrieveStubs = new();

        public bool ThrowDuplicateOnNextCreate { get; set; }
        public bool ThrowAccessDeniedOnNextCreate { get; set; }
        public int CreateCallCount { get; private set; }
        public Guid? ForcedOwningBusinessUnitOnVerifyRetrieve { get; set; }
        public Guid? ForcedOwningTeamOnVerifyRetrieve { get; set; }

        /// <summary>Every create payload exactly as the writer sent it (before this fake derives owner columns).</summary>
        public readonly List<Entity> CreatedPayloads = new();

        /// <summary>Every RetrieveAsync, with the columns asked for.</summary>
        public readonly List<(string Entity, string[] Columns)> RetrievedColumns = new();

        public void StubRetrieve(string entityLogicalName, Guid id, Entity result) =>
            _retrieveStubs[(entityLogicalName, id)] = result;

        /// <summary>Seeds a row as though an earlier run already created it, keyed by its dedupe key.</summary>
        public Guid SeedExisting(string dedupeKey, Entity entity)
        {
            var id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
            entity.Id = id;
            entity.LogicalName = "sprk_signal";
            Rows[id] = entity;
            _byDedupeKey[dedupeKey] = id;
            return id;
        }

        public Task<Guid> CreateAsync(Entity entity, CancellationToken cancellationToken)
        {
            CreateCallCount++;
            if (ThrowDuplicateOnNextCreate)
            {
                // R9 (second independent review): a TYPED fault, not a message-text-only exception, so
                // DataverseServiceClientImpl.IsAlternateKeyDuplicate's TYPED classification branch
                // (FaultException<OrganizationServiceFault>.Detail.ErrorCode) is what this test actually
                // exercises -- not just its message-text fallback.
                throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault
                    {
                        ErrorCode = unchecked((int)0x80060892),
                        Message = "Entity Key sprk_dedupekey already exists (duplicate).",
                    },
                    "Entity Key sprk_dedupekey already exists (duplicate).");
            }

            if (ThrowAccessDeniedOnNextCreate)
            {
                // R9: TYPED fault with 0x80040220 -- the GENERIC access-check-failure code (AccessCheckEx2 /
                // "does not have ... right(s)"). 0x80040299 is a DIFFERENT, more specific fault ("Read
                // Privilege Check For Owner failed" -- the F3 team-ownership fault) and was mislabelled here
                // in the first rework; fixed per R9.
                throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault
                    {
                        ErrorCode = unchecked((int)0x80040220),
                        Message = "does not have AppendToAccess right(s) for record ... of entity Communication.",
                    },
                    "does not have AppendToAccess right(s) for record ... of entity Communication.");
            }

            var id = Guid.NewGuid();
            var payload = new Entity(entity.LogicalName);
            foreach (var attribute in entity.Attributes) payload[attribute.Key] = attribute.Value;
            CreatedPayloads.Add(payload);
            entity.Id = id;
            if (entity.GetAttributeValue<EntityReference>("ownerid") is { LogicalName: "team" } teamOwner)
            {
                // Dataverse derives these from a team owner (task 039's secure path).
                entity["owningteam"] = new EntityReference("team", teamOwner.Id);
                entity["owningbusinessunit"] = new EntityReference("businessunit", SecureBusinessUnitId);
            }

            Rows[id] = entity;
            if (entity.Attributes.TryGetValue("sprk_dedupekey", out var dk) && dk is string dedupeKey)
            {
                _byDedupeKey[dedupeKey] = id;
            }

            return Task.FromResult(id);
        }

        public Task<Entity> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet, CancellationToken cancellationToken)
        {
            RetrievedColumns.Add((entityName, columnSet.Columns.ToArray()));
            if (entityName == "sprk_signal" && Rows.TryGetValue(id, out var signalRow))
            {
                if (ForcedOwningTeamOnVerifyRetrieve is { } forcedTeam)
                {
                    var teamClone = new Entity(signalRow.LogicalName, signalRow.Id);
                    foreach (var attribute in signalRow.Attributes) teamClone[attribute.Key] = attribute.Value;
                    teamClone["owningteam"] = new EntityReference("team", forcedTeam);
                    return Task.FromResult(teamClone);
                }

                if (ForcedOwningBusinessUnitOnVerifyRetrieve is { } forcedBu)
                {
                    var clone = new Entity(signalRow.LogicalName, signalRow.Id);
                    foreach (var attribute in signalRow.Attributes) clone[attribute.Key] = attribute.Value;
                    clone["owningbusinessunit"] = new EntityReference("businessunit", forcedBu);
                    return Task.FromResult(clone);
                }

                return Task.FromResult(signalRow);
            }

            if (_retrieveStubs.TryGetValue((entityName, id), out var stub))
            {
                return Task.FromResult(stub);
            }

            throw new InvalidOperationException($"No stubbed/created row for {entityName} {id:D}.");
        }

        public Task<OrganizationResponse> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken)
        {
            if (request is RetrieveRequest retrieve)
            {
                var dedupeKey = (string)retrieve.Target.KeyAttributes["sprk_dedupekey"];
                var id = _byDedupeKey[dedupeKey];
                var response = new RetrieveResponse();
                response.Results["Entity"] = Rows[id];
                return Task.FromResult<OrganizationResponse>(response);
            }

            throw new NotImplementedException($"ExecuteAsync not stubbed for {request.GetType().Name}.");
        }

        public Task UpdateAsync(Entity entity, CancellationToken cancellationToken)
        {
            var fields = entity.Attributes.ToDictionary(a => a.Key, a => a.Value);
            UpdateCalls.Add((entity.Id, fields));
            foreach (var (key, value) in fields)
            {
                Rows[entity.Id][key] = value;
            }

            return Task.CompletedTask;
        }

        // ── Unused by SignalWriter — all throw. ──────────────────────────────────────────────────────────────
        public Task AssociateAsync(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Entity> CreateAndReturnAsync(Entity entity, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteAsync(string entityName, Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DisassociateAsync(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<EntityCollection> RetrieveMultipleAsync(QueryBase query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Guid> CreateAsync(Entity entity) => throw new NotImplementedException();
        public Task<Entity> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet) => throw new NotImplementedException();
        public Task UpdateAsync(Entity entity) => throw new NotImplementedException();
        public Task DeleteAsync(string entityName, Guid id) => throw new NotImplementedException();
        public Task<OrganizationResponse> ExecuteAsync(OrganizationRequest request) => throw new NotImplementedException();
        public Task AssociateAsync(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotImplementedException();
        public Task DisassociateAsync(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotImplementedException();
        public Task<EntityCollection> RetrieveMultipleAsync(QueryBase query) => throw new NotImplementedException();
        public Guid Create(Entity entity) => throw new NotImplementedException();
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotImplementedException();
        public void Update(Entity entity) => throw new NotImplementedException();
        public void Delete(string entityName, Guid id) => throw new NotImplementedException();
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotImplementedException();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotImplementedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotImplementedException();
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotImplementedException();
    }
}
