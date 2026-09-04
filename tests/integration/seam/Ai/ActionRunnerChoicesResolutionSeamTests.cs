using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// Seam test for the linear-path <c>$choices</c> pre-resolution fix
/// (email-communication-intelligence-r2, 2026-09-04). Drives a JPS Action carrying a
/// <c>lookup:</c> <c>$choices</c> reference (the TRIAGE-EMAIL category shape) through the REAL
/// <see cref="ActionRunner"/> → real <see cref="PromptSchemaRenderer"/> → real
/// <see cref="LookupChoicesResolver"/>, with only the external LLM boundary
/// (<see cref="IOpenAiClient"/>) and the Dataverse boundary (<see cref="IScopeResolverService"/>) doubled.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bug this pins.</b> Triage runs on the Linear AI Consumer path (<see cref="ActionRunner"/>),
/// NOT the node path (<c>AiAnalysisNodeExecutor</c>). Only the node path pre-resolved a JPS Action's
/// <c>$choices</c> references before render, so on the linear path the firm's <c>sprk_triagecategory</c>
/// taxonomy names never reached the model — it emitted a free-form category that matched no taxonomy row,
/// and <c>sprk_triagecategory</c> was left unset on 100% of real captures. The fix makes
/// <see cref="ActionRunner"/> run the SAME pre-resolution pass the node path runs.
/// </para>
/// <para>
/// <b>Anti-stub.</b> A TRIAGE-EMAIL-style Action declares <c>structuredOutput:true</c>, so its output fields
/// never render into the prompt — the taxonomy can only reach the model through the constrained-decoding
/// SCHEMA. The positive test asserts the resolved taxonomy names were injected as an <c>enum</c> on the
/// <c>category</c> property of the schema handed to the LLM boundary. The control test — the same Action with
/// no scope factory (the pre-fix wiring) — asserts they were NOT, so a regression that silently stops
/// resolving/enforcing <c>$choices</c> on the linear path fails the build. The
/// <see cref="IScopeResolverService"/> <c>Verify</c> additionally pins the OData entity-set name to
/// <c>sprk_triagecategories</c> (the y→ies pluralization, not the 404-ing <c>sprk_triagecategorys</c>).
/// </para>
/// </remarks>
public sealed class ActionRunnerChoicesResolutionSeamTests
{
    // The firm's live triage taxonomy (7 rows) the category $choices resolves against.
    private static readonly string[] TaxonomyNames =
    [
        "Client instruction", "Court / Filing", "Invoice / Billing",
        "Scheduling", "Opposing counsel", "Administrative", "Marketing / Noise",
    ];

    // A JPS Action whose `category` output field carries the lookup $choices reference — the TRIAGE-EMAIL shape.
    private const string TriageShapedJps =
        """{"$schema":"https://spaarke.com/schemas/prompt/v1","instruction":{"role":"You are the Spaarke email triage structuring assistant.","task":"Map the classification's freeform category onto the firm taxonomy."},"output":{"fields":[{"name":"category","type":"string","description":"The triage category.","$choices":"lookup:sprk_triagecategory.sprk_name"},{"name":"summary","type":"string","description":"2-line summary."},{"name":"priority","type":"string","description":"Priority."}],"structuredOutput":true}}""";

    // Constrained-decoding output schema — category stays a FREE STRING (no static enum), so FR-16
    // (admin-tunable taxonomy without a redeploy) is preserved; the taxonomy is guidance in the PROMPT only.
    private const string TriageOutputSchema =
        """{"type":"object","additionalProperties":false,"required":["category","summary","priority"],"properties":{"category":{"type":"string"},"summary":{"type":"string"},"priority":{"type":"string"}}}""";

    [Fact]
    public async Task RunAsync_JpsActionWithLookupChoices_EnforcesResolvedTaxonomyAsOutputSchemaEnum()
    {
        var h = new Harness();

        await h.RunTriageActionAsync(withScopeFactory: true);

        // The fix: the firm's live category names became a constrained-decoding enum on the category
        // property, so the model MUST emit one of them (never a free-form label that matches no taxonomy row).
        var categoryEnum = h.CategoryEnumValues();
        categoryEnum.Should().BeEquivalentTo(TaxonomyNames,
            "the linear path MUST inject the resolved category $choices as the output-schema enum so "
            + "structured-output decoding enforces the live sprk_triagecategory taxonomy");

        // Both fixes locked together: the resolver queried the correctly-pluralized OData entity set
        // (y→ies), not the 404-ing naive `sprk_triagecategorys`.
        h.Scope.Verify(
            s => s.QueryLookupValuesAsync("sprk_triagecategories", "sprk_name", It.IsAny<CancellationToken>()),
            Times.Once,
            "the lookup must query the platform-pluralized entity set `sprk_triagecategories`");
    }

    [Fact]
    public async Task RunAsync_JpsActionWithLookupChoices_NoScopeFactory_LeavesCategoryAFreeString()
    {
        // Control = the pre-fix wiring (ActionRunner constructed without the scope factory). Proves the
        // enum enforcement is caused by the fix, not by some other path, and guards against a silent
        // regression back to the "category null on 100% of captures" bug.
        var h = new Harness();

        await h.RunTriageActionAsync(withScopeFactory: false);

        h.CategoryEnumValues().Should().BeNull(
            "without $choices resolution the category property stays a free string — the shipped bug");
        h.Scope.Verify(
            s => s.QueryLookupValuesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "with no scope factory there is no resolver to query Dataverse");
    }

    private sealed class Harness
    {
        public Mock<IScopeResolverService> Scope { get; } = new();
        public string? CapturedPrompt { get; private set; }
        public string? CapturedSchemaJson { get; private set; }

        public Harness()
        {
            Scope
                .Setup(s => s.QueryLookupValuesAsync("sprk_triagecategories", "sprk_name", It.IsAny<CancellationToken>()))
                .ReturnsAsync(TaxonomyNames);
        }

        /// <summary>The <c>enum</c> injected onto the <c>category</c> property of the schema handed to the
        /// LLM boundary, or <c>null</c> when category was left a free string (no enum).</summary>
        public string[]? CategoryEnumValues()
        {
            if (CapturedSchemaJson is null)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(CapturedSchemaJson);
            if (!doc.RootElement.TryGetProperty("properties", out var props)
                || !props.TryGetProperty("category", out var category)
                || !category.TryGetProperty("enum", out var enumEl)
                || enumEl.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return enumEl.EnumerateArray().Select(e => e.GetString()!).ToArray();
        }

        public async Task RunTriageActionAsync(bool withScopeFactory)
        {
            var openAi = new Mock<IOpenAiClient>();
            openAi
                .Setup(o => o.GetStructuredCompletionRawAsync(
                    It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(),
                    It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
                .Callback((string prompt, BinaryData schema, string _, string? _, int? _, float? _, CancellationToken _) =>
                {
                    CapturedPrompt = prompt;
                    CapturedSchemaJson = schema.ToString();
                })
                .ReturnsAsync("""{"category":"Court / Filing","summary":"x","priority":"High"}""");

            var renderer = new PromptSchemaRenderer(Mock.Of<ILogger<PromptSchemaRenderer>>());

            IServiceScopeFactory? scopeFactory = null;
            if (withScopeFactory)
            {
                var resolver = new LookupChoicesResolver(Scope.Object, Mock.Of<ILogger<LookupChoicesResolver>>());
                var sp = new Mock<IServiceProvider>();
                sp.Setup(p => p.GetService(typeof(LookupChoicesResolver))).Returns(resolver);
                var scope = new Mock<IServiceScope>();
                scope.SetupGet(x => x.ServiceProvider).Returns(sp.Object);
                var factory = new Mock<IServiceScopeFactory>();
                factory.Setup(f => f.CreateScope()).Returns(scope.Object);
                scopeFactory = factory.Object;
            }

            var runner = new ActionRunner(
                openAi.Object, renderer, Mock.Of<ILogger<ActionRunner>>(),
                modelOptions: null, referenceRetrieval: null, scopeFactory: scopeFactory);

            var action = new AnalysisAction
            {
                Id = Guid.NewGuid(),
                Name = "Triage Email",
                SystemPrompt = TriageShapedJps,
                OutputSchemaJson = TriageOutputSchema,
                Temperature = 0.0m,
            };

            var input = JsonSerializer.SerializeToElement(new
            {
                classification = new { category = "court-notice", urgency = "elevated" },
                message = new { subject = "LITG-119896 hearing notice", bodyText = "The court has set a hearing." },
            });

            var inputs = new BoundInputs
            {
                Context = ContextEnvelopeReferenceProducer.Assemble(),
                Operand = new ResolvedOperand
                {
                    Channel = OperandChannel.Input,
                    Kind = OperandKind.PreResolved,
                    Input = input,
                },
            };

            var context = new LinearRunContext
            {
                ConsumerType = "email-triage",
                TenantId = "00000000-0000-0000-0000-0000000000aa",
            };

            await runner.RunAsync(action, inputs, context, CancellationToken.None);
        }
    }
}
