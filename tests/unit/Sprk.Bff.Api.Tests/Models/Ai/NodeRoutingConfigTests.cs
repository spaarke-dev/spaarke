using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Models.Ai;

/// <summary>
/// Unit tests for <see cref="NodeRoutingConfig"/> — the R6 Pillar 5 / FR-27 per-playbook
/// node routing contract. Verifies:
///
/// 1. Default destination is <see cref="NodeDestination.Chat"/> when the source blob
///    omits the routing properties (FR-27 backward compatibility);
/// 2. All four enum values (chat, workspace, form-prefill, side-effect) round-trip;
/// 3. Conditional-required rule: widgetType is REQUIRED when destination=workspace,
///    IGNORED otherwise;
/// 4. NFR-08 additive safety: parsing a blob containing the unrelated
///    DeliveryNodeConfig fields (deliveryType, template, outputFormat) does NOT throw
///    and correctly extracts only the routing subset;
/// 5. Malformed / null / empty blobs degrade to defaults rather than throwing.
/// </summary>
public class NodeRoutingConfigTests
{
    // -------------------------------------------------------------------------
    // Parse() — null / empty / malformed input handling
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_WithNullJson_ReturnsDefaultRoutingConfig()
    {
        // Arrange + Act
        var config = NodeRoutingConfig.Parse(null);

        // Assert
        config.Should().NotBeNull();
        config.Destination.Should().Be(NodeDestination.Chat,
            "null configJson must default to chat destination per FR-27 backward compatibility");
        config.WidgetType.Should().BeNull();
    }

    [Fact]
    public void Parse_WithEmptyString_ReturnsDefaultRoutingConfig()
    {
        // Arrange + Act
        var config = NodeRoutingConfig.Parse(string.Empty);

        // Assert
        config.Destination.Should().Be(NodeDestination.Chat);
        config.WidgetType.Should().BeNull();
    }

    [Fact]
    public void Parse_WithWhitespaceString_ReturnsDefaultRoutingConfig()
    {
        // Arrange + Act
        var config = NodeRoutingConfig.Parse("   \r\n\t  ");

        // Assert
        config.Destination.Should().Be(NodeDestination.Chat);
        config.WidgetType.Should().BeNull();
    }

    [Fact]
    public void Parse_WithMalformedJson_ReturnsDefaultRoutingConfig()
    {
        // Arrange — malformed JSON (missing closing brace)
        var malformed = "{ \"destination\": \"workspace\" ";

        // Act
        var config = NodeRoutingConfig.Parse(malformed);

        // Assert — degrades to default rather than throwing
        config.Destination.Should().Be(NodeDestination.Chat,
            "malformed JSON must degrade to default rather than throw — matches " +
            "DeliverOutputNodeExecutor.ParseConfigOrDefault swallow-and-default pattern");
    }

    // -------------------------------------------------------------------------
    // Parse() — empty object → defaults (FR-27 backward compatibility)
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_WithEmptyObject_DefaultsToChatDestination()
    {
        // Arrange — empty JSON object (pre-R6 node config with no routing fields)
        var configJson = "{}";

        // Act
        var config = NodeRoutingConfig.Parse(configJson);

        // Assert
        config.Destination.Should().Be(NodeDestination.Chat,
            "FR-27: nodes without destination property default to chat (current pre-R6 behavior)");
        config.WidgetType.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Parse() — all four destination enum values round-trip
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("chat", NodeDestination.Chat)]
    [InlineData("workspace", NodeDestination.Workspace)]
    [InlineData("form-prefill", NodeDestination.FormPrefill)]
    [InlineData("side-effect", NodeDestination.SideEffect)]
    public void Parse_WithDestinationValue_ReturnsCorrectEnum(string jsonValue, NodeDestination expected)
    {
        // Arrange — workspace destination also includes widgetType to satisfy the
        // conditional-required rule (we test parsing in isolation here; Validate has
        // its own tests below).
        var widgetTypeFragment = expected == NodeDestination.Workspace ? ", \"widgetType\": \"Summary\"" : string.Empty;
        var configJson = $"{{\"destination\": \"{jsonValue}\"{widgetTypeFragment}}}";

        // Act
        var config = NodeRoutingConfig.Parse(configJson);

        // Assert
        config.Destination.Should().Be(expected);
    }

    // -------------------------------------------------------------------------
    // Parse() — NFR-08 additive safety: blob with DeliveryNodeConfig fields
    //                                    does NOT throw, extracts only routing
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_WithDeliveryNodeConfigFields_ExtractsOnlyRoutingSubset()
    {
        // Arrange — a realistic sprk_configjson blob containing BOTH the existing
        // DeliveryNodeConfig fields (deliveryType, template, outputFormat,
        // R2 outputType/targetPage/prePopulateFields) AND the new R6 routing fields
        // (destination, widgetType). NFR-08 binding: NodeRoutingConfig must parse this
        // without choking on the unknown DeliveryNodeConfig fields.
        var configJson = """
        {
          "deliveryType": "json",
          "template": "## Summary\n{{summary.output}}",
          "outputFormat": { "includeMetadata": true, "maxLength": 5000 },
          "outputType": "dialog",
          "targetPage": "sprk_summarydialog",
          "prePopulateFields": { "name": "{{output.title}}" },
          "destination": "workspace",
          "widgetType": "Summary"
        }
        """;

        // Act
        var config = NodeRoutingConfig.Parse(configJson);

        // Assert — extracted routing subset correctly; unrelated fields silently ignored.
        config.Destination.Should().Be(NodeDestination.Workspace);
        config.WidgetType.Should().Be("Summary");
    }

    [Fact]
    public void Parse_WithCaseInsensitivePropertyNames_ResolvesCorrectly()
    {
        // Arrange — System.Text.Json with PropertyNameCaseInsensitive=true matches the
        // existing DeliveryNodeConfig parser convention (DeliverOutputNodeExecutor.cs:32).
        var configJson = """
        {
          "Destination": "side-effect",
          "WIDGETTYPE": "ignored-anyway-for-side-effect"
        }
        """;

        // Act
        var config = NodeRoutingConfig.Parse(configJson);

        // Assert
        config.Destination.Should().Be(NodeDestination.SideEffect);
        config.WidgetType.Should().Be("ignored-anyway-for-side-effect");
    }

    // -------------------------------------------------------------------------
    // Validate() — conditional-required rule for widgetType
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_WorkspaceDestinationWithWidgetType_ReturnsSuccess()
    {
        // Arrange
        var config = new NodeRoutingConfig
        {
            Destination = NodeDestination.Workspace,
            WidgetType = "Summary"
        };

        // Act
        var result = config.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WorkspaceDestinationWithoutWidgetType_ReturnsFailure()
    {
        // Arrange — destination=workspace but widgetType missing (conditional rule violation)
        var config = new NodeRoutingConfig
        {
            Destination = NodeDestination.Workspace,
            WidgetType = null
        };

        // Act
        var result = config.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("widgetType is required when destination = 'workspace'");
    }

    [Fact]
    public void Validate_WorkspaceDestinationWithEmptyWidgetType_ReturnsFailure()
    {
        // Arrange — destination=workspace, widgetType = whitespace-only
        var config = new NodeRoutingConfig
        {
            Destination = NodeDestination.Workspace,
            WidgetType = "   "
        };

        // Act
        var result = config.Validate();

        // Assert — whitespace-only is treated as missing
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
    }

    [Theory]
    [InlineData(NodeDestination.Chat)]
    [InlineData(NodeDestination.FormPrefill)]
    [InlineData(NodeDestination.SideEffect)]
    public void Validate_NonWorkspaceDestinationWithoutWidgetType_ReturnsSuccess(NodeDestination destination)
    {
        // Arrange — non-workspace destination: widgetType is OPTIONAL (in fact, ignored)
        var config = new NodeRoutingConfig
        {
            Destination = destination,
            WidgetType = null
        };

        // Act
        var result = config.Validate();

        // Assert — the conditional-required rule only applies to workspace destination
        result.IsValid.Should().BeTrue("widgetType is required ONLY when destination = workspace; " +
            "non-workspace destinations may omit widgetType");
    }

    [Theory]
    [InlineData(NodeDestination.Chat)]
    [InlineData(NodeDestination.FormPrefill)]
    [InlineData(NodeDestination.SideEffect)]
    public void Validate_NonWorkspaceDestinationWithWidgetType_ReturnsSuccess(NodeDestination destination)
    {
        // Arrange — non-workspace destination may have a widgetType value; it's just ignored
        // (not validated, not consumed). This documents the "widgetType is ignored otherwise"
        // half of the conditional rule.
        var config = new NodeRoutingConfig
        {
            Destination = destination,
            WidgetType = "Summary"  // valid-looking but ignored for non-workspace destinations
        };

        // Act
        var result = config.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Round-trip serialization (defensive — ensures JsonStringEnumConverter
    // produces the expected camelCase wire values for downstream consumers)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(NodeDestination.Chat, "chat")]
    [InlineData(NodeDestination.Workspace, "workspace")]
    [InlineData(NodeDestination.FormPrefill, "form-prefill")]
    [InlineData(NodeDestination.SideEffect, "side-effect")]
    public void Serialize_ProducesKebabCaseEnumValues(NodeDestination destination, string expectedWireValue)
    {
        // Arrange — bespoke NodeDestinationJsonConverter writes kebab-case to match the
        // JSON Schema (node-routing-config.schema.json) and the existing playbook-canvas
        // wire convention. .NET 8's built-in JsonStringEnumConverter would write
        // PascalCase; we use the bespoke converter for kebab-case parity.
        var widget = destination == NodeDestination.Workspace ? "Summary" : null;
        var config = new NodeRoutingConfig
        {
            Destination = destination,
            WidgetType = widget
        };

        // Act
        var json = JsonSerializer.Serialize(config);

        // Assert
        json.Should().Contain($"\"destination\":\"{expectedWireValue}\"",
            "wire value MUST match the JSON Schema enum value verbatim — kebab-case " +
            "convention for routing config destinations");
    }

    [Fact]
    public void Deserialize_WithUnknownDestinationValue_ThrowsJsonException()
    {
        // Arrange — unknown enum value
        var configJson = """{ "destination": "carrier-pigeon" }""";

        // Act — direct JsonSerializer.Deserialize (NOT Parse) surfaces the JsonException
        // so callers can distinguish "unknown value" from "default to chat".
        var act = () => JsonSerializer.Deserialize<NodeRoutingConfig>(configJson);

        // Assert
        act.Should().Throw<JsonException>()
            .WithMessage("*Unknown NodeDestination value*");
    }

    [Fact]
    public void Parse_WithUnknownDestinationValue_DegradesToDefault()
    {
        // Arrange — unknown enum value via the swallow-and-default Parse() entry point
        var configJson = """{ "destination": "carrier-pigeon" }""";

        // Act
        var config = NodeRoutingConfig.Parse(configJson);

        // Assert — Parse() catches the JsonException and degrades to default,
        // matching the DeliverOutputNodeExecutor.ParseConfigOrDefault pattern. This
        // means: a malformed downstream playbook (e.g. typo in destination value) will
        // route to chat rather than crash the playbook execution.
        config.Destination.Should().Be(NodeDestination.Chat);
    }

    [Fact]
    public void RoundTrip_WorkspaceDestination_PreservesValues()
    {
        // Arrange
        var original = new NodeRoutingConfig
        {
            Destination = NodeDestination.Workspace,
            WidgetType = "DocumentViewer"
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var parsed = NodeRoutingConfig.Parse(json);

        // Assert
        parsed.Destination.Should().Be(original.Destination);
        parsed.WidgetType.Should().Be(original.WidgetType);
        parsed.Validate().IsValid.Should().BeTrue();
    }
}
