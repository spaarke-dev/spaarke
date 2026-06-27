namespace Sprk.Bff.Api.Services.Todo;

/// <summary>
/// Builds the Modern UCI deep link for a <c>sprk_todo</c> record so users can click through
/// from Microsoft To Do (<c>linkedResources[0].webUrl</c>) back to the Spaarke form.
/// Per smart-todo-decoupling-r3 FR-25.
/// </summary>
/// <remarks>
/// <para>
/// URL shape: <c>{OrgUrl}/main.aspx?appid={AppId}&amp;pagetype=entityrecord&amp;etn=sprk_todo&amp;id={todoId}</c>
/// is the legacy CRM scheme. This builder uses the <b>Modern UCI</b> short scheme:
/// <c>{OrgUrl}/apps/{AppId}/r/sprk_todo/{todoId}</c> — see <c>DeepLinkBuilder</c> for the
/// canonical format and rationale.
/// </para>
/// <para>
/// Configuration is supplied via <see cref="DeepLinkBuilderOptions"/> (org URL + app id).
/// No hardcoded values — see CLAUDE.md §10 / NFR-01 product-portability rule.
/// </para>
/// </remarks>
public interface IDeepLinkBuilder
{
    /// <summary>
    /// Builds the Modern UCI deep link for the given <paramref name="todoId"/>.
    /// </summary>
    /// <param name="todoId">Primary key of the <c>sprk_todo</c> record. Must not be
    /// <see cref="Guid.Empty"/> — empty GUIDs throw <see cref="ArgumentException"/>.</param>
    /// <returns>The absolute URI suitable for Graph <c>linkedResources[0].webUrl</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="todoId"/> is
    /// <see cref="Guid.Empty"/>.</exception>
    Uri BuildTodoUrl(Guid todoId);
}
