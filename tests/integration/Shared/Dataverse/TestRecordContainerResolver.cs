using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;

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

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // The document-pointer world (task 166 r1; owner round 23 item 1 in r2): business units with their containers and
    // parents, secure records claiming their own containers, the document rows and their creators, the drive items
    // and THEIR creators, the configured archive container and the BFF identity.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The root business unit of every pointer world (parent null).</summary>
    public static readonly Guid PointerWorldRootBusinessUnit = Guid.Parse("b0b0b0b0-0000-4000-8000-000000000166");

    /// <summary>The PERSON (a human systemuser) who created the default document and uploaded its item.</summary>
    public static readonly Guid PointerWorldCreator = Guid.Parse("c0c0c0c0-0000-4000-8000-000000000166");

    /// <summary><see cref="PointerWorldCreator"/>'s Entra object id — what Graph reports as <c>createdBy.user.id</c>.</summary>
    public static readonly Guid PointerWorldCreatorObjectId = Guid.Parse("c1c1c1c1-0000-4000-8000-000000000166");

    /// <summary>The BFF's Entra application id ("the BFF identity", configured as <c>API_APP_ID</c>).</summary>
    public static readonly Guid PointerWorldBffApplicationId = Guid.Parse("bff00000-0000-4000-8000-000000000166");

    /// <summary>The BFF's Dataverse application user (its <c>applicationid</c> is <see cref="PointerWorldBffApplicationId"/>).</summary>
    public static readonly Guid PointerWorldBffUser = Guid.Parse("bff11111-0000-4000-8000-000000000166");

    /// <summary>
    /// An environment in which NO secure record claims any container and the ROOT business unit stamps exactly the
    /// containers <paramref name="isBusinessUnitContainer"/> accepts; every document is created and owned in that root
    /// by <see cref="PointerWorldCreator"/>, who also uploaded every item. The document-pointer check therefore honours
    /// a pointer into one of those containers and refuses any other — for tests of app-only download paths whose
    /// subject is something else, which must still run behind the real check.
    /// </summary>
    public static RecordContainerResolver ForBusinessUnitContainers(Func<string, bool> isBusinessUnitContainer)
        => new DocumentPointerWorld
        {
            RootClaims = isBusinessUnitContainer,
        }.Build();

    /// <summary><see cref="ForBusinessUnitContainers(Func{string, bool})"/> for a fixed set of containers.</summary>
    public static RecordContainerResolver ForBusinessUnitContainers(params string[] containers)
        => ForBusinessUnitContainers(c => Array.IndexOf(containers, c) >= 0);

    /// <summary>
    /// The full document-pointer world (task 166 r1 / r2). Defaults: one root business unit claiming nothing; any
    /// document not in <see cref="Rows"/> is <see cref="Document"/> (created by <see cref="PointerWorldCreator"/>, owned
    /// by the root); any item not in <see cref="Items"/> was uploaded by <see cref="PointerWorldCreator"/>.
    /// </summary>
    public sealed class DocumentPointerWorld
    {
        /// <summary>Business units: id → (parent, stamped container). The root is present by default.</summary>
        public Dictionary<Guid, (Guid? Parent, string? Container)> BusinessUnits { get; } = new()
        {
            [PointerWorldRootBusinessUnit] = (null, null),
        };

        /// <summary>Extra containers the ROOT business unit claims (the predicate form of <see cref="BusinessUnits"/>).</summary>
        public Func<string, bool>? RootClaims { get; init; }

        /// <summary>SECURE records claiming their own containers: container → record.</summary>
        public Dictionary<string, (string Entity, Guid Id)> SecureClaims { get; } = new(StringComparer.Ordinal);

        /// <summary>Dataverse rows the check reads (documents, the records they link, systemusers).</summary>
        public Dictionary<(string, Guid), Entity> Rows { get; } = new();

        /// <summary>Drive items and who created them: (drive, item) → creator (null = not in that drive).</summary>
        public Dictionary<(string Drive, string Item), SpeItemCreator?> Items { get; } = new();

        /// <summary>The configured <c>Communication:ArchiveContainerId</c>.</summary>
        public string? ArchiveContainerId { get; init; }

        /// <summary>A fault every RetrieveMultiple throws (the container / hierarchy queries).</summary>
        public Exception? RetrieveMultipleFault { get; init; }

        /// <summary>The environment does not have the <c>sprk_createdbyperson</c> column on documents (owner round 17).</summary>
        public bool CreatedByPersonColumnMissing { get; init; }

        /// <summary>The Graph creator read throws (Graph unavailable).</summary>
        public Exception? ItemReadFault { get; init; }

        /// <summary>The resolver is built with NO SharePoint Embedded item reader (a host that registers none).</summary>
        public bool NoItemReader { get; init; }

        /// <summary>The STRICT derived-container rule is in force (<c>DocumentPointer:StrictDerivedContainer</c>, task 166 f1).</summary>
        public bool Strict { get; init; }

        /// <summary><c>EmailProcessing:DefaultContainerId</c> — an unfiled document's container of last resort (task 166 f1).</summary>
        public string? UnfiledDefaultContainer { get; init; }

        /// <summary>Every app-only UPDATE the built resolver's entity service received (the relocator's re-point).</summary>
        public List<(string Entity, Guid Id, Dictionary<string, object> Fields)> Updates { get; } = new();

        /// <summary>
        /// The environment does not have <c>sprk_document.sprk_relocationpending</c> yet (task 166 f1-v1: the relocation
        /// ledger's schema gate) — any read naming it throws, as Dataverse does.
        /// </summary>
        public bool RelocationLedgerColumnMissing { get; init; }

        /// <summary>
        /// The environment does not have <c>sprk_document.sprk_relocatedversions</c> yet (task 166 f1-v2: the relocation's
        /// version record, the same schema gate) — any read naming it throws, as Dataverse does.
        /// </summary>
        public bool RelocatedVersionsColumnMissing { get; init; }

        /// <summary>
        /// A query of this entity faults (Dataverse unavailable) — e.g. <c>sprk_communicationattachment</c> to make the
        /// relocator's re-key or reference read fail.
        /// </summary>
        public string? FaultQueriesOf { get; set; }

        /// <summary>Every RetrieveMultiple query the entity service answered, in order.</summary>
        public List<QueryExpression> Queries { get; } = new();

        /// <summary>How many business-unit HIERARCHY reads the built resolver made (task 166 f1: one per scope).</summary>
        public int HierarchyReads { get; private set; }

        /// <summary>How many "which units stamp this container?" reads the built resolver made.</summary>
        public int ClaimantReads { get; private set; }

        /// <summary>The first N hierarchy reads FAULT (a failed read must not be remembered for the scope).</summary>
        public int FailFirstHierarchyReads { get; set; }

        /// <summary>Every app-only UPDATE faults (the relocator's re-point fails).</summary>
        public Exception? UpdateFault { get; set; }

        /// <summary>The entity service the last <see cref="Build"/> created — the relocator shares it (task 166 f1).</summary>
        public IGenericEntityService? EntityService { get; private set; }

        /// <summary>The SPE item reader the last <see cref="Build"/> created, over <see cref="Items"/>.</summary>
        public SpeItemCreator? ItemFacts(string drive, string item)
            => Items.TryGetValue((drive, item), out var creator)
                ? creator
                : new SpeItemCreator("file.pdf", PointerWorldCreatorObjectId.ToString("D"), PointerWorldBffApplicationId.ToString("D"), Size: 1234, QuickXorHash: "hash-1234");

        /// <summary>A document row created by <paramref name="createdBy"/> and owned in <paramref name="owningBusinessUnit"/>.</summary>
        public static Entity Document(Guid id, Guid? createdBy = null, Guid? owningBusinessUnit = null)
            => new("sprk_document", id)
            {
                ["createdby"] = new EntityReference("systemuser", createdBy ?? PointerWorldCreator),
                ["owningbusinessunit"] = new EntityReference("businessunit", owningBusinessUnit ?? PointerWorldRootBusinessUnit),
            };

        public RecordContainerResolver Build()
        {
            var registry = new Mock<ISecurableEntityRegistry>();
            registry.Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(SecurableRoots);
            registry.Setup(r => r.ClassifyEntityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string name, CancellationToken _) => TestEntityCatalog.Classify(name, SecurableRoots, DocumentWorldEntities));

            var people = new Dictionary<(string, Guid), Entity>
            {
                [("systemuser", PointerWorldCreator)] = new Entity("systemuser", PointerWorldCreator)
                {
                    ["azureactivedirectoryobjectid"] = PointerWorldCreatorObjectId,
                },
                [("systemuser", PointerWorldBffUser)] = new Entity("systemuser", PointerWorldBffUser)
                {
                    ["applicationid"] = PointerWorldBffApplicationId,
                },
            };

            var entityService = new Mock<IGenericEntityService>(MockBehavior.Strict);
            entityService.Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string entity, Guid id, string[] columns, CancellationToken _) =>
                {
                    if (entity == "sprk_document" && CreatedByPersonColumnMissing && columns.Contains("sprk_createdbyperson"))
                    {
                        throw new InvalidOperationException(
                            "'sprk_Document' entity doesn't contain attribute with Name = 'sprk_createdbyperson'.");
                    }

                    if (entity == "sprk_document" && RelocationLedgerColumnMissing && columns.Contains("sprk_relocationpending"))
                    {
                        throw new InvalidOperationException(
                            "'sprk_Document' entity doesn't contain attribute with Name = 'sprk_relocationpending'.");
                    }

                    if (entity == "sprk_document" && RelocatedVersionsColumnMissing && columns.Contains("sprk_relocatedversions"))
                    {
                        throw new InvalidOperationException(
                            "'sprk_Document' entity doesn't contain attribute with Name = 'sprk_relocatedversions'.");
                    }

                    if (Rows.TryGetValue((entity, id), out var row) || people.TryGetValue((entity, id), out row))
                    {
                        return row;
                    }

                    if (entity == "businessunit" && BusinessUnits.TryGetValue(id, out var unit))
                    {
                        // The forward resolution's non-secure default: a business unit's stamped container (task 166 f1).
                        var businessUnit = new Entity("businessunit", id);
                        if (unit.Container is not null)
                        {
                            businessUnit["sprk_containerid"] = unit.Container;
                        }

                        return businessUnit;
                    }

                    return entity == "sprk_document"
                        ? Document(id)
                        : throw new InvalidOperationException($"Unmodelled row {entity}({id}).");
                });
            entityService.Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
                {
                    if (UpdateFault is not null)
                    {
                        return Task.FromException(UpdateFault);
                    }

                    Updates.Add((entity, id, fields));
                    if (Rows.TryGetValue((entity, id), out var updated))
                    {
                        foreach (var (column, value) in fields)
                        {
                            if (value is DBNull)
                            {
                                updated.Attributes.Remove(column); // the generic seam's explicit CLEAR
                            }
                            else if (value is not null)
                            {
                                updated[column] = value;
                            }
                        }
                    }

                    return Task.CompletedTask;
                });
            entityService.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression query, CancellationToken _) =>
                {
                    Queries.Add(query);
                    if (RetrieveMultipleFault is not null)
                    {
                        throw RetrieveMultipleFault;
                    }

                    if (FaultQueriesOf is not null && query.EntityName == FaultQueriesOf)
                    {
                        throw new TimeoutException($"Dataverse unavailable (scripted fault on {FaultQueriesOf})");
                    }

                    var collection = new EntityCollection();
                    if (query.EntityName is "sprk_document" or "sprk_communicationattachment")
                    {
                        // The relocator's and the migration's row queries (task 166 f1 / f1-v1): answered from Rows by the
                        // query's own AND-conditions — the keyset batch, the "who else names this file?" read, the child
                        // and attachment re-key reads.
                        var idColumn = query.EntityName + "id";
                        var matches = Rows
                            .Where(r => r.Key.Item1 == query.EntityName
                                        && query.Criteria.Conditions.All(c => ConditionHolds(r.Value, r.Key.Item2, idColumn, c)));
                        if (query.Orders.Any(o => o.AttributeName == idColumn))
                        {
                            matches = matches.OrderBy(r => r.Key.Item2);
                        }

                        foreach (var match in matches.Take(query.TopCount ?? int.MaxValue))
                        {
                            collection.Entities.Add(match.Value);
                        }

                        return collection;
                    }

                    var container = ContainerSearchedFor(query);

                    if (query.EntityName == "businessunit")
                    {
                        if (container is null)
                        {
                            HierarchyReads++;
                            if (HierarchyReads <= FailFirstHierarchyReads)
                            {
                                throw new TimeoutException("Dataverse unavailable (scripted hierarchy fault)");
                            }

                            // The hierarchy read: every unit with its parent.
                            foreach (var (unitId, (parent, _)) in BusinessUnits)
                            {
                                var unit = new Entity("businessunit", unitId);
                                if (parent is { } p)
                                {
                                    unit["parentbusinessunitid"] = new EntityReference("businessunit", p);
                                }

                                collection.Entities.Add(unit);
                            }

                            return collection;
                        }

                        ClaimantReads++;
                        foreach (var (unitId, (_, stamped)) in BusinessUnits)
                        {
                            var claims = string.Equals(stamped, container, StringComparison.Ordinal)
                                         || (unitId == PointerWorldRootBusinessUnit && RootClaims?.Invoke(container) == true);
                            if (claims)
                            {
                                collection.Entities.Add(new Entity("businessunit", unitId) { ["sprk_containerid"] = container });
                            }
                        }

                        return collection;
                    }

                    if (container is null)
                    {
                        return collection;
                    }

                    // Pass 1 (secure claimants) carries `sprk_issecure == true`; pass 2 (co-mingling) does not — and this
                    // world has no non-secure claimant of a secure container.
                    var asksForSecure = query.Criteria.Conditions.Any(c =>
                        c.AttributeName == "sprk_issecure" && c.Operator == ConditionOperator.Equal);
                    if (asksForSecure && SecureClaims.TryGetValue(container, out var owner) && owner.Entity == query.EntityName)
                    {
                        collection.Entities.Add(new Entity(owner.Entity, owner.Id) { ["sprk_containerid"] = container });
                    }

                    return collection;
                });

            var speFiles = new Mock<ISpeFileOperations>(MockBehavior.Loose);
            speFiles.Setup(f => f.GetItemCreatorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string drive, string item, CancellationToken _) =>
                {
                    if (ItemReadFault is not null)
                    {
                        throw ItemReadFault;
                    }

                    return ItemFacts(drive, item);
                });

            var options = Microsoft.Extensions.Options.Options.Create(
                new Sprk.Bff.Api.Configuration.CommunicationOptions { ArchiveContainerId = ArchiveContainerId });
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["API_APP_ID"] = PointerWorldBffApplicationId.ToString("D"),
                    [RecordContainerResolver.StrictDerivedContainerKey] = Strict ? "true" : null,
                    [RecordContainerResolver.UnfiledDefaultContainerKey] = UnfiledDefaultContainer,
                })
                .Build();

            EntityService = entityService.Object;
            return new RecordContainerResolver(
                registry.Object, entityService.Object, NullLogger<RecordContainerResolver>.Instance,
                restampQueue: null, communicationOptions: options,
                speFiles: NoItemReader ? null : speFiles.Object, configuration: configuration);
        }
    }

    /// <summary>One AND-condition of a row query, against a modelled row (lookups compare by id, the key column by row id).</summary>
    private static bool ConditionHolds(Entity row, Guid rowId, string idColumn, ConditionExpression condition)
    {
        object? actual = condition.AttributeName == idColumn
            ? rowId
            : row.Contains(condition.AttributeName) ? row[condition.AttributeName] : null;
        if (actual is EntityReference reference)
        {
            actual = reference.Id;
        }

        var expected = condition.Values.FirstOrDefault();
        return condition.Operator switch
        {
            ConditionOperator.Equal => Same(actual, expected),
            ConditionOperator.NotEqual => !Same(actual, expected),
            ConditionOperator.NotNull => actual is not null && !(actual is string s && string.IsNullOrWhiteSpace(s)),
            ConditionOperator.Null => actual is null || (actual is string t && string.IsNullOrWhiteSpace(t)),
            ConditionOperator.GreaterThan => actual is Guid g && expected is Guid e && g.CompareTo(e) > 0,
            _ => throw new NotSupportedException($"The test world does not model {condition.Operator} on {condition.AttributeName}."),
        };

        static bool Same(object? a, object? b) => a switch
        {
            null => b is null,
            Guid g => b is Guid h && g == h,
            string s => b is string t && string.Equals(s, t, StringComparison.Ordinal),
            _ => Equals(a, b),
        };
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
