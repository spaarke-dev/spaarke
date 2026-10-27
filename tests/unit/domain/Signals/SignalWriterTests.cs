using System.Diagnostics.Metrics;
using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.PowerPlatform.Dataverse.Client;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;
using Ownership = Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // reuse CapturingLogger<T>/LogEntry (internal, same assembly)
using Sprk.Bff.Api.Tests.TestInfrastructure;
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
    internal static readonly Guid MatterId = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");
    internal static readonly Guid CommunicationId = Guid.Parse("f00b6389-8fbf-f111-aaaf-0022482913fc");
    internal static readonly Guid BusinessUnitId = Guid.Parse("06fbf21c-1872-f011-b4cb-7c1e52671ad0");

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
            request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant(), 1);
        var existingId = writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
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
            request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant(), 1);
        writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
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
            request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant(), 1);
        var existingId = writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
            ["sprk_signalstatus"] = new OptionSetValue(resolved),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
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
        // Task 141 (D-132): the pair type is the Lookup to the subject table's sprk_recordtype_ref row, written by the shared builder.
        var pairType = row["sprk_regardingrecordtype"].Should().BeOfType<EntityReference>().Subject;
        pairType.LogicalName.Should().Be("sprk_recordtype_ref");
        pairType.Id.Should().Be(RecordTypeCatalogStub.SpaarkeDev1["sprk_matter"]);
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(MatterId.ToString("D").ToLowerInvariant());
    }

    [Fact]
    public async Task WriteAsync_CommunicationSubject_DerivesMatterFromRegardingMatter_SetsTypedLookupAndTrio()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        sysadmin.Stub(new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        });
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var result = await writer.WriteAsync(CommunicationSubjectRequest());
        var row = writerOrg.Rows[result.SignalId];

        result.GroupingMatterId.Should().Be(MatterId, "a Signal about a communication on matter X groups under X");
        row.GetAttributeValue<EntityReference>("sprk_regardingcommunication").Id.Should().Be(CommunicationId);
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);
        row.Attributes.Keys.Where(k => SignalWriter.RegardingLookupByEntity.Values.Contains(k)).Should().Equal(
            new[] { "sprk_regardingcommunication" }, "ADR-024: at most one specific regarding lookup");
        // Task 141 (D-133): sprk_communication has no sprk_recordtype_ref row, so the pair is untyped (id only), never an error.
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(CommunicationId.ToString("D").ToLowerInvariant());
        row.Attributes.Should().NotContainKey("sprk_regardingrecordtype");
        result.Created.Should().BeTrue();
    }

    // =====================================================================================
    // Task 143 (D-129/D-130): the owner is the resolver's team, in the create; owningbusinessunit is never written
    // =====================================================================================

    [Fact]
    public async Task WriteAsync_OrdinaryCoreRecord_IsOwnedByTheResolversTeam_InTheCreate_NoOwningBusinessUnit_D130()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var result = await writer.WriteAsync(MatterSubjectRequest());

        var created = writerOrg.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId),
            "D-130: the matter's business-unit default team the resolver answered, set IN the create");
        created.Attributes.Keys.Should().NotContain("owningbusinessunit", "C3: it derives from the owner team, never written");
        writerOrg.RetrievedColumns.Should().Contain(c => c.Entity == "sprk_signal" && c.Columns.Contains("ownerid"),
            "the owner is verified by reading ownerid back");
        result.OwningBusinessUnitId.Should().Be(BusinessUnitId, "reported from the read-back (the team's unit), not computed");
        result.SecureOwnerTeamId.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_ReEvaluation_ReportsEmptyOwningBusinessUnit_OwnershipNotTouched()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var request = MatterSubjectRequest();
        var dedupeKey = SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant(), 1);
        // The reconcile path re-checks the owner, so the seeded row carries the resolver's team (ownership is not TOUCHED on
        // reconcile, but it IS still verified).
        writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun));
        var result = await writer.WriteAsync(request);

        result.OwningBusinessUnitId.Should().Be(Guid.Empty, "ownership is not RE-ASSIGNED on reconcile, even though it was just verified");
        writerOrg.UpdateCalls.Should().ContainSingle().Which.Fields.Keys.Should().NotContain(new[] { "ownerid", "owningbusinessunit" });
    }

    [Theory]
    [InlineData("team")]
    [InlineData("systemuser")] // a row written before task 143 (owned by the writer identity): known limit, refused loudly
    public async Task WriteAsync_ReconcileFindsAnotherOwner_Escalates_OwnerMismatch_NotSuccess(string ownerKind)
    {
        // R1 (second independent review), task 143: a Signal whose owner is not the resolver's answer now (its matter moved
        // business unit, or it predates task 143) is caught on every re-evaluation, not only on the original create.
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var request = MatterSubjectRequest();
        var dedupeKey = SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant(), 1);
        writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
            ["ownerid"] = new EntityReference(ownerKind, Guid.NewGuid()),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, failureCount) = ListenFailuresScoped(scope);
        using var _ = listener;
        var logger = new CapturingLogger<SignalWriter>();

        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun), logger);
        var act = async () => await writer.WriteAsync(request);

        (await act.Should().ThrowAsync<SignalWriterEscalationException>("a mis-owned row must escalate, not report a quiet success"))
            .Which.Reason.Should().Be(OntologyWriterFailureReason.OwnerMismatch);
        failureCount().Should().Be(1);
        writerOrg.UpdateCalls.Should().BeEmpty("lastevaluated must not be touched when the owner check fails -- the row is wrong, not merely stale");

        var errorEntry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        errorEntry.EventId.Id.Should().Be(OntologyWriterEvents.WriteRefused.Id);
        errorEntry.Field("Reason").Should().Be(OntologyWriterFailureReason.OwnerMismatch);
    }

    [Fact]
    public async Task WriteAsync_CreatedRowReadsBackWithAnotherOwner_Escalates_OwnerMismatch()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService { ForcedOwnerOnVerifyRetrieve = new EntityReference("team", Guid.NewGuid()) };
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var act = async () => await writer.WriteAsync(MatterSubjectRequest());

        (await act.Should().ThrowAsync<SignalWriterEscalationException>())
            .Which.Reason.Should().Be(OntologyWriterFailureReason.OwnerMismatch);
    }

    [Fact]
    public async Task WriteAsync_CreatedRowOwnerReadBackFaults_Escalates_OwnerReadBackFailed()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService { ThrowOnVerifyRetrieve = true };
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var act = async () => await writer.WriteAsync(MatterSubjectRequest());

        (await act.Should().ThrowAsync<SignalWriterEscalationException>())
            .Which.Reason.Should().Be(OntologyWriterFailureReason.OwnerReadBackFailed);
    }

    [Fact]
    public async Task WriteAsync_AnswerThatNamesNoTeam_IsARefusal_WritesNothing_MetersOwnerRefused_D130()
    {
        // "Unchanged" is not an owner for a new row: the writer identity never keeps it (the pre-143 FR-14 shape).
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, reasons) = ListenFailureReasonsScoped(scope);
        using var _ = listener;
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver { Answer = Ownership.RecordOwnerResolution.Unchanged("names no team") };

        var result = await Build(writerOrg, new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId),
            new FakeTimeProvider(FirstRun), ownership: ownership).WriteAsync(MatterSubjectRequest());

        result.IsSkipped.Should().BeTrue();
        result.SkippedRefusalCode.Should().Be(Ownership.RecordOwnerRefusal.NoOwnerSource);
        writerOrg.CreateCallCount.Should().Be(0);
        reasons().Should().Equal(OntologyWriterFailureReason.OwnerRefused);
    }

    [Fact]
    public async Task WriteAsync_CoreResolverFault_PropagatesAndWritesNothing()
    {
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver { FaultFor = _ => new TimeoutException("resolver read timed out") };

        var act = async () => await Build(writerOrg, new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId),
            new FakeTimeProvider(FirstRun), ownership: ownership).WriteAsync(MatterSubjectRequest());

        await act.Should().ThrowAsync<TimeoutException>();
        writerOrg.CreateCallCount.Should().Be(0);
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
        var request = MatterSubjectRequest() with { SubjectEntityLogicalName = "sprk_servicerequest", SubjectId = Guid.NewGuid() };

        var act = async () => await writer.WriteAsync(request);

        (await act.Should().ThrowAsync<SignalWriterEscalationException>()).WithMessage("*sprk_servicerequest*");
        writerOrg.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_ProjectSubject_IsItsOwnCoreRecord_RegardingTheProject_NoMatter()
    {
        // Task 025b (D-122): a Threshold rule on a project's spend or budget raises a Signal about the project.
        var sysadmin = new FakeSysadminClient().WithBusinessUnit("sprk_project", ProjectId, ProjectBusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver();
        var request = MatterSubjectRequest() with { SubjectEntityLogicalName = "sprk_project", SubjectId = ProjectId };

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: ownership).WriteAsync(request);
        var row = writerOrg.Rows[result.SignalId];

        row.GetAttributeValue<EntityReference>("sprk_regardingproject").Id.Should().Be(ProjectId);
        row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(sysadmin.CatalogId("sprk_project"));
        row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(ProjectId));
        row.Attributes.Keys.Should().NotContain("sprk_matter");
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId));
        ownership.Contexts.Should().ContainSingle().Which.TargetEntityLogicalName.Should().Be("sprk_project");
    }

    [Fact]
    public async Task WriteAsync_CommunicationWithNoRegardingMatter_Escalates_AndWritesNothing()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        sysadmin.Stub(new Entity("sprk_communication", CommunicationId));
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
        sysadmin.Stub(new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        });
        var ownership = new FakeOwnershipResolver { Answer = SecureAnswer() };
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: ownership);

        var result = await writer.WriteAsync(CommunicationSubjectRequest());

        var context = ownership.Contexts.Should().ContainSingle().Subject;
        context.TargetEntityLogicalName.Should().Be("sprk_matter");
        context.TargetRecordId.Should().Be(MatterId);
        context.Parents.Should().Equal(
            new Ownership.RecordOwnershipParent("sprk_matter", MatterId), new Ownership.RecordOwnershipParent("sprk_communication", CommunicationId));

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
        var dedupeKey = SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant(), 1);
        var existingId = writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
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
        var dedupeKey = SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_matter", MatterId.ToString("D").ToLowerInvariant(), 1);
        writerOrg.SeedExisting(dedupeKey, new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
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
    public async Task WriteAsync_NotSecure_AppliesTheOrdinaryAnswer_TheBusinessUnitsTeam_D130()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        // Task 143 (D-130): an ordinary answer (the parent's business-unit default team) IS the owner; before 143 the writer
        // kept itself as owner and set owningbusinessunit (FR-14, F3).
        var ownership = new FakeOwnershipResolver { Answer = Ownership.RecordOwnerResolution.Owned(DefaultTeamId) };
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: ownership);

        var result = await writer.WriteAsync(MatterSubjectRequest());

        ownership.Contexts.Should().ContainSingle().Which.Parents.Should().Equal(
            new[] { new Ownership.RecordOwnershipParent("sprk_matter", MatterId) }, "the subject IS the matter: one parent");
        var created = writerOrg.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId));
        created.Attributes.Keys.Should().NotContain("owningbusinessunit");
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

    /// <summary>
    /// Task 039 review B-1 pin: the secure path above is only safe because uac-r2's mechanism knows <c>sprk_signal</c>.
    /// Without the lineage entry a Secure-team-owned Signal is mirrored to nobody; without the role's Read the create is
    /// refused (0x80040299). Both come from master #1390, so this pins them where the writer depends on them: the entry
    /// lists the typed matter lookup and the regarding lookups the writer fills, and the role set grants the Read.
    /// </summary>
    [Fact]
    public void SecurePath_DependsOn_SignalLineageEntry_AndSecureRecordOwnerRead()
    {
        Sprk.Bff.Api.Services.Access.SecureChildLineage.Children.Should().ContainKey("sprk_signal");
        var columns = Sprk.Bff.Api.Services.Access.SecureChildLineage.Children["sprk_signal"].Lookups.Keys.ToArray();
        columns.Should().Contain(new[]
        {
            "sprk_matter", "sprk_regardingmatter", "sprk_regardingcommunication",
            "sprk_regardingproject", "sprk_regardingworkassignment",
        });
        columns.Should().NotContain("sprk_regardingservicerequest", "a service request is never secure (D-36)");

        Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerRoleSet.Embedded.Tables
            .Should().ContainSingle(t => t.LogicalName == "sprk_signal")
            .Which.Should().Match<Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerRoleTable>(
                t => t.Kind == "child" && t.PrivilegeName == "prvReadsprk_Signal");
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
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        // The created row reads back with another owner -> OwnerMismatch escalation (task 143; was OwningBusinessUnitNotFound).
        var writer = Build(new FakeOrganizationService { ForcedOwnerOnVerifyRetrieve = new EntityReference("systemuser", Guid.NewGuid()) },
            sysadmin, new FakeTimeProvider(FirstRun), logger);

        var act = async () => await writer.WriteAsync(MatterSubjectRequest());

        await act.Should().ThrowAsync<SignalWriterEscalationException>(); // F13/F14: rethrown, never swallowed
        failureCount().Should().Be(1, "exactly one refusal occurred");

        var errorEntry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        errorEntry.EventId.Id.Should().Be(OntologyWriterEvents.WriteRefused.Id);
        errorEntry.Field("Reason").Should().Be(OntologyWriterFailureReason.OwnerMismatch);
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
    // Task 141 review F1: a subject catalog read that FAULTS is not "no catalog row"
    // =====================================================================================

    [Fact]
    public async Task WriteAsync_SubjectCatalogReadFaults_Escalates_LoggedAndMetered_AndWritesNothing_NeverUntyped()
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, reasons) = ListenFailureReasonsScoped(scope);
        using var _ = listener;

        var logger = new CapturingLogger<SignalWriter>();
        var writerOrg = new FakeOrganizationService();
        var catalog = new RecordTypeCatalogStub((_, _) => Task.FromException<Entity?>(new TimeoutException("429: too many requests")));
        var writer = Build(writerOrg, new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId),
            new FakeTimeProvider(FirstRun), logger, recordTypes: catalog);

        var act = async () => await writer.WriteAsync(MatterSubjectRequest());

        var thrown = await act.Should().ThrowAsync<SignalWriterEscalationException>("the evaluator counts an escalation as refused");
        thrown.Which.Reason.Should().Be(OntologyWriterFailureReason.SubjectPairUnresolved);
        thrown.Which.InnerException.Should().BeOfType<TimeoutException>();
        writerOrg.CreateCallCount.Should().Be(0, "a Signal is never written with an untyped pair because of a fault");
        reasons().Should().Equal(OntologyWriterFailureReason.SubjectPairUnresolved);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error)
            .Which.EventId.Id.Should().Be(OntologyWriterEvents.WriteRefused.Id);
        SignalWriter.IsMeteredByWriter(thrown.Which).Should().BeTrue("the job must not meter it a second time");
    }

    [Fact]
    public async Task WriteAsync_CancelledDuringTheSubjectCatalogRead_PropagatesTheCancellation_NotAnEscalation()
    {
        using var cts = new CancellationTokenSource();
        var writerOrg = new FakeOrganizationService();
        var catalog = new RecordTypeCatalogStub((_, _) =>
        {
            cts.Cancel();
            return Task.FromException<Entity?>(new OperationCanceledException(cts.Token));
        });
        var writer = Build(writerOrg, new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId),
            new FakeTimeProvider(FirstRun), recordTypes: catalog);

        var act = async () => await writer.WriteAsync(MatterSubjectRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        writerOrg.CreateCallCount.Should().Be(0);
    }

    // =====================================================================================
    // Task 141 review F2: SubjectEntityOf fails closed on a missing or ambiguous subject
    // =====================================================================================

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "sprk_todo")]
    [InlineData(2, null)]
    public void SubjectEntityOf_ExactlyOneLookupCarryingThePairId_NamesTheSubject_NoneOrTwoAreNull(int lookupsCarryingPairId, string? expected)
    {
        var pairId = Guid.NewGuid();
        // Another lookup carrying a DIFFERENT id (a later core-ancestor stamp, task 144) never counts.
        var columns = new Dictionary<string, Guid?>(StringComparer.Ordinal) { ["sprk_regardingmatter"] = Guid.NewGuid() };
        if (lookupsCarryingPairId >= 1) columns["sprk_regardingtodo"] = pairId;
        if (lookupsCarryingPairId >= 2) columns["sprk_regardingevent"] = pairId;

        SignalWriter.SubjectEntityOf(pairId, lookup => columns.TryGetValue(lookup, out var id) ? id : null)
            .Should().Be(expected);
    }

    // =====================================================================================
    // Task 037: Do-lane subjects (event, To Do, work assignment) and the core record (D-34..D-38)
    // =====================================================================================

    private static readonly Guid EventId = Guid.Parse("11111111-0000-0000-0000-0000000000e1");
    private static readonly Guid TodoId = Guid.Parse("11111111-0000-0000-0000-0000000000d1");
    private static readonly Guid WorkAssignmentId = Guid.Parse("11111111-0000-0000-0000-0000000000a1");
    private static readonly Guid ProjectId = Guid.Parse("22222222-0000-0000-0000-0000000000b1");
    private static readonly Guid ServiceRequestId = Guid.Parse("22222222-0000-0000-0000-0000000000c1");
    private static readonly Guid ProjectBusinessUnitId = Guid.Parse("33333333-0000-0000-0000-0000000000b1");
    private static readonly Guid ServiceRequestBusinessUnitId = Guid.Parse("33333333-0000-0000-0000-0000000000c1");
    private static readonly Guid WorkAssignmentBusinessUnitId = Guid.Parse("33333333-0000-0000-0000-0000000000a1");
    private static readonly Guid TodoOwnerUserId = Guid.Parse("44444444-0000-0000-0000-0000000000f1");

    private static SignalWriteRequest DoRequest(string subjectEntity, Guid subjectId) => new(
        PolicyId: PolicyId,
        PolicyCode: "POL-DO-ITEM",
        PolicyVersionId: PolicyVersionId,
        SubjectEntityLogicalName: subjectEntity,
        SubjectId: subjectId,
        SubjectDisplayName: "Item",
        ShortHeadline: "Item is overdue",
        Lane: SignalWriter.LaneDo,
        Severity: SignalWriter.SeverityWarning,
        MessageTemplate: "The item is {{daysOverdue}} days overdue.",
        FactValues: new Dictionary<string, object?> { ["daysOverdue"] = 3 });

    private static string Lower(Guid id) => id.ToString("D").ToLowerInvariant();

    [Theory]
    [InlineData("sprk_event", "11111111-0000-0000-0000-0000000000e1", "sprk_regardingevent")]
    [InlineData("sprk_todo", "11111111-0000-0000-0000-0000000000d1", "sprk_regardingtodo")]
    public async Task WriteAsync_EventOrTodoFiledUnderAMatter_GroupsUnderTheMatter_TypedLookupTrioAndMatterBusinessUnit(
        string subjectEntity, string subjectIdText, string typedLookup)
    {
        var subjectId = Guid.Parse(subjectIdText);
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId)
            .Stub(new Entity(subjectEntity, subjectId) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) });
        var writerOrg = new FakeOrganizationService();
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var result = await writer.WriteAsync(DoRequest(subjectEntity, subjectId));
        var row = writerOrg.Rows[result.SignalId];

        row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(sysadmin.CatalogId("sprk_matter"));
        row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(MatterId));
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);
        row.GetAttributeValue<EntityReference>(typedLookup).Id.Should().Be(subjectId);
        row.GetAttributeValue<EntityReference>("sprk_regardingrecordtype").Id.Should().Be(RecordTypeCatalogStub.SpaarkeDev1[subjectEntity]);
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(Lower(subjectId));
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId));
        result.GroupingMatterId.Should().Be(MatterId);
        result.CoreRecordEntity.Should().Be("sprk_matter");
    }

    [Fact]
    public async Task WriteAsync_DoSubjects_CarryTheSubjectsDate_EventUsesDueDateNeverFinalDueDate()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId)
            .WithBusinessUnit("sprk_workassignment", WorkAssignmentId, WorkAssignmentBusinessUnitId)
            .Stub(new Entity("sprk_event", EventId)
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                ["sprk_duedate"] = new DateTime(2026, 10, 7),
                ["sprk_finalduedate"] = new DateTime(2026, 10, 9),
            })
            .Stub(new Entity("sprk_todo", TodoId)
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                ["sprk_duedate"] = new DateTime(2026, 10, 15),
            })
            .Stub(new Entity("sprk_workassignment", WorkAssignmentId) { ["sprk_responseduedate"] = new DateTime(2026, 6, 9) });
        var writerOrg = new FakeOrganizationService();
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun));

        var evt = await writer.WriteAsync(DoRequest("sprk_event", EventId));
        var todo = await writer.WriteAsync(DoRequest("sprk_todo", TodoId));
        var wa = await writer.WriteAsync(DoRequest("sprk_workassignment", WorkAssignmentId));

        writerOrg.Rows[evt.SignalId].GetAttributeValue<DateTime?>("sprk_duedate").Should().Be(new DateTime(2026, 10, 7));
        writerOrg.Rows[todo.SignalId].GetAttributeValue<DateTime?>("sprk_duedate").Should().Be(new DateTime(2026, 10, 15));
        writerOrg.Rows[wa.SignalId].GetAttributeValue<DateTime?>("sprk_duedate").Should().Be(new DateTime(2026, 6, 9));
        sysadmin.Retrieved.SelectMany(r => r.Columns).Should().NotContain("sprk_finalduedate", "D-27: never the final due date");
    }

    [Fact]
    public async Task WriteAsync_MatterSubject_HasNoDueDate()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        var writerOrg = new FakeOrganizationService();
        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(MatterSubjectRequest());

        writerOrg.Rows[result.SignalId].Attributes.Keys.Should().NotContain("sprk_duedate");
    }

    [Theory]
    [InlineData(100000000, true)]  // Open
    [InlineData(100000001, true)]  // Acknowledged
    [InlineData(100000002, false)] // Resolved: the date it last saw is what D-13 compares
    public async Task WriteAsync_ReEvaluation_RefreshesTheDueDateOnlyWhileOpenOrAcknowledged(int status, bool refreshed)
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId)
            .Stub(new Entity("sprk_todo", TodoId)
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                ["sprk_duedate"] = new DateTime(2026, 10, 20),
            });
        var writerOrg = new FakeOrganizationService();
        var request = DoRequest("sprk_todo", TodoId);
        var existingId = writerOrg.SeedExisting(SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_todo", Lower(TodoId), 1), new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
            ["sprk_signalstatus"] = new OptionSetValue(status),
            ["sprk_duedate"] = new DateTime(2026, 10, 10),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        await Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun)).WriteAsync(request);

        writerOrg.Rows[existingId].GetAttributeValue<DateTime?>("sprk_duedate")
            .Should().Be(refreshed ? new DateTime(2026, 10, 20) : new DateTime(2026, 10, 10));
        writerOrg.UpdateCalls.Should().ContainSingle().Which.Fields.ContainsKey("sprk_duedate").Should().Be(refreshed);
    }

    [Fact]
    public async Task WriteAsync_ReEvaluation_UnchangedDate_UpdatesOnlyLastEvaluated()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId)
            .Stub(new Entity("sprk_todo", TodoId)
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
                ["sprk_duedate"] = new DateTime(2026, 10, 10),
            });
        var writerOrg = new FakeOrganizationService();
        var request = DoRequest("sprk_todo", TodoId);
        writerOrg.SeedExisting(SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_todo", Lower(TodoId), 1), new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["sprk_duedate"] = new DateTime(2026, 10, 10),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        await Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun)).WriteAsync(request);

        writerOrg.UpdateCalls.Should().ContainSingle().Which.Fields.Keys.Should().BeEquivalentTo(new[] { "sprk_lastevaluated" });
    }

    [Fact]
    public async Task WriteAsync_TodoFiledUnderAProject_GroupsUnderTheProject_NoMatter_ProjectBusinessUnit()
    {
        var sysadmin = new FakeSysadminClient().WithBusinessUnit("sprk_project", ProjectId, ProjectBusinessUnitId)
            .Stub(new Entity("sprk_todo", TodoId) { ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId) });
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver();

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: ownership)
            .WriteAsync(DoRequest("sprk_todo", TodoId));
        var row = writerOrg.Rows[result.SignalId];

        row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(sysadmin.CatalogId("sprk_project"));
        row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(ProjectId));
        row.Attributes.Keys.Should().NotContain("sprk_matter");
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId));
        result.GroupingMatterId.Should().Be(Guid.Empty);
        var context = ownership.Contexts.Should().ContainSingle().Subject;
        context.TargetEntityLogicalName.Should().Be("sprk_project");
        context.Parents.Should().Equal(
            new Ownership.RecordOwnershipParent("sprk_project", ProjectId), new Ownership.RecordOwnershipParent("sprk_todo", TodoId));
    }

    [Fact]
    public async Task WriteAsync_TodoFiledUnderAServiceRequest_GroupsUnderTheServiceRequest()
    {
        var sysadmin = new FakeSysadminClient().WithBusinessUnit("sprk_servicerequest", ServiceRequestId, ServiceRequestBusinessUnitId)
            .Stub(new Entity("sprk_todo", TodoId) { ["sprk_regardingservicerequest"] = new EntityReference("sprk_servicerequest", ServiceRequestId) });
        var writerOrg = new FakeOrganizationService();

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(DoRequest("sprk_todo", TodoId));
        var row = writerOrg.Rows[result.SignalId];

        row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(sysadmin.CatalogId("sprk_servicerequest"));
        row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(ServiceRequestId));
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId));
        writerOrg.CreatedPayloads.Should().ContainSingle().Which.Attributes.Keys.Should().NotContain("owningbusinessunit", "C3: never sent");
        row.Attributes.Keys.Should().NotContain("sprk_matter");
    }

    [Fact]
    public async Task WriteAsync_WorkAssignmentSubject_IsItsOwnCoreRecord_NotItsMatter()
    {
        var sysadmin = new FakeSysadminClient().WithBusinessUnit("sprk_workassignment", WorkAssignmentId, WorkAssignmentBusinessUnitId)
            .Stub(new Entity("sprk_workassignment", WorkAssignmentId) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) });
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver();

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: ownership)
            .WriteAsync(DoRequest("sprk_workassignment", WorkAssignmentId));
        var row = writerOrg.Rows[result.SignalId];

        row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id.Should().Be(sysadmin.CatalogId("sprk_workassignment"));
        row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(WorkAssignmentId));
        row.Attributes.Keys.Should().NotContain("sprk_matter");
        row.GetAttributeValue<EntityReference>("sprk_regardingworkassignment").Id.Should().Be(WorkAssignmentId);
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId));
        var context = ownership.Contexts.Should().ContainSingle().Subject;
        context.TargetEntityLogicalName.Should().Be("sprk_workassignment");
        context.Parents.Should().Equal(new[] { new Ownership.RecordOwnershipParent("sprk_workassignment", WorkAssignmentId) },
            "the subject IS the core record: one parent");
    }

    [Fact]
    public async Task WriteAsync_TodoWithNoMatterAndNoProject_HasNoCoreRecord_IsOwnedByTheResolversTeam_NeverAUser_D130()
    {
        // The To Do is user-owned; its Signal is NOT (D-129: a person is never read from ownerid). The resolver answers for the
        // subject itself, its only parent.
        var sysadmin = new FakeSysadminClient()
            .Stub(new Entity("sprk_todo", TodoId) { ["ownerid"] = new EntityReference("systemuser", TodoOwnerUserId) });
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver();

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: ownership)
            .WriteAsync(DoRequest("sprk_todo", TodoId));

        var created = writerOrg.CreatedPayloads.Should().ContainSingle().Subject;
        created.Attributes.Keys.Should().NotContain(new[] { "sprk_corerecordtype", "sprk_corerecordid", "sprk_matter", "owningbusinessunit" });
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId));
        writerOrg.RetrievedColumns.Should().Contain(c => c.Entity == "sprk_signal" && c.Columns.Contains("ownerid"), "ownerid is read back");
        var context = ownership.Contexts.Should().ContainSingle("no assignee: the To Do's own answer stands").Subject;
        context.TargetEntityLogicalName.Should().Be("sprk_todo");
        context.TargetRecordId.Should().Be(TodoId);
        context.Parents.Should().Equal(new[] { new Ownership.RecordOwnershipParent("sprk_todo", TodoId) });
        context.RequestedBy.Should().BeNull("a job-raised Signal names nobody");
        sysadmin.Retrieved.Should().NotContain(r => r.Entity == "sprk_todo" && r.Columns.Contains("ownerid"),
            "D-129: the item's owner is never read as the Signal's owner");
        result.CoreRecordEntity.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_NoCore_OwnerReadsBackAsSomeoneElse_Escalates()
    {
        var sysadmin = new FakeSysadminClient().Stub(new Entity("sprk_todo", TodoId));
        var writerOrg = new FakeOrganizationService { ForcedOwnerOnVerifyRetrieve = new EntityReference("systemuser", Guid.NewGuid()) };

        var act = async () => await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(DoRequest("sprk_todo", TodoId));

        (await act.Should().ThrowAsync<SignalWriterEscalationException>())
            .Which.Reason.Should().Be(OntologyWriterFailureReason.NoCoreOwnerMismatch);
    }

    [Fact]
    public async Task WriteAsync_NoCore_ResolverRefuses_WritesNothing_MetersOwnerRefused()
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, reasons) = ListenFailureReasonsScoped(scope);
        using var _ = listener;
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver
        {
            Answer = Ownership.RecordOwnerResolution.Refused(Ownership.RecordOwnerRefusal.NoOwnerSource, "no business-unit team"),
        };

        var result = await Build(writerOrg, new FakeSysadminClient().Stub(new Entity("sprk_event", EventId)),
            new FakeTimeProvider(FirstRun), ownership: ownership).WriteAsync(DoRequest("sprk_event", EventId));

        result.IsSkipped.Should().BeTrue();
        result.SkippedRefusalCode.Should().Be(Ownership.RecordOwnerRefusal.NoOwnerSource);
        writerOrg.CreateCallCount.Should().Be(0);
        reasons().Should().Equal(OntologyWriterFailureReason.OwnerRefused);
    }

    [Fact]
    public async Task WriteAsync_NoCore_ResolverFaults_RefusesAsNoCoreOwnerUnresolved_WritesNothing()
    {
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver { FaultFor = _ => new TimeoutException("resolver read timed out") };

        var act = async () => await Build(writerOrg, new FakeSysadminClient().Stub(new Entity("sprk_todo", TodoId)),
            new FakeTimeProvider(FirstRun), ownership: ownership).WriteAsync(DoRequest("sprk_todo", TodoId));

        var thrown = await act.Should().ThrowAsync<SignalWriterEscalationException>();
        thrown.Which.Reason.Should().Be(OntologyWriterFailureReason.NoCoreOwnerUnresolved);
        thrown.Which.InnerException.Should().BeOfType<TimeoutException>();
        writerOrg.CreateCallCount.Should().Be(0);
    }

    // ── D-134 / D-135: a personal To Do's Signal is owned by its Assigned To user's business-unit team ─────────────────

    private static readonly Guid AssigneeContactId = Guid.Parse("55555555-0000-4000-8000-0000000000c1");
    private static readonly Guid AssigneeUserId = Guid.Parse("55555555-0000-4000-8000-0000000000a1");
    private static readonly Guid AssigneeTeamId = Guid.Parse("55555555-0000-4000-8000-0000000000b1");

    private static FakeSysadminClient PersonalTodo(Guid? assignedTo = null, bool filedUnderMatter = false, bool alsoFiledUnderProject = false)
    {
        var todo = new Entity("sprk_todo", TodoId) { ["ownerid"] = new EntityReference("systemuser", TodoOwnerUserId) };
        if (assignedTo is { } contact) todo["sprk_assignedto"] = new EntityReference("contact", contact);
        if (filedUnderMatter) todo["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId);
        if (alsoFiledUnderProject) todo["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId);
        return new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId).Stub(todo);
    }

    private static AssignedAccessTestDoubles.FakeAssignedAccessStore LinkedAssignee()
    {
        var links = new AssignedAccessTestDoubles.FakeAssignedAccessStore();
        links.UsersByLink[AssigneeContactId] =
            [new AssignedLinkCandidate(AssigneeUserId, AssigneeContactId, Guid.NewGuid(), IsDisabled: false, AccessMode: 0, ApplicationId: null, IsExternal: false)];
        return links;
    }

    /// <summary>The To Do's own answer is <paramref name="parentsAnswer"/>; the assignee's team question (ForUserTeam) answers
    /// <see cref="AssigneeTeamId"/>.</summary>
    private static FakeOwnershipResolver AssigneeAware(Ownership.RecordOwnerResolution? parentsAnswer = null) => new()
    {
        AnswerFor = context => context.CallerSystemUserId == AssigneeUserId && context.Parents.Count == 0
            ? Ownership.RecordOwnerResolution.Owned(AssigneeTeamId)
            : parentsAnswer ?? Ownership.RecordOwnerResolution.Owned(DefaultTeamId),
    };

    [Fact]
    public async Task D134_PersonalTodo_WithOneEnabledAssignee_IsOwnedByTheAssigneesBusinessUnitTeam()
    {
        var writerOrg = new FakeOrganizationService();
        var ownership = AssigneeAware();

        var result = await Build(writerOrg, PersonalTodo(AssigneeContactId), new FakeTimeProvider(FirstRun), ownership: ownership,
            links: LinkedAssignee()).WriteAsync(DoRequest("sprk_todo", TodoId));

        result.Created.Should().BeTrue();
        writerOrg.CreatedPayloads.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("ownerid")
            .Should().BeEquivalentTo(new EntityReference("team", AssigneeTeamId), "D-134: the Assigned To user's team, not the To Do's own");
        ownership.Contexts.Should().HaveCount(2);
        ownership.Contexts[0].Parents.Should().Equal(new[] { new Ownership.RecordOwnershipParent("sprk_todo", TodoId) },
            "the parents are asked first (D-135 item 7)");
        ownership.Contexts[1].Parents.Should().BeEmpty();
        ownership.Contexts[1].CallerSystemUserId.Should().Be(AssigneeUserId, "then the assignee's team, through ForUserTeam");
    }

    [Fact]
    public async Task D134_ASecureParent_Wins_TheAssigneeIsNeverRead()
    {
        var links = LinkedAssignee();
        links.FailLinkRead = true; // a read would fault: proves it is never made
        var writerOrg = new FakeOrganizationService();
        var ownership = AssigneeAware(SecureAnswer());

        var result = await Build(writerOrg, PersonalTodo(AssigneeContactId), new FakeTimeProvider(FirstRun), ownership: ownership,
            links: links).WriteAsync(DoRequest("sprk_todo", TodoId));

        result.SecureOwnerTeamId.Should().Be(SecureTeamId);
        writerOrg.CreatedPayloads.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("ownerid")
            .Should().BeEquivalentTo(new EntityReference("team", SecureTeamId));
        ownership.Contexts.Should().ContainSingle("a secure answer stands; the assignee is not consulted");
    }

    [Fact]
    public async Task D134_AssigneeReadFaults_WhileTheAnswerIsOrdinary_FailsClosed_NoCoreOwnerUnresolved_WritesNothing()
    {
        var links = LinkedAssignee();
        links.FailLinkRead = true;
        var writerOrg = new FakeOrganizationService();
        var logger = new CapturingLogger<SignalWriter>();

        var act = async () => await Build(writerOrg, PersonalTodo(AssigneeContactId), new FakeTimeProvider(FirstRun), logger,
            ownership: AssigneeAware(), links: links).WriteAsync(DoRequest("sprk_todo", TodoId));

        var thrown = await act.Should().ThrowAsync<SignalWriterEscalationException>("unknown whether a user exists: never fall back");
        thrown.Which.Reason.Should().Be(OntologyWriterFailureReason.NoCoreOwnerUnresolved);
        writerOrg.CreateCallCount.Should().Be(0);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.EventId.Id.Should().Be(OntologyWriterEvents.WriteRefused.Id);
        SignalWriter.IsMeteredByWriter(thrown.Which).Should().BeTrue();
    }

    [Fact]
    public async Task D134_TodoReadFaults_WhileTheAnswerIsOrdinary_FailsClosed()
    {
        var sysadmin = PersonalTodo(AssigneeContactId);
        var writerOrg = new FakeOrganizationService();
        // The first To Do reads (core ancestors, due date) succeed; the assignee read of the To Do then faults.
        var writer = Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: AssigneeAware(), links: LinkedAssignee());
        sysadmin.ThrowOnRetrieveWithColumn = "sprk_assignedto";

        var act = async () => await writer.WriteAsync(DoRequest("sprk_todo", TodoId));

        (await act.Should().ThrowAsync<SignalWriterEscalationException>()).Which.Reason
            .Should().Be(OntologyWriterFailureReason.NoCoreOwnerUnresolved);
        writerOrg.CreateCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("no-assigned-to")]
    [InlineData("no-linked-user")]
    [InlineData("disabled-only")]
    [InlineData("two-users")]
    public async Task D134_NoSingleEnabledAssignee_FallsBackToTheTodosOwnAnswer(string shape)
    {
        var links = new AssignedAccessTestDoubles.FakeAssignedAccessStore();
        AssignedLinkCandidate Linked(Guid user, bool disabled = false) =>
            new(user, AssigneeContactId, Guid.NewGuid(), IsDisabled: disabled, AccessMode: 0, ApplicationId: null, IsExternal: false);
        if (shape == "disabled-only") links.UsersByLink[AssigneeContactId] = [Linked(AssigneeUserId, disabled: true)];
        if (shape == "two-users") links.UsersByLink[AssigneeContactId] = [Linked(AssigneeUserId), Linked(Guid.NewGuid())];
        var writerOrg = new FakeOrganizationService();
        var ownership = AssigneeAware();

        await Build(writerOrg, PersonalTodo(shape == "no-assigned-to" ? null : AssigneeContactId), new FakeTimeProvider(FirstRun),
            ownership: ownership, links: links).WriteAsync(DoRequest("sprk_todo", TodoId));

        writerOrg.CreatedPayloads.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("ownerid")
            .Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId), "no single user: the To Do's own answer");
        ownership.Contexts.Should().ContainSingle();
    }

    [Fact]
    public async Task D134_ATodoFiledUnderAMatter_IsNotPersonal_TheCoreAnswerStands_AssigneeNeverAsked()
    {
        var writerOrg = new FakeOrganizationService();
        var ownership = AssigneeAware();

        await Build(writerOrg, PersonalTodo(AssigneeContactId, filedUnderMatter: true), new FakeTimeProvider(FirstRun),
            ownership: ownership, links: LinkedAssignee()).WriteAsync(DoRequest("sprk_todo", TodoId));

        writerOrg.CreatedPayloads.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("ownerid")
            .Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId));
        ownership.Contexts.Should().ContainSingle().Which.TargetEntityLogicalName.Should().Be("sprk_matter", "a core filing takes precedence");
    }

    [Fact]
    public async Task D134_ANoCoreEvent_HasNoAssignee_ItsOwnAnswerStands()
    {
        var writerOrg = new FakeOrganizationService();
        var ownership = AssigneeAware();

        await Build(writerOrg, new FakeSysadminClient().Stub(new Entity("sprk_event", EventId)), new FakeTimeProvider(FirstRun),
            ownership: ownership, links: LinkedAssignee()).WriteAsync(DoRequest("sprk_event", EventId));

        ownership.Contexts.Should().ContainSingle().Which.Parents.Should().Equal(new[] { new Ownership.RecordOwnershipParent("sprk_event", EventId) });
    }

    [Fact]
    public async Task D134_DeriveCoreRecord_NamesTheSameTeamTheWriteWould_ForTheJobsCompare()
    {
        var writer = Build(new FakeOrganizationService(), PersonalTodo(AssigneeContactId), new FakeTimeProvider(FirstRun),
            ownership: AssigneeAware(), links: LinkedAssignee());

        var core = await writer.DeriveCoreRecordAsync("sprk_todo", TodoId, SignalWriter.LaneDo);

        core.IsNoCore.Should().BeTrue();
        core.NoCoreOwner.Should().BeEquivalentTo(new EntityReference("team", AssigneeTeamId), "a team, never a user");
    }

    [Fact]
    public async Task D134_TheAssigneesTeamIsRefused_WritesNothing_NeverFallsBackToTheTodosOwnTeam()
    {
        // Review F3: the second question (the assignee's team, ForUserTeam) is refused — e.g. the assignee's unit has no default
        // team. The Decision Record refuses here (requireTeam); so does the Signal: nothing is written, the To Do's team is not used.
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver
        {
            AnswerFor = context => context.CallerSystemUserId == AssigneeUserId && context.Parents.Count == 0
                ? Ownership.RecordOwnerResolution.Refused(Ownership.RecordOwnerRefusal.NoOwnerSource, "the assignee's unit has no default team")
                : Ownership.RecordOwnerResolution.Owned(DefaultTeamId),
        };

        var result = await Build(writerOrg, PersonalTodo(AssigneeContactId), new FakeTimeProvider(FirstRun), ownership: ownership,
            links: LinkedAssignee()).WriteAsync(DoRequest("sprk_todo", TodoId));

        result.IsSkipped.Should().BeTrue();
        writerOrg.CreateCallCount.Should().Be(0);
        ownership.Contexts.Should().HaveCount(2, "the assignee's team was asked, and its refusal stands");
    }

    [Fact]
    public async Task D134_TheAssigneesTeamQuestionFaults_FailsClosed_NoCoreOwnerUnresolved()
    {
        var writerOrg = new FakeOrganizationService();
        var ownership = AssigneeAware();
        ownership.FaultFor = context => context.CallerSystemUserId == AssigneeUserId && context.Parents.Count == 0
            ? new TimeoutException("the resolver's read of the assignee's unit timed out")
            : null;

        var act = async () => await Build(writerOrg, PersonalTodo(AssigneeContactId), new FakeTimeProvider(FirstRun), ownership: ownership,
            links: LinkedAssignee()).WriteAsync(DoRequest("sprk_todo", TodoId));

        var thrown = await act.Should().ThrowAsync<SignalWriterEscalationException>();
        thrown.Which.Reason.Should().Be(OntologyWriterFailureReason.NoCoreOwnerUnresolved);
        thrown.Which.InnerException.Should().BeOfType<TimeoutException>();
        writerOrg.CreateCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("systemuser")]
    [InlineData("team-empty")]
    public async Task ReassignNoCoreOwner_NeverTakesAUserOrAnEmptyTeam_D130(string shape)
    {
        var writer = Build(new FakeOrganizationService(), new FakeSysadminClient(), new FakeTimeProvider(FirstRun));
        var owner = shape == "systemuser" ? new EntityReference("systemuser", TodoOwnerUserId) : new EntityReference("team", Guid.Empty);

        var act = async () => await writer.ReassignNoCoreOwnerAsync(Guid.NewGuid(), owner, "POL-DO-ITEM");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeriveCoreRecord_NoCoreOwnerRefused_IsTheWritersEscalation()
    {
        var writer = Build(new FakeOrganizationService(), new FakeSysadminClient().Stub(new Entity("sprk_todo", TodoId)),
            new FakeTimeProvider(FirstRun), ownership: new FakeOwnershipResolver
            {
                Answer = Ownership.RecordOwnerResolution.Refused(Ownership.RecordOwnerRefusal.NoOwnerSource, "none"),
            });

        var act = async () => await writer.DeriveCoreRecordAsync("sprk_todo", TodoId, SignalWriter.LaneDo);

        (await act.Should().ThrowAsync<SignalWriterEscalationException>()).Which.Reason.Should().Be(OntologyWriterFailureReason.OwnerRefused);
    }

    // ── #1783 / D-135: the person who asked is stamped with the owner; a job-raised Signal names nobody ─────────────────

    [Fact]
    public async Task WriteAsync_RequestedBy_IsStampedAsTheCreatorPerson_ByTheRealResolver_AndDecidesNothingAboutTheOwner()
    {
        var directory = TestInfrastructure.OwnershipDirectory.Standard().WithOrdinaryRoot("sprk_matter", MatterId);
        var writerOrg = new FakeOrganizationService();
        var requester = Ownership.RecordRequester.Of(TestInfrastructure.OwnershipDirectory.CallerUserId);

        await Build(writerOrg, new FakeSysadminClient(), new FakeTimeProvider(FirstRun), ownership: directory.Resolver())
            .WriteAsync(MatterSubjectRequest() with { RequestedBy = requester });

        var created = writerOrg.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<EntityReference>(Ownership.RecordCreatorPerson.Column)
            .Should().BeEquivalentTo(new EntityReference("systemuser", TestInfrastructure.OwnershipDirectory.CallerUserId));
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(
            new EntityReference("team", TestInfrastructure.OwnershipDirectory.ChildTeam), "the matter's unit's team, not the requester's");
    }

    [Fact]
    public async Task WriteAsync_JobRaised_NoRequestedBy_StampsNoCreatorPerson()
    {
        var directory = TestInfrastructure.OwnershipDirectory.Standard().WithOrdinaryRoot("sprk_matter", MatterId);
        var writerOrg = new FakeOrganizationService();

        await Build(writerOrg, new FakeSysadminClient(), new FakeTimeProvider(FirstRun), ownership: directory.Resolver())
            .WriteAsync(MatterSubjectRequest());

        writerOrg.CreatedPayloads.Should().ContainSingle().Which.Attributes.Should().NotContainKey(Ownership.RecordCreatorPerson.Column);
    }

    [Fact]
    public async Task WriteAsync_TheRealResolver_ASecureMatter_OwnsTheSignalBySecureRecordOwners_AnOrdinaryOne_ByItsUnitsTeam()
    {
        var secureMatter = Guid.NewGuid();
        var directory = TestInfrastructure.OwnershipDirectory.Standard()
            .WithOrdinaryRoot("sprk_matter", MatterId).WithSecureRoot("sprk_matter", secureMatter);
        var writerOrg = new FakeOrganizationService
        {
            TeamBusinessUnits =
            {
                [TestInfrastructure.OwnershipDirectory.SecureNamedTeam] = TestInfrastructure.OwnershipDirectory.SecureBu,
                [TestInfrastructure.OwnershipDirectory.ChildTeam] = TestInfrastructure.OwnershipDirectory.ChildBu,
            },
        };
        var writer = Build(writerOrg, new FakeSysadminClient(), new FakeTimeProvider(FirstRun), ownership: directory.Resolver());

        var ordinary = await writer.WriteAsync(MatterSubjectRequest());
        var secure = await writer.WriteAsync(MatterSubjectRequest() with { SubjectId = secureMatter, PolicyCode = "POL-SECURE" });

        writerOrg.Rows[ordinary.SignalId].GetAttributeValue<EntityReference>("ownerid")
            .Should().BeEquivalentTo(new EntityReference("team", TestInfrastructure.OwnershipDirectory.ChildTeam));
        ordinary.OwningBusinessUnitId.Should().Be(TestInfrastructure.OwnershipDirectory.ChildBu);
        ordinary.SecureOwnerTeamId.Should().BeNull();
        writerOrg.Rows[secure.SignalId].GetAttributeValue<EntityReference>("ownerid")
            .Should().BeEquivalentTo(new EntityReference("team", TestInfrastructure.OwnershipDirectory.SecureNamedTeam));
        secure.SecureOwnerTeamId.Should().Be(TestInfrastructure.OwnershipDirectory.SecureNamedTeam);
    }

    // ── Parity with the Decision Record (task 142): the same parents, in the same order, the same requester ─────────────

    public static TheoryData<string?, Guid, string, Guid, string[]> ParityShapes() => new()
    {
        // core record, its id, the subject, its id, the stamps (entity:id)
        { "sprk_matter", MatterId, "sprk_communication", CommunicationId, new[] { "sprk_matter:" + MatterId } },
        { "sprk_matter", MatterId, "sprk_matter", MatterId, new[] { "sprk_matter:" + MatterId } },
        { "sprk_project", ProjectId, "sprk_todo", TodoId, new[] { "sprk_project:" + ProjectId, "sprk_matter:" + MatterId } },
        { null, Guid.Empty, "sprk_todo", TodoId, Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(ParityShapes))]
    public void OwnershipContextOf_AsksTheSameQuestionAsTheDecisionRecordWriter_ForTheSameItem(
        string? coreEntity, Guid coreId, string subjectEntity, Guid subjectId, string[] stampText)
    {
        var stamps = stampText.Select(t => t.Split(':'))
            .Select(p => new Ownership.CoreAncestorStamp(p[0], "sprk_regarding" + p[0]["sprk_".Length..], Guid.Parse(p[1]))).ToList();
        var user = Guid.NewGuid();
        var dr = new DecisionRecordRequest(Guid.NewGuid(), PolicyVersionId,
            coreEntity is null ? null : new DecisionRecordCore(coreEntity, coreId, Guid.NewGuid()),
            new Sprk.Bff.Api.Services.Signals.Actions.DecisionRecordRef(subjectEntity, subjectId), user, null, [], [], null, [], new Dictionary<string, object?>(), null, null, null);

        var signal = SignalWriter.OwnershipContextOf(coreEntity, coreId, stamps, subjectEntity, subjectId, Ownership.RecordRequester.Of(user));
        var record = DecisionRecordWriter.OwnershipContextOf(dr, stamps);

        signal.Parents.Should().Equal(record.Parents);
        signal.TargetEntityLogicalName.Should().Be(record.TargetEntityLogicalName);
        signal.TargetRecordId.Should().Be(record.TargetRecordId);
        signal.RequestedBy.Should().Be(record.RequestedBy);
        signal.WhenUnfiled.Should().Be(record.WhenUnfiled);
    }

    public static TheoryData<string> OwnerParityShapes() => new()
    {
        "one-assignee", "no-assigned-to", "filed-under-matter", "assignee-unit-has-no-team",
        // K2 (task 143 follow-up): the two secure shapes. A secure answer wins over every ordinary one and over the assignee.
        "ordinary-core-secure-stamp-parent", "secure-todo",
    };

    [Theory]
    [MemberData(nameof(OwnerParityShapes))]
    public async Task D134_TheSignalAndItsDecisionRecord_AboutTheSameToDo_GetTheSameOwner_FromTheRealResolver(string shape)
    {
        // Review F3 (criterion "the Signal and its DR agree"): ONE world, uac-r2's REAL resolver, BOTH writers. The Decision
        // Record's assignee is read as DecisionCommitService reads it (PersonalToDoAssignee, only when the review has no core).
        // The To Do sits in the child unit, its Assigned To user in the general unit, a filed To Do's matter in the child unit.
        var secureStamp = shape == "ordinary-core-secure-stamp-parent"; // filed under an ordinary matter (the core) AND a secure project (a stamp)
        var secureTodo = shape == "secure-todo";                        // the To Do itself is owned in the Secure Record BU; it has no core
        var filed = shape == "filed-under-matter" || secureStamp;
        var lonelyBu = Guid.Parse("55555555-0000-4000-8000-0000000000e1");
        var directory = TestInfrastructure.OwnershipDirectory.Standard()
            .WithOrdinaryRoot("sprk_matter", MatterId)
            .WithBusinessUnit(lonelyBu, "A unit with no default team")
            .WithUser(AssigneeUserId, Guid.NewGuid(),
                shape == "assignee-unit-has-no-team" ? lonelyBu : TestInfrastructure.OwnershipDirectory.GeneralBu)
            .WithRecord("sprk_todo", TodoId,
                secureTodo ? TestInfrastructure.OwnershipDirectory.SecureBu
                    : filed ? TestInfrastructure.OwnershipDirectory.GeneralBu : TestInfrastructure.OwnershipDirectory.ChildBu,
                owningTeam: secureTodo ? TestInfrastructure.OwnershipDirectory.SecureNamedTeam : null,
                // The secure project is deliberately NOT a lookup on the directory's row: it reaches both writers only as a core-ancestor stamp
                // (read from the To Do by CoreAncestorResolver), so dropping the stamp parents from a writer's question changes its answer.
                extra: filed ? new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) } : null);
        if (secureStamp)
        {
            directory.WithSecureRoot("sprk_project", ProjectId);
        }

        var resolver = directory.Resolver();
        var sysadmin = PersonalTodo(shape == "no-assigned-to" ? null : AssigneeContactId, filedUnderMatter: filed, alsoFiledUnderProject: secureStamp);
        var links = LinkedAssignee();

        var writerOrg = new FakeOrganizationService
        {
            TeamBusinessUnits =
            {
                [TestInfrastructure.OwnershipDirectory.GeneralTeam] = TestInfrastructure.OwnershipDirectory.GeneralBu,
                [TestInfrastructure.OwnershipDirectory.ChildTeam] = TestInfrastructure.OwnershipDirectory.ChildBu,
                [TestInfrastructure.OwnershipDirectory.SecureNamedTeam] = TestInfrastructure.OwnershipDirectory.SecureBu,
            },
        };
        var signal = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: resolver, links: links)
            .WriteAsync(DoRequest("sprk_todo", TodoId));
        Guid? signalOwner = signal.IsSkipped ? null : writerOrg.Rows[signal.SignalId].GetAttributeValue<EntityReference>("ownerid").Id;

        var assignee = filed ? null : await PersonalToDoAssignee.ResolveAsync(sysadmin, links, TodoId, CancellationToken.None);
        var recordWriter = new DecisionRecordWriter(
            new OntologyWriterDataverseClient(() => new FakeOrganizationService(), NullLogger<OntologyWriterDataverseClient>.Instance),
            resolver,
            new Ownership.CoreAncestorResolver(sysadmin, StampColumnProbe, NullLogger<Ownership.CoreAncestorResolver>.Instance),
            sysadmin,
            new RecordTypeCatalogStub(RecordTypeCatalogStub.SpaarkeDev1), new FakeTimeProvider(FirstRun), NullLogger<DecisionRecordWriter>.Instance,
            new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing);
        var review = new DecisionRecordRequest(Guid.NewGuid(), PolicyVersionId,
            filed ? new DecisionRecordCore("sprk_matter", MatterId, Guid.NewGuid()) : null,
            new Sprk.Bff.Api.Services.Signals.Actions.DecisionRecordRef("sprk_todo", TodoId), TestInfrastructure.OwnershipDirectory.CallerUserId,
            null, [], [], null, [], new Dictionary<string, object?>(), null, null, null) with { AssigneeSystemUserId = assignee };
        Guid? recordOwner;
        try
        {
            recordOwner = (await recordWriter.ResolveOwnerAsync(review)).Resolution.OwningTeamId;
        }
        catch (DecisionRecordRefusedException)
        {
            recordOwner = null;
        }

        Guid? expected = shape switch
        {
            "one-assignee" => TestInfrastructure.OwnershipDirectory.GeneralTeam,   // the Assigned To user's unit's team
            "no-assigned-to" => TestInfrastructure.OwnershipDirectory.ChildTeam,   // the To Do's own unit's team
            "filed-under-matter" => TestInfrastructure.OwnershipDirectory.ChildTeam, // the matter's team: a core filing wins
            "ordinary-core-secure-stamp-parent" or "secure-todo" => TestInfrastructure.OwnershipDirectory.SecureNamedTeam, // secure-if-any
            _ => null,                                                             // both refuse; neither falls back
        };
        signalOwner.Should().Be(expected, "the Signal's owner");
        recordOwner.Should().Be(expected, "the Decision Record's owner");
    }

    [Fact]
    public async Task WriteAsync_DecideLaneItemWithNoCore_Refuses()
    {
        var sysadmin = new FakeSysadminClient()
            .Stub(new Entity("sprk_todo", TodoId) { ["ownerid"] = new EntityReference("systemuser", TodoOwnerUserId) });
        var writerOrg = new FakeOrganizationService();
        var request = DoRequest("sprk_todo", TodoId) with { Lane = SignalWriter.LaneDecide };

        var act = async () => await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(request);

        await act.Should().ThrowAsync<SignalWriterEscalationException>();
        writerOrg.CreateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_SecureWorkAssignment_IsOwnedBySecureTeam()
    {
        var sysadmin = new FakeSysadminClient();
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver { Answer = SecureAnswer() };

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: ownership)
            .WriteAsync(DoRequest("sprk_workassignment", WorkAssignmentId));

        var created = writerOrg.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", SecureTeamId));
        created.Attributes.Keys.Should().NotContain("owningbusinessunit");
        result.SecureOwnerTeamId.Should().Be(SecureTeamId);
    }

    [Fact]
    public async Task WriteAsync_TodoUnderASecureProjectWithNoMatter_IsWritten_SecureTeamOwned_NoSkip()
    {
        var sysadmin = new FakeSysadminClient()
            .Stub(new Entity("sprk_todo", TodoId) { ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId) });
        var writerOrg = new FakeOrganizationService();
        var ownership = new FakeOwnershipResolver { Answer = SecureAnswer() };

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), ownership: ownership)
            .WriteAsync(DoRequest("sprk_todo", TodoId));

        result.IsSkipped.Should().BeFalse("D-38: no skips");
        result.Created.Should().BeTrue();
        writerOrg.Rows[result.SignalId].GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureTeamId);
        ownership.Contexts.Should().ContainSingle().Which.TargetEntityLogicalName.Should().Be("sprk_project");
    }

    [Theory]
    [InlineData("sprk_event", "11111111-0000-0000-0000-0000000000e1")]
    [InlineData("sprk_todo", "11111111-0000-0000-0000-0000000000d1")]
    public async Task WriteAsync_SubjectOnARestrictedOrLimitedMatter_IsNeverSkipped(string subjectEntity, string subjectIdText)
    {
        // Restricted and Limited are ordinary answers to the ownership resolver (not Secure): the writer writes them.
        var subjectId = Guid.Parse(subjectIdText);
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId)
            .Stub(new Entity(subjectEntity, subjectId) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) });
        var writerOrg = new FakeOrganizationService();

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun),
            ownership: new FakeOwnershipResolver { Answer = Ownership.RecordOwnerResolution.Owned(DefaultTeamId) })
            .WriteAsync(DoRequest(subjectEntity, subjectId));

        result.IsSkipped.Should().BeFalse();
        result.Created.Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_ServiceRequestSubject_IsStillRefused()
    {
        var writerOrg = new FakeOrganizationService();

        var act = async () => await Build(writerOrg, new FakeSysadminClient(), new FakeTimeProvider(FirstRun))
            .WriteAsync(DoRequest("sprk_servicerequest", ServiceRequestId));

        (await act.Should().ThrowAsync<SignalWriterEscalationException>()).Which.Reason
            .Should().Be(OntologyWriterFailureReason.MatterDerivationUnverified);
        writerOrg.CreateCallCount.Should().Be(0);
    }

    // ── Review F1: a subject that moved between no-core and a core is refused on reconcile, loudly ───────

    [Fact]
    public async Task WriteAsync_ReEvaluation_NoCoreSignalWhoseTodoIsNowFiledUnderAMatter_IsRefusedLoudly_NothingUpdated()
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, reasons) = ListenFailureReasonsScoped(scope);
        using var _ = listener;
        var logger = new CapturingLogger<SignalWriter>();
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId)
            .Stub(new Entity("sprk_todo", TodoId) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) });
        var writerOrg = new FakeOrganizationService();
        var request = DoRequest("sprk_todo", TodoId);
        writerOrg.SeedExisting(SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_todo", Lower(TodoId), 1), new Entity("sprk_signal")
        {
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["ownerid"] = new EntityReference("systemuser", TodoOwnerUserId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun), logger).WriteAsync(request);

        result.IsSkipped.Should().BeTrue();
        result.SkippedRefusalCode.Should().Be(OntologyWriterFailureReason.CoreRecordChanged);
        writerOrg.UpdateCalls.Should().BeEmpty("a changed subject is never passed silently, and never half-updated");
        reasons().Should().Equal(OntologyWriterFailureReason.CoreRecordChanged);
        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.EventId.Id.Should().Be(OntologyWriterEvents.WriteSkippedCoreRecordChanged.Id);
    }

    [Fact]
    public async Task WriteAsync_ReEvaluation_SignalGroupedUnderAMatterWhoseTodoIsNowUnfiled_IsRefusedLoudly()
    {
        var sysadmin = new FakeSysadminClient()
            .Stub(new Entity("sprk_todo", TodoId) { ["ownerid"] = new EntityReference("systemuser", TodoOwnerUserId) });
        var writerOrg = new FakeOrganizationService();
        var request = DoRequest("sprk_todo", TodoId);
        writerOrg.SeedExisting(SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_todo", Lower(TodoId), 1), new Entity("sprk_signal")
        {
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["sprk_corerecordid"] = Lower(MatterId),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun)).WriteAsync(request);

        result.SkippedRefusalCode.Should().Be(OntologyWriterFailureReason.CoreRecordChanged);
        writerOrg.UpdateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_ReEvaluation_TodoRefiledFromOneMatterToAnother_IsRefusedLoudly()
    {
        var otherMatter = Guid.NewGuid();
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(otherMatter, BusinessUnitId)
            .Stub(new Entity("sprk_todo", TodoId) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", otherMatter) });
        var writerOrg = new FakeOrganizationService();
        var request = DoRequest("sprk_todo", TodoId);
        writerOrg.SeedExisting(SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_todo", Lower(TodoId), 1), new Entity("sprk_signal")
        {
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["sprk_corerecordid"] = Lower(MatterId),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun)).WriteAsync(request);

        result.SkippedRefusalCode.Should().Be(OntologyWriterFailureReason.CoreRecordChanged);
    }

    [Theory]
    [InlineData("team")]       // the item's owner answer changed since the Signal was written (the job re-owns it, task 031)
    [InlineData("systemuser")] // a no-core Signal written before task 143 (owned by the item's user): known limit, refused loudly
    public async Task WriteAsync_ReEvaluation_NoCoreSignalWhoseOwnerAnswerChanged_IsRefusedLoudly(string ownerKind)
    {
        var sysadmin = new FakeSysadminClient().Stub(new Entity("sprk_todo", TodoId));
        var writerOrg = new FakeOrganizationService();
        var request = DoRequest("sprk_todo", TodoId);
        writerOrg.SeedExisting(SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_todo", Lower(TodoId), 1), new Entity("sprk_signal")
        {
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["ownerid"] = new EntityReference(ownerKind, Guid.NewGuid()),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun)).WriteAsync(request);

        result.SkippedRefusalCode.Should().Be(OntologyWriterFailureReason.CoreRecordChanged);
        writerOrg.UpdateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_ReEvaluation_NoCoreSignalUnchanged_RefreshesLastEvaluated()
    {
        var sysadmin = new FakeSysadminClient()
            .Stub(new Entity("sprk_todo", TodoId) { ["ownerid"] = new EntityReference("systemuser", TodoOwnerUserId) });
        var writerOrg = new FakeOrganizationService();
        var request = DoRequest("sprk_todo", TodoId);
        writerOrg.SeedExisting(SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_todo", Lower(TodoId), 1), new Entity("sprk_signal")
        {
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun)).WriteAsync(request);

        result.IsSkipped.Should().BeFalse();
        writerOrg.UpdateCalls.Should().ContainSingle().Which.Fields.Keys.Should().BeEquivalentTo(new[] { "sprk_lastevaluated" });
    }

    [Fact]
    public async Task WriteAsync_ReEvaluation_SubjectDateCleared_ClearsTheOpenSignalsDate()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId)
            .Stub(new Entity("sprk_todo", TodoId) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) }); // no sprk_duedate
        var writerOrg = new FakeOrganizationService();
        var request = DoRequest("sprk_todo", TodoId);
        var existingId = writerOrg.SeedExisting(SignalWriter.BuildDedupeKey(request.PolicyCode, "sprk_todo", Lower(TodoId), 1), new Entity("sprk_signal")
        {
            ["sprk_signalstatus"] = new OptionSetValue(100000000),
            ["sprk_corerecordid"] = MatterId.ToString("D"),
            ["sprk_duedate"] = new DateTime(2026, 10, 10),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
        });
        writerOrg.ThrowDuplicateOnNextCreate = true;

        await Build(writerOrg, sysadmin, new FakeTimeProvider(SecondRun)).WriteAsync(request);

        writerOrg.UpdateCalls.Should().ContainSingle().Which.Fields.Should().ContainKey("sprk_duedate");
        writerOrg.Rows[existingId].GetAttributeValue<DateTime?>("sprk_duedate").Should().BeNull();
    }

    // ── D-37: a subject with more than one core record ───────────────────────────────────

    private static FakeSysadminClient TodoWithMatterAndProject(string? pair, params (string Column, string Entity, Guid Id)[] extra)
    {
        var todo = new Entity("sprk_todo", TodoId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
            ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId),
        };
        if (pair is not null) todo["sprk_regardingrecordid"] = pair;
        foreach (var (column, entity, id) in extra) todo[column] = new EntityReference(entity, id);
        return new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId)
            .WithBusinessUnit("sprk_project", ProjectId, ProjectBusinessUnitId).Stub(todo);
    }

    [Fact]
    public async Task WriteAsync_MatterAndProject_RegardingPairNamesTheProject_ProjectWins()
    {
        var writerOrg = new FakeOrganizationService();
        var sysadmin = TodoWithMatterAndProject(Lower(ProjectId));

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(DoRequest("sprk_todo", TodoId));

        writerOrg.Rows[result.SignalId].GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(ProjectId));
        writerOrg.Rows[result.SignalId].Attributes.Keys.Should().NotContain("sprk_matter");
    }

    [Fact]
    public async Task WriteAsync_MatterAndProject_NoDirectLink_MatterWins()
    {
        var writerOrg = new FakeOrganizationService();
        var sysadmin = TodoWithMatterAndProject(pair: null);

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(DoRequest("sprk_todo", TodoId));

        writerOrg.Rows[result.SignalId].GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(MatterId));
        writerOrg.Rows[result.SignalId].GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);
    }

    [Fact]
    public async Task WriteAsync_ProjectAndWorkAssignment_NoDirectLink_StillAmbiguous_Stops()
    {
        var writerOrg = new FakeOrganizationService();
        var todo = new Entity("sprk_todo", TodoId)
        {
            ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId),
            ["sprk_regardingworkassignment"] = new EntityReference("sprk_workassignment", WorkAssignmentId),
        };
        var sysadmin = new FakeSysadminClient().Stub(todo);

        var act = async () => await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(DoRequest("sprk_todo", TodoId));

        (await act.Should().ThrowAsync<SignalWriterEscalationException>()).Which.Reason
            .Should().Be(OntologyWriterFailureReason.CoreRecordAmbiguous);
        writerOrg.CreateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_MatterProjectAndWorkAssignment_MatterBeatsProjectButWorkAssignmentRemains_Stops()
    {
        var writerOrg = new FakeOrganizationService();
        var sysadmin = TodoWithMatterAndProject(pair: null, ("sprk_regardingworkassignment", "sprk_workassignment", WorkAssignmentId));

        var act = async () => await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(DoRequest("sprk_todo", TodoId));

        (await act.Should().ThrowAsync<SignalWriterEscalationException>()).Which.Reason
            .Should().Be(OntologyWriterFailureReason.CoreRecordAmbiguous);
    }

    // ── Fail closed ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_CoreResolverFails_RefusesLogsErrorMetersAndWritesNothing()
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, reasons) = ListenFailureReasonsScoped(scope);
        using var _ = listener;
        var logger = new CapturingLogger<SignalWriter>();
        var sysadmin = new FakeSysadminClient { ThrowOnRetrieveOf = "sprk_todo" };
        var writerOrg = new FakeOrganizationService();

        var act = async () => await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun), logger).WriteAsync(DoRequest("sprk_todo", TodoId));

        (await act.Should().ThrowAsync<SignalWriterEscalationException>()).Which.Reason
            .Should().Be(OntologyWriterFailureReason.CoreRecordUnresolved);
        writerOrg.CreateCallCount.Should().Be(0);
        reasons().Should().Equal(OntologyWriterFailureReason.CoreRecordUnresolved);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.EventId.Id.Should().Be(OntologyWriterEvents.WriteRefused.Id);
    }

    [Fact]
    public async Task WriteAsync_CoreTypeHasNoCatalogRow_Refuses()
    {
        var sysadmin = new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId);
        sysadmin.CatalogRowCount["sprk_matter"] = 0;
        var writerOrg = new FakeOrganizationService();

        var act = async () => await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(MatterSubjectRequest());

        (await act.Should().ThrowAsync<SignalWriterEscalationException>()).Which.Reason
            .Should().Be(OntologyWriterFailureReason.CoreRecordTypeNotCataloged);
        writerOrg.CreateCallCount.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_CommunicationFiledUnderAProject_GroupsUnderTheProject()
    {
        var sysadmin = new FakeSysadminClient().WithBusinessUnit("sprk_project", ProjectId, ProjectBusinessUnitId)
            .Stub(new Entity("sprk_communication", CommunicationId) { ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId) });
        var writerOrg = new FakeOrganizationService();

        var result = await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(CommunicationSubjectRequest());

        writerOrg.Rows[result.SignalId].GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(ProjectId));
    }

    // =====================================================================================
    // Static invariants
    // =====================================================================================

    [Fact]
    public void VerifiedSubjects_AreAllPresentInRegardingLookupByEntity()
    {
        SignalWriter.VerifiedSubjects.Should().BeSubsetOf(SignalWriter.RegardingLookupByEntity.Keys);
        SignalWriter.DueDateColumnBySubject.Keys.Should().BeSubsetOf(SignalWriter.VerifiedSubjects);
    }

    [Fact]
    public void BuildDedupeKey_ComposesPolicyCodeSubjectTypeAndCleanId()
    {
        var key = SignalWriter.BuildDedupeKey("POL-COMMIT-BUDGET", "sprk_matter", "2444af6d-e1f2-f011-8406-7ced8d1dc988", 1);

        key.Should().Be("POL-COMMIT-BUDGET|sprk_matter|2444af6d-e1f2-f011-8406-7ced8d1dc988|1", "D-13: the key ends with the episode");
    }

    /// <summary>
    /// Task 141 (D-132): the pair type became a Lookup, and the dedupe key must not change with it. The literal keys below are
    /// exactly what the pre-141 writer composed for the dev seed subjects (matter REAL-2026-123456.01 and its communication;
    /// spaarkedev1 holds no sprk_signal row today, read 2026-10-13, so these literals are the existing format, not a live
    /// read). The new writer must compose the same key, and must reconcile to a row that already carries it.
    /// </summary>
    [Theory]
    [InlineData("sprk_matter", "POL-COMMIT-BUDGET|sprk_matter|2444af6d-e1f2-f011-8406-7ced8d1dc988|1")]
    [InlineData("sprk_communication", "POL-COMMENT-FLAG|sprk_communication|f00b6389-8fbf-f111-aaaf-0022482913fc|1")]
    public async Task WriteAsync_DedupeKey_IsThePreLookupKey_AndAnExistingRowWithItIsReconciled_D132(string subjectEntity, string existingKey)
    {
        var request = subjectEntity == "sprk_matter" ? MatterSubjectRequest() : CommunicationSubjectRequest();
        FakeSysadminClient Sysadmin() => new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId)
            .Stub(new Entity("sprk_communication", CommunicationId) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) });

        var freshOrg = new FakeOrganizationService();
        var created = await Build(freshOrg, Sysadmin(), new FakeTimeProvider(FirstRun)).WriteAsync(request);
        var createdRow = freshOrg.Rows[created.SignalId];
        createdRow.GetAttributeValue<string>("sprk_dedupekey").Should().Be(existingKey,
            "the key is composed from the subject logical name the writer holds, never from the (now lookup) pair type");
        createdRow.Attributes.ContainsKey("sprk_regardingrecordtype").Should().Be(subjectEntity == "sprk_matter",
            "the matter's pair is typed, the communication's is untyped; the key is the same shape either way");

        var existingOrg = new FakeOrganizationService();
        var existingId = existingOrg.SeedExisting(existingKey, new Entity("sprk_signal")
        {
            ["sprk_corerecordid"] = MatterId.ToString("D"),
            ["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusOpen),
            ["ownerid"] = new EntityReference("team", DefaultTeamId),
        });
        existingOrg.ThrowDuplicateOnNextCreate = true;

        var reconciled = await Build(existingOrg, Sysadmin(), new FakeTimeProvider(SecondRun)).WriteAsync(request);

        reconciled.Created.Should().BeFalse("the existing row's key is the key the new writer composes");
        reconciled.SignalId.Should().Be(existingId);
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

    internal static SignalWriter Build(
        FakeOrganizationService writerOrg, FakeSysadminClient sysadmin, FakeTimeProvider clock, ILogger<SignalWriter>? logger = null,
        Ownership.IRecordOwnershipResolver? ownership = null, RecordTypeCatalogStub? recordTypes = null,
        AssignedAccessTestDoubles.FakeAssignedAccessStore? links = null) =>
        new(new OntologyWriterDataverseClient(() => writerOrg, NullLogger<OntologyWriterDataverseClient>.Instance),
            sysadmin,
            new Ownership.CoreAncestorResolver(sysadmin, StampColumnProbe, NullLogger<Ownership.CoreAncestorResolver>.Instance),
            recordTypes ?? new RecordTypeCatalogStub(RecordTypeCatalogStub.SpaarkeDev1),
            ownership ?? new FakeOwnershipResolver(), Scopes(links), clock, logger ?? NullLogger<SignalWriter>.Instance);

    /// <summary>Task 143: the scope the writer resolves its two SCOPED services from, as the host registers them — the D-134
    /// link read (<see cref="AssignedAccessStore"/>; default: nobody linked) and, when a test re-owns, uac-r2's reconciler.</summary>
    internal static IServiceScopeFactory Scopes(
        AssignedAccessTestDoubles.FakeAssignedAccessStore? links = null, Func<IServiceProvider, Sprk.Bff.Api.Services.Access.SecureChildReconciler>? reconciler = null)
    {
        var services = new ServiceCollection();
        var store = links ?? new AssignedAccessTestDoubles.FakeAssignedAccessStore();
        services.AddScoped<AssignedAccessStore>(_ => store);
        if (reconciler is not null) services.AddScoped(reconciler);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static Task<IReadOnlySet<string>> StampColumnProbe(string entityLogicalName, CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
            Ownership.CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute), StringComparer.OrdinalIgnoreCase));

    private static readonly Guid SecureTeamId = Guid.Parse("6eabc7f9-13be-f111-a05b-0022482913fc");
    private static readonly Guid SecureBusinessUnitId = Guid.Parse("d9ec0b6f-80a0-f111-aaac-000d3a99d1d7");
    private static readonly Guid DefaultTeamId = Guid.Parse("09fbf21c-1872-f011-b4cb-7c1e52671ad0");

    private static Ownership.RecordOwnerResolution SecureAnswer() =>
        Ownership.RecordOwnerResolution.Owned(SecureTeamId) with { IsSecureOwner = true };

    internal static SignalWriteRequest MatterSubjectRequest() => new(
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

    internal static SignalWriteRequest CommunicationSubjectRequest() => new(
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
    internal sealed class FakeOwnershipResolver : Ownership.IRecordOwnershipResolver
    {
        public Ownership.RecordOwnerResolution Answer { get; set; } = Ownership.RecordOwnerResolution.Owned(DefaultTeamId);

        /// <summary>Task 143: an answer per question (null: <see cref="Answer"/>).</summary>
        public Func<Ownership.RecordOwnershipContext, Ownership.RecordOwnerResolution>? AnswerFor { get; set; }

        /// <summary>Task 143: a resolver fault per question (a Dataverse read failure inside the resolver).</summary>
        public Func<Ownership.RecordOwnershipContext, Exception?>? FaultFor { get; set; }

        public List<Ownership.RecordOwnershipContext> Contexts { get; } = new();

        public Task<Ownership.RecordOwnerResolution> ResolveOwnerAsync(Ownership.RecordOwnershipContext context, CancellationToken ct)
        {
            Contexts.Add(context);
            if (FaultFor?.Invoke(context) is { } fault) return Task.FromException<Ownership.RecordOwnerResolution>(fault);
            return Task.FromResult(AnswerFor?.Invoke(context) ?? Answer);
        }

        public Task<Guid?> ResolveOwningTeamAsync(Ownership.RecordOwnershipContext context, CancellationToken ct) => throw new NotImplementedException();
        public Task<Ownership.RecordOwnerResolution> ReparentAsync(
            Ownership.RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct) => throw new NotImplementedException();
    }

    internal sealed class FakeSysadminClient : IGenericEntityService
    {
        private readonly Dictionary<(string Entity, Guid Id), Entity> _rows = new();
        private readonly Dictionary<string, Guid> _catalog = new(StringComparer.Ordinal)
        {
            ["sprk_matter"] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            ["sprk_project"] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
            ["sprk_workassignment"] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
            ["sprk_servicerequest"] = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"),
        };

        /// <summary>Every retrieve, with the columns asked for.</summary>
        public List<(string Entity, string[] Columns)> Retrieved { get; } = new();

        /// <summary>How many active catalog rows the lookup finds, per core table (default 1).</summary>
        public Dictionary<string, int> CatalogRowCount { get; } = new(StringComparer.Ordinal);

        /// <summary>A retrieve of this table faults (a Dataverse read failure).</summary>
        public string? ThrowOnRetrieveOf { get; set; }

        /// <summary>Task 143: a retrieve that asks for this column faults (the D-134 Assigned To read of the To Do).</summary>
        public string? ThrowOnRetrieveWithColumn { get; set; }

        public Guid CatalogId(string coreEntity) => _catalog[coreEntity];

        public FakeSysadminClient Stub(Entity row)
        {
            if (_rows.TryGetValue((row.LogicalName, row.Id), out var existing))
            {
                foreach (var attribute in row.Attributes) existing[attribute.Key] = attribute.Value;
            }
            else
            {
                _rows[(row.LogicalName, row.Id)] = row;
            }

            return this;
        }

        public FakeSysadminClient WithBusinessUnit(string entity, Guid id, Guid businessUnitId) =>
            Stub(new Entity(entity, id) { ["owningbusinessunit"] = new EntityReference("businessunit", businessUnitId) });

        public FakeSysadminClient WithMatterBusinessUnit(Guid matterId, Guid businessUnitId) =>
            WithBusinessUnit("sprk_matter", matterId, businessUnitId);

        public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default)
        {
            Retrieved.Add((entityLogicalName, columns));
            if (ThrowOnRetrieveOf == entityLogicalName || (ThrowOnRetrieveWithColumn is { } column && columns.Contains(column)))
            {
                throw new InvalidOperationException("simulated Dataverse read failure");
            }

            var entity = new Entity(entityLogicalName, id);
            if (_rows.TryGetValue((entityLogicalName, id), out var row))
            {
                foreach (var attribute in row.Attributes) entity[attribute.Key] = attribute.Value;
            }

            return Task.FromResult(entity);
        }

        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default)
        {
            var result = new EntityCollection();
            if (query.EntityName == "sprk_recordtype_ref"
                && query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_recordlogicalname")?.Values[0] is string core
                && _catalog.TryGetValue(core, out var catalogId))
            {
                var count = CatalogRowCount.TryGetValue(core, out var n) ? n : 1;
                for (var i = 0; i < count; i++) result.Entities.Add(new Entity("sprk_recordtype_ref", i == 0 ? catalogId : Guid.NewGuid()));
            }

            return Task.FromResult(result);
        }

        public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(Guid Id, bool Created)> UpsertAsync(Entity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateIfRowVersionMatchesAsync(string entityLogicalName, Guid id, string rowVersion, Dictionary<string, object> fields, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default) => throw new NotImplementedException();
        public Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AssociateAsync(string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct = default) => throw new NotImplementedException();
    }

    /// <summary>
    /// A fake of the Dataverse SDK's OWN <see cref="IOrganizationServiceAsync2"/> — not a custom interface of
    /// this project's (ADR-010 F10). Implements CreateAsync/RetrieveAsync/ExecuteAsync(RetrieveRequest)/UpdateAsync
    /// meaningfully; everything else throws (unused by <see cref="SignalWriter"/>).
    /// </summary>
    internal sealed class FakeOrganizationService : IOrganizationServiceAsync2
    {
        public readonly Dictionary<Guid, Entity> Rows = new();
        private readonly Dictionary<string, Guid> _byDedupeKey = new(StringComparer.Ordinal);
        public readonly List<(Guid Id, Dictionary<string, object> Fields)> UpdateCalls = new();
        private readonly Dictionary<(string Entity, Guid Id), Entity> _retrieveStubs = new();

        public bool ThrowDuplicateOnNextCreate { get; set; }
        public bool ThrowAccessDeniedOnNextCreate { get; set; }
        public int CreateCallCount { get; private set; }
        public Guid? ForcedOwningTeamOnVerifyRetrieve { get; set; }
        public EntityReference? ForcedOwnerOnVerifyRetrieve { get; set; }

        /// <summary>Task 143: the post-create owner read faults.</summary>
        public bool ThrowOnVerifyRetrieve { get; set; }

        /// <summary>Task 143: the business unit Dataverse derives for a team owner (default: the Secure team's unit for
        /// <see cref="SecureTeamId"/>, the general unit for <see cref="DefaultTeamId"/>, none for any other team).</summary>
        public Dictionary<Guid, Guid> TeamBusinessUnits { get; } = new()
        {
            [SecureTeamId] = SecureBusinessUnitId,
            [DefaultTeamId] = BusinessUnitId,
        };

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
                // Dataverse derives these from a team owner: owningteam, and the team's own business unit.
                entity["owningteam"] = new EntityReference("team", teamOwner.Id);
                if (TeamBusinessUnits.TryGetValue(teamOwner.Id, out var teamUnit))
                    entity["owningbusinessunit"] = new EntityReference("businessunit", teamUnit);
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
                if (ThrowOnVerifyRetrieve)
                {
                    throw new InvalidOperationException("simulated owner read-back failure");
                }

                if (ForcedOwnerOnVerifyRetrieve is { } forcedOwner)
                {
                    var ownerClone = new Entity(signalRow.LogicalName, signalRow.Id);
                    foreach (var attribute in signalRow.Attributes) ownerClone[attribute.Key] = attribute.Value;
                    ownerClone["ownerid"] = forcedOwner;
                    return Task.FromResult(ownerClone);
                }

                if (ForcedOwningTeamOnVerifyRetrieve is { } forcedTeam)
                {
                    var teamClone = new Entity(signalRow.LogicalName, signalRow.Id);
                    foreach (var attribute in signalRow.Attributes) teamClone[attribute.Key] = attribute.Value;
                    teamClone["owningteam"] = new EntityReference("team", forcedTeam);
                    return Task.FromResult(teamClone);
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
