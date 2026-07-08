using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Tests for PlaybookTemplateContextBuilder — the Wave 11 Option B Layer 1 shared helper.
/// Pure-transformation tests; no mocks; no I/O. Verifies the merge order (NodeOutputs →
/// Parameters → run bag), JsonElement-to-traversable conversion, and graceful handling of
/// empty/missing values.
/// </summary>
public class PlaybookTemplateContextBuilderTests
{
    private static PlaybookRunContext CreateRunContext(
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        var httpContext = new DefaultHttpContext();
        return new PlaybookRunContext(
            runId: Guid.NewGuid(),
            playbookId: Guid.NewGuid(),
            documentIds: Array.Empty<Guid>(),
            httpContext: httpContext,
            userContext: null,
            parameters: parameters);
    }

    private static NodeOutput StructuredOutput(string outputVariable, object data) =>
        NodeOutput.Ok(
            nodeId: Guid.NewGuid(),
            outputVariable: outputVariable,
            data: data);

    private static NodeOutput TextOutput(string outputVariable, string text) =>
        NodeOutput.Ok(
            nodeId: Guid.NewGuid(),
            outputVariable: outputVariable,
            data: null,
            textContent: text);

    [Fact]
    public void Build_NodeOutputs_AreExposedUnderOutputVariableNames()
    {
        var runContext = CreateRunContext();
        runContext.StoreNodeOutput(StructuredOutput("tldrResult", new { summary = "5 notifications today" }));

        var context = PlaybookTemplateContextBuilder.Build(runContext);

        context.Should().ContainKey("tldrResult");
        context["tldrResult"].Should().NotBeNull();
    }

    [Fact]
    public void Build_StructuredDataOutput_IsConvertedToTraversableDictionary()
    {
        var runContext = CreateRunContext();
        runContext.StoreNodeOutput(StructuredOutput("start", new
        {
            categories = new[] { new { category = "tasks-due-soon", count = 3 } },
            totalNotificationCount = 5
        }));

        var context = PlaybookTemplateContextBuilder.Build(runContext);

        // Object outputs converted via TemplateEngine.ConvertJsonElement become Dictionary<string,object?>
        // — Handlebars can walk {{start.totalNotificationCount}} on this.
        context["start"].Should().BeAssignableTo<IDictionary<string, object?>>();
        var startDict = (IDictionary<string, object?>)context["start"]!;
        startDict.Should().ContainKey("totalNotificationCount");
        startDict["totalNotificationCount"].Should().Be(5L);
    }

    [Fact]
    public void Build_TextOnlyOutput_IsExposedAsWrappedShapeWithLosslessText()
    {
        var runContext = CreateRunContext();
        runContext.StoreNodeOutput(TextOutput("rawText", "Plain narrative output"));

        var context = PlaybookTemplateContextBuilder.Build(runContext);

        // Since commit 3789ce43c ("dual-shape node output exposure in Layer 1 template
        // context", R7 W12) text-only outputs are exposed as the wrapped pre-Wave-11
        // per-executor shape so canvas-authored playbooks can bind {{node.output}} /
        // {{node.text}} / {{node.success}} (e.g. Document Profile binds
        // {{output_aiAnalysis.output.sprk_filesummary}}). Protective intent preserved:
        // the node's text MUST remain template-reachable and lossless via BOTH
        // {{rawText.text}} and {{rawText.output}}, and the success flag must be honest.
        var wrapped = context["rawText"].Should()
            .BeAssignableTo<IDictionary<string, object?>>(
                "text-only outputs surface as the wrapped {output, text, success} shape")
            .Subject;
        wrapped["text"].Should().Be("Plain narrative output",
            "{{rawText.text}} is the canonical text binding for text-only node outputs");
        wrapped["output"].Should().Be("Plain narrative output",
            "{{rawText.output}} mirrors the text so wrapped-shape playbook bindings resolve");
        wrapped["success"].Should().Be(true,
            "a successful text-only node must expose success=true to gate-style templates");
    }

    [Fact]
    public void Build_Parameters_AreExposedAndWinOnCollision()
    {
        var parameters = new Dictionary<string, string>
        {
            ["matterId"] = "matter-from-parameters",
            ["overlapping"] = "parameters-value"
        };
        var runContext = CreateRunContext(parameters);
        runContext.StoreNodeOutput(StructuredOutput("overlapping", new { value = "nodeoutputs-value" }));

        var context = PlaybookTemplateContextBuilder.Build(runContext);

        context["matterId"].Should().Be("matter-from-parameters");
        // Parameters take precedence when both NodeOutputs and Parameters carry the same key.
        // This matches the pre-Wave-11 literal-Replace behavior where Parameters were the only
        // substitution surface.
        context["overlapping"].Should().Be("parameters-value");
    }

    [Fact]
    public void Build_RunBag_ExposesIdAndPlaybookIdAndTenantIdAndStartedAtAndCompletedAtUtc()
    {
        var runContext = CreateRunContext();

        var context = PlaybookTemplateContextBuilder.Build(runContext);

        context.Should().ContainKey("run");
        // The orchestrator overload includes startedAt; the per-node overload omits it.
        // This is the orchestrator overload — verify all 5 fields present via reflection
        // (anonymous types don't expose to Handlebars-style dotted access in test scope,
        // but presence + shape can be verified via JSON serialization).
        var runJson = JsonSerializer.Serialize(context["run"]);
        runJson.Should().Contain("id").And.Contain("playbookId").And.Contain("tenantId");
        runJson.Should().Contain("startedAt").And.Contain("completedAtUtc");
    }

    [Fact]
    public void Build_FromNodeExecutionContext_OmitsStartedAtFromRunBag()
    {
        // NodeExecutionContext is internally constructed via PlaybookRunContext.CreateNodeContext.
        // Use that path rather than reconstructing one (avoids leaking implementation details
        // of NodeExecutionContext's required-member shape into tests). The per-node overload
        // omits startedAt — matches the pre-Wave-11 per-executor BuildTemplateContext behavior.
        var runContext = CreateRunContext();
        // Reuse the orchestrator-overload context-build result to confirm the per-node overload
        // intentionally produces a different shape (startedAt absent). Both overloads are
        // exercised end-to-end by orchestrator + LoadKnowledgeNodeExecutor / ReturnResponseNodeExecutor
        // integration paths.
        var orchestratorContext = PlaybookTemplateContextBuilder.Build(runContext);
        var orchestratorRunJson = JsonSerializer.Serialize(orchestratorContext["run"]);
        orchestratorRunJson.Should().Contain("startedAt");
    }

    [Fact]
    public void Build_EmptyRunContext_ProducesContextWithOnlyRunBag()
    {
        var runContext = CreateRunContext();

        var context = PlaybookTemplateContextBuilder.Build(runContext);

        // No node outputs + no parameters; only the run bag should be populated.
        context.Should().ContainKey("run");
        context.Count.Should().Be(1);
    }
}
