using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Domain.Signals.Billing;
using Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;
using Sprk.Bff.Api.Tests.Services.Finance.Submission;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.Procedures.ProcedureBannerWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance.Procedures;

/// <summary>
/// ebilling-r1 task 113, review fix round: (1) an invoice no evaluation judged (held before any rule ran) gets NO member, decided from what the
/// pipeline really stores, while an evaluated invoice whose snapshot is null still reads "Not yet checked"; (2) the banner's stored side is built
/// by the REAL <see cref="InvoiceComplianceEvaluator"/>, not by the banner's own construction; (3) the 5 s limit holds for a Dataverse call that
/// ignores its token.
/// </summary>
public sealed class ProcedureChangeBannerFixRoundTests
{
    private static ProcedureChangeBanner BannerOver(IGenericEntityService entities, TimeProvider? clock = null) =>
        new(entities, NullLogger<ProcedureChangeBanner>.Instance, clock);

    private static Task<ProcedureChangeBannerDto?> Ask(
        ProcedureChangeBanner banner, string? findings, Entity? invoice = null, List<Entity>? lines = null) =>
        banner.ComputeAsync(invoice ?? Invoice(), lines ?? Lines(), findings, FinanceStepZeroVerdict.Internal, open: true, CancellationToken.None);

    // ── The "no evaluation ran" exemption (the spec's "judged only by BILL-SYSTEM-SUBMISSION") ──

    [Fact(DisplayName = "113 exempt: findings stored by a submission with no firm (nothing evaluated) give no member, even once the invoice has a firm")]
    public async Task StoredByANoFirmSubmission_IsAbsent_FromTheRealPipelineJson()
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var outcome = await InvoiceSubmissionProcessorTests.NoFirm().RunAsync();
        outcome.RulesSkippedForNoFirm.Should().BeTrue("control: this is the no-evaluation outcome");
        var findings = InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId);
        JsonNode.Parse(findings)!.AsObject()["decision"].Should().BeNull("the pipeline's own marker that nothing was judged");
        var before = w.Reads;

        (await Ask(BannerOver(w.Entities.Object), findings)).Should().BeNull();

        w.Reads.Should().Be(before, "nothing is resolved for an invoice no Procedure judged");
    }

    [Fact(DisplayName = "113 exempt: findings stored by a submission held because its Procedures could not be resolved (nothing evaluated) give no member")]
    public async Task StoredByAProceduresUnresolvedHold_IsAbsent_FromTheRealPipelineJson()
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var outcome = await new InvoiceSubmissionProcessorTests.Harness
        {
            EvaluationFault = new ProcedureResolutionFaultException("two versions in force"),
            FinalDelivery = true,
        }.RunAsync();
        outcome.ReasonCode.Should().Be(SubmissionReasonCodes.ProceduresUnresolved, "control: the held-unresolved outcome");
        var findings = InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId);
        JsonNode.Parse(findings)!.AsObject()["procedureSnapshot"].Should().BeNull("no evaluation, so no snapshot");

        (await Ask(BannerOver(w.Entities.Object), findings)).Should().BeNull();
    }

    [Fact(DisplayName = "113 must-not-fire: an EVALUATED invoice whose snapshot is null (size cap, correction, added line) is Not yet checked, never exempt")]
    public async Task EvaluatedButSnapshotNull_IsNotYetChecked_FromTheRealPipelineJson()
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var outcome = await new InvoiceSubmissionProcessorTests.Harness().RunAsync();
        outcome.Evaluation.Should().NotBeNull("control: an evaluation ran");
        var capped = InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId, out _, includeProcedureSnapshot: false);
        var root = JsonNode.Parse(capped)!.AsObject();
        root["procedureSnapshot"].Should().BeNull();
        root["decision"].Should().NotBeNull("an evaluated invoice keeps its decision");

        var banner = await Ask(BannerOver(w.Entities.Object), capped);

        banner.Should().Be(new ProcedureChangeBannerDto("NotYetChecked", "Not yet checked"));
    }

    [Fact(DisplayName = "113 must-not-fire: an evaluated invoice with its snapshot is judged (a member, not the exemption)")]
    public async Task EvaluatedWithSnapshot_IsNotExempt()
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var outcome = await new InvoiceSubmissionProcessorTests.Harness().RunAsync();
        var findings = InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId);

        (await Ask(BannerOver(w.Entities.Object), findings)).Should().NotBeNull();
    }

    // ── NotChanged / Changed against a snapshot the REAL evaluator produced ──

    /// <summary>The stored findings of an evaluation by the real evaluator over the world's CURRENT rows and the given request (lines match the rows).</summary>
    private static async Task<string> EvaluatedFindingsAsync(ProcedureBannerWorld w, BillingEvaluationRequest request)
    {
        var resolution = await ProcedureResolver.ReadAsync(
            w.Entities.Object, NullLogger.Instance, new CoreRecordReference(CoreRecordKind.Matter, M), F, CancellationToken.None);
        var result = BillingEvaluationKit.Evaluator().Evaluate(request, new BillingLoadedFacts
        {
            Rules = [],
            Procedures = resolution,
            InvoiceSecureRoots = BillingHandlerKit.Ordinary,
            ReliabilityThreshold = BillingHandlerKit.Threshold,
        });
        return new JsonObject
        {
            ["schema"] = 1,
            ["receivedOn"] = request.ReceivedOn.ToString("yyyy-MM-dd"),
            ["decision"] = new JsonObject { ["outcome"] = "Accepted" },
            [ProcedureSnapshot.Member] = ProcedureSnapshot.Build(result),
        }.ToJsonString();
    }

    private static BillingEvaluationRequest RequestFor(params DateOnly?[] lineDates) =>
        BillingEvaluationKit.Request(lineDates.Select((d, i) => BillingHandlerKit.Fee(i) with { Date = d }).ToArray())
            with
        { InvoiceDate = Mar15, ReceivedOn = new DateOnly(2026, 3, 20) };

    [Fact(DisplayName = "113 NotChanged against the REAL evaluator: the snapshot it stored for these rows and dates reads NotChanged; one version change reads Changed")]
    public async Task SnapshotOfTheRealEvaluator_ReadsNotChanged_AndAVersionChangeReadsChanged()
    {
        var w = new ProcedureBannerWorld();
        var (_, version, _, _) = w.Simple("RATE-1");
        var findings = await EvaluatedFindingsAsync(w, RequestFor(Feb15, Mar15));

        ShouldBe(await Ask(BannerOver(w.Entities.Object), findings, lines: Lines()), "NotChanged");

        // The must-fire twin: the same stored findings, one Procedure version ended before the later work date.
        w.Set(ProcedureEntity.VersionEntity, version, ProcedureEntity.VersionEffectiveToColumn, new DateTime(2026, 3, 1));

        ShouldBe(await Ask(BannerOver(w.Entities.Object), findings, lines: Lines()), "Changed");
    }

    [Fact(DisplayName = "113 NotChanged against the REAL evaluator: an undated Fee line, a dated one and an Adjustment line (never evaluated) read NotChanged")]
    public async Task SnapshotOfTheRealEvaluator_UndatedAndAdjustmentLines_ReadNotChanged()
    {
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1", from: Feb15);
        // The Procedure version starts Feb 15, so a wrong judged date (the Adjustment line's 2026-01-20) would resolve to a gap and read Changed.
        var findings = await EvaluatedFindingsAsync(w, RequestFor(Feb15, null));
        var rows = new List<Entity> { Line(Feb15), Line(null), Line(new DateOnly(2026, 1, 20), InvoiceLineCostType.Adjustment) };

        ShouldBe(await Ask(BannerOver(w.Entities.Object), findings, lines: rows), "NotChanged");
    }

    private static void ShouldBe(ProcedureChangeBannerDto? banner, string state) => banner!.State.Should().Be(state);

    // ── The 5 s limit holds for a Dataverse call that ignores its token ──

    [Fact(DisplayName = "113 CouldNotBeChecked: a Dataverse read that IGNORES its cancellation token still ends at the timeout")]
    public async Task ATokenIgnoringRead_StillEndsAtTheTimeout()
    {
        var entered = new TaskCompletionSource();
        var never = new TaskCompletionSource<EntityCollection>();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression _, CancellationToken _) =>
            {
                entered.TrySetResult();
                return never.Task; // never honours the token
            });
        var clock = new FakeTimeProvider();
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();

        var answer = Ask(BannerOver(entities.Object, clock), findings);
        await entered.Task;
        answer.IsCompleted.Should().BeFalse("the read is still waiting");
        clock.Advance(ProcedureChangeBanner.ResolutionTimeout);

        // A guard so a banner that waits on the read fails the test instead of hanging it.
        var banner = await answer.WaitAsync(TimeSpan.FromSeconds(20));
        banner.Should().Be(new ProcedureChangeBannerDto("CouldNotBeChecked", "Procedure changes could not be checked"));
    }

    [Fact(DisplayName = "113: a REQUEST cancelled while a token-ignoring read waits still propagates, not a state")]
    public async Task ARequestCancelledDuringATokenIgnoringRead_Propagates()
    {
        var entered = new TaskCompletionSource();
        var never = new TaskCompletionSource<EntityCollection>();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression _, CancellationToken _) =>
            {
                entered.TrySetResult();
                return never.Task;
            });
        var w = new ProcedureBannerWorld();
        w.Simple("RATE-1");
        var findings = await w.StoredFindingsAsync();
        using var cts = new CancellationTokenSource();

        var answer = BannerOver(entities.Object).ComputeAsync(Invoice(), Lines(), findings, FinanceStepZeroVerdict.Internal, true, cts.Token);
        await entered.Task;
        await cts.CancelAsync();

        var act = () => answer.WaitAsync(TimeSpan.FromSeconds(20));
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
