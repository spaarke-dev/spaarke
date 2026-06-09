using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Todo;

/// <summary>
/// Builds the Modern UCI deep link for a <c>sprk_todo</c> record per smart-todo-decoupling-r3
/// FR-25. The URL is populated into Graph <c>linkedResources[0].webUrl</c> so MS To Do users
/// can navigate back to the Spaarke form for the to-do.
/// </summary>
/// <remarks>
/// <para>
/// <b>URL format</b>: <c>{OrgUrl}/apps/{AppId}/r/sprk_todo/{todoId}</c>
/// </para>
/// <para>
/// <b>GUID format choice</b>: the to-do id is rendered as a dashed GUID (.NET <c>"D"</c>
/// specifier, e.g. <c>12345678-1234-1234-1234-123456789012</c>) — NOT <c>"N"</c> (no dashes).
/// Rationale: Power Apps Modern UCI parses both forms but the dashed form is the
/// canonical Dataverse record id rendering and is what every Dataverse Web API response
/// returns; staying consistent with that representation simplifies log correlation and
/// avoids any ambiguity vs the no-dash form used by older Web API <c>/EntitySetName(id)</c>
/// addressing.
/// </para>
/// <para>
/// <b>Configuration sources</b> (per spec A-9, CLAUDE.md §10 product-portability rule):
/// <list type="bullet">
///   <item><c>Spaarke:Environment:OrgUrl</c> — host of the Modern UCI app
///   (e.g. <c>https://spaarkedev1.crm.dynamics.com</c>)</item>
///   <item><c>Spaarke:ModelDrivenApps:DefaultAppId</c> — GUID of the app hosting the
///   <c>sprk_todo</c> form (D-4 / UQ-2)</item>
/// </list>
/// No hardcoded values in source. Both are validated in the constructor; the service is
/// a singleton, so the validation runs on first DI resolution → fail-fast at boot for
/// misconfigured environments.
/// </para>
/// <para>
/// <b>Empty GUID handling</b>: <see cref="BuildTodoUrl"/> throws
/// <see cref="ArgumentException"/> for <see cref="Guid.Empty"/> — this is a programming
/// error (no real to-do has an empty primary key) and would produce an invalid deep link
/// that confuses MS To Do users.
/// </para>
/// </remarks>
public sealed class DeepLinkBuilder : IDeepLinkBuilder
{
    private readonly Uri _orgUrl;
    private readonly Guid _appId;

    /// <summary>
    /// Creates a <see cref="DeepLinkBuilder"/> from validated options. Throws
    /// <see cref="InvalidOperationException"/> with a precise message if either
    /// configuration value is missing or malformed.
    /// </summary>
    /// <param name="options">Options containing org URL + app id. Bound by
    /// <see cref="Infrastructure.DI.TodoSyncModule"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Org URL is missing/blank/malformed or
    /// app id is missing/blank/not a GUID.</exception>
    public DeepLinkBuilder(IOptions<DeepLinkBuilderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value
            ?? throw new InvalidOperationException(
                "DeepLinkBuilderOptions.Value is null. Configuration binding failed.");

        // Validate OrgUrl: must be present + a well-formed absolute http(s) URL.
        if (string.IsNullOrWhiteSpace(value.OrgUrl))
        {
            throw new InvalidOperationException(
                $"DeepLinkBuilder configuration is invalid: '{DeepLinkBuilderOptions.OrgUrlConfigKey}' " +
                "is missing or blank. Set it to the Modern UCI host, e.g. " +
                "'https://spaarkedev1.crm.dynamics.com'. See spec.md A-9 / FR-25.");
        }

        if (!Uri.TryCreate(value.OrgUrl, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"DeepLinkBuilder configuration is invalid: '{DeepLinkBuilderOptions.OrgUrlConfigKey}' " +
                $"= '{value.OrgUrl}' is not a valid absolute http(s) URL. " +
                "Expected form: 'https://<org>.crm.dynamics.com'.");
        }

        // Validate AppId: must be present + parse as a GUID.
        if (string.IsNullOrWhiteSpace(value.AppId))
        {
            throw new InvalidOperationException(
                $"DeepLinkBuilder configuration is invalid: '{DeepLinkBuilderOptions.AppIdConfigKey}' " +
                "is missing or blank. Set it to the canonical Modern UCI app id (GUID) hosting " +
                "the sprk_todo form. See spec.md D-4 / UQ-2.");
        }

        if (!Guid.TryParse(value.AppId, out var appId) || appId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"DeepLinkBuilder configuration is invalid: '{DeepLinkBuilderOptions.AppIdConfigKey}' " +
                $"= '{value.AppId}' is not a valid (non-empty) GUID. " +
                "Expected a Modern UCI app id GUID, e.g. '0a1b2c3d-4e5f-6789-abcd-ef0123456789'.");
        }

        _orgUrl = parsed;
        _appId = appId;
    }

    /// <inheritdoc />
    public Uri BuildTodoUrl(Guid todoId)
    {
        if (todoId == Guid.Empty)
        {
            throw new ArgumentException(
                "todoId must not be Guid.Empty — empty GUIDs cannot resolve to a real sprk_todo record.",
                nameof(todoId));
        }

        // Normalise org URL: strip any trailing slash so we control the segment join.
        var orgBase = _orgUrl.GetLeftPart(UriPartial.Authority); // scheme://host[:port]
        // Use the dashed ("D") GUID format — see <remarks> above for rationale.
        var relative = $"/apps/{_appId:D}/r/sprk_todo/{todoId:D}";
        return new Uri(orgBase + relative, UriKind.Absolute);
    }
}
