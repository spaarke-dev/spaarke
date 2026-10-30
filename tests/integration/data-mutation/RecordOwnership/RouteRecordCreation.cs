using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Tests.TestInfrastructure;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// Field-mapping-server-write-path-r1 task 017: the child-records route's create takes the ONE server create
/// (<see cref="RecordCreationService"/>) instead of the collaborators it used to call itself. The route suites build it
/// here over THEIR OWN collaborators (the resolver, the app-only writer, the gate, the restamper, the share synchronizer,
/// the scopes the "Assigned *" step resolves), so each suite still asserts what it asserted before; only the mechanism moved.
/// </summary>
internal static class RouteRecordCreation
{
    /// <summary>The business unit every team in <see cref="Entities"/> reports, and whose search-index defaults are empty.</summary>
    internal static readonly Guid TeamBusinessUnit = OwnershipDirectory.ChildBu;

    /// <summary>The service over the route's collaborators.</summary>
    internal static RecordCreationService Over(
        IRecordOwnershipResolver ownership,
        IFieldMappingDataverseService appOnly,
        CoreAncestorRestamper restamper,
        SecureChildShareSynchronizer shares,
        SecureRootFilingGate gate,
        IServiceScopeFactory? scopes = null,
        IMembershipEventPublisher? membership = null,
        IGenericEntityService? entities = null,
        ILogger<RecordCreationService>? logger = null) =>
        new(ownership, appOnly, entities ?? Entities().Object, gate, restamper, shares,
            scopes ?? new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            membership ?? new Mock<IMembershipEventPublisher>().Object,
            logger ?? NullLogger<RecordCreationService>.Instance);

    /// <summary>
    /// The service's own app-only reads, answered so they add nothing a route suite did not see before: a team's business
    /// unit (step 8 on a search-indexed table), a business unit with no search-index defaults (so nothing is filled), and any
    /// other row read back with each asked column holding a number (step 12's autonumber read-back on a budget). Queries
    /// return nothing (the project pair's catalog read is never asked on these tables).
    /// </summary>
    internal static Mock<IGenericEntityService> Entities()
    {
        var entities = new Mock<IGenericEntityService>();
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] columns, CancellationToken _) =>
            {
                var row = new Entity(table, id);
                if (table == "team")
                {
                    row["businessunitid"] = new EntityReference("businessunit", TeamBusinessUnit);
                }
                else if (table != "businessunit")
                {
                    foreach (var column in columns)
                        row[column] = $"N-{id:N}";
                }

                return row;
            });
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        return entities;
    }
}
