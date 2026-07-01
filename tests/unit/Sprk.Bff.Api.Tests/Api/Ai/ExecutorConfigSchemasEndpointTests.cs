// R7 spaarke-ai-platform-unification-r7 — Wave 3 task 034 / FR-16 + NFR-05
//
// xUnit + integration tests for `GET /api/ai/playbook-builder/executor-config-schemas`
// (task 033 endpoint). Locks the wire contract Wave 8 task 083 typed config form
// renderer depends on:
//
//   - HTTP 200 + envelope shape `{ "schemas": ExecutorConfigSchema[] }` for authenticated requests
//   - HTTP 401 for unauthenticated requests (ADR-008 RequireAuthorization on the group)
//   - All 25 registered executors return a schema entry (AnalysisServicesModule registers
//     23 executors + InsightsIngestModule registers 2 = 25 total). The ExecutorType enum
//     has 33 values, but 8 (AiEmbedding, RuleEngine, Calculation, DataTransform, CallWebhook,
//     SendTeamsMessage, Parallel, Wait) have no concrete executor — they're forward-compat
//     enum placeholders, not registered, so they're not in the response.
//   - 5 priority executors (AiAnalysis, AiCompletion, Condition, EntityNameValidator,
//     CreateNotification per spec FR-16) return RICH schemas with non-empty `Fields`
//     and specific named fields the UI consumes.
//   - The other 20 registered executors return placeholder schemas with empty `Fields`
//     and a non-empty `Description` (the canvas's "no configuration required" hint).
//   - Each schema entry round-trips through System.Text.Json with the camelCase property
//     names declared on `ExecutorConfigSchema` / `ConfigSchemaField` / `SchemaFieldType`.
//
// Per ADR-038 KEEP-category: integration test via `WebApplicationFactory<Program>` —
// real executors registered through real DI; ONLY heavy outbound deps (Dataverse, Graph,
// OpenAI) are mocked at the CustomWebAppFactory boundary. No `Mock<HttpMessageHandler>`,
// no DI-registration assertions, no ctor null-check tests, no method-presence reflection.
// Each test exercises observable behavior through the real route + real serializer.
//
// References: spec.md FR-16 + NFR-05 (test coverage); ADR-001 Minimal API; ADR-008 endpoint
// filters; ADR-010 DI Minimalism; ADR-013 BFF AI Architecture; ADR-038 Testing Strategy
// (integration-heavy pyramid); `.claude/constraints/bff-extensions.md` (BFF Hygiene §10).

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Tests for <c>GET /api/ai/playbook-builder/executor-config-schemas</c> (R7 Wave 3 task 033 / FR-16).
/// Uses the canonical <see cref="CustomWebAppFactory"/> fixture so the real
/// <c>INodeExecutorRegistry</c> + real concrete executors participate. Only outbound deps
/// (Dataverse / Graph / OpenAI) are mocked at the factory boundary per ADR-038.
/// </summary>
public class ExecutorConfigSchemasEndpointTests : IClassFixture<CustomWebAppFactory>
{
    private const string EndpointUrl = "/api/ai/playbook-builder/executor-config-schemas";

    /// <summary>
    /// Camel-case JSON options matching what the BFF emits (Minimal API default —
    /// <see cref="ExecutorConfigSchema"/> + <see cref="ConfigSchemaField"/> declare
    /// <c>JsonPropertyName</c> attributes; <see cref="SchemaFieldType"/> uses
    /// <c>JsonStringEnumConverter</c>). Tests deserialize against these options to
    /// validate the wire format the Playbook Builder canvas consumes.
    /// </summary>
    private static readonly JsonSerializerOptions BffJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly CustomWebAppFactory _factory;

    public ExecutorConfigSchemasEndpointTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUTH (ADR-008)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetExecutorConfigSchemas_WhenUnauthenticated_Returns401()
    {
        // Arrange — no Authorization header. The endpoint group has RequireAuthorization()
        // per AiPlaybookBuilderEndpoints.MapGroup; FakeAuthHandler fails when the header
        // is absent.
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync(EndpointUrl);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "endpoint group requires authorization (ADR-008); unauthenticated requests must be rejected");
    }

    [Fact]
    public async Task GetExecutorConfigSchemas_WhenAuthenticated_Returns200WithSchemasEnvelope()
    {
        // Arrange
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync(EndpointUrl);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object,
            "response is the ExecutorConfigSchemasResponse envelope, not a bare array");
        doc.RootElement.TryGetProperty("schemas", out var schemasEl).Should().BeTrue(
            "envelope MUST carry a 'schemas' property per design doc §6 wire contract");
        schemasEl.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGISTRY COVERAGE — all 25 registered executors return a schema entry
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetExecutorConfigSchemas_WhenAuthenticated_ReturnsAllRegisteredExecutors()
    {
        // Arrange — 23 executors in AnalysisServicesModule.AddNodeExecutors +
        // 2 executors in InsightsIngestModule (SanitizerNodeExecutor, ObservationEmitterNodeExecutor)
        // = 25 concrete INodeExecutor registrations per task 032 commit message.
        const int ExpectedExecutorCount = 25;
        var client = CreateAuthenticatedClient();

        // Act
        var envelope = await GetSchemasEnvelopeAsync(client);

        // Assert
        envelope.Schemas.Should().HaveCount(ExpectedExecutorCount,
            "registry MUST contain every concrete INodeExecutor registration " +
            "(23 in AnalysisServicesModule.AddNodeExecutors + 2 in InsightsIngestModule)");
    }

    [Fact]
    public async Task GetExecutorConfigSchemas_OrdersSchemasByExecutorTypeValueAscending()
    {
        // Arrange — design doc §6: deterministic ordering by ExecutorTypeValue ascending,
        // so the canvas can group + diff schemas predictably across deployments.
        var client = CreateAuthenticatedClient();

        // Act
        var envelope = await GetSchemasEnvelopeAsync(client);

        // Assert
        var values = envelope.Schemas.Select(s => s.ExecutorTypeValue).ToArray();
        values.Should().BeInAscendingOrder(
            "endpoint orders schemas by ExecutorTypeValue ascending per design doc §6");
        values.Should().OnlyHaveUniqueItems(
            "every ExecutorType is registered at most once (no duplicate executor instances)");
    }

    [Fact]
    public async Task GetExecutorConfigSchemas_EverySchema_HasNonEmptyExecutorTypeNameAndDescription()
    {
        // Arrange — the canvas reads ExecutorTypeName for the form title and Description for
        // the no-configuration-required hint (placeholder schemas) or section help text (rich
        // schemas). Both MUST be non-empty for every entry — defends the canvas against UX
        // regressions where an executor ships with a placeholder schema but no description.
        var client = CreateAuthenticatedClient();

        // Act
        var envelope = await GetSchemasEnvelopeAsync(client);

        // Assert
        envelope.Schemas.Should().AllSatisfy(s =>
        {
            s.ExecutorTypeName.Should().NotBeNullOrWhiteSpace(
                "every schema MUST carry the ExecutorType name for the canvas form title");
            s.Description.Should().NotBeNullOrWhiteSpace(
                "every schema MUST carry a description (placeholder = 'no configuration required' hint; " +
                "rich = section help text)");
        });
    }

    [Fact]
    public async Task GetExecutorConfigSchemas_EverySchema_ExecutorTypeNameMatchesEnumName()
    {
        // Arrange — schemas use nameof(ExecutorType.X) for the name field; the integer
        // value must match the enum value. Sanity check that the registry isn't emitting
        // mis-keyed pairs (e.g., name='Condition' with value=42 would be a serious bug).
        var client = CreateAuthenticatedClient();

        // Act
        var envelope = await GetSchemasEnvelopeAsync(client);

        // Assert
        envelope.Schemas.Should().AllSatisfy(s =>
        {
            Enum.IsDefined(typeof(ExecutorType), s.ExecutorTypeValue).Should().BeTrue(
                $"ExecutorTypeValue={s.ExecutorTypeValue} MUST correspond to a defined ExecutorType enum value");

            var expectedName = ((ExecutorType)s.ExecutorTypeValue).ToString();
            s.ExecutorTypeName.Should().Be(expectedName,
                $"ExecutorTypeName must match the enum name for ExecutorTypeValue={s.ExecutorTypeValue}");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIORITY EXECUTORS (5) — rich schemas with named fields
    // Per spec FR-16: AiAnalysis + AiCompletion + Condition + EntityNameValidator + CreateNotification
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetExecutorConfigSchemas_AiCompletionSchema_IsRichWithExpectedFields()
    {
        // Arrange — AiCompletion (ExecutorType=1) per task 032 declares 2 fields:
        // templateParameters + promptSchemaOverride (both optional, FR-12 prompt-only contract).
        var client = CreateAuthenticatedClient();

        // Act
        var envelope = await GetSchemasEnvelopeAsync(client);

        // Assert
        var schema = envelope.Schemas.Single(s => s.ExecutorTypeValue == (int)ExecutorType.AiCompletion);
        schema.Fields.Should().NotBeEmpty(
            "AiCompletion is a priority executor per FR-16; Fields MUST be populated");
        var fieldNames = schema.Fields.Select(f => f.Name).ToArray();
        fieldNames.Should().Contain("templateParameters",
            "FR-12 prompt-only executor reads {{var}} substitutions from ConfigJson.templateParameters");
        fieldNames.Should().Contain("promptSchemaOverride",
            "FR-25 per-node override merges into the Action's base JPS prompt schema");
    }

    [Fact]
    public async Task GetExecutorConfigSchemas_AiAnalysisSchema_IsRichWithExpectedFields()
    {
        // Arrange — AiAnalysis (ExecutorType=0) per task 032 declares 6 fields:
        // templateParameters, promptSchemaOverride, knowledgeRetrieval, includeDocumentContext,
        // parentEntityType, parentEntityId.
        var client = CreateAuthenticatedClient();

        // Act
        var envelope = await GetSchemasEnvelopeAsync(client);

        // Assert
        var schema = envelope.Schemas.Single(s => s.ExecutorTypeValue == (int)ExecutorType.AiAnalysis);
        schema.Fields.Should().NotBeEmpty(
            "AiAnalysis is a priority executor per FR-16; Fields MUST be populated");
        schema.Fields.Select(f => f.Name).Should().Contain(new[]
        {
            "templateParameters",
            "promptSchemaOverride"
        }, "AiAnalysis surfaces JPS substitution + per-node overrides on the canvas");
    }

    [Fact]
    public async Task GetExecutorConfigSchemas_ConditionSchema_IsRichWithRequiredConditionField()
    {
        // Arrange — Condition (ExecutorType=30) per task 032 declares 3 fields:
        // condition (REQUIRED), trueBranch (optional), falseBranch (optional).
        // Validate() requires condition + at least one branch.
        var client = CreateAuthenticatedClient();

        // Act
        var envelope = await GetSchemasEnvelopeAsync(client);

        // Assert
        var schema = envelope.Schemas.Single(s => s.ExecutorTypeValue == (int)ExecutorType.Condition);
        schema.Fields.Should().NotBeEmpty(
            "Condition is a priority executor per FR-16; Fields MUST be populated");

        var conditionField = schema.Fields.SingleOrDefault(f => f.Name == "condition");
        conditionField.Should().NotBeNull(
            "Condition node's primary config field is 'condition' — drives the canvas expression builder");
        conditionField!.Required.Should().BeTrue(
            "schema-side Required MUST match Validate() — condition is required to evaluate the branch");

        schema.Fields.Select(f => f.Name).Should().Contain(new[] { "trueBranch", "falseBranch" },
            "Condition surfaces both branch targets on the canvas");
    }

    [Fact]
    public async Task GetExecutorConfigSchemas_EntityNameValidatorSchema_IsRichWithRequiredFields()
    {
        // Arrange — EntityNameValidator (ExecutorType=141) per task 032 declares 2 fields:
        // candidateText (required) + allowList (required).
        var client = CreateAuthenticatedClient();

        // Act
        var envelope = await GetSchemasEnvelopeAsync(client);

        // Assert
        var schema = envelope.Schemas.Single(
            s => s.ExecutorTypeValue == (int)ExecutorType.EntityNameValidator);
        schema.Fields.Should().NotBeEmpty(
            "EntityNameValidator is a priority executor per FR-16; Fields MUST be populated");
        schema.Fields.Should().AllSatisfy(f => f.Required.Should().BeTrue(
            "both candidateText and allowList are required for hallucination scrubbing to operate"));
    }

    [Fact]
    public async Task GetExecutorConfigSchemas_CreateNotificationSchema_IsRichWithTitleAndBodyRequired()
    {
        // Arrange — CreateNotification (ExecutorType=50) per task 032 declares 20 fields;
        // title + body are required; recipient/category/priority/toastType/actionUrl +
        // R2.2 dueDate + 8 FR-6 enrichment fields are optional.
        var client = CreateAuthenticatedClient();

        // Act
        var envelope = await GetSchemasEnvelopeAsync(client);

        // Assert
        var schema = envelope.Schemas.Single(
            s => s.ExecutorTypeValue == (int)ExecutorType.CreateNotification);
        schema.Fields.Should().NotBeEmpty(
            "CreateNotification is a priority executor per FR-16; Fields MUST be populated");
        schema.Fields.Should().HaveCountGreaterThanOrEqualTo(2,
            "CreateNotification surfaces multiple maker-editable fields on the canvas");

        var titleField = schema.Fields.SingleOrDefault(f => f.Name == "title");
        titleField.Should().NotBeNull("title is the notification headline — canvas-editable");
        titleField!.Required.Should().BeTrue(
            "schema-side Required MUST match Validate() — title is required to render a notification");

        var bodyField = schema.Fields.SingleOrDefault(f => f.Name == "body");
        bodyField.Should().NotBeNull("body is the notification message — canvas-editable");
        bodyField!.Required.Should().BeTrue(
            "schema-side Required MUST match Validate() — body is required to render a notification");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PLACEHOLDER EXECUTORS — empty Fields + non-empty Description
    // Spot-checks 3 of the 20 placeholders (Start, DeliverOutput, AgentService)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetExecutorConfigSchemas_PlaceholderExecutors_HaveEmptyFieldsAndNonEmptyDescription()
    {
        // Arrange — placeholder executors per task 032 use ExecutorConfigSchema.Empty(type, description)
        // which sets Fields = Array.Empty<ConfigSchemaField>(). The empty array IS the contract
        // — distinguishes "placeholder" from "we forgot to define a schema" per design doc §4.
        // Spot-check 3 representative placeholders covering different node-type categories:
        //   - Start (33)         — canvas anchor, pass-through
        //   - DeliverOutput (40) — terminal delivery, template-driven (no maker fields surfaced yet)
        //   - AgentService (60)  — Azure AI Foundry agent dispatch
        var client = CreateAuthenticatedClient();

        // Act
        var envelope = await GetSchemasEnvelopeAsync(client);

        // Assert
        var placeholderTypes = new[]
        {
            ExecutorType.Start,
            ExecutorType.DeliverOutput,
            ExecutorType.AgentService
        };
        foreach (var type in placeholderTypes)
        {
            var schema = envelope.Schemas.SingleOrDefault(s => s.ExecutorTypeValue == (int)type);
            schema.Should().NotBeNull($"{type} executor MUST appear in the schema registry");
            schema!.Fields.Should().BeEmpty(
                $"{type} is a placeholder executor — Fields[] is the 'no maker-editable config' contract per design doc §4");
            schema.Description.Should().NotBeNullOrWhiteSpace(
                $"{type} placeholder MUST still carry a description (the canvas's empty-state hint)");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JSON ROUND-TRIP — wire contract validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetExecutorConfigSchemas_ResponseRoundTrips_ThroughCamelCaseSerializer()
    {
        // Arrange — the Playbook Builder canvas (TypeScript) reads `executorTypeName`,
        // `executorTypeValue`, `description`, `fields`, `name`, `type`, `required`. The
        // BFF declares these via [JsonPropertyName] on ExecutorConfigSchema /
        // ConfigSchemaField. This test deserializes the raw body using camelCase options
        // and confirms every top-level property binds — a regression here would silently
        // break the canvas without producing an obvious server-side error.
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync(EndpointUrl);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — round-trip through System.Text.Json with camelCase property naming.
        var envelope = JsonSerializer.Deserialize<ExecutorConfigSchemasResponse>(body, BffJsonOptions);
        envelope.Should().NotBeNull("envelope MUST deserialize from camelCase JSON");
        envelope!.Schemas.Should().NotBeNullOrEmpty();

        // Pick the AiCompletion schema (priority, known field set) and confirm every
        // ConfigSchemaField property bound — Name + Type + Required + Description.
        var aiCompletion = envelope.Schemas.Single(s => s.ExecutorTypeValue == (int)ExecutorType.AiCompletion);
        aiCompletion.ExecutorTypeName.Should().Be(nameof(ExecutorType.AiCompletion));
        aiCompletion.Fields.Should().AllSatisfy(f =>
        {
            f.Name.Should().NotBeNullOrWhiteSpace("each field MUST have a Name (canvas form input key)");
            // f.Type is non-nullable enum — default(SchemaFieldType) = String = 0, also valid.
            // Just confirm it's a defined enum value.
            Enum.IsDefined(typeof(SchemaFieldType), f.Type).Should().BeTrue(
                "each field's Type MUST be a defined SchemaFieldType enum value");
            f.Description.Should().NotBeNullOrWhiteSpace("each field MUST have a Description (canvas tooltip)");
        });
    }

    [Fact]
    public async Task GetExecutorConfigSchemas_SchemaFieldType_SerializesAsStringName()
    {
        // Arrange — SchemaFieldType is decorated with [JsonConverter(typeof(JsonStringEnumConverter))]
        // per design doc §7 (TypeScript canvas reads strings, not integers, for the field type
        // discriminator). The raw body MUST contain string names like "Object" / "String" —
        // not integer values like 3 / 0 — for the AiCompletion schema's templateParameters field.
        var client = CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync(EndpointUrl);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — the raw JSON contains a string-encoded SchemaFieldType for AiCompletion's
        // templateParameters field (which is SchemaFieldType.Object = 3).
        body.Should().Contain("\"type\":\"Object\"",
            "SchemaFieldType MUST serialize as the enum NAME (e.g., 'Object') not the integer value (3) " +
            "per JsonStringEnumConverter convention so the TypeScript canvas reads typed strings");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an HttpClient with the FakeAuth bearer token configured —
    /// FakeAuthHandler in CustomWebAppFactory accepts any non-empty bearer.
    /// </summary>
    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    /// <summary>
    /// Issues the GET and deserializes the envelope. Asserts 200 OK + content-type
    /// to fail fast on transport-level surprises that would otherwise mask schema bugs.
    /// </summary>
    private static async Task<ExecutorConfigSchemasResponse> GetSchemasEnvelopeAsync(HttpClient client)
    {
        var response = await client.GetAsync(EndpointUrl);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "endpoint MUST return 200 OK for authenticated requests");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<ExecutorConfigSchemasResponse>(body, BffJsonOptions);
        envelope.Should().NotBeNull("envelope MUST deserialize from response body");
        return envelope!;
    }
}
