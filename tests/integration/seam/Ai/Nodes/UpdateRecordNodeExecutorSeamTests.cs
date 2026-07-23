using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai.Nodes;

/// <summary>
/// CHARACTERIZATION seam tests for <see cref="UpdateRecordNodeExecutor"/> (task 030, FR-07 pre-req).
/// </summary>
/// <remarks>
/// <para>
/// Pins the CURRENT shipped coercion behavior BEFORE the Layer-A extraction (task 031). Uses a REAL
/// <see cref="TemplateEngine"/>; the Dataverse-boundary <see cref="IFieldMappingDataverseService"/> is a
/// Moq double whose captured payload is asserted, and the metadata-driven Choice path uses a REAL
/// <see cref="MetadataService"/> backed by a mocked <see cref="IDistributedCache"/> cache-HIT (the proven
/// convention from <c>UpdateRecordNodeExecutorTests</c>) so no Dataverse round-trip occurs.
/// </para>
/// <para>
/// The slice pinned here is the exact coerced <c>Dictionary&lt;string,object?&gt;</c> handed to
/// <c>UpdateRecordFieldsAsync</c> — the observable contract 031 must not change: typed-mapping coercion
/// (Choice options-map / Boolean / Number), legacy <c>HeuristicParse</c> precedence, and the FAIL-LOUD
/// contract for a metadata-confirmed Choice column with an unmatchable label.
/// </para>
/// </remarks>
public class UpdateRecordNodeExecutorSeamTests
{
    private readonly Mock<IFieldMappingDataverseService> _fieldMappingMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly UpdateRecordNodeExecutor _executor;

    private static readonly JsonSerializerOptions MetadataCacheJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UpdateRecordNodeExecutorSeamTests()
    {
        _fieldMappingMock = new Mock<IFieldMappingDataverseService>();
        _fieldMappingMock
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Strict: CreateScope() is only expected when a test opts in via UseMetadataFor(...). A missing
        // setup fails loudly rather than silently no-op'ing (mirrors the unit-test convention).
        _scopeFactoryMock = new Mock<IServiceScopeFactory>(MockBehavior.Strict);

        _executor = new UpdateRecordNodeExecutor(
            new TemplateEngine(NullLogger<TemplateEngine>.Instance),
            _fieldMappingMock.Object,
            _scopeFactoryMock.Object,
            NullLogger<UpdateRecordNodeExecutor>.Instance);
    }

    // ── Typed field-mapping coercion (criterion 8) ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithTypedFieldMappings_CoercesEachToItsDeclaredType()
    {
        // Arrange — one mapping of each type. The String mapping triggers metadata resolution
        // (sprk_summary is Memo ⇒ stays verbatim). Choice uses its own options map; Boolean/Number
        // coerce directly.
        var recordId = Guid.NewGuid();
        var config = $$"""
        {
          "entityLogicalName": "sprk_document",
          "recordId": "{{recordId}}",
          "fieldMappings": [
            { "field": "sprk_summary", "type": "string", "value": "Free-form summary" },
            { "field": "sprk_status", "type": "choice", "value": "Complete",
              "options": { "pending": 100000000, "complete": 100000002 } },
            { "field": "sprk_isconfidential", "type": "boolean", "value": "yes" },
            { "field": "sprk_partycount", "type": "number", "value": "42" }
          ]
        }
        """;
        var context = CreateContext(config);
        UseMetadataFor("sprk_document", BuildDocumentMetadata());
        var captured = CaptureUpdatePayload();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert — exact coerced payload
        result.Success.Should().BeTrue();
        captured().Should().NotBeNull();
        var payload = captured()!;
        payload["sprk_summary"].Should().Be("Free-form summary"); // Memo ⇒ verbatim
        payload["sprk_status"].Should().Be(100000002);            // Choice label → int (options map)
        payload["sprk_isconfidential"].Should().Be(true);         // Boolean truthy
        payload["sprk_partycount"].Should().Be(42);               // Number int-first
    }

    // ── Legacy flat fields — HeuristicParse precedence (criterion 9) ──────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithLegacyFlatFields_PinsHeuristicParsePrecedence()
    {
        // Arrange — no fieldMappings ⇒ legacy path ⇒ no metadata resolution (scope factory untouched).
        var recordId = Guid.NewGuid();
        var config = $$"""
        {
          "entityLogicalName": "sprk_document",
          "recordId": "{{recordId}}",
          "fields": {
            "sprk_int": "42",
            "sprk_decimal": "3.14",
            "sprk_bool": "true",
            "sprk_string": "hello"
          }
        }
        """;
        var context = CreateContext(config);
        var captured = CaptureUpdatePayload();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert — int → decimal → bool → string precedence pinned exactly
        result.Success.Should().BeTrue();
        var payload = captured()!;
        payload["sprk_int"].Should().Be(42);       // int wins
        payload["sprk_decimal"].Should().Be(3.14m); // decimal (int failed)
        payload["sprk_bool"].Should().Be(true);     // bool (int+decimal failed)
        payload["sprk_string"].Should().Be("hello"); // string fallback
    }

    // ── Negative case — FAIL LOUD on unmatchable Choice label (criterion 10) ──────────────────

    [Fact]
    public async Task ExecuteAsync_WithStringMappingToInvalidChoiceLabel_FailsLoudAndNeverPatches()
    {
        // Arrange — type:"string" mapping whose target is a metadata-confirmed Choice column, with a
        // value matching no option. Per FR-C1 this MUST fail loud (never a silent pass-through, never a
        // downstream Dataverse 500).
        var recordId = Guid.NewGuid();
        var config = $$"""
        {
          "entityLogicalName": "sprk_document",
          "recordId": "{{recordId}}",
          "fieldMappings": [
            { "field": "sprk_documenttype", "type": "string", "value": "Bogus" }
          ]
        }
        """;
        var context = CreateContext(config);
        UseMetadataFor("sprk_document", BuildDocumentMetadata());

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert — controlled NodeOutput.Error BEFORE any PATCH
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("Bogus");
        result.ErrorMessage.Should().Contain("Contract");
        result.ErrorMessage.Should().Contain("Invoice");
        result.ErrorMessage.Should().Contain("Other");
        _fieldMappingMock.Verify(
            s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private Func<Dictionary<string, object?>?> CaptureUpdatePayload()
    {
        Dictionary<string, object?>? captured = null;
        _fieldMappingMock
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken>((_, _, fields, _) => captured = fields)
            .Returns(Task.CompletedTask);
        return () => captured;
    }

    /// <summary>
    /// Wires a real <see cref="MetadataService"/> (backed by a mocked <see cref="IDistributedCache"/>
    /// cache-HIT) through the executor's Singleton+Scoped bridge, mirroring the unit-test convention.
    /// </summary>
    private void UseMetadataFor(string entityLogicalName, EntityMetadataDto dto)
    {
        var cacheKey = $"sdap:dv:entitymetadata:{entityLogicalName.ToLowerInvariant()}";
        var cacheMock = new Mock<IDistributedCache>();
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto, MetadataCacheJsonOptions));
        cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        var metadataService = new MetadataService(
            Mock.Of<IDataverseService>(),
            cacheMock.Object,
            NullLogger<MetadataService>.Instance);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(MetadataService)))
            .Returns(metadataService);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
    }

    private static EntityMetadataDto BuildDocumentMetadata(string entity = "sprk_document") =>
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
                    LogicalName: "sprk_summary",
                    AttributeType: "Memo",
                    Format: null,
                    IsPrimaryName: false,
                    IsPrimaryId: false,
                    OptionSet: null)
            });

    private static NodeExecutionContext CreateContext(string? configJson)
    {
        var actionId = Guid.NewGuid();
        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Update Record Node",
                ExecutionOrder = 1,
                OutputVariable = "updateResult",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction { Id = actionId, Name = "Update Record" },
            ExecutorType = ExecutorType.UpdateRecord,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant"
        };
    }
}
