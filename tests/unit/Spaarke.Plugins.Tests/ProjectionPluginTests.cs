using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Plugins;
using Xunit;

namespace Spaarke.Plugins.Tests;

/// <summary>
/// Tests for ProjectionPlugin ensuring it follows ADR-002 principles:
/// - Thin, synchronous denormalization/projection only
/// - No external I/O or long-running operations
/// - Execution under 50ms
/// </summary>
public class ProjectionPluginTests
{
    [Fact]
    public void Execute_DocumentCreated_UpdatesContainerDocumentCount()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockContext = new Mock<IPluginExecutionContext>();
        var mockOrganizationService = new Mock<IOrganizationService>();
        var mockServiceFactory = new Mock<IOrganizationServiceFactory>();

        var containerId = Guid.NewGuid();
        var documentEntity = new Entity("spe_document")
        {
            Id = Guid.NewGuid(),
            ["spe_containerid"] = new EntityReference("spe_container", containerId),
            ["spe_name"] = "Test Document",
            ["spe_size"] = 1024
        };

        var containerEntity = new Entity("spe_container")
        {
            Id = containerId,
            ["spe_documentcount"] = 5
        };

        mockContext.Setup(c => c.InputParameters).Returns(new ParameterCollection
        {
            ["Target"] = documentEntity
        });
        mockContext.Setup(c => c.MessageName).Returns("Create");
        mockContext.Setup(c => c.PrimaryEntityName).Returns("spe_document");

        mockServiceProvider.Setup(s => s.GetService(typeof(IPluginExecutionContext)))
            .Returns(mockContext.Object);
        mockServiceProvider.Setup(s => s.GetService(typeof(IOrganizationServiceFactory)))
            .Returns(mockServiceFactory.Object);
        mockServiceFactory.Setup(f => f.CreateOrganizationService(It.IsAny<Guid?>()))
            .Returns(mockOrganizationService.Object);

        // Setup retrieve for container
        mockOrganizationService.Setup(s => s.Retrieve("spe_container", containerId, It.IsAny<ColumnSet>()))
            .Returns(containerEntity);

        var plugin = new ProjectionPlugin();

        // Act
        plugin.Execute(mockServiceProvider.Object);

        // Assert
        mockOrganizationService.Verify(s => s.Update(It.Is<Entity>(e =>
            e.LogicalName == "spe_container" &&
            e.Id == containerId &&
            e.GetAttributeValue<int>("spe_documentcount") == 6)), Times.Once);
    }

    [Fact]
    public void Execute_DocumentDeleted_UpdatesContainerDocumentCount()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockContext = new Mock<IPluginExecutionContext>();
        var mockOrganizationService = new Mock<IOrganizationService>();
        var mockServiceFactory = new Mock<IOrganizationServiceFactory>();

        var containerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var preImageEntity = new Entity("spe_document")
        {
            Id = documentId,
            ["spe_containerid"] = new EntityReference("spe_container", containerId),
            ["spe_name"] = "Test Document"
        };

        var containerEntity = new Entity("spe_container")
        {
            Id = containerId,
            ["spe_documentcount"] = 5
        };

        mockContext.Setup(c => c.MessageName).Returns("Delete");
        mockContext.Setup(c => c.PrimaryEntityName).Returns("spe_document");
        mockContext.Setup(c => c.PrimaryEntityId).Returns(documentId);
        mockContext.Setup(c => c.PreEntityImages).Returns(new EntityImageCollection
        {
            ["PreImage"] = preImageEntity
        });

        mockServiceProvider.Setup(s => s.GetService(typeof(IPluginExecutionContext)))
            .Returns(mockContext.Object);
        mockServiceProvider.Setup(s => s.GetService(typeof(IOrganizationServiceFactory)))
            .Returns(mockServiceFactory.Object);
        mockServiceFactory.Setup(f => f.CreateOrganizationService(It.IsAny<Guid?>()))
            .Returns(mockOrganizationService.Object);

        // Setup retrieve for container
        mockOrganizationService.Setup(s => s.Retrieve("spe_container", containerId, It.IsAny<ColumnSet>()))
            .Returns(containerEntity);

        var plugin = new ProjectionPlugin();

        // Act
        plugin.Execute(mockServiceProvider.Object);

        // Assert
        mockOrganizationService.Verify(s => s.Update(It.Is<Entity>(e =>
            e.LogicalName == "spe_container" &&
            e.Id == containerId &&
            e.GetAttributeValue<int>("spe_documentcount") == 4)), Times.Once);
    }

    [Fact]
    public void Execute_DocumentWithoutContainer_DoesNotUpdateContainer()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockContext = new Mock<IPluginExecutionContext>();
        var mockOrganizationService = new Mock<IOrganizationService>();
        var mockServiceFactory = new Mock<IOrganizationServiceFactory>();

        var documentEntity = new Entity("spe_document")
        {
            Id = Guid.NewGuid(),
            ["spe_name"] = "Test Document",
            ["spe_size"] = 1024
            // No container reference
        };

        mockContext.Setup(c => c.InputParameters).Returns(new ParameterCollection
        {
            ["Target"] = documentEntity
        });
        mockContext.Setup(c => c.MessageName).Returns("Create");
        mockContext.Setup(c => c.PrimaryEntityName).Returns("spe_document");

        mockServiceProvider.Setup(s => s.GetService(typeof(IPluginExecutionContext)))
            .Returns(mockContext.Object);
        mockServiceProvider.Setup(s => s.GetService(typeof(IOrganizationServiceFactory)))
            .Returns(mockServiceFactory.Object);
        mockServiceFactory.Setup(f => f.CreateOrganizationService(It.IsAny<Guid?>()))
            .Returns(mockOrganizationService.Object);

        var plugin = new ProjectionPlugin();

        // Act
        plugin.Execute(mockServiceProvider.Object);

        // Assert
        mockOrganizationService.Verify(s => s.Update(It.IsAny<Entity>()), Times.Never);
    }

    [Fact]
    public void Execute_UnsupportedMessage_DoesNotPerformProjection()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockContext = new Mock<IPluginExecutionContext>();
        var mockOrganizationService = new Mock<IOrganizationService>();
        var mockServiceFactory = new Mock<IOrganizationServiceFactory>();

        mockContext.Setup(c => c.MessageName).Returns("Retrieve");
        mockContext.Setup(c => c.PrimaryEntityName).Returns("spe_document");

        mockServiceProvider.Setup(s => s.GetService(typeof(IPluginExecutionContext)))
            .Returns(mockContext.Object);
        mockServiceProvider.Setup(s => s.GetService(typeof(IOrganizationServiceFactory)))
            .Returns(mockServiceFactory.Object);
        mockServiceFactory.Setup(f => f.CreateOrganizationService(It.IsAny<Guid?>()))
            .Returns(mockOrganizationService.Object);

        var plugin = new ProjectionPlugin();

        // Act
        plugin.Execute(mockServiceProvider.Object);

        // Assert
        mockOrganizationService.Verify(s => s.Update(It.IsAny<Entity>()), Times.Never);
    }
}
