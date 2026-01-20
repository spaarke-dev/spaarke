using System.Text;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Services.Ai.SemanticSearch;

/// <summary>
/// Builds OData filter expressions for Azure AI Search semantic search queries.
/// Handles tenant isolation, entity scoping, document filtering, and optional filters.
/// </summary>
/// <remarks>
/// <para>
/// Filter composition always includes tenant isolation:
/// <code>tenantId eq '{tenantId}' AND {scope-filter} AND {optional-filters}</code>
/// </para>
/// <para>
/// All user input is escaped to prevent OData filter injection attacks.
/// </para>
/// </remarks>
public static class SearchFilterBuilder
{
    /// <summary>
    /// Builds a complete OData filter expression for semantic search.
    /// </summary>
    /// <param name="tenantId">Required tenant ID for tenant isolation.</param>
    /// <param name="scope">Search scope: "entity" or "documentIds".</param>
    /// <param name="entityType">Entity type when scope is "entity".</param>
    /// <param name="entityId">Entity ID when scope is "entity".</param>
    /// <param name="documentIds">Document IDs when scope is "documentIds".</param>
    /// <param name="filters">Optional additional filters.</param>
    /// <returns>Complete OData filter string.</returns>
    /// <exception cref="ArgumentException">Thrown when required parameters are missing for the specified scope.</exception>
    public static string BuildFilter(
        string tenantId,
        string scope,
        string? entityType,
        string? entityId,
        IReadOnlyList<string>? documentIds,
        SearchFilters? filters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId, nameof(tenantId));
        ArgumentException.ThrowIfNullOrWhiteSpace(scope, nameof(scope));

        var filterParts = new List<string>();

        // 1. Tenant isolation filter (ALWAYS required)
        filterParts.Add(BuildTenantFilter(tenantId));

        // 2. Scope-specific filter
        filterParts.Add(BuildScopeFilter(scope, entityType, entityId, documentIds));

        // 3. Optional filters
        if (filters is not null)
        {
            AddOptionalFilters(filterParts, filters);
        }

        return string.Join(" and ", filterParts);
    }

    /// <summary>
    /// Builds the tenant isolation filter.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <returns>OData filter clause for tenant isolation.</returns>
    public static string BuildTenantFilter(string tenantId)
    {
        return $"tenantId eq '{EscapeODataValue(tenantId)}'";
    }

    /// <summary>
    /// Builds the scope-specific filter based on search scope.
    /// </summary>
    /// <param name="scope">Search scope: "entity" or "documentIds".</param>
    /// <param name="entityType">Entity type when scope is "entity".</param>
    /// <param name="entityId">Entity ID when scope is "entity".</param>
    /// <param name="documentIds">Document IDs when scope is "documentIds".</param>
    /// <returns>OData filter clause for the specified scope.</returns>
    /// <exception cref="ArgumentException">Thrown when required parameters are missing for the specified scope.</exception>
    public static string BuildScopeFilter(
        string scope,
        string? entityType,
        string? entityId,
        IReadOnlyList<string>? documentIds)
    {
        return scope.ToLowerInvariant() switch
        {
            "entity" => BuildEntityScopeFilter(entityType, entityId),
            "documentids" => BuildDocumentIdsScopeFilter(documentIds),
            _ => throw new ArgumentException($"Invalid scope: {scope}. Must be 'entity' or 'documentIds'.", nameof(scope))
        };
    }

    /// <summary>
    /// Builds the entity scope filter for parent entity filtering.
    /// </summary>
    /// <param name="entityType">The parent entity type (matter, project, invoice, account, contact).</param>
    /// <param name="entityId">The parent entity ID (GUID).</param>
    /// <returns>OData filter clause for entity scope.</returns>
    /// <exception cref="ArgumentException">Thrown when entityType or entityId is missing.</exception>
    public static string BuildEntityScopeFilter(string? entityType, string? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("entityType is required when scope is 'entity'.", nameof(entityType));
        }

        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new ArgumentException("entityId is required when scope is 'entity'.", nameof(entityId));
        }

        return $"parentEntityType eq '{EscapeODataValue(entityType.ToLowerInvariant())}' and parentEntityId eq '{EscapeODataValue(entityId)}'";
    }

    /// <summary>
    /// Builds the documentIds scope filter using search.in() for efficient filtering.
    /// </summary>
    /// <param name="documentIds">List of document IDs to filter by.</param>
    /// <returns>OData filter clause using search.in().</returns>
    /// <exception cref="ArgumentException">Thrown when documentIds is null or empty.</exception>
    public static string BuildDocumentIdsScopeFilter(IReadOnlyList<string>? documentIds)
    {
        if (documentIds is null || documentIds.Count == 0)
        {
            throw new ArgumentException("documentIds is required when scope is 'documentIds'.", nameof(documentIds));
        }

        // Use search.in() for efficient IN clause
        var escapedIds = documentIds.Select(id => EscapeODataValue(id));
        var idList = string.Join(",", escapedIds);
        return $"search.in(documentId, '{idList}', ',')";
    }

    /// <summary>
    /// Builds filter for document types using search.in().
    /// </summary>
    /// <param name="documentTypes">List of document types to filter by.</param>
    /// <returns>OData filter clause, or null if no document types specified.</returns>
    public static string? BuildDocumentTypesFilter(IReadOnlyList<string>? documentTypes)
    {
        if (documentTypes is null || documentTypes.Count == 0)
        {
            return null;
        }

        var escapedTypes = documentTypes.Select(t => EscapeODataValue(t));
        var typeList = string.Join(",", escapedTypes);
        return $"search.in(documentType, '{typeList}', ',')";
    }

    /// <summary>
    /// Builds filter for file types using search.in().
    /// </summary>
    /// <param name="fileTypes">List of file types (extensions) to filter by.</param>
    /// <returns>OData filter clause, or null if no file types specified.</returns>
    public static string? BuildFileTypesFilter(IReadOnlyList<string>? fileTypes)
    {
        if (fileTypes is null || fileTypes.Count == 0)
        {
            return null;
        }

        var escapedTypes = fileTypes.Select(t => EscapeODataValue(t.ToLowerInvariant()));
        var typeList = string.Join(",", escapedTypes);
        return $"search.in(fileType, '{typeList}', ',')";
    }

    /// <summary>
    /// Builds filter for tags using any() operator.
    /// </summary>
    /// <param name="tags">List of tags to filter by (matches any).</param>
    /// <returns>OData filter clause, or null if no tags specified.</returns>
    public static string? BuildTagsFilter(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return null;
        }

        // Build OR conditions for each tag: tags/any(t: t eq 'tag1') or tags/any(t: t eq 'tag2')
        var tagFilters = tags.Select(tag => $"tags/any(t: t eq '{EscapeODataValue(tag)}')");
        return $"({string.Join(" or ", tagFilters)})";
    }

    /// <summary>
    /// Builds filter for date range on specified field.
    /// </summary>
    /// <param name="dateRange">Date range specification with field, from, and to.</param>
    /// <returns>OData filter clause, or null if no date range specified.</returns>
    public static string? BuildDateRangeFilter(DateRangeFilter? dateRange)
    {
        if (dateRange is null)
        {
            return null;
        }

        var field = dateRange.Field?.ToLowerInvariant() switch
        {
            "createdat" => "createdAt",
            "updatedat" => "updatedAt",
            null => "createdAt", // Default to createdAt
            _ => dateRange.Field
        };

        var parts = new List<string>();

        if (dateRange.From.HasValue)
        {
            parts.Add($"{field} ge {dateRange.From.Value:O}");
        }

        if (dateRange.To.HasValue)
        {
            parts.Add($"{field} le {dateRange.To.Value:O}");
        }

        return parts.Count > 0 ? $"({string.Join(" and ", parts)})" : null;
    }

    /// <summary>
    /// Escapes a value for use in OData filter expressions.
    /// Prevents filter injection by escaping single quotes.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped value safe for OData filters.</returns>
    public static string EscapeODataValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // In OData, single quotes are escaped by doubling them
        return value.Replace("'", "''");
    }

    /// <summary>
    /// Adds optional filters to the filter parts list.
    /// </summary>
    private static void AddOptionalFilters(List<string> filterParts, SearchFilters filters)
    {
        var documentTypesFilter = BuildDocumentTypesFilter(filters.DocumentTypes);
        if (documentTypesFilter is not null)
        {
            filterParts.Add(documentTypesFilter);
        }

        var fileTypesFilter = BuildFileTypesFilter(filters.FileTypes);
        if (fileTypesFilter is not null)
        {
            filterParts.Add(fileTypesFilter);
        }

        var tagsFilter = BuildTagsFilter(filters.Tags);
        if (tagsFilter is not null)
        {
            filterParts.Add(tagsFilter);
        }

        var dateRangeFilter = BuildDateRangeFilter(filters.DateRange);
        if (dateRangeFilter is not null)
        {
            filterParts.Add(dateRangeFilter);
        }
    }
}
