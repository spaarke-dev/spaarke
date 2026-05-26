using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Persisted snapshot of a single non-Home workspace tab (NFR-09).
///
/// Wire shape (System.Text.Json camelCase): <c>{ id, widgetType, widgetData, displayName }</c>.
///
/// <para><b>Why no <c>kind</c> field</b>: the Home tab is never persisted — it is recreated
/// by <c>ensureHomeTab()</c> on every <c>WorkspacePane</c> mount. Only widget-kind tabs reach
/// this DTO, so <c>kind</c> would be a constant and is omitted.</para>
///
/// <para><b>Why <see cref="JsonElement"/> for <see cref="WidgetData"/></b>: the BFF does not
/// interpret the widget payload — it stores it opaquely and returns it verbatim on restore.
/// <see cref="JsonElement"/> preserves arbitrary client-defined JSON without forcing a typed
/// contract that would couple the BFF to widget-specific schemas.</para>
///
/// <para><b>Placement (CLAUDE.md §10 / ADR-013)</b>: in-process DTO on the existing
/// <see cref="SessionPersistenceService"/> pipeline. No new DI module, no new service, no new
/// NuGet packages — purely additive to <see cref="StoredSession"/>. Cosmos partition key
/// <c>/tenantId</c> is unchanged (ADR-015).</para>
/// </summary>
/// <param name="Id">Tab identifier (client-generated, stable across persist/restore).</param>
/// <param name="WidgetType">Widget kind to re-resolve via the client widget registry on restore.</param>
/// <param name="WidgetData">Opaque widget payload pass-through; null if the widget has no state.</param>
/// <param name="DisplayName">Tab title displayed in the workspace tab strip.</param>
public record StoredWorkspaceTab(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("widgetType")] string WidgetType,
    [property: JsonPropertyName("widgetData")] JsonElement? WidgetData,
    [property: JsonPropertyName("displayName")] string DisplayName);
