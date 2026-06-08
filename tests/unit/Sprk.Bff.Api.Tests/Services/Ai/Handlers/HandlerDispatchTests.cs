using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// D-H-09 + D-H-10 Handler Dispatch Tests (Playbook + Chat Contexts) — R6 Pillar 2, FR-13..FR-20.
/// </summary>
/// <remarks>
/// <para>
/// Cross-handler integration layer that proves each of the 8 typed handlers (101–108) is
/// reachable via BOTH dispatch surfaces it declares:
/// </para>
/// <list type="bullet">
/// <item>
/// <strong>D-H-09 (Playbook dispatch)</strong>: For each of the 8 handlers, instantiate it,
/// build a realistic <see cref="ToolExecutionContext"/>, invoke <see cref="IToolHandler.ExecuteAsync"/>,
/// and assert structured output matches the FR's acceptance criterion. LLM-assisted handlers
/// (Wave 2) use a mocked <see cref="IOpenAiClient"/> for hermetic execution.
/// </item>
/// <item>
/// <strong>D-H-10 (Chat dispatch via adapter)</strong>: For each of the 5 handlers declaring
/// <see cref="InvocationContextKind.Both"/> (DateExtractor + the 4 Wave-2 LLM-assisted handlers),
/// wrap the handler via <see cref="ToolHandlerToAIFunctionAdapter"/>, invoke as if from an
/// LLM tool call (<c>AIFunction.InvokeAsync</c>), and assert the returned <see cref="ToolResult"/>
/// matches the playbook-path output for the same input.
/// </item>
/// </list>
/// <para>
/// <strong>Wave 1 Playbook-only handlers (FinancialCalculator, ClauseComparison,
/// FinancialCalculation)</strong> intentionally do NOT override
/// <see cref="IToolHandler.SupportedInvocationContexts"/> and therefore default to
/// <see cref="InvocationContextKind.Playbook"/>. They appear in the playbook dispatch suite only.
/// The adapter rejects wrapping a Playbook-only handler at construction time
/// (<see cref="InvalidOperationException"/>) — this is verified separately in
/// <see cref="ChatDispatch_RejectsPlaybookOnlyHandler"/>.
/// </para>
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-013</strong>: tests live in the BFF test project (not PublicContracts);
/// no production code modified.</item>
/// <item><strong>ADR-014</strong>: LLM-assisted handlers receive an in-memory
/// <see cref="MemoryDistributedCache"/> with a deterministic tenant id so per-tenant
/// cache-key behavior is exercised.</item>
/// <item><strong>ADR-015</strong>: <see cref="TypedToolHandlerTestFixture.AssertTelemetryRespectsAdr015"/>
/// asserts no document text, conversation transcript, or LLM response body leaks to logs.</item>
/// <item><strong>ADR-029</strong>: this file ships in tests only; BFF publish-size delta = 0 MB.</item>
/// </list>
/// </remarks>
public sealed class HandlerDispatchTests : TypedToolHandlerTestFixture
{
    // ═════════════════════════════════════════════════════════════════════════════
    // D-H-09 — Playbook-context dispatch: 8 [Fact] tests (one per handler)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PlaybookDispatch_DateExtractor_ExtractsNormalizedIsoDates()
    {
        // FR-17 (Wave 1, deterministic, AvailableInContexts = Both)
        var handler = new DateExtractorHandler(CreateLogger<DateExtractorHandler>());
        var context = BuildToolExecutionContext(
            extractedText: "Meeting on March 15, 2026 and review on 2026-04-01.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue("DateExtractor is pure-deterministic and inputs are valid");
        var element = result.Data!.Value;
        var dates = element.GetProperty("dates");
        dates.GetArrayLength().Should().BeGreaterThan(0,
            because: "FR-17 acceptance: realistic input must yield at least one normalized ISO date");

        var normalized = new List<string>();
        foreach (var d in dates.EnumerateArray())
        {
            normalized.Add(d.GetProperty("normalizedDate").GetString()!);
        }
        normalized.Should().Contain("2026-03-15",
            because: "FR-17 acceptance: 'March 15, 2026' must normalize to ISO 8601 '2026-03-15'");
        normalized.Should().Contain("2026-04-01",
            because: "FR-17 acceptance: explicit '2026-04-01' must be retained as-is");
    }

    [Fact]
    public async Task PlaybookDispatch_FinancialCalculator_AddsTwoUsdAmountsExactly()
    {
        // FR-18 (Wave 1, deterministic, Playbook-only)
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            toolType: ToolType.FinancialCalculator,
            configuration: """
            {
              "operation": "Sum",
              "operands": [
                { "amount": 100.50, "currency": "USD" },
                { "amount": 50.25, "currency": "USD" }
              ]
            }
            """);
        var context = BuildToolExecutionContext();

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue(
            "FR-18 acceptance: 100.50 USD + 50.25 USD must succeed with decimal precision");
        var element = result.Data!.Value;
        element.GetProperty("Result").GetDecimal().Should().Be(
            150.75m,
            because: "FR-18 acceptance: 100.50 + 50.25 = 150.75 exactly (decimal, not IEEE-754 double)");
        element.GetProperty("Currency").GetString().Should().Be("USD");
    }

    [Fact]
    public async Task PlaybookDispatch_ClauseComparison_IdenticalClausesYieldSimilarityOne()
    {
        // FR-16 (Wave 1, deterministic, Playbook-only)
        var handler = new ClauseComparisonHandler(CreateLogger<ClauseComparisonHandler>());
        const string clauseText = "The party shall provide thirty days notice prior to termination.";
        var tool = BuildAnalysisTool(
            handlerClass: nameof(ClauseComparisonHandler),
            toolType: ToolType.ClauseComparison,
            configuration: JsonSerializer.Serialize(new
            {
                clauseA = clauseText,
                clauseB = clauseText
            }));
        var context = BuildToolExecutionContext();

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue(
            "FR-16 acceptance: comparison of identical clauses must succeed");
        var element = result.Data!.Value;
        var similarity = element.GetProperty("similarity").GetDouble();
        similarity.Should().Be(1.0,
            because: "FR-16 acceptance: identical clauses must yield similarity 1.0 exactly");
    }

    [Fact]
    public async Task PlaybookDispatch_FinancialCalculation_SimpleInterestProducesCorrectValue()
    {
        // FR-19 (Wave 1, deterministic, Playbook-only) — simple_interest: I = P*r*t
        var handler = new FinancialCalculationToolHandler(CreateLogger<FinancialCalculationToolHandler>());
        var tool = BuildAnalysisTool(
            handlerClass: nameof(FinancialCalculationToolHandler),
            toolType: ToolType.FinancialCalculator,
            configuration: """
            {
              "formula": "simple_interest",
              "inputs": {
                "principal": 1000,
                "rate": 0.05,
                "time": 2
              }
            }
            """);
        var context = BuildToolExecutionContext();

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue(
            "FR-19 acceptance: simple_interest(1000, 0.05, 2) must succeed");
        var element = result.Data!.Value;
        // The handler emits a structured object with the computed interest. Locate it
        // resiliently across possible shapes ("result" / "interest").
        decimal computed = 0m;
        if (element.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Number)
            computed = r.GetDecimal();
        else if (element.TryGetProperty("interest", out var i) && i.ValueKind == JsonValueKind.Number)
            computed = i.GetDecimal();
        else if (element.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number)
            computed = v.GetDecimal();
        else
        {
            // Walk any single number child as a last resort
            foreach (var p in element.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Number)
                {
                    computed = p.Value.GetDecimal();
                    break;
                }
            }
        }

        computed.Should().Be(
            100m,
            because: "FR-19 acceptance: simple_interest = principal*rate*time = 1000*0.05*2 = 100");
    }

    [Fact]
    public async Task PlaybookDispatch_EntityExtractor_ExtractsOrganizationFromMockedLlm()
    {
        // FR-13 (Wave 2, LLM-assisted, AvailableInContexts = Both)
        const string llmJson = """
            {
              "entities": [
                { "text": "Acme Corp", "type": "organization", "normalizedValue": "Acme Corp",
                  "confidence": 0.95, "span": { "start": 0, "end": 9 } }
              ]
            }
            """;
        SetupLlmStructuredResponse(llmJson);

        var handler = new EntityExtractorHandler(
            OpenAiClientMock.Object,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new ModelSelectorOptions
            {
                ToolHandlerModel = DefaultModelDeployment,
                DefaultModel = DefaultModelDeployment
            }),
            CreateLogger<EntityExtractorHandler>());

        var context = BuildToolExecutionContext(
            extractedText: "Acme Corp signed the contract on 2026-03-15.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler),
            toolType: ToolType.EntityExtractor);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue(
            "FR-13 acceptance: realistic input + mocked-LLM organization response must succeed");
        var data = result.GetData<EntityExtractorHandler.EntityExtractionResult>()!;
        data.Entities.Should().Contain(e => e.Type == "organization" && e.NormalizedValue == "Acme Corp",
            because: "FR-13 acceptance: extracted entity must surface type='organization' and value 'Acme Corp'");
    }

    [Fact]
    public async Task PlaybookDispatch_ClauseAnalyzer_ReturnsStructuredClauseWithTypeAndSpan()
    {
        // FR-14 (Wave 2, LLM-assisted, AvailableInContexts = Both)
        const string clauseText = "The party shall pay within thirty days of invoice.";
        var llmJson = JsonSerializer.Serialize(new
        {
            clauses = new[]
            {
                new
                {
                    type = "payment",
                    text = clauseText,
                    startSpan = 0,
                    endSpan = clauseText.Length,
                    structuredFields = new { termDays = 30 },
                    confidence = 0.92
                }
            }
        });
        SetupLlmStructuredResponse(llmJson);

        var handler = new ClauseAnalyzerHandler(
            OpenAiClientMock.Object,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new ModelSelectorOptions { ToolHandlerModel = DefaultModelDeployment }),
            CreateLogger<ClauseAnalyzerHandler>());

        var context = BuildToolExecutionContext(extractedText: clauseText);
        var tool = BuildAnalysisTool(
            handlerClass: nameof(ClauseAnalyzerHandler),
            toolType: ToolType.ClauseAnalyzer);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue(
            "FR-14 acceptance: realistic input + mocked-LLM payment-clause response must succeed");
        var element = result.Data!.Value;
        var clauses = element.GetProperty("clauses");
        clauses.GetArrayLength().Should().Be(1,
            because: "FR-14 acceptance: exactly one structured clause is returned");
        var clause = clauses[0];
        clause.GetProperty("type").GetString().Should().Be("payment",
            because: "FR-14 acceptance: structured clause carries 'type'");
        clause.GetProperty("startSpan").GetInt32().Should().Be(0,
            because: "FR-14 acceptance: structured clause carries source span (start)");
        clause.GetProperty("endSpan").GetInt32().Should().Be(clauseText.Length,
            because: "FR-14 acceptance: structured clause carries source span (end)");
    }

    [Fact]
    public async Task PlaybookDispatch_RiskDetector_AssignsDeterministicSeverityBucket()
    {
        // FR-15 (Wave 2, LLM-assisted, AvailableInContexts = Both) — severity assigned by handler,
        // NOT by the LLM (D-H-07 deterministic post-processing).
        var llmJson = JsonSerializer.Serialize(new
        {
            risks = new object[]
            {
                new
                {
                    description = "Unlimited liability exposure to vendor",
                    category = "legal",
                    confidence = 0.95,
                    evidenceSpan = new { start = 0, end = 39 }
                }
            }
        });
        SetupLlmStructuredResponse(llmJson);

        var handler = new RiskDetectorHandler(
            OpenAiClientMock.Object,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new ModelSelectorOptions { ToolHandlerModel = DefaultModelDeployment }),
            CreateLogger<RiskDetectorHandler>());

        var context = BuildToolExecutionContext(
            extractedText: "Unlimited liability exposure to vendor for all damages.");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue(
            "FR-15 acceptance: realistic LLM-assisted risk extraction must succeed");
        var element = result.Data!.Value;
        var risks = element.GetProperty("risks");
        risks.GetArrayLength().Should().BeGreaterThan(0,
            because: "FR-15 acceptance: at least one risk must surface for the high-confidence liability evidence");
        var first = risks[0];
        var severity = first.GetProperty("severity").GetString();
        severity.Should().BeOneOf(
            new[] { "low", "medium", "high", "critical" },
            because: "FR-15 acceptance: handler deterministically buckets risks into low/medium/high/critical (D-H-07 binding)");
    }

    [Fact]
    public async Task PlaybookDispatch_InvoiceExtraction_SurfacesDecimalArithmeticMatch()
    {
        // FR-20 (Wave 2, LLM-assisted, AvailableInContexts = Both)
        // LLM returns: 2 widgets at 25.00 = 50.00 subtotal + 0 tax = 50.00 grand. Decimal-exact.
        var llmJson = JsonSerializer.Serialize(new
        {
            invoice = new
            {
                number = "INV-DISPATCH-001",
                issueDate = "2026-06-01",
                dueDate = "2026-06-30",
                vendor = "Vendor LLC",
                customer = "Customer Inc."
            },
            lineItems = new object[]
            {
                new
                {
                    description = "Widget A",
                    quantity = 2.00m,
                    unitPrice = 25.00m,
                    lineTotal = 50.00m,
                    taxRate = (object?)null,
                    taxAmount = (object?)null
                }
            },
            totals = new
            {
                subtotal = 50.00m,
                taxTotal = 0.00m,
                discountTotal = 0.00m,
                grandTotal = 50.00m,
                currency = "USD"
            }
        });
        SetupLlmStructuredResponse(llmJson);

        var handler = new InvoiceExtractionToolHandler(
            OpenAiClientMock.Object,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new ModelSelectorOptions { ToolHandlerModel = DefaultModelDeployment }),
            CreateLogger<InvoiceExtractionToolHandler>());

        var context = BuildToolExecutionContext(
            extractedText: "INVOICE INV-DISPATCH-001\nWidget A 2 x $25.00 = $50.00\nTOTAL: $50.00 USD");
        var tool = BuildAnalysisTool(
            handlerClass: nameof(InvoiceExtractionToolHandler),
            toolType: ToolType.Custom);

        var result = await handler.ExecuteAsync(context, tool, CancellationToken.None);

        result.Success.Should().BeTrue(
            "FR-20 acceptance: realistic invoice + LLM-mocked structured response must succeed");
        var element = result.Data!.Value;
        var totals = element.GetProperty("totals");
        totals.GetProperty("grandTotal").GetDecimal().Should().Be(
            50.00m,
            because: "FR-20 acceptance: decimal arithmetic match surfaces grand total 50.00 USD");
        totals.GetProperty("currency").GetString().Should().Be(
            "USD",
            because: "FR-20 acceptance: currency surfaces unchanged from LLM response");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // D-H-10 — Chat-context dispatch via ToolHandlerToAIFunctionAdapter:
    // 5 [Fact] tests (one per handler with AvailableInContexts ∋ Chat)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChatDispatch_DateExtractor_ExtractsNormalizedIsoDatesViaAdapter()
    {
        // FR-17: deterministic, supports Both contexts.
        var handler = new DateExtractorHandler(CreateLogger<DateExtractorHandler>());
        var tool = BuildChatAvailableTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            jsonSchema: BuildTextOnlySchema(toolName: "extract_dates"));
        var adapter = BuildAdapter(tool, handler);

        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["text"] = "Meeting on March 15, 2026 and review on 2026-04-01."
        });

        var rawResult = await adapter.InvokeAsync(args, CancellationToken.None);
        var toolResult = AsToolResult(rawResult);

        toolResult.Success.Should().BeTrue(
            "FR-17 + FR-10: chat-side dispatch through the adapter must succeed for a Both-context handler");
        var element = toolResult.Data!.Value;
        var dates = element.GetProperty("dates");
        dates.GetArrayLength().Should().BeGreaterThan(0,
            because: "Adapter dispatch must reach the same deterministic extraction code as the playbook path");
    }

    [Fact]
    public async Task ChatDispatch_EntityExtractor_ExtractsOrganizationViaAdapter()
    {
        // FR-13: LLM-assisted, supports Both contexts.
        const string llmJson = """
            {
              "entities": [
                { "text": "Acme Corp", "type": "organization", "normalizedValue": "Acme Corp",
                  "confidence": 0.95, "span": { "start": 0, "end": 9 } }
              ]
            }
            """;
        SetupLlmStructuredResponse(llmJson);

        var handler = new EntityExtractorHandler(
            OpenAiClientMock.Object,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new ModelSelectorOptions
            {
                ToolHandlerModel = DefaultModelDeployment,
                DefaultModel = DefaultModelDeployment
            }),
            CreateLogger<EntityExtractorHandler>());

        var tool = BuildChatAvailableTool(
            handlerClass: nameof(EntityExtractorHandler),
            toolType: ToolType.EntityExtractor,
            jsonSchema: BuildTextOnlySchema(toolName: "extract_entities"));
        var adapter = BuildAdapter(tool, handler);

        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["text"] = "Acme Corp signed the contract."
        });
        var rawResult = await adapter.InvokeAsync(args, CancellationToken.None);
        var toolResult = AsToolResult(rawResult);

        toolResult.Success.Should().BeTrue(
            "FR-13 + FR-10: chat-side dispatch through the adapter must succeed for a Both-context Wave-2 handler");
        var data = toolResult.GetData<EntityExtractorHandler.EntityExtractionResult>()!;
        data.Entities.Should().Contain(e => e.Type == "organization" && e.NormalizedValue == "Acme Corp",
            because: "Adapter dispatch must reach the same LLM-assisted extraction code as the playbook path");
    }

    [Fact]
    public async Task ChatDispatch_ClauseAnalyzer_ReturnsStructuredClauseViaAdapter()
    {
        // FR-14: LLM-assisted, supports Both contexts.
        const string clauseText = "The party shall pay within thirty days of invoice.";
        var llmJson = JsonSerializer.Serialize(new
        {
            clauses = new[]
            {
                new
                {
                    type = "payment",
                    text = clauseText,
                    startSpan = 0,
                    endSpan = clauseText.Length,
                    structuredFields = new { termDays = 30 },
                    confidence = 0.92
                }
            }
        });
        SetupLlmStructuredResponse(llmJson);

        var handler = new ClauseAnalyzerHandler(
            OpenAiClientMock.Object,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new ModelSelectorOptions { ToolHandlerModel = DefaultModelDeployment }),
            CreateLogger<ClauseAnalyzerHandler>());

        var tool = BuildChatAvailableTool(
            handlerClass: nameof(ClauseAnalyzerHandler),
            toolType: ToolType.ClauseAnalyzer,
            jsonSchema: BuildTextOnlySchema(toolName: "analyze_clauses"));
        var adapter = BuildAdapter(tool, handler);

        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["text"] = clauseText
        });
        var rawResult = await adapter.InvokeAsync(args, CancellationToken.None);
        var toolResult = AsToolResult(rawResult);

        toolResult.Success.Should().BeTrue(
            "FR-14 + FR-10: chat-side dispatch through the adapter must succeed");
        var element = toolResult.Data!.Value;
        element.GetProperty("clauses").GetArrayLength().Should().Be(1,
            because: "Adapter dispatch returns the structured clause shape unchanged");
        element.GetProperty("clauses")[0].GetProperty("type").GetString().Should().Be("payment");
    }

    [Fact]
    public async Task ChatDispatch_RiskDetector_AssignsDeterministicSeverityViaAdapter()
    {
        // FR-15: LLM-assisted, supports Both contexts.
        var llmJson = JsonSerializer.Serialize(new
        {
            risks = new object[]
            {
                new
                {
                    description = "Unlimited liability exposure to vendor",
                    category = "legal",
                    confidence = 0.95,
                    evidenceSpan = new { start = 0, end = 39 }
                }
            }
        });
        SetupLlmStructuredResponse(llmJson);

        var handler = new RiskDetectorHandler(
            OpenAiClientMock.Object,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new ModelSelectorOptions { ToolHandlerModel = DefaultModelDeployment }),
            CreateLogger<RiskDetectorHandler>());

        var tool = BuildChatAvailableTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector,
            jsonSchema: BuildTextOnlySchema(toolName: "detect_risks"));
        var adapter = BuildAdapter(tool, handler);

        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["text"] = "Unlimited liability exposure to vendor for all damages."
        });
        var rawResult = await adapter.InvokeAsync(args, CancellationToken.None);
        var toolResult = AsToolResult(rawResult);

        toolResult.Success.Should().BeTrue(
            "FR-15 + FR-10: chat-side dispatch through the adapter must succeed");
        var element = toolResult.Data!.Value;
        var risks = element.GetProperty("risks");
        risks.GetArrayLength().Should().BeGreaterThan(0);
        risks[0].GetProperty("severity").GetString().Should().BeOneOf(
            new[] { "low", "medium", "high", "critical" },
            because: "Adapter dispatch must preserve deterministic severity bucketing (D-H-07 binding)");
    }

    [Fact]
    public async Task ChatDispatch_InvoiceExtraction_SurfacesDecimalTotalsViaAdapter()
    {
        // FR-20: LLM-assisted, supports Both contexts.
        var llmJson = JsonSerializer.Serialize(new
        {
            invoice = new
            {
                number = "INV-CHAT-001",
                issueDate = "2026-06-01",
                dueDate = "2026-06-30",
                vendor = "Vendor LLC",
                customer = "Customer Inc."
            },
            lineItems = new object[]
            {
                new
                {
                    description = "Widget A",
                    quantity = 2.00m,
                    unitPrice = 25.00m,
                    lineTotal = 50.00m,
                    taxRate = (object?)null,
                    taxAmount = (object?)null
                }
            },
            totals = new
            {
                subtotal = 50.00m,
                taxTotal = 0.00m,
                discountTotal = 0.00m,
                grandTotal = 50.00m,
                currency = "USD"
            }
        });
        SetupLlmStructuredResponse(llmJson);

        var handler = new InvoiceExtractionToolHandler(
            OpenAiClientMock.Object,
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new ModelSelectorOptions { ToolHandlerModel = DefaultModelDeployment }),
            CreateLogger<InvoiceExtractionToolHandler>());

        var tool = BuildChatAvailableTool(
            handlerClass: nameof(InvoiceExtractionToolHandler),
            toolType: ToolType.Custom,
            jsonSchema: BuildTextOnlySchema(toolName: "extract_invoice"));
        var adapter = BuildAdapter(tool, handler);

        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["text"] = "INVOICE INV-CHAT-001\nWidget A 2 x $25.00 = $50.00\nTOTAL: $50.00 USD"
        });
        var rawResult = await adapter.InvokeAsync(args, CancellationToken.None);
        var toolResult = AsToolResult(rawResult);

        toolResult.Success.Should().BeTrue(
            "FR-20 + FR-10: chat-side dispatch through the adapter must succeed");
        var totals = toolResult.Data!.Value.GetProperty("totals");
        totals.GetProperty("grandTotal").GetDecimal().Should().Be(50.00m);
        totals.GetProperty("currency").GetString().Should().Be("USD");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // D-H-10 — Adapter contract: rejects Playbook-only handlers (defensive guard)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ChatDispatch_RejectsPlaybookOnlyHandler()
    {
        // FR-10 + D-A-10 binding: the adapter MUST refuse to wrap a handler that didn't
        // opt into chat invocation, even if the Dataverse row says AvailableInContexts=Chat.
        var handler = new FinancialCalculatorHandler(CreateLogger<FinancialCalculatorHandler>());
        var tool = BuildChatAvailableTool(
            handlerClass: nameof(FinancialCalculatorHandler),
            toolType: ToolType.FinancialCalculator,
            jsonSchema: BuildTextOnlySchema(toolName: "calculate"));

        Action act = () => BuildAdapter(tool, handler);

        act.Should().Throw<InvalidOperationException>(
                because: "FR-10 binding: adapter constructor rejects handlers that don't declare InvocationContextKind.Chat")
            .WithMessage("*does not declare InvocationContextKind.Chat*");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry binding — dispatch surface must not leak input content
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DispatchSurface_DoesNotLeakInputTextToTelemetry()
    {
        // Verify the dispatch path (playbook + chat) keeps the sentinel input strings out of
        // captured logs. Uses DateExtractor — exercises both paths cheaply (no LLM mock needed).
        const string SentinelText = "CONFIDENTIAL-SENTINEL-Meeting-on-March-15-2026-XYZZY123";

        var handler = new DateExtractorHandler(CreateLogger<DateExtractorHandler>());

        // Playbook path
        var pbContext = BuildToolExecutionContext(extractedText: SentinelText);
        var pbTool = BuildAnalysisTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor);
        await handler.ExecuteAsync(pbContext, pbTool, CancellationToken.None);

        // Chat path via adapter
        var chatTool = BuildChatAvailableTool(
            handlerClass: nameof(DateExtractorHandler),
            toolType: ToolType.DateExtractor,
            jsonSchema: BuildTextOnlySchema(toolName: "extract_dates"));
        var adapter = BuildAdapter(chatTool, handler);
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["text"] = SentinelText });
        _ = await adapter.InvokeAsync(args, CancellationToken.None);

        AssertTelemetryRespectsAdr015(SentinelText);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    private void SetupLlmStructuredResponse(string responseJson)
    {
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseJson);
    }

    /// <summary>
    /// Build an <see cref="AnalysisTool"/> with a populated JSON schema + AvailableInContexts.Both
    /// so it satisfies the <see cref="ToolHandlerToAIFunctionAdapter"/> construction contract.
    /// </summary>
    private static AnalysisTool BuildChatAvailableTool(
        string handlerClass,
        ToolType toolType,
        string jsonSchema,
        string? configuration = null,
        string? name = null)
    {
        return new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = name ?? handlerClass,
            Description = $"Chat-available tool for {handlerClass}",
            Type = toolType,
            HandlerClass = handlerClass,
            Configuration = configuration,
            AvailableInContexts = ToolAvailabilityContext.Both,
            JsonSchema = jsonSchema
        };
    }

    /// <summary>
    /// Minimal JSON-Schema (Draft 2020-12 family) declaring a single required <c>text</c> string
    /// property. Matches the chat-arg shape every Both-context handler in this dispatch suite expects.
    /// </summary>
    private static string BuildTextOnlySchema(string toolName) => JsonSerializer.Serialize(new
    {
        title = toolName,
        type = "object",
        properties = new
        {
            text = new
            {
                type = "string",
                minLength = 1,
                description = "Input text to process."
            }
        },
        required = new[] { "text" },
        additionalProperties = false
    });

    /// <summary>
    /// Construct a <see cref="ToolHandlerToAIFunctionAdapter"/> with a per-test
    /// <see cref="ChatInvocationContext"/> factory anchored at <see cref="DefaultTenantId"/>.
    /// </summary>
    private ToolHandlerToAIFunctionAdapter BuildAdapter(AnalysisTool tool, IToolHandler handler)
    {
        return new ToolHandlerToAIFunctionAdapter(
            tool,
            handler,
            contextFactory: () => new ChatInvocationContext
            {
                ChatSessionId = Guid.NewGuid(),
                TenantId = DefaultTenantId,
                RequestedToolName = tool.Name,
                ToolArgumentsJson = "{}"
            },
            logger: CreateLogger<ToolHandlerToAIFunctionAdapter>());
    }

    /// <summary>
    /// Cast the raw <see cref="object?"/> returned from
    /// <see cref="AIFunction.InvokeAsync(AIFunctionArguments, CancellationToken)"/> to a
    /// <see cref="ToolResult"/>. The adapter contract returns either the handler's
    /// <see cref="ToolResult"/> on success or a small anonymous validation envelope on failure;
    /// dispatch tests assert against the former.
    /// </summary>
    private static ToolResult AsToolResult(object? raw)
    {
        raw.Should().NotBeNull("adapter must return a non-null result envelope");
        raw.Should().BeOfType<ToolResult>(
            because: "FR-10 + D-A-10: on successful dispatch, the adapter returns the handler's ToolResult unchanged");
        return (ToolResult)raw!;
    }
}
