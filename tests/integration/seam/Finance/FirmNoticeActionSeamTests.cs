using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Tests.Seam.ContentUnderstanding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Finance;

/// <summary>
/// ADR-043 vertical-slice seam for the firm notice (ebilling-r1 task 131, FR-50, D-60, D-52): <see cref="FirmNoticeComposer"/> over the PRODUCTION
/// <see cref="InvoiceAi"/> facade → <see cref="ActionResolver"/> (resolving the Binding for consumer type <c>invoice-firm-notice</c>) →
/// <see cref="ActionRunner"/> → <see cref="PromptSchemaRenderer"/> with the REAL authored Action mirror (<c>invoice-firm-notice.action.json</c> and its
/// output schema) as the catalog row. Only the Binding table, the Action row's storage, the LLM and the projection's Dataverse reads are doubled.
/// </summary>
/// <remarks>
/// <b>Anti-stub.</b> The outcome and the findings are read back out of the rendered prompt's frozen <c>## Input</c> section (they fail if the operand
/// never reaches the model); the authored instruction text and the authored output schema must reach the model; the notice's prose comes from the
/// fake model's reply (it fails if the reply is not parsed) and a reply with a figure must NOT appear in the notice (it fails if the prose checks are
/// bypassed). The facade's own playbook service and OpenAI client are STRICT mocks with no setups.
/// </remarks>
public sealed class FirmNoticeActionSeamTests
{
    private const string ActionCode = "invoice-firm-notice";
    private static readonly Guid ActionId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid InvoiceId = Guid.Parse("31000000-0000-4000-8000-0000000000f1");

    private const string GoodReply =
        """{"subject":"Invoice returned for correction","body":"Your invoice has been returned to you and is now closed.\n\nSome billed lines were reduced under the rule Block billing."}""";

    private static FirmOutcome Outcome() => new(
        FirmOutcomeKind.InvoiceInReview, "TRK-5.1", "In review", 4200.00m, null,
        [
            new FirmOutcomeItem(
                FirmOutcomeDecision.Reduced, Guid.NewGuid(),
                new FirmOutcomeLine(new DateOnly(2026, 2, 3), "R. Lee", "Drafting and filing", 600.00m, 450.00m),
                null, "Tasks were combined in one entry", ["Entries must describe one task each"],
                [new FirmOutcomeCitation("Block billing", "Acme Billing Guidelines", 2, "Section 7.3", "Each task is entered separately.")]),
        ],
        false);

    private sealed class Rig
    {
        public readonly Mock<IConsumerRoutingService> Routing = new();
        public readonly Mock<IScopeResolverService> Catalog = new();
        public readonly Mock<IOpenAiClient> Model = new();
        public readonly List<string> Prompts = new();
        public readonly List<string> Schemas = new();
        public FirmNoticeComposer Composer { get; }

        public Rig(string reply)
        {
            Routing.Setup(r => r.ResolveActionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActionId);
            Catalog.Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "Invoice Firm Notice",
                    SystemPrompt = AuthoredActionMirror.SystemPrompt(ActionCode),
                    OutputSchemaJson = AuthoredActionMirror.OutputSchema(ActionCode),
                    Temperature = 0.2m,
                });
            Model.Setup(o => o.GetStructuredCompletionRawAsync(
                    It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
                .Callback((string prompt, BinaryData schema, string _, string? _, int? _, float? _, CancellationToken _) =>
                {
                    Prompts.Add(prompt);
                    Schemas.Add(schema.ToString());
                })
                .ReturnsAsync(reply);

            var resolver = new ActionResolver(Routing.Object, Catalog.Object, NullLogger<ActionResolver>.Instance);
            var runner = new ActionRunner(Model.Object, new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance), NullLogger<ActionRunner>.Instance);
            var ai = new InvoiceAi(
                new Mock<IOpenAiClient>(MockBehavior.Strict).Object,
                resolver,
                runner,
                ContentUnderstandingTestHost.Unconfigured(),
                NullLogger<InvoiceAi>.Instance);

            var settings = new Mock<IEbillingSettingsReader>();
            settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings { FirmNoticeAiEnabled = true });
            Composer = new FirmNoticeComposer(
                (_, _) => Task.FromResult<FirmOutcome?>(Outcome()), (_, _, _) => Task.FromResult<FirmOutcome?>(Outcome()),
                settings.Object, ai, NullLogger<FirmNoticeComposer>.Instance);
        }

        public async Task<FirmNoticeDraft> ComposeAsync()
        {
            var result = await Composer.ComposeForInvoiceAsync(FirmNoticeKind.ReturnedToFirm, InvoiceId, CancellationToken.None);
            result.Status.Should().Be(FirmNoticeStatus.Composed);
            return result.Draft!;
        }
    }

    [Fact]
    public async Task TheNotice_ResolvesTheBindingForTheConsumerType_AndRunsTheAuthoredActionThroughTheRunner()
    {
        var rig = new Rig(GoodReply);

        var draft = await rig.ComposeAsync();

        rig.Routing.Verify(r => r.ResolveActionAsync(ActionCode, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        var prompt = rig.Prompts.Should().ContainSingle().Subject;
        prompt.Should().Contain("Never write a digit", "the authored instruction reaches the model");
        prompt.Should().Contain("Never mention ruleSetName");

        draft.Source.Should().Be(FirmNoticeSource.Action);
        draft.ActionVersion.Should().Be("INVOICE-FIRM-NOTICE@v1");
        draft.Subject.Should().Be("Invoice returned for correction (TRK-5.1)");
        draft.Html.Should().Contain("Your invoice has been returned to you and is now closed.").And.Contain("Some billed lines were reduced under the rule Block billing.");
    }

    [Fact]
    public async Task TheModelIsSentTheOutcomeAndSummaryThroughTheInputSection_WithNoNumber_AndTheAuthoredSchemaWithNoNumericType()
    {
        var rig = new Rig(GoodReply);

        await rig.ComposeAsync();

        var input = AuthoredActionMirror.InputSection(rig.Prompts.Single());
        input.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["outcome", "findings"]);
        input.GetProperty("outcome").GetString().Should().Be("ReturnedToFirm");
        var finding = input.GetProperty("findings").EnumerateArray().Should().ContainSingle().Subject;
        finding.GetProperty("ruleName").GetString().Should().Be("Block billing");
        finding.GetProperty("ruleSetName").GetString().Should().BeEmpty();
        finding.GetProperty("consequence").GetString().Should().Be("Reduced");
        finding.GetProperty("issue").GetString().Should().Be("Entries must describe one task each");
        ContainsNumber(input).Should().BeFalse("FR-50: no number reaches the model");
        foreach (var figure in new[] { "4200", "600", "450", "TRK-5", "R. Lee", "Each task is entered separately", "Section 7.3", "Acme Billing", "Tasks were combined" })
        {
            rig.Prompts.Single().Should().NotContain(figure, "an amount, a line, a clause or a reviewer's reason never reaches the model");
        }

        var schema = JsonDocument.Parse(rig.Schemas.Single()).RootElement;
        schema.ToString().Should().NotContain("\"number\"").And.NotContain("\"integer\"");
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["subject", "body"]);
    }

    [Fact]
    public async Task EveryFigureInTheNotice_IsTheProjections_NotTheModels()
    {
        var rig = new Rig(GoodReply);

        var draft = await rig.ComposeAsync();

        // 130f (K4): a Return notice has no Approved column, so the approved line amount (450.00) is not shown; the billed amount and the billed total are.
        draft.Html.Should().Contain("600.00").And.NotContain("450.00").And.Contain("Billed total: 4,200.00").And.Contain("Tracking number: TRK-5.1");
        draft.Html.Should().Contain("Acme Billing Guidelines, version 2, Section 7.3").And.Contain("Each task is entered separately.");
    }

    [Fact]
    public async Task AModelReplyWithAFigure_NeverReachesTheNotice_TheTemplateIsUsed()
    {
        var rig = new Rig("""{"subject":"Invoice returned","body":"Your invoice total of 9999 was reduced by ten percent."}""");

        var draft = await rig.ComposeAsync();

        draft.Source.Should().Be(FirmNoticeSource.Template);
        draft.FallbackReason.Should().Be(FirmNoticeFallbackReasons.ProseDigit);
        draft.Html.Should().NotContain("9999").And.NotContain("ten percent");
        draft.Html.Should().Contain(FirmNoticeFallbackText.Prose[FirmNoticeKind.ReturnedToFirm]).And.Contain("600.00");
    }

    [Theory]
    [InlineData("""{"subject":"Invoice returned"}""")]
    [InlineData("""{"subject":"","body":"x"}""")]
    [InlineData("""[]""")]
    public async Task AModelReplyTheParserRefuses_UsesTheTemplate_WithTheOutputInvalidCode(string reply)
    {
        var rig = new Rig(reply);

        var draft = await rig.ComposeAsync();

        draft.Source.Should().Be(FirmNoticeSource.Template);
        draft.FallbackReason.Should().Be(InvoiceAiException.OutputInvalid);
    }

    [Fact]
    public async Task WhenTheBindingRoutesNoAction_TheTemplateIsUsed_AndTheModelIsNeverCalled()
    {
        var rig = new Rig(GoodReply);
        rig.Routing.Setup(r => r.ResolveActionAsync(ActionCode, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var draft = await rig.ComposeAsync();

        draft.Source.Should().Be(FirmNoticeSource.Template);
        draft.FallbackReason.Should().Be(InvoiceAiException.BindingMissing);
        rig.Prompts.Should().BeEmpty();
        draft.Html.Should().Contain("600.00", "the template carries the same data");
    }

    [Fact]
    public async Task WhenTheCompletionFails_TheTemplateIsUsed()
    {
        var rig = new Rig(GoodReply);
        rig.Model.Setup(o => o.GetStructuredCompletionRawAsync(
                It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("model unreachable"));

        var draft = await rig.ComposeAsync();

        draft.Source.Should().Be(FirmNoticeSource.Template);
        draft.FallbackReason.Should().Be(InvoiceAiException.CompletionFailed);
    }

    private static bool ContainsNumber(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Number => true,
        JsonValueKind.Object => node.EnumerateObject().Any(p => ContainsNumber(p.Value)),
        JsonValueKind.Array => node.EnumerateArray().Any(ContainsNumber),
        _ => false,
    };
}
