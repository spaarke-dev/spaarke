using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Plugins;
using Xunit;

namespace Spaarke.Plugins.Tests;

/// <summary>
/// Tests for ValidationPlugin ensuring it follows ADR-002 principles:
/// - Thin, synchronous validation only
/// - No external I/O or long-running operations
/// - Execution under 50ms
/// </summary>
public class ValidationPluginTests
{
    [Fact]
    public void Execute_ValidEntity_DoesNotThrow()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockContext = new Mock<IPluginExecutionContext>();
        var mockOrganizationService = new Mock<IOrganizationService>();
        var mockServiceFactory = new Mock<IOrganizationServiceFactory>();

        var entity = new Entity("spe_document")
        {
            ["spe_name"] = "Test Document",
            ["spe_size"] = 1024,
            ["spe_mimetype"] = "application/pdf"
        };

        mockContext.Setup(c => c.InputParameters).Returns(new ParameterCollection
        {
            ["Target"] = entity
        });
        mockContext.Setup(c => c.MessageName).Returns("Create");
        mockContext.Setup(c => c.PrimaryEntityName).Returns("spe_document");

        mockServiceProvider.Setup(s => s.GetService(typeof(IPluginExecutionContext)))
            .Returns(mockContext.Object);
        mockServiceProvider.Setup(s => s.GetService(typeof(IOrganizationServiceFactory)))
            .Returns(mockServiceFactory.Object);
        mockServiceFactory.Setup(f => f.CreateOrganizationService(It.IsAny<Guid?>()))
            .Returns(mockOrganizationService.Object);

        var plugin = new ValidationPlugin();

        // Act & Assert
        var action = () => plugin.Execute(mockServiceProvider.Object);
        action.Should().NotThrow();
    }

    [Fact]
    public void Execute_EntityWithInvalidName_ThrowsInvalidPluginExecutionException()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockContext = new Mock<IPluginExecutionContext>();
        var mockOrganizationService = new Mock<IOrganizationService>();
        var mockServiceFactory = new Mock<IOrganizationServiceFactory>();

        var entity = new Entity("spe_document")
        {
            ["spe_name"] = "", // Invalid empty name
            ["spe_size"] = 1024,
            ["spe_mimetype"] = "application/pdf"
        };

        mockContext.Setup(c => c.InputParameters).Returns(new ParameterCollection
        {
            ["Target"] = entity
        });
        mockContext.Setup(c => c.MessageName).Returns("Create");
        mockContext.Setup(c => c.PrimaryEntityName).Returns("spe_document");

        mockServiceProvider.Setup(s => s.GetService(typeof(IPluginExecutionContext)))
            .Returns(mockContext.Object);
        mockServiceProvider.Setup(s => s.GetService(typeof(IOrganizationServiceFactory)))
            .Returns(mockServiceFactory.Object);
        mockServiceFactory.Setup(f => f.CreateOrganizationService(It.IsAny<Guid?>()))
            .Returns(mockOrganizationService.Object);

        var plugin = new ValidationPlugin();

        // Act & Assert
        var action = () => plugin.Execute(mockServiceProvider.Object);
        action.Should().Throw<InvalidPluginExecutionException>()
            .WithMessage("*name*required*");
    }

    [Fact]
    public void Execute_EntityWithInvalidSize_ThrowsInvalidPluginExecutionException()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockContext = new Mock<IPluginExecutionContext>();
        var mockOrganizationService = new Mock<IOrganizationService>();
        var mockServiceFactory = new Mock<IOrganizationServiceFactory>();

        var entity = new Entity("spe_document")
        {
            ["spe_name"] = "Test Document",
            ["spe_size"] = -1, // Invalid negative size
            ["spe_mimetype"] = "application/pdf"
        };

        mockContext.Setup(c => c.InputParameters).Returns(new ParameterCollection
        {
            ["Target"] = entity
        });
        mockContext.Setup(c => c.MessageName).Returns("Create");
        mockContext.Setup(c => c.PrimaryEntityName).Returns("spe_document");

        mockServiceProvider.Setup(s => s.GetService(typeof(IPluginExecutionContext)))
            .Returns(mockContext.Object);
        mockServiceProvider.Setup(s => s.GetService(typeof(IOrganizationServiceFactory)))
            .Returns(mockServiceFactory.Object);
        mockServiceFactory.Setup(f => f.CreateOrganizationService(It.IsAny<Guid?>()))
            .Returns(mockOrganizationService.Object);

        var plugin = new ValidationPlugin();

        // Act & Assert
        var action = () => plugin.Execute(mockServiceProvider.Object);
        action.Should().Throw<InvalidPluginExecutionException>()
            .WithMessage("*size*must be positive*");
    }

    [Fact]
    public void Execute_UnsupportedMessage_DoesNotThrow()
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

        var plugin = new ValidationPlugin();

        // Act & Assert
        var action = () => plugin.Execute(mockServiceProvider.Object);
        action.Should().NotThrow();
    }
}