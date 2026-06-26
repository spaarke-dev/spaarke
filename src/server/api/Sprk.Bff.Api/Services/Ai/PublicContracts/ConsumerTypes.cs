namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Canonical set of consumer-type identifiers used by
/// <see cref="IConsumerRoutingService.ResolveAsync"/>. Each constant is the
/// stable string key that BFF consumers pass when resolving their playbook
/// and that admins set in the <c>sprk_consumertype</c> column of the
/// <c>sprk_playbookconsumer</c> Dataverse table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class exists</b>: <c>sprk_consumertype</c> is free-text on the
/// Dataverse side (NVARCHAR(250)) — admins can type anything in Power Apps.
/// The 2026-06-24 UAT-2 incident (Matter pre-fill broken because the Power
/// Apps form received <c>matter-pre-fil</c> missing the final l) is the
/// failure mode this class defends against on the BFF side. By referencing
/// <c>ConsumerTypes.MatterPreFill</c> rather than the literal string
/// <c>"matter-pre-fill"</c>, callers cannot typo the consumer-type code
/// — the compiler catches it.
/// </para>
/// <para>
/// <b>The Dataverse-side typo class is NOT prevented by this code</b>. A
/// future enhancement (suggestion S-5C from the 2026-06-24 code review)
/// is a startup health log that compares the Dataverse-side consumertypes
/// against this constant list and warns on mismatch. That is queued for
/// task 028e (Phase 1R exit gate).
/// </para>
/// <para>
/// <b>Adding a new consumer type</b>:
/// </para>
/// <list type="number">
///   <item>Add a <c>public const string</c> here.</item>
///   <item>Create the corresponding <c>sprk_playbookconsumer</c> row(s) in
///         Dataverse (or extend <c>scripts/dataverse/Seed-PlaybookConsumers.ps1</c>).</item>
///   <item>Update the relevant consumer (or new consumer) to inject
///         <see cref="IConsumerRoutingService"/> and call
///         <c>ResolveAsync(ConsumerTypes.YourNewType)</c>.</item>
/// </list>
/// </remarks>
public static class ConsumerTypes
{
    /// <summary>
    /// <c>MatterPreFillService</c> (workspace) — pre-fills a new Matter form
    /// from uploaded documents (NFR-07 contract preserved).
    /// </summary>
    public const string MatterPreFill = "matter-pre-fill";

    /// <summary>
    /// <c>ProjectPreFillService</c> (workspace) — pre-fills a new Project
    /// form from uploaded documents (NFR-07 contract preserved).
    /// </summary>
    public const string ProjectPreFill = "project-pre-fill";

    /// <summary>
    /// <c>WorkspaceAiService</c> — generates the workspace tile AI summary
    /// (Document Profile playbook).
    /// </summary>
    public const string AiSummary = "ai-summary";

    /// <summary>
    /// <c>WorkspaceFileEndpoints</c> — file summarization endpoint behind
    /// the Workspace summarize button (Summarize File playbook).
    /// </summary>
    public const string SummarizeFile = "summarize-file";

    /// <summary>
    /// <c>SessionSummarizeOrchestrator</c> — chat-side summarize-document
    /// flow (summarize-document-for-chat@v1 playbook).
    /// </summary>
    public const string ChatSummarize = "chat-summarize";

    /// <summary>
    /// <c>AppOnlyAnalysisService</c> — email analysis pipeline (Email
    /// Analysis playbook, app-only execution context).
    /// </summary>
    public const string EmailAnalysis = "email-analysis";

    /// <summary>
    /// Read-only list of all consumer-type constants. Intended for startup
    /// health-log diffing against Dataverse (chat-routing-redesign-r1 task
    /// 028e exit gate).
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        MatterPreFill,
        ProjectPreFill,
        AiSummary,
        SummarizeFile,
        ChatSummarize,
        EmailAnalysis,
    };
}
