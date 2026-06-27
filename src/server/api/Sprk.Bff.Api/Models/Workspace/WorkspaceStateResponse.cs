using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Workspace;

/// <summary>
/// Response shape for <c>GET /api/workspace/state</c> (R6 Pillar 6a / FR-33).
///
/// <para>
/// Carries the full set of workspace tabs the (tenant, session) tuple owns plus
/// two extension-point fields (<see cref="ActiveTabId"/> + <see cref="UserSelection"/>)
/// reserved for Phase C-G2 (chat tools surface "active tab" via Pillar 6b
/// <c>send_workspace_artifact</c> / <c>update_workspace_tab</c>) and Phase C-G6
/// (user-selection events on the workspace channel — Pillar 6c trace + FR-38).
/// </para>
///
/// <para>
/// Phase C-G1 emits both extension fields as <c>null</c>. The endpoint contract
/// already commits to the shape so downstream consumers (Pillar 9 prompt builder,
/// Pillar 6b chat tools) can take a stable dependency now.
/// </para>
///
/// <para>
/// The <see cref="Tabs"/> list is unfiltered — the raw state. The Pillar 9 prompt
/// builder (task 074) applies the per-widget <c>getAgentVisibleState()</c> +
/// <see cref="WorkspaceTab.VisibleToAssistant"/> filter when composing the agent
/// snapshot, per FR-33's binding "filter logic lives in prompt builder, NOT in
/// endpoint."
/// </para>
/// </summary>
public sealed record WorkspaceStateResponse(
    [property: JsonPropertyName("tabs")] IReadOnlyList<WorkspaceTab> Tabs,
    [property: JsonPropertyName("activeTabId")] string? ActiveTabId,
    [property: JsonPropertyName("userSelection")] object? UserSelection);
