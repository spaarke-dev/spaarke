using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// The REAL <see cref="RecordContainerResolver"/> over a Dataverse world of exactly one root record — for tests of
/// routes that DERIVE a container from an authorized record instead of accepting one from the request
/// (unified-access-control-r2 task 166: close-project and revoke).
/// </summary>
/// <remarks>
/// <para><b>Why the resolver is real.</b> It is <c>sealed</c> with non-virtual methods (ADR-010), and — more to the
/// point — the decision under test (secure record → its own container; non-secure → the shared business-unit
/// container; secure with no container → refuse) IS the resolver's logic. Substituting it with a constant would test
/// the endpoint against an answer the real resolver might never give. The substitution is at its two seams instead:
/// <see cref="ISecurableEntityRegistry"/> (classified by <see cref="TestEntityCatalog"/>, the one test-side model of
/// that answer) and <see cref="IGenericEntityService"/> (the record row and its business unit's row). Moq, not
/// NSubstitute, because this file is compiled into all three test assemblies and only Moq is common to them.</para>
/// </remarks>
internal static class TestRecordContainerResolver
{
    /// <summary>The shared business-unit container a NON-secure record's derivation resolves to.</summary>
    public const string SharedBusinessUnitContainer = "b!shared-business-unit-container";

    private static readonly Guid BusinessUnitId = Guid.Parse("0b0b0b0b-0000-4000-8000-000000000166");

    private static readonly IReadOnlySet<string> SecurableRoots =
        new HashSet<string>(StringComparer.Ordinal) { "sprk_project", "sprk_matter", "sprk_workassignment" };

    private static readonly IReadOnlySet<string> KnownEntities =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "sprk_project", "sprk_matter", "sprk_workassignment", "businessunit", "contact", "account",
        };

    /// <summary>A SECURE record (<c>sprk_issecure</c> = true) owning <paramref name="ownContainerId"/> —
    /// <see cref="ContainerDecisionOutcome.ResolvedSecure"/>. A null container makes the resolver REFUSE
    /// (<c>secure_record_container_missing</c>), which is the FailClosed case.</summary>
    public static RecordContainerResolver ForSecureRecord(string logicalName, Guid recordId, string? ownContainerId)
        => Build(logicalName, recordId, isSecure: true, ownContainerId);

    /// <summary>A NON-secure record — <see cref="ContainerDecisionOutcome.ResolvedFallback"/> to
    /// <see cref="SharedBusinessUnitContainer"/>, the container a closure or revoke must never sweep.</summary>
    public static RecordContainerResolver ForNonSecureRecord(string logicalName, Guid recordId)
        => Build(logicalName, recordId, isSecure: false, ownContainerId: null);

    /// <summary>A resolver whose first question throws <paramref name="fault"/> (metadata unavailable, a typed
    /// refusal, …) — the "decision could not be made" case.</summary>
    public static RecordContainerResolver Throwing(Exception fault)
    {
        var registry = new Mock<ISecurableEntityRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.ClassifyEntityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(fault);
        registry.Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(fault);

        return new RecordContainerResolver(
            registry.Object, new Mock<IGenericEntityService>(MockBehavior.Strict).Object,
            NullLogger<RecordContainerResolver>.Instance);
    }

    private static RecordContainerResolver Build(string logicalName, Guid recordId, bool isSecure, string? ownContainerId)
    {
        var registry = new Mock<ISecurableEntityRegistry>();
        registry.Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SecurableRoots);
        registry.Setup(r => r.ClassifyEntityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => TestEntityCatalog.Classify(name, SecurableRoots, KnownEntities));

        var row = new Entity(logicalName, recordId)
        {
            ["sprk_issecure"] = isSecure,
            ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
        };
        if (ownContainerId is not null)
        {
            row["sprk_containerid"] = ownContainerId;
        }

        var entityService = new Mock<IGenericEntityService>();
        entityService.Setup(s => s.RetrieveAsync(logicalName, recordId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        entityService.Setup(s => s.RetrieveAsync("businessunit", BusinessUnitId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("businessunit", BusinessUnitId) { ["sprk_containerid"] = SharedBusinessUnitContainer });

        return new RecordContainerResolver(registry.Object, entityService.Object, NullLogger<RecordContainerResolver>.Instance);
    }
}
