using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Shared test helpers for the D-P12 (task 022) Insights-mode node executor tests.
/// Builds a minimal <see cref="NodeExecutionContext"/> sufficient for the new five nodes
/// (which do NOT require Document context, tool scopes, or model settings).
/// </summary>
internal static class InsightsNodeTestHelpers
{
    public const string DefaultTenantId = "test-tenant";

    public static NodeExecutionContext CreateContext(
        ExecutorType actionType,
        string? configJson,
        string outputVariable = "result",
        IDictionary<string, NodeOutput>? previousOutputs = null,
        string tenantId = DefaultTenantId,
        Guid? nodeId = null,
        Guid? runId = null,
        Guid? playbookId = null,
        string? nodeName = null,
        IDictionary<string, string>? parameters = null)
    {
        var actionId = Guid.NewGuid();
        var node = new PlaybookNodeDto
        {
            Id = nodeId ?? Guid.NewGuid(),
            PlaybookId = playbookId ?? Guid.NewGuid(),
            ActionId = actionId,
            Name = nodeName ?? $"{actionType} Node",
            ExecutionOrder = 1,
            OutputVariable = outputVariable,
            ConfigJson = configJson,
            IsActive = true
        };
        var action = new AnalysisAction
        {
            Id = actionId,
            Name = actionType.ToString(),
            ExecutorType = actionType
        };
        return new NodeExecutionContext
        {
            RunId = runId ?? Guid.NewGuid(),
            PlaybookId = node.PlaybookId,
            Node = node,
            Action = action,
            ExecutorType = actionType,
            Scopes = new ResolvedScopes(Array.Empty<AnalysisSkill>(), Array.Empty<AnalysisKnowledge>(), Array.Empty<AnalysisTool>()),
            TenantId = tenantId,
            PreviousOutputs = previousOutputs is null
                ? new Dictionary<string, NodeOutput>()
                : new Dictionary<string, NodeOutput>(previousOutputs),
            Parameters = parameters is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(parameters)
        };
    }
}
