using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Privileges;

namespace Sprk.Bff.Api.IntegrationTests.Helpers;

/// <summary>
/// Test helper that configures the mocks attached to <see cref="DataverseIntegrationTestFixture"/>
/// for common Dataverse passthrough scenarios.
/// </summary>
/// <remarks>
/// <para>
/// <b>Architectural note (task 016 deviation D-016-01)</b>: the task POML originally specified a
/// helper that mocks the underlying Dataverse <c>ServiceClient</c>. In practice, <c>ServiceClient</c>
/// is a sealed type from <c>Microsoft.PowerPlatform.Dataverse.Client</c> with no mockable contract,
/// and the three "internal sealed" Dataverse projection services (<c>SavedQueryService</c>,
/// <c>MetadataService</c>, <c>FetchService</c>) hard-cast <see cref="IDataverseService"/> to the
/// concrete <c>DataverseServiceClientImpl</c> to reach <c>ServiceClient</c>. We therefore mock at the
/// <see cref="IDataverseService"/> + <see cref="IDataversePrivilegeChecker"/> interface boundary.
/// </para>
/// <para>
/// This helper provides convenience methods that configure the fixture's existing mocks. Tests call
/// these to set up canned responses without re-stating Moq boilerplate per test.
/// </para>
/// </remarks>
internal static class MockServiceClientFactory
{
    /// <summary>
    /// Configures the privilege checker to grant Read on the specified entity(ies). All other
    /// entities are denied.
    /// </summary>
    /// <param name="fixture">The fixture whose mocks to configure.</param>
    /// <param name="readableEntities">Entity logical names the test user can read.</param>
    public static void GrantReadOn(this DataverseIntegrationTestFixture fixture, params string[] readableEntities)
    {
        var readable = new HashSet<string>(readableEntities, StringComparer.OrdinalIgnoreCase);

        // HasReadPrivilegeAsync — true iff entity is in the readable set.
        fixture.PrivilegeCheckerMock
            .Setup(p => p.HasReadPrivilegeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string entity, CancellationToken _) => readable.Contains(entity));

        // GetReadableEntitiesAsync — returns the configured readable set.
        // Used by the FetchXML cross-entity check (filter calls this when >1 distinct entity).
        fixture.PrivilegeCheckerMock
            .Setup(p => p.GetReadableEntitiesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, CancellationToken _) =>
                (IReadOnlySet<string>)new HashSet<string>(readable, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Configures the privilege checker to deny ALL Read privilege (for 403 read-deny tests).
    /// </summary>
    public static void DenyAllReads(this DataverseIntegrationTestFixture fixture)
    {
        fixture.PrivilegeCheckerMock
            .Setup(p => p.HasReadPrivilegeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        fixture.PrivilegeCheckerMock
            .Setup(p => p.GetReadableEntitiesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Configures <see cref="IDataverseService.RetrieveAsync"/> to return a populated record for the
    /// given entity + id. Used by the RecordService happy-path tests.
    /// </summary>
    /// <param name="fixture">The fixture whose mocks to configure.</param>
    /// <param name="entityLogicalName">The entity the mock returns the record for.</param>
    /// <param name="recordId">The record id.</param>
    /// <param name="attributes">Attribute name → value pairs to populate on the returned Entity.</param>
    public static void ReturnRecord(
        this DataverseIntegrationTestFixture fixture,
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object> attributes)
    {
        var entity = new Entity(entityLogicalName, recordId);
        foreach (var kvp in attributes)
        {
            entity[kvp.Key] = kvp.Value;
        }

        fixture.DataverseServiceMock
            .Setup(d => d.RetrieveAsync(entityLogicalName, recordId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
    }

    /// <summary>
    /// Configures <see cref="IDataverseService.RetrieveAsync"/> to throw a Dataverse-style
    /// "record does not exist" exception for any retrieve. Used by 404 tests.
    /// </summary>
    public static void ReturnRecordNotFound(this DataverseIntegrationTestFixture fixture)
    {
        fixture.DataverseServiceMock
            .Setup(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Record with Id = aaaaaaaa-1111-1111-1111-111111111111 Does Not Exist"));
    }

    /// <summary>
    /// Returns how many times the privilege checker's <c>HasReadPrivilegeAsync</c> was invoked
    /// for the given entity. Used to verify the authorization filter's per-request behavior.
    /// </summary>
    public static int HasReadPrivilegeCalls(this DataverseIntegrationTestFixture fixture, string entityLogicalName)
    {
        return fixture.PrivilegeCheckerMock.Invocations
            .Count(i => i.Method.Name == nameof(IDataversePrivilegeChecker.HasReadPrivilegeAsync)
                        && i.Arguments.Count >= 2
                        && string.Equals(i.Arguments[1] as string, entityLogicalName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns how many times the privilege checker's <c>GetReadableEntitiesAsync</c> was invoked.
    /// Used to verify the FetchXML cross-entity check makes a single call regardless of breadth.
    /// </summary>
    public static int GetReadableEntitiesCalls(this DataverseIntegrationTestFixture fixture)
    {
        return fixture.PrivilegeCheckerMock.Invocations
            .Count(i => i.Method.Name == nameof(IDataversePrivilegeChecker.GetReadableEntitiesAsync));
    }

    /// <summary>
    /// Resets all mock invocation counters. Tests that verify call counts across multiple endpoint
    /// hits should call this between phases (e.g., set-up vs measurement).
    /// </summary>
    public static void ResetInvocationCounts(this DataverseIntegrationTestFixture fixture)
    {
        fixture.PrivilegeCheckerMock.Invocations.Clear();
        fixture.DataverseServiceMock.Invocations.Clear();
    }
}
