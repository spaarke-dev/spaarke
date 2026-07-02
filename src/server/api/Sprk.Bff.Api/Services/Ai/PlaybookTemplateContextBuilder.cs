using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Builds the merged Handlebars template context dictionary for a playbook run —
/// the Layer 1 shared helper of the Wave 11 two-layer architecture (R7 task 111).
/// </summary>
/// <remarks>
/// <para>
/// Before Wave 11, multiple executors (LoadKnowledgeNodeExecutor,
/// ReturnResponseNodeExecutor) each had a private byte-for-byte-duplicate
/// <c>BuildTemplateContext</c> method that walked <see cref="PlaybookRunContext.NodeOutputs"/>
/// + added a <c>run</c> metadata bag. The orchestrator's
/// <see cref="PlaybookOrchestrationService.ApplyConfigJsonTemplates"/> did NOT use any of this —
/// it only did literal <c>{{paramName}}</c> string-replace from <see cref="PlaybookRunContext.Parameters"/>.
/// </para>
/// <para>
/// This helper centralizes the template-context construction. All callers (orchestrator + per-executor
/// resolvers) build context the same way. Wave 11 uses <see cref="ITemplateEngine"/> + this helper
/// at the orchestrator's <c>ApplyConfigJsonTemplates</c> so every executor's configJson gets
/// uniform <c>{{X.Y.Z}}</c> resolution against:
/// </para>
/// <list type="bullet">
///   <item><strong>Parameters</strong>: the run's <see cref="PlaybookRunContext.Parameters"/>
///     dictionary (BFF wrapper-provided, string-typed)</item>
///   <item><strong>NodeOutputs</strong>: each prior node's <see cref="NodeOutput"/> exposed under
///     its <c>OutputVariable</c> name. Object-shaped outputs come from
///     <see cref="NodeOutput.StructuredData"/> converted via
///     <see cref="TemplateEngine.ConvertJsonElement"/>; text-only outputs come from
///     <see cref="NodeOutput.TextContent"/></item>
///   <item><strong><c>run</c> metadata bag</strong>: id + playbookId + tenantId + startedAt +
///     completedAtUtc (the latter is "now" at context-build time, supporting
///     <c>{{run.completedAtUtc}}</c> references in terminal binding templates)</item>
/// </list>
/// <para>
/// Collision semantics: Parameters take precedence over NodeOutputs (matches the prior literal-Replace
/// behavior; a node named the same as a wrapper parameter still resolves to the parameter value).
/// </para>
/// </remarks>
public static class PlaybookTemplateContextBuilder
{
    /// <summary>
    /// Builds the merged template context dictionary from a run context. Used by the
    /// orchestrator's <see cref="PlaybookOrchestrationService.ApplyConfigJsonTemplates"/>
    /// (Layer 1 of Wave 11 two-layer architecture).
    /// </summary>
    /// <param name="runContext">The active playbook run context.</param>
    /// <returns>
    /// A non-null dictionary suitable as the <c>context</c> argument to
    /// <see cref="ITemplateEngine.Render"/>. Always returns a fresh dictionary (caller may
    /// mutate it for per-iteration overlays without affecting subsequent renders).
    /// </returns>
    public static Dictionary<string, object?> Build(PlaybookRunContext runContext)
    {
        ArgumentNullException.ThrowIfNull(runContext);

        return BuildCore(
            runContext.NodeOutputs,
            runContext.Parameters,
            runContext.RunId,
            runContext.PlaybookId,
            runContext.TenantId,
            runContext.StartedAt,
            runContext.Document);
    }

    /// <summary>
    /// Builds the merged template context dictionary from a per-node execution context. Used by
    /// node executors (LoadKnowledgeNodeExecutor, ReturnResponseNodeExecutor) that perform
    /// their own per-binding template rendering after the orchestrator's Layer 1 has already
    /// resolved configJson.
    /// </summary>
    /// <param name="nodeContext">The per-node execution context.</param>
    /// <returns>
    /// A non-null dictionary suitable as the <c>context</c> argument to
    /// <see cref="ITemplateEngine.Render"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The per-node overload does NOT have access to <see cref="PlaybookRunContext.StartedAt"/>
    /// (NodeExecutionContext doesn't carry it). The <c>run.startedAt</c> field is therefore
    /// omitted from the <c>run</c> bag in this overload — matches the pre-Wave-11 behavior of
    /// the per-executor BuildTemplateContext methods this overload replaces.
    /// </para>
    /// </remarks>
    public static Dictionary<string, object?> Build(NodeExecutionContext nodeContext)
    {
        ArgumentNullException.ThrowIfNull(nodeContext);

        return BuildCore(
            nodeContext.PreviousOutputs,
            nodeContext.Parameters,
            nodeContext.RunId,
            nodeContext.PlaybookId,
            nodeContext.TenantId,
            startedAt: null,
            nodeContext.Document);
    }

    /// <summary>
    /// Shared implementation backing both <see cref="Build(PlaybookRunContext)"/> and
    /// <see cref="Build(Nodes.NodeExecutionContext)"/>. Centralizes the merge order
    /// (NodeOutputs → Parameters → run bag) and JsonElement-to-traversable conversion.
    /// </summary>
    private static Dictionary<string, object?> BuildCore(
        IReadOnlyDictionary<string, NodeOutput> nodeOutputs,
        IReadOnlyDictionary<string, string>? parameters,
        Guid runId,
        Guid playbookId,
        string tenantId,
        DateTimeOffset? startedAt,
        DocumentContext? document = null)
    {
        var context = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Expose the run's DocumentContext (when supplied by the caller) as a top-level
        // "document" bag. Playbook config templates like {{document.id}}, {{document.name}},
        // {{document.fileName}}, {{document.extractedText}} resolve here. Missing when the
        // request path is document-less; templates against a null document render empty.
        if (document is not null)
        {
            context["document"] = new
            {
                id = document.DocumentId.ToString(),
                name = document.Name,
                fileName = document.FileName,
                extractedText = document.ExtractedText,
            };
        }

        // 1. NodeOutputs first — each prior node's OutputVariable becomes a top-level key.
        //    Object outputs (StructuredData JsonElement) are converted to traversable
        //    Dictionary/List/primitives so Handlebars can walk {{nodeName.field.subfield}}.
        //    Text-only outputs surface as plain strings.
        //
        //    Dual-shape exposure — the same OutputVariable is registered TWICE:
        //      (a) Raw shape: {{nodeName.field}} — top-level dict keys (Wave 11 narrator
        //          playbooks, e.g. Daily Briefing, are authored against this shape).
        //      (b) Wrapped shape: {{nodeName.output.field}} + {{nodeName.text}} +
        //          {{nodeName.success}} — matches the pre-Wave-11 per-executor
        //          BuildTemplateContext convention still used by playbooks authored via
        //          the canvas Update Record / Deliver To Index / etc. blocks (e.g. Document
        //          Profile playbook binds `{{output_aiAnalysis.output.sprk_filesummary}}`).
        //    Layer 1 runs first and would render the wrapped-shape references to empty
        //    if only the raw shape were exposed. Adding both keeps both playbook
        //    conventions working with no author-side changes.
        foreach (var (varName, output) in nodeOutputs)
        {
            if (string.IsNullOrEmpty(varName))
            {
                continue;
            }

            object? rawValue;
            if (output.StructuredData is JsonElement element && element.ValueKind != JsonValueKind.Null && element.ValueKind != JsonValueKind.Undefined)
            {
                rawValue = TemplateEngine.ConvertJsonElement(element);
            }
            else if (!string.IsNullOrEmpty(output.TextContent))
            {
                rawValue = output.TextContent;
            }
            else
            {
                // Output produced no usable content — register as null so missing-reference
                // {{nodeName.field}} renders as empty (graceful per Handlebars config).
                rawValue = null;
            }

            // (a) Raw shape — legacy Wave 11 narrator convention
            context[varName] = rawValue;

            // (b) Wrapped shape — pre-Wave-11 per-executor BuildTemplateContext convention
            //     ({{nodeName.output.X}} / .text / .success). The key differs from (a) —
            //     add "_wrapped" suffix, then… actually, Handlebars can't have two keys
            //     with the same name pointing to different objects. Instead, if raw is
            //     an object/dict, we embed the wrapper INTO it so both paths resolve:
            //       - {{nodeName.field}} → dict.field (via rawValue path)
            //       - {{nodeName.output.field}} → dict["output"].field (via merged wrapper)
            //     For non-dict raw (text/null), we replace with the wrapper record only.
            if (rawValue is Dictionary<string, object?> rawDict)
            {
                if (!rawDict.ContainsKey("output"))
                    rawDict["output"] = rawValue;
                if (!rawDict.ContainsKey("text"))
                    rawDict["text"] = output.TextContent;
                if (!rawDict.ContainsKey("success"))
                    rawDict["success"] = output.Success;
                // context[varName] already points to rawDict — no reassignment needed
            }
            else
            {
                // Text-only or null output: expose only the wrapped shape (raw text
                // access {{nodeName}} still works because Handlebars will stringify
                // the wrapper object; the more useful access is {{nodeName.text}}).
                context[varName] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["output"] = rawValue,
                    ["text"] = output.TextContent,
                    ["success"] = output.Success,
                };
            }
        }

        // 2. Parameters second — wrapper-provided scalars win on key collision (matches the
        //    prior literal-Replace behavior where Parameters were the only substitution surface).
        if (parameters is { Count: > 0 })
        {
            foreach (var kvp in parameters)
            {
                if (string.IsNullOrEmpty(kvp.Key))
                {
                    continue;
                }
                context[kvp.Key] = kvp.Value;
            }
        }

        // 3. The "run" metadata bag — exposes run lifecycle + identity for terminal-binding
        //    templates like {{run.completedAtUtc}}. The per-node overload omits startedAt
        //    (not carried by NodeExecutionContext); the orchestrator overload includes it.
        if (startedAt.HasValue)
        {
            context["run"] = new
            {
                id = runId.ToString(),
                playbookId = playbookId.ToString(),
                tenantId,
                startedAt = startedAt.Value.ToString("o"),
                completedAtUtc = DateTimeOffset.UtcNow.ToString("o")
            };
        }
        else
        {
            context["run"] = new
            {
                id = runId.ToString(),
                playbookId = playbookId.ToString(),
                tenantId,
                completedAtUtc = DateTimeOffset.UtcNow.ToString("o")
            };
        }

        return context;
    }
}
