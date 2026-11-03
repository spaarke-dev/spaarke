using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Seam.ContentUnderstanding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Finance;

/// <summary>
/// ADR-043 vertical-slice seam for the four Action-backed <see cref="IInvoiceAi"/> capability methods
/// (ebilling-r1 task 041, D-52; FR-22 / FR-27 / FR-46 / FR-50). Each method is driven through the PRODUCTION
/// <see cref="ActionResolver"/> → <see cref="ActionRunner"/> → <see cref="PromptSchemaRenderer"/> path; only the
/// catalog data boundary (<see cref="IConsumerRoutingService"/> = the Binding table,
/// <see cref="IScopeResolverService"/> = the Action row) and the LLM boundary (<see cref="IOpenAiClient"/>) are
/// doubled.
/// </summary>
/// <remarks>
/// <para>
/// <b>Anti-stub.</b> The request is read back out of the rendered prompt's frozen <c>## Input</c> section, so the
/// tests fail if the operand never reaches the model; the parsed DTO is compared with the JSON the fake model
/// returned, so they fail if the output is not parsed. The facade's own <see cref="IPlaybookService"/> and
/// <see cref="IOpenAiClient"/> are STRICT mocks with no setups: a capability method that used either directly
/// (a by-name playbook or a C#-supplied schema) fails the test.
/// </para>
/// <para>
/// <b>Fail closed (FR-46).</b> No Binding (the routing table returns no Action), a failed completion and an
/// output missing a required field each raise <see cref="InvoiceAiException"/> with its stable reason code —
/// never a default result.
/// </para>
/// </remarks>
public sealed class InvoiceAiActionSeamTests
{
    private const string TenantId = "00000000-0000-0000-0000-0000000000aa";
    private static readonly Guid ActionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid DocumentId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // A JPS Action (the shape the eBilling Actions are authored in). The schema here is the test's stand-in
    // for the Action row's sprk_outputschemajson — it lives in the catalog, never in the facade.
    private const string JpsPrompt =
        """{"$schema":"https://spaarke.com/schemas/prompt/v1","instruction":{"role":"You are the Spaarke eBilling assistant.","task":"Process the structured input."},"output":{"fields":[{"name":"result","type":"string"}],"structuredOutput":true}}""";

    private const string OutputSchema = """{"type":"object"}""";

    private const string ClassifyOutput =
        """{"classification":"InvoiceCandidate","confidence":0.92,"hints":{"vendorName":"Acme LLP","invoiceNumber":"INV-7","invoiceDate":"2026-09-30","totalAmount":1250.50,"currency":"USD","matterReference":"M-1"},"reasoning":"Has an invoice header."}""";

    private const string CodeLinesOutput =
        """{"lines":[{"lineKey":"L1","taskCode":"L110","activityCode":"A101","expenseCode":null,"confidence":0.81,"observations":[{"type":"BlockBilling","confidence":0.7,"quotedText":"review, draft and call"}]}]}""";

    private const string DraftRulesOutput =
        """{"proposals":[{"ruleType":"rate-cap","parameters":{"maxRate":500},"consequence":"Reject","clauseQuote":"Rates may not exceed 500 per hour.","clauseLocation":"2.1"}],"notConverted":[{"text":"Be courteous.","location":"9","reason":"No matching rule type."}]}""";

    private const string FirmNoticeOutput =
        """{"subject":"Your invoice was returned","body":"We returned your invoice because some entries need more detail."}""";

    // ── The ClassifyAsync seam (task 041 step 6): operand → prompt, structured output → DTO ─────────────

    [Fact]
    public async Task ClassifyAsync_ThroughTheRealActionRunner_RendersTheDocumentIntoTheInputSectionAndParsesTheOutput()
    {
        var h = new Harness(ClassifyOutput);
        const string documentText = "INVOICE INV-7 from Acme LLP for professional services rendered in September";

        var result = await h.Sut.ClassifyAsync(new InvoiceClassifyRequest { DocumentText = documentText, TenantId = TenantId });

        h.VerifyRoutedOnce("invoice-classify");
        h.Prompt.Should().Contain("## Input").And.Contain(documentText,
            "the request must reach the model through the single-source ## Input operand");
        h.InputSection().GetProperty("documentText").GetString().Should().Be(documentText);

        result.Should().BeEquivalentTo(new InvoiceClassifyResult
        {
            Classification = InvoiceClassification.InvoiceCandidate,
            Confidence = 0.92m,
            Reasoning = "Has an invoice header.",
            Hints = new InvoiceClassifyHints
            {
                VendorName = "Acme LLP",
                InvoiceNumber = "INV-7",
                InvoiceDate = "2026-09-30",
                TotalAmount = 1250.50m,
                Currency = "USD",
                MatterReference = "M-1",
            },
        });
    }

    // ── The other three capabilities on the same real path ───────────────────────────────────────────

    [Fact]
    public async Task CodeLinesAsync_ThroughTheRealActionRunner_SendsLinesAndAllowedCodesWithGuidanceAndParsesCodesAndObservations()
    {
        var h = new Harness(CodeLinesOutput);

        var result = await h.Sut.CodeLinesAsync(CodeLinesRequest());

        h.VerifyRoutedOnce("invoice-line-code");
        var input = h.InputSection();
        var line = input.GetProperty("lines")[0];
        line.GetProperty("lineKey").GetString().Should().Be("L1");
        line.GetProperty("date").GetString().Should().Be("2026-09-02");
        line.GetProperty("lineType").GetString().Should().Be("Fee");
        line.GetProperty("narrative").GetString().Should().Be("review, draft and call");
        line.GetProperty("hours").GetDecimal().Should().Be(1.5m);
        line.GetProperty("amount").GetDecimal().Should().Be(750m);
        line.GetProperty("billedTaskCode").GetString().Should().Be("L120");
        var taskCode = input.GetProperty("allowedTaskCodes")[0];
        taskCode.GetProperty("code").GetString().Should().Be("L110");
        taskCode.GetProperty("description").GetString().Should().Be("Fact Investigation");
        taskCode.GetProperty("guidance").GetString().Should().Be("Use for fact gathering.");
        input.GetProperty("allowedActivityCodes")[0].GetProperty("code").GetString().Should().Be("A101");
        input.GetProperty("allowedExpenseCodes")[0].GetProperty("code").GetString().Should().Be("E101");

        var coded = result.Lines.Should().ContainSingle().Subject;
        coded.Should().BeEquivalentTo(new InvoiceLineCoding
        {
            LineKey = "L1",
            TaskCode = "L110",
            ActivityCode = "A101",
            ExpenseCode = null,
            Confidence = 0.81m,
            Observations =
            [
                new InvoiceLineObservation
                {
                    Type = InvoiceLineObservationType.BlockBilling,
                    Confidence = 0.7m,
                    QuotedText = "review, draft and call",
                },
            ],
        });
    }

    [Fact]
    public async Task DraftRulesAsync_ThroughTheRealActionRunner_SendsDocumentAndRuleSchemasAsJsonAndParsesProposals()
    {
        var h = new Harness(DraftRulesOutput);

        var result = await h.Sut.DraftRulesAsync(DraftRulesRequest());

        h.VerifyRoutedOnce("billing-rule-draft");
        var input = h.InputSection();
        input.GetProperty("documentText").GetString().Should().Be("Outside counsel guidelines.");
        var ruleType = input.GetProperty("ruleTypes")[0];
        ruleType.GetProperty("ruleType").GetString().Should().Be("rate-cap");
        ruleType.GetProperty("description").GetString().Should().Be("Caps the hourly rate.");
        ruleType.GetProperty("parameterSchema").GetProperty("required")[0].GetString().Should().Be("maxRate",
            "the parameter schema reaches the model as JSON, not as an escaped string");
        h.Prompt.Should().NotContain(DocumentId.ToString(), "the document id is for logs, not the model");

        var proposal = result.Proposals.Should().ContainSingle().Subject;
        proposal.RuleType.Should().Be("rate-cap");
        JsonDocument.Parse(proposal.ParametersJson).RootElement.GetProperty("maxRate").GetInt32().Should().Be(500);
        proposal.Consequence.Should().Be("Reject");
        proposal.ClauseQuote.Should().Be("Rates may not exceed 500 per hour.");
        proposal.ClauseLocation.Should().Be("2.1");
        result.NotConverted.Should().ContainSingle().Which.Should().BeEquivalentTo(new BillingRuleNotConverted
        {
            Text = "Be courteous.",
            Location = "9",
            Reason = "No matching rule type.",
        });
    }

    [Fact]
    public async Task DraftFirmNoticeAsync_ThroughTheRealActionRunner_SendsOutcomeAndFindingsWithNoNumberAndParsesProse()
    {
        var h = new Harness(FirmNoticeOutput);

        var result = await h.Sut.DraftFirmNoticeAsync(FirmNoticeRequest());

        h.VerifyRoutedOnce("invoice-firm-notice");
        var input = h.InputSection();
        input.GetProperty("outcome").GetString().Should().Be("ReturnedToFirm");
        var finding = input.GetProperty("findings")[0];
        finding.GetProperty("ruleSetName").GetString().Should().Be("Acme Billing Guidelines");
        finding.GetProperty("ruleName").GetString().Should().Be("Block billing");
        finding.GetProperty("consequence").GetString().Should().Be("Return");
        finding.GetProperty("issue").GetString().Should().Be("Several entries combine tasks.");
        ValueKinds(input).Should().NotContain(JsonValueKind.Number,
            "FR-50: no number reaches the notice model; amounts are inserted by the template");

        result.Subject.Should().Be("Your invoice was returned");
        result.Body.Should().Be("We returned your invoice because some entries need more detail.");
    }

    // ── Fail closed (FR-46) ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Capabilities))]
    public async Task CapabilityMethod_WhenTheBindingRoutesNoAction_ThrowsBindingMissingAndNeverCallsTheModel(string consumerType, Capability call)
    {
        var h = new Harness(output: "{}");
        h.Routing.Setup(r => r.ResolveActionAsync(consumerType, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var ex = await FluentActions.Awaiting(() => call(h.Sut)).Should().ThrowAsync<InvoiceAiException>();

        ex.Which.ReasonCode.Should().Be("ai.invoice.binding_missing");
        ex.Which.ConsumerType.Should().Be(consumerType);
        h.Prompt.Should().BeNull("a missing Binding never falls back to a hard-coded prompt");
    }

    [Theory]
    [MemberData(nameof(Capabilities))]
    public async Task CapabilityMethod_WhenTheCompletionFails_ThrowsCompletionFailed(string consumerType, Capability call)
    {
        var h = new Harness(output: "{}");
        h.Model.Setup(o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream 500"));

        var ex = await FluentActions.Awaiting(() => call(h.Sut)).Should().ThrowAsync<InvoiceAiException>();

        ex.Which.ReasonCode.Should().Be("ai.invoice.completion_failed");
        ex.Which.ConsumerType.Should().Be(consumerType);
        ex.Which.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Theory]
    [MemberData(nameof(OutputsMissingARequiredField))]
    public async Task CapabilityMethod_WhenTheOutputLacksARequiredField_ThrowsOutputInvalid(string consumerType, Capability call, string output)
    {
        var h = new Harness(output);

        var ex = await FluentActions.Awaiting(() => call(h.Sut)).Should().ThrowAsync<InvoiceAiException>();

        ex.Which.ReasonCode.Should().Be("ai.invoice.output_invalid");
        ex.Which.ConsumerType.Should().Be(consumerType);
    }

    // A confidence outside 0-1, an unknown enum name or a numeric enum string would otherwise read as a valid
    // classification feeding the FR-26 confidence threshold, so each is refused rather than coerced.
    [Theory]
    [InlineData("""{"classification":"InvoiceCandidate","confidence":1.5}""")]
    [InlineData("""{"classification":"InvoiceCandidate","confidence":-0.1}""")]
    [InlineData("""{"classification":"InvoiceCandidate","confidence":"0.9"}""")]
    [InlineData("""{"classification":"Invoice","confidence":0.9}""")]
    [InlineData("""{"classification":"0","confidence":0.9}""")]
    public async Task ClassifyAsync_WhenAnOutputValueIsOutOfContract_ThrowsOutputInvalid(string output)
    {
        var h = new Harness(output);

        var ex = await FluentActions.Awaiting(() => h.Sut.ClassifyAsync(ClassifyRequest())).Should().ThrowAsync<InvoiceAiException>();

        ex.Which.ReasonCode.Should().Be("ai.invoice.output_invalid");
    }

    // The production routing read (ConsumerRoutingService.ResolveActionAsync) swallows every non-cancellation
    // exception and returns null, so a failed Binding-table read and a genuinely missing Binding look the same
    // to the facade: both surface as binding_missing. The code says where the failure happened, not that
    // retrying is useless - callers retry it a bounded number of times (platform issue #1936).
    [Fact]
    public async Task ClassifyAsync_WhenTheRoutingReadReturnsNull_AsOnAMissingRowOrAFailedRead_ThrowsBindingMissing()
    {
        var h = new Harness(output: "{}");
        h.Routing.Setup(r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var ex = await FluentActions.Awaiting(() => h.Sut.ClassifyAsync(ClassifyRequest())).Should().ThrowAsync<InvoiceAiException>();

        ex.Which.ReasonCode.Should().Be("ai.invoice.binding_missing");
        ex.Which.ConsumerType.Should().Be("invoice-classify");
        h.Prompt.Should().BeNull("a null route never reaches the model");
    }

    // An optional string the model returns empty or whitespace-only carries no information; it is absent (null),
    // not "", so callers can test for presence with a null check. A required string stays strict.
    [Fact]
    public async Task ClassifyAsync_WhenAnOptionalStringIsEmptyOrWhitespace_ParsesItAsNull()
    {
        var h = new Harness("""{"classification":"NotInvoice","confidence":0.4,"hints":{"vendorName":"   ","invoiceNumber":"","currency":"USD"},"reasoning":" "}""");

        var result = await h.Sut.ClassifyAsync(ClassifyRequest());

        result.Reasoning.Should().BeNull();
        result.Hints.Should().NotBeNull();
        result.Hints!.VendorName.Should().BeNull();
        result.Hints.InvoiceNumber.Should().BeNull();
        result.Hints.Currency.Should().Be("USD");
    }

    // Regression (task 047 review): one observation with a blank or missing quote used to fail the whole chunk as output_invalid,
    // and with it the coding of every line on the invoice. It now parses as "", the line keeps its codes, and LineCodingGuard drops
    // the evidence-free observation.
    [Theory]
    [InlineData("\"quotedText\":\"\"")]
    [InlineData("\"quotedText\":\"   \"")]
    [InlineData("\"quotedText\":null")]
    public async Task CodeLinesAsync_WhenAnObservationQuoteIsBlank_ParsesTheLineAndPassesTheQuoteAsEmpty(string quote)
    {
        var h = new Harness("{\"lines\":[{\"lineKey\":\"L1\",\"taskCode\":\"L110\",\"activityCode\":\"A101\",\"expenseCode\":null,\"confidence\":0.81," +
                            "\"observations\":[{\"type\":\"VagueDescription\",\"confidence\":0.2," + quote + "}]}]}");

        var result = await h.Sut.CodeLinesAsync(CodeLinesRequest());

        var line = result.Lines.Should().ContainSingle().Subject;
        line.TaskCode.Should().Be("L110");
        line.Observations.Should().ContainSingle().Which.QuotedText.Should().BeEmpty();
    }

    // The AI kill switch keeps its own 503 contract (ADR-032): a disabled resolver is not relabelled as a
    // capability failure.
    [Fact]
    public async Task ClassifyAsync_WhenTheActionResolverIsKillSwitched_PropagatesFeatureDisabledUnchanged()
    {
        var sut = new InvoiceAi(
            new Mock<IPlaybookService>(MockBehavior.Strict).Object,
            new Mock<IOpenAiClient>(MockBehavior.Strict).Object,
            new NullActionResolver(NullLogger<NullActionResolver>.Instance),
            new NullActionRunner(NullLogger<NullActionRunner>.Instance),
            ContentUnderstandingTestHost.Unconfigured(),
            NullLogger<InvoiceAi>.Instance);

        var ex = await FluentActions.Awaiting(() => sut.ClassifyAsync(ClassifyRequest())).Should().ThrowAsync<FeatureDisabledException>();

        ex.Which.ErrorCode.Should().Be(NullActionResolver.ErrorCode);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────────

    public delegate Task Capability(IInvoiceAi ai);

    public static TheoryData<string, Capability> Capabilities() => new()
    {
        { "invoice-classify", ai => ai.ClassifyAsync(ClassifyRequest()) },
        { "invoice-line-code", ai => ai.CodeLinesAsync(CodeLinesRequest()) },
        { "billing-rule-draft", ai => ai.DraftRulesAsync(DraftRulesRequest()) },
        { "invoice-firm-notice", ai => ai.DraftFirmNoticeAsync(FirmNoticeRequest()) },
    };

    public static TheoryData<string, Capability, string> OutputsMissingARequiredField() => new()
    {
        { "invoice-classify", ai => ai.ClassifyAsync(ClassifyRequest()), """{"classification":"NotInvoice","reasoning":"x"}""" },
        { "invoice-line-code", ai => ai.CodeLinesAsync(CodeLinesRequest()), """{"lines":[{"taskCode":"L110","confidence":0.8,"observations":[]}]}""" },
        { "billing-rule-draft", ai => ai.DraftRulesAsync(DraftRulesRequest()), """{"proposals":[{"ruleType":"rate-cap","consequence":"Reject","clauseQuote":"q"}],"notConverted":[]}""" },
        { "invoice-firm-notice", ai => ai.DraftFirmNoticeAsync(FirmNoticeRequest()), """{"subject":"Returned"}""" },
    };

    private static InvoiceClassifyRequest ClassifyRequest() => new() { DocumentText = "INVOICE", TenantId = TenantId };

    private static InvoiceCodeLinesRequest CodeLinesRequest() => new()
    {
        Lines =
        [
            new InvoiceLineToCode
            {
                LineKey = "L1",
                Date = new DateOnly(2026, 9, 2),
                LineType = InvoiceLineType.Fee,
                Narrative = "review, draft and call",
                Hours = 1.5m,
                Amount = 750m,
                BilledTaskCode = "L120",
            },
        ],
        AllowedTaskCodes = [new InvoiceCodeOption("L110", "Fact Investigation", "Use for fact gathering.")],
        AllowedActivityCodes = [new InvoiceCodeOption("A101", "Plan and prepare for", null)],
        AllowedExpenseCodes = [new InvoiceCodeOption("E101", "Copying", null)],
        TenantId = TenantId,
    };

    private static BillingRuleDraftRequest DraftRulesRequest() => new()
    {
        DocumentText = "Outside counsel guidelines.",
        DocumentId = DocumentId,
        RuleTypes =
        [
            new BillingRuleTypeOption(
                "rate-cap",
                "Caps the hourly rate.",
                """{"type":"object","properties":{"maxRate":{"type":"number"}},"required":["maxRate"]}"""),
        ],
        TenantId = TenantId,
    };

    private static InvoiceFirmNoticeRequest FirmNoticeRequest() => new()
    {
        Outcome = InvoiceFirmNoticeOutcome.ReturnedToFirm,
        Findings = [new InvoiceFirmNoticeFinding("Acme Billing Guidelines", "Block billing", "Return", "Several entries combine tasks.")],
        TenantId = TenantId,
    };

    private static List<JsonValueKind> ValueKinds(JsonElement element)
    {
        var kinds = new List<JsonValueKind> { element.ValueKind };
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                kinds.AddRange(ValueKinds(property.Value));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                kinds.AddRange(ValueKinds(item));
            }
        }

        return kinds;
    }

    private sealed class Harness
    {
        private readonly string _output;

        public Harness(string output)
        {
            _output = output;

            Routing.Setup(r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActionId);
            Catalog.Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "eBilling capability",
                    SystemPrompt = JpsPrompt,
                    OutputSchemaJson = OutputSchema,
                    Temperature = 0.0m,
                });
            Model.Setup(o => o.GetStructuredCompletionRawAsync(
                    It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
                .Callback((string prompt, BinaryData _, string _, string? _, int? _, float? _, CancellationToken _) => Prompt = prompt)
                .ReturnsAsync(() => _output);

            var resolver = new ActionResolver(Routing.Object, Catalog.Object, NullLogger<ActionResolver>.Instance);
            var runner = new ActionRunner(
                Model.Object, new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance), NullLogger<ActionRunner>.Instance);

            Sut = new InvoiceAi(
                new Mock<IPlaybookService>(MockBehavior.Strict).Object,
                new Mock<IOpenAiClient>(MockBehavior.Strict).Object,
                resolver,
                runner,
                ContentUnderstandingTestHost.Unconfigured(),
                NullLogger<InvoiceAi>.Instance);
        }

        /// <summary>The Binding table (sprk_playbookconsumer) boundary.</summary>
        public Mock<IConsumerRoutingService> Routing { get; } = new();

        /// <summary>The Action row (sprk_analysisaction) boundary.</summary>
        public Mock<IScopeResolverService> Catalog { get; } = new();

        /// <summary>The LLM boundary the production ActionRunner calls.</summary>
        public Mock<IOpenAiClient> Model { get; } = new();

        public InvoiceAi Sut { get; }

        /// <summary>The prompt the model received, or null when the model was never called.</summary>
        public string? Prompt { get; private set; }

        /// <summary>Reads the JSON value of the rendered prompt's <c>## Input</c> section.</summary>
        public JsonElement InputSection()
        {
            Prompt.Should().NotBeNull("the model must have been called");
            var start = Prompt!.IndexOf("## Input\n\n", StringComparison.Ordinal);
            start.Should().BeGreaterThanOrEqualTo(0, "the operand renders through the ## Input section");
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(Prompt[(start + "## Input\n\n".Length)..]));
            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.Clone();
        }

        /// <summary>The Binding was looked up exactly once, by the literal consumer-type key, and the model was called once.</summary>
        public void VerifyRoutedOnce(string consumerType)
        {
            Routing.Verify(r => r.ResolveActionAsync(consumerType, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
            Routing.VerifyNoOtherCalls();
            Model.Verify(o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
