using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests;

namespace Sprk.Bff.Api.Tests.Services.Jobs.Handlers;

/// <summary>
/// The retry of a drafting run that died midway (ebilling-r1 task 075, review finding F1), end to end: the real handler over the real
/// <see cref="RuleSetService"/> and the 076 fake store. Only the model, the file store and the lock are substituted.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the job is delivered at least once, and the model is not deterministic. A re-delivery must leave one set of
/// drafts whatever the model says the second time, must not touch a finished run, and must not draft at all when it cannot clear the
/// earlier run's drafts.
/// </remarks>
public sealed class BillingRuleDraftRetryTests
{
    private const string Drive = "b!procedurelibrary";
    private static readonly Guid DocId = Guid.NewGuid();
    private static readonly Guid Caller = Guid.NewGuid();

    private const string Q1 = "Time must be billed in tenth-hour increments.";
    private const string Q2 = "No timekeeper may bill more than ten hours a day.";
    private const string Q3 = "Weekend work is capped at eight hours a day.";
    private const string DocumentText = $"4.2 {Q1} 5.1 {Q2} 5.2 {Q3} 9.3 Staff sensibly.";

    private static BillingRuleProposal Increment() => Proposal("TimeIncrement", """{"incrementHours":0.1}""", Q1);

    private static BillingRuleProposal Cap(int hours, string quote) => Proposal("DailyHoursCap", $$"""{"maxHoursPerDay":{{hours}}}""", quote);

    private static BillingRuleProposal Proposal(string type, string parameters, string quote) => new()
    {
        RuleType = type,
        ParametersJson = parameters,
        Consequence = "Flag",
        ClauseQuote = quote,
        ClauseLocation = "Section 4",
    };

    private sealed class Rig
    {
        public readonly Harness H = new();
        public readonly Guid SetId;
        public readonly Mock<IInvoiceAi> Ai = new();
        public IReadOnlyList<BillingRuleProposal> Reply = Array.Empty<BillingRuleProposal>();
        public int AiCalls;
        private readonly BillingRuleDraftJobHandler _handler;

        public Rig()
        {
            SetId = H.RuleSet();
            H.Row("sprk_ruleset", SetId)["sprk_sourcedocument"] = new EntityReference("sprk_document", DocId);
            H.Documents.Setup(d => d.GetDocumentAsync(DocId.ToString(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentEntity { Id = DocId.ToString(), Name = "ocg", GraphDriveId = Drive, GraphItemId = "item-1", FileName = "ocg.pdf" });

            var spe = new Mock<ISpeFileOperations>();
            spe.Setup(s => s.DownloadFileAsync(Drive, "item-1", It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream(new byte[] { 1 }));
            var text = new Mock<ITextExtractor>();
            text.Setup(t => t.ExtractAsync(It.IsAny<Stream>(), "ocg.pdf", It.IsAny<CancellationToken>()))
                .ReturnsAsync(TextExtractionResult.Succeeded(DocumentText, TextExtractionMethod.Native));
            var gate = new Mock<IIdempotencyService>();
            gate.Setup(l => l.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            gate.Setup(l => l.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Ai.Setup(a => a.DraftRulesAsync(It.IsAny<BillingRuleDraftRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    AiCalls++;
                    return new BillingRuleDraftResult { Proposals = Reply.ToArray(), NotConverted = Array.Empty<BillingRuleNotConverted>() };
                });

            _handler = new BillingRuleDraftJobHandler(H.Service(), Ai.Object, spe.Object, text.Object, gate.Object, NullLogger<BillingRuleDraftJobHandler>.Instance);
        }

        public Task<JobOutcome> Deliver(int delivery = 1)
        {
            var job = BillingRuleDraftJobHandler.BuildJob(Guid.NewGuid(), SetId, Caller, "corr");
            job.Attempt = 1;
            job.MaxAttempts = 3;
            job.DeliveryCount = delivery;
            return _handler.ProcessAsync(job, CancellationToken.None);
        }

        /// <summary>Makes the store fail when it is asked to create the <paramref name="n"/>th policy: a run that dies at rule n (a Dataverse 429).</summary>
        public void DieAtPolicy(int n) =>
            H.FailCreateWhen = e => e.LogicalName == "sprk_policy" && H.Rows.GetValueOrDefault("sprk_policy")?.Count >= n - 1;

        public IReadOnlyList<string> Bodies() =>
            H.Rows.GetValueOrDefault("sprk_policyversion")?.Select(v => v.GetAttributeValue<string>("sprk_rulebody")).ToList() ?? new List<string>();

        public int Policies => H.Rows.GetValueOrDefault("sprk_policy")?.Count ?? 0;
    }

    [Fact]
    public async Task ARerunAfterAPartialRun_WhereTheModelAnswersDifferently_LeavesExactlyOneSetOfDrafts()
    {
        var rig = new Rig();
        rig.Reply = new[] { Increment(), Cap(10, Q2), Cap(8, Q3) };
        rig.DieAtPolicy(3); // the first run stores two rules and dies at the third, before the marker

        var first = await rig.Deliver(delivery: 1);

        first.Status.Should().Be(JobStatus.Failed);
        rig.Policies.Should().Be(2);
        rig.H.Row("sprk_ruleset", rig.SetId).Contains("sprk_notconverted").Should().BeFalse("a run that died has no completion marker");

        // The retry: the model is asked again and answers differently (other numbers for the same clauses).
        rig.H.FailCreateWhen = null;
        rig.Reply = new[] { Increment(), Cap(12, Q2), Cap(9, Q3) };

        var second = await rig.Deliver(delivery: 2);

        second.Status.Should().Be(JobStatus.Completed);
        rig.Policies.Should().Be(3, "one set: the first run's two rules were cleared, not left beside the second run's three");
        rig.Bodies().Should().Contain(b => b!.Contains("\"maxHoursPerDay\":12")).And.Contain(b => b!.Contains("\"maxHoursPerDay\":9"))
            .And.NotContain(b => b!.Contains("\"maxHoursPerDay\":10"));
        rig.H.Row("sprk_ruleset", rig.SetId).Contains("sprk_notconverted").Should().BeTrue();
        rig.AiCalls.Should().Be(2);
    }

    [Fact]
    public async Task ARerunAfterAPartialRun_KeepsADraftAnAdminEdited_AndDraftsOnlyWhatIsMissing()
    {
        var rig = new Rig();
        rig.Reply = new[] { Increment(), Cap(10, Q2), Cap(8, Q3) };
        rig.DieAtPolicy(3);
        (await rig.Deliver(delivery: 1)).Status.Should().Be(JobStatus.Failed);
        var edited = rig.H.Rows["sprk_policyversion"].Single(v => v.GetAttributeValue<string>("sprk_clausereference") == Q2);
        rig.H.ConcurrentWrite("sprk_policyversion", edited.Id, new Dictionary<string, object> { ["sprk_shortheadline"] = "Edited by an admin" });
        rig.H.FailCreateWhen = null;
        rig.Reply = new[] { Increment(), Cap(12, Q2), Cap(9, Q3) };

        var second = await rig.Deliver(delivery: 2);

        second.Status.Should().Be(JobStatus.Completed);
        rig.H.Rows["sprk_policyversion"].Select(v => v.Id).Should().Contain(edited.Id, "the admin's edit is never deleted");
        rig.Policies.Should().Be(3, "the edited rule, plus the other two clauses drafted again; the edited rule's clause is not drafted twice");
        rig.Bodies().Count(b => b!.Contains("DailyHoursCap") && b.Contains("\"maxHoursPerDay\":10")).Should().Be(1);
        rig.Bodies().Should().NotContain(b => b!.Contains("\"maxHoursPerDay\":12"));
        var marker = rig.H.Row("sprk_ruleset", rig.SetId).GetAttributeValue<string>("sprk_notconverted");
        marker.Should().Contain("covers this clause");
    }

    [Fact]
    public async Task ACompletedRun_RedeliveredWithDifferentModelOutput_DeletesNothing_AndDoesNotRedraft()
    {
        var rig = new Rig();
        rig.Reply = new[] { Increment(), Cap(10, Q2) };
        (await rig.Deliver()).Status.Should().Be(JobStatus.Completed);
        var before = rig.H.Rows["sprk_policy"].Select(p => p.Id).ToList();
        rig.H.DeleteAttempts.Clear();
        rig.H.Writes.Clear();
        rig.Reply = new[] { Cap(12, Q2) };

        var again = await rig.Deliver(delivery: 2);

        again.Status.Should().Be(JobStatus.Completed);
        rig.H.DeleteAttempts.Should().BeEmpty();
        rig.H.Writes.Should().BeEmpty("a finished run is a no-op");
        rig.H.Rows["sprk_policy"].Select(p => p.Id).Should().Equal(before);
        rig.AiCalls.Should().Be(1, "the model is not asked again");
    }

    [Theory]
    [InlineData(2, JobStatus.Failed)]
    [InlineData(5, JobStatus.Poisoned)]
    public async Task ADeleteFault_FailsTheRun_WithoutAskingTheModelOrDraftingAnything_AndTheNextDeliveryCompensatesAgain(int delivery, JobStatus expected)
    {
        var rig = new Rig();
        rig.Reply = new[] { Increment(), Cap(10, Q2), Cap(8, Q3) };
        rig.DieAtPolicy(3);
        (await rig.Deliver(delivery: 1)).Status.Should().Be(JobStatus.Failed);
        rig.H.FailCreateWhen = null;
        var rowsBefore = rig.H.Rows["sprk_policy"].Select(p => p.Id).ToList();
        var callsBefore = rig.AiCalls;
        rig.H.Writes.Clear();
        rig.H.OnDelete = (_, _) => new InvalidOperationException("simulated Dataverse fault");
        rig.Reply = new[] { Increment(), Cap(12, Q2), Cap(9, Q3) };

        var outcome = await rig.Deliver(delivery);

        outcome.Status.Should().Be(expected);
        rig.AiCalls.Should().Be(callsBefore, "the model is not asked when the earlier drafts could not be cleared");
        rig.H.Writes.Where(w => w.Kind == Write.Create).Should().BeEmpty("nothing is drafted");
        rig.H.Rows["sprk_policy"].Select(p => p.Id).Should().BeEquivalentTo(rowsBefore);
        rig.H.Row("sprk_ruleset", rig.SetId).Contains("sprk_notconverted").Should().BeFalse("no marker: the next delivery compensates again");

        // The fault clears; the next delivery compensates and drafts one set.
        rig.H.OnDelete = null;
        var next = await rig.Deliver(delivery: 3);
        next.Status.Should().Be(JobStatus.Completed);
        rig.Policies.Should().Be(3);
    }
}
