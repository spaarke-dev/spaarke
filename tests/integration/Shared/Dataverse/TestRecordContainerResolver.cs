using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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

    /// <summary>The container of the SECURE matter in <see cref="ForNonSecureRecordUnderSecureMatter"/>.</summary>
    public const string SecureMatterContainer = "b!secure-matter-own-container";

    private static readonly Guid RecordTypeRefId = Guid.Parse("0c0c0c0c-0000-4000-8000-000000000166");

    /// <summary>
    /// A NON-secure record filed under a SECURE matter (task 155 f3/f4): a project names the matter through the
    /// polymorphic regarding pair, a work assignment through its typed <c>sprk_regardingmatter</c>. The real
    /// resolver's CONTENT answer for it is the matter's own container (<c>ResolvedSecure</c>,
    /// <see cref="SecureMatterContainer"/>) — the case unified-access-control-r2 task 166 r1 found the closure and
    /// revoke paths sweeping, although the record itself owns no isolated container.
    /// </summary>
    public static RecordContainerResolver ForNonSecureRecordUnderSecureMatter(
        string logicalName, Guid recordId, Guid secureMatterId)
    {
        var rows = new Dictionary<(string, Guid), Entity>();

        var record = new Entity(logicalName, recordId)
        {
            ["sprk_issecure"] = false,
            ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
        };
        if (logicalName == "sprk_workassignment")
        {
            record["sprk_regardingmatter"] = new EntityReference("sprk_matter", secureMatterId);
        }
        else
        {
            record["sprk_regardingrecordid"] = secureMatterId.ToString("D");
            record["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", RecordTypeRefId);
            rows[("sprk_recordtype_ref", RecordTypeRefId)] =
                new Entity("sprk_recordtype_ref", RecordTypeRefId) { ["sprk_recordlogicalname"] = "sprk_matter" };
        }

        rows[(logicalName, recordId)] = record;
        rows[("sprk_matter", secureMatterId)] = new Entity("sprk_matter", secureMatterId)
        {
            ["sprk_issecure"] = true,
            ["sprk_containerid"] = SecureMatterContainer,
            ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
        };
        rows[("businessunit", BusinessUnitId)] =
            new Entity("businessunit", BusinessUnitId) { ["sprk_containerid"] = SharedBusinessUnitContainer };

        var registry = new Mock<ISecurableEntityRegistry>();
        registry.Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SecurableRoots);
        registry.Setup(r => r.ClassifyEntityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => TestEntityCatalog.Classify(name, SecurableRoots, KnownEntities));

        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) =>
                rows.TryGetValue((entity, id), out var row)
                    ? row
                    : throw new InvalidOperationException($"Unmodelled row {entity}({id})."));

        return new RecordContainerResolver(registry.Object, entityService.Object, NullLogger<RecordContainerResolver>.Instance);
    }

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

    /// <summary>
    /// An environment in which NO secure record claims any container and the business units stamp exactly the containers
    /// <paramref name="isBusinessUnitContainer"/> accepts. The document-pointer check (task 166 r1, owner round 21 item 1b)
    /// therefore honours a pointer into one of them and refuses any other — for tests of app-only download paths whose
    /// subject is something else, which must still run behind the real check.
    /// </summary>
    public static RecordContainerResolver ForBusinessUnitContainers(Func<string, bool> isBusinessUnitContainer)
        => ForDocumentPointerWorld(
            isBusinessUnitContainer,
            secureClaims: new Dictionary<string, (string Entity, Guid Id)>(StringComparer.Ordinal),
            rows: new Dictionary<(string, Guid), Entity>(),
            archiveContainerId: null,
            retrieveMultipleFault: null);

    /// <summary><see cref="ForBusinessUnitContainers(Func{string, bool})"/> for a fixed set of containers.</summary>
    public static RecordContainerResolver ForBusinessUnitContainers(params string[] containers)
        => ForBusinessUnitContainers(c => Array.IndexOf(containers, c) >= 0);

    /// <summary>
    /// The full document-pointer world (task 166 r1): business-unit containers, SECURE records claiming their own
    /// containers (<paramref name="secureClaims"/>: container → record), the rows the check reads (documents and the
    /// records they link), the configured archive container, and an optional fault every query throws.
    /// </summary>
    public static RecordContainerResolver ForDocumentPointerWorld(
        Func<string, bool> isBusinessUnitContainer,
        IReadOnlyDictionary<string, (string Entity, Guid Id)> secureClaims,
        IReadOnlyDictionary<(string, Guid), Entity> rows,
        string? archiveContainerId,
        Exception? retrieveMultipleFault)
    {
        var registry = new Mock<ISecurableEntityRegistry>();
        registry.Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SecurableRoots);
        registry.Setup(r => r.ClassifyEntityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => TestEntityCatalog.Classify(name, SecurableRoots, DocumentWorldEntities));

        var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entityService.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) =>
                rows.TryGetValue((entity, id), out var row)
                    ? row
                    : throw new InvalidOperationException($"Unmodelled row {entity}({id})."));
        entityService.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                if (retrieveMultipleFault is not null)
                {
                    throw retrieveMultipleFault;
                }

                var collection = new EntityCollection();
                var container = ContainerSearchedFor(query);
                if (container is null)
                {
                    return collection;
                }

                if (query.EntityName == "businessunit")
                {
                    if (isBusinessUnitContainer(container))
                    {
                        collection.Entities.Add(new Entity("businessunit", Guid.NewGuid()) { ["sprk_containerid"] = container });
                    }

                    return collection;
                }

                // Pass 1 (secure claimants) carries `sprk_issecure == true`; pass 2 (co-mingling) does not — and this
                // world has no non-secure claimant of a secure container.
                var asksForSecure = query.Criteria.Conditions.Any(c =>
                    c.AttributeName == "sprk_issecure" && c.Operator == ConditionOperator.Equal);
                if (asksForSecure && secureClaims.TryGetValue(container, out var owner) && owner.Entity == query.EntityName)
                {
                    collection.Entities.Add(new Entity(owner.Entity, owner.Id) { ["sprk_containerid"] = container });
                }

                return collection;
            });

        var options = Microsoft.Extensions.Options.Options.Create(
            new Sprk.Bff.Api.Configuration.CommunicationOptions { ArchiveContainerId = archiveContainerId });

        return new RecordContainerResolver(
            registry.Object, entityService.Object, NullLogger<RecordContainerResolver>.Instance, options);
    }

    private static readonly IReadOnlySet<string> DocumentWorldEntities =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "sprk_project", "sprk_matter", "sprk_workassignment", "businessunit", "sprk_document", "sprk_invoice",
            "sprk_event", "sprk_todo", "sprk_communication",
        };

    /// <summary>The container a reverse-resolution query searches for: its <c>LIKE '%…%'</c> value, un-escaped.</summary>
    private static string? ContainerSearchedFor(QueryExpression query)
    {
        var like = query.Criteria.Conditions.FirstOrDefault(c => c.Operator == ConditionOperator.Like);
        if (like?.Values.FirstOrDefault() is not string pattern || pattern.Length < 2)
        {
            return null;
        }

        return pattern[1..^1].Replace("[_]", "_", StringComparison.Ordinal)
            .Replace("[%]", "%", StringComparison.Ordinal)
            .Replace("[[]", "[", StringComparison.Ordinal);
    }

    /// <summary>A securable record whose <c>sprk_issecure</c> is ABSENT (an unset column, or a value masked from the
    /// reader) — "is it secure?" cannot be answered (task 166 r1: its own-container decision fails closed).</summary>
    public static RecordContainerResolver ForRecordWithNoSecureFlag(string logicalName, Guid recordId)
        => Build(logicalName, recordId, isSecure: null, ownContainerId: "b!stale-stamp-never-trusted");

    private static RecordContainerResolver Build(string logicalName, Guid recordId, bool? isSecure, string? ownContainerId)
    {
        var registry = new Mock<ISecurableEntityRegistry>();
        registry.Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SecurableRoots);
        registry.Setup(r => r.ClassifyEntityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => TestEntityCatalog.Classify(name, SecurableRoots, KnownEntities));

        var row = new Entity(logicalName, recordId)
        {
            ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
        };
        if (isSecure is { } secure)
        {
            row["sprk_issecure"] = secure;
        }
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
