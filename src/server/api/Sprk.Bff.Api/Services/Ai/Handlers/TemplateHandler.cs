using System.Diagnostics;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Reference template for R6 Pillar 2 typed tool handler implementations.
/// </summary>
/// <remarks>
/// <para>
/// <strong>NOT a production handler.</strong> The "Template" suffix flags this as a
/// reference implementation for tasks 101–108 (the 8 typed handlers). It registers itself
/// via auto-discovery (so the contract tests in
/// <c>HandlerContractTestTemplate.cs</c> can exercise it), but it has no Dataverse
/// <c>sprk_analysistool</c> row pointing at <c>"TemplateHandler"</c>, so it is never
/// invoked at runtime.
/// </para>
/// <para>
/// Wave 1 + Wave 2 handler tasks copy this shape and substitute:
/// </para>
/// <list type="number">
/// <item>The class name (e.g., <c>DateExtractorHandler</c>) — auto-discovery picks it up</item>
/// <item><see cref="HandlerId"/> string — must match the C# class name (binding per R6 Pillar 2)</item>
/// <item><see cref="Metadata"/> — Name + Description + Version + supported input types + parameter definitions</item>
/// <item><see cref="SupportedToolTypes"/> — at least one <see cref="ToolType"/> entry so the registry can index the handler</item>
/// <item><see cref="Validate"/> — pre-execution input validation; return <see cref="ToolValidationResult.Failure(string[])"/> when invalid</item>
/// <item><see cref="ExecuteAsync"/> — actual work (deterministic logic for Wave 1; LLM call for Wave 2)</item>
/// <item>(Optional, Wave 2 chat-available handlers): <see cref="IToolHandler.SupportedInvocationContexts"/> override + <see cref="IToolHandler.ValidateChat"/> + <see cref="IToolHandler.ExecuteChatAsync"/></item>
/// </list>
/// <para>
/// <strong>Binding rules (project CLAUDE.md Pillar 2 + ADRs)</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: NO per-handler DI line. Auto-discovery via <see cref="ToolFrameworkExtensions.AddToolHandlersFromAssembly"/>.</item>
/// <item><strong>ADR-013</strong>: Handlers stay in <c>Services/Ai/Handlers/</c> or <c>Services/Ai/Tools/</c>; CRUD callers route through <c>Services/Ai/PublicContracts/</c> facades, NOT directly into handlers.</item>
/// <item><strong>ADR-014</strong>: If the handler caches state, the cache key MUST include <c>tenantId</c>. Use <c>context.TenantId</c> as the per-tenant prefix.</item>
/// <item><strong>ADR-015</strong>: Telemetry emits handler name + outcome + timestamp + deterministic IDs ONLY. NEVER log document content / extracted text / full prompts / full model responses.</item>
/// <item><strong>ADR-016</strong>: For LLM-assisted handlers (Wave 2), respect the per-tenant rate limit via the existing <c>IOpenAiClient</c> wrapper — do NOT call Azure OpenAI directly.</item>
/// <item><strong>Dataverse contract</strong>: Each handler ships a <c>sprk_analysistool</c> row with <c>sprk_handlerclass</c> set to the C# class name (matches <see cref="HandlerId"/>).</item>
/// </list>
/// <para>
/// <strong>See also</strong>: <c>Services/Ai/Handlers/HandlerRegistrationConventions.md</c>
/// for the canonical 4-point contract + worked examples.
/// </para>
/// </remarks>
public sealed class TemplateHandler : IToolHandler
{
    // (1) HandlerId — MUST equal the C# class name. The Dataverse sprk_handlerclass
    //     column routes incoming AnalysisTool rows to this handler via this string.
    /// <inheritdoc />
    public string HandlerId => nameof(TemplateHandler);

    // (2) Metadata — Name + Description + Version (semver) + SupportedInputTypes + Parameters.
    //     Version follows semver so the registry can track compatibility across handler revisions.
    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Template Handler (reference; not a production handler)",
        Description: "Reference implementation for R6 Pillar 2 typed handlers. " +
                     "Tasks 101–108 copy this shape and substitute concrete logic.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: Array.Empty<ToolParameterDefinition>(),
        ConfigurationSchema: null);

    // (3) SupportedToolTypes — at least one entry so the registry can index the handler
    //     under ToolHandlerRegistry.GetHandlersByType. Wave 1 + Wave 2 handlers will declare
    //     the type that matches their Dataverse sprk_analysistool row (e.g., ToolType.Extractor,
    //     ToolType.Classifier, ToolType.Custom).
    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    // (4) Validate — pre-execution validation. Return ToolValidationResult.Failure(...)
    //     with explicit error messages so the orchestrator can surface them clearly. For
    //     Wave 1 deterministic handlers this is usually input-shape checks (regex / structure).
    //     For Wave 2 LLM-assisted handlers, also validate that required scope context is present.
    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
    {
        if (context.Document is null)
            return ToolValidationResult.Failure("Document context is required.");

        if (string.IsNullOrWhiteSpace(context.Document.ExtractedText))
            return ToolValidationResult.Failure("Document extracted text is required.");

        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required (ADR-014 cache-key invariant).");

        return ToolValidationResult.Success();
    }

    // (5) ExecuteAsync — actual work. Returns ToolResult.Ok(...) on success or
    //     ToolResult.Error(...) on failure. Telemetry emits only IDs + outcome + duration
    //     (ADR-015). The handler MUST NOT log context.Document.ExtractedText or any portion
    //     of the model response body.
    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;

        // ────────────────────────────────────────────────────────────────────────────
        // Wave 1 deterministic handler shape:
        //   - read fields from context.Document.ExtractedText
        //   - apply regex / structured parsing / arithmetic
        //   - emit ToolResult.Ok(...) with structured data
        //
        // Wave 2 LLM-assisted handler shape:
        //   - inject IOpenAiClient via constructor (added to ToolFrameworkExtensions
        //     auto-discovery scope)
        //   - call _openAiClient.GetStructuredCompletionRawAsync with a JSON schema so
        //     output is constrained
        //   - validate the JSON, then ToolResult.Ok(...)
        //   - on cancellation: ToolResult.Error(..., ToolErrorCodes.Cancelled, ...)
        //   - on other exceptions: ToolResult.Error(..., ToolErrorCodes.InternalError, ...)
        // ────────────────────────────────────────────────────────────────────────────

        stopwatch.Stop();

        var executionMetadata = new ToolExecutionMetadata
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            ModelCalls = 0,                  // Wave 1 deterministic handlers: 0; Wave 2 LLM: 1+
            ModelName = null                 // Wave 2: actionModel string
        };

        var result = ToolResult.Ok(
            HandlerId,
            tool.Id,
            tool.Name,
            data: new { template = true },   // Wave 1/Wave 2 replace with their typed result
            summary: null,                   // Wave 2 LLM handlers: pass raw model response string
            confidence: 1.0,
            execution: executionMetadata);

        return Task.FromResult(result);
    }

    // (Optional, for Wave 2 chat-available handlers): override the three default interface
    // methods introduced by task 009 to opt into chat invocation.
    //
    // public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;
    //
    // public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    // {
    //     // ADR-015: assert only IDs + handles on `context`; never raw user message text.
    //     if (string.IsNullOrWhiteSpace(context.TenantId))
    //         return ToolValidationResult.Failure("TenantId is required.");
    //     return ToolValidationResult.Success();
    // }
    //
    // public Task<ToolResult> ExecuteChatAsync(
    //     ChatInvocationContext context, AnalysisTool tool, CancellationToken cancellationToken)
    // {
    //     // Parse context.ToolArgumentsJson (validated against AnalysisTool.JsonSchema at the
    //     // adapter boundary in task D-A-10), execute deterministic logic or call the LLM,
    //     // and return ToolResult.Ok / ToolResult.Error.
    //     throw new NotImplementedException("Override per Wave 2 chat-available handler tasks.");
    // }
}
