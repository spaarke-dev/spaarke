namespace Sprk.Bff.Api.Services.Todo;

/// <summary>
/// Configuration options for <see cref="DeepLinkBuilder"/>. Composes values from TWO
/// configuration sub-paths (per spec.md A-9 / FR-25):
/// <list type="bullet">
///   <item><c>Spaarke:Environment:OrgUrl</c> — the model-driven app host (e.g.
///   <c>https://spaarkedev1.crm.dynamics.com</c>). Environment-specific.</item>
///   <item><c>Spaarke:ModelDrivenApps:DefaultAppId</c> — the canonical app id (GUID) that
///   hosts the <c>sprk_todo</c> form. Environment-specific (D-4 / UQ-2).</item>
/// </list>
/// Bound by <see cref="Infrastructure.DI.TodoSyncModule"/> via <c>services.Configure&lt;...&gt;</c>
/// against the two sub-paths assembled into one POCO at registration time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Product portability rule (CLAUDE.md §10, spec.md NFR / A-9)</b>: NO hardcoded org URL
/// or app id may appear in source. Both values are read from configuration. The
/// <see cref="DeepLinkBuilder"/> constructor validates them and fails fast on missing /
/// malformed input to surface mis-configured environments at startup.
/// </para>
/// </remarks>
public sealed class DeepLinkBuilderOptions
{
    /// <summary>
    /// Configuration section root. The two leaf paths are
    /// <c>{SectionName}:Environment:OrgUrl</c> and
    /// <c>{SectionName}:ModelDrivenApps:DefaultAppId</c>.
    /// </summary>
    public const string SectionName = "Spaarke";

    /// <summary>
    /// Configuration leaf paths under <see cref="SectionName"/>.
    /// </summary>
    public const string OrgUrlConfigKey = "Spaarke:Environment:OrgUrl";

    /// <inheritdoc cref="OrgUrlConfigKey"/>
    public const string AppIdConfigKey = "Spaarke:ModelDrivenApps:DefaultAppId";

    /// <summary>
    /// Modern UCI host (org base URL). Example:
    /// <c>https://spaarkedev1.crm.dynamics.com</c>. Trailing slash is normalised away
    /// in <see cref="DeepLinkBuilder.BuildTodoUrl"/>.
    /// </summary>
    public string OrgUrl { get; set; } = string.Empty;

    /// <summary>
    /// Canonical Modern UCI app id (GUID) hosting the <c>sprk_todo</c> form. Per
    /// spec A-9: if multiple model-driven apps host <c>sprk_todo</c>, one canonical
    /// app id is chosen per environment.
    /// </summary>
    public string AppId { get; set; } = string.Empty;
}
