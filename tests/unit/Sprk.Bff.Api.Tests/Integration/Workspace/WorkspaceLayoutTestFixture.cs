using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Tests.Integration.Workspace;

/// <summary>
/// Extended WebApplicationFactory for workspace layout integration tests.
/// Inherits from WorkspaceTestFixture (shared auth, cache, config) and adds
/// per-scenario IGenericEntityService mocking for layout CRUD operations.
///
/// Provides static factory methods for common test scenarios:
///   - WithUserLayouts(n): Returns n user layouts from RetrieveMultipleAsync
///   - WithEmptyDefaults(): Returns no default user layout
///   - WithUserDefault(id, name): Returns a specific user default
///   - WithSingleUserLayout(id, name): RetrieveAsync returns a specific layout
///   - WithRetrieveThrows(): RetrieveAsync throws (simulates not found)
///   - WithCreateSuccess(id, count): CreateAsync returns id; count existing layouts
///   - WithCreateAndExistingDefault(newId, existingDefaultId): Tests default toggle
///   - WithUpdateSuccess(id): RetrieveAsync returns layout; UpdateAsync succeeds
///   - WithDeleteSuccess(id): RetrieveAsync returns layout; UpdateAsync (deactivate) succeeds
/// </summary>
public class WorkspaceLayoutTestFixture : WorkspaceTestFixture
{
    private readonly Action<Mock<IGenericEntityService>>? _configureMock;

    /// <summary>
    /// When false, the fixture's identity resolver maps the caller's oid to NO systemuser, so
    /// ownership-scoped operations take their fail-closed branch.
    /// </summary>
    private bool _callerResolvesToSystemUser = true;

    /// <summary>
    /// Exposes the IGenericEntityService mock for verification in tests.
    /// </summary>
    public Mock<IGenericEntityService> EntityServiceMock { get; } = new();

    public WorkspaceLayoutTestFixture()
    {
    }

    private WorkspaceLayoutTestFixture(Action<Mock<IGenericEntityService>> configureMock)
    {
        _configureMock = configureMock;
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        // Call base to get auth, cache, Dataverse mocking, etc.
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            // Replace IGenericEntityService (registered as singleton facade onto IDataverseService)
            // with our mock so WorkspaceLayoutService gets controlled test data.
            _configureMock?.Invoke(EntityServiceMock);

            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(EntityServiceMock.Object);

            if (!_callerResolvesToSystemUser)
            {
                services.RemoveAll<Sprk.Bff.Api.Services.Identity.ISystemUserIdentityResolver>();
                services.AddSingleton<Sprk.Bff.Api.Services.Identity.ISystemUserIdentityResolver>(
                    new FixtureSystemUserIdentityResolver(resolvesAnyCaller: false));
            }
        });
    }

    // =========================================================================
    // Factory Methods — Each returns a configured fixture for a specific scenario
    // =========================================================================

    /// <summary>
    /// Creates a fixture where RetrieveMultipleAsync returns <paramref name="count"/>
    /// user layouts. Used for GET /api/workspace/layouts list tests.
    /// </summary>
    public static WorkspaceLayoutTestFixture WithUserLayouts(int count)
    {
        return new WorkspaceLayoutTestFixture(mock =>
        {
            var entities = CreateUserLayoutEntities(count);
            var collection = new EntityCollection(entities);

            mock.Setup(s => s.RetrieveMultipleAsync(
                    It.IsAny<QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(collection);
        });
    }

    /// <summary>
    /// Creates a fixture where the default layout query returns an empty result,
    /// causing the endpoint to fall back to the Corporate Workspace system layout.
    /// </summary>
    public static WorkspaceLayoutTestFixture WithEmptyDefaults()
    {
        return new WorkspaceLayoutTestFixture(mock =>
        {
            mock.Setup(s => s.RetrieveMultipleAsync(
                    It.IsAny<QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection());
        });
    }

    /// <summary>
    /// Creates a fixture where the default layout query returns a single user layout.
    /// </summary>
    public static WorkspaceLayoutTestFixture WithUserDefault(Guid id, string name)
    {
        return new WorkspaceLayoutTestFixture(mock =>
        {
            var entity = CreateLayoutEntity(id, name, isDefault: true);
            var collection = new EntityCollection(new List<Entity> { entity });

            mock.Setup(s => s.RetrieveMultipleAsync(
                    It.IsAny<QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(collection);
        });
    }

    /// <summary>
    /// Creates a fixture where RetrieveAsync returns a single user layout for GET by ID.
    /// RetrieveMultipleAsync returns empty (no additional user layouts for list queries).
    /// </summary>
    public static WorkspaceLayoutTestFixture WithSingleUserLayout(Guid id, string name)
    {
        return new WorkspaceLayoutTestFixture(mock =>
        {
            var entity = CreateLayoutEntity(id, name);

            mock.Setup(s => s.RetrieveAsync(
                    "sprk_workspacelayout",
                    id,
                    It.IsAny<string[]>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(entity);

            // List queries return empty
            mock.Setup(s => s.RetrieveMultipleAsync(
                    It.IsAny<QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection());
        });
    }

    /// <summary>
    /// Creates a fixture where RetrieveAsync throws an exception, simulating
    /// a not-found scenario in Dataverse. The service catches the exception
    /// and returns null, which the endpoint maps to 404.
    /// </summary>
    public static WorkspaceLayoutTestFixture WithRetrieveThrows()
    {
        return new WorkspaceLayoutTestFixture(mock =>
        {
            mock.Setup(s => s.RetrieveAsync(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string[]>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Entity not found"));

            // List queries also return empty (used by update/delete to verify ownership)
            mock.Setup(s => s.RetrieveMultipleAsync(
                    It.IsAny<QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection());
        });
    }

    /// <summary>
    /// Creates a fixture for successful layout creation. RetrieveMultipleAsync returns
    /// <paramref name="existingCount"/> existing layouts (for max limit checking),
    /// and CreateAsync returns the specified ID.
    /// </summary>
    public static WorkspaceLayoutTestFixture WithCreateSuccess(Guid createdId, int existingCount)
    {
        return new WorkspaceLayoutTestFixture(mock =>
        {
            var entities = CreateUserLayoutEntities(existingCount);
            var collection = new EntityCollection(entities);

            mock.Setup(s => s.RetrieveMultipleAsync(
                    It.IsAny<QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(collection);

            mock.Setup(s => s.CreateAsync(
                    It.IsAny<Entity>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(createdId);
        });
    }

    /// <summary>
    /// Creates a fixture for testing the default toggle on create. Returns one existing
    /// layout that is currently marked as default, so the create with isDefault=true
    /// must clear it via BulkUpdateAsync.
    /// </summary>
    public static WorkspaceLayoutTestFixture WithCreateAndExistingDefault(
        Guid newLayoutId, Guid existingDefaultId)
    {
        var fixture = new WorkspaceLayoutTestFixture(mock =>
        {
            var existingDefault = CreateLayoutEntity(existingDefaultId, "Old Default", isDefault: true);
            var collection = new EntityCollection(new List<Entity> { existingDefault });

            mock.Setup(s => s.RetrieveMultipleAsync(
                    It.IsAny<QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(collection);

            mock.Setup(s => s.CreateAsync(
                    It.IsAny<Entity>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(newLayoutId);

            mock.Setup(s => s.BulkUpdateAsync(
                    It.IsAny<string>(),
                    It.IsAny<List<(Guid id, Dictionary<string, object> fields)>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        });

        return fixture;
    }

    /// <summary>
    /// Creates a fixture for successful layout update. RetrieveAsync returns the layout
    /// for ownership verification, and UpdateAsync succeeds.
    /// </summary>
    public static WorkspaceLayoutTestFixture WithUpdateSuccess(Guid layoutId)
    {
        return new WorkspaceLayoutTestFixture(mock =>
        {
            var entity = CreateLayoutEntity(layoutId, "Original Name");

            mock.Setup(s => s.RetrieveAsync(
                    "sprk_workspacelayout",
                    layoutId,
                    It.IsAny<string[]>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(entity);

            mock.Setup(s => s.UpdateAsync(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // List queries return empty
            mock.Setup(s => s.RetrieveMultipleAsync(
                    It.IsAny<QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection());
        });
    }

    /// <summary>
    /// Creates a fixture whose layout is owned by SOMEONE ELSE — a systemuserid that is not the
    /// caller's. Every mutation is wired to SUCCEED, so a missing ownership guard shows up as a 2xx
    /// and a real <c>UpdateAsync</c> call rather than as an incidental failure.
    /// </summary>
    /// <remarks>
    /// The suite had no fixture like this before 2026-08-27, which is why the ownership guard could
    /// sit inert (it read an <c>ownerid</c> column that <c>SelectColumns</c> never requested) through
    /// a fully green run. A guard with only allow-path coverage is untested, not verified.
    /// </remarks>
    public static WorkspaceLayoutTestFixture WithForeignOwnedLayout(Guid layoutId)
    {
        return new WorkspaceLayoutTestFixture(mock =>
        {
            var entity = CreateLayoutEntity(layoutId, "Someone Else's Workspace");
            entity["ownerid"] = new EntityReference("systemuser", ForeignSystemUserId);

            mock.Setup(s => s.RetrieveAsync(
                    "sprk_workspacelayout",
                    layoutId,
                    It.IsAny<string[]>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(entity);

            // Deliberately permissive: if authorization lets the request through, the write
            // succeeds and the Verify(Never) assertions below fail loudly.
            mock.Setup(s => s.UpdateAsync(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            mock.Setup(s => s.RetrieveMultipleAsync(
                    It.IsAny<QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection());
        });
    }

    /// <summary>A systemuserid deliberately different from the caller's, for isolation tests.</summary>
    public static readonly Guid ForeignSystemUserId = Guid.Parse("00000000-dead-4000-8000-000000000bad");

    /// <summary>
    /// Records every <see cref="QueryExpression"/> the service issues, so a test can assert on the
    /// query that was BUILT rather than on the rows a mock chose to return.
    /// </summary>
    /// <param name="captured">Sink for the issued queries.</param>
    /// <param name="resolvesCaller">
    /// When false, the caller's oid resolves to no systemuser — exercising the fail-closed branch.
    /// </param>
    /// <remarks>
    /// Result-shape assertions cannot see a missing <c>WHERE</c>: the mock returns its canned rows
    /// either way, so an unscoped query and a scoped one are indistinguishable downstream. The
    /// disclosure lived in the query, so the query is the thing to observe.
    /// </remarks>
    public static WorkspaceLayoutTestFixture CapturingQueries(
        IList<QueryExpression> captured,
        bool resolvesCaller = true)
    {
        var fixture = new WorkspaceLayoutTestFixture(mock =>
        {
            mock.Setup(s => s.RetrieveMultipleAsync(
                    It.IsAny<QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .Callback<QueryExpression, CancellationToken>((q, _) => captured.Add(q))
                .ReturnsAsync(new EntityCollection());
        });

        fixture._callerResolvesToSystemUser = resolvesCaller;
        return fixture;
    }

    /// <summary>
    /// Creates a fixture for successful layout deletion. RetrieveAsync returns the layout,
    /// and UpdateAsync (soft delete via statecode=1) succeeds.
    /// </summary>
    public static WorkspaceLayoutTestFixture WithDeleteSuccess(Guid layoutId)
    {
        return new WorkspaceLayoutTestFixture(mock =>
        {
            var entity = CreateLayoutEntity(layoutId, "Doomed Layout");

            mock.Setup(s => s.RetrieveAsync(
                    "sprk_workspacelayout",
                    layoutId,
                    It.IsAny<string[]>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(entity);

            mock.Setup(s => s.UpdateAsync(
                    It.IsAny<string>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // List queries return empty
            mock.Setup(s => s.RetrieveMultipleAsync(
                    It.IsAny<QueryExpression>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection());
        });
    }

    // =========================================================================
    // Helpers — Entity construction
    // =========================================================================

    /// <summary>
    /// A deterministic <c>modifiedon</c> value used by every mock layout entity
    /// so tests can assert exact wire serialization (ISO-8601, UTC). Chosen as
    /// "2026-05-26T10:00:00Z" — a stable, recognizable timestamp.
    /// </summary>
    public static readonly DateTime FixedModifiedOnUtc =
        new(2026, 5, 26, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Creates a Dataverse Entity matching the sprk_workspacelayout schema.
    /// Sets <c>ownerid</c> to <see cref="WorkspaceTestConstants.TestSystemUserId"/> so ownership
    /// checks pass — deliberately a different value from the caller's Entra oid.
    /// R4 task 053 (B-4): also seeds <c>modifiedon</c> so MapToDto's
    /// <see cref="WorkspaceLayoutDto.ModifiedOn"/> mapping is exercised.
    /// </summary>
    private static Entity CreateLayoutEntity(
        Guid id,
        string name,
        bool isDefault = false,
        int sortOrder = 1,
        string templateId = "2-column",
        string sectionsJson = "[]")
    {
        var entity = new Entity("sprk_workspacelayout", id);
        entity["sprk_name"] = name;
        entity["sprk_layouttemplateid"] = templateId;
        entity["sprk_sectionsjson"] = sectionsJson;
        entity["sprk_isdefault"] = isDefault;
        entity["sprk_sortorder"] = sortOrder;
        // R4 task 053 (B-4 / FR-07): Dataverse maintains modifiedon
        // automatically; tests inject a fixed value so wire-shape assertions
        // are deterministic.
        entity["modifiedon"] = FixedModifiedOnUtc;

        // Own the row with the caller's SYSTEMUSERID — the value Dataverse stores in `ownerid` —
        // not their Entra oid. WorkspaceLayoutService resolves oid → systemuserid before comparing.
        //
        // This assignment used to be wrapped in `if (Guid.TryParse(TestUserId, ...))`. TestUserId is
        // not GUID-shaped, so the parse always failed and `ownerid` was NEVER SET — while the comment
        // asserted the opposite. The service's guard read `ownerId.HasValue`, so an absent column read
        // as "allowed" and every ownership test passed without exercising ownership at all.
        entity["ownerid"] = new EntityReference(
            "systemuser", Guid.Parse(WorkspaceTestConstants.TestSystemUserId));

        return entity;
    }

    /// <summary>
    /// Creates a list of user layout entities for RetrieveMultipleAsync responses.
    /// </summary>
    private static List<Entity> CreateUserLayoutEntities(int count)
    {
        var entities = new List<Entity>(count);
        for (var i = 0; i < count; i++)
        {
            entities.Add(CreateLayoutEntity(
                Guid.NewGuid(),
                $"User Layout {i + 1}",
                isDefault: false,
                sortOrder: i + 1));
        }
        return entities;
    }
}
