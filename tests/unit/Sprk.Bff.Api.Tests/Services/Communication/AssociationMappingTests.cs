using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using System.Net;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Tests that MapAssociationFields correctly maps all 8 entity types
/// to their corresponding Dataverse regarding lookup fields and denormalized fields.
/// Tests are exercised through SendAsync by capturing the Entity passed to IDataverseService.CreateAsync.
/// </summary>
public class AssociationMappingTests
{
    #region Test Infrastructure

    private readonly Mock<IGraphClientFactory> _graphClientFactoryMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<CommunicationService>> _loggerMock;
    private DataverseEntity? _capturedEntity;

    public AssociationMappingTests()
    {
        _graphClientFactoryMock = new Mock<IGraphClientFactory>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<CommunicationService>>();

        _graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(CreateMockGraphClient());

        _dataverseServiceMock
            .Setup(ds => ds.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((entity, _) => _capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());
    }

    private CommunicationService CreateService()
    {
        var options = new CommunicationOptions
        {
            ApprovedSenders = new[]
            {
                new ApprovedSenderConfig
                {
                    Email = "noreply@contoso.com",
                    DisplayName = "Contoso Notifications",
                    IsDefault = true
                }
            },
            DefaultMailbox = "noreply@contoso.com"
        };

        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        var emlGenerationService = new EmlGenerationService(
            Mock.Of<ILogger<EmlGenerationService>>());

        var fakeGraphFactory = Mock.Of<IGraphClientFactory>();
        var speFileStore = new SpeFileStore(
            new ContainerOperations(fakeGraphFactory, Mock.Of<ILogger<ContainerOperations>>()),
            new DriveItemOperations(fakeGraphFactory, Mock.Of<ILogger<DriveItemOperations>>()),
            new UploadSessionManager(fakeGraphFactory, Mock.Of<ILogger<UploadSessionManager>>()),
            new UserOperations(fakeGraphFactory, Mock.Of<ILogger<UserOperations>>()));

        return new CommunicationService(
            _graphClientFactoryMock.Object,
            senderValidator,
            _dataverseServiceMock.Object,
            emlGenerationService,
            speFileStore,
            Options.Create(options),
            _loggerMock.Object);
    }

    private static SendCommunicationRequest CreateRequestWithAssociations(
        params CommunicationAssociation[] associations) => new()
    {
        To = new[] { "recipient@example.com" },
        Subject = "Test Subject",
        Body = "<p>Test body</p>",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
        Associations = associations,
        CorrelationId = "assoc-test-001"
    };

    private static GraphServiceClient CreateMockGraphClient()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.Accepted);
        var httpClient = new HttpClient(handler);
        return new GraphServiceClient(httpClient);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public MockHttpMessageHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_statusCode));
    }

    #endregion

    #region Entity Type: sprk_matter

    [Fact]
    public async Task SendAsync_WithMatterAssociation_SetsRegardingMatterLookup()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_matter",
            EntityId = matterId,
            EntityName = "Smith v. Jones",
            EntityUrl = "https://crm.example.com/sprk_matters(abc)"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var entityRef = _capturedEntity!["sprk_regardingmatter"] as EntityReference;
        entityRef.Should().NotBeNull("sprk_matter maps to sprk_regardingmatter lookup");
        entityRef!.LogicalName.Should().Be("sprk_matter");
        entityRef.Id.Should().Be(matterId);
    }

    #endregion

    #region Entity Type: sprk_organization

    [Fact]
    public async Task SendAsync_WithOrganizationAssociation_SetsRegardingOrganizationLookup()
    {
        // Arrange - NOTE: the lookup field is sprk_regardingorganization, NOT sprk_regardingaccount
        var orgId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_organization",
            EntityId = orgId,
            EntityName = "Acme Corp"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var entityRef = _capturedEntity!["sprk_regardingorganization"] as EntityReference;
        entityRef.Should().NotBeNull("sprk_organization maps to sprk_regardingorganization, NOT sprk_regardingaccount");
        entityRef!.LogicalName.Should().Be("sprk_organization");
        entityRef.Id.Should().Be(orgId);
    }

    #endregion

    #region Entity Type: contact

    [Fact]
    public async Task SendAsync_WithContactAssociation_SetsRegardingPersonLookup()
    {
        // Arrange - NOTE: the lookup field is sprk_regardingperson, NOT sprk_regardingcontact
        var contactId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "contact",
            EntityId = contactId,
            EntityName = "Jane Doe"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var entityRef = _capturedEntity!["sprk_regardingperson"] as EntityReference;
        entityRef.Should().NotBeNull("contact maps to sprk_regardingperson, NOT sprk_regardingcontact");
        entityRef!.LogicalName.Should().Be("contact");
        entityRef.Id.Should().Be(contactId);
    }

    #endregion

    #region Entity Type: sprk_project

    [Fact]
    public async Task SendAsync_WithProjectAssociation_SetsRegardingProjectLookup()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_project",
            EntityId = projectId,
            EntityName = "Project Alpha"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var entityRef = _capturedEntity!["sprk_regardingproject"] as EntityReference;
        entityRef.Should().NotBeNull();
        entityRef!.LogicalName.Should().Be("sprk_project");
        entityRef.Id.Should().Be(projectId);
    }

    #endregion

    #region Entity Type: sprk_analysis

    [Fact]
    public async Task SendAsync_WithAnalysisAssociation_SetsRegardingAnalysisLookup()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_analysis",
            EntityId = analysisId,
            EntityName = "Risk Analysis Report"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var entityRef = _capturedEntity!["sprk_regardinganalysis"] as EntityReference;
        entityRef.Should().NotBeNull();
        entityRef!.LogicalName.Should().Be("sprk_analysis");
        entityRef.Id.Should().Be(analysisId);
    }

    #endregion

    #region Entity Type: sprk_budget

    [Fact]
    public async Task SendAsync_WithBudgetAssociation_SetsRegardingBudgetLookup()
    {
        // Arrange
        var budgetId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_budget",
            EntityId = budgetId,
            EntityName = "Q4 Budget"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var entityRef = _capturedEntity!["sprk_regardingbudget"] as EntityReference;
        entityRef.Should().NotBeNull();
        entityRef!.LogicalName.Should().Be("sprk_budget");
        entityRef.Id.Should().Be(budgetId);
    }

    #endregion

    #region Entity Type: sprk_invoice

    [Fact]
    public async Task SendAsync_WithInvoiceAssociation_SetsRegardingInvoiceLookup()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_invoice",
            EntityId = invoiceId,
            EntityName = "INV-2026-001"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var entityRef = _capturedEntity!["sprk_regardinginvoice"] as EntityReference;
        entityRef.Should().NotBeNull();
        entityRef!.LogicalName.Should().Be("sprk_invoice");
        entityRef.Id.Should().Be(invoiceId);
    }

    #endregion

    #region Entity Type: sprk_workassignment

    [Fact]
    public async Task SendAsync_WithWorkAssignmentAssociation_SetsRegardingWorkAssignmentLookup()
    {
        // Arrange
        var waId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_workassignment",
            EntityId = waId,
            EntityName = "Document Review Task"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var entityRef = _capturedEntity!["sprk_regardingworkassignment"] as EntityReference;
        entityRef.Should().NotBeNull();
        entityRef!.LogicalName.Should().Be("sprk_workassignment");
        entityRef.Id.Should().Be(waId);
    }

    #endregion

    #region All 8 Entity Types - Theory Test

    [Theory]
    [InlineData("sprk_matter", "sprk_regardingmatter")]
    [InlineData("sprk_organization", "sprk_regardingorganization")]
    [InlineData("contact", "sprk_regardingperson")]
    [InlineData("sprk_project", "sprk_regardingproject")]
    [InlineData("sprk_analysis", "sprk_regardinganalysis")]
    [InlineData("sprk_budget", "sprk_regardingbudget")]
    [InlineData("sprk_invoice", "sprk_regardinginvoice")]
    [InlineData("sprk_workassignment", "sprk_regardingworkassignment")]
    public async Task SendAsync_WithEntityType_SetsCorrectLookupField(string entityType, string expectedLookupField)
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = entityType,
            EntityId = entityId,
            EntityName = "Test Record"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var entityRef = _capturedEntity![expectedLookupField] as EntityReference;
        entityRef.Should().NotBeNull($"{entityType} should map to {expectedLookupField}");
        entityRef!.LogicalName.Should().Be(entityType);
        entityRef.Id.Should().Be(entityId);
    }

    #endregion

    #region EntityReference Format (NOT @odata.bind)

    [Fact]
    public async Task SendAsync_UsesEntityReference_NotOdataBind()
    {
        // Arrange - verify the lookup value is EntityReference, not a string like @odata.bind
        var matterId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_matter",
            EntityId = matterId,
            EntityName = "Test Matter"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var value = _capturedEntity!["sprk_regardingmatter"];
        value.Should().BeOfType<EntityReference>("lookup should use EntityReference, not @odata.bind string");
    }

    #endregion

    #region Association Count Field

    [Fact]
    public async Task SendAsync_SetsAssociationCount_ToNumberOfAssociations()
    {
        // Arrange
        var service = CreateService();
        var request = CreateRequestWithAssociations(
            new CommunicationAssociation { EntityType = "sprk_matter", EntityId = Guid.NewGuid(), EntityName = "Matter 1" },
            new CommunicationAssociation { EntityType = "contact", EntityId = Guid.NewGuid(), EntityName = "Contact 1" },
            new CommunicationAssociation { EntityType = "sprk_project", EntityId = Guid.NewGuid(), EntityName = "Project 1" });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_associationcount"].Should().Be(3);
    }

    [Fact]
    public async Task SendAsync_WithSingleAssociation_SetsAssociationCountToOne()
    {
        // Arrange
        var service = CreateService();
        var request = CreateRequestWithAssociations(
            new CommunicationAssociation { EntityType = "sprk_matter", EntityId = Guid.NewGuid(), EntityName = "Only Matter" });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_associationcount"].Should().Be(1);
    }

    #endregion

    #region Denormalized Regarding Fields

    [Fact]
    public async Task SendAsync_SetsDenormalizedRegardingRecordName()
    {
        // Arrange
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_matter",
            EntityId = Guid.NewGuid(),
            EntityName = "Smith v. Jones"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_regardingrecordname"].Should().Be("Smith v. Jones");
    }

    [Fact]
    public async Task SendAsync_SetsDenormalizedRegardingRecordId()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_matter",
            EntityId = entityId,
            EntityName = "Test Matter"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_regardingrecordid"].Should().Be(entityId.ToString());
    }

    [Fact]
    public async Task SendAsync_SetsDenormalizedRegardingRecordUrl_WhenProvided()
    {
        // Arrange
        var service = CreateService();
        var url = "https://crm.example.com/main.aspx?id=abc";
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_matter",
            EntityId = Guid.NewGuid(),
            EntityName = "Test Matter",
            EntityUrl = url
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_regardingrecordurl"].Should().Be(url);
    }

    [Fact]
    public async Task SendAsync_DoesNotSetRegardingRecordName_WhenEntityNameIsNull()
    {
        // Arrange
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_matter",
            EntityId = Guid.NewGuid(),
            EntityName = null
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!.Attributes.ContainsKey("sprk_regardingrecordname").Should().BeFalse(
            "sprk_regardingrecordname should not be set when EntityName is null");
    }

    [Fact]
    public async Task SendAsync_DoesNotSetRegardingRecordUrl_WhenEntityUrlIsNull()
    {
        // Arrange
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_matter",
            EntityId = Guid.NewGuid(),
            EntityName = "Test Matter",
            EntityUrl = null
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!.Attributes.ContainsKey("sprk_regardingrecordurl").Should().BeFalse(
            "sprk_regardingrecordurl should not be set when EntityUrl is null");
    }

    #endregion

    #region Null and Empty Associations

    [Fact]
    public async Task SendAsync_WithNullAssociations_DoesNotSetAssociationFields()
    {
        // Arrange
        var service = CreateService();
        var request = new SendCommunicationRequest
        {
            To = new[] { "recipient@example.com" },
            Subject = "Test Subject",
            Body = "<p>Test body</p>",
            BodyFormat = BodyFormat.HTML,
            CommunicationType = CommunicationType.Email,
            Associations = null,
            CorrelationId = "null-assoc-test"
        };

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!.Attributes.ContainsKey("sprk_associationcount").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardingrecordname").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardingrecordid").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardingrecordurl").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WithEmptyAssociations_DoesNotSetAssociationFields()
    {
        // Arrange
        var service = CreateService();
        var request = new SendCommunicationRequest
        {
            To = new[] { "recipient@example.com" },
            Subject = "Test Subject",
            Body = "<p>Test body</p>",
            BodyFormat = BodyFormat.HTML,
            CommunicationType = CommunicationType.Email,
            Associations = Array.Empty<CommunicationAssociation>(),
            CorrelationId = "empty-assoc-test"
        };

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!.Attributes.ContainsKey("sprk_associationcount").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardingrecordname").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardingrecordid").Should().BeFalse();
    }

    #endregion

    #region Unknown Entity Type

    [Fact]
    public async Task SendAsync_WithUnknownEntityType_DoesNotSetRegardingLookup()
    {
        // Arrange
        var service = CreateService();
        var unknownId = Guid.NewGuid();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "sprk_unknown_entity",
            EntityId = unknownId,
            EntityName = "Unknown Thing",
            EntityUrl = "https://crm.example.com/unknown"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();

        // Association count and denormalized fields should still be set
        _capturedEntity!["sprk_associationcount"].Should().Be(1);
        _capturedEntity["sprk_regardingrecordname"].Should().Be("Unknown Thing");
        _capturedEntity["sprk_regardingrecordid"].Should().Be(unknownId.ToString());
        _capturedEntity["sprk_regardingrecordurl"].Should().Be("https://crm.example.com/unknown");

        // But no regarding lookup field should be set (none of the known ones)
        _capturedEntity.Attributes.ContainsKey("sprk_regardingmatter").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardingorganization").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardingperson").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardingproject").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardinganalysis").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardingbudget").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardinginvoice").Should().BeFalse();
        _capturedEntity.Attributes.ContainsKey("sprk_regardingworkassignment").Should().BeFalse();
    }

    #endregion

    #region Case Insensitivity of Entity Type

    [Fact]
    public async Task SendAsync_WithUpperCaseEntityType_StillMapsCorrectly()
    {
        // Arrange - the RegardingLookupMap uses OrdinalIgnoreCase
        var matterId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(new CommunicationAssociation
        {
            EntityType = "SPRK_MATTER",
            EntityId = matterId,
            EntityName = "Upper Case Test"
        });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var entityRef = _capturedEntity!["sprk_regardingmatter"] as EntityReference;
        entityRef.Should().NotBeNull("RegardingLookupMap uses OrdinalIgnoreCase");
        entityRef!.Id.Should().Be(matterId);
    }

    #endregion

    #region Primary Association Only (First in Array)

    [Fact]
    public async Task SendAsync_WithMultipleAssociations_UsesFirstForRegardingLookup()
    {
        // Arrange - first association determines the lookup, not subsequent ones
        var matterId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var service = CreateService();
        var request = CreateRequestWithAssociations(
            new CommunicationAssociation { EntityType = "sprk_matter", EntityId = matterId, EntityName = "Primary Matter" },
            new CommunicationAssociation { EntityType = "contact", EntityId = contactId, EntityName = "Secondary Contact" });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();

        // Primary (first) association should set the regarding lookup
        var matterRef = _capturedEntity!["sprk_regardingmatter"] as EntityReference;
        matterRef.Should().NotBeNull();
        matterRef!.Id.Should().Be(matterId);

        // Secondary association should NOT set its own regarding lookup
        _capturedEntity.Attributes.ContainsKey("sprk_regardingperson").Should().BeFalse(
            "only the primary (first) association sets the regarding lookup");

        // Denormalized fields should reflect primary association
        _capturedEntity["sprk_regardingrecordname"].Should().Be("Primary Matter");
    }

    #endregion
}
