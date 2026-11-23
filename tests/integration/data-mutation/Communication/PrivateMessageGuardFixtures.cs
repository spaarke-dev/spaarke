using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Access;

namespace Sprk.Bff.Api.Tests.DataMutation.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056: what an app-only entity service answers
/// <see cref="PrivateMessageWriteGuard.RefuseUpdateAsync"/>, for compositions whose generic writers update a
/// <c>sprk_communication</c>'s filing and so now ask the guard first. The guard fails closed on a service that gives no answer,
/// so a double that answers nothing refuses every such write; these give it the published schema.
/// </summary>
internal static class PrivateMessageGuardFixtures
{
    private const string Communication = "sprk_communication";

    /// <summary>
    /// The published <c>sprk_communication</c> schema has the three private-context reference columns, so the guard reads the
    /// row. The row read itself is left to the double (a world answers it from its rows; a row it does not hold passes).
    /// </summary>
    public static Mock<IGenericEntityService> WithPrivateContextColumns(this Mock<IGenericEntityService> entities)
    {
        entities.Setup(e => e.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivateContextColumns.All.ToDictionary(
                c => c, c => new ColumnMetadata(c, "Lookup", true, true, null), StringComparer.OrdinalIgnoreCase));
        return entities;
    }

    /// <summary>
    /// For a double with no rows: the schema as <see cref="WithPrivateContextColumns"/>, and the guard's read of a communication
    /// answers a row holding no context reference (an ordinary message). Set up after the double's own query answer, so it wins
    /// for that read only.
    /// </summary>
    public static Mock<IGenericEntityService> AnsweringNoPrivateMessage(this Mock<IGenericEntityService> entities) =>
        entities.AnsweringTheGuardFrom(query => new EntityCollection(new List<Entity> { new(Communication, RowOf(query)) }));

    /// <summary>
    /// The schema as <see cref="WithPrivateContextColumns"/>, and the guard's read of a communication answered by
    /// <paramref name="answer"/> (a world's own query evaluation, so a private row it holds is refused). Set up after the
    /// double's own query answer, so it wins for that read only.
    /// </summary>
    public static Mock<IGenericEntityService> AnsweringTheGuardFrom(
        this Mock<IGenericEntityService> entities, Func<QueryExpression, EntityCollection> answer)
    {
        entities.WithPrivateContextColumns();
        entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => IsTheGuardRead(q)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) => answer(query));
        return entities;
    }

    /// <summary>An app-only entity service holding nothing else, for a writer composed with no app reader of its own.</summary>
    public static IGenericEntityService NoPrivateMessage() => new Mock<IGenericEntityService>().AnsweringNoPrivateMessage().Object;

    private static bool IsTheGuardRead(QueryExpression query) =>
        query.EntityName == Communication
        && query.ColumnSet.Columns.Any(PrivateContextColumns.All.Contains)
        && query.Criteria.Conditions.Any(c => c.AttributeName == Communication + "id" && c.Operator == ConditionOperator.Equal);

    private static Guid RowOf(QueryExpression query) =>
        (Guid)query.Criteria.Conditions.First(c => c.AttributeName == Communication + "id").Values[0];
}
