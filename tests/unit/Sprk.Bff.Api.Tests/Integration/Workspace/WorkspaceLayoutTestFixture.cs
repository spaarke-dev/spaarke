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
    /// Creates a Dataverse Entity matching the sprk_workspacelayout schema.
    /// Includes ownerid set to the test user so ownership checks pass.
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

        // Set ownerid to the test user so ownership verification passes.
        // WorkspaceLayoutService checks ownerid against the authenticated user's "oid" claim.
        if (Guid.TryParse(WorkspaceTestConstants.TestUserId, out var userGuid))
        {
            entity["ownerid"] = new EntityReference("systemuser", userGuid);
        }

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
