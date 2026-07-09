using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for UpdateRecordNodeExecutor.
/// Tests validation, template substitution, and Dataverse record updates.
///
/// Metadata-driven coercion tests (R5 task 030 / FR-C1) construct a REAL
/// <see cref="MetadataService"/> instance (not a mock — it is sealed with no seam) whose
/// underlying <see cref="IDistributedCache"/> is mocked to return a pre-seeded cache HIT. This
/// exercises the actual production cache-read path (see <see cref="MetadataService.GetMetadataAsync"/>)
/// without hitting Dataverse, and lets the tests assert the cache is read exactly ONCE per
/// ExecuteAsync run regardless of how many String-typed mappings target the same entity.
/// </summary>
public class UpdateRecordNodeExecutorTests
{
    private readonly Mock<ITemplateEngine> _templateEngineMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<UpdateRecordNodeExecutor>> _loggerMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly UpdateRecordNodeExecutor _executor;

    private static readonly JsonSerializerOptions MetadataCacheJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UpdateRecordNodeExecutorTests()
    {
        _templateEngineMock = new Mock<ITemplateEngine>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<UpdateRecordNodeExecutor>>();

        // Default: no scope factory interaction expected unless a test opts into metadata-driven
        // coercion via WithMetadataScopeFactory(...). CreateScope() throws if unexpectedly invoked
        // so a missing setup fails loudly rather than silently no-op'ing.
        _scopeFactoryMock = new Mock<IServiceScopeFactory>(MockBehavior.Strict);

        _executor = new UpdateRecordNodeExecutor(
            _templateEngineMock.Object,
            _dataverseServiceMock.Object,
            _scopeFactoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Builds a real <see cref="MetadataService"/> backed by a mocked <see cref="IDistributedCache"/>
    /// pre-seeded with a cache HIT for <paramref name="entityLogicalName"/>, and wires
    /// <see cref="_scopeFactoryMock"/> to resolve it via the Singleton+Scoped bridge pattern the
    /// executor uses. Returns the <see cref="IDistributedCache"/> mock so tests can assert
    /// <c>GetAsync</c> call counts (single-lookup-per-run acceptance criterion).
    /// </summary>
    private Mock<IDistributedCache> UseMetadataFor(string entityLogicalName, EntityMetadataDto dto)
    {
        var cacheKey = $"sdap:dv:entitymetadata:{entityLogicalName.ToLowerInvariant()}";
        var cacheMock = new Mock<IDistributedCache>();
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto, MetadataCacheJsonOptions));
        cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        var metadataService = new MetadataService(
            _dataverseServiceMock.Object,
            cacheMock.Object,
            Mock.Of<ILogger<MetadataService>>());

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(MetadataService)))
            .Returns(metadataService);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        _scopeFactoryMock
            .Setup(f => f.CreateScope())
            .Returns(scopeMock.Object);

        return cacheMock;
    }

    /// <summary>
    /// Convenience builder for the <c>sprk_documenttype</c> Choice column used throughout the
    /// R7 UAT incident that motivated task 030 (see notes/inbound-from-r7/06-...). Values match
    /// the real option set cited in the task POML acceptance criteria.
    /// </summary>
    private static EntityMetadataDto BuildDocumentTypeChoiceMetadata(string entity = "sprk_document") =>
        new(
            LogicalName: entity,
            PrimaryIdAttribute: $"{entity}id",
            PrimaryNameAttribute: "sprk_name",
            Attributes: new List<AttributeDto>
            {
                new(
                    LogicalName: "sprk_documenttype",
                    AttributeType: "Picklist",
                    Format: null,
                    IsPrimaryName: false,
                    IsPrimaryId: false,
                    OptionSet: new OptionSetDto(new List<OptionDto>
                    {
                        new(100000000, "Contract", null),
                        new(100000001, "Invoice", null),
                        new(100000012, "Other", null)
                    })),
                new(
                    LogicalName: "sprk_isconfidential",
                    AttributeType: "Boolean",
                    Format: null,
                    IsPrimaryName: false,
                    IsPrimaryId: false,
                    OptionSet: null),
                new(
                    LogicalName: "sprk_partycount",
                    AttributeType: "Integer",
                    Format: null,
                    IsPrimaryName: false,
                    IsPrimaryId: false,
                    OptionSet: null),
                new(
                    LogicalName: "sprk_summary",
                    AttributeType: "Memo",
                    Format: null,
                    IsPrimaryName: false,
                    IsPrimaryId: false,
                    OptionSet: null)
            });

    #region SupportedExecutorTypes Tests

    [Fact]
    public void SupportedActionTypes_ContainsUpdateRecord()
    {
        // Assert
        _executor.SupportedExecutorTypes.Should().Contain(ExecutorType.UpdateRecord);
        _executor.SupportedExecutorTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void Validate_WithValidConfig_ReturnsSuccess()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{""sprk_status"":""Completed""}}
        }}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithNoConfig_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext(null);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ConfigJson"));
    }

    [Fact]
    public void Validate_WithEmptyConfig_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext("");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithInvalidJson_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext("{invalid json}");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Failed to parse") || e.Contains("Invalid"));
    }

    [Fact]
    public void Validate_WithMissingEntityName_ReturnsFailure()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""recordId"":""{recordId}"",
            ""fields"":{{""status"":""Done""}}
        }}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("logical name") || e.Contains("EntityLogicalName"));
    }

    [Fact]
    public void Validate_WithMissingRecordId_ReturnsFailure()
    {
        // Arrange
        var config = @"{
            ""entityLogicalName"":""sprk_document"",
            ""fields"":{""status"":""Done""}
        }";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Record ID") || e.Contains("RecordId"));
    }

    [Fact]
    public void Validate_WithMissingFields_ReturnsFailure()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}""
        }}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("field"));
    }

    [Fact]
    public void Validate_WithEmptyFields_ReturnsFailure()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{}}
        }}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("field"));
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithValidConfig_ReturnsSuccessfulOutput()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{""sprk_analysisstatus"":""Completed""}}
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.NodeId.Should().Be(context.Node.Id);
        result.OutputVariable.Should().Be(context.Node.OutputVariable);
        result.TextContent.Should().Contain("Updated");

        var data = result.GetData<UpdateRecordOutput>();
        data.Should().NotBeNull();
        data!.Updated.Should().BeTrue();
        data.EntityLogicalName.Should().Be("sprk_document");
        data.RecordId.Should().Be(recordId);
    }

    [Fact]
    public async Task ExecuteAsync_WithTemplateVariables_SubstitutesCorrectly()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{""sprk_summary"":""{{{{analysis.output.summary}}}}""}}
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidRecordId_ReturnsErrorOutput()
    {
        // Arrange
        var config = @"{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""not-a-guid"",
            ""fields"":{""status"":""Done""}
        }";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid record ID");
    }

    [Fact]
    public async Task ExecuteAsync_WithValidationFailure_ReturnsErrorOutput()
    {
        // Arrange
        var context = CreateValidContext("{}");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleFields_UpdatesAllFields()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{
                ""sprk_status"":""Analyzed"",
                ""sprk_summary"":""Summary text"",
                ""sprk_partycount"":""5""
            }}
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var data = result.GetData<UpdateRecordOutput>();
        data!.FieldsUpdated.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExecuteAsync_WithLookupFields_FormatsODataBind()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fields"":{{""sprk_status"":""Assigned""}},
            ""lookups"":{{
                ""ownerid"":{{
                    ""targetEntity"":""systemuser"",
                    ""targetId"":""{ownerId}""
                }}
            }}
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region Metadata-Driven Coercion Tests (R5 task 030 / FR-C1)

    [Fact]
    public async Task ExecuteAsync_WithStringMappingToValidChoiceLabel_CoercesToNumericOptionValue()
    {
        // Arrange — "Contract" is a valid label on the sprk_documenttype Choice column, but the
        // fieldMapping declares type:"string" (the R7 UAT trap this task fixes).
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fieldMappings"":[
                {{""field"":""sprk_documenttype"",""type"":""string"",""value"":""Contract""}}
            ]
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        UseMetadataFor("sprk_document", BuildDocumentTypeChoiceMetadata());

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var data = result.GetData<UpdateRecordOutput>();
        data!.FieldsUpdated.Should().Contain("sprk_documenttype");
    }

    [Fact]
    public async Task ExecuteAsync_WithStringMappingToInvalidChoiceLabel_ReturnsDescriptiveFailureNotVerbatim()
    {
        // Arrange — "Bogus" is not a valid sprk_documenttype option label. Per FR-C1 this MUST
        // fail loud with the unmatched label + valid options, NOT pass the raw string through
        // (which is what produced the downstream Dataverse 500 in the R7 UAT incident).
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fieldMappings"":[
                {{""field"":""sprk_documenttype"",""type"":""string"",""value"":""Bogus""}}
            ]
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        UseMetadataFor("sprk_document", BuildDocumentTypeChoiceMetadata());

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("Bogus");
        result.ErrorMessage.Should().Contain("Contract");
        result.ErrorMessage.Should().Contain("Invoice");
        result.ErrorMessage.Should().Contain("Other");
        // Never a raw pass-through / raw-string field write and never an unhandled exception —
        // ExecuteAsync returns a controlled NodeOutput.Error before the Dataverse PATCH is issued.
        _dataverseServiceMock.Verify(
            s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("no", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public async Task ExecuteAsync_WithStringMappingToBooleanColumn_CoercesToBool(string rendered, bool expected)
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fieldMappings"":[
                {{""field"":""sprk_isconfidential"",""type"":""string"",""value"":""{rendered}""}}
            ]
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        UseMetadataFor("sprk_document", BuildDocumentTypeChoiceMetadata());

        Dictionary<string, object?>? capturedFields = null;
        _dataverseServiceMock
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken>((_, _, fields, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedFields.Should().NotBeNull();
        capturedFields!["sprk_isconfidential"].Should().Be(expected);
    }

    [Fact]
    public async Task ExecuteAsync_WithStringMappingToNumberColumn_CoercesToInt()
    {
        // Arrange
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fieldMappings"":[
                {{""field"":""sprk_partycount"",""type"":""string"",""value"":""42""}}
            ]
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        UseMetadataFor("sprk_document", BuildDocumentTypeChoiceMetadata());

        Dictionary<string, object?>? capturedFields = null;
        _dataverseServiceMock
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken>((_, _, fields, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedFields.Should().NotBeNull();
        capturedFields!["sprk_partycount"].Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_WithStringMappingToTextColumn_StillPassesThroughVerbatim()
    {
        // Regression — a type:"string" mapping targeting an actual text/Memo column must keep the
        // pre-existing verbatim pass-through behavior; metadata-driven coercion must NOT alter it.
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fieldMappings"":[
                {{""field"":""sprk_summary"",""type"":""string"",""value"":""Free-form summary text""}}
            ]
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        UseMetadataFor("sprk_document", BuildDocumentTypeChoiceMetadata());

        Dictionary<string, object?>? capturedFields = null;
        _dataverseServiceMock
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken>((_, _, fields, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedFields.Should().NotBeNull();
        capturedFields!["sprk_summary"].Should().Be("Free-form summary text");
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleStringMappingsToSameEntity_ResolvesColumnMetadataOnce()
    {
        // Arrange — two type:"string" mappings in the same run targeting the same entity. The
        // executor must resolve entity metadata ONCE (single cache read) and reuse it for both
        // fields, not issue a metadata lookup per field.
        var recordId = Guid.NewGuid();
        var config = $@"{{
            ""entityLogicalName"":""sprk_document"",
            ""recordId"":""{recordId}"",
            ""fieldMappings"":[
                {{""field"":""sprk_documenttype"",""type"":""string"",""value"":""Contract""}},
                {{""field"":""sprk_isconfidential"",""type"":""string"",""value"":""yes""}}
            ]
        }}";
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        var cacheMock = UseMetadataFor("sprk_document", BuildDocumentTypeChoiceMetadata());

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        cacheMock.Verify(
            c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private void SetupMockPassThrough()
    {
        // Mock both overloads - compiler chooses generic version for Dictionary<string, object?>
        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>()))
            .Returns((string template, IDictionary<string, object?> _) => template);

        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>()))
            .Returns((string template, Dictionary<string, object?> _) => template);
    }

    private static NodeExecutionContext CreateValidContext(string? configJson)
    {
        var nodeId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = nodeId,
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Update Record Node",
                ExecutionOrder = 1,
                OutputVariable = "updateResult",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Update Record"
            },
            ExecutorType = ExecutorType.UpdateRecord,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant"
        };
    }

    private class UpdateRecordOutput
    {
        public bool Updated { get; set; }
        public string EntityLogicalName { get; set; } = string.Empty;
        public Guid RecordId { get; set; }
        public string[] FieldsUpdated { get; set; } = [];
        public DateTimeOffset UpdatedAt { get; set; }
    }

    #endregion
}
