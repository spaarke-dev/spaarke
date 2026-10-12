using System.Diagnostics.Metrics;
using System.ServiceModel;
using System.Xml.Linq;
using FluentAssertions;
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

    private const string PolicyCode = "POL-COMMIT-BUDGET";
    private const string TodoPolicyCode = "POL-TODO-OVERDUE";
    private const int Existence = 100000002;

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
    public async Task Run_DoLanePolicy_IsEvaluated_AndAnOverdueToDoWithNoCoreRecord_IsOwnerOnly()
    {
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ResultJson.Should().Contain("\"doLanePolicies\":1");
        var created = h.Org.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("systemuser", UserId));
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
            .Which.GetAttributeValue<EntityReference>("owningbusinessunit").Should().BeEquivalentTo(new EntityReference("businessunit", BusinessUnitId));
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
        metrics.Counter("ontology.writer.failures", "reason", OntologyWriterFailureReason.DedupeKeyUnparseable).Should().BeGreaterThanOrEqualTo(1);
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
    public async Task Run_NoCoreToDoReassigned_ReassignsItsSignalToTheCurrentOwner_Once()
    {
        var h = new Harness().WithTodoPolicy().WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId));
        await h.RunAsync();
        h.Sysadmin.Stub(new Entity("sprk_todo", TodoId) { ["ownerid"] = new EntityReference("systemuser", OtherUserId) });
        h.Org.ResetCounts();

        var second = await h.RunAsync();
        h.Org.ResetCounts();
        var third = await h.RunAsync();

        second.ResultJson.Should().Contain("\"reassigned\":1");
        h.Org.Rows.Values.Single().GetAttributeValue<EntityReference>("ownerid")!.Id.Should().Be(OtherUserId);
        third.Success.Should().BeTrue();
        h.Org.WriteCalls.Should().Be(0, "logged once: the next pass sees no drift");
        h.Log.Count(e => e.EventId.Id == OntologyWriterEvents.NoCoreSignalReassigned.Id).Should().Be(1);
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
    public async Task Evaluate_DateOnlyBound_IsWidenedInTheQuery_SoAnItemNearMidnightIsNotLeftOut()
    {
        var h = new Harness().WithTodoPolicy();

        await h.Evaluator.EvaluateAsync(h.TodoPolicy(), subjectId: null);

        var conditions = XElement.Parse(h.Sysadmin.Fetches.Single()).Element("entity")!.Element("filter")!.Elements("condition")
            .Select(c => (c.Attribute("attribute")!.Value, c.Attribute("operator")!.Value, c.Attribute("value")!.Value)).ToList();
        conditions.Should().Equal(("sprk_duedate", "le", "2026-10-12T03:00:00Z"));
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
            : new SignalCoreRecord(null, Guid.Empty, null, new EntityReference("systemuser", UserId)),
        null, due, new Dictionary<string, object?>(), Array.Empty<SignalEvidenceRef>());

    private static ExistingSignal Existing(
        SignalHit hit, int episode, int status, int? resolution = null, DateTime? resolvedOn = null,
        DateTime? suppressedUntil = null, DateTime? due = null) => new(
        Guid.NewGuid(), "1", hit.SubjectEntity, hit.SubjectId, episode, status, resolution, resolvedOn, suppressedUntil, due,
        hit.Core!.TypeRefId, hit.Core.IsNoCore ? null : hit.Core.Id, hit.Core.NoCoreOwner ?? new EntityReference("systemuser", UserId));

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

    private sealed class Harness
    {
        public FakeTimeProvider Clock { get; }
        public World Sysadmin { get; } = new();
        public WriterOrg Org { get; } = new();
        public FakeOwnership Ownership { get; } = new();
        public IDistributedCache Cache { get; } = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        public List<LogEntry> Log { get; } = new();
        public SignalWriter Writer { get; }
        public SignalEvaluator Evaluator { get; }
        public SignalReevaluationJob Job { get; }
        public SignalClosureService Closure { get; }
        public SignalSuppressionService Suppression { get; }

        public Harness(DateTimeOffset? runAt = null, string? writerClientId = null)
        {
            Clock = new FakeTimeProvider(runAt ?? RunAt);
            Sysadmin.Org = Org;
            var writerClient = writerClientId is null
                ? new OntologyWriterDataverseClient(() => Org, Logger<OntologyWriterDataverseClient>())
                : new OntologyWriterDataverseClient(
                    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dataverse:ServiceUrl"] = "https://spaarkedev1.crm.dynamics.com",
                        ["Ontology:Writer:ManagedIdentityClientId"] = writerClientId,
                    }).Build(),
                    Logger<OntologyWriterDataverseClient>());
            Writer = new SignalWriter(writerClient, Sysadmin,
                new Ownership.CoreAncestorResolver(Sysadmin, StampColumnProbe, NullLogger<Ownership.CoreAncestorResolver>.Instance),
                Ownership, Clock, Logger<SignalWriter>());
            var schema = new RuleBodySchemaValidator();
            var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, Clock), Logger<PolicyVersionValidator>());
            Evaluator = new SignalEvaluator(validator, Sysadmin, Writer, Logger<SignalEvaluator>());
            Closure = new SignalClosureService(Writer, Sysadmin, Clock, Logger<SignalClosureService>());
            Suppression = new SignalSuppressionService(Sysadmin, writerClient, Clock, Logger<SignalSuppressionService>());
            Job = new SignalReevaluationJob(Evaluator, Writer, Suppression, Closure, Sysadmin, Cache, Clock, Logger<SignalReevaluationJob>());
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

    internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message);

    private sealed class SinkLogger<T>(List<LogEntry> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (sink) sink.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
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
        public List<Ownership.RecordOwnershipContext> Contexts { get; } = new();

        public Task<Ownership.RecordOwnerResolution> ResolveOwnerAsync(Ownership.RecordOwnershipContext context, CancellationToken ct)
        {
            Contexts.Add(context);
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
        private readonly Dictionary<int, string> _zones = new();

        public WriterOrg Org { get; set; } = null!;
        public Dictionary<string, List<Entity>> SubjectRows { get; } = new(StringComparer.Ordinal)
        {
            ["sprk_matter"] = new(),
            ["sprk_todo"] = new(),
        };
        public Dictionary<string, List<Entity>> WitnessRows { get; } = new(StringComparer.Ordinal)
        {
            ["sprk_communication"] = new(),
        };
        public List<string> Fetches { get; } = new();
        public int WriteCalls { get; private set; }

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

        /// <summary>Off (<c>sprk_enabled</c> No) or Retired (<c>statecode</c> Inactive).</summary>
        public void SetPolicyState(string code, bool enabled, int statecode = 0)
        {
            var policy = _policies.Single(p => p.GetAttributeValue<string>("sprk_policycode") == code);
            policy["sprk_enabled"] = enabled;
            policy["statecode"] = new OptionSetValue(statecode);
        }

        public void AddPolicy(string code, int lane, bool enabled, string body, string template, string? tenant = null)
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
                ["sprk_ruletype"] = new OptionSetValue(Existence),
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

        public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default)
        {
            var entity = new Entity(entityLogicalName, id);
            if (entityLogicalName == "usersettings" && _zoneOfUser.TryGetValue(id, out var code))
            {
                entity["timezonecode"] = code;
            }
            else if (_rows.TryGetValue((entityLogicalName, id), out var row))
            {
                foreach (var attribute in row.Attributes.Where(a => a.Value is not null)) entity[attribute.Key] = attribute.Value;
            }

            return Task.FromResult(entity);
        }

        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default)
        {
            object? Value(string attribute) => query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == attribute)?.Values[0];
            var result = new EntityCollection();
            switch (query.EntityName)
            {
                case "sprk_recordtype_ref" when Value("sprk_recordlogicalname") is string table && _catalog.TryGetValue(table, out var cat):
                    result.Entities.Add(new Entity("sprk_recordtype_ref", cat.Id)
                    {
                        ["sprk_recorddisplaynamefield"] = cat.Display,
                        ["sprk_regardingrecordnumberfield"] = cat.Number,
                    });
                    break;
                case "sprk_policy":
                    foreach (var p in _policies.Where(p => query.Criteria.Conditions.All(c =>
                                 c.Operator == ConditionOperator.Equal && Matches(p, c.AttributeName, c.Values[0]))))
                    {
                        result.Entities.Add(p);
                    }

                    break;
                case "sprk_signal" when Value("sprk_policy") is Guid policyId:
                    var openStatuses = query.Criteria.Conditions.Single(c => c.AttributeName == "sprk_signalstatus").Values.Cast<int>().ToHashSet();
                    foreach (var row in Org.Rows.Values.Where(r => r.GetAttributeValue<EntityReference>("sprk_policy")?.Id == policyId
                                 && openStatuses.Contains(r.GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value)))
                    {
                        var copy = new Entity("sprk_signal", row.Id) { RowVersion = Org.VersionOf(row.Id) };
                        foreach (var attribute in row.Attributes) copy[attribute.Key] = attribute.Value;
                        result.Entities.Add(copy);
                    }

                    break;
                case "sprk_signal" when Value("sprk_dedupekey") is string prefix:
                    foreach (var row in Org.Rows.Values.Where(r => r.GetAttributeValue<string>("sprk_dedupekey")?.StartsWith(prefix, StringComparison.Ordinal) == true))
                    {
                        var copy = new Entity("sprk_signal", row.Id) { RowVersion = Org.VersionOf(row.Id) };
                        foreach (var attribute in row.Attributes) copy[attribute.Key] = attribute.Value;
                        result.Entities.Add(copy);
                    }

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

        private static bool Matches(Entity row, string attribute, object expected) => row.Attributes.TryGetValue(attribute, out var actual) && actual switch
        {
            OptionSetValue o => Equals(o.Value, expected),
            _ => Equals(actual, expected),
        };

        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default)
        {
            Fetches.Add(fetch.Query);
            var entity = XElement.Parse(fetch.Query).Element("entity")!;
            var name = entity.Attribute("name")!.Value;
            var result = new EntityCollection();
            if (SubjectRows.TryGetValue(name, out var subjects))
            {
                foreach (var row in subjects.Where(r => InnerLinksSatisfied(entity, r))) result.Entities.Add(row);
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

        public void ResetCounts()
        {
            CreateCalls = 0;
            CreatedPayloads.Clear();
            Updates.Clear();
            ConditionalUpdates.Clear();
        }

        public string VersionOf(Guid id) => _versions[id].ToString();

        public Guid SeedSignal(string key, int status, int? resolution = null, DateTime? resolvedOn = null, Guid? coreId = null,
            DateTime? suppressedUntil = null, DateTime? due = null, Guid? policyId = null, Guid? versionId = null)
        {
            var id = Guid.NewGuid();
            var row = new Entity("sprk_signal", id)
            {
                ["sprk_dedupekey"] = key,
                ["sprk_signalstatus"] = new OptionSetValue(status),
                ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
            };
            if (policyId is { } pid) row["sprk_policy"] = new EntityReference("sprk_policy", pid);
            if (versionId is { } vid) row["sprk_policyversion"] = new EntityReference("sprk_policyversion", vid);
            if (resolution is { } r) row["sprk_resolutiontype"] = new OptionSetValue(r);
            if (resolvedOn is { } on) row["sprk_resolvedon"] = on;
            if (suppressedUntil is { } until) row["sprk_suppresseduntil"] = until;
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
            if (FailCreates)
            {
                throw new InvalidOperationException("Simulated Dataverse failure on the create.");
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
                row["owningbusinessunit"] = new EntityReference("businessunit", SecureBusinessUnitId);
            }

            Rows[id] = row;
            _byKey[key] = id;
            _versions[id] = 1;
            return Task.FromResult(id);
        }

        public Task<Entity> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet, CancellationToken cancellationToken) =>
            Task.FromResult(Rows[id]);

        public Task<OrganizationResponse> ExecuteAsync(OrganizationRequest request, CancellationToken cancellationToken)
        {
            switch (request)
            {
                case RetrieveRequest retrieve:
                    var response = new RetrieveResponse();
                    response.Results["Entity"] = Rows[_byKey[(string)retrieve.Target.KeyAttributes["sprk_dedupekey"]]];
                    return Task.FromResult<OrganizationResponse>(response);
                case UpdateRequest { ConcurrencyBehavior: ConcurrencyBehavior.IfRowVersionMatches } update:
                    var target = update.Target;
                    if (FailConditionalUpdates)
                    {
                        throw new InvalidOperationException("Simulated Dataverse failure on the conditional update.");
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
}
