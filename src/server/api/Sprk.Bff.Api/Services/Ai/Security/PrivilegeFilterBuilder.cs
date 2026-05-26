namespace Sprk.Bff.Api.Services.Ai.Security;

/// <summary>
/// Builds OData filter expressions for privilege_group_ids security filtering in Azure AI Search.
/// </summary>
/// <remarks>
/// The resulting filter enforces cross-matter retrieval isolation at the search layer (AIPU2-027).
/// Public documents (privilege_group_ids collection is empty) are always included via
/// the "not privilege_group_ids/any()" clause so that unrestricted knowledge is available to all users.
///
/// Filter structure for a user with groups [g1, g2]:
/// <code>
///   (privilege_group_ids/any(g: g eq 'g1') or privilege_group_ids/any(g: g eq 'g2')
///    or not privilege_group_ids/any())
/// </code>
///
/// Filter structure for a user with NO groups (fail-closed — returns only public documents):
/// <code>
///   not privilege_group_ids/any()
/// </code>
///
/// IMPORTANT: When the caller determines the user has no group memberships AND this is due to a
/// resolution error, the caller must NOT use this filter — it should return empty results instead.
/// Use <see cref="BuildFilter"/> only when groups have been successfully resolved (even if empty
/// because the user genuinely has no groups).
/// </remarks>
public static class PrivilegeFilterBuilder
{
    private const string FieldName = "privilege_group_ids";

    /// <summary>
    /// Builds the OData filter expression for privilege_group_ids security filtering.
    /// </summary>
    /// <param name="userGroupIds">
    /// The Azure AD group object IDs the user belongs to.
    /// Pass an empty list for users with no group memberships — this produces a filter that
    /// returns only public documents (those with no privilege_group_ids set).
    /// </param>
    /// <returns>
    /// An OData filter string ready to be ANDed with other search filters.
    /// Never null or empty — always returns a valid, complete filter expression.
    /// </returns>
    public static string BuildFilter(IReadOnlyList<string> userGroupIds)
    {
        ArgumentNullException.ThrowIfNull(userGroupIds);

        // Public documents clause — always included so unrestricted content is accessible
        const string publicClause = $"not {FieldName}/any()";

        if (userGroupIds.Count == 0)
        {
            // User has no group memberships — only public documents are accessible
            return publicClause;
        }

        // Build one any() clause per group ID.
        // Azure AI Search OData does not support search.in() on Collection(Edm.String) with any(),
        // so we enumerate individual any() predicates joined with OR.
        var groupClauses = userGroupIds
            .Select(id => $"{FieldName}/any(g: g eq '{EscapeODataValue(id)}')")
            .ToList();

        // Combine: (group_clause_1 or group_clause_2 ... or not privilege_group_ids/any())
        groupClauses.Add(publicClause);

        return $"({string.Join(" or ", groupClauses)})";
    }

    /// <summary>
    /// Escapes a value for use in an OData string literal by doubling single quotes.
    /// Azure AD group GUIDs never contain single quotes, but this guard is included
    /// for defence-in-depth.
    /// </summary>
    private static string EscapeODataValue(string value)
    {
        return value.Replace("'", "''");
    }
}
