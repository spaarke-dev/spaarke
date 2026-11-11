using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using FeatureDisabledException = Sprk.Bff.Api.Configuration.FeatureDisabledException;

namespace Sprk.Bff.Api.Tests.Services.Jobs.Handlers;

/// <summary>
/// <see cref="BillingRuleDraftJobHandler"/> (ebilling-r1 task 075, FR-27, ADR-004): the drafting job's orchestration — source, download, text,
/// the facade, the store — its idempotent redelivery, which failures retry and which go to the dead-letter queue, and that no document text
/// is logged (ADR-015).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the guarantees a redelivered at-least-once job and an operator rely on. The collaborators are substituted at their
/// own seams (the store's behaviour is <see cref="RuleSetServiceDraftTests"/>, the facade's is the seam test); the handler's decisions are real.
/// </remarks>
public sealed class BillingRuleDraftJobHandlerTests
{
    private static readonly Guid SetId = Guid.NewGuid();
    private static readonly Guid DocId = Guid.NewGuid();
    private static readonly Guid Caller = Guid.NewGuid();
    private const string SecretText = "CONFIDENTIAL-GUIDELINE-TEXT-7731";
    private const string SecretQuote = "CONFIDENTIAL-CLAUSE-QUOTE-4412";

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<string> Messages = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null)
            {
                Messages.Add(exception.ToString());
            }
        }
    }

    private sealed class Rig
    {
        public readonly Mock<RuleSetService> RuleSets;
        public readonly Mock<IInvoiceAi> Ai = new(MockBehavior.Strict);
        public readonly Mock<ISpeFileOperations> Spe = new(MockBehavior.Strict);
        public readonly Mock<ITextExtractor> Text = new(MockBehavior.Strict);
        public readonly Mock<IIdempotencyService> Lock = new(MockBehavior.Strict);
        public readonly CapturingLogger<BillingRuleDraftJobHandler> Log = new();
        public bool LockAvailable = true;
        public readonly List<BillingRuleDraftRequest> AiRequests = new();
        public IReadOnlyList<DraftedRule>? Stored;
        public IReadOnlyList<NotConvertedClause>? StoredNotConverted;
        public Guid? StoredCaller;
        public readonly List<string> Calls = new();

        public Rig()
        {
            var gcf = Mock.Of<IGraphClientFactory>();
            var spe = new SpeFileStore(
                new ContainerOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()));
            var entities = Mock.Of<IGenericEntityService>();
            var schema = new RuleBodySchemaValidator();
            var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
            RuleSets = new Mock<RuleSetService>(
                MockBehavior.Strict,
                entities, Mock.Of<IDocumentDataverseService>(), spe, Mock.Of<IRecordOwnershipResolver>(), validator,
                new RuleBodyDescriber(validator, entities, NullLogger<RuleBodyDescriber>.Instance),
                Mock.Of<IOptionsMonitor<FinanceOptions>>(), Mock.Of<Spaarke.Scheduling.IScheduledJobLease>(), Mock.Of<ILogger<RuleSetService>>());

            Lock.Setup(l => l.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => LockAvailable);
            Lock.Setup(l => l.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            RuleSets.Setup(r => r.GetDraftSourceAsync(SetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(new DraftSource(SetId, "Acme", DocId, "b!lib", "item-1", "ocg.pdf", false)));
            RuleSets.Setup(r => r.RemoveUntouchedDraftsAsync(SetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    Calls.Add("compensate");
                    return RuleSetOutcome<DraftsCompensated>.Done(new DraftsCompensated(0, 0));
                });
            Spe.Setup(s => s.DownloadFileAsync("b!lib", "item-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(new byte[] { 1 }));
            Text.Setup(t => t.ExtractAsync(It.IsAny<Stream>(), "ocg.pdf", It.IsAny<CancellationToken>()))
                .ReturnsAsync(TextExtractionResult.Succeeded($"4.2 Bill in tenth hours. {SecretText} {SecretQuote}", TextExtractionMethod.Native));
            Ai.Setup(a => a.DraftRulesAsync(It.IsAny<BillingRuleDraftRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((BillingRuleDraftRequest r, CancellationToken _) =>
                {
                    AiRequests.Add(r);
                    Calls.Add("ask");
                    return new BillingRuleDraftResult
                    {
                        Proposals = new[]
                        {
                            new BillingRuleProposal
                            {
                                RuleType = "TimeIncrement",
                                ParametersJson = """{"incrementHours":0.1,"maxHoursPerDay":null}""",
                                Consequence = "Reject",
                                ClauseQuote = SecretQuote,
                                ClauseLocation = "Section 4.2",
                            },
                        },
                        NotConverted = new[] { new BillingRuleNotConverted { Text = "Staff sensibly.", Location = "9.3", Reason = "Nothing to check." } },
                    };
                });
            RuleSets.Setup(r => r.AddDraftsAsync(SetId, It.IsAny<IReadOnlyList<DraftedRule>>(), It.IsAny<IReadOnlyList<NotConvertedClause>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, IReadOnlyList<DraftedRule> rules, IReadOnlyList<NotConvertedClause> nc, Guid? caller, CancellationToken _) =>
                {
                    Calls.Add("store");
                    Stored = rules;
                    StoredNotConverted = nc;
                    StoredCaller = caller;
                    return RuleSetOutcome<DraftsAdded>.Done(new DraftsAdded(rules.Count, 0, nc));
                });
        }

        public BillingRuleDraftJobHandler Handler() => new(RuleSets.Object, Ai.Object, Spe.Object, Text.Object, Lock.Object, Log);

        public void Extracts(string text) =>
            Text.Setup(t => t.ExtractAsync(It.IsAny<Stream>(), "ocg.pdf", It.IsAny<CancellationToken>()))
                .ReturnsAsync(TextExtractionResult.Succeeded(text, TextExtractionMethod.Native));
    }

    private static JobContract Job(int attempt = 1, int max = 3, int delivery = 1)
    {
        var job = BillingRuleDraftJobHandler.BuildJob(Guid.NewGuid(), SetId, Caller, "corr-1");
        job.Attempt = attempt;
        job.MaxAttempts = max;
        job.DeliveryCount = delivery;
        return job;
    }

    // ── the job contract ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildJob_CarriesTheRuleSetAndTheCaller_AndUsesTheRuleSetIdAsItsIdempotencyKey()
    {
        var jobId = Guid.NewGuid();

        var job = BillingRuleDraftJobHandler.BuildJob(jobId, SetId, Caller, "corr-1");

        job.JobId.Should().Be(jobId);
        job.JobType.Should().Be("BillingRuleDraft").And.Be(new Rig().Handler().JobType);
        job.SubjectId.Should().Be(SetId.ToString());
        job.IdempotencyKey.Should().Be(SetId.ToString());
        job.CorrelationId.Should().Be("corr-1");
        job.Payload!.RootElement.GetProperty("ruleSetId").GetGuid().Should().Be(SetId);
        job.Payload.RootElement.GetProperty("callerObjectId").GetGuid().Should().Be(Caller);
    }

    // ── happy path ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Process_DraftsFromTheDocument_ThroughTheFacade_AndStoresThroughTheRuleSetService()
    {
        var rig = new Rig();

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.AiRequests.Should().ContainSingle();
        var request = rig.AiRequests[0];
        request.DocumentText.Should().Contain(SecretText);
        request.DocumentId.Should().Be(DocId);
        request.RuleTypes.Select(t => t.RuleType).Should().Equal(BillingRuleCatalogue.Types.Select(t => t.ToString()),
            "the whole closed catalogue and nothing else is offered");
        request.RuleTypes.Should().HaveCount(13);

        rig.Stored.Should().ContainSingle();
        var rule = rig.Stored![0];
        rule.RuleType.Should().Be("TimeIncrement");
        rule.RuleBody.Should().Be("""{"type":"TimeIncrement","incrementHours":0.1}""");
        rule.Consequence.Should().Be("Reject");
        rule.ClauseQuote.Should().Be(SecretQuote);
        rig.StoredNotConverted.Should().ContainSingle().Which.Should().Be(new NotConvertedClause("Staff sensibly.", "9.3", "Nothing to check."));
        rig.StoredCaller.Should().Be(Caller);
    }

    [Fact]
    public async Task Process_AProposalOfATypeThatIsNotOffered_GoesToTheNotConvertedList_NotDropped()
    {
        var rig = new Rig();
        rig.Ai.Setup(a => a.DraftRulesAsync(It.IsAny<BillingRuleDraftRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingRuleDraftResult
            {
                Proposals = new[] { new BillingRuleProposal { RuleType = "Existence", ParametersJson = "{}", Consequence = "Flag", ClauseQuote = "Some clause.", ClauseLocation = null } },
                NotConverted = Array.Empty<BillingRuleNotConverted>(),
            });

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Stored.Should().BeEmpty();
        rig.StoredNotConverted.Should().ContainSingle().Which.Text.Should().Be("Some clause.");
    }

    [Fact]
    public async Task Process_ALongDocument_IsReadInPieces_AndTheProposalsOfAllPiecesAreStoredTogether()
    {
        var rig = new Rig();
        var paragraph = new string('p', 10_000) + "\n\n";
        rig.Extracts(SecretQuote + string.Concat(Enumerable.Repeat(paragraph, 7))); // about 70,000 chars: pieces of two paragraphs (a third would pass 24,000)

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.AiRequests.Should().HaveCount(4);
        rig.AiRequests.Select(r => r.DocumentText.Length).Should().OnlyContain(l => l <= BillingRuleDraftTranslator.ChunkChars);
        rig.Stored.Should().ContainSingle("the same rule found in every piece is stored once");
        rig.StoredNotConverted!.Should().HaveCount(4, "each piece's own not-converted clause is kept");
    }

    [Fact]
    public async Task Process_ADocumentBeyondTheLimit_ListsTheUnreadTextAsNotConverted()
    {
        var rig = new Rig();
        rig.Extracts(new string('a', (BillingRuleDraftTranslator.ChunkChars * BillingRuleDraftTranslator.MaxChunks) + 500));

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.AiRequests.Should().HaveCount(BillingRuleDraftTranslator.MaxChunks);
        rig.StoredNotConverted!.Should().Contain(c => c.Reason.Contains("was not read") && c.Reason.Contains((BillingRuleDraftTranslator.ChunkChars * BillingRuleDraftTranslator.MaxChunks).ToString()));
    }

    // ── idempotent redelivery ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Process_ARuleSetThatIsAlreadyDrafted_CompletesWithoutDownloadingAskingTheModelOrWriting()
    {
        var rig = new Rig();
        rig.RuleSets.Setup(r => r.GetDraftSourceAsync(SetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(new DraftSource(SetId, "Acme", DocId, "b!lib", "item-1", "ocg.pdf", AlreadyDrafted: true)));

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Spe.VerifyNoOtherCalls();
        rig.Ai.VerifyNoOtherCalls();
        rig.RuleSets.Verify(r => r.AddDraftsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<DraftedRule>>(), It.IsAny<IReadOnlyList<NotConvertedClause>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Process_WhenAnotherDeliveryFinishedFirst_TheStoreAnswersAlreadyDrafted_AndTheJobCompletes()
    {
        var rig = new Rig();
        rig.RuleSets.Setup(r => r.AddDraftsAsync(SetId, It.IsAny<IReadOnlyList<DraftedRule>>(), It.IsAny<IReadOnlyList<NotConvertedClause>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftsAdded>.Refused(409, RuleSetService.CodeAlreadyDrafted, "done"));

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
    }

    // ── retry compensation (the earlier run's untouched drafts go before the model is asked again) ──

    [Fact]
    public async Task Process_ClearsTheEarlierRunsUntouchedDrafts_BeforeTheModelIsAsked_AndBeforeAnythingIsStored()
    {
        var rig = new Rig();
        rig.RuleSets.Setup(r => r.RemoveUntouchedDraftsAsync(SetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                rig.Calls.Add("compensate");
                return RuleSetOutcome<DraftsCompensated>.Done(new DraftsCompensated(3, 1));
            });

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Calls.Should().Equal("compensate", "ask", "store");
    }

    [Theory]
    [InlineData(1, JobStatus.Failed)]
    [InlineData(5, JobStatus.Poisoned)]
    public async Task Process_ACompensationFault_FailsTheRun_WithoutAskingTheModelOrDrafting_SoTheNextDeliveryCompensatesAgain(int delivery, JobStatus expected)
    {
        var rig = new Rig();
        rig.RuleSets.Setup(r => r.RemoveUntouchedDraftsAsync(SetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftsCompensated>.Refused(500, RuleSetService.CodeWriteFailed, "could not clear"));

        var outcome = await rig.Handler().ProcessAsync(Job(delivery: delivery), CancellationToken.None);

        outcome.Status.Should().Be(expected);
        outcome.ErrorMessage.Should().Be(RuleSetService.CodeWriteFailed);
        rig.Spe.VerifyNoOtherCalls();
        rig.Ai.VerifyNoOtherCalls();
        rig.RuleSets.Verify(r => r.AddDraftsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<DraftedRule>>(), It.IsAny<IReadOnlyList<NotConvertedClause>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Process_WhenTheCompensationFindsTheMarker_AnotherDeliveryFinishedFirst_TheJobCompletes_AndAsksNothing()
    {
        var rig = new Rig();
        rig.RuleSets.Setup(r => r.RemoveUntouchedDraftsAsync(SetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftsCompensated>.Refused(409, RuleSetService.CodeAlreadyDrafted, "done"));

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Ai.VerifyNoOtherCalls();
        rig.Spe.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Process_ARuleSetThatIsAlreadyDrafted_NeverRunsTheCompensation()
    {
        var rig = new Rig();
        rig.RuleSets.Setup(r => r.GetDraftSourceAsync(SetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(new DraftSource(SetId, "Acme", DocId, "b!lib", "item-1", "ocg.pdf", AlreadyDrafted: true)));

        await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        rig.Calls.Should().BeEmpty("a finished run is never compensated");
        rig.RuleSets.Verify(r => r.RemoveUntouchedDraftsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── which failures retry and which are poison ───────────────────────────────────────────────

    [Theory]
    [InlineData(404, "ruleset.not_found")]
    [InlineData(422, "ruleset.source_missing")]
    [InlineData(409, "ruleset.retired")]
    public async Task Process_ASourceThatCannotBeFound_IsPoisoned_NotRetried(int status, string code)
    {
        var rig = new Rig();
        rig.RuleSets.Setup(r => r.GetDraftSourceAsync(SetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Refused(status, code, "no"));

        var outcome = await rig.Handler().ProcessAsync(Job(attempt: 1), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Be(code);
        rig.Spe.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(1, JobStatus.Failed)]
    [InlineData(2, JobStatus.Failed)]
    [InlineData(3, JobStatus.Poisoned)]
    public async Task Process_ADownloadThatReturnsNothing_IsRetried_UntilTheLastAttempt(int attempt, JobStatus expected)
    {
        var rig = new Rig();
        rig.Spe.Setup(s => s.DownloadFileAsync("b!lib", "item-1", It.IsAny<CancellationToken>())).ReturnsAsync((Stream?)null);

        var outcome = await rig.Handler().ProcessAsync(Job(attempt, 3), CancellationToken.None);

        outcome.Status.Should().Be(expected);
        rig.Ai.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Process_ADocumentWithNoReadableText_IsPoisoned_AndTheModelIsNeverAsked()
    {
        var rig = new Rig();
        rig.Text.Setup(t => t.ExtractAsync(It.IsAny<Stream>(), "ocg.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Failed("scanned image", TextExtractionMethod.DocumentIntelligence));

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.Ai.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Process_WhenTextExtractionIsSwitchedOff_IsPoisoned()
    {
        var rig = new Rig();
        rig.Text.Setup(t => t.ExtractAsync(It.IsAny<Stream>(), "ocg.pdf", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException("ai.disabled", "off"));

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Contain("ai.disabled");
    }

    [Fact]
    public async Task Process_WhenAiIsSwitchedOff_IsPoisoned_AndNothingIsWritten()
    {
        var rig = new Rig();
        rig.Ai.Setup(a => a.DraftRulesAsync(It.IsAny<BillingRuleDraftRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException("ai.disabled", "off"));

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.Stored.Should().BeNull();
    }

    [Theory]
    [InlineData(InvoiceAiException.BindingMissing, JobStatus.Failed)]
    [InlineData(InvoiceAiException.CompletionFailed, JobStatus.Failed)]
    [InlineData(InvoiceAiException.OutputInvalid, JobStatus.Failed)]
    public async Task Process_AFacadeFailure_IsRetried_IncludingAMissingBindingWhichCanBeATransientRead(string reason, JobStatus expected)
    {
        var rig = new Rig();
        rig.Ai.Setup(a => a.DraftRulesAsync(It.IsAny<BillingRuleDraftRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvoiceAiException(reason, ConsumerTypes.BillingRuleDraft, "failed"));

        var outcome = await rig.Handler().ProcessAsync(Job(attempt: 1), CancellationToken.None);

        outcome.Status.Should().Be(expected);
        rig.Stored.Should().BeNull("nothing is written when any piece failed");
    }

    [Theory]
    [InlineData(InvoiceAiException.CompletionFailed)]
    [InlineData(InvoiceAiException.BindingMissing)]
    public async Task Process_AFacadeFailureOnTheFinalDelivery_IsPoisoned_WithItsReason(string reason)
    {
        // Production: Attempt never advances on a redelivery; the broker's delivery count does (JobContract.IsFinalDelivery).
        var rig = new Rig();
        rig.Ai.Setup(a => a.DraftRulesAsync(It.IsAny<BillingRuleDraftRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvoiceAiException(reason, ConsumerTypes.BillingRuleDraft, "failed"));

        var notFinal = await rig.Handler().ProcessAsync(Job(attempt: 1, max: 3, delivery: JobContract.MaxDeliveryCount - 1), CancellationToken.None);
        var final = await rig.Handler().ProcessAsync(Job(attempt: 1, max: 3, delivery: JobContract.MaxDeliveryCount), CancellationToken.None);

        notFinal.Status.Should().Be(JobStatus.Failed);
        final.Status.Should().Be(JobStatus.Poisoned);
        final.ErrorMessage.Should().Contain(reason);
    }

    [Fact]
    public async Task Process_WhenTheNewRulesCannotBeOwned_IsPoisoned_WithTheOwnerRefusalCode()
    {
        var rig = new Rig();
        rig.RuleSets.Setup(r => r.AddDraftsAsync(SetId, It.IsAny<IReadOnlyList<DraftedRule>>(), It.IsAny<IReadOnlyList<NotConvertedClause>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftsAdded>.OwnerRefused(RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team")));

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Contain(RecordOwnerRefusal.NoOwnerSource);
    }

    // ── one run at a time ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Process_WhileAnotherDeliveryHoldsTheLock_DoesNothing_AndIsRetried()
    {
        var rig = new Rig { LockAvailable = false };

        var first = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);
        var last = await rig.Handler().ProcessAsync(Job(delivery: JobContract.MaxDeliveryCount), CancellationToken.None);

        first.Status.Should().Be(JobStatus.Failed);
        last.Status.Should().Be(JobStatus.Poisoned);
        rig.RuleSets.Verify(r => r.GetDraftSourceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Spe.VerifyNoOtherCalls();
        rig.Ai.VerifyNoOtherCalls();
        rig.Stored.Should().BeNull();
        rig.Lock.Verify(l => l.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "a lock this delivery never held is not released");
    }

    [Fact]
    public async Task Process_TakesTheLockOfTheRuleSet_AndReleasesItOnEveryPath()
    {
        var rig = new Rig();
        var failing = new Rig();
        failing.Spe.Setup(s => s.DownloadFileAsync("b!lib", "item-1", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("graph 503"));

        await rig.Handler().ProcessAsync(Job(), CancellationToken.None);
        await failing.Handler().ProcessAsync(Job(), CancellationToken.None);

        var key = BillingRuleDraftJobHandler.LockKeyPrefix + SetId.ToString("N");
        foreach (var r in new[] { rig, failing })
        {
            r.Lock.Verify(l => l.TryAcquireProcessingLockAsync(key, BillingRuleDraftJobHandler.LockDuration, It.IsAny<CancellationToken>()), Times.Once);
            r.Lock.Verify(l => l.ReleaseProcessingLockAsync(key, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    // ── the clause quote ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Process_AQuoteThatIsNotInTheDocument_IsNotStored_AndIsListedWithThatReason()
    {
        var rig = new Rig();
        rig.Extracts("4.2 Bill in tenth hours. The firm shall staff sensibly.");

        var outcome = await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Stored.Should().BeEmpty("the model's quote does not occur in the document, so the rule cites nothing real");
        rig.StoredNotConverted!.Should().Contain(c => c.Text == SecretQuote && c.Reason.Contains("not found in the document"));
    }

    [Fact]
    public async Task Process_AFailedWrite_IsRetried_ButARefusalIsNot()
    {
        var rig = new Rig();
        rig.RuleSets.Setup(r => r.AddDraftsAsync(SetId, It.IsAny<IReadOnlyList<DraftedRule>>(), It.IsAny<IReadOnlyList<NotConvertedClause>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftsAdded>.Refused(500, RuleSetService.CodeWriteFailed, "write"));
        var retried = await rig.Handler().ProcessAsync(Job(attempt: 1), CancellationToken.None);

        rig.RuleSets.Setup(r => r.AddDraftsAsync(SetId, It.IsAny<IReadOnlyList<DraftedRule>>(), It.IsAny<IReadOnlyList<NotConvertedClause>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftsAdded>.Refused(404, RuleSetService.CodeNotFound, "gone"));
        var refused = await rig.Handler().ProcessAsync(Job(attempt: 1), CancellationToken.None);

        retried.Status.Should().Be(JobStatus.Failed);
        refused.Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact]
    public async Task Process_AnUnexpectedException_IsRetried_NotSwallowed()
    {
        var rig = new Rig();
        rig.Spe.Setup(s => s.DownloadFileAsync("b!lib", "item-1", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("graph 503"));

        var outcome = await rig.Handler().ProcessAsync(Job(attempt: 1), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        outcome.ErrorMessage.Should().Be(nameof(HttpRequestException), "the type, not the message, which may carry a path or text");
    }

    [Fact]
    public async Task Process_ACancellation_IsPropagated_NotTurnedIntoAFailure()
    {
        var rig = new Rig();
        using var cts = new CancellationTokenSource();
        rig.Spe.Setup(s => s.DownloadFileAsync("b!lib", "item-1", It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var act = () => rig.Handler().ProcessAsync(Job(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{"ruleSetId":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("""{"ruleSetId":"not-a-guid"}""")]
    public async Task Process_AnInvalidPayload_IsPoisoned(string? payload)
    {
        var rig = new Rig();
        var job = new JobContract
        {
            JobType = BillingRuleDraftJobHandler.JobTypeName,
            Payload = payload is null ? null : JsonDocument.Parse(payload),
        };

        var outcome = await rig.Handler().ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        rig.RuleSets.VerifyNoOtherCalls();
    }

    // ── ADR-015 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Process_NeverLogsTheDocumentTextOrAClause_OnAnyPath()
    {
        // Success, a facade failure, a write failure and an unexpected exception: each writes log lines; none the handler writes may carry document content.
        var paths = new List<Action<Rig>>
        {
            _ => { },
            r => r.Ai.Setup(a => a.DraftRulesAsync(It.IsAny<BillingRuleDraftRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvoiceAiException(InvoiceAiException.OutputInvalid, ConsumerTypes.BillingRuleDraft, "bad output")),
            r => r.RuleSets.Setup(x => x.AddDraftsAsync(SetId, It.IsAny<IReadOnlyList<DraftedRule>>(), It.IsAny<IReadOnlyList<NotConvertedClause>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(RuleSetOutcome<DraftsAdded>.Refused(500, RuleSetService.CodeWriteFailed, "write")),
            r => r.Text.Setup(t => t.ExtractAsync(It.IsAny<Stream>(), "ocg.pdf", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("extractor failed")),
        };

        foreach (var arrange in paths)
        {
            var rig = new Rig();
            arrange(rig);

            await rig.Handler().ProcessAsync(Job(), CancellationToken.None);

            rig.Log.Messages.Should().NotBeEmpty();
            rig.Log.Messages.Should().NotContain(m => m.Contains(SecretText) || m.Contains(SecretQuote) || m.Contains("Staff sensibly"));
        }
    }
}
