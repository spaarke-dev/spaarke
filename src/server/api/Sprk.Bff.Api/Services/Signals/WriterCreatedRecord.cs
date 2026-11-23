// ebilling-r1 task 165b-3 part B (EB-D-208; ontology-r1 O2): the one definition of "a row the BFF writer identity created".
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceLineOrigin.AddWriterCreatedConditions (moved here, unchanged in effect) and the ontology's own createdby-only lookups
//       (DecisionRecordWriter, DecisionCommitService, SignalReadService, SignalReevaluationJob; DI-144 moves them onto this type).
//   (2) Extension — DecisionRecordWriter owns CreatedByColumn but is an instance writer with a connection; a static rule next to it can be called
//       by the static Finance readers (InvoiceLineOrigin, the AP export and the spend readers of 165b-4) without that writer.
//   (3) Cost of doing nothing — each reader would spell its own copy of the rule, and a copy that drops createdonbehalfby counts a row created
//       on the writer's behalf by another identity as the server's.
//
// Placement (bff-extensions.md): BFF, Services/Signals, next to DecisionRecordWriter.CreatedByColumn. Pure (no I/O, no DI registration).

using System.Globalization;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk.Query;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The one rule for "a row the BFF writer identity created": <c>createdby</c> is in the writer allow-list AND <c>createdonbehalfby</c> is empty.
/// Never <c>ownerid</c>, never a column a user can write.
/// </summary>
/// <remarks>
/// <para><b>The allow-list.</b> One id today: the BFF writer identity, the <c>systemuserid</c> that
/// <see cref="OntologyWriterDataverseClient.GetWriterUserIdAsync"/> reads (WhoAmI on the writer's own connection, app-only, cached). Callers
/// pass that id in; a fault reading it propagates and the caller fails its decision closed. <see cref="Guid.Empty"/> is refused here (an
/// <see cref="ArgumentException"/>), so an unknown writer never becomes a filter that matches nothing and reads as "no record".</para>
/// <para><b>Forms.</b> A <see cref="QueryExpression"/> form (<see cref="AddConditions(FilterExpression, Guid)"/> on a query's criteria, or
/// <see cref="AddConditions(LinkEntity, Guid)"/> on a link's) and a FetchXML form (<see cref="FetchXmlFilter"/>, a filter element for an entity
/// or a link-entity). An ArchTest (<c>WriterCreatedRecordSingleSourceGuardTests</c>) refuses a <c>createdby</c> or <c>createdonbehalfby</c>
/// condition built outside this type in <c>Services/Finance/**</c> and <c>Services/Signals/**</c>.</para>
/// </remarks>
public static class WriterCreatedRecord
{
    /// <summary>The column Dataverse stamps with the identity a row was created on behalf of (empty for a row the writer created itself).</summary>
    public const string CreatedOnBehalfByColumn = "createdonbehalfby";

    /// <summary>Adds the rule to <paramref name="criteria"/>: <c>createdby</c> = <paramref name="writerUserId"/> and <c>createdonbehalfby</c> null.</summary>
    public static void AddConditions(FilterExpression criteria, Guid writerUserId)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var writer = Allowed(writerUserId);
        criteria.AddCondition(DecisionRecordWriter.CreatedByColumn, ConditionOperator.Equal, writer);
        criteria.AddCondition(CreatedOnBehalfByColumn, ConditionOperator.Null);
    }

    /// <summary>Adds the rule to <paramref name="link"/>'s own criteria (the linked rows must be the writer's).</summary>
    public static void AddConditions(LinkEntity link, Guid writerUserId)
    {
        ArgumentNullException.ThrowIfNull(link);
        AddConditions(link.LinkCriteria, writerUserId);
    }

    /// <summary>The rule as a FetchXML <c>&lt;filter type="and"&gt;</c>, for an <c>entity</c> or a <c>link-entity</c> element.</summary>
    public static XElement FetchXmlFilter(Guid writerUserId)
    {
        var writer = Allowed(writerUserId);
        return new XElement("filter", new XAttribute("type", "and"),
            new XElement("condition", new XAttribute("attribute", DecisionRecordWriter.CreatedByColumn), new XAttribute("operator", "eq"),
                new XAttribute("value", writer.ToString("D", CultureInfo.InvariantCulture))),
            new XElement("condition", new XAttribute("attribute", CreatedOnBehalfByColumn), new XAttribute("operator", "null")));
    }

    private static Guid Allowed(Guid writerUserId) => writerUserId != Guid.Empty
        ? writerUserId
        : throw new ArgumentException("The writer identity is unknown, so no row can be treated as the writer's.", nameof(writerUserId));
}
