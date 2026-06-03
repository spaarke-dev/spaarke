namespace Sprk.Bff.Api.Services.Ai.Insights;

/// <summary>
/// Configuration options for the Insights Engine universal-ingest path
/// (<see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.IInsightsAi.RunIngestAsync"/>).
/// Bound at <c>Spaarke:Insights:Ingest</c> in <c>appsettings.json</c> / App Service config /
/// Key Vault.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.5 r2 Wave C4 (task 023)</b>: enables the rewire of
/// <c>InsightsOrchestrator.RunIngestAsync</c> from the legacy code-defined
/// <see cref="Ingest.IIngestOrchestrator"/> path to the JPS-defined
/// <c>universal-ingest@v1</c> playbook invoked via
/// <see cref="IPlaybookOrchestrationService.ExecuteAppOnlyAsync"/>.
/// </para>
/// <para>
/// <b>Config shape</b>:
/// <code>
/// {
///   "Spaarke": {
///     "Insights": {
///       "Ingest": {
///         "UseUniversalIngestPlaybook": true,
///         "UniversalIngestPlaybookId": "00000000-0000-0000-0000-000000000000"
///       }
///     }
///   }
/// }
/// </code>
/// </para>
/// <para>
/// <b>Feature flag — runtime switch, not a DI flag</b>: the
/// <see cref="UseUniversalIngestPlaybook"/> flag is consulted inside
/// <c>InsightsOrchestrator.RunIngestAsync</c> at invocation time — NOT at DI registration
/// time. Both the legacy <see cref="Ingest.IIngestOrchestrator"/> path and the new
/// <see cref="IPlaybookOrchestrationService"/> path are unconditionally registered (no
/// <c>if (flag)</c> blocks in any <c>*Module.cs</c>). Per ADR-030 + bff-extensions §F.1
/// "Asymmetric-Registration Tier 1.5 Anti-Pattern": this design intentionally avoids the
/// pattern — there is NO conditional registration, so there is NO asymmetric-registration
/// risk. The flag is a runtime-only choice between two real implementations.
/// </para>
/// <para>
/// <b>Fallback semantics</b>:
/// <list type="bullet">
///   <item><see cref="UseUniversalIngestPlaybook"/> = <c>true</c> AND
///   <see cref="UniversalIngestPlaybookId"/> is non-empty → invoke the playbook via
///   <see cref="IPlaybookOrchestrationService.ExecuteAppOnlyAsync"/>.</item>
///   <item><see cref="UseUniversalIngestPlaybook"/> = <c>false</c> OR
///   <see cref="UniversalIngestPlaybookId"/> = <see cref="System.Guid.Empty"/> → delegate
///   to the legacy <see cref="Ingest.IIngestOrchestrator"/>. The orchestrator logs an
///   Information event recording the path chosen so observability tooling can attribute
///   ingest behavior unambiguously.</item>
/// </list>
/// </para>
/// <para>
/// <b>Deploy ordering</b> (per bff-extensions §F.4 deploy coordination): runtime
/// activation of the playbook path requires THREE things to land together:
/// <list type="number">
///   <item>Wave C1 (task 020) BFF deploy — ships
///   <c>SanitizerNodeExecutor</c> + <c>ObservationEmitterNodeExecutor</c> + the engine
///   patches (predicate:"in" + branch-aware skip).</item>
///   <item>Wave C1 Dataverse deploy — the <c>universal-ingest@v1</c> playbook row + 6
///   node rows + 4 NEW <c>sprk_analysisaction</c> rows (INS-SANI, INS-L1C, INS-L2X,
///   INS-OBSE) — already authored, NOT yet deployed.</item>
///   <item>Wave C4 (task 023) — this rewire — BFF deploy with
///   <see cref="UniversalIngestPlaybookId"/> populated and
///   <see cref="UseUniversalIngestPlaybook"/> = <c>true</c>.</item>
/// </list>
/// Until all three are in place, set <see cref="UseUniversalIngestPlaybook"/> = <c>false</c>
/// (or leave the playbook Guid empty) so the orchestrator silently falls back to the
/// legacy path — no runtime behavior change.
/// </para>
/// <para>
/// <b>Wave C-G4 (task 022) horizon</b>: once the playbook path is validated in production,
/// task 022 retires the legacy <see cref="Ingest.IIngestOrchestrator"/> registration and
/// removes the fallback branch. At that point this options class drops
/// <see cref="UseUniversalIngestPlaybook"/> (always-on) and
/// <see cref="UniversalIngestPlaybookId"/> becomes a hard requirement validated at startup.
/// </para>
/// </remarks>
public sealed class InsightsIngestOptions
{
    /// <summary>Configuration section name to bind to.</summary>
    public const string SectionName = "Spaarke:Insights:Ingest";

    /// <summary>
    /// When <c>true</c> AND <see cref="UniversalIngestPlaybookId"/> is non-empty, the
    /// facade invokes the <c>universal-ingest@v1</c> JPS playbook. When <c>false</c> (or
    /// the Guid is empty), the facade delegates to the legacy <see cref="Ingest.IIngestOrchestrator"/>.
    /// Defaults to <c>true</c> — production posture is "playbook-first" once the Guid is
    /// configured.
    /// </summary>
    public bool UseUniversalIngestPlaybook { get; set; } = true;

    /// <summary>
    /// Dataverse <c>sprk_analysisplaybook</c> Guid for the <c>universal-ingest@v1</c>
    /// playbook row in the target environment. Per-env config holds the env-specific Guid
    /// (Dataverse generates a fresh Guid per environment per ADR-027). When unset
    /// (<see cref="System.Guid.Empty"/>) the orchestrator falls back to the legacy path
    /// even if <see cref="UseUniversalIngestPlaybook"/> = <c>true</c> — defensive default
    /// so a misconfigured env can't silently break ingest.
    /// </summary>
    public Guid UniversalIngestPlaybookId { get; set; } = Guid.Empty;
}
