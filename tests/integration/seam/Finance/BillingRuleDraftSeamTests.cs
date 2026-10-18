using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Seam.ContentUnderstanding;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Finance;

/// <summary>
/// ADR-043 vertical-slice seam for rule drafting (ebilling-r1 task 075, FR-27, D-27, D-52): the rule drafting JOB over the PRODUCTION
/// <see cref="InvoiceAi"/> facade → <see cref="ActionResolver"/> → <see cref="ActionRunner"/> → <see cref="PromptSchemaRenderer"/> path with the
/// REAL authored Action mirror (<c>billing-rule-draft.action.json</c> and its output schema) as the catalog row, into the REAL
/// <see cref="RuleSetService"/> store and <c>PolicyVersionValidator</c>. Only the Binding table, the Action row's storage, the LLM, the
/// document download/extraction and Dataverse are doubled.
/// </summary>
/// <remarks>
/// <b>Anti-stub.</b> The offered rule types and the document are read back out of the rendered prompt's frozen <c>## Input</c> section (they
/// fail if the operand never reaches the model); the authored instruction text must be in the prompt; the rows in the store come from the fake
/// model's reply (they fail if the reply is not parsed, validated and stored); and a model reply the validator refuses must reappear in the
/// not-converted list (it fails if a refusal is dropped). The facade's own playbook service and OpenAI client are STRICT mocks with no setups.
/// </remarks>
public sealed class BillingRuleDraftSeamTests
{
    private const string ActionCode = "billing-rule-draft";
    private static readonly Guid ActionId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid SourceId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private const string Drive = "b!procedurelibrary";
    private const string DocumentText = "4.2 Time is billed in tenth-hour increments.\n\n5.1 No timekeeper may bill over 99 hours a day.\n\n6.3 Hourly rates are capped at the agreed rate card.\n\n9.3 Staff sensibly.";

    private static string Params(string member, string value, params (string Member, string Value)[] more)
    {
        var all = new[] { "tolerancePercent", "incrementHours", "maxHoursPerDay", "codes", "nonReimbursableCodes", "maxAmountPerItem", "maxAgeDays", "graceDays", "matchFields", "fields", "observation", "minConfidence" };
        var set = new Dictionary<string, string> { [member] = value };
        foreach (var (m, v) in more)
        {
            set[m] = v;
        }

        return "{" + string.Join(",", all.Select(a => $"\"{a}\":{set.GetValueOrDefault(a, "null")}")) + "}";
    }

    // What a model returns for the document above: two good rules, one the validator refuses (99 hours), one with a parameter the type lacks.
    private static readonly string ModelReply =
        "{\"proposals\":[" +
        "{\"ruleType\":\"TimeIncrement\",\"parameters\":" + Params("incrementHours", "0.1") + ",\"consequence\":\"Flag\",\"clauseQuote\":\"4.2 Time is billed in tenth-hour increments.\",\"clauseLocation\":\"Section 4.2\"}," +
        "{\"ruleType\":\"DailyHoursCap\",\"parameters\":" + Params("maxHoursPerDay", "99") + ",\"consequence\":\"Flag\",\"clauseQuote\":\"5.1 No timekeeper may bill over 99 hours a day.\",\"clauseLocation\":\"Section 5.1\"}," +
        "{\"ruleType\":\"ApprovedRate\",\"parameters\":" + Params("tolerancePercent", "0", ("incrementHours", "0.5")) + ",\"consequence\":\"SuggestAdjustment\",\"clauseQuote\":\"6.3 Hourly rates are capped at the agreed rate card.\",\"clauseLocation\":null}" +
        "],\"notConverted\":[{\"text\":\"9.3 Staff sensibly.\",\"location\":\"Section 9.3\",\"reason\":\"No offered rule type checks this.\"}]}";

    /// <summary>A real (in-memory) processing lock: the second taker of a key is refused until the first releases it.</summary>
    private sealed class InMemoryLock : IIdempotencyService
    {
        private readonly HashSet<string> _held = new();

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
        {
            lock (_held)
            {
                return Task.FromResult(_held.Add(eventId));
            }
        }

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
        {
            lock (_held)
            {
                _held.Remove(eventId);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class Rig
    {
        public readonly RuleSetServiceTests.Harness Store = new();
        public readonly Mock<IConsumerRoutingService> Routing = new();
        public readonly Mock<IScopeResolverService> Catalog = new();
        public readonly Mock<IOpenAiClient> Model = new();
        public readonly Mock<ISpeFileOperations> Spe = new();
        public readonly Mock<ITextExtractor> Text = new();
        public readonly List<string> Prompts = new();
        public readonly Guid RuleSetId;
        public BillingRuleDraftJobHandler Handler { get; }

        public Rig()
        {
            RuleSetId = Store.RuleSet();
            Store.Row("sprk_ruleset", RuleSetId)["sprk_sourcedocument"] = new EntityReference("sprk_document", SourceId);
            Store.Documents.Setup(d => d.GetDocumentAsync(SourceId.ToString(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Spaarke.Dataverse.DocumentEntity { Id = SourceId.ToString(), Name = "ocg", GraphDriveId = Drive, GraphItemId = "item-1", FileName = "ocg.pdf" });

            Routing.Setup(r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActionId);
            Catalog.Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "Billing Rule Draft",
                    SystemPrompt = AuthoredActionMirror.SystemPrompt(ActionCode),
                    OutputSchemaJson = AuthoredActionMirror.OutputSchema(ActionCode),
                    Temperature = 0.0m,
                });
            Model.Setup(o => o.GetStructuredCompletionRawAsync(
                    It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
                .Callback((string prompt, BinaryData _, string _, string? _, int? _, float? _, CancellationToken _) => Prompts.Add(prompt))
                .ReturnsAsync(ModelReply);

            Spe.Setup(s => s.DownloadFileAsync(Drive, "item-1", It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream(new byte[] { 1 }));
            Text.Setup(t => t.ExtractAsync(It.IsAny<Stream>(), "ocg.pdf", It.IsAny<CancellationToken>()))
                .ReturnsAsync(TextExtractionResult.Succeeded(DocumentText, TextExtractionMethod.Native));

            var resolver = new ActionResolver(Routing.Object, Catalog.Object, NullLogger<ActionResolver>.Instance);
            var runner = new ActionRunner(Model.Object, new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance), NullLogger<ActionRunner>.Instance);
            var ai = new InvoiceAi(
                new Mock<IPlaybookService>(MockBehavior.Strict).Object,
                new Mock<IOpenAiClient>(MockBehavior.Strict).Object,
                resolver,
                runner,
                ContentUnderstandingTestHost.Unconfigured(),
                NullLogger<InvoiceAi>.Instance);
            Handler = new BillingRuleDraftJobHandler(Store.Service(), ai, Spe.Object, Text.Object, new InMemoryLock(), NullLogger<BillingRuleDraftJobHandler>.Instance);
        }

        public Task<JobOutcome> RunAsync() =>
            Handler.ProcessAsync(BillingRuleDraftJobHandler.BuildJob(Guid.NewGuid(), RuleSetId, Guid.NewGuid(), "corr"), CancellationToken.None);
    }

    [Fact]
    public async Task TheJob_SendsTheDocumentAndTheClosedTypeListToTheModel_ThroughTheAuthoredAction()
    {
        var rig = new Rig();

        var outcome = await rig.RunAsync();

        outcome.Status.Should().Be(JobStatus.Completed);
        rig.Routing.Verify(r => r.ResolveActionAsync(ActionCode, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        var prompt = rig.Prompts.Should().ContainSingle().Subject;
        prompt.Should().Contain("The list of rule types is closed", "the authored instruction reaches the model");
        prompt.Should().Contain("notConverted");

        var input = AuthoredActionMirror.InputSection(prompt);
        input.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["documentText", "ruleTypes"]);
        input.GetProperty("documentText").GetString().Should().Be(DocumentText);
        var types = input.GetProperty("ruleTypes").EnumerateArray().ToList();
        types.Select(t => t.GetProperty("ruleType").GetString()).Should().Equal(BillingRuleCatalogue.Types.Select(t => t.ToString()));
        types.Should().OnlyContain(t => t.GetProperty("parameterSchema").ValueKind == JsonValueKind.Object,
            "the schema is embedded as JSON, not as an escaped string");
        types.Select(t => t.GetProperty("parameterSchema").GetProperty("properties").EnumerateObject().Select(p => p.Name))
            .Should().OnlyContain(names => !names.Contains("type"), "the application adds the type member itself");
    }

    [Fact]
    public async Task TheJob_StoresTheValidProposals_AsProposedOffRules_AndListsEveryRefusalWithTheClauseThatCausedIt()
    {
        var rig = new Rig();

        var outcome = await rig.RunAsync();

        outcome.Status.Should().Be(JobStatus.Completed);

        // Stored: the one valid proposal, as a Proposed, Off, not-in-force billing rule beside its quote.
        var policy = rig.Store.Rows["sprk_policy"].Should().ContainSingle().Subject;
        policy.GetAttributeValue<bool>("sprk_enabled").Should().BeFalse();
        policy.Contains("sprk_currentversion").Should().BeFalse();
        policy.GetAttributeValue<OptionSetValue>("sprk_domain").Value.Should().Be(RuleDomains.PolicyDomainBilling);
        var version = rig.Store.Rows["sprk_policyversion"].Should().ContainSingle().Subject;
        version.GetAttributeValue<string>("sprk_rulebody").Should().Be("""{"type":"TimeIncrement","incrementHours":0.1}""");
        version.GetAttributeValue<string>("sprk_clausereference").Should().Be("4.2 Time is billed in tenth-hour increments.");
        version.GetAttributeValue<OptionSetValue>("sprk_proposalstatus").Value.Should().Be((int)ProposalState.Proposed);

        // Refused, not dropped: the 99-hour cap (schema) and the rate rule with a parameter its type lacks; plus the model's own entry.
        var marker = (string)rig.Store.Writes.Last().Fields["sprk_notconverted"];
        using var list = JsonDocument.Parse(marker);
        var entries = list.RootElement.EnumerateArray().ToList();
        entries.Select(e => e.GetProperty("text").GetString()).Should().BeEquivalentTo(
            "9.3 Staff sensibly.",
            "5.1 No timekeeper may bill over 99 hours a day.",
            "6.3 Hourly rates are capped at the agreed rate card.");
        entries.Single(e => e.GetProperty("text").GetString()!.StartsWith("5.1", StringComparison.Ordinal))
            .GetProperty("reason").GetString().Should().StartWith("Not stored: the drafted rule was not valid");
        entries.Single(e => e.GetProperty("text").GetString()!.StartsWith("6.3", StringComparison.Ordinal))
            .GetProperty("location").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ARedeliveredJob_AfterAFinishedRun_DoesNotAskTheModelOrWriteAgain()
    {
        var rig = new Rig();
        (await rig.RunAsync()).Status.Should().Be(JobStatus.Completed);
        var writes = rig.Store.Writes.Count;

        var again = await rig.RunAsync();

        again.Status.Should().Be(JobStatus.Completed);
        rig.Prompts.Should().ContainSingle("the second delivery found the marker and did not call the model");
        rig.Store.Writes.Should().HaveCount(writes, "and wrote nothing");
        rig.Store.Rows["sprk_policy"].Should().ContainSingle();
    }

    [Fact]
    public async Task TwoDeliveriesAtOnce_DraftOnce_TheSecondWaitsAndFindsTheMarker()
    {
        // The queue does not de-duplicate: a double click, or a redelivery while the first run is still in the model, starts a second run.
        var rig = new Rig();
        var gate = new TaskCompletionSource();
        rig.Model.Setup(o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .Returns(async (string prompt, BinaryData _, string _, string? _, int? _, float? _, CancellationToken _) =>
            {
                rig.Prompts.Add(prompt);
                await gate.Task;
                return ModelReply;
            });

        var first = rig.RunAsync();
        while (rig.Prompts.Count == 0)
        {
            await Task.Delay(5);
        }

        JobOutcome second;
        try
        {
            // Bounded: without the lock this delivery would also wait on the model and the test would hang instead of failing.
            second = await rig.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            gate.SetResult();
        }

        var firstOutcome = await first;
        var retried = await rig.RunAsync();

        second.Status.Should().Be(JobStatus.Failed, "the second delivery could not take the lock and is retried later");
        firstOutcome.Status.Should().Be(JobStatus.Completed);
        retried.Status.Should().Be(JobStatus.Completed, "the retry finds the marker");
        rig.Prompts.Should().ContainSingle("only the first run asked the model");
        rig.Store.Rows["sprk_policy"].Should().ContainSingle("the rule was stored once");
    }

    [Fact]
    public async Task AModelReplyTheParserRefuses_FailsTheJobWithoutWritingAnything()
    {
        var rig = new Rig();
        rig.Model.Setup(o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"proposals":[{"ruleType":"TimeIncrement"}],"notConverted":[]}""");

        var outcome = await rig.RunAsync();

        outcome.Status.Should().Be(JobStatus.Failed, "output_invalid may succeed on the next attempt");
        rig.Store.Writes.Should().BeEmpty();
    }
}
