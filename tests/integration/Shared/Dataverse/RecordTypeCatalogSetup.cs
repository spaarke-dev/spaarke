using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;

/// <summary>
/// #2191 review K2 — the ADR-024 record-type catalog (<c>sprk_recordtype_ref</c>) on an app-only
/// <see cref="IGenericEntityService"/> mock: one active row for <c>sprk_matter</c> and one for <c>sprk_project</c>, answered by
/// logical name (the Office quick-create derives a project's filed-under pair from it) and by id (the secure-root plan reads
/// the pair's type back from it). A host whose mock does not answer it models an environment without the rows, where a
/// project created from a matter or project is refused.
/// </summary>
/// <remarks>Global namespace, like <see cref="MappingSourceCallerReadDouble"/>.</remarks>
public static class RecordTypeCatalogSetup
{
    /// <summary>The catalog's <c>sprk_matter</c> row.</summary>
    public static readonly Guid MatterTypeId = Guid.Parse("7e7e7e7e-0000-4000-8000-000000002191");

    /// <summary>The catalog's <c>sprk_project</c> row.</summary>
    public static readonly Guid ProjectTypeId = Guid.Parse("7e7e7e7e-0000-4000-8000-000000002192");

    private const string Table = "sprk_recordtype_ref";
    private const string LogicalNameColumn = "sprk_recordlogicalname";

    private static readonly IReadOnlyDictionary<string, Guid> Rows = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
    {
        ["sprk_matter"] = MatterTypeId,
        ["sprk_project"] = ProjectTypeId,
    };

    /// <summary>True for a query on the catalog table.</summary>
    public static bool IsCatalogQuery(QueryExpression? query) => query?.EntityName == Table;

    /// <summary>The catalog's answer to <paramref name="query"/>: the row its logical-name or id condition names, else none.</summary>
    public static EntityCollection Answer(QueryExpression query)
    {
        ArgumentNullException.ThrowIfNull(query);
        foreach (var condition in query.Criteria.Conditions)
        {
            foreach (var value in condition.Values)
            {
                if (condition.AttributeName == LogicalNameColumn && value is string name && Rows.TryGetValue(name, out var id))
                    return new EntityCollection(new List<Entity> { Row(name.ToLowerInvariant(), id) });

                if (condition.AttributeName == Table + "id" && value is Guid asked
                    && Rows.FirstOrDefault(r => r.Value == asked) is { Key: { } table })
                    return new EntityCollection(new List<Entity> { Row(table, asked) });
            }
        }

        return new EntityCollection();
    }

    /// <summary>Answers every catalog query on <paramref name="entities"/> with <see cref="Answer"/>.</summary>
    public static Mock<IGenericEntityService> AnswersRecordTypeCatalog(this Mock<IGenericEntityService> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => IsCatalogQuery(q)), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) => Task.FromResult(Answer(query)));
        return entities;
    }

    private static Entity Row(string table, Guid id) =>
        new(Table, id) { [Table + "id"] = id, [LogicalNameColumn] = table };
}
