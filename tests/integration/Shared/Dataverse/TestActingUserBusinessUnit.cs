using System;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;

/// <summary>
/// The ONE wire-level arrangement for issue #858's server-derived create-on-save container: the two
/// Dataverse reads <c>RecordContainerResolver.ResolveForActingUserAsync</c> makes for the caller every
/// integration fixture authenticates as (<see cref="TestSessionOwner.Oid"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Issue #858 deleted <c>SaveComposeDocumentRequest.ContainerId</c>: a
/// create-on-save no longer states its storage container in the request — the SERVER derives it, for a
/// matter-less draft via <c>systemuser</c> (filtered on <c>azureactivedirectoryobjectid</c> = the
/// caller's <c>oid</c>, a TRANSLATION not a comparison) → that user's <c>businessunit</c> →
/// <c>sprk_containerid</c>. Every <c>WebApplicationFactory</c> fixture that drives create-on-save
/// through the wire must arrange those two reads or resolution throws
/// <c>acting_user_not_resolvable</c> (403) — which is what turned 12 seam/contract/regression tests
/// red when #858 landed. This is the wire-layer mirror of
/// <c>ComposeServiceCollaborators.SetupActingUserContainer</c> (the unit-layer helper), shared here so
/// the arrangement exists ONCE, not per fixture.
/// </para>
/// <para>
/// <b>The matchers are deliberately STRICT — they arrange the REAL derivation, not "any query".</b>
/// The <c>systemuser</c> setup only matches a query that actually filters
/// <c>azureactivedirectoryobjectid == TestSessionOwner.Oid</c>, and the <c>businessunit</c> setup only
/// matches a retrieve of THE business unit the arranged user belongs to, selecting
/// <c>sprk_containerid</c>. If production ever stops translating the caller's oid through the
/// systemuser column (the <c>CallerIdentityGuardTests</c> Rule 2 id-space defect class), or reads a
/// different business unit than the user's, these setups stop matching, resolution fails closed, and
/// the wire tests go red — which is exactly the property a test that "bypasses the derivation" could
/// not give you.
/// </para>
/// <para>
/// <b>What this must NEVER become</b>: a hook that lets a request name a container, or a DI swap that
/// replaces <c>RecordContainerResolver</c> with a constant. The resolver stays REAL (it is
/// <c>sealed</c> precisely so it cannot be mocked); only its Dataverse boundary is arranged, per
/// ADR-038 §"mock at module boundaries".
/// </para>
/// <para>
/// Global namespace, same convention (and reason) as <see cref="TestSessionOwner"/>: referenced from
/// seam, contract and regression suites, and a value whose only job is to be the same everywhere
/// should not need a using directive to stay that way.
/// </para>
/// </remarks>
internal static class TestActingUserBusinessUnit
{
    /// <summary>
    /// The container id stamped on the arranged business unit — the value a matter-less
    /// create-on-save resolves SERVER-SIDE for the test caller. Assert against this to prove the
    /// server derivation ran; a body-supplied container id must never influence it.
    /// </summary>
    public const string ContainerId = "b!acting-user-bu-container-0001";

    /// <summary>The arranged systemuser's business unit. Stable so fixtures can arrange sibling
    /// business units (e.g. a matter's OWNING business unit) without colliding with this one.</summary>
    public static readonly Guid BusinessUnitId = Guid.Parse("0b5e0000-0000-0000-0000-00000000bbbb");

    /// <summary>The arranged systemuser row id (arbitrary but stable).</summary>
    public static readonly Guid SystemUserId = Guid.Parse("5057e300-0000-0000-0000-0000000000aa");

    private static readonly Guid CallerOid = Guid.Parse(TestSessionOwner.Oid);

    /// <summary>
    /// Arrange the full acting-user derivation chain on <paramref name="dataverse"/>:
    /// caller oid → one <c>systemuser</c> row → <see cref="BusinessUnitId"/> →
    /// <paramref name="containerId"/> (default <see cref="ContainerId"/>).
    /// </summary>
    /// <typeparam name="TService">
    /// The mocked Dataverse interface. Generic because fixtures double the boundary at two levels:
    /// most register a dedicated <c>Mock&lt;IGenericEntityService&gt;</c>; the DEF-14 fixture registers
    /// a <c>Mock&lt;IDataverseService&gt;</c> that <c>IGenericEntityService</c> forwards to
    /// (GraphModule.cs). Both carry the same inherited members, so one helper serves both.
    /// </typeparam>
    /// <remarks>
    /// Call this AFTER any <c>Mock.Reset()</c> (a reset erases it) and note Moq resolves overlapping
    /// setups last-configured-wins: a later <c>It.IsAny</c> catch-all on
    /// <c>RetrieveMultipleAsync</c>/<c>RetrieveAsync</c> would shadow these. None of the Compose wire
    /// suites use such catch-alls today (they arrange <c>RetrieveByAlternateKeyAsync</c> and
    /// entity-specific retrieves), so fixture-level placement in <c>ResetBoundaries()</c> is safe.
    /// </remarks>
    public static void Arrange<TService>(Mock<TService> dataverse, string containerId = ContainerId)
        where TService : class, IGenericEntityService
    {
        ArrangeUserAndBusinessUnit(dataverse, buildBusinessUnitRow: () =>
            new Entity("businessunit", BusinessUnitId)
            {
                ["sprk_containerid"] = containerId
            });
    }

    /// <summary>
    /// Arrange the derivation chain up to a business unit with NO <c>sprk_containerid</c> stamped —
    /// the legitimate, common configuration state (3 of 6 live business units, verified 2026-08-27).
    /// Resolution then yields <c>Unresolved</c>/null and the save fails its container step honestly
    /// (HTTP 200 carrying <c>outcome: storage-failed</c>, nothing written) — it does NOT throw.
    /// </summary>
    public static void ArrangeWithNoContainer<TService>(Mock<TService> dataverse)
        where TService : class, IGenericEntityService
    {
        ArrangeUserAndBusinessUnit(dataverse, buildBusinessUnitRow: () =>
            new Entity("businessunit", BusinessUnitId));
    }

    private static void ArrangeUserAndBusinessUnit<TService>(
        Mock<TService> dataverse, Func<Entity> buildBusinessUnitRow)
        where TService : class, IGenericEntityService
    {
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => IsCallerSystemUserQuery(q)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var user = new Entity("systemuser", SystemUserId)
                {
                    ["businessunitid"] = new EntityReference("businessunit", BusinessUnitId)
                };
                var collection = new EntityCollection();
                collection.Entities.Add(user);
                return collection;
            });

        dataverse
            .Setup(d => d.RetrieveAsync(
                "businessunit",
                BusinessUnitId,
                It.Is<string[]>(cols => cols.Contains("sprk_containerid")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(buildBusinessUnitRow);
    }

    /// <summary>
    /// True only for the query <c>ResolveForActingUserAsync</c> actually issues: a
    /// <c>systemuser</c> query whose criteria TRANSLATE the caller's Entra oid through
    /// <c>azureactivedirectoryobjectid</c>. Anything looser would also green-light a production
    /// regression that looked users up by the wrong column.
    /// </summary>
    private static bool IsCallerSystemUserQuery(QueryExpression query)
        => query.EntityName == "systemuser"
           && query.Criteria != null
           && query.Criteria.Conditions.Any(c =>
               c.AttributeName == "azureactivedirectoryobjectid"
               && c.Values.Count == 1
               && (Equals(c.Values[0], CallerOid)
                   || Equals(c.Values[0], CallerOid.ToString())));
}
