using System.Diagnostics.Metrics;
using System.ServiceModel;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Xunit;
using Ownership = Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 031: the re-evaluating job, its side-effect-free evaluation core and the episode rules (D-13, D-25, D-32, D-33,
/// D-35, D-36, D-38, D-39, D-61; FR-03, FR-15, FR-16, FR-17a, FR-47, FR-48).
/// </summary>
/// <remarks>
/// <para>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): the real <see cref="SignalReevaluationJob"/>,
/// <see cref="SignalEvaluator"/>, <see cref="SignalWriter"/>, <see cref="PolicyVersionValidator"/> and uac-r2's
/// <see cref="Ownership.CoreAncestorResolver"/> run over two in-memory Dataverse seams: the shared app-only
/// <see cref="IGenericEntityService"/> (reads) and the SDK's own <see cref="IOrganizationServiceAsync2"/> behind the writer's
/// dedicated client (writes). The ownership resolver is faked at its interface. Every column, entity and option value used
/// here was read from spaarkedev1 on 2026-10-10 (notes/031-evaluator-progress.md, "Live schema read for this task");
/// <c>sprk_signal</c> writes are additionally exercised against the live schema by <c>SignalWriterSeamTests</c>.</para>
/// </remarks>
public partial class SignalReevaluationJobTests
{
    private static readonly DateTimeOffset RunAt = new(2026, 10, 10, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid PolicyId = Guid.Parse("4d204810-61bf-f111-aaaf-0022482913fc");
    private static readonly Guid VersionId = Guid.Parse("e51906ea-0000-4000-8000-000000000002");
    private static readonly Guid MatterId = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");
    private static readonly Guid OtherMatterId = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc989");
    private static readonly Guid BusinessUnitId = Guid.Parse("06fbf21c-1872-f011-b4cb-7c1e52671ad0");
    private static readonly Guid DefaultTeamId = Guid.Parse("09fbf21c-1872-f011-b4cb-7c1e52671ad0");
    private static readonly Guid SecureTeamId = Guid.Parse("6eabc7f9-13be-f111-a05b-0022482913fc");
    private static readonly Guid SecureBusinessUnitId = Guid.Parse("d9ec0b6f-80a0-f111-aaac-000d3a99d1d7");
    private static readonly Guid CommunicationId = Guid.Parse("f00b6389-8fbf-f111-aaaf-0022482913fc");
    private static readonly Guid TodoId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000a1");
    private static readonly Guid ProjectId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000b1");
    private static readonly Guid UserId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000c1");
    private static readonly Guid OtherUserId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000c2");
    private static readonly Guid OtherTeamId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000d1");
    private static readonly Guid OtherBusinessUnitId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000d2");

    private const string PolicyCode = "POL-COMMIT-BUDGET";
    private const string TodoPolicyCode = "POL-TODO-OVERDUE";
    private const int Existence = 100000002;
    private const int Threshold = 100000000;

    /// <summary>Task 025: a spend threshold, the same compiled-predicate contract as Existence (D-16).</summary>
    private const string UtilizationBody =
        """{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_budgetutilizationpercent","operator":">=","value":90}}""";

    private const string UtilizationTemplate = "Budget utilization has reached {{sprk_budgetutilizationpercent}} percent.";

    /// <summary>The live Path B shape (dev policy POL-COMMIT-BUDGET v2), with a one-value category list so its token pins.</summary>
    private const string PathBBody = """
        {"type":"Existence","subject":"sprk_matter","when":{},"all":[
          {"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_triagecategory":[100000001],"sprk_receiveddate":{">=":"now-30d"}}},
          {"notExists":"sprk_budgetrevision","path":"sprk_matter","filter":{"sprk_revisedon":{">=":"now-30d"}}}]}
        """;

    private const string PathBTemplate = "A {{sprk_triagecategory}} communication arrived with no budget revision in 30 days.";

    private const string TodoOverdueBody = """{"type":"Existence","subject":"sprk_todo","when":{"sprk_duedate":{"<":"now"}},"all":[]}""";

    // =====================================================================================
    // D-13: the episode-scoped key (before any Signal is written)
    // =====================================================================================

    [Fact]
    public void DedupeKey_IsEpisodeScoped_AndParsesBackFromTheRight()
    {
        var key = SignalWriter.BuildDedupeKey("POL|ODD", "sprk_todo", Lower(TodoId), 12);

        key.Should().Be($"POL|ODD|sprk_todo|{Lower(TodoId)}|12");
        SignalWriter.TryParseDedupeKey(key, out var code, out var entity, out var id, out var episode).Should().BeTrue();
        (code, entity, id, episode).Should().Be(("POL|ODD", "sprk_todo", TodoId, 12));
    }

    [Theory]
    [InlineData("POL-COMMIT-BUDGET|sprk_matter|2444af6d-e1f2-f011-8406-7ced8d1dc988")]      // the pre-D-13 key
    [InlineData("POL-COMMIT-BUDGET|sprk_matter|2444af6d-e1f2-f011-8406-7ced8d1dc988|0")]    // no episode 0
    [InlineData("POL-COMMIT-BUDGET|sprk_matter|2444AF6D-E1F2-F011-8406-7CED8D1DC988|1")]    // ids are lowercase (FR-03)
    [InlineData("POL-COMMIT-BUDGET|Sprk_Matter|2444af6d-e1f2-f011-8406-7ced8d1dc988|1")]
    [InlineData("|sprk_matter|2444af6d-e1f2-f011-8406-7ced8d1dc988|1")]
    public void DedupeKey_ThatIsNotEpisodeScoped_DoesNotParse(string key) =>
        SignalWriter.TryParseDedupeKey(key, out _, out _, out _, out _).Should().BeFalse();

    [Fact]
    public async Task WriteAsync_RefusesEpisodeZero()
    {
        var h = new Harness();
        var request = new SignalWriteRequest(PolicyId, PolicyCode, VersionId, "sprk_matter", MatterId, null, "Headline",
            SignalWriter.LaneDecide, null, "Text.", new Dictionary<string, object?>())
        { Episode = 0 };

        await h.Writer.Invoking(w => w.WriteAsync(request)).Should().ThrowAsync<ArgumentException>().WithMessage("*Episode 0*");
        h.Org.WriteCalls.Should().Be(0);
    }

    // =====================================================================================
    // Job: create, close, unchanged (D-61), telemetry
    // =====================================================================================

    [Fact]
    public async Task Run_PredicateNowHolds_CreatesTheSignalWithAKeyEndingInEpisode1()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        var created = h.Org.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<string>("sprk_dedupekey").Should().Be($"{PolicyCode}|sprk_matter|{Lower(MatterId)}|1");
        created.GetAttributeValue<string>("sprk_regardingrecordnumber").Should().Be("MAT-0001", "R13: from the catalog row's number field");
        created.GetAttributeValue<string>("sprk_regardingrecordname").Should().Be("Acme v Beta");
        created.GetAttributeValue<string>("sprk_policycode").Should().Be(PolicyCode);
    }

    [Fact]
    public async Task Run_OpenSignalWhosePredicateNoLongerHolds_IsClosedConditionCleared_WithNoDecisionRecord()
    {
        var h = new Harness().WithPathBPolicy(); // no hit
        var signalId = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        var update = h.Org.ConditionalUpdates.Should().ContainSingle().Subject;
        update.Id.Should().Be(signalId);
        update.Fields["sprk_signalstatus"].Should().BeEquivalentTo(new OptionSetValue(SignalWriter.SignalStatusResolved));
        update.Fields["sprk_resolutiontype"].Should().BeEquivalentTo(new OptionSetValue(SignalWriter.ResolutionConditionCleared));
        update.Fields.Should().ContainKey("sprk_resolvedon");
        update.Fields.Should().NotContainKey("sprk_decisionrecord", "a system closure has no Decision Record (FR-16)");
        h.Org.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Run_AcknowledgedSignalWhosePredicateNoLongerHolds_IsClosedConditionCleared_LikeAnOpenOne()
    {
        // D-119 / D-128 Q18: a budget inquiry or a reminder leaves the Signal Acknowledged and linked to its Decision Record; the
        // nightly pass is what closes it once the work is done or the due date moves.
        var h = new Harness().WithPathBPolicy(); // no hit
        var signalId = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusAcknowledged, coreId: MatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        var update = h.Org.ConditionalUpdates.Should().ContainSingle().Subject;
        update.Id.Should().Be(signalId);
        update.Fields["sprk_signalstatus"].Should().BeEquivalentTo(new OptionSetValue(SignalWriter.SignalStatusResolved));
        update.Fields["sprk_resolutiontype"].Should().BeEquivalentTo(new OptionSetValue(SignalWriter.ResolutionConditionCleared));
        update.Fields.Should().NotContainKey("sprk_decisionrecord", "a system closure writes no Decision Record; the existing link stays");
    }

    [Fact]
    public async Task Run_OpenSignalWhosePredicateStillHolds_IssuesNoWriteAtAll_AndEmitsRunLevelLastEvaluated()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        await h.RunAsync();
        var lastEvaluatedBefore = h.Org.Rows.Values.Single().GetAttributeValue<DateTime?>("sprk_lastevaluated");
        h.Org.ResetCounts();
        h.Clock.Advance(TimeSpan.FromDays(1));

        using var metrics = new MetricCapture();
        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.WriteCalls.Should().Be(0, "D-61: an unchanged result writes nothing, not even sprk_lastevaluated");
        h.Org.Rows.Values.Single().GetAttributeValue<DateTime?>("sprk_lastevaluated").Should().Be(lastEvaluatedBefore);
        metrics.Gauge("ontology.evaluator.last_evaluated").Should().Contain(h.Clock.GetUtcNow().ToUnixTimeSeconds(),
            "the run-level last-evaluated goes to telemetry");
        h.Log.Should().Contain(e => e.EventId.Id == OntologyWriterEvents.EvaluatorHeartbeat.Id && e.Message.Contains("unchanged=1"));
    }

    [Fact]
    public async Task Run_AResolvedSignal_IsNeverClosedAgain()
    {
        var h = new Harness().WithPathBPolicy(); // no hit
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: RunAt.UtcDateTime.AddDays(-1), coreId: MatterId);

        await h.RunAsync();

        h.Org.WriteCalls.Should().Be(0, "FR-16: a decision-closed Signal stays Acted");
    }

    [Fact]
    public async Task Run_ASignalAPersonResolvedAfterTheRead_IsNotClosed()
    {
        var h = new Harness().WithPathBPolicy();
        var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId);
        h.Org.ResolveByPersonBeforeConditionalUpdate.Add(id); // a person dismisses it between the job's read and its close

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ResultJson.Should().Contain("\"changedSinceRead\":1").And.Contain("\"closed\":0");
        h.Org.ConditionalUpdates.Should().BeEmpty("the row-version check refused the conditional update");
        h.Org.Updates.Should().BeEmpty("the close is never retried unconditionally");
        h.Org.Rows[id].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value
            .Should().Be(SignalWriter.ResolutionDismissed, "the person's resolution is untouched");
    }

    [Fact]
    public async Task Run_DismissedFifteenDaysAgo_RaisesEpisode2_OnTheSameSubject()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionDismissed, resolvedOn: RunAt.UtcDateTime.AddDays(-15), coreId: MatterId);

        await h.RunAsync();

        h.Org.CreatedPayloads.Should().ContainSingle()
            .Which.GetAttributeValue<string>("sprk_dedupekey").Should().EndWith("|2");
    }

    [Fact]
    public async Task Run_DoLanePolicy_IsEvaluated_AndAnOverdueToDoWithNoCoreRecord_IsOwnedByTheResolversTeam_NeverItsUser()
    {
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ResultJson.Should().Contain("\"doLanePolicies\":1");
        var created = h.Org.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId),
            "task 143 (D-129/D-130): the resolver's team for the To Do, never the To Do's user");
        created.Attributes.Should().NotContainKey("sprk_corerecordtype", "D-35: both core-record columns stay null");
        created.Attributes.Should().NotContainKey("sprk_corerecordid");
        created.GetAttributeValue<DateTime?>("sprk_duedate").Should().Be(new DateTime(2026, 10, 1));
    }

    [Fact]
    public async Task Run_OverdueToDoUnderASecureProject_IsWrittenOwnedByTheSecureRecordOwnersTeam()
    {
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), project: ProjectId);
        h.Ownership.AnswerFor[ProjectId] = Ownership.RecordOwnerResolution.Owned(SecureTeamId) with { IsSecureOwner = true };

        var result = await h.RunAsync();

        result.Success.Should().BeTrue("D-38: a Secure core record is not skipped");
        var created = h.Org.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", SecureTeamId));
        created.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(ProjectId), "D-34: grouped under the project");
        h.Ownership.Contexts.Should().ContainSingle().Which.TargetEntityLogicalName.Should().Be("sprk_project");
    }

    [Fact]
    public async Task Run_HitOnASecureMatter_IsWrittenThroughTheResolverPath_OwnedByTheSecureRecordOwnersTeam()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Ownership.AnswerFor[MatterId] = Ownership.RecordOwnerResolution.Owned(SecureTeamId) with { IsSecureOwner = true };

        await h.RunAsync();

        h.Org.CreatedPayloads.Should().ContainSingle()
            .Which.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", SecureTeamId));
    }

    [Theory]
    [InlineData(100000001)] // Limited
    [InlineData(100000002)] // Restricted
    public async Task Run_HitOnARestrictedOrLimitedMatter_IsWrittenNormally(int accessPermission)
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Sysadmin.Stub(new Entity("sprk_matter", MatterId) { ["sprk_accesspermission"] = new OptionSetValue(accessPermission) });

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.CreatedPayloads.Should().ContainSingle("D-33 withdrew D-15's skip: Restricted/Limited exclude only external contacts")
            .Which.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", DefaultTeamId),
                "task 143 (D-130): the matter's business-unit team, an ordinary answer");
    }

    [Fact]
    public async Task Run_ResolverRefusal_WritesNothing_IsCountedRefused_AndTheRunFails()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Ownership.AnswerFor[MatterId] = Ownership.RecordOwnerResolution.Refused(Ownership.RecordOwnerRefusal.NoOwnerSource, "test refusal");

        using var metrics = new MetricCapture();
        var result = await h.RunAsync();

        h.Org.CreateCalls.Should().Be(0);
        result.Success.Should().BeFalse("a refusal is never a successful run");
        result.ResultJson.Should().Contain("\"refused\":1");
        metrics.Counter("ontology.writer.failures", "reason", OntologyWriterFailureReason.OwnerRefused).Should().BeGreaterThanOrEqualTo(1);
        metrics.Counter("ontology.evaluator.signals", "action", EvaluatorSignalAction.Refused).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Run_WriterCredentialUnresolvable_RecordsAFailedRun_WithTheStableEventId_AndTheFailureMetric()
    {
        var h = new Harness(writerClientId: "").WithPathBPolicy().WithMatterHit(MatterId);

        using var metrics = new MetricCapture();
        var result = await h.RunAsync();

        result.Success.Should().BeFalse("never recorded as a successful run with zero Signals");
        result.ResultJson.Should().Contain("\"outcome\":\"failed\"").And.Contain("\"written\":0");
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.EventId.Id == OntologyWriterEvents.WriteRefused.Id);
        metrics.Counter("ontology.writer.failures", "reason", OntologyWriterFailureReason.CredentialUnresolvable).Should().BeGreaterThanOrEqualTo(1);
        metrics.Counter("ontology.evaluator.runs", "outcome", "failed").Should().BeGreaterThanOrEqualTo(1);
        (await h.Cache.GetStringAsync(SignalReevaluationJob.LastSuccessCacheKey)).Should().BeNull("a failed run is not a success");
    }

    [Fact]
    public async Task Run_DisabledPolicy_IsNotEvaluatedAtAll()
    {
        var h = new Harness().WithPathBPolicy(enabled: false).WithMatterHit(MatterId);

        var result = await h.RunAsync();

        result.ResultJson.Should().Contain("\"policiesEvaluated\":0");
        h.Sysadmin.Fetches.Should().BeEmpty("no predicate of a disabled policy is run");
        h.Org.WriteCalls.Should().Be(0);
    }

    [Fact]
    public async Task Run_OffThenOn_ReRaisesTheSubjectsWhoseLatestEpisodeClosedPolicyRetired()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 3), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionPolicyRetired, resolvedOn: RunAt.UtcDateTime.AddHours(-2), coreId: MatterId);

        await h.RunAsync();

        h.Org.CreatedPayloads.Should().ContainSingle()
            .Which.GetAttributeValue<string>("sprk_dedupekey").Should().EndWith("|4", "D-32: a new episode");
    }

    [Fact]
    public async Task Run_PolicyWithAKeyThatIsNotEpisodeScoped_WritesNothingForIt_AndTheRunFails()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Org.SeedSignal($"{PolicyCode}|sprk_matter|{Lower(MatterId)}", SignalWriter.SignalStatusOpen, coreId: MatterId);

        using var metrics = new MetricCapture();
        var result = await h.RunAsync();

        result.Success.Should().BeFalse();
        h.Org.WriteCalls.Should().Be(0, "a duplicate episode 1 would otherwise be raised beside the unreadable key");
        // Task 145 (uac-r2 re-check F1): once each, though the evidence sweep reads the policy's Signals again.
        metrics.Counter("ontology.writer.failures", "reason", OntologyWriterFailureReason.DedupeKeyUnparseable).Should().Be(1);
        result.ResultJson.Should().Contain("\"unparseableKeys\":1").And.Contain("\"policyFailures\":1");
        h.Log.Count(e => e.Message.Contains("has a dedupe key that is not")).Should().Be(1);
    }

    [Fact]
    public async Task Run_SuccessfulRun_RecordsTheLastSuccess_ForTheHealthCheck()
    {
        var h = new Harness().WithPathBPolicy();

        (await h.RunAsync()).Success.Should().BeTrue();

        (await h.Cache.GetStringAsync(SignalReevaluationJob.LastSuccessCacheKey)).Should().Be(RunAt.ToString("o"));
    }

    [Fact]
    public async Task Run_PathBHit_WritesOneWitnessLinePerExistsClause_AndNoWitnessValueInTheSentence()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);

        await h.RunAsync();

        var created = h.Org.CreatedPayloads.Single();
        var evidence = System.Text.Json.JsonDocument.Parse(created.GetAttributeValue<string>("sprk_evidencerefs")!).RootElement;
        evidence.GetArrayLength().Should().Be(1, "one exists clause; a notExists clause has no witness");
        evidence[0].GetProperty("clause").GetString().Should().Be("all[0]");
        evidence[0].GetProperty("kind").GetString().Should().Be("communication");
        evidence[0].GetProperty("ref").GetString().Should().Be($"sprk_communication:{Lower(CommunicationId)}");
        evidence[0].GetProperty("text").GetString().Should().Be("Rate increase from Firm LLP");
        evidence[0].GetProperty("source").GetString().Should().Be("partner@firm.example");
        evidence[0].GetProperty("asOf").GetString().Should().Be("2026-10-05T09:15:00Z");
        var sentence = created.GetAttributeValue<string>("sprk_sentence");
        sentence.Should().Be("A 100000001 communication arrived with no budget revision in 30 days.");
        sentence.Should().NotContain("Rate increase").And.NotContain("partner@firm.example");
    }

    [Fact]
    public async Task Run_T145_AWitnessAlsoFiledUnderASecureProject_IsWrittenWithoutItsSubjectOrSender()
    {
        // D-123 / uac-r2 #1747 finding (e), through the job's real write path: the communication is filed to ordinary matter M (the
        // hit) and to secure project X (another filing), so M's Signal, read by M's business unit, carries no subject or sender of it.
        // Run_PathBHit_WritesOneWitnessLinePerExistsClause_AndNoWitnessValueInTheSentence is the ordinary control (detail kept).
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001a5");
        OrdinaryMatterBesideSecureX(h, secureProject);
        RefileTheWitnessUnder(h, secureProject);

        await h.RunAsync();

        var created = h.Org.CreatedPayloads.Single();
        var evidence = System.Text.Json.JsonDocument.Parse(created.GetAttributeValue<string>("sprk_evidencerefs")!).RootElement;
        evidence.GetArrayLength().Should().Be(1, "the witness line is still written");
        evidence[0].TryGetProperty("text", out _).Should().BeFalse("the communication's subject stays behind the secure wall");
        evidence[0].TryGetProperty("source", out _).Should().BeFalse("and so does its sender");
        evidence[0].GetProperty("ref").GetString().Should().Be($"sprk_communication:{Lower(CommunicationId)}");
        evidence[0].GetProperty("asOf").GetString().Should().Be("2026-10-05T09:15:00Z");
        evidence[0].GetProperty("clause").GetString().Should().Be("all[0]");
        created.GetAttributeValue<string>("sprk_evidencerefs").Should().NotContain("Rate increase").And.NotContain("partner@firm.example");
    }

    [Fact]
    public async Task Run_T145_AReconcileOfAWitnessAlsoFiledUnderASecureProject_AttemptsNoSubjectOrSender()
    {
        // Review F3: a reconcile also goes create-first (SignalWriter.WriteAsync), so the payload it attempts (and writes if the row is
        // gone) is filtered exactly as a create's is.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        // A different due date is what makes the planner reconcile (an identical one is "unchanged" and writes nothing).
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId, due: new DateTime(2020, 1, 1));
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001a5");
        OrdinaryMatterBesideSecureX(h, secureProject);
        RefileTheWitnessUnder(h, secureProject);
        var attempted = new List<Entity>();
        h.Org.CreateFaultFor = e => { attempted.Add(e); return null; };

        var run = await h.RunAsync();

        run.ResultJson.Should().Contain("\"updated\":1", "the row exists: this pass is a reconcile");
        h.Org.CreatedPayloads.Should().BeEmpty("the row exists: this pass is a reconcile");
        var evidence = attempted.Should().ContainSingle().Subject.GetAttributeValue<string>("sprk_evidencerefs");
        evidence.Should().Contain($"sprk_communication:{Lower(CommunicationId)}")
            .And.NotContain("Rate increase").And.NotContain("partner@firm.example");
    }

    // ── Task 145 review F1 (main session 2026-10-14, option A): the redaction pass over STORED evidence ─────────────────────────────

    /// <summary>A matter Signal created while its witness communication was ordinary (so its evidence carries the subject and sender),
    /// with secure project X known to the environment. Returns the Signal row.</summary>
    private static async Task<Entity> SignalCreatedWithAnOrdinaryWitness(Harness h, Guid secureProject)
    {
        OrdinaryMatterBesideSecureX(h, secureProject);
        await h.RunAsync();
        var signal = h.Org.Rows.Values.Single();
        signal.GetAttributeValue<string>("sprk_evidencerefs").Should().Contain("Rate increase", "the precondition: created with the detail");
        h.Org.ResetCounts();
        return signal;
    }

    /// <summary>An environment with a Secure Record owner team and secure project X, in which matter M, the witness communication and the
    /// matter Signal are ORDINARY: M is a readable, non-secure root owned by its business-unit team, as is the witness. Without M as a
    /// readable root the walk cannot name M's family, and every line would be cleared as undetermined, proving nothing.</summary>
    private static void OrdinaryMatterBesideSecureX(Harness h, Guid secureProject)
    {
        h.Sysadmin.SecureProject(secureProject);
        h.Sysadmin.Stub(new Entity("sprk_matter", MatterId) { ["owningteam"] = new EntityReference("team", DefaultTeamId), ["sprk_issecure"] = false });
        h.Sysadmin.SubjectRows["sprk_matter"].Single()["owningteam"] = new EntityReference("team", DefaultTeamId);
        h.Sysadmin.WitnessRows["sprk_communication"].Single()["owningteam"] = new EntityReference("team", DefaultTeamId);
    }

    private static void RefileTheWitnessUnder(Harness h, Guid secureProject)
    {
        var witness = h.Sysadmin.WitnessRows["sprk_communication"].Single();
        witness["sprk_regardingproject"] = new EntityReference("sprk_project", secureProject);
        witness["owningteam"] = new EntityReference("team", h.Sysadmin.SecureTeamId!.Value);
    }

    [Fact]
    public async Task Run_T145F1_AWitnessRefiledIntoSecureXAfterTheSignalWasCreated_LosesItsDetailOnTheNextPass_Once()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001a6");
        var signal = await SignalCreatedWithAnOrdinaryWitness(h, secureProject);
        RefileTheWitnessUnder(h, secureProject);

        var second = await h.RunAsync();

        var update = h.Org.ConditionalUpdates.Should().ContainSingle("one conditional write, on the version read").Subject;
        update.Id.Should().Be(signal.Id);
        update.Fields.Keys.Should().BeEquivalentTo(new[] { "sprk_evidencerefs" }, "the redaction writes nothing else");
        var stored = System.Text.Json.JsonDocument.Parse((string)update.Fields["sprk_evidencerefs"]).RootElement;
        stored.GetArrayLength().Should().Be(1);
        stored[0].TryGetProperty("text", out _).Should().BeFalse("the subject is now behind secure X's wall");
        stored[0].TryGetProperty("source", out _).Should().BeFalse("and so is the sender");
        stored[0].GetProperty("ref").GetString().Should().Be($"sprk_communication:{Lower(CommunicationId)}");
        stored[0].GetProperty("asOf").GetString().Should().Be("2026-10-05T09:15:00Z");
        stored[0].GetProperty("clause").GetString().Should().Be("all[0]");
        second.ResultJson.Should().Contain("\"evidenceRedacted\":1");
        second.Success.Should().BeTrue();

        h.Org.ResetCounts();
        var third = await h.RunAsync();
        h.Org.ConditionalUpdates.Should().BeEmpty("monotonic: nothing left to clear, and nothing is ever restored");
        third.ResultJson.Should().Contain("\"evidenceRedacted\":0");
    }

    [Fact]
    public async Task Run_T145F1_ASignalChangedSinceTheRead_IsNotOverwritten_AndOnceResolvedIsStillRedactedNextPass()
    {
        // The redaction is conditional on the version read: a person resolving the Signal between the read and the write wins, and the
        // next pass (which reads the now RESOLVED Signal) clears the detail on the new version.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001a9");
        var signal = await SignalCreatedWithAnOrdinaryWitness(h, secureProject);
        RefileTheWitnessUnder(h, secureProject);
        h.Org.ResolveByPersonBeforeConditionalUpdate.Add(signal.Id);

        var raced = await h.RunAsync();

        raced.Success.Should().BeTrue();
        raced.ResultJson.Should().Contain("\"changedSinceRead\":1").And.Contain("\"evidenceRedacted\":0");
        h.Org.ConditionalUpdates.Should().BeEmpty("the row-version check refused the redaction");
        h.Org.Updates.Should().BeEmpty("never retried unconditionally");
        h.Org.Rows[signal.Id].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value
            .Should().Be(SignalWriter.ResolutionDismissed, "the person's resolution is untouched");

        h.Org.ResolveByPersonBeforeConditionalUpdate.Clear();
        h.Org.ResetCounts();
        var next = await h.RunAsync();

        next.ResultJson.Should().Contain("\"evidenceRedacted\":1", "a resolved Signal's stored evidence is redacted too");
        h.Org.ConditionalUpdates.Should().ContainSingle().Which.Fields["sprk_evidencerefs"].As<string>().Should().NotContain("Rate increase");
    }

    [Fact]
    public async Task Run_T145F1_ASignalOfAPolicySwitchedOffLater_IsStillRedacted_ThoughThePolicyIsNotEvaluated()
    {
        // An Off or Retired policy is never evaluated again, so its Signals reach the redaction only through the not-in-force sweep.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001aa");
        var signal = await SignalCreatedWithAnOrdinaryWitness(h, secureProject);
        h.Sysadmin.SwitchOff(PolicyCode);
        RefileTheWitnessUnder(h, secureProject);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ResultJson.Should().Contain("\"policiesEvaluated\":0").And.Contain("\"policyRetiredClosed\":1").And.Contain("\"evidenceRedacted\":1");
        var row = h.Org.Rows[signal.Id];
        row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionPolicyRetired);
        row.GetAttributeValue<string>("sprk_evidencerefs").Should().NotContain("Rate increase").And.NotContain("partner@firm.example")
            .And.Contain($"sprk_communication:{Lower(CommunicationId)}");
    }

    // ── uac-r2 F1 on #1874 / pass 2 F2: an in-force policy that stops before its redaction is still swept ──────────────────────────

    /// <summary>The stored evidence a create wrote while the witness was ordinary: one line quoting the witness's subject and sender.</summary>
    private static string StoredEvidenceQuotingTheWitness() =>
        $"[{{\"kind\":\"communication\",\"ref\":\"sprk_communication:{Lower(CommunicationId)}\",\"tier\":\"Fact\","
        + "\"text\":\"Rate increase from Firm LLP\",\"source\":\"partner@firm.example\",\"asOf\":\"2026-10-05T09:15:00Z\",\"clause\":\"all[0]\"}]";

    /// <summary>An ordinary matter Signal of the policy already stored with the witness's detail, the witness then re-filed under secure X.</summary>
    private static Guid OrdinarySignalWhoseWitnessMovedIntoSecureX(Harness h, Guid secureProject)
    {
        OrdinaryMatterBesideSecureX(h, secureProject);
        var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: h.Sysadmin.PolicyIdOf(PolicyCode), versionId: h.Sysadmin.CurrentVersionOf(PolicyCode));
        h.Org.Rows[id]["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId);
        h.Org.Rows[id]["sprk_matter"] = new EntityReference("sprk_matter", MatterId);
        h.Org.Rows[id]["sprk_evidencerefs"] = StoredEvidenceQuotingTheWitness();
        RefileTheWitnessUnder(h, secureProject);
        return id;
    }

    private static void ShouldBeRedactedOnceBySweep(Harness h, JobRunResult result, Guid signalId)
    {
        var redaction = h.Org.ConditionalUpdates.Where(u => u.Fields.ContainsKey("sprk_evidencerefs")).Should()
            .ContainSingle("the sweep redacts the policy's Signals once, though the policy stopped before its own redaction").Subject;
        redaction.Id.Should().Be(signalId);
        redaction.Fields.Keys.Should().BeEquivalentTo(new[] { "sprk_evidencerefs" });
        ((string)redaction.Fields["sprk_evidencerefs"]).Should().NotContain("Rate increase").And.NotContain("partner@firm.example")
            .And.Contain($"sprk_communication:{Lower(CommunicationId)}");
        result.ResultJson.Should().Contain("\"evidenceRedacted\":1");
        result.Success.Should().BeFalse("the policy's own failure is still reported");
    }

    [Fact]
    public async Task Run_T145Sweep_AnInvalidRule_StillHasItsStoredEvidenceRedacted()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var id = OrdinarySignalWhoseWitnessMovedIntoSecureX(h, Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001b1"));
        h.Sysadmin.BreakCurrentRuleBody(PolicyCode);

        var result = await h.RunAsync();

        result.ResultJson.Should().Contain("\"invalidPolicies\":1", "the precondition: the policy stopped at the validator gate");
        ShouldBeRedactedOnceBySweep(h, result, id);
    }

    [Fact]
    public async Task Run_T145Sweep_ACurrentVersionNoLongerInForce_StillHasItsStoredEvidenceRedacted()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var id = OrdinarySignalWhoseWitnessMovedIntoSecureX(h, Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001b2"));
        h.Sysadmin.SetCurrentVersionInForceTo(PolicyCode, RunAt.UtcDateTime.AddDays(-1));

        var result = await h.RunAsync();

        h.Log.Should().Contain(e => e.EventId.Id == OntologyWriterEvents.PolicyVersionNotInForce.Id, "the precondition: that early return");
        result.ResultJson.Should().Contain("\"policyFailures\":1", "only the version's own failure: the sweep did not fail");
        ShouldBeRedactedOnceBySweep(h, result, id);
    }

    [Fact]
    public async Task Run_T145Sweep_NoCurrentVersion_Thrown_StillHasItsStoredEvidenceRedacted()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var id = OrdinarySignalWhoseWitnessMovedIntoSecureX(h, Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001b3"));
        h.Sysadmin.ClearCurrentVersion(PolicyCode);

        var result = await h.RunAsync();

        h.Log.Should().Contain(e => e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id && e.Exception != null && e.Exception.Message.Contains("no current version"),
            "the precondition: the policy threw before evaluating");
        ShouldBeRedactedOnceBySweep(h, result, id);
    }

    [Fact]
    public async Task Run_T145Sweep_APartitionSkipInTheSuppressionPass_StillHasItsStoredEvidenceRedacted()
    {
        // #1661's skip: the suppression pass cannot place the policy's Signals (one timeout), so nothing is planned for the policy. The
        // sweep after the loop reads the families again (the timeout has passed) and redacts. otherOpen is an ordinary Signal quoting a
        // communication filed under the SECURE matter.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var (policyId, _, otherOpen) = SecureStampedMatterWithAnOpenNonHit(h);
        h.Org.Rows[otherOpen]["sprk_evidencerefs"] = StoredEvidenceQuotingTheWitness();
        var faults = 0;
        h.Sysadmin.FaultOn = q => q.EntityName == "businessunit" && faults++ == 0 ? new TimeoutException("Dataverse timed out.") : null;

        var result = await h.RunAsync();

        var json = System.Text.Json.JsonDocument.Parse(result.ResultJson!).RootElement;
        json.GetProperty("partitionSkippedPolicies").EnumerateArray().Should().ContainSingle("the precondition: the policy was skipped")
            .Which.GetProperty("policyId").GetGuid().Should().Be(policyId);
        ShouldBeRedactedOnceBySweep(h, result, otherOpen);
    }

    [Fact]
    public async Task Run_T145Sweep_AFaultPlacingTheHits_StillHasItsStoredEvidenceRedacted()
    {
        // Pass 2 F2: hitPartitions is null (the hits' placement timed out once, after the suppression pass completed).
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var (policyId, _, otherOpen) = SecureStampedMatterWithAnOpenNonHit(h);
        h.Org.Rows[otherOpen]["sprk_evidencerefs"] = StoredEvidenceQuotingTheWitness();
        var reconciled = false;
        var faults = 0;
        h.Sysadmin.FaultOn = q =>
        {
            if (q.EntityName == "sprk_decisionrecord" && q.ColumnSet.Columns.Contains("sprk_recordclass")) reconciled = true;
            return reconciled && q.EntityName == "businessunit" && faults++ == 0 ? new TimeoutException("Dataverse timed out.") : null;
        };

        var result = await h.RunAsync();

        faults.Should().BeGreaterThan(0, "the precondition: the hits' placement faulted");
        System.Text.Json.JsonDocument.Parse(result.ResultJson!).RootElement.GetProperty("partitionSkippedPolicies").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("policyId").GetGuid().Should().Be(policyId);
        ShouldBeRedactedOnceBySweep(h, result, otherOpen);
    }

    [Fact]
    public async Task Run_T145Sweep_ADismissalFactsReadFailure_StillHasItsStoredEvidenceRedacted()
    {
        // Pass 2 F2: PartitionsUnavailable from the suppression pass's count read (the Decision Records' facts), not a family read.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var (policyId, _, otherOpen) = SecureStampedMatterWithAnOpenNonHit(h);
        h.Org.Rows[otherOpen]["sprk_evidencerefs"] = StoredEvidenceQuotingTheWitness();
        var faults = 0;
        h.Sysadmin.FaultOn = q => q.EntityName == "sprk_decisionrecord" && q.ColumnSet.Columns.Contains("sprk_recordclass") && faults++ == 0
            ? new TimeoutException("Dataverse timed out.")
            : null;

        var result = await h.RunAsync();

        faults.Should().BeGreaterThan(0, "the precondition: the facts read faulted");
        System.Text.Json.JsonDocument.Parse(result.ResultJson!).RootElement.GetProperty("partitionSkippedPolicies").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("policyId").GetGuid().Should().Be(policyId);
        ShouldBeRedactedOnceBySweep(h, result, otherOpen);
    }

    [Fact]
    public async Task Run_T145Sweep_APolicyThatReachedItsOwnRedaction_IsNotSweptAgain()
    {
        // Never twice per policy per run: the completed pass redacts; the sweep skips the policy (one write, one count).
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var id = OrdinarySignalWhoseWitnessMovedIntoSecureX(h, Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001b4"));
        h.Org.Rows[id]["sprk_duedate"] = null!;

        var policySignalReads = 0;
        h.Sysadmin.FaultOn = q =>
        {
            if (q.EntityName == "sprk_signal" && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_dedupekey")) policySignalReads++;
            return null;
        };

        var result = await h.RunAsync();

        result.ResultJson.Should().Contain("\"policyFailures\":0").And.Contain("\"evidenceRedacted\":1");
        h.Org.ConditionalUpdates.Where(u => u.Fields.ContainsKey("sprk_evidencerefs")).Should().ContainSingle();
        // uac-r2 re-check F3: the write count alone cannot see a second pass (the redaction is monotonic and the fake's write
        // updates the row), so the policy's Signals must be read exactly once: by its own pass, never again by the sweep.
        policySignalReads.Should().Be(1, "the sweep skips a policy that already reached its own redaction");
    }

    [Fact]
    public async Task Run_T145Sweep_APolicyWithAnUnparseableKey_CountsItOnce_AndStillRedactsItsParseableSignals()
    {
        // uac-r2 re-check F1: the policy's pass fails on the bad key (counted, logged and metered once). The sweep then redacts
        // the Signals it can parse, without counting the bad key again.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var id = OrdinarySignalWhoseWitnessMovedIntoSecureX(h, Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001b5"));
        h.Org.SeedSignal($"{PolicyCode}|sprk_matter|{Lower(MatterId)}", SignalWriter.SignalStatusOpen, coreId: MatterId);

        using var metrics = new MetricCapture();
        var result = await h.RunAsync();

        result.ResultJson.Should().Contain("\"unparseableKeys\":1").And.Contain("\"policyFailures\":1");
        metrics.Counter("ontology.writer.failures", "reason", OntologyWriterFailureReason.DedupeKeyUnparseable).Should().Be(1);
        h.Log.Count(e => e.Message.Contains("has a dedupe key that is not")).Should().Be(1);
        ShouldBeRedactedOnceBySweep(h, result, id);
    }

    [Fact]
    public async Task Run_T145Sweep_ASweepReadFailureForAPolicyThatAlreadyFailed_IsNotCountedTwice()
    {
        // uac-r2 re-check F1: the policy's own read of its Signals faults, then the sweep's re-read faults too. One policy failure.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        OrdinarySignalWhoseWitnessMovedIntoSecureX(h, Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001b6"));
        var reads = 0;
        h.Sysadmin.FaultOn = q => q.EntityName == "sprk_signal" && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_dedupekey") && ++reads > 0
            ? new TimeoutException("Dataverse timed out.")
            : null;

        var result = await h.RunAsync();

        reads.Should().Be(2, "the precondition: the policy's own read and the sweep's re-read both faulted");
        result.Success.Should().BeFalse();
        result.ResultJson.Should().Contain("\"policyFailures\":1");
        h.Org.ConditionalUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_T145F1_AWitnessThatStaysInTheSignalsFamily_KeepsItsDetail()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var signal = await SignalCreatedWithAnOrdinaryWitness(h, Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001a7"));

        var second = await h.RunAsync();

        h.Org.ConditionalUpdates.Should().BeEmpty("an ordinary witness on an ordinary Signal is the same family");
        h.Org.Rows[signal.Id].GetAttributeValue<string>("sprk_evidencerefs").Should().Contain("Rate increase").And.Contain("partner@firm.example");
        second.ResultJson.Should().Contain("\"evidenceRedacted\":0");
    }

    [Fact]
    public async Task Run_T145F1_AFamilyReadFault_LeavesTheStoredLinesUntouchedThatPass_AndTheNextPassClearsThem()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001a8");
        var signal = await SignalCreatedWithAnOrdinaryWitness(h, secureProject);
        RefileTheWitnessUnder(h, secureProject);
        h.Sysadmin.FailWitnessReads = true;

        var faulted = await h.RunAsync();

        h.Org.ConditionalUpdates.Should().BeEmpty("a fault neither strips detail nor adds any: nothing is written that pass");
        h.Org.Rows[signal.Id].GetAttributeValue<string>("sprk_evidencerefs").Should().Contain("Rate increase");
        faulted.ResultJson.Should().Contain("\"policyFailures\":1", "the fault is reported, never silent");
        h.Log.Should().Contain(e => e.EventId.Id == SuppressionEvents.Failed.Id, "the stored-evidence family read is what faulted");

        h.Sysadmin.FailWitnessReads = false;
        h.Org.ResetCounts();
        await h.RunAsync();
        h.Org.ConditionalUpdates.Should().ContainSingle("the next pass retries").Which.Fields["sprk_evidencerefs"].As<string>()
            .Should().NotContain("Rate increase");
    }

    [Fact]
    public async Task Run_NoCoreToDoWhoseOwnerAnswerChanged_ReownsItsSignalToTheNewTeam_ThroughTheReconciler_Once()
    {
        // Task 143: the item's answer moves to another ordinary team (its user moved unit, or D-134's assignee changed). The job
        // re-owns the Signal through uac-r2's SecureChildReconciler.ReownOrdinaryRowAsync (the real one, over this World), as a
        // conditional write on the version it read.
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));
        await h.RunAsync();
        h.Ownership.AnswerFor[TodoId] = Ownership.RecordOwnerResolution.Owned(OtherTeamId);
        h.Org.ResetCounts();

        var second = await h.RunAsync();
        h.Org.ResetCounts();
        var third = await h.RunAsync();

        second.ResultJson.Should().Contain("\"reassigned\":1");
        var row = h.Org.Rows.Values.Single();
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", OtherTeamId));
        h.ReownApi.ConditionalWrites.Should().ContainSingle().Which.ETag.Should().Be("W/\"1\"", "conditional on the version the guard read");
        third.Success.Should().BeTrue();
        h.Org.WriteCalls.Should().Be(0, "logged once: the next pass sees no drift");
        h.Log.Count(e => e.EventId.Id == OntologyWriterEvents.NoCoreSignalReassigned.Id).Should().Be(1);
    }

    [Fact]
    public async Task Run_NoCoreReown_ReconcilerFails_IsLoggedMeteredAndCountedRefused_NothingWritten()
    {
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));
        await h.RunAsync();
        h.Sysadmin.TeamBusinessUnits.Remove(OtherTeamId); // the target team cannot be read: the reconciler answers Failed
        h.Ownership.AnswerFor[TodoId] = Ownership.RecordOwnerResolution.Owned(OtherTeamId);
        h.Org.ResetCounts();

        var second = await h.RunAsync();

        second.Success.Should().BeFalse();
        second.ResultJson.Should().Contain("\"reassigned\":0").And.Contain("\"refused\":1");
        h.ReownApi.ConditionalWrites.Should().BeEmpty();
        h.Log.Should().Contain(e => e.EventId.Id == OntologyWriterEvents.WriteRefused.Id && e.Level == LogLevel.Error
            && e.Message.Contains(OntologyWriterFailureReason.SignalReassignFailed));
    }

    [Fact]
    public async Task Run_NoCoreReown_RowChangedAfterTheGuardRead_IsAConflict_CountedChangedSinceRead_NothingWritten()
    {
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));
        await h.RunAsync();
        h.ReownApi.ChangeTheRowBeforeTheWrite = true;
        h.Ownership.AnswerFor[TodoId] = Ownership.RecordOwnerResolution.Owned(OtherTeamId);
        h.Org.ResetCounts();

        var second = await h.RunAsync();

        second.ResultJson.Should().Contain("\"reassigned\":0").And.Contain("\"changedSinceRead\":1");
        h.Org.Rows.Values.Single().GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(DefaultTeamId, "412: nothing written");
    }

    [Fact]
    public async Task Run_NoCoreReown_ConflictWhileItsDateAlsoMoved_IsCountedOnce_TheReconcileWaitsForTheNextPass()
    {
        // Review F2: the To Do was reassigned AND re-dated in one day, so the plan holds a re-own and a reconcile for the same Signal.
        // The re-own meets a 412 (counted changedSinceRead); the reconcile must not then meet the old owner and refuse it again.
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));
        await h.RunAsync();
        h.WithTodo(TodoId, due: new DateTime(2026, 10, 3), owner: new EntityReference("systemuser", UserId));
        h.ReownApi.ChangeTheRowBeforeTheWrite = true;
        h.Ownership.AnswerFor[TodoId] = Ownership.RecordOwnerResolution.Owned(OtherTeamId);
        h.Org.ResetCounts();

        var second = await h.RunAsync();

        second.Success.Should().BeTrue("a 412 race is not a failure");
        second.ResultJson.Should().Contain("\"changedSinceRead\":1").And.Contain("\"refused\":0").And.Contain("\"updated\":0");
        h.Log.Should().NotContain(e => e.Level >= LogLevel.Warning && e.Message.Contains(OntologyWriterFailureReason.CoreRecordChanged));
        h.Log.Should().Contain(e => e.Message.Contains("was not reconciled because its re-own did not land"));
    }

    [Fact]
    public async Task Run_NoCoreReownThatLands_WhileItsDateAlsoMoved_StillReconcilesTheDate_InTheSamePass()
    {
        // Re-check K3 (positive control): only a re-own that did NOT land holds the reconcile back.
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));
        await h.RunAsync();
        h.WithTodo(TodoId, due: new DateTime(2026, 10, 3), owner: new EntityReference("systemuser", UserId));
        h.Ownership.AnswerFor[TodoId] = Ownership.RecordOwnerResolution.Owned(OtherTeamId);
        h.Org.ResetCounts();

        var second = await h.RunAsync();

        second.Success.Should().BeTrue();
        second.ResultJson.Should().Contain("\"reassigned\":1").And.Contain("\"updated\":1").And.Contain("\"refused\":0");
        var row = h.Org.Rows.Values.Single();
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(OtherTeamId);
        row.GetAttributeValue<DateTime?>("sprk_duedate").Should().Be(new DateTime(2026, 10, 3));
    }

    [Fact]
    public async Task Run_NoCoreReownRefused_WhileItsDateAlsoMoved_IsCountedRefusedOnce_NoCoreRecordChangedReport()
    {
        // Re-check K3: a legacy user-owned Signal (refused by the reconciler) whose date also moved is one refusal, not two.
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));
        await h.RunAsync();
        var signal = h.Org.Rows.Values.Single();
        signal["ownerid"] = new EntityReference("systemuser", UserId);
        signal.Attributes.Remove("owningteam");
        h.WithTodo(TodoId, due: new DateTime(2026, 10, 3), owner: new EntityReference("systemuser", UserId));
        h.Ownership.AnswerFor[TodoId] = Ownership.RecordOwnerResolution.Owned(OtherTeamId);
        h.Org.ResetCounts();

        var second = await h.RunAsync();

        second.ResultJson.Should().Contain("\"refused\":1").And.Contain("\"updated\":0");
        h.Log.Should().NotContain(e => e.Level >= LogLevel.Warning && e.Message.Contains(OntologyWriterFailureReason.CoreRecordChanged));
    }

    [Fact]
    public async Task Run_NoCoreReown_DataverseRefusesTheTeamsRead_IsMeteredAsAnAccessDenial()
    {
        // Review F1 + K1 (re-own): in production the shared Web API client throws a bare HttpRequestException carrying only the
        // status for a 403 (it maps 412 and 404 only), so the denial is read from the status, not from any Dataverse code in the text.
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));
        await h.RunAsync();
        h.ReownApi.WriteFault = new HttpRequestException(
            "Response status code does not indicate success: 403 (Forbidden).", null, System.Net.HttpStatusCode.Forbidden);
        h.Ownership.AnswerFor[TodoId] = Ownership.RecordOwnerResolution.Owned(OtherTeamId);
        h.Org.ResetCounts();

        using var metrics = new MetricCapture();
        var second = await h.RunAsync();

        second.Success.Should().BeFalse();
        second.ResultJson.Should().Contain("\"reassigned\":0").And.Contain("\"refused\":1").And.Contain("\"accessDenied\":1");
        h.Org.Rows.Values.Single().GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(DefaultTeamId, "nothing was re-owned");
        h.Log.Should().Contain(e => e.EventId.Id == OntologyWriterEvents.WriteRefused.Id && e.Level == LogLevel.Error
            && e.Message.Contains(OntologyWriterFailureReason.DataverseAccessDenied) && e.Message.Contains("403 (Forbidden)")
            && e.Message.Contains(OtherTeamId.ToString()));
        h.Log.Should().NotContain(e => e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id && e.Level == LogLevel.Error,
            "a counted privilege refusal adds a Warning, not a second Error");
        metrics.Counter("ontology.writer.failures", "reason", OntologyWriterFailureReason.DataverseAccessDenied).Should().BeGreaterThanOrEqualTo(1);
    }

    [Theory]
    [InlineData(false)] // the resolver refuses the To Do's owner
    [InlineData(true)]  // the resolver faults while answering for it
    public async Task Run_NoCoreItemWhoseOwnerCannotBeResolved_IsLoggedAsAnOwnerFailure_WithItsCause_NotAsACoreRecordFailure(bool fault)
    {
        // Review F1: the job meets a no-core item's owner refusal first, in DeriveCoreRecordAsync while it plans. The Error log
        // says the OWNER cannot be resolved and carries the writer's escalation (the refusal code, or the inner fault).
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));
        if (fault) h.Ownership.FaultFor[TodoId] = new TimeoutException("the resolver's read timed out");
        else h.Ownership.AnswerFor[TodoId] = Ownership.RecordOwnerResolution.Refused(Ownership.RecordOwnerRefusal.NoOwnerSource, "no default team");

        var result = await h.RunAsync();

        result.Success.Should().BeFalse();
        h.Org.CreatedPayloads.Should().BeEmpty();
        var reason = fault ? OntologyWriterFailureReason.NoCoreOwnerUnresolved : OntologyWriterFailureReason.OwnerRefused;
        var entry = h.Log.Should().ContainSingle(e => e.EventId.Id == OntologyWriterEvents.WriteRefused.Id && e.Level == LogLevel.Error).Subject;
        entry.Message.Should().Contain($"reason={reason}").And.Contain("the owner of its Signal cannot be resolved").And.NotContain("core record of");
        var cause = entry.Exception.Should().BeOfType<SignalWriterEscalationException>().Subject;
        if (fault) cause.InnerException.Should().BeOfType<TimeoutException>();
        else cause.Message.Should().Contain(Ownership.RecordOwnerRefusal.NoOwnerSource);
    }

    [Theory]
    [InlineData("secure-target")]  // the new team is in the Secure Record business unit: only the secure pass moves a row there
    [InlineData("user-owned-row")] // a Signal written before task 143, owned by a user: the reconciler never re-owns it
    public async Task Run_NoCoreReownTheReconcilerRefuses_WritesNothing_IsCountedRefused(string shape)
    {
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));
        await h.RunAsync();
        var signal = h.Org.Rows.Values.Single();
        if (shape == "secure-target")
        {
            h.Sysadmin.SecureMatter(OtherMatterId);
            h.Sysadmin.TeamBusinessUnits[OtherTeamId] = h.Sysadmin.SecureBusinessUnit;
        }
        else
        {
            signal["ownerid"] = new EntityReference("systemuser", UserId);
            signal.Attributes.Remove("owningteam");
        }

        h.Ownership.AnswerFor[TodoId] = Ownership.RecordOwnerResolution.Owned(OtherTeamId);
        h.Org.ResetCounts();

        var second = await h.RunAsync();

        second.ResultJson.Should().Contain("\"reassigned\":0").And.Contain("\"refused\":1");
        h.ReownApi.ConditionalWrites.Should().BeEmpty("a refusal writes nothing");
        signal.GetAttributeValue<EntityReference>("ownerid").Id.Should().NotBe(OtherTeamId);
        h.Log.Should().Contain(e => e.EventId.Id == OntologyWriterEvents.WriteRefused.Id && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Run_SubjectRefiledToAnotherCoreRecord_ClosesTheOldEpisode_AndOpensOneUnderTheNewCore()
    {
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), project: ProjectId);
        await h.RunAsync();
        h.Sysadmin.Stub(new Entity("sprk_todo", TodoId) { ["sprk_regardingproject"] = null!, ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) });
        h.Org.ResetCounts();

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.ConditionalUpdates.Should().ContainSingle().Which.Fields["sprk_resolutiontype"]
            .Should().BeEquivalentTo(new OptionSetValue(SignalWriter.ResolutionConditionCleared));
        var created = h.Org.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<string>("sprk_dedupekey").Should().EndWith("|2");
        created.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(MatterId));
    }

    private static Harness RefiledToDoHarness()
    {
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), project: ProjectId);
        h.RunAsync().GetAwaiter().GetResult();
        h.Sysadmin.Stub(new Entity("sprk_todo", TodoId) { ["sprk_regardingproject"] = null!, ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId) });
        h.Org.ResetCounts();
        return h;
    }

    [Fact]
    public async Task Run_RefileWhoseCloseIsChangedSinceRead_CreatesNoNewEpisode_SoNoSubjectHasTwoOpenSignals()
    {
        var h = RefiledToDoHarness();
        var oldId = h.Org.Rows.Keys.Single();
        h.Org.ResolveByPersonBeforeConditionalUpdate.Add(oldId);

        var result = await h.RunAsync();

        result.ResultJson.Should().Contain("\"changedSinceRead\":1");
        h.Org.CreateCalls.Should().Be(0, "episode 2 must not open while episode 1 was not closed");
        h.Org.Rows.Values.Count(r => r.GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value == SignalWriter.SignalStatusOpen)
            .Should().Be(0);

        // the next pass reads the person's resolution and re-raises under the new core (the old Signal is Resolved, in another core)
        var next = await h.RunAsync();
        next.Success.Should().BeTrue();
        h.Org.CreatedPayloads.Should().ContainSingle().Which.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(MatterId));
    }

    [Fact]
    public async Task Run_RefileWhoseCloseThrows_CreatesNoNewEpisode_AndTheRunFails()
    {
        var h = RefiledToDoHarness();
        h.Org.FailConditionalUpdates = true;

        var result = await h.RunAsync();

        result.Success.Should().BeFalse();
        h.Org.CreateCalls.Should().Be(0, "the old episode is still open: a second open Signal must not be created beside it");
        h.Org.Rows.Should().ContainSingle().Which.Value.GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value
            .Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Fact]
    public async Task Run_RefileWhoseCreateFailsAfterTheOldEpisodeClosed_IsReRaisedOnTheNextPass()
    {
        var h = RefiledToDoHarness();
        h.Org.FailCreates = true;

        var failed = await h.RunAsync();

        failed.Success.Should().BeFalse();
        h.Org.Rows.Should().ContainSingle().Which.Value.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value
            .Should().Be(SignalWriter.ResolutionConditionCleared);

        h.Org.FailCreates = false;
        h.Org.ResetCounts();
        var retry = await h.RunAsync();

        retry.Success.Should().BeTrue();
        var created = h.Org.CreatedPayloads.Should().ContainSingle("the Do-lane date rule must not strand a re-filed subject").Subject;
        created.GetAttributeValue<string>("sprk_dedupekey").Should().EndWith("|2");
        created.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(MatterId));
    }

    [Fact]
    public async Task Run_AnOlderEpisodeStillOpenBesideANewerOne_IsAnAnomaly_AndTheRunFails()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId);
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 2), SignalWriter.SignalStatusOpen, coreId: MatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeFalse("a Signal left untouched as an anomaly is not a healthy run");
        result.ResultJson.Should().Contain("\"anomalies\":1");
    }

    [Fact]
    public async Task Run_ATenantScopedPolicyThatIsSkipped_MakesTheRunNotSuccessful_AndStillWarns()
    {
        var h = new Harness();
        h.Sysadmin.AddPolicy(PolicyCode, SignalWriter.LaneDecide, true, PathBBody, PathBTemplate, tenant: "tenant-a");

        var result = await h.RunAsync();

        result.Success.Should().BeFalse("a silently skipped policy must not leave the health check Healthy");
        result.ResultJson.Should().Contain("\"scopedNotEvaluated\":1");
        h.Log.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id);
        (await h.Cache.GetStringAsync(SignalReevaluationJob.LastSuccessCacheKey)).Should().BeNull();
    }

    [Fact]
    public async Task Run_AnotherPoliciesKeyUnderTheSamePrefix_PolicyCodeContainingAPipe_IsNotUnparseable()
    {
        var h = new Harness().WithPathBPolicy();
        // Task 102b fix round 3: policy POL|ODD exists (Off, so only POL is evaluated); without it, its Signal would be a deleted
        // policy's, which the deleted-policy sweep rightly closes.
        h.Sysadmin.AddPolicy($"{PolicyCode}|ODD", SignalWriter.LaneDecide, false, PathBBody, PathBTemplate);
        var otherId = h.Org.SeedSignal($"{PolicyCode}|ODD|sprk_matter|{Lower(MatterId)}|1", SignalWriter.SignalStatusOpen, coreId: MatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue("the key belongs to policy POL|ODD, not to a malformed Signal of this policy");
        result.ResultJson.Should().Contain("\"unparseableKeys\":0");
        h.Org.WriteCalls.Should().Be(0, "POL's pass must not close POL|ODD's Signal");
        h.Org.Rows[otherId].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Fact]
    public async Task Run_OpenToDoSignalWhoseDueDateMoved_IsReconciled_NotDuplicated()
    {
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));
        await h.RunAsync();
        h.WithTodo(TodoId, due: new DateTime(2026, 10, 3), owner: new EntityReference("systemuser", UserId));
        h.Org.ResetCounts();

        var result = await h.RunAsync();

        result.ResultJson.Should().Contain("\"updated\":1");
        h.Org.Rows.Should().ContainSingle().Which.Value.GetAttributeValue<DateTime?>("sprk_duedate").Should().Be(new DateTime(2026, 10, 3));
    }

    // =====================================================================================
    // Evaluate: side-effect free, per-item today (D-25), witnesses (D-23)
    // =====================================================================================

    [Fact]
    public async Task Evaluate_ReturnsHitsWithWitnessEvidence_AndPerformsNoDataverseWrite()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);

        var evaluation = await h.Evaluator.EvaluateAsync(h.PathBPolicy(), subjectId: null);

        evaluation.PolicyValid.Should().BeTrue();
        var hit = evaluation.Hits.Should().ContainSingle().Subject;
        hit.Core!.Entity.Should().Be("sprk_matter");
        hit.Evidence.Should().ContainSingle().Which.Clause.Should().Be("all[0]");
        hit.FactValues.Should().ContainKey("sprk_triagecategory").WhoseValue.Should().Be(100000001m, "the clause's pinned literal");
        hit.FactValues.Should().NotContainKey("sprk_revisedon", "never a notExists field (§0.3)");
        h.Org.WriteCalls.Should().Be(0);
        h.Sysadmin.WriteCalls.Should().Be(0);
    }

    [Fact]
    public async Task Evaluate_TwoAssigneesInUtcMinus8AndUtcPlus10_SameDueDate_OnlyTheOneWhoseTodayIsPastItIsOverdue()
    {
        // 2027-01-15T03:00Z: 2027-01-14 19:00 in Pacific (UTC-8, no DST in January), 2027-01-15 13:00 in Brisbane (UTC+10).
        var h = new Harness(runAt: new DateTimeOffset(2027, 1, 15, 3, 0, 0, TimeSpan.Zero)).WithTodoPolicy();
        var pacificTodo = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000d1");
        var brisbaneTodo = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000d2");
        h.WithTodo(pacificTodo, due: new DateTime(2027, 1, 14), owner: new EntityReference("systemuser", UserId),
            assignee: h.Sysadmin.ContactInZone("Pacific Standard Time"));
        h.WithTodo(brisbaneTodo, due: new DateTime(2027, 1, 14), owner: new EntityReference("systemuser", UserId),
            assignee: h.Sysadmin.ContactInZone("E. Australia Standard Time"));

        var evaluation = await h.Evaluator.EvaluateAsync(h.TodoPolicy(), subjectId: null);

        evaluation.Hits.Select(x => x.SubjectId).Should().Equal(new[] { brisbaneTodo },
            "due 14 January is before today (15th) in Brisbane but IS today in Pacific (D-25)");
    }

    [Fact]
    public async Task Evaluate_ItemWithNoAssignee_IsJudgedOnTheUtcDate_NeverOnTheOwnersZone_D129()
    {
        // 2027-01-15T03:00Z is 2027-01-14 19:00 in Pacific. The owner (a systemuser) sits in Pacific, but ownerid is a team's
        // slot (D-129), not a person: with no assignee the item is judged on the UTC date (15th), so due 14 January IS overdue.
        var h = new Harness(runAt: new DateTimeOffset(2027, 1, 15, 3, 0, 0, TimeSpan.Zero)).WithTodoPolicy();
        h.Sysadmin.SetUserZone(UserId, "Pacific Standard Time");
        h.WithTodo(TodoId, due: new DateTime(2027, 1, 14), owner: new EntityReference("systemuser", UserId));

        var evaluation = await h.Evaluator.EvaluateAsync(h.TodoPolicy(), subjectId: null);

        evaluation.Hits.Select(x => x.SubjectId).Should().Equal(new[] { TodoId },
            "the owner's Pacific zone must not be used (it would make 14 January 'today', not overdue)");
        XElement.Parse(h.Sysadmin.Fetches.Single()).Descendants("attribute").Select(a => a.Attribute("name")!.Value)
            .Should().NotContain("ownerid", "the evaluator no longer reads the owner as a person");
    }

    [Fact]
    public async Task Evaluate_DateOnlyBound_IsWidenedInTheQuery_SoAnItemNearMidnightIsNotLeftOut()
    {
        var h = new Harness().WithTodoPolicy();

        await h.Evaluator.EvaluateAsync(h.TodoPolicy(), subjectId: null);

        var conditions = XElement.Parse(h.Sysadmin.Fetches.Single()).Element("entity")!.Element("filter")!.Elements("condition")
            .Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value, c.Attribute("value")!.Value)).ToList();
        conditions.Should().Equal(("sprk_duedate", "le", "2026-10-12T03:00:00Z"));
    }

    [Fact]
    public async Task Evaluate_DateOnlyBoundOnRespondedOn_IsWidenedLikeTheOtherDateOnlyColumns()
    {
        // F-68: sprk_workassignment.sprk_respondedon is Date Only, so its relative bound is widened by DateOnlyWideningDays (2)
        // before the per-item judgement; sprk_responseduedate is the reference (same widening).
        var h = new Harness();
        const string body = """{"type":"Existence","subject":"sprk_workassignment","when":{"sprk_respondedon":{"<=":"now"}},"all":[]}""";
        var policy = new SignalEvaluationPolicy(PolicyId, PolicyCode, SignalWriter.LaneDo, VersionId, Existence.ToString(), body, "Responded.");

        await h.Evaluator.EvaluateAsync(policy, subjectId: null);

        var conditions = XElement.Parse(h.Sysadmin.Fetches.Single()).Element("entity")!.Element("filter")!.Elements("condition")
            .Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value, c.Attribute("value")!.Value)).ToList();
        conditions.Should().Equal(new[] { ("sprk_respondedon", "le", "2026-10-12T03:00:00Z") }, "the same widening the To Do due date gets");
    }

    [Fact]
    public async Task Evaluate_InvalidPolicyVersion_ReturnsNoHits_AndReadsNothing()
    {
        var h = new Harness();
        var policy = new SignalEvaluationPolicy(PolicyId, PolicyCode, SignalWriter.LaneDecide, VersionId, "100000002", "{not json", null);

        var evaluation = await h.Evaluator.EvaluateAsync(policy, subjectId: null);

        evaluation.PolicyValid.Should().BeFalse();
        evaluation.Hits.Should().BeEmpty();
        h.Sysadmin.Fetches.Should().BeEmpty();
    }

    // =====================================================================================
    // The episode rules (pure planner): D-13, D-32, FR-17a, D-34/D-36/D-39 grain
    // =====================================================================================

    private static readonly Guid MatterType = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df");
    private static readonly Guid ProjectType = Guid.Parse("ca68b3bb-8600-f111-8407-7c1e520aa4df");
    private static readonly Guid WorkAssignmentType = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid ServiceRequestType = Guid.Parse("144c6790-d58a-f111-8076-7ced8d174eb8");
    private static readonly Guid FakeFifthType = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005");

    [Theory]
    [InlineData(10, false)]
    [InlineData(15, true)]
    public void Plan_DecideDismissed_QuietWindow14_RaisesOnlyAfterTheWindow(int daysSinceDismissal, bool raised)
    {
        var hit = CommHit(MatterId);
        var dismissed = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionDismissed,
            resolvedOn: RunAt.UtcDateTime.AddDays(-daysSinceDismissal));

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { dismissed });

        if (raised) plan.Creates.Should().ContainSingle().Which.Episode.Should().Be(2);
        else
        {
            plan.Creates.Should().BeEmpty();
            plan.QuietWindow.Should().Be(1);
        }
    }

    // ---- D-119: Decide-lane Acted re-raises only on evidence newer than the resolution ----------------------------------

    private static SignalHit WithEvidence(SignalHit hit, params string?[] asOf) => hit with
    {
        Evidence = asOf.Select((t, i) => new SignalEvidenceRef("communication", $"sprk_communication:{Guid.NewGuid():D}", "Fact")
        { AsOf = t, Clause = $"all[{i}]" }).ToArray(),
    };

    private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Plan_DecideActed_PredicateStillHolds_OnlyOlderEvidence_RaisesNothing()
    {
        var resolvedOn = RunAt.UtcDateTime.AddDays(-3);
        var hit = WithEvidence(CommHit(MatterId), Iso(resolvedOn.AddDays(-1)), Iso(resolvedOn.AddHours(-2)));
        var acted = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed, resolvedOn: resolvedOn);

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { acted });

        plan.Creates.Should().BeEmpty("D-119: the predicate still holding is not new evidence");
        plan.AwaitingEvidence.Should().Be(1);
        (plan.Reconciles.Count + plan.Reassigns.Count + plan.Closes.Count).Should().Be(0);
    }

    [Fact]
    public void Plan_DecideActed_OneEvidenceRowNewerThanTheResolution_RaisesTheNextEpisode()
    {
        var resolvedOn = RunAt.UtcDateTime.AddDays(-3);
        var hit = WithEvidence(CommHit(MatterId), Iso(resolvedOn.AddDays(-1)), Iso(resolvedOn.AddHours(5)));
        var acted = Existing(hit, 2, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed, resolvedOn: resolvedOn);

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { acted });

        plan.Creates.Should().ContainSingle().Which.Episode.Should().Be(3);
        plan.AwaitingEvidence.Should().Be(0);
    }

    [Fact]
    public void Plan_DecideActed_EvidenceExactlyAtTheResolution_IsNotNewer()
    {
        var resolvedOn = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
        var hit = WithEvidence(CommHit(MatterId), Iso(resolvedOn));
        var acted = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed, resolvedOn: resolvedOn);

        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { acted }).Creates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)] // no evidence lines at all
    [InlineData(true)]  // evidence lines without a readable date
    public void Plan_DecideActed_NoDatedEvidence_StaysUnraised_AndIsCountedOnce(bool undatedLine)
    {
        var hit = undatedLine ? WithEvidence(CommHit(MatterId), (string?)null, "not a date") : CommHit(MatterId);
        var acted = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed, resolvedOn: RunAt.UtcDateTime.AddDays(-3));

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { acted });

        plan.Creates.Should().BeEmpty();
        plan.NoDatedEvidence.Should().Be(1);
    }

    [Fact]
    public void Plan_DecideActed_WithNoResolutionTime_StaysUnraised()
    {
        var hit = WithEvidence(CommHit(MatterId), Iso(RunAt.UtcDateTime.AddDays(-1)));
        var acted = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed, resolvedOn: null);

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { acted });

        plan.Creates.Should().BeEmpty();
        plan.NoDatedEvidence.Should().Be(1);
    }

    [Fact]
    public void Plan_DecideDismissed_PastTheQuietWindow_StillReRaises_WithoutAnyEvidence()
    {
        // Dismissed keeps its own rules (quiet window + suppression): D-119 does not touch it.
        var hit = CommHit(MatterId);
        var dismissed = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionDismissed,
            resolvedOn: RunAt.UtcDateTime.AddDays(-20));

        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { dismissed })
            .Creates.Should().ContainSingle().Which.Episode.Should().Be(2);
    }

    [Fact]
    public void Plan_DecideConditionCleared_StillReRaisesWhenThePredicateHoldsAgain()
    {
        var hit = CommHit(MatterId);
        var cleared = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionConditionCleared,
            resolvedOn: RunAt.UtcDateTime.AddDays(-1));

        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { cleared })
            .Creates.Should().ContainSingle().Which.Episode.Should().Be(2);
    }

    [Fact]
    public async Task Run_DecideActed_PredicateStillHolds_NoNewerEvidence_WritesNothing()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: RunAt.UtcDateTime.AddDays(-1), coreId: MatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.WriteCalls.Should().Be(0, "D-61 and D-119: a no-change night writes nothing");
        h.Org.CreateCalls.Should().Be(0);
        h.Log.Should().Contain(e => e.EventId.Id == OntologyWriterEvents.EvaluatorHeartbeat.Id && e.Message.Contains("awaitingEvidence=1"));
    }

    [Fact]
    public async Task Run_DecideActed_ACommunicationNewerThanTheResolution_RaisesEpisodeTwo()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId); // the communication is dated 2026-10-05 09:15 UTC
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), coreId: MatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.CreateCalls.Should().Be(1);
        h.Org.Rows.Values.Should().Contain(r => r.GetAttributeValue<string>("sprk_dedupekey") == Key(PolicyCode, "sprk_matter", MatterId, 2));
    }

    [Fact]
    public void Plan_RefiledWhileOpen_TheCreateNamesTheSignalItReplaces()
    {
        var hit = TodoHit(TodoId, due: new DateTime(2026, 10, 1), project: ProjectId);
        var open = Existing(hit, 1, SignalWriter.SignalStatusOpen, due: new DateTime(2026, 10, 1)) with { CoreRecordId = OtherMatterId };

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, RunAt, new[] { hit }, new[] { open });

        plan.Closes.Should().ContainSingle().Which.Id.Should().Be(open.Id);
        plan.Creates.Should().ContainSingle().Which.ReplacesSignalId.Should().Be(open.Id);
    }

    [Theory]
    [InlineData(SignalWriter.LaneDo, SignalWriter.ResolutionConditionCleared, 1, true)]
    [InlineData(SignalWriter.LaneDo, SignalWriter.ResolutionActed, 1, true)]
    [InlineData(SignalWriter.LaneDecide, SignalWriter.ResolutionDismissed, 3, false)]   // inside the quiet window: still held back
    [InlineData(SignalWriter.LaneDecide, SignalWriter.ResolutionDismissed, 20, true)]
    public void Plan_ResolvedLatestInAnotherCoreRecord_IsReRaisable_EvenWhenTheDueDateIsUnchanged(
        int lane, int resolution, int daysSinceResolved, bool raised)
    {
        var hit = TodoHit(TodoId, due: new DateTime(2026, 10, 1), project: ProjectId);
        var resolved = Existing(hit, 1, SignalWriter.SignalStatusResolved, resolution,
            resolvedOn: RunAt.UtcDateTime.AddDays(-daysSinceResolved), due: new DateTime(2026, 10, 1)) with
        { CoreRecordId = OtherMatterId };

        var plan = SignalEpisodePlanner.Plan(lane, 14, RunAt, new[] { hit }, new[] { resolved });

        if (raised) plan.Creates.Should().ContainSingle().Which.Episode.Should().Be(2);
        else plan.Creates.Should().BeEmpty();
    }

    [Fact]
    public void Plan_DoLaneResolved_RaisesOnlyAfterTheDueDateChanged()
    {
        var unchanged = TodoHit(TodoId, due: new DateTime(2026, 10, 1));
        var resolved = Existing(unchanged, 2, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed,
            resolvedOn: RunAt.UtcDateTime.AddDays(-1), due: new DateTime(2026, 10, 1));

        var same = SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, RunAt, new[] { unchanged }, new[] { resolved });
        var moved = SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, RunAt,
            new[] { unchanged with { DueDate = new DateTime(2026, 10, 8) } }, new[] { resolved });

        same.Creates.Should().BeEmpty();
        same.DateUnchanged.Should().Be(1);
        moved.Creates.Should().ContainSingle().Which.Episode.Should().Be(3);
    }

    [Fact]
    public void Plan_SuppressedCoreRecord_RaisesNothing_NotEvenAFirstEpisode()
    {
        var hit = CommHit(MatterId);
        var suppressedSibling = Existing(CommHit(MatterId, Guid.NewGuid()), 1, SignalWriter.SignalStatusResolved,
            SignalWriter.ResolutionDismissed, resolvedOn: RunAt.UtcDateTime.AddDays(-40), suppressedUntil: RunAt.UtcDateTime.AddDays(20));

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { suppressedSibling });

        plan.Creates.Should().BeEmpty();
        plan.Suppressed.Should().Be(1);
    }

    [Fact]
    public void Plan_SuppressionThatLapsed_NoLongerBlocks()
    {
        var hit = CommHit(MatterId);
        var lapsed = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionDismissed,
            resolvedOn: RunAt.UtcDateTime.AddDays(-40), suppressedUntil: RunAt.UtcDateTime.AddDays(-1));

        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { lapsed })
            .Creates.Should().ContainSingle().Which.Episode.Should().Be(2);
    }

    [Theory]
    [InlineData(SignalWriter.ResolutionSuperseded)]
    [InlineData(SignalWriter.ResolutionPolicyRetired)]
    public void Plan_SupersededOrPolicyRetired_ReRaises_EvenInsideTheQuietWindow_ButNotWhenSuppressed(int resolution)
    {
        var hit = CommHit(MatterId);
        var closed = Existing(hit, 1, SignalWriter.SignalStatusResolved, resolution, resolvedOn: RunAt.UtcDateTime.AddDays(-1));
        var suppressed = closed with { SuppressedUntil = RunAt.UtcDateTime.AddDays(5) };

        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { closed })
            .Creates.Should().ContainSingle().Which.Episode.Should().Be(2);
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { suppressed })
            .Creates.Should().BeEmpty();
    }

    [Fact]
    public void Plan_OpenAndUnchanged_PlansNoWrite()
    {
        var hit = TodoHit(TodoId, due: new DateTime(2026, 10, 1), project: ProjectId);
        var open = Existing(hit, 1, SignalWriter.SignalStatusAcknowledged, due: new DateTime(2026, 10, 1));

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, RunAt, new[] { hit }, new[] { open });

        (plan.Creates.Count + plan.Reconciles.Count + plan.Reassigns.Count + plan.Closes.Count).Should().Be(0);
        plan.Unchanged.Should().Be(1);
    }

    [Fact]
    public void Plan_HitWhoseCoreRecordWasRefused_IsNeitherWrittenNorClosed()
    {
        var hit = CommHit(MatterId) with { Core = null, CoreRefusalReason = OntologyWriterFailureReason.CoreRecordAmbiguous };
        var open = Existing(CommHit(MatterId), 1, SignalWriter.SignalStatusOpen);

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { open });

        plan.RefusedHits.Should().ContainSingle();
        plan.Closes.Should().BeEmpty("the predicate still holds: a refusal is not a cleared condition");
    }

    [Fact]
    public void Plan_HitUnderAProjectWithNoMatter_GroupsAndSuppressesPerPolicyAndThatProject()
    {
        var hit = TodoHit(TodoId, due: new DateTime(2026, 10, 1), project: ProjectId);
        var suppressedOnProject = Existing(TodoHit(Guid.NewGuid(), due: null, project: ProjectId), 1,
            SignalWriter.SignalStatusResolved, SignalWriter.ResolutionDismissed, suppressedUntil: RunAt.UtcDateTime.AddDays(3));
        var suppressedOnMatter = Existing(CommHit(MatterId), 1,
            SignalWriter.SignalStatusResolved, SignalWriter.ResolutionDismissed, suppressedUntil: RunAt.UtcDateTime.AddDays(3));

        SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, RunAt, new[] { hit }, new[] { suppressedOnMatter })
            .Creates.Should().ContainSingle("a matter's suppression does not reach a project's item");
        SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, RunAt, new[] { hit }, new[] { suppressedOnProject })
            .Suppressed.Should().Be(1);
    }

    [Fact]
    public void Plan_NoCoreItem_IsSuppressedPerPolicyAndItem_Only()
    {
        var hit = TodoHit(TodoId, due: new DateTime(2026, 10, 1));
        var otherNoCoreSuppressed = Existing(TodoHit(Guid.NewGuid(), due: null), 1, SignalWriter.SignalStatusResolved,
            SignalWriter.ResolutionDismissed, suppressedUntil: RunAt.UtcDateTime.AddDays(3));
        var sameItemSuppressed = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionDismissed,
            suppressedUntil: RunAt.UtcDateTime.AddDays(3), due: new DateTime(2026, 9, 1));

        SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, RunAt, new[] { hit }, new[] { otherNoCoreSuppressed })
            .Creates.Should().ContainSingle("D-39: no-core grain is (policy, item), never 'all no-core items'");
        SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, RunAt, new[] { hit }, new[] { sameItemSuppressed })
            .Suppressed.Should().Be(1);
    }

    [Fact]
    public void Plan_FourCoreTypesAndAFakeFifth_EachGroupsAndSuppressesOnItsOwnCoreRecord()
    {
        var types = new[]
        {
            ("sprk_matter", MatterType), ("sprk_project", ProjectType), ("sprk_workassignment", WorkAssignmentType),
            ("sprk_servicerequest", ServiceRequestType), ("sprk_fakecore", FakeFifthType),
        };
        foreach (var (entity, typeRef) in types)
        {
            var coreId = Guid.NewGuid();
            var hit = TodoHit(Guid.NewGuid(), due: new DateTime(2026, 10, 1)) with { Core = new SignalCoreRecord(entity, coreId, typeRef, null) };
            var sameIdOtherType = Existing(TodoHit(Guid.NewGuid(), due: null) with { Core = new SignalCoreRecord("sprk_other", coreId, Guid.NewGuid(), null) },
                1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionDismissed, suppressedUntil: RunAt.UtcDateTime.AddDays(3));
            var sameCore = Existing(TodoHit(Guid.NewGuid(), due: null) with { Core = hit.Core }, 1, SignalWriter.SignalStatusResolved,
                SignalWriter.ResolutionDismissed, suppressedUntil: RunAt.UtcDateTime.AddDays(3));

            SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, RunAt, new[] { hit }, new[] { sameIdOtherType })
                .Creates.Should().ContainSingle($"{entity}: the grain is (policy, core type, core id)");
            SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, RunAt, new[] { hit }, new[] { sameCore })
                .Suppressed.Should().Be(1, $"{entity}: suppressed on its own core record with no type-specific code");
        }
    }

    [Fact]
    public void Plan_OnlyOpenSignalsOfSubjectsThatAreNoLongerHits_AreClosed()
    {
        var hit = CommHit(MatterId);
        var stillTrue = Existing(hit, 1, SignalWriter.SignalStatusOpen);
        var gone = Existing(CommHit(OtherMatterId), 1, SignalWriter.SignalStatusOpen);
        var goneResolved = Existing(CommHit(Guid.NewGuid()), 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed);

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { stillTrue, gone, goneResolved });

        plan.Closes.Should().ContainSingle().Which.Should().Be(gone);
    }

    // =====================================================================================
    // Registration, placement and the diagnostic health check
    // =====================================================================================

    [Fact]
    public void Job_IsAnIScheduledJob_AndNoTimerBackgroundServiceWasAdded()
    {
        typeof(IScheduledJob).IsAssignableFrom(typeof(SignalReevaluationJob)).Should().BeTrue();
        typeof(IHostedService).IsAssignableFrom(typeof(SignalReevaluationJob)).Should().BeFalse();
        typeof(SignalReevaluationJob).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(SignalReevaluationJob).Namespace)
            .Should().NotContain(t => typeof(BackgroundService).IsAssignableFrom(t) || typeof(IHostedService).IsAssignableFrom(t));
    }

    [Fact]
    public void SignalsModule_RegistersTheJob_AndTheStalenessCheckOnCatalogDiagnosticsOnly()
    {
        var services = new ServiceCollection();
        services.AddSignalsModule();
        using var provider = services.BuildServiceProvider();

        services.Should().Contain(d => d.ServiceType == typeof(SignalReevaluationJob));
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Should().ContainSingle(r => r.Name == "signal-reevaluation").Subject;
        registration.Tags.Should().Contain("catalog", "the /healthz liveness probe excludes catalog-tagged checks");
        registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task HealthCheck_NoSuccessForLongerThanTheThreshold_IsUnhealthy_WithTheLastSuccessTime()
    {
        var (check, cache, clock) = HealthCheck();
        await cache.SetStringAsync(SignalReevaluationJob.LastSuccessCacheKey, RunAt.AddHours(-30).ToString("o"));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data["lastSuccess"].Should().Be(RunAt.AddHours(-30).ToString("o"));
    }

    [Fact]
    public async Task HealthCheck_NeverSucceeded_IsUnhealthy_RecentSuccess_IsHealthy()
    {
        var (check, cache, _) = HealthCheck();

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Unhealthy);
        await cache.SetStringAsync(SignalReevaluationJob.LastSuccessCacheKey, RunAt.AddHours(-2).ToString("o"));
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);
    }

    private static (SignalEvaluatorHealthCheck Check, IDistributedCache Cache, FakeTimeProvider Clock) HealthCheck()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var clock = new FakeTimeProvider(RunAt);
        return (new SignalEvaluatorHealthCheck(cache, new ConfigurationBuilder().Build(), clock), cache, clock);
    }

    // =====================================================================================
    // Task 025: a Threshold policy runs through the unchanged job and evaluator (D-16)
    // =====================================================================================

    [Fact]
    public async Task Run_ThresholdPolicy_WritesASignalForTheMatterOverTheThreshold_AndNoneForTheMatterUnder()
    {
        var h = new Harness().WithThresholdPolicy()
            .WithMatterUtilization(MatterId, 94.5000000000m, "MAT-OVER") // scale 10, as the SDK returns it (live spaarkedev1)
            .WithMatterUtilization(OtherMatterId, 62m, "MAT-UNDER");

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        var created = h.Org.CreatedPayloads.Should().ContainSingle("only the matter at 94.5 meets >= 90").Subject;
        created.GetAttributeValue<string>("sprk_dedupekey").Should().Be($"{PolicyCode}|sprk_matter|{Lower(MatterId)}|1");
        created.GetAttributeValue<string>("sprk_sentence").Should().Be("Budget utilization has reached 94.5 percent.",
            "the sentence states the subject own value of the column the rule compared (section 0.3)");

        // Task 025b (D-122): the subject query selects the rule's SCOPE only. The comparison is not sent to Dataverse as a condition
        // on the stored column (a copy only a form edit refreshes); it is judged from the base rows.
        var fetch = XElement.Parse(h.Sysadmin.Fetches.Should().ContainSingle().Subject);
        fetch.Descendants("condition").Should().BeEmpty("the stored column is not filtered on");
        fetch.Descendants("attribute").Select(a => a.Attribute("name")!.Value).Should().NotContain("sprk_budgetutilizationpercent");
        fetch.Descendants("link-entity").Should().BeEmpty("a threshold reads only its subject");
    }

    [Fact]
    public async Task Run_ThresholdPolicy_JudgesFromTheBaseRows_NotTheStaleStoredColumn_InBothDirections()
    {
        // MatterId: stored 1 (stale) but base rows say 95 -> fires. OtherMatterId: stored 99 (stale) but base rows say 62 -> does not.
        var h = new Harness().WithThresholdPolicy()
            .WithMatterUtilization(MatterId, 95m, "MAT-REAL-OVER")
            .WithMatterUtilization(OtherMatterId, 62m, "MAT-REAL-UNDER");
        h.Sysadmin.SubjectRows["sprk_matter"].Select(r => r.GetAttributeValue<decimal>("sprk_budgetutilizationpercent"))
            .Should().Equal(new[] { 1m, 99m }, "the stored copies are the opposite of the truth");

        await h.RunAsync();

        var created = h.Org.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<string>("sprk_dedupekey").Should().Be($"{PolicyCode}|sprk_matter|{Lower(MatterId)}|1");
        created.GetAttributeValue<string>("sprk_sentence").Should().Be("Budget utilization has reached 95 percent.",
            "the sentence states the computed value (D-121, section 0.3), never the stored 1");
        created.GetAttributeValue<string>("sprk_factsnapshot").Should().Contain("95").And.NotContain("\"sprk_budgetutilizationpercent\":1,");
    }

    [Fact]
    public async Task Run_ThresholdPolicy_ReadsTheBaseRowsWithGroupedQueries_NotOneQueryPerSubject()
    {
        var h = new Harness().WithThresholdPolicy();
        foreach (var n in Enumerable.Range(1, 5))
        {
            h.WithMatterUtilization(Guid.Parse($"2444af6d-e1f2-f011-8406-7ced8d1dc9{n:00}"), 50m + n, "MAT-" + n);
        }

        await h.RunAsync();

        h.Sysadmin.AggregateFetches.Should().HaveCount(2, "one grouped query for invoices and one for budgets, for all five matters");
        h.Sysadmin.AggregateFetches.Select(f => XElement.Parse(f).Element("entity")!.Attribute("name")!.Value)
            .Should().BeEquivalentTo("sprk_invoice", "sprk_budget");
        h.Sysadmin.AggregateFetches.Should().OnlyContain(f => f.Contains("aggregate=\"true\"") && f.Contains("groupby=\"true\""));
    }

    [Fact]
    public async Task Run_SpendThreshold_NeedsOnlyTheInvoiceQuery_AndOnlyCountsTheCountedStatuses()
    {
        const string body = """{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalspendtodate","operator":">=","value":50000}}""";
        var h = new Harness().WithThresholdPolicy(body, "Spend to date is {{sprk_totalspendtodate}}.");
        h.Sysadmin.SubjectRows["sprk_matter"].Add(new Entity("sprk_matter", MatterId) { ["sprk_matterid"] = MatterId });
        h.WithInvoice("sprk_matter", MatterId, 30000m, FinanceBaseValues.InvoiceStatusReviewed)
            .WithInvoice("sprk_matter", MatterId, 15000m, FinanceBaseValues.InvoiceStatusReviewed)
            .WithInvoice("sprk_matter", MatterId, 4000m, FinanceBaseValues.InvoiceStatusReviewed)
            .WithInvoice("sprk_matter", MatterId, 900000m, 100000000); // ToReview: the other real status, not counted, ignored

        await h.RunAsync();

        h.Sysadmin.AggregateFetches.Should().ContainSingle().Which.Should().Contain("sprk_invoice");
        h.Org.CreatedPayloads.Should().BeEmpty("30000 + 15000 + 4000 = 49000 is under 50000, and the uncounted 900000 must not be added");

        h.WithInvoice("sprk_matter", MatterId, 1000m); // now exactly 50000: at-least is inclusive
        h.Org.ResetCounts();
        await h.RunAsync();
        h.Org.CreatedPayloads.Should().ContainSingle().Which.GetAttributeValue<string>("sprk_sentence").Should().Be("Spend to date is 50000.");
    }

    [Theory]
    [InlineData(false, 0)]   // no budget row at all
    [InlineData(true, 0)]    // a budget row of zero
    public async Task Run_UtilizationThreshold_WithNoBudgetAboveZero_DoesNotFire_AndIsReportedAsAMissingInput(bool zeroRow, decimal budget)
    {
        var h = new Harness().WithThresholdPolicy();
        h.Sysadmin.SubjectRows["sprk_matter"].Add(new Entity("sprk_matter", MatterId) { ["sprk_matterid"] = MatterId, ["sprk_budgetutilizationpercent"] = 500m });
        h.WithInvoice("sprk_matter", MatterId, 75000m);
        if (zeroRow) h.WithBudget("sprk_matter", MatterId, budget);
        var spec = new SignalEvaluationPolicy(PolicyId, PolicyCode, SignalWriter.LaneDecide, VersionId, "Threshold", UtilizationBody, UtilizationTemplate);

        var evaluation = await h.Evaluator.EvaluateAsync(spec, null);

        evaluation.PolicyValid.Should().BeTrue();
        evaluation.Hits.Should().BeEmpty("a utilization of 75000 over no budget is undefined, not infinite, and the stored 500 is not trusted");
        var missing = evaluation.MissingInputs.Should().ContainSingle().Subject;
        (missing.SubjectEntity, missing.SubjectId, missing.Field).Should().Be(("sprk_matter", MatterId, "sprk_budgetutilizationpercent"));
        missing.Reason.Should().Contain("no budget above zero");
        h.Log.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("could not be judged") && e.Message.Contains("missing input"));
    }

    // ---- D-120 (finding 1): a subject whose input is missing is neither a hit nor observed false ------------------------------

    [Theory]
    [InlineData(false, 0)]   // the budget row was removed
    [InlineData(true, 0)]    // the budget was set to zero
    public async Task Run_UtilizationThreshold_OpenSignal_WhoseBudgetIsGone_StaysOpen_AndIsCountedAsAMissingInput(bool zeroRow, decimal budget)
    {
        var h = new Harness().WithThresholdPolicy();
        h.Sysadmin.SubjectRows["sprk_matter"].Add(new Entity("sprk_matter", MatterId)
        {
            ["sprk_matterid"] = MatterId,
            ["sprk_mattername"] = "Matter MAT-GONE",
            ["sprk_matternumber"] = "MAT-GONE",
        });
        h.WithInvoice("sprk_matter", MatterId, 75000m);
        if (zeroRow) h.WithBudget("sprk_matter", MatterId, budget);
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.ConditionalUpdates.Should().BeEmpty("the condition was not observed false: with no budget the utilization is undefined, so the Signal is not closed as cleared");
        h.Org.CreateCalls.Should().Be(0);
        result.ResultJson.Should().Contain("\"missingInputs\":1").And.Contain("\"closed\":0");
    }

    [Fact]
    public async Task Run_UtilizationThreshold_ActedSignal_WhoseBudgetIsGone_GetsNoClearedMarker()
    {
        var h = new Harness().WithThresholdPolicy();
        h.Sysadmin.SubjectRows["sprk_matter"].Add(new Entity("sprk_matter", MatterId)
        {
            ["sprk_matterid"] = MatterId,
            ["sprk_mattername"] = "Matter MAT-GONE",
            ["sprk_matternumber"] = "MAT-GONE",
        });
        h.WithInvoice("sprk_matter", MatterId, 75000m); // no budget row at all
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: ActedOn, coreId: MatterId, lastEvaluated: ActedOn);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.ConditionalUpdates.Should().BeEmpty("an undefined utilization is not the condition observed false, so no cleared marker is stamped");
        result.ResultJson.Should().Contain("\"clearedMarked\":0").And.Contain("\"missingInputs\":1");
    }

    [Fact]
    public void Plan_UnjudgedSubject_IsNeitherClosedNorStamped_ButAJudgedOneIs()
    {
        var hit = CommHit(MatterId);
        var other = CommHit(OtherMatterId);
        var open = Existing(hit, 1, SignalWriter.SignalStatusOpen);
        var acted = Existing(other, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed, resolvedOn: ActedOn);
        var unjudged = new HashSet<(string, Guid)> { ("sprk_matter", MatterId), ("sprk_matter", OtherMatterId) };

        var held = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, Array.Empty<SignalHit>(), new[] { open, acted },
            isThreshold: true, unjudged: unjudged);
        held.Closes.Should().BeEmpty();
        held.ClearedMarks.Should().BeEmpty();

        var judged = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, Array.Empty<SignalHit>(), new[] { open, acted }, isThreshold: true);
        judged.Closes.Should().ContainSingle();
        judged.ClearedMarks.Should().ContainSingle();
    }

    [Fact]
    public async Task Run_ThresholdPolicy_OnAProject_WritesASignalAboutTheProject_FromTheProjectsOwnBaseRows()
    {
        // The matter and the project share ids nowhere; a matter with big spend must not leak into the project's sum.
        var body = UtilizationBody.Replace("sprk_matter", "sprk_project", StringComparison.Ordinal);
        var h = new Harness().WithThresholdPolicy(body).WithProject(ProjectId, "PRJ-1")
            .WithBudget("sprk_project", ProjectId, 200000m).WithInvoice("sprk_project", ProjectId, 190000m)
            .WithBudget("sprk_matter", MatterId, 1m).WithInvoice("sprk_matter", MatterId, 1m);
        h.Sysadmin.SubjectRows["sprk_project"].Single()["sprk_budgetutilizationpercent"] = 3m; // stale stored copy

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        var created = h.Org.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<string>("sprk_dedupekey").Should().Be($"{PolicyCode}|sprk_project|{Lower(ProjectId)}|1");
        created.GetAttributeValue<EntityReference>("sprk_regardingproject").Id.Should().Be(ProjectId);
        created.GetAttributeValue<string>("sprk_sentence").Should().Be("Budget utilization has reached 95 percent.");
        h.Sysadmin.AggregateFetches.Should().OnlyContain(f => f.Contains("attribute=\"sprk_project\""), "the project lookup, not the matter's");
    }

    // ---- D-120: a Threshold Signal resolved Acted re-raises only after a RE-CROSSING ---------------------------------------

    private static readonly DateTime ActedOn = RunAt.UtcDateTime.AddDays(-2);

    private Harness ThresholdActedHarness(decimal utilization, DateTime? lastEvaluated = null)
    {
        var h = new Harness().WithThresholdPolicy().WithMatterUtilization(MatterId, utilization, "MAT-ACTED");
        h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: ActedOn, coreId: MatterId, lastEvaluated: lastEvaluated ?? ActedOn);
        return h;
    }

    [Fact]
    public async Task Run_ThresholdActed_ClearedThenCrossingAgain_StampsOnce_AndRaisesEpisodeTwo()
    {
        var h = ThresholdActedHarness(utilization: 62m); // the condition is FALSE now

        (await h.RunAsync()).Success.Should().BeTrue();
        var stamp = h.Org.ConditionalUpdates.Should().ContainSingle("the cleared observation is recorded once").Subject;
        stamp.Fields.Keys.Should().Equal("sprk_lastevaluated");
        h.Org.CreateCalls.Should().Be(0);

        // A second cleared pass writes nothing (D-61).
        h.Org.ResetCounts();
        (await h.RunAsync()).Success.Should().BeTrue();
        h.Org.ConditionalUpdates.Should().BeEmpty();
        h.Org.Updates.Should().BeEmpty();
        h.Org.CreateCalls.Should().Be(0);

        // The matter crosses the threshold again: a new episode (D-13 key, episode 2).
        h.Sysadmin.InvoiceRows.Single()["sprk_totalamount"] = new Money(95000m); // utilization is now 95 on the base rows
        (await h.RunAsync()).Success.Should().BeTrue();
        h.Org.CreatedPayloads.Should().ContainSingle().Which.GetAttributeValue<string>("sprk_dedupekey")
            .Should().Be($"{PolicyCode}|sprk_matter|{Lower(MatterId)}|2");
    }

    [Fact]
    public async Task Run_ThresholdActed_NeverCleared_StaysSilentWhileItHolds()
    {
        var h = ThresholdActedHarness(utilization: 95m); // still over the threshold, never observed false

        (await h.RunAsync()).Success.Should().BeTrue();

        h.Org.WriteCalls.Should().Be(0);
        h.Org.CreateCalls.Should().Be(0);
        h.Org.ConditionalUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_ThresholdActed_AlreadyMarked_ClearedPassWritesNothing()
    {
        var h = ThresholdActedHarness(utilization: 62m, lastEvaluated: ActedOn.AddDays(1));

        (await h.RunAsync()).Success.Should().BeTrue();

        h.Org.WriteCalls.Should().Be(0);
        h.Org.ConditionalUpdates.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ThresholdActed_UnknownResolutionTime_NeverStampsAndNeverReRaises()
    {
        var hit = CommHit(MatterId);
        var unknown = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed, resolvedOn: null);

        var cleared = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, Array.Empty<SignalHit>(), new[] { unknown }, isThreshold: true);
        cleared.ClearedMarks.Should().BeEmpty("a stamp cannot register as a marker without a resolution time; stamping would repeat every pass (D-61)");
        cleared.NoDatedEvidence.Should().Be(1);

        var holds = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { unknown with { LastEvaluated = RunAt.UtcDateTime } },
            isThreshold: true);
        holds.Creates.Should().BeEmpty();
        holds.NoDatedEvidence.Should().Be(1);
    }

    [Fact]
    public void Plan_ThresholdActed_ClearedWithinTheToleranceOfTheResolution_DoesNotStampYet()
    {
        var hit = CommHit(MatterId);
        var justResolved = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed,
            resolvedOn: RunAt.UtcDateTime.AddSeconds(-30));
        var justPast = justResolved with { ResolvedOn = RunAt.UtcDateTime - ExistingSignal.ClearedMarkerTolerance - TimeSpan.FromSeconds(1) };

        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, Array.Empty<SignalHit>(), new[] { justResolved }, isThreshold: true)
            .ClearedMarks.Should().BeEmpty("a stamp within the tolerance is not a marker, so the next pass would stamp again");
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, Array.Empty<SignalHit>(), new[] { justPast }, isThreshold: true)
            .ClearedMarks.Should().ContainSingle("the stamp now registers as a marker");
    }

    [Fact]
    public async Task Run_ThresholdActed_TheSignalChangedBeforeTheStamp_WritesNothing_CountsChangedSinceRead_AndTheRunSucceeds()
    {
        var h = new Harness().WithThresholdPolicy().WithMatterUtilization(MatterId, 62m, "MAT-ACTED");
        var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: ActedOn, coreId: MatterId, lastEvaluated: ActedOn);
        h.Org.ResolveByPersonBeforeConditionalUpdate.Add(id); // the row changes between the job's read and its stamp

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ResultJson.Should().Contain("\"changedSinceRead\":1").And.Contain("\"clearedMarked\":0");
        h.Org.ConditionalUpdates.Should().BeEmpty("the row-version check refused the stamp");
        h.Org.Rows[id].GetAttributeValue<DateTime>("sprk_lastevaluated").Should().Be(ActedOn, "no stamp was written");
    }

    [Fact]
    public void Plan_ThresholdActed_Marker_DecidesTheReRaise_AndTheStamp()
    {
        var hit = CommHit(MatterId);
        var unmarked = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed, resolvedOn: ActedOn);
        var marked = unmarked with { LastEvaluated = ActedOn.AddHours(6) };
        var withinTolerance = unmarked with { LastEvaluated = ActedOn.AddSeconds(5) };

        // Holds, no marker: silent. Holds, marker: episode 2. A stamp within moments of the resolution is not a marker.
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { unmarked }, isThreshold: true)
            .Creates.Should().BeEmpty();
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { withinTolerance }, isThreshold: true)
            .Creates.Should().BeEmpty();
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { marked }, isThreshold: true)
            .Creates.Should().ContainSingle().Which.Episode.Should().Be(2);

        // Not a hit: an unmarked Acted signal is stamped; a marked one is not; a Dismissed one is not.
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, Array.Empty<SignalHit>(), new[] { unmarked }, isThreshold: true)
            .ClearedMarks.Should().ContainSingle();
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, Array.Empty<SignalHit>(), new[] { marked }, isThreshold: true)
            .ClearedMarks.Should().BeEmpty();
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, Array.Empty<SignalHit>(),
            new[] { unmarked with { Resolution = SignalWriter.ResolutionDismissed } }, isThreshold: true).ClearedMarks.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ExistenceActed_IsUnchangedByTheMarkerAndNeverStamps()
    {
        var resolvedOn = RunAt.UtcDateTime.AddDays(-3);
        var hit = WithEvidence(CommHit(MatterId), Iso(resolvedOn.AddDays(-1)));
        var markedActed = Existing(hit, 1, SignalWriter.SignalStatusResolved, SignalWriter.ResolutionActed, resolvedOn: resolvedOn)
            with
        { LastEvaluated = resolvedOn.AddDays(1) };

        // D-119 still rules: older evidence does not re-raise, whatever lastevaluated says.
        var holds = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, new[] { hit }, new[] { markedActed });
        holds.Creates.Should().BeEmpty();
        holds.AwaitingEvidence.Should().Be(1);

        // Not a hit: nothing is stamped for an Existence body.
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, RunAt, Array.Empty<SignalHit>(),
            new[] { markedActed with { LastEvaluated = null } }).ClearedMarks.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_ThresholdPolicy_WithAnOptionSetWhenCondition_IsNotExcludedByTheFake_ControlForTheNumericFilter()
    {
        // statecode = 0 is an OptionSetValue: the fake must ignore it (not read it as a null number) and still judge the comparison.
        const string body = """{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_budgetutilizationpercent","operator":">=","value":90},"when":{"statecode":0}}""";
        var h = new Harness().WithThresholdPolicy(body, UtilizationTemplate)
            .WithMatterUtilization(MatterId, 94.5m, "MAT-OVER");
        h.Sysadmin.SubjectRows["sprk_matter"][0]["statecode"] = new OptionSetValue(0);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.CreatedPayloads.Should().ContainSingle("the active matter at 94.5 meets >= 90; an option set condition is not a number");
        XElement.Parse(h.Sysadmin.Fetches.Should().ContainSingle().Subject).Descendants("condition")
            .Select(c => c.Attribute("attribute")!.Value).Should().Contain("statecode");
    }

    [Fact]
    public async Task Run_ThresholdPolicy_ReadsACurrencyColumnAsAPlainNumber_AndEqualsTheDeclaredValueCounts()
    {
        const string body = """{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalspendtodate","operator":">=","value":50000}}""";
        var h = new Harness().WithThresholdPolicy(body, "Spend to date is {{sprk_totalspendtodate}}.");
        h.Sysadmin.SubjectRows["sprk_matter"].Add(new Entity("sprk_matter", MatterId) { ["sprk_matterid"] = MatterId });
        h.Sysadmin.SubjectRows["sprk_matter"].Add(new Entity("sprk_matter", OtherMatterId) { ["sprk_matterid"] = OtherMatterId });
        h.WithInvoice("sprk_matter", MatterId, 50000.0000000000m); // scale 10, as the SDK returns an aggregate
        h.WithInvoice("sprk_matter", OtherMatterId, 49999.99m);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.CreatedPayloads.Should().ContainSingle()
            .Which.GetAttributeValue<string>("sprk_sentence").Should().Be("Spend to date is 50000.");
    }

    [Fact]
    public async Task Run_ThresholdPolicy_WhenTheMatterFallsBelow_ClosesTheOpenSignalAsConditionCleared()
    {
        var h = new Harness().WithThresholdPolicy().WithMatterUtilization(MatterId, 71m, "MAT-OVER");
        var signalId = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        var update = h.Org.ConditionalUpdates.Should().ContainSingle().Subject;
        update.Id.Should().Be(signalId);
        update.Fields["sprk_resolutiontype"].Should().BeEquivalentTo(new OptionSetValue(SignalWriter.ResolutionConditionCleared));
        h.Org.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Run_ThresholdPolicyWhoseTemplateNamesAFieldItDoesNotRead_IsRefusedAtEvaluation_AndWritesNothing()
    {
        var h = new Harness()
            .WithThresholdPolicy(template: "Utilization {{sprk_budgetutilizationpercent}} on {{sprk_matterstatus}}.")
            .WithMatterUtilization(MatterId, 94.5m, "MAT-OVER");

        await h.RunAsync();

        h.Org.WriteCalls.Should().Be(0, "section 0.3: a template may not assert a field the rule did not read");
        h.Log.Should().Contain(e => e.EventId.Id == OntologyWriterEvents.PolicyVersionInvalid.Id);
    }

    // =====================================================================================
    // Fixtures
    // =====================================================================================

    private static string Lower(Guid id) => id.ToString("D");

    private static string Key(string code, string entity, Guid id, int episode) => SignalWriter.BuildDedupeKey(code, entity, Lower(id), episode);

    private static SignalHit CommHit(Guid matterId, Guid? subjectId = null) => new(
        "sprk_matter", subjectId ?? matterId, null, null, new SignalCoreRecord("sprk_matter", matterId, MatterType, null), null, null,
        new Dictionary<string, object?>(), Array.Empty<SignalEvidenceRef>());

    private static SignalHit TodoHit(Guid todoId, DateTime? due, Guid? project = null) => new(
        "sprk_todo", todoId, null, null,
        project is { } p
            ? new SignalCoreRecord("sprk_project", p, ProjectType, null)
            : new SignalCoreRecord(null, Guid.Empty, null, new EntityReference("team", DefaultTeamId)),
        null, due, new Dictionary<string, object?>(), Array.Empty<SignalEvidenceRef>());

    private static ExistingSignal Existing(
        SignalHit hit, int episode, int status, int? resolution = null, DateTime? resolvedOn = null,
        DateTime? suppressedUntil = null, DateTime? due = null) => new(
        Guid.NewGuid(), "1", hit.SubjectEntity, hit.SubjectId, episode, status, resolution, resolvedOn, suppressedUntil, due,
        hit.Core!.TypeRefId, hit.Core.IsNoCore ? null : hit.Core.Id, hit.Core.NoCoreOwner ?? new EntityReference("team", DefaultTeamId));

    /// <summary>The real job, evaluator, writer, validator and core-ancestor resolver over the in-memory seams.</summary>
    // =====================================================================================
    // Task 033: closure semantics (FR-16; D-13, D-14, D-22, D-32, D-34)
    // =====================================================================================

    private static void AssertSystemClosure(Entity row, int resolution)
    {
        row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusResolved);
        row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(resolution);
        row.GetAttributeValue<DateTime?>("sprk_resolvedon").Should().NotBeNull("closing stamps sprk_resolvedon");
        row.Contains("sprk_resolvedby").Should().BeFalse("no human acted");
        row.Contains("sprk_decisionrecord").Should().BeFalse("a system closure has no Decision Record");
        row.Contains("sprk_suppresseduntil").Should().BeFalse("an auto-closure never feeds suppression");
    }

    [Theory]
    [InlineData("2026-08-01T09:15:00Z", true)]   // 70 days old: outside the 30-day window
    [InlineData("2026-09-09T09:15:00Z", true)]   // just outside (window starts 2026-09-10)
    [InlineData("2026-09-11T09:15:00Z", false)]  // just inside: the condition still holds
    public async Task Run_CommunicationAgedOutOfTheWindow_ClosesConditionCleared(string received, bool closes)
    {
        // The communication exists, but the rule's now-30d bound (the fake honours the FetchXML link filter, as Dataverse does)
        // excludes it once it is old. Without the window bound in the query this subject would still be a hit.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId, DateTime.Parse(received, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal));
        var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        if (closes) AssertSystemClosure(h.Org.Rows[id], SignalWriter.ResolutionConditionCleared);
        else h.Org.WriteCalls.Should().Be(0, "still a hit: nothing is written (D-61)");
    }

    [Fact]
    public async Task Run_OpenSignalCitingASupersededVersion_ClosesSuperseded_AndTheNewVersionReRaisesTheSubject()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var v1 = h.Sysadmin.CurrentVersionOf(PolicyCode);
        var v1Signal = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: policyId, versionId: v1);
        var v2 = h.Sysadmin.PublishNewVersion(PolicyCode, PathBBody, PathBTemplate);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        AssertSystemClosure(h.Org.Rows[v1Signal], SignalWriter.ResolutionSuperseded);
        var created = h.Org.CreatedPayloads.Should().ContainSingle("the new version still holds for the subject").Subject;
        created.GetAttributeValue<string>("sprk_dedupekey").Should().EndWith("|2", "a new episode, FR-03");
        created.GetAttributeValue<EntityReference>("sprk_policyversion")!.Id.Should().Be(v2, "the new episode cites the new version");
        result.ResultJson.Should().Contain("\"supersededClosed\":1").And.Contain("\"reraised\":1");
    }

    [Fact]
    public async Task Run_SupersededSignalWhoseSubjectTheNewVersionNoLongerHolds_StaysSuperseded_AndRaisesNothing()
    {
        var h = new Harness().WithPathBPolicy(); // no hit under v2
        var v1Signal = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: h.Sysadmin.PolicyIdOf(PolicyCode), versionId: h.Sysadmin.CurrentVersionOf(PolicyCode));
        h.Sysadmin.PublishNewVersion(PolicyCode, PathBBody, PathBTemplate);

        await h.RunAsync();

        AssertSystemClosure(h.Org.Rows[v1Signal], SignalWriter.ResolutionSuperseded);
        h.Org.CreateCalls.Should().Be(0);
        h.Org.ConditionalUpdates.Should().ContainSingle("closed once, as Superseded, not again as ConditionCleared");
    }

    [Fact]
    public async Task ReevaluatePolicyAsync_Callable_SupersedesAndRaisesTheNewEpisode_AndNeverTouchesAResolvedRow()
    {
        // The task 101 publish contract: stamp + move the pointer, then call this. One path: supersede AND re-raise.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var v1 = h.Sysadmin.CurrentVersionOf(PolicyCode);
        var openOld = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: policyId, versionId: v1);
        var actedOld = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", OtherMatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: RunAt.UtcDateTime.AddDays(-2), coreId: OtherMatterId,
            policyId: policyId, versionId: v1);
        var v2 = h.Sysadmin.PublishNewVersion(PolicyCode, PathBBody, PathBTemplate);

        var result = await h.Job.ReevaluatePolicyAsync(policyId);

        result.Success.Should().BeTrue();
        result.SupersededClosed.Should().Be(1);
        result.Raised.Should().Be(1);
        AssertSystemClosure(h.Org.Rows[openOld], SignalWriter.ResolutionSuperseded);
        var created = h.Org.CreatedPayloads.Should().ContainSingle("the new version still holds, so exactly one new episode").Subject;
        created.GetAttributeValue<string>("sprk_dedupekey").Should().Be($"{PolicyCode}|sprk_matter|{Lower(MatterId)}|2");
        created.GetAttributeValue<EntityReference>("sprk_policyversion")!.Id.Should().Be(v2);
        h.Org.Rows[actedOld].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionActed);
        h.Org.ConditionalUpdates.Should().ContainSingle("only the open v1 Signal was closed");
    }

    [Fact]
    public async Task ReevaluatePolicyAsync_OnAnOffPolicy_Throws_AndWritesNothing()
    {
        var h = new Harness().WithPathBPolicy(enabled: false);

        await h.Job.Invoking(j => j.ReevaluatePolicyAsync(h.Sysadmin.PolicyIdOf(PolicyCode)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*ClosePolicyRetiredNowAsync*");
        h.Org.WriteCalls.Should().Be(0);
    }

    [Fact]
    public async Task ClosurePathsThatFail_ReportIsCompleteFalse_NotJustACount()
    {
        var h = new Harness().WithPathBPolicy();
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var failing = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: policyId, versionId: h.Sysadmin.CurrentVersionOf(PolicyCode));
        h.Org.FailConditionalUpdates = true;

        var failed = await h.Closure.ClosePolicyRetiredNowAsync(policyId);

        failed.Should().Be(new SignalClosureOutcome(0, 0, 1));
        failed.IsComplete.Should().BeFalse("a Signal is still open; a caller reading Closed alone would report success");
        h.Org.Rows[failing].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen);

        h.Org.FailConditionalUpdates = false;
        h.Org.ResolveByPersonBeforeConditionalUpdate.Add(failing);
        var changed = await h.Closure.ClosePolicyRetiredNowAsync(policyId);
        changed.ChangedSinceRead.Should().Be(1);
        changed.IsComplete.Should().BeFalse("a person resolved it after the read: re-run to confirm");

        (await h.Closure.ClosePolicyRetiredNowAsync(policyId)).IsComplete.Should().BeTrue("the re-run finds nothing open");
    }

    [Fact]
    public async Task Run_OffPolicyClosureFailure_MakesTheRunNotSuccessful_AndLeavesTheSignalOpen()
    {
        var h = new Harness().WithPathBPolicy(enabled: false);
        var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: h.Sysadmin.PolicyIdOf(PolicyCode), versionId: h.Sysadmin.CurrentVersionOf(PolicyCode));
        h.Org.FailConditionalUpdates = true;

        var result = await h.RunAsync();

        result.Success.Should().BeFalse("a Signal of an Off policy could not be closed: never a quiet success");
        result.ResultJson.Should().Contain("\"refused\":1").And.Contain("\"outcome\":\"failed\"");
        h.Org.Rows[id].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen);
        (await h.Cache.GetStringAsync(SignalReevaluationJob.LastSuccessCacheKey)).Should().BeNull();
    }

    [Fact]
    public async Task Run_SupersedeCloseSkippedBecauseAPersonResolvedIt_IsCountedOnce_AndNotRetriedByThePlanner()
    {
        var h = new Harness().WithPathBPolicy(); // no hit: the planner would also want to close it as ConditionCleared
        var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: h.Sysadmin.PolicyIdOf(PolicyCode), versionId: h.Sysadmin.CurrentVersionOf(PolicyCode));
        h.Sysadmin.PublishNewVersion(PolicyCode, PathBBody, PathBTemplate);
        h.Org.ResolveByPersonBeforeConditionalUpdate.Add(id);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ResultJson.Should().Contain("\"changedSinceRead\":1", "counted once, not once by the supersede and again by the planner")
            .And.Contain("\"closed\":0");
        h.Org.ConditionalUpdates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(30, false)]
    [InlineData(null, false)]
    public async Task Run_CurrentVersionWhoseInForceToHasPassed_IsAFailure_NothingEvaluatedOrClosed(int? daysFromRun, bool refused)
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Sysadmin.SetCurrentVersionInForceTo(PolicyCode, daysFromRun is { } d ? RunAt.UtcDateTime.AddDays(d) : null);
        // An open Signal citing an older version: would be Superseded if the policy were evaluated.
        var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: h.Sysadmin.PolicyIdOf(PolicyCode), versionId: Guid.NewGuid());

        var result = await h.RunAsync();

        if (refused)
        {
            result.Success.Should().BeFalse();
            result.ResultJson.Should().Contain("\"policyFailures\":1").And.Contain("\"policiesEvaluated\":0");
            h.Org.WriteCalls.Should().Be(0, "nothing is closed or raised for a policy with no version in force");
            h.Sysadmin.Fetches.Should().BeEmpty();
            h.Log.Should().Contain(e => e.EventId.Id == OntologyWriterEvents.PolicyVersionNotInForce.Id);
            h.Org.Rows[id].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen);
        }
        else
        {
            result.Success.Should().BeTrue("a current version with no (or a future) end runs normally");
            h.Log.Should().NotContain(e => e.EventId.Id == OntologyWriterEvents.PolicyVersionNotInForce.Id);
            AssertSystemClosure(h.Org.Rows[id], SignalWriter.ResolutionSuperseded);
            h.Org.CreatedPayloads.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Run_PolicySwitchedOff_ClosesItsOpenSignalsPolicyRetired_AtThePass_AndLeavesResolvedOnesAlone()
    {
        var h = new Harness().WithPathBPolicy(enabled: false).WithMatterHit(MatterId);
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var version = h.Sysadmin.CurrentVersionOf(PolicyCode);
        var open = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: policyId, versionId: version);
        var acknowledged = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", OtherMatterId, 1), SignalWriter.SignalStatusAcknowledged,
            coreId: OtherMatterId, policyId: policyId, versionId: version);
        var acted = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", Guid.NewGuid(), 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: RunAt.UtcDateTime.AddDays(-1), policyId: policyId, versionId: version);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        AssertSystemClosure(h.Org.Rows[open], SignalWriter.ResolutionPolicyRetired);
        AssertSystemClosure(h.Org.Rows[acknowledged], SignalWriter.ResolutionPolicyRetired);
        h.Org.Rows[acted].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value
            .Should().Be(SignalWriter.ResolutionActed, "no closure path touches a Resolved Signal");
        h.Org.ConditionalUpdates.Should().HaveCount(2);
        h.Org.CreateCalls.Should().Be(0, "an Off policy is not evaluated, so nothing is raised");
        result.ResultJson.Should().Contain("\"policyRetiredClosed\":2");
    }

    [Fact]
    public async Task Run_RetiredPolicy_ClosesItsOpenSignalsPolicyRetired()
    {
        var h = new Harness().WithPathBPolicy();
        h.Sysadmin.SetPolicyState(PolicyCode, enabled: true, statecode: 1); // Retired: Inactive
        var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: h.Sysadmin.PolicyIdOf(PolicyCode), versionId: h.Sysadmin.CurrentVersionOf(PolicyCode));

        await h.RunAsync();

        AssertSystemClosure(h.Org.Rows[id], SignalWriter.ResolutionPolicyRetired);
    }

    [Fact]
    public async Task ClosePolicyRetiredNowAsync_ClosesOpenSignalsImmediately_NoPassNeeded()
    {
        var h = new Harness().WithPathBPolicy();
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var version = h.Sysadmin.CurrentVersionOf(PolicyCode);
        var open = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: policyId, versionId: version);
        var acted = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", OtherMatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionActed, resolvedOn: RunAt.UtcDateTime.AddDays(-1), policyId: policyId, versionId: version);

        var outcome = await h.Closure.ClosePolicyRetiredNowAsync(policyId);

        outcome.Should().Be(new SignalClosureOutcome(1, 0, 0));
        outcome.IsComplete.Should().BeTrue();
        AssertSystemClosure(h.Org.Rows[open], SignalWriter.ResolutionPolicyRetired);
        h.Org.Rows[acted].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionActed);
        (await h.Closure.ClosePolicyRetiredNowAsync(policyId)).Should().Be(SignalClosureOutcome.None, "idempotent: a second call writes nothing");
    }

    [Fact]
    public async Task Run_PolicySwitchedOffThenOn_ReRaisesStillTrueSubjects_AndLeavesThePolicyRetiredSignalUnchanged()
    {
        var h = new Harness().WithPathBPolicy(enabled: false).WithMatterHit(MatterId);
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var old = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: policyId, versionId: h.Sysadmin.CurrentVersionOf(PolicyCode));
        await h.RunAsync(); // Off: closed PolicyRetired
        AssertSystemClosure(h.Org.Rows[old], SignalWriter.ResolutionPolicyRetired);
        var closedAs = h.Org.Rows[old].Attributes.ToDictionary(a => a.Key, a => a.Value);
        h.Org.ResetCounts();

        h.Sysadmin.SetPolicyState(PolicyCode, enabled: true); // On again
        h.Clock.Advance(TimeSpan.FromDays(1));
        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.CreatedPayloads.Should().ContainSingle().Which.GetAttributeValue<string>("sprk_dedupekey").Should().EndWith("|2", "D-32");
        h.Org.Updates.Should().BeEmpty();
        h.Org.ConditionalUpdates.Should().BeEmpty("the PolicyRetired Signal is not touched again");
        h.Org.Rows[old].Attributes.ToDictionary(a => a.Key, a => a.Value).Should().BeEquivalentTo(closedAs);
    }

    [Fact]
    public async Task Run_ASignalAPersonResolvedBeforeAnOffClosure_IsNotClosed()
    {
        var h = new Harness().WithPathBPolicy(enabled: false);
        var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: MatterId, policyId: h.Sysadmin.PolicyIdOf(PolicyCode), versionId: h.Sysadmin.CurrentVersionOf(PolicyCode));
        h.Org.ResolveByPersonBeforeConditionalUpdate.Add(id);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ResultJson.Should().Contain("\"changedSinceRead\":1").And.Contain("\"closed\":0");
        h.Org.Rows[id].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionDismissed);
    }

    [Fact]
    public async Task Run_AutoClosuresOfProjectGroupedSignals_NeverCountAsADismissal_ForThePolicyProjectGrain()
    {
        // Superseded and ConditionCleared closures of Signals grouped under a project: none is Dismissed and none sets
        // sprk_suppresseduntil, so a later hit on the same (policy, project) is not suppressed (D-34).
        var h = new Harness().WithPathBPolicy();
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var v1 = h.Sysadmin.CurrentVersionOf(PolicyCode);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() }
            .Select(todo => h.Org.SeedSignal(Key(PolicyCode, "sprk_todo", todo, 1), SignalWriter.SignalStatusOpen, policyId: policyId, versionId: v1))
            .ToArray();
        foreach (var id in ids)
        {
            h.Org.Rows[id]["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", ProjectType);
            h.Org.Rows[id]["sprk_corerecordid"] = Lower(ProjectId);
        }

        h.Sysadmin.PublishNewVersion(PolicyCode, PathBBody, PathBTemplate);
        // ids[1] now cites the current version and is not a hit (ConditionCleared); ids[0] still cites v1 (Superseded).
        h.Org.Rows[ids[1]]["sprk_policyversion"] = new EntityReference("sprk_policyversion", h.Sysadmin.CurrentVersionOf(PolicyCode));
        await h.RunAsync();

        h.Org.Rows[ids[0]].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionSuperseded);
        h.Org.Rows[ids[1]].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionConditionCleared);
        h.Org.Rows.Values.Count(r => r.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value == SignalWriter.ResolutionDismissed)
            .Should().Be(0, "an auto-closure is never recorded as a dismissal");
        h.Org.Rows.Values.Should().OnlyContain(r => !r.Contains("sprk_suppresseduntil"));

        var hit = TodoHit(Guid.NewGuid(), due: new DateTime(2026, 10, 1), project: ProjectId);
        var closed = ids.Select(id => PlannerView(h.Org.Rows[id])).ToArray();
        SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, RunAt, new[] { hit }, closed)
            .Creates.Should().ContainSingle("no Signal of the (policy, project) grain is suppressed by auto-closures");
    }

    private static ExistingSignal PlannerView(Entity row) => new(
        row.Id, null, "sprk_todo", Guid.NewGuid(), 1,
        row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value, row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value,
        row.GetAttributeValue<DateTime?>("sprk_resolvedon"), row.GetAttributeValue<DateTime?>("sprk_suppresseduntil"), null,
        row.GetAttributeValue<EntityReference>("sprk_corerecordtype")?.Id,
        Guid.TryParse(row.GetAttributeValue<string>("sprk_corerecordid"), out var c) ? c : null, null);

    [Fact]
    public async Task Run_HandsThePlanner_TheSecureFamilyPartitionsOfTheRowsAndOfTheHits_1661()
    {
        // The matter is a secure root. Its three earlier episodes, dismissed 20, 18 and 16 days ago (past the 14-day quiet window), are
        // the family's own three dismissals and carry its live stamp. The rows are still the writer's (no owner team): only the lineage
        // walk places them, and only the hit's own placement puts the new episode in the same family.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        h.Sysadmin.SecureMatter(MatterId);
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var until = RunAt.UtcDateTime.AddDays(-16).AddDays(30);
        foreach (var (episode, days) in new[] { (1, 20), (2, 18), (3, 16) })
        {
            var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, episode), SignalWriter.SignalStatusResolved,
                SignalWriter.ResolutionDismissed, resolvedOn: RunAt.UtcDateTime.AddDays(-days), coreId: MatterId, suppressedUntil: until, policyId: policyId);
            h.Org.Rows[id]["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId);
            h.Org.Rows[id]["sprk_matter"] = new EntityReference("sprk_matter", MatterId);
        }

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        h.Org.CreateCalls.Should().Be(0, "the family's stamp mutes the family's new episode: both were placed by the same truth");
        System.Text.Json.JsonDocument.Parse(result.ResultJson!).RootElement.GetProperty("suppressed").GetInt32().Should().Be(1);
        h.Org.Rows.Values.Should().OnlyContain(r => r.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == until, "a justified stamp is not rewritten (D-61)");
    }

    [Fact]
    public async Task Run_DoLane_BobsThreeDismissals_StillRaiseAlicesToDo_AndSuppressBobsNext_152()
    {
        // Task 152 (D-145a, #1921): Bob dismissed three of his own To Dos on the project (he decided each); their rows carry the stamp those
        // dismissals justify (written when he dismissed). The nightly pass raises Alice's overdue To Do on the same project, and suppresses
        // Bob's next one.
        var bob = Guid.Parse("15200000-0000-4000-8000-0000000000b0");
        var alice = Guid.Parse("15200000-0000-4000-8000-0000000000a0");
        var h = new Harness().WithTodoPolicy();
        var policyId = h.Sysadmin.PolicyIdOf(TodoPolicyCode);
        var versionId = h.Sysadmin.CurrentVersionOf(TodoPolicyCode);
        var due = new DateTime(2026, 10, 1);
        var until = RunAt.UtcDateTime.AddDays(-1).AddDays(30);
        foreach (var days in new[] { 6, 3, 1 })
        {
            var todo = Guid.NewGuid();
            h.WithTodo(todo, due, project: ProjectId, assignee: bob);
            var id = h.Org.SeedSignal(Key(TodoPolicyCode, "sprk_todo", todo, 1), SignalWriter.SignalStatusResolved, SignalWriter.ResolutionDismissed,
                resolvedOn: RunAt.UtcDateTime.AddDays(-days), suppressedUntil: until, due: due, policyId: policyId, versionId: versionId);
            h.Org.Rows[id]["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", ProjectType);
            h.Org.Rows[id]["sprk_corerecordid"] = Lower(ProjectId);
            var recordId = Guid.NewGuid();
            h.Org.Rows[id]["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", recordId);
            h.Sysadmin.DecisionRecords[recordId] = new Entity("sprk_decisionrecord", recordId)
            {
                ["sprk_recordclass"] = new OptionSetValue((int)RecordedDecisionClass.Dismissal),
                ["sprk_decidedon"] = RunAt.UtcDateTime.AddDays(-days),
                ["sprk_assignedtointernal"] = new EntityReference("contact", bob),
            };
        }

        var aliceTodo = Guid.NewGuid();
        h.WithTodo(aliceTodo, due, project: ProjectId, assignee: alice);
        var bobNext = Guid.NewGuid();
        h.WithTodo(bobNext, due, project: ProjectId, assignee: bob);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        h.Org.Rows.Values.Should().Contain(r => r.GetAttributeValue<string>("sprk_dedupekey") == Key(TodoPolicyCode, "sprk_todo", aliceTodo, 1),
            "Bob's dismissals never suppress Alice's Do item");
        h.Org.Rows.Values.Should().NotContain(r => r.GetAttributeValue<string>("sprk_dedupekey") == Key(TodoPolicyCode, "sprk_todo", bobNext, 1),
            "three of Bob's own dismissals on the project suppress his next Do item for the policy");
        h.Org.Updates.Should().BeEmpty("Bob's stamps are justified by his own dismissals and are not rewritten (D-61)");
    }

    /// <summary>#1661 round 2: the secure matter's family carries a live stamp from its three dismissals (each with a Decision Record), the
    /// matter is a hit, and another subject's open Signal is not a hit (a normal pass would close it as ConditionCleared).</summary>
    private static (Guid PolicyId, DateTime Until, Guid OtherOpen) SecureStampedMatterWithAnOpenNonHit(Harness h)
    {
        h.Sysadmin.SecureMatter(MatterId);
        var policyId = h.Sysadmin.PolicyIdOf(PolicyCode);
        var until = RunAt.UtcDateTime.AddDays(-16).AddDays(30);
        foreach (var (episode, days) in new[] { (1, 20), (2, 18), (3, 16) })
        {
            var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, episode), SignalWriter.SignalStatusResolved,
                SignalWriter.ResolutionDismissed, resolvedOn: RunAt.UtcDateTime.AddDays(-days), coreId: MatterId, suppressedUntil: until, policyId: policyId);
            h.Org.Rows[id]["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId);
            h.Org.Rows[id]["sprk_matter"] = new EntityReference("sprk_matter", MatterId);
            var recordId = Guid.NewGuid();
            h.Org.Rows[id]["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", recordId);
            h.Sysadmin.DecisionRecords[recordId] = new Entity("sprk_decisionrecord", recordId)
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", MatterId),
                ["sprk_recordclass"] = new OptionSetValue((int)RecordedDecisionClass.Dismissal),
                ["sprk_decidedon"] = RunAt.UtcDateTime.AddDays(-days),
            };
        }

        var otherMatter = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc9aa");
        var otherOpen = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", otherMatter, 1), SignalWriter.SignalStatusOpen,
            coreId: otherMatter, policyId: policyId, versionId: h.Sysadmin.CurrentVersionOf(PolicyCode));
        return (policyId, until, otherOpen);
    }

    private static void ShouldBeSkippedForPartitions(JobRunResult result, WriterOrg org, Guid policyId, Guid otherOpen, DateTime until)
    {
        result.Success.Should().BeFalse("a policy that was not planned is not a healthy run");
        org.CreateCalls.Should().Be(0, "nothing is raised");
        org.Updates.Should().BeEmpty("nothing is stamped, lowered or cleared");
        org.ConditionalUpdates.Should().BeEmpty("nothing is closed, reconciled or reassigned");
        org.Rows[otherOpen].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen,
            "the WHOLE policy is skipped, not only the hits on stamped records");
        org.Rows.Values.Where(r => r.Id != otherOpen).Should().OnlyContain(r => r.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == until);

        var json = System.Text.Json.JsonDocument.Parse(result.ResultJson!).RootElement;
        json.GetProperty("policyFailures").GetInt32().Should().Be(1);
        var skipped = json.GetProperty("partitionSkippedPolicies").EnumerateArray().Should().ContainSingle(
            "the skipped policy is in the run result, not only in a log line").Subject;
        skipped.GetProperty("policyId").GetGuid().Should().Be(policyId);
        skipped.GetProperty("policyCode").GetString().Should().Be(PolicyCode);
        skipped.GetProperty("failures").GetInt32().Should().Be(1);
        result.ErrorMessage.Should().Contain("partitionSkippedPolicies=1");
    }

    [Fact]
    public async Task Run_APartitioningFault_InTheSuppressionPass_SkipsThePolicyWhole_AndTheRunNamesIt_1661R2F3()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var (policyId, until, otherOpen) = SecureStampedMatterWithAnOpenNonHit(h);
        h.Sysadmin.FaultOn = q => q.EntityName == "businessunit" ? new TimeoutException("Dataverse timed out.") : null;

        var result = await h.RunAsync();

        ShouldBeSkippedForPartitions(result, h.Org, policyId, otherOpen, until);
    }

    [Fact]
    public async Task Run_AFaultPlacingTheHits_SkipsThePolicyWhole_NeverAPerHitAnswer_1661R2F2()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var (policyId, until, otherOpen) = SecureStampedMatterWithAnOpenNonHit(h);
        // The suppression pass completes: its last read is the Decision Records' facts batch (the one that asks for sprk_recordclass; the
        // walk's own reads of the records ask for lookups). The next placement, the hits', times out.
        var reconciled = false;
        var faulted = 0;
        h.Sysadmin.FaultOn = q =>
        {
            if (q.EntityName == "sprk_decisionrecord" && q.ColumnSet.Columns.Contains("sprk_recordclass")) reconciled = true;
            if (!reconciled || q.EntityName != "businessunit") return null;
            faulted++;
            return new TimeoutException("Dataverse timed out.");
        };

        var result = await h.RunAsync();

        reconciled.Should().BeTrue("precondition: the suppression pass read the Decision Records' facts, so it got past its own placement");
        faulted.Should().BeGreaterThan(0, "precondition: the fault is armed on the hits' placement, after the suppression pass");
        ShouldBeSkippedForPartitions(result, h.Org, policyId, otherOpen, until);
        h.Log.Should().Contain(e => e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains("partitions of its hits could not be read"), "the skip comes from the hit-placement fault");
        h.Log.Should().NotContain(e => e.Message.Contains("could not be placed in their secure-family partitions"),
            "the suppression pass itself completed: it is not the PartitionsUnavailable skip");
    }

    private sealed class Harness
    {
        public FakeTimeProvider Clock { get; }
        public World Sysadmin { get; } = new();
        public WriterOrg Org { get; } = new();
        public FakeOwnership Ownership { get; } = new();

        /// <summary>Task 143: the D-134 link read (nobody linked unless a test says so).</summary>
        public AccessControl.AssignedAccessTestDoubles.FakeAssignedAccessStore Links { get; } = new();

        /// <summary>Task 143: the reconciler's conditional owner write, over <see cref="Org"/>.</summary>
        public ReownWebApi ReownApi { get; }
        public IDistributedCache Cache { get; } = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        public List<LogEntry> Log { get; } = new();
        public SignalWriter Writer { get; }
        public SignalEvaluator Evaluator { get; }
        public SignalReevaluationJob Job { get; }
        public SignalClosureService Closure { get; }
        public SignalSuppressionService Suppression { get; }

        /// <summary>Task 153: the real Decision Record writer, over the same two seams (its creates land in <see cref="WriterOrg.DecisionRecordRows"/>).</summary>
        public DecisionRecordWriter Records { get; }

        public Harness(DateTimeOffset? runAt = null, string? writerClientId = null)
        {
            Clock = new FakeTimeProvider(runAt ?? RunAt);
            Sysadmin.Org = Org;
            Org.Now = () => Clock.GetUtcNow().UtcDateTime;
            var writerClient = writerClientId is null
                ? new OntologyWriterDataverseClient(() => Org, Logger<OntologyWriterDataverseClient>())
                : new OntologyWriterDataverseClient(
                    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dataverse:ServiceUrl"] = "https://spaarkedev1.crm.dynamics.com",
                        ["Ontology:Writer:ManagedIdentityClientId"] = writerClientId,
                    }).Build(),
                    Logger<OntologyWriterDataverseClient>());
            // #1661: uac-r2's real synchronizer (its walk reads the same World), scoped as in ExternalAccessModule. Task 143: the
            // writer's two scoped services, as the host registers them: the D-134 link read, and uac-r2's REAL reconciler over this
            // World (its guard reads) and Org (its conditional owner write, ReownWebApi).
            var configuration = new ConfigurationBuilder().Build();
            ReownApi = new ReownWebApi(Org);
            var services = new ServiceCollection();
            services.AddScoped(_ => new Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer(
                Sysadmin, null!, null!, configuration, NullLogger<Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer>.Instance));
            services.AddScoped(_ => AccessControl.AssignedAccessTestDoubles.Representation(Links));
            services.AddScoped(sp => new Sprk.Bff.Api.Services.Access.SecureChildReconciler(
                Sysadmin, Ownership, sp.GetRequiredService<Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer>(), ReownApi,
                configuration, NullLogger<Sprk.Bff.Api.Services.Access.SecureChildReconciler>.Instance, new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing));
            var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            Writer = new SignalWriter(writerClient, Sysadmin,
                new Ownership.CoreAncestorResolver(Sysadmin, StampColumnProbe, NullLogger<Ownership.CoreAncestorResolver>.Instance),
                new Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub(Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1),
                Ownership, scopes, Clock, Logger<SignalWriter>());
            var schema = new RuleBodySchemaValidator();
            var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, Clock), Logger<PolicyVersionValidator>());
            Evaluator = new SignalEvaluator(validator, Sysadmin, Writer, Logger<SignalEvaluator>());
            Closure = new SignalClosureService(Writer, Sysadmin, Clock, Logger<SignalClosureService>());
            Suppression = new SignalSuppressionService(Sysadmin, writerClient, Clock, configuration, scopes, Logger<SignalSuppressionService>());
            Records = new DecisionRecordWriter(writerClient, Ownership,
                new Ownership.CoreAncestorResolver(Sysadmin, StampColumnProbe, NullLogger<Ownership.CoreAncestorResolver>.Instance),
                Sysadmin,
                new Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub(Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1),
                Clock, Logger<DecisionRecordWriter>(), new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing);
            Job = new SignalReevaluationJob(Evaluator, Writer, Suppression, Closure, Records, Sysadmin, Cache, Clock, Logger<SignalReevaluationJob>());
            Sysadmin.Stub(new Entity("sprk_matter", MatterId) { ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId) });
            Sysadmin.Stub(new Entity("sprk_project", ProjectId) { ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId) });
        }

        public Task<JobRunResult> RunAsync() =>
            Job.ExecuteAsync(new JobRunContext(Guid.NewGuid(), "corr-031", JobRunTrigger.Scheduled, new Dictionary<string, object>()), default);

        public SignalEvaluationPolicy PathBPolicy() =>
            new(PolicyId, PolicyCode, SignalWriter.LaneDecide, VersionId, Existence.ToString(), PathBBody, PathBTemplate);

        public SignalEvaluationPolicy TodoPolicy() =>
            new(PolicyId, TodoPolicyCode, SignalWriter.LaneDo, VersionId, Existence.ToString(), TodoOverdueBody, "This To Do is past its due date.");

        public Harness WithPathBPolicy(bool enabled = true)
        {
            Sysadmin.AddPolicy(PolicyCode, SignalWriter.LaneDecide, enabled, PathBBody, PathBTemplate);
            return this;
        }

        public Harness WithThresholdPolicy(string body = UtilizationBody, string template = UtilizationTemplate)
        {
            Sysadmin.AddPolicy(PolicyCode, SignalWriter.LaneDecide, true, body, template, ruleType: Threshold);
            return this;
        }

        /// <summary>A matter whose BASE rows (a 100000 budget and an invoice of <c>percent</c> x 1000) give <paramref name="percent"/>
        /// utilization, with a deliberately WRONG stored copy (the other side of the 90 threshold): a rule that read the stored column
        /// would reach the opposite verdict, which is the point (task 025b, D-122).</summary>
        public Harness WithMatterUtilization(Guid matterId, decimal percent, string number)
        {
            Sysadmin.SubjectRows["sprk_matter"].Add(new Entity("sprk_matter", matterId)
            {
                ["sprk_matterid"] = matterId,
                ["sprk_mattername"] = "Matter " + number,
                ["sprk_matternumber"] = number,
                ["sprk_budgetutilizationpercent"] = percent >= 90m ? 1m : 99m,
            });
            WithBudget("sprk_matter", matterId, 100000m);
            return WithInvoice("sprk_matter", matterId, percent * 1000m);
        }

        public Harness WithProject(Guid projectId, string number)
        {
            Sysadmin.SubjectRows["sprk_project"].Add(new Entity("sprk_project", projectId)
            {
                ["sprk_projectid"] = projectId,
                ["sprk_projectname"] = "Project " + number,
                ["sprk_projectnumber"] = number,
            });
            return this;
        }

        public Harness WithInvoice(string parent, Guid parentId, decimal amount, int status = FinanceBaseValues.InvoiceStatusReviewed)
        {
            Sysadmin.InvoiceRows.Add(new Entity("sprk_invoice", Guid.NewGuid())
            {
                [parent] = new EntityReference(parent, parentId),
                ["sprk_totalamount"] = new Money(amount),
                ["sprk_invoicestatus"] = new OptionSetValue(status),
            });
            return this;
        }

        public Harness WithBudget(string parent, Guid parentId, decimal total)
        {
            Sysadmin.BudgetRows.Add(new Entity("sprk_budget", Guid.NewGuid())
            {
                [parent] = new EntityReference(parent, parentId),
                ["sprk_totalbudget"] = new Money(total),
            });
            return this;
        }

        public Harness WithTodoPolicy()
        {
            Sysadmin.AddPolicy(TodoPolicyCode, SignalWriter.LaneDo, true, TodoOverdueBody, "This To Do is past its due date.");
            return this;
        }

        public Harness WithMatterHit(Guid matterId, DateTime? received = null)
        {
            Sysadmin.SubjectRows["sprk_matter"].Add(new Entity("sprk_matter", matterId)
            {
                ["sprk_matterid"] = matterId,
                ["sprk_mattername"] = "Acme v Beta",
                ["sprk_matternumber"] = "MAT-0001",
            });
            Sysadmin.WitnessRows["sprk_communication"].Add(new Entity("sprk_communication", CommunicationId)
            {
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId),
                ["sprk_subject"] = "Rate increase from Firm LLP",
                ["sprk_from"] = "partner@firm.example",
                ["sprk_receiveddate"] = received ?? new DateTime(2026, 10, 5, 9, 15, 0, DateTimeKind.Utc),
            });
            return this;
        }

        public Harness WithTodo(Guid todoId, DateTime due, EntityReference? owner = null, Guid? project = null, Guid? assignee = null)
        {
            var row = new Entity("sprk_todo", todoId)
            {
                ["sprk_todoid"] = todoId,
                ["sprk_name"] = "File the reply",
                ["sprk_duedate"] = due,
                ["ownerid"] = owner ?? new EntityReference("systemuser", UserId),
            };
            if (project is { } p) row["sprk_regardingproject"] = new EntityReference("sprk_project", p);
            if (assignee is { } a) row["sprk_assignedto"] = new EntityReference("contact", a);
            Sysadmin.Stub(row);
            Sysadmin.SubjectRows["sprk_todo"].RemoveAll(r => r.Id == todoId);
            Sysadmin.SubjectRows["sprk_todo"].Add(row);
            return this;
        }

        private ILogger<T> Logger<T>() => new SinkLogger<T>(Log);
    }

    private static Task<IReadOnlySet<string>> StampColumnProbe(string entityLogicalName, CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
            Ownership.CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute), StringComparer.OrdinalIgnoreCase));

    internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception = null);

    private sealed class SinkLogger<T>(List<LogEntry> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (sink) sink.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }

    /// <summary>Captures this meter's measurements for one test (other tests may record concurrently: assert "at least").</summary>
    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

        public MetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName) listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                lock (_measurements) _measurements.Add((instrument.Name, value, tags.ToArray()));
            });
            _listener.Start();
        }

        public long Counter(string name, string tag, string value)
        {
            lock (_measurements)
                return _measurements.Where(m => m.Name == name && m.Tags.Any(t => t.Key == tag && Equals(t.Value, value))).Sum(m => m.Value);
        }

        public IReadOnlyList<long> Gauge(string name)
        {
            _listener.RecordObservableInstruments();
            lock (_measurements) return _measurements.Where(m => m.Name == name).Select(m => m.Value).ToList();
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>uac-r2's resolver seam: answers per core record id (default: an ordinary, not secure, owner).</summary>
    private sealed class FakeOwnership : Ownership.IRecordOwnershipResolver
    {
        public Dictionary<Guid, Ownership.RecordOwnerResolution> AnswerFor { get; } = new();

        /// <summary>Task 143: the resolver faults while answering for this record (a Dataverse read failure inside it).</summary>
        public Dictionary<Guid, Exception> FaultFor { get; } = new();

        public List<Ownership.RecordOwnershipContext> Contexts { get; } = new();

        public Task<Ownership.RecordOwnerResolution> ResolveOwnerAsync(Ownership.RecordOwnershipContext context, CancellationToken ct)
        {
            Contexts.Add(context);
            if (context.TargetRecordId is { } faulted && FaultFor.TryGetValue(faulted, out var fault))
                return Task.FromException<Ownership.RecordOwnerResolution>(fault);
            return Task.FromResult(context.TargetRecordId is { } target && AnswerFor.TryGetValue(target, out var answer)
                ? answer
                : Ownership.RecordOwnerResolution.Owned(DefaultTeamId));
        }

        public Task<Guid?> ResolveOwningTeamAsync(Ownership.RecordOwnershipContext context, CancellationToken ct) => throw new NotImplementedException();
        public Task<Ownership.RecordOwnerResolution> ReparentAsync(
            Ownership.RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct) => throw new NotImplementedException();
    }

    /// <summary>The shared app-only reader. Answers the reads the job, evaluator, writer and time-zone helper make; any write
    /// throws and is counted (the evaluation core must never write through it).</summary>
    private sealed class World : IGenericEntityService
    {
        private readonly Dictionary<(string Entity, Guid Id), Entity> _rows = new();
        private readonly List<Entity> _policies = new();
        private readonly Dictionary<string, (Guid Id, string? Display, string? Number)> _catalog = new(StringComparer.Ordinal)
        {
            ["sprk_matter"] = (MatterType, "sprk_mattername", "sprk_matternumber"),
            ["sprk_project"] = (ProjectType, "sprk_projectname", "sprk_projectnumber"),
            ["sprk_workassignment"] = (WorkAssignmentType, "sprk_name", null),
            ["sprk_servicerequest"] = (ServiceRequestType, "sprk_name", "sprk_servicerequestnumber"),
            ["sprk_todo"] = (Guid.Parse("d4a606e6-cf62-f111-ab0c-70a8a58ae145"), "sprk_name", null),
        };
        private readonly Dictionary<Guid, Guid> _userOfContact = new();
        private readonly Dictionary<Guid, int> _zoneOfUser = new();

        /// <summary>Task 153: who last changed a subject row and when (its <c>modifiedby</c> / <c>modifiedon</c>), by subject id.</summary>
        public Dictionary<Guid, (Guid By, DateTime On)> LastChange { get; } = new();

        /// <summary>Task 153: the eligibility columns of the users <see cref="LastChange"/> names (absent: the link finds no user).</summary>
        public Dictionary<Guid, Entity> Users { get; } = new();

        /// <summary>Task 153: the attribution reads the Decision Record writer made (the subject read with its <c>changedby</c> link).</summary>
        public int ChangedByReads { get; private set; }

        /// <summary>Task 153: an enabled, interactive person (access mode Read-Write, no application id).</summary>
        public Guid Person(Guid id)
        {
            Users[id] = new Entity("systemuser", id) { ["isdisabled"] = false, ["accessmode"] = new OptionSetValue(0), ["sprk_isexternal"] = false };
            return id;
        }

        /// <summary>Task 153: an application user (the Spaarke service identity: non-interactive, with an application id).</summary>
        public Guid ApplicationUser(Guid id)
        {
            Users[id] = new Entity("systemuser", id)
            {
                ["isdisabled"] = false,
                ["accessmode"] = new OptionSetValue(4),
                ["applicationid"] = Guid.Parse("170c33c3-0000-4000-8000-0000000000a9"),
            };
            return id;
        }
        private readonly Dictionary<int, string> _zones = new();

        public WriterOrg Org { get; set; } = null!;
        public Dictionary<string, List<Entity>> SubjectRows { get; } = new(StringComparer.Ordinal)
        {
            ["sprk_matter"] = new(),
            ["sprk_project"] = new(),
            ["sprk_todo"] = new(),
        };

        /// <summary>The base rows of the finance rollup (task 025b): <c>sprk_invoice</c> and <c>sprk_budget</c>, each with its
        /// <c>sprk_matter</c> / <c>sprk_project</c> parent lookup. Answered ONLY as the grouped aggregate the evaluator sends.</summary>
        public List<Entity> InvoiceRows { get; } = new();
        public List<Entity> BudgetRows { get; } = new();
        public List<string> AggregateFetches { get; } = new();

        public Dictionary<string, List<Entity>> WitnessRows { get; } = new(StringComparer.Ordinal)
        {
            ["sprk_communication"] = new(),
        };
        public List<string> Fetches { get; } = new();
        public int WriteCalls { get; private set; }

        /// <summary>#1661 round 3 F-T1: Decision Records by id (<see cref="Stub"/> is for the other tables).</summary>
        public Dictionary<Guid, Entity> DecisionRecords { get; } = new();

        /// <summary>#1661: the Secure Record owner team (null: this World has none, so nothing is secure and nothing is walked).</summary>
        public Guid? SecureTeamId { get; private set; }

        /// <summary>Task 145 F1: a witness communication read by id (the evidence family read) faults.</summary>
        public bool FailWitnessReads { get; set; }

        /// <summary>Task 102b fix round 1 (uac-r2 F3): Dataverse ignores the subject-scoped root condition (<see cref="HoldsSubjectId"/>),
        /// so a scoped evaluation returns every subject: the job's fail-closed hit guard must fire.</summary>
        public bool IgnoreSubjectIdCondition { get; set; }

        private static readonly Guid SecureBusinessUnitId = Guid.Parse("5ec0be00-0000-4000-8000-0000000000b1");

        /// <summary>Task 143: this World's Secure Record business unit (answered once <see cref="SecureTeamId"/> exists).</summary>
        public Guid SecureBusinessUnit => SecureBusinessUnitId;

        /// <summary>Task 143: each team's business unit, for the reconciler's guard reads (<c>team</c> by <c>teamid</c>).</summary>
        public Dictionary<Guid, Guid> TeamBusinessUnits { get; } = new()
        {
            [DefaultTeamId] = BusinessUnitId,
            [OtherTeamId] = OtherBusinessUnitId,
        };

        /// <summary>Task 145: the Secure Record owner team exists and <paramref name="projectId"/> is a secure root it owns.</summary>
        public World SecureProject(Guid projectId)
        {
            SecureTeamId = Guid.Parse("5ec0be00-0000-4000-8000-000000000001");
            return Stub(new Entity("sprk_project", projectId) { ["owningteam"] = new EntityReference("team", SecureTeamId.Value), ["sprk_issecure"] = true });
        }

        /// <summary>#1661: the Secure Record owner team exists and <paramref name="matterId"/> is a secure root it owns.</summary>
        public World SecureMatter(Guid matterId)
        {
            SecureTeamId = Guid.Parse("5ec0be00-0000-4000-8000-000000000001");
            return Stub(new Entity("sprk_matter", matterId) { ["owningteam"] = new EntityReference("team", SecureTeamId.Value), ["sprk_issecure"] = true });
        }

        public World Stub(Entity row)
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

        /// <summary>Task 145 F1: the policy is switched Off (sprk_enabled = No) after its Signals were raised.</summary>
        public void SwitchOff(string code) => _policies.Single(p => p.GetAttributeValue<string>("sprk_policycode") == code)["sprk_enabled"] = false;

        /// <summary>Task 145 (uac-r2 F1 on #1874): the current version's rule body stops validating.</summary>
        public void BreakCurrentRuleBody(string code) => _rows[("sprk_policyversion", CurrentVersionOf(code))]["sprk_rulebody"] = "{ \"not\": \"a rule body\" }";

        /// <summary>Task 145 (uac-r2 F1 on #1874): the policy loses its current version pointer.</summary>
        public void ClearCurrentVersion(string code) =>
            _policies.Single(p => p.GetAttributeValue<string>("sprk_policycode") == code).Attributes.Remove("sprk_currentversion");

        public Guid PolicyIdOf(string code) => _policies.Single(p => p.GetAttributeValue<string>("sprk_policycode") == code).Id;

        public void SetCurrentVersionInForceTo(string code, DateTime? inForceTo)
        {
            var version = _rows[("sprk_policyversion", CurrentVersionOf(code))];
            if (inForceTo is { } value) version["sprk_inforceto"] = value; else version.Attributes.Remove("sprk_inforceto");
        }

        public Guid CurrentVersionOf(string code) =>
            _policies.Single(p => p.GetAttributeValue<string>("sprk_policycode") == code).GetAttributeValue<EntityReference>("sprk_currentversion").Id;

        /// <summary>Publishes a new version the way task 101 does: the old version's in-force end stamped, a new current version, the pointer moved.</summary>
        public Guid PublishNewVersion(string code, string body, string template)
        {
            var policy = _policies.Single(p => p.GetAttributeValue<string>("sprk_policycode") == code);
            var oldVersion = policy.GetAttributeValue<EntityReference>("sprk_currentversion").Id;
            _rows[("sprk_policyversion", oldVersion)]["sprk_inforceto"] = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
            var versionId = Guid.NewGuid();
            policy["sprk_currentversion"] = new EntityReference("sprk_policyversion", versionId);
            Stub(new Entity("sprk_policyversion", versionId)
            {
                ["sprk_ruletype"] = new OptionSetValue(Existence),
                ["sprk_rulebody"] = body,
                ["sprk_messagetemplate"] = template,
            });
            return versionId;
        }

        /// <summary>Task 102b: the policy's matter scope (<c>sprk_matter</c>), as a publish sets it; null for all matters.</summary>
        public void SetMatterScope(string code, Guid? matter)
        {
            var policy = _policies.Single(p => p.GetAttributeValue<string>("sprk_policycode") == code);
            if (matter is { } m) policy["sprk_matter"] = new EntityReference("sprk_matter", m); else policy.Attributes.Remove("sprk_matter");
        }

        /// <summary>Off (<c>sprk_enabled</c> No) or Retired (<c>statecode</c> Inactive).</summary>
        public void SetPolicyState(string code, bool enabled, int statecode = 0)
        {
            var policy = _policies.Single(p => p.GetAttributeValue<string>("sprk_policycode") == code);
            policy["sprk_enabled"] = enabled;
            policy["statecode"] = new OptionSetValue(statecode);
        }

        public void AddPolicy(string code, int lane, bool enabled, string body, string template, string? tenant = null, int ruleType = Existence)
        {
            var versionId = Guid.NewGuid();
            var policy = new Entity("sprk_policy", Guid.NewGuid())
            {
                ["sprk_policycode"] = code,
                ["sprk_enabled"] = enabled,
                ["statecode"] = new OptionSetValue(0),
                ["sprk_lane"] = new OptionSetValue(lane),
                ["sprk_severity"] = new OptionSetValue(SignalWriter.SeverityWarning),
                ["sprk_shortname"] = code + " short name",
                ["sprk_currentversion"] = new EntityReference("sprk_policyversion", versionId),
            };
            if (tenant is not null) policy["sprk_tenant"] = tenant;
            _policies.Add(policy);
            Stub(policy);
            Stub(new Entity("sprk_policyversion", versionId)
            {
                ["sprk_ruletype"] = new OptionSetValue(ruleType),
                ["sprk_rulebody"] = body,
                ["sprk_messagetemplate"] = template,
            });
        }

        /// <summary>A contact that represents one enabled user whose Dataverse zone is <paramref name="windowsZoneId"/>.</summary>
        public Guid ContactInZone(string windowsZoneId)
        {
            var contact = Guid.NewGuid();
            var user = Guid.NewGuid();
            var code = 1000 + _zones.Count;
            _zones[code] = windowsZoneId;
            _zoneOfUser[user] = code;
            _userOfContact[contact] = user;
            return contact;
        }

        /// <summary>Gives a systemuser (e.g. an item's owner) a Dataverse zone, to prove it is NOT used as a person's zone.</summary>
        public void SetUserZone(Guid user, string windowsZoneId)
        {
            var code = 1000 + _zones.Count;
            _zones[code] = windowsZoneId;
            _zoneOfUser[user] = code;
        }

        /// <summary>Task 153 (adversarial pass 1 K4): a by-id read that faults, chosen per (table, id).</summary>
        public Func<string, Guid, Exception?>? RetrieveFaultFor { get; set; }

        /// <summary>Task 153 (adversarial pass 1 F1c): a by-id read of a row that does not exist faults as the SDK's ServiceClient does:
        /// <see cref="FaultException{OrganizationServiceFault}"/> with ObjectDoesNotExist (<c>0x80040217</c>). Production never answers an
        /// empty row for a missing id.</summary>
        internal static FaultException<OrganizationServiceFault> DoesNotExist(string table, Guid id) =>
            new(new OrganizationServiceFault { ErrorCode = -2147220969, Message = $"{table} With Id = {id:D} Does Not Exist" },
                new FaultReason($"{table} With Id = {id:D} Does Not Exist"));

        public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default)
        {
            if (RetrieveFaultFor?.Invoke(entityLogicalName, id) is { } readFault) throw readFault;
            var entity = new Entity(entityLogicalName, id);
            if (entityLogicalName == "usersettings" && _zoneOfUser.TryGetValue(id, out var code))
            {
                entity["timezonecode"] = code;
            }
            else if (entityLogicalName == "sprk_recordtype_ref" && _catalog.FirstOrDefault(c => c.Value.Id == id) is { Key: { } table })
            {
                entity["sprk_recordlogicalname"] = table; // task 153: the job's core-type read (the type's table)
            }
            else if (_rows.TryGetValue((entityLogicalName, id), out var row))
            {
                foreach (var attribute in row.Attributes.Where(a => a.Value is not null)) entity[attribute.Key] = attribute.Value;
            }
            else
            {
                throw DoesNotExist(entityLogicalName, id);
            }

            return Task.FromResult(entity);
        }

        /// <summary>#1661 round 2: a read that faults (a Dataverse timeout), chosen per query.</summary>
        public Func<QueryExpression, Exception?>? FaultOn { get; set; }

        /// <summary>Task 102b fix round 4 (K c): a smaller page than the query asks for, so a test can reach a second page or the per-run
        /// bound without seeding thousands of rows. Null: the query's own <c>PageInfo.Count</c>.</summary>
        public int? PageSizeCap { get; set; }

        /// <summary>Task 102b fix round 4 (K c): each paged read answered, as (table, page number).</summary>
        public List<(string Table, int Page)> PagedReads { get; } = new();

        /// <summary>Task 102b fix round 5 (F3): only the columns the query selects come back, as in Dataverse (a column the query leaves out is
        /// absent), plus the primary id, which Dataverse always returns, and the row version the fake carries beside the attributes. A
        /// query that asks for <c>AllColumns</c> gets the whole row. A select that omits a column production reads therefore fails a test.</summary>
        private static Entity Project(QueryExpression query, Entity row)
        {
            if (query.ColumnSet.AllColumns) return row;
            var copy = new Entity(row.LogicalName, row.Id) { RowVersion = row.RowVersion };
            foreach (var attribute in row.Attributes.Where(a => query.ColumnSet.Columns.Contains(a.Key))) copy[attribute.Key] = attribute.Value;
            return copy;
        }

        /// <summary>A query with <c>PageInfo.Count</c> set is answered one page at a time, ordered by id (every paged read here orders by
        /// its primary key), with <c>MoreRecords</c> and a paging cookie as Dataverse sets them; any other query gets every row.</summary>
        private EntityCollection Paged(QueryExpression query, IEnumerable<Entity> rows)
        {
            var ordered = rows.OrderBy(r => r.Id).ToList();
            var result = new EntityCollection();
            if (query.PageInfo is not { Count: > 0 } paging)
            {
                result.Entities.AddRange(ordered.Select(r => Project(query, r)));
                return result;
            }

            var size = PageSizeCap is { } cap ? Math.Min(cap, paging.Count) : paging.Count;
            var page = Math.Max(paging.PageNumber, 1);
            PagedReads.Add((query.EntityName, page));
            result.Entities.AddRange(ordered.Skip((page - 1) * size).Take(size).Select(r => Project(query, r)));
            result.MoreRecords = ordered.Count > page * size;
            result.PagingCookie = result.MoreRecords ? $"<cookie page=\"{page}\" />" : null;
            return result;
        }

        /// <summary>Task 102b fix round 4: the policy row's <c>createdon</c>, as Dataverse stamps it on create.</summary>
        public void SetPolicyCreatedOn(string code, DateTime createdOn) =>
            _policies.Single(p => p.GetAttributeValue<string>("sprk_policycode") == code)["createdon"] = createdOn;

        /// <summary>Task 102b fix rounds 3-4: an administrator's hard delete of the rule, or a matter delete cascading to it. The row is gone,
        /// and <c>sprk_signal_policy</c> (RemoveLink) empties the policy lookup on every one of its Signals; their rows stay.</summary>
        public void DeletePolicy(string code)
        {
            var policy = _policies.Single(p => p.GetAttributeValue<string>("sprk_policycode") == code);
            _policies.Remove(policy);
            _rows.Remove(("sprk_policy", policy.Id));
            foreach (var signal in Org.Rows.Values.Where(r => r.GetAttributeValue<EntityReference>("sprk_policy")?.Id == policy.Id))
            {
                signal.Attributes.Remove("sprk_policy");
            }
        }

        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default)
        {
            if (FaultOn?.Invoke(query) is { } fault) throw fault;
            object? Value(string attribute) => query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == attribute)?.Values[0];
            var result = new EntityCollection();
            switch (query.EntityName)
            {
                // Task 153: the Decision Record writer's attribution read: the subject's modifiedby / modifiedon, and the modifying user's
                // eligibility columns through the left-outer "changedby" link, as aliased values (absent when the link finds no user).
                case var subjectTable when query.LinkEntities.Any(l => l.EntityAlias == "changedby") && Value($"{subjectTable}id") is Guid changedId:
                    ChangedByReads++;
                    if (LastChange.TryGetValue(changedId, out var change))
                    {
                        var changed = new Entity(subjectTable, changedId)
                        {
                            ["modifiedby"] = new EntityReference("systemuser", change.By),
                            ["modifiedon"] = change.On,
                        };
                        if (Users.TryGetValue(change.By, out var user))
                        {
                            foreach (var attribute in user.Attributes)
                                changed[$"changedby.{attribute.Key}"] = new AliasedValue("systemuser", attribute.Key, attribute.Value);
                        }

                        result.Entities.Add(changed);
                    }
                    else if (_rows.ContainsKey((subjectTable, changedId)))
                    {
                        // Adversarial pass 1 F1c: the row exists but names no modifying user this World knows; a missing row answers none.
                        result.Entities.Add(new Entity(subjectTable, changedId));
                    }

                    break;
                // Task 153: the condition-cleared record's key lookup (the subject lookup, then two LIKE tests on sprk_steps), over the
                // records the writer created, as Dataverse answers it.
                case "sprk_decisionrecord" when query.Criteria.Conditions.Any(c => c.Operator == ConditionOperator.Like):
                    foreach (var record in Org.DecisionRecordRows.Values.Where(r => query.Criteria.Conditions.All(c => c.Operator switch
                             {
                                 ConditionOperator.Equal => r.GetAttributeValue<EntityReference>(c.AttributeName)?.Id is { } id && Equals(id, c.Values[0]),
                                 ConditionOperator.Like => r.GetAttributeValue<string>(c.AttributeName)?.Contains(((string)c.Values[0]).Trim('%'), StringComparison.Ordinal) == true,
                                 _ => false,
                             })))
                    {
                        var copy = new Entity("sprk_decisionrecord", record.Id);
                        foreach (var attribute in record.Attributes) copy[attribute.Key] = attribute.Value;
                        result.Entities.Add(copy);
                    }

                    break;
                case "sprk_recordtype_ref" when Value("sprk_recordlogicalname") is string table && _catalog.TryGetValue(table, out var cat):
                    result.Entities.Add(new Entity("sprk_recordtype_ref", cat.Id)
                    {
                        ["sprk_recorddisplaynamefield"] = cat.Display,
                        ["sprk_regardingrecordnumberfield"] = cat.Number,
                    });
                    break;
                case "sprk_policy":
                    // Fix round 4 (K c): a paged read (the existence read) is answered page by page, as Dataverse answers it.
                    result = Paged(query, _policies.Where(p => query.Criteria.Conditions.All(c =>
                                 c.Operator == ConditionOperator.Equal && Matches(p, c.AttributeName, c.Values[0]))));
                    break;
                // Task 102b fix round 3: the deleted-policy sweep's read (sprk_policy is null, Open or Acknowledged), as Dataverse answers it.
                // Fix round 4: and the redaction's read of the codes (sprk_policy is null, ANY status: no status condition); both paged.
                case "sprk_signal" when query.Criteria.Conditions.Any(c => c.AttributeName == "sprk_policy" && c.Operator == ConditionOperator.Null):
                    var sweepStatuses = query.Criteria.Conditions.SingleOrDefault(c => c.AttributeName == "sprk_signalstatus")?.Values.Cast<int>().ToHashSet();
                    result = Paged(query, Org.Rows.Values.Where(r => r.GetAttributeValue<EntityReference>("sprk_policy") is null
                                 && (sweepStatuses is null || sweepStatuses.Contains(r.GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value)))
                        .Select(row =>
                        {
                            var copy = new Entity("sprk_signal", row.Id) { RowVersion = Org.VersionOf(row.Id) };
                            foreach (var attribute in row.Attributes) copy[attribute.Key] = attribute.Value;
                            return copy;
                        }));
                    break;
                case "sprk_signal" when Value("sprk_policy") is Guid policyId:
                    var openStatuses = query.Criteria.Conditions.Single(c => c.AttributeName == "sprk_signalstatus").Values.Cast<int>().ToHashSet();
                    foreach (var row in Org.Rows.Values.Where(r => r.GetAttributeValue<EntityReference>("sprk_policy")?.Id == policyId
                                 && openStatuses.Contains(r.GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value)))
                    {
                        var copy = new Entity("sprk_signal", row.Id) { RowVersion = Org.VersionOf(row.Id) };
                        foreach (var attribute in row.Attributes) copy[attribute.Key] = attribute.Value;
                        result.Entities.Add(Project(query, copy));
                    }

                    break;
                case "sprk_signal" when Value("sprk_dedupekey") is string prefix:
                    foreach (var row in Org.Rows.Values.Where(r => r.GetAttributeValue<string>("sprk_dedupekey")?.StartsWith(prefix, StringComparison.Ordinal) == true))
                    {
                        var copy = new Entity("sprk_signal", row.Id) { RowVersion = Org.VersionOf(row.Id) };
                        foreach (var attribute in row.Attributes) copy[attribute.Key] = attribute.Value;
                        result.Entities.Add(Project(query, copy));
                    }

                    break;
                // Task 143: the reconciler's guard and read-back reads of one Signal (its owner columns and version) and of a team's unit.
                case "sprk_signal" when Value("sprk_signalid") is Guid signalId && Org.Rows.TryGetValue(signalId, out var signalRow):
                    var owned = new Entity("sprk_signal", signalId);
                    foreach (var attribute in signalRow.Attributes) owned[attribute.Key] = attribute.Value;
                    if (signalRow.GetAttributeValue<EntityReference>("ownerid") is { LogicalName: "systemuser" } owningUser)
                        owned["owninguser"] = new EntityReference("systemuser", owningUser.Id);
                    owned["versionnumber"] = long.Parse(Org.VersionOf(signalId), System.Globalization.CultureInfo.InvariantCulture);
                    result.Entities.Add(owned);
                    break;
                case "team" when Value("teamid") is Guid teamId && TeamBusinessUnits.TryGetValue(teamId, out var teamUnit):
                    result.Entities.Add(new Entity("team", teamId) { ["businessunitid"] = new EntityReference("businessunit", teamUnit) });
                    break;
                // #1661: uac-r2's Secure Record owner team rule and its root read (owner team + sprk_issecure), only once a team exists.
                case "businessunit" when SecureTeamId is not null:
                    result.Entities.Add(new Entity("businessunit", SecureBusinessUnitId));
                    break;
                case "team" when SecureTeamId is { } team && Equals(Value("businessunitid"), SecureBusinessUnitId):
                    result.Entities.Add(new Entity("team", team));
                    break;
                case "sprk_matter" when SecureTeamId is not null && query.ColumnSet.Columns.Contains("sprk_issecure")
                                         && Value("sprk_matterid") is Guid rootId && _rows.TryGetValue(("sprk_matter", rootId), out var root):
                    result.Entities.Add(root);
                    break;
                // Task 145: a secure project root (the same root read as a matter's), and a witness communication read by id with its
                // lineage lookups and owner (the evidence filter's ReadSubjectRowsAsync).
                case "sprk_project" when SecureTeamId is not null && query.ColumnSet.Columns.Contains("sprk_issecure")
                                          && Value("sprk_projectid") is Guid projectId && _rows.TryGetValue(("sprk_project", projectId), out var project):
                    result.Entities.Add(project);
                    break;
                case "sprk_communication" when query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_communicationid") is { } byId:
                    if (FailWitnessReads) throw new InvalidOperationException("Simulated failure reading a witness row.");
                    foreach (var witness in WitnessRows["sprk_communication"].Where(w => byId.Values.Contains(w.Id))) result.Entities.Add(witness);
                    break;
                // #1661 round 3 F-T1: the Decision Records the Signals cite, for both reads of them (the lineage walk's, with the lookup
                // columns, and the suppression pass's facts: class, reason, decided-on). A record not stubbed is absent, as in Dataverse.
                case "sprk_decisionrecord":
                    var wanted = query.Criteria.Conditions.Where(c => c.AttributeName == "sprk_decisionrecordid")
                        .SelectMany(c => c.Values).OfType<Guid>().ToHashSet();
                    // Adversarial pass 2: a createdby condition is evaluated (a row with no createdby was not created by that user).
                    var creator = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "createdby")?.Values.Single();
                    // Task 153: and the records the writer created (a condition-cleared closure's, once linked).
                    foreach (var record in DecisionRecords.Values.Concat(Org.DecisionRecordRows.Values).Where(r => wanted.Contains(r.Id)
                                 && (creator is null || Equals(r.GetAttributeValue<EntityReference>("createdby")?.Id, creator))))
                    {
                        var copy = new Entity("sprk_decisionrecord", record.Id);
                        // uac-r2 re-check 2 F3-2: only the selected columns come back, as in Dataverse (a column the query leaves out is absent).
                        foreach (var attribute in record.Attributes.Where(a => query.ColumnSet.AllColumns || query.ColumnSet.Columns.Contains(a.Key)))
                            copy[attribute.Key] = attribute.Value;
                        result.Entities.Add(copy);
                    }

                    break;
                // Task 152 (D-145a): the suppression pass's read of a Do item's person (the "mine" rule's column), by id.
                case "sprk_todo" when query.ColumnSet.Columns.SequenceEqual(["sprk_assignedto"])
                                      && query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_todoid") is { } todoIds:
                    foreach (var todo in SubjectRows["sprk_todo"].Where(t => todoIds.Values.Contains(t.Id))) result.Entities.Add(todo);
                    break;
                case "timezonedefinition" when Value("timezonecode") is int zoneCode && _zones.TryGetValue(zoneCode, out var name):
                    result.Entities.Add(new Entity("timezonedefinition", Guid.NewGuid()) { ["standardname"] = name });
                    break;
                case "systemuser" when Value("sprk_primarycontact") is Guid contact && _userOfContact.TryGetValue(contact, out var user):
                    result.Entities.Add(new Entity("systemuser", user));
                    break;
            }

            return Task.FromResult(result);
        }

        /// <summary>
        /// The subject's OWN numeric comparisons (task 025): a root-filter <c>ge/gt/le/lt/eq</c> condition with a numeric
        /// value must hold for the row's value (a Money or decimal; a null never satisfies it, as in Dataverse). Every other
        /// condition (dates, ids, joins) is left to the test's rows, as before this task.
        /// </summary>
        private static bool HoldsNumericConditions(Entity row, XElement entity) =>
            (entity.Element("filter")?.Elements("condition") ?? Enumerable.Empty<XElement>())
            .Where(c => c.Attribute("entityname") is null
                        && decimal.TryParse((string?)c.Attribute("value"), System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture, out _))
            .All(c =>
            {
                var expected = decimal.Parse((string)c.Attribute("value")!, System.Globalization.CultureInfo.InvariantCulture);
                var op = (string?)c.Attribute("operator");
                var present = row.Attributes.TryGetValue((string)c.Attribute("attribute")!, out var v);

                // Only a numeric column is compared. An option set, lookup or string column (statecode eq 0) is not modelled
                // here and never excludes a row; a missing value never satisfies an ordered comparison (Dataverse: null).
                if (present && v is not (Money or decimal)) return true;
                if (!present) return op == "eq";
                decimal? actual = v switch { Money m => m.Value, decimal d => d, _ => null };
                return actual is { } a && op switch
                {
                    "ge" => a >= expected,
                    "gt" => a > expected,
                    "le" => a <= expected,
                    "lt" => a < expected,
                    "eq" => a == expected,
                    _ => true,
                };
            });

        /// <summary>
        /// Task 061 (D-54): the subject's OWN null tests, as Dataverse answers them: <c>null</c> holds when the column has no value,
        /// <c>not-null</c> when it has one. Only a condition WITHOUT an <c>entityname</c> is the subject's: the notExists anti-join's
        /// own <c>null</c> carries the link alias and is modelled by the test's rows, so it must not be read as a column test here.
        /// </summary>
        private static bool HoldsNullTests(Entity row, XElement entity) =>
            (entity.Element("filter")?.Elements("condition") ?? Enumerable.Empty<XElement>())
            .Where(c => c.Attribute("entityname") is null && (string?)c.Attribute("operator") is "null" or "not-null")
            .All(c =>
            {
                var hasValue = row.Attributes.TryGetValue((string)c.Attribute("attribute")!, out var v) && v is not null;
                return (string)c.Attribute("operator")! == "null" ? !hasValue : hasValue;
            });

        /// <summary>
        /// Task 102b: the subject-scoped evaluation's root condition (<c>{subject}id eq id</c>, PredicateCompiler; an event trigger's
        /// subject or a policy's matter scope), as Dataverse answers it: only that row. Before 102b every scoped test had one subject.
        /// </summary>
        private static bool HoldsSubjectId(Entity row, XElement entity, string subject) =>
            (entity.Element("filter")?.Elements("condition") ?? Enumerable.Empty<XElement>())
            .Where(c => c.Attribute("entityname") is null && (string?)c.Attribute("attribute") == subject + "id" && (string?)c.Attribute("operator") == "eq")
            .All(c => Guid.Parse((string)c.Attribute("value")!) == row.Id);

        private static bool Matches(Entity row, string attribute, object expected) => row.Attributes.TryGetValue(attribute, out var actual) && actual switch
        {
            OptionSetValue o => Equals(o.Value, expected),
            _ => Equals(actual, expected),
        };

        /// <summary>Answers the evaluator's grouped aggregate over invoices or budgets the way Dataverse does: rows of the given
        /// parents (and, for invoices, of the listed statuses) summed per parent, the parent and the sum as aliased values. Anything
        /// that is not a grouped sum over an <c>in</c> list of parents is refused, so a per-parent query fails the test.</summary>
        private EntityCollection Aggregate(string name, XElement entity, string query)
        {
            AggregateFetches.Add(query);
            if ((string?)XElement.Parse(query).Attribute("aggregate") != "true") throw new InvalidOperationException("Expected an aggregate query.");
            var group = entity.Elements("attribute").Single(a => (string?)a.Attribute("groupby") == "true");
            var sum = entity.Elements("attribute").Single(a => (string?)a.Attribute("aggregate") == "sum");
            var lookup = (string)group.Attribute("name")!;
            var sumColumn = (string)sum.Attribute("name")!;
            var conditions = entity.Element("filter")!.Elements("condition").ToList();
            var parents = conditions.Single(c => (string)c.Attribute("attribute")! == lookup && (string)c.Attribute("operator")! == "in")
                .Elements("value").Select(v => Guid.Parse(v.Value)).ToHashSet();
            var statuses = conditions.SingleOrDefault(c => (string)c.Attribute("attribute")! == "sprk_invoicestatus")
                ?.Elements("value").Select(v => int.Parse(v.Value)).ToHashSet();

            var result = new EntityCollection();
            var rows = (name == "sprk_invoice" ? InvoiceRows : BudgetRows).Where(r =>
                r.GetAttributeValue<EntityReference>(lookup) is { } p && parents.Contains(p.Id)
                && (statuses is null || r.GetAttributeValue<OptionSetValue>("sprk_invoicestatus") is { } s && statuses.Contains(s.Value)));
            foreach (var byParent in rows.GroupBy(r => r.GetAttributeValue<EntityReference>(lookup)!.Id))
            {
                result.Entities.Add(new Entity(name)
                {
                    ["parent"] = new AliasedValue(name, lookup, new EntityReference(lookup, byParent.Key)),
                    ["total"] = new AliasedValue(name, sumColumn, new Money(byParent.Sum(r => r.GetAttributeValue<Money>(sumColumn)?.Value ?? 0m))),
                });
            }

            return result;
        }

        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default)
        {
            var entity = XElement.Parse(fetch.Query).Element("entity")!;
            var name = entity.Attribute("name")!.Value;
            if (name is "sprk_invoice" or "sprk_budget") return Task.FromResult(Aggregate(name, entity, fetch.Query));
            Fetches.Add(fetch.Query);
            var result = new EntityCollection();
            if (SubjectRows.TryGetValue(name, out var subjects))
            {
                foreach (var row in subjects.Where(r => HoldsNumericConditions(r, entity) && HoldsNullTests(r, entity) && (IgnoreSubjectIdCondition || HoldsSubjectId(r, entity, name))
                             && InnerLinksSatisfied(entity, r))) result.Entities.Add(row);
            }
            else if (WitnessRows.TryGetValue(name, out var witnesses))
            {
                var join = entity.Element("filter")!.Elements("condition").First(c => c.Attribute("operator")!.Value == "in");
                var path = join.Attribute("attribute")!.Value;
                var ids = join.Elements("value").Select(v => Guid.Parse(v.Value)).ToHashSet();
                foreach (var row in witnesses.Where(w => ids.Contains(w.GetAttributeValue<EntityReference>(path)!.Id))) result.Entities.Add(row);
            }

            return Task.FromResult(result);
        }

        /// <summary>An inner link-entity keeps a subject only when a linked witness row satisfies the link's date bounds (what the
        /// FetchXML join does in Dataverse); other link conditions are not modelled.</summary>
        private bool InnerLinksSatisfied(XElement entity, Entity subject)
        {
            foreach (var link in entity.Elements("link-entity").Where(l => l.Attribute("link-type")?.Value == "inner"))
            {
                if (!WitnessRows.TryGetValue(link.Attribute("name")!.Value, out var witnesses)) continue;
                var from = link.Attribute("from")!.Value;
                var bounds = (link.Element("filter")?.Elements("condition") ?? Enumerable.Empty<XElement>())
                    .Where(c => c.Attribute("value") is not null && c.Attribute("operator")!.Value is "ge" or "gt" or "le" or "lt")
                    .ToList();
                var any = witnesses.Any(w => w.GetAttributeValue<EntityReference>(from)?.Id == subject.Id && bounds.All(c =>
                {
                    var actual = w.GetAttributeValue<DateTime?>(c.Attribute("attribute")!.Value);
                    if (actual is null) return false;
                    var expected = DateTime.Parse(c.Attribute("value")!.Value, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
                    return c.Attribute("operator")!.Value switch
                    {
                        "ge" => actual >= expected,
                        "gt" => actual > expected,
                        "le" => actual <= expected,
                        _ => actual < expected,
                    };
                }));
                if (!any) return false;
            }

            return true;
        }

        private Exception Write()
        {
            WriteCalls++;
            return new InvalidOperationException("The shared app-only reader must never write in task 031.");
        }

        public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default) => throw Write();
        public Task<(Guid Id, bool Created)> UpsertAsync(Entity entity, CancellationToken ct = default) => throw Write();
        public Task UpdateIfRowVersionMatchesAsync(string entityLogicalName, Guid id, string rowVersion, Dictionary<string, object> fields, CancellationToken ct = default) => throw Write();
        public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default) => throw Write();
        public Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default) => throw Write();
        public Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default) => throw Write();
        public Task AssociateAsync(string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct = default) => throw Write();
        public Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
    }

    /// <summary>
    /// The writer's own Dataverse seam (the SDK's <see cref="IOrganizationServiceAsync2"/>): an alternate-key-unique store of
    /// <c>sprk_signal</c> rows with a row version per row, so a conditional update can be refused as Dataverse refuses it.
    /// </summary>
    private sealed class WriterOrg : IOrganizationServiceAsync2
    {
        public readonly Dictionary<Guid, Entity> Rows = new();

        /// <summary>Task 153: the Decision Records created through the writer's client, by id (kept apart from the Signal rows, whose
        /// counts and alternate key they do not touch).</summary>
        public readonly Dictionary<Guid, Entity> DecisionRecordRows = new();

        /// <summary>Adversarial pass 2 F1: the ontology writer's own application user, as WhoAmI on its connection answers. Dataverse stamps
        /// it in <c>createdby</c> on every row the writer creates.</summary>
        public static readonly Guid WriterUserId = Guid.Parse("3121bf1b-9fbf-f111-aaaf-0022482913fc");
        private readonly Dictionary<string, Guid> _byKey = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, long> _versions = new();

        public List<Entity> CreatedPayloads { get; } = new();
        public List<(Guid Id, Dictionary<string, object> Fields)> Updates { get; } = new();
        public List<(Guid Id, Dictionary<string, object> Fields)> ConditionalUpdates { get; } = new();
        public int CreateCalls { get; private set; }
        public int WriteCalls => CreateCalls + Updates.Count + ConditionalUpdates.Count;

        /// <summary>Signals a person resolves (Dismissed, version bumped) just before the writer's conditional update reaches
        /// Dataverse, i.e. after the job read them: the row-version check, not a status guard, must refuse the close.</summary>
        public HashSet<Guid> ResolveByPersonBeforeConditionalUpdate { get; } = new();

        public bool FailConditionalUpdates { get; set; }
        public bool FailCreates { get; set; }

        /// <summary>K1: when set, every create throws exactly this (a typed Dataverse fault, say), instead of the plain failure.</summary>
        public Exception? CreateFault { get; set; }

        /// <summary>K1: like <see cref="CreateFault"/>, but chosen per create by its payload (null: the create succeeds).</summary>
        public Func<Entity, Exception?>? CreateFaultFor { get; set; }

        /// <summary>K1: when set, every conditional update (a close, a stamp) throws exactly this.</summary>
        public Exception? ConditionalUpdateFault { get; set; }

        /// <summary>Task 153: like <see cref="ConditionalUpdateFault"/>, but chosen per update by its fields (null: the update proceeds).</summary>
        public Func<Entity, Exception?>? ConditionalUpdateFaultFor { get; set; }

        /// <summary>K1: when set, every row read through the writer's client throws exactly this (the owner read-back after a create).</summary>
        public Exception? RetrieveFault { get; set; }

        /// <summary>Task 153 (adversarial pass 1 F2): Dataverse's clock, stamped as <c>createdon</c> on each Decision Record created here.</summary>
        public Func<DateTime>? Now { get; set; }

        /// <summary>Task 153 (adversarial pass 1 F2): another writer changes the row (its version moves) after the conditional update's read
        /// and before it lands, for the updates this picks (by their fields).</summary>
        public Func<Entity, bool>? TouchBeforeConditionalUpdateFor { get; set; }

        public void ResetCounts()
        {
            CreateCalls = 0;
            CreatedPayloads.Clear();
            Updates.Clear();
            ConditionalUpdates.Clear();
        }

        public string VersionOf(Guid id) => _versions[id].ToString();

        /// <summary>Task 143: another writer changed the row (its version moves, its owner does not).</summary>
        public void Touch(Guid id) => _versions[id]++;

        /// <summary>Task 143: the reconciler's owner write landing (Dataverse derives owningteam; the version moves).</summary>
        public void Reown(Guid id, Guid teamId)
        {
            Apply(id, new Dictionary<string, object> { ["ownerid"] = new EntityReference("team", teamId) });
            Rows[id]["owningteam"] = new EntityReference("team", teamId);
            Rows[id]["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitOfTeam(teamId)); // Dataverse derives it
            Rows[id].Attributes.Remove("owninguser");
            Updates.Add((id, new Dictionary<string, object> { ["ownerid"] = new EntityReference("team", teamId) }));
        }

        public Guid SeedSignal(string key, int status, int? resolution = null, DateTime? resolvedOn = null, Guid? coreId = null,
            DateTime? suppressedUntil = null, DateTime? due = null, Guid? policyId = null, Guid? versionId = null,
            DateTime? lastEvaluated = null)
        {
            var id = Guid.NewGuid();
            var row = new Entity("sprk_signal", id)
            {
                ["sprk_dedupekey"] = key,
                ["sprk_signalstatus"] = new OptionSetValue(status),
                // Task 143 (D-130): every Signal is owned by the resolver's team; the unit derives from it.
                ["ownerid"] = new EntityReference("team", DefaultTeamId),
                ["owningteam"] = new EntityReference("team", DefaultTeamId),
                ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
            };
            if (policyId is { } pid) row["sprk_policy"] = new EntityReference("sprk_policy", pid);
            if (versionId is { } vid) row["sprk_policyversion"] = new EntityReference("sprk_policyversion", vid);
            if (resolution is { } r) row["sprk_resolutiontype"] = new OptionSetValue(r);
            if (resolvedOn is { } on) row["sprk_resolvedon"] = on;
            if (suppressedUntil is { } until) row["sprk_suppresseduntil"] = until;
            if (lastEvaluated is { } evaluated) row["sprk_lastevaluated"] = evaluated;
            if (due is { } d) row["sprk_duedate"] = d;
            if (coreId is { } c)
            {
                row["sprk_corerecordid"] = Lower(c);
                row["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", MatterType);
            }

            Rows[id] = row;
            _byKey[key] = id;
            _versions[id] = 1;
            return id;
        }

        public Task<Guid> CreateAsync(Entity entity, CancellationToken cancellationToken)
        {
            CreateCalls++;
            if (CreateFault is not null)
            {
                throw CreateFault;
            }

            if (CreateFaultFor?.Invoke(entity) is { } createFault)
            {
                throw createFault;
            }

            if (FailCreates)
            {
                throw new InvalidOperationException("Simulated Dataverse failure on the create.");
            }

            if (entity.LogicalName == "sprk_decisionrecord")
            {
                CreateCalls--; // task 153: a Decision Record, not a Signal create
                var record = new Entity("sprk_decisionrecord", Guid.NewGuid());
                foreach (var attribute in entity.Attributes) record[attribute.Key] = attribute.Value;
                if (Now is { } now) record["createdon"] = now(); // Dataverse stamps it
                record["createdby"] = new EntityReference("systemuser", WriterUserId); // and the creator: the writer, no impersonation
                if (record.GetAttributeValue<EntityReference>("ownerid") is { LogicalName: "team" } recordTeam)
                    record["owningteam"] = new EntityReference("team", recordTeam.Id);
                DecisionRecordRows[record.Id] = record;
                return Task.FromResult(record.Id);
            }

            var key = entity.GetAttributeValue<string>("sprk_dedupekey")!;
            if (_byKey.ContainsKey(key))
            {
                throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { ErrorCode = unchecked((int)0x80060892), Message = "Entity Key sprk_dedupekey already exists (duplicate)." },
                    "Entity Key sprk_dedupekey already exists (duplicate).");
            }

            var payload = new Entity(entity.LogicalName);
            foreach (var attribute in entity.Attributes) payload[attribute.Key] = attribute.Value;
            CreatedPayloads.Add(payload);

            var id = Guid.NewGuid();
            var row = new Entity("sprk_signal", id);
            foreach (var attribute in entity.Attributes) row[attribute.Key] = attribute.Value;
            if (row.GetAttributeValue<EntityReference>("ownerid") is { LogicalName: "team" } team)
            {
                row["owningteam"] = new EntityReference("team", team.Id);
                row["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitOfTeam(team.Id));
            }

            Rows[id] = row;
            _byKey[key] = id;
            _versions[id] = 1;
            return Task.FromResult(id);
        }

        public Task<Entity> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet, CancellationToken cancellationToken)
        {
            if (RetrieveFault is not null) throw RetrieveFault;
            if (entityName == "sprk_decisionrecord") return Task.FromResult(DecisionRecordRows[id]);

            // Task 153: a Signal read carries its version, as Dataverse returns it (the link is conditional on it).
            var copy = new Entity(entityName, id) { RowVersion = VersionOf(id) };
            foreach (var attribute in Rows[id].Attributes) copy[attribute.Key] = attribute.Value;
            copy["versionnumber"] = _versions[id];
            return Task.FromResult(copy);
        }

        public Task<OrganizationResponse> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken)
        {
            switch (request)
            {
                case WhoAmIRequest:
                    return Task.FromResult<OrganizationResponse>(new WhoAmIResponse { Results = { ["UserId"] = WriterUserId } });
                case RetrieveRequest retrieve:
                    var response = new RetrieveResponse();
                    response.Results["Entity"] = Rows[_byKey[(string)retrieve.Target.KeyAttributes["sprk_dedupekey"]]];
                    return Task.FromResult<OrganizationResponse>(response);
                case UpdateRequest { ConcurrencyBehavior: ConcurrencyBehavior.IfRowVersionMatches } update:
                    var target = update.Target;
                    if (ConditionalUpdateFault is not null)
                    {
                        throw ConditionalUpdateFault;
                    }

                    if (ConditionalUpdateFaultFor?.Invoke(target) is { } conditionalFault)
                    {
                        throw conditionalFault;
                    }

                    if (FailConditionalUpdates)
                    {
                        throw new InvalidOperationException("Simulated Dataverse failure on the conditional update.");
                    }

                    if (TouchBeforeConditionalUpdateFor?.Invoke(target) == true)
                    {
                        _versions[target.Id]++;
                    }

                    if (ResolveByPersonBeforeConditionalUpdate.Remove(target.Id))
                    {
                        Rows[target.Id]["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
                        Rows[target.Id]["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionDismissed);
                        _versions[target.Id]++;
                    }

                    if (target.RowVersion != _versions[target.Id].ToString())
                    {
                        throw new FaultException<OrganizationServiceFault>(
                            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80060882), Message = "The version of the existing record doesn't match." },
                            "The version of the existing record doesn't match.");
                    }

                    var fields = target.Attributes.ToDictionary(a => a.Key, a => a.Value);
                    ConditionalUpdates.Add((target.Id, fields));
                    Apply(target.Id, fields);
                    return Task.FromResult<OrganizationResponse>(new UpdateResponse());
                default:
                    throw new NotImplementedException(request.GetType().Name);
            }
        }

        public Task UpdateAsync(Entity entity, CancellationToken cancellationToken)
        {
            var fields = entity.Attributes.ToDictionary(a => a.Key, a => a.Value);
            Updates.Add((entity.Id, fields));
            Apply(entity.Id, fields);
            return Task.CompletedTask;
        }

        private void Apply(Guid id, Dictionary<string, object> fields)
        {
            foreach (var (key, value) in fields) Rows[id][key] = value;
            _versions[id]++;
        }

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

    /// <summary>
    /// Task 143: the reconciler's Web API seam (<see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>), applied to <see cref="WriterOrg"/>:
    /// the If-Match version must be the row's current one (else 412, <see cref="System.Data.DBConcurrencyException"/>), and the
    /// payload must be the owner bind alone.
    /// </summary>
    /// <summary>The unit a team's row derives (the teams these doubles know).</summary>
    private static Guid BusinessUnitOfTeam(Guid teamId) =>
        teamId == SecureTeamId ? SecureBusinessUnitId : teamId == OtherTeamId ? OtherBusinessUnitId : BusinessUnitId;

    private sealed class ReownWebApi : DataverseWebApiClient
    {
        private readonly WriterOrg _org;

        public ReownWebApi(WriterOrg org)
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                new NoCredential())
            => _org = org;

        public List<(string EntitySet, Guid Id, string ETag)> ConditionalWrites { get; } = new();

        /// <summary>Another writer changes the row between the reconciler's guard read and its write (the version moves).</summary>
        public bool ChangeTheRowBeforeTheWrite { get; set; }

        /// <summary>Dataverse refuses the write (e.g. F2's 0x80040299: the target team lacks Read on the table).</summary>
        public Exception? WriteFault { get; set; }

        public override Task UpdateIfMatchAsync(string entitySetName, Guid id, object entity, string etag, CancellationToken cancellationToken = default)
        {
            ConditionalWrites.Add((entitySetName, id, etag));
            if (WriteFault is { } fault)
                return Task.FromException(fault);
            if (ChangeTheRowBeforeTheWrite && _org.Rows.ContainsKey(id))
                _org.Touch(id);
            if (!_org.Rows.ContainsKey(id))
                throw new KeyNotFoundException($"{entitySetName}({id}) was not found.");
            if (etag != $"W/\"{_org.VersionOf(id)}\"")
                throw new System.Data.DBConcurrencyException($"{entitySetName}({id}) changed since it was read.");
            var fields = (IDictionary<string, object>)entity;
            fields.Keys.Should().Equal(new[] { "ownerid@odata.bind" }, "the re-own writes the owner and nothing else");
            var bind = (string)fields["ownerid@odata.bind"];
            _org.Reown(id, Guid.Parse(bind["/teams(".Length..^1]));
            return Task.CompletedTask;
        }

        public override Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("the ordinary re-own is always conditional");
    }

    private sealed class NoCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This double issues no HTTP.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This double issues no HTTP.");
    }
}
