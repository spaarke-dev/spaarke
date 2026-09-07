using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// Builders for the <see cref="CoreAncestorResolver"/> that every converged FR-26 writer now takes
/// (unified-access-control-r2 task 052).
/// </summary>
/// <remarks>
/// <para>
/// Most tests of those writers are about something else entirely — channel routing, footer text, rung
/// telemetry — and only need a resolver that does not change what they assert. <see cref="Inert"/> is
/// that: it reports the full core-ancestor column set on every entity, and returns target rows whose
/// ancestor lookups are all null. A CORE target still stamps only itself (which the writer already wrote),
/// and a CHILD target resolves to <c>NoAncestor</c>, so no field is added to any payload.
/// </para>
/// <para>
/// <b>Inert is not the same as absent.</b> It still exercises the real derivation code path, so a writer
/// that stopped calling the resolver would not be caught by these tests — that is what the dedicated
/// stamping tests are for. Use <see cref="WithAncestors"/> when the ancestor stamp IS the subject, and
/// <see cref="Failing"/> to assert the fail-closed branch.
/// </para>
/// </remarks>
internal static class CoreAncestorResolverFixtures
{
    /// <summary>The four core-ancestor lookups, as an entity that carries all of them would report.</summary>
    internal static readonly string[] AllCoreAncestorColumns =
        CoreAncestorResolver.CoreAncestorLookups.Select(c => c.LookupAttribute).ToArray();

    /// <summary>A resolver that derives nothing — no stamp is added to any payload.</summary>
    internal static CoreAncestorResolver Inert() => WithAncestors();

    /// <summary>
    /// A resolver whose child targets carry the supplied ancestors.
    /// </summary>
    /// <param name="ancestors">Lookup attribute → ancestor record id, e.g. <c>("sprk_regardingmatter", matterId)</c>.</param>
    internal static CoreAncestorResolver WithAncestors(params (string LookupAttribute, Guid RecordId)[] ancestors)
    {
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string logicalName, Guid id, string[] _, CancellationToken __) =>
            {
                var row = new Entity(logicalName, id);
                foreach (var (lookupAttribute, recordId) in ancestors)
                {
                    var entityType = CoreAncestorResolver.CoreAncestorLookups
                        .First(c => string.Equals(c.LookupAttribute, lookupAttribute, StringComparison.OrdinalIgnoreCase))
                        .EntityType;
                    row[lookupAttribute] = new EntityReference(entityType, recordId);
                }

                return row;
            });

        return new CoreAncestorResolver(
            entityService.Object,
            ProbeReturning(AllCoreAncestorColumns),
            NullLogger<CoreAncestorResolver>.Instance);
    }

    /// <summary>
    /// A resolver whose derivation always fails — for asserting that a writer refuses to write rather than
    /// creating an unstamped child (NFR-01).
    /// </summary>
    internal static CoreAncestorResolver Failing() =>
        new(
            new Mock<IGenericEntityService>(MockBehavior.Loose).Object,
            (_, _) => throw new InvalidOperationException("metadata unavailable"),
            NullLogger<CoreAncestorResolver>.Instance);

    /// <summary>A column probe reporting exactly <paramref name="columns"/> for every entity.</summary>
    internal static CoreAncestorResolver.EntityColumnProbe ProbeReturning(params string[] columns) =>
        (_, _) => Task.FromResult<IReadOnlySet<string>>(
            new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase));
}
