using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Per-playbook routing destination for a playbook node's output (R6 Q5 re-shape, FR-27).
/// </summary>
/// <remarks>
/// <para>
/// This enum is the Q5 RE-SHAPED counterpart to action-level <c>outputSchema</c>
/// (which describes the INTRINSIC shape an action produces). The destination is set
/// PER NODE in a playbook canvas — the same action (e.g. <c>SUM-CHAT@v1</c>) can be
/// routed to different destinations in different playbooks without changing the action.
/// </para>
/// <para>
/// Lives inside <c>sprk_playbooknode.sprk_configjson</c> as the <c>destination</c>
/// property. Consumed by <see cref="NodeRoutingConfig"/> parse / by downstream
/// CapabilityRouter dedup (task 042 / FR-30 — "one user intent → one route → one
/// playbook → one DeliverOutput → one render") and by the schema-aware
/// <c>StructuredOutputStreamWidget</c> (tasks 040/041).
/// </para>
/// <para>
/// Serialized as <b>kebab-case</b> strings in <c>sprk_configjson</c> to match the JSON
/// Schema (<c>node-routing-config.schema.json</c>) — values are <c>"chat"</c>,
/// <c>"workspace"</c>, <c>"form-prefill"</c>, <c>"side-effect"</c>. Wire format
/// established by <see cref="NodeDestinationJsonConverter"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(NodeDestinationJsonConverter))]
public enum NodeDestination
{
    /// <summary>
    /// Default destination — output renders inline in the chat conversation surface.
    /// This is the backward-compatible default for nodes without an explicit destination,
    /// matching pre-R6 behavior where chat-driven playbooks emit content into the chat
    /// pane (e.g. <c>summarize-document-for-chat@v1</c>).
    /// </summary>
    Chat,

    /// <summary>
    /// Output streams into a workspace tab via <c>StructuredOutputStreamWidget</c>.
    /// Requires <see cref="NodeRoutingConfig.WidgetType"/> to be set (conditionally required).
    /// Example: <c>summarize-document-for-workspace@v1</c> playbook routes to a Summary widget.
    /// </summary>
    Workspace,

    /// <summary>
    /// Output pre-fills a form on the host record context (matter, project, etc.) using
    /// the existing pre-fill flow (<c>MatterPreFillService</c>, <c>ProjectPreFillService</c>,
    /// <c>useAiPrefill</c> hook). NFR-07 binding: signatures + 45s timeout + behavior of
    /// the pre-fill flow are UNCHANGED; this destination merely tags the routing intent
    /// for CapabilityRouter dedup (task 042).
    /// </summary>
    FormPrefill,

    /// <summary>
    /// Output is consumed for a side-effect only (e.g. enqueue indexing, write to
    /// Dataverse, send notification, fire event) with no user-visible chat or workspace
    /// rendering. Used for playbooks whose primary product is a Dataverse mutation or
    /// system action rather than a conversational reply.
    /// </summary>
    SideEffect,

    /// <summary>
    /// Dual destination — output is routed BOTH to the chat conversation surface AND to
    /// a workspace tab via <c>StructuredOutputStreamWidget</c>. Requires
    /// <see cref="NodeRoutingConfig.WidgetType"/> to be set when routing the workspace
    /// half. Added in chat-routing-redesign-r1 (FR-14a / WP3) to support playbooks whose
    /// output must appear in both surfaces (e.g. an answer rendered inline in chat while
    /// a structured artifact is also streamed into a workspace widget). Inserted at the
    /// end of the enum list to preserve the numeric ordinals of the four pre-existing
    /// values bit-for-bit (FR-14a).
    /// </summary>
    Both
}

/// <summary>
/// Per-node routing configuration stored inside <c>sprk_playbooknode.sprk_configjson</c>
/// (R6 Pillar 5 / FR-27, the Q5-re-shaped per-playbook routing surface).
/// </summary>
/// <remarks>
/// <para>
/// <b>Storage surface</b>: this record describes the <c>destination</c> + <c>widgetType</c>
/// properties added to the existing <c>sprk_configjson</c> JSON blob on
/// <c>sprk_playbooknode</c> rows. No new Dataverse column is added. The blob is opaque to
/// <see cref="Services.Ai.NodeService"/> (which PATCHes it as a string) and to the
/// <c>DeliverOutputNodeExecutor</c> (which deserializes only its known fields and
/// silently ignores unknown JSON properties — System.Text.Json default behavior).
/// </para>
/// <para>
/// <b>NFR-08 binding</b>: the 11 production node executors are NOT modified. This record
/// is a SEPARATE contract consumed by:
/// <list type="bullet">
///   <item>CapabilityRouter dedup (task 042 / FR-30) — reads <see cref="Destination"/> to enforce one-route-per-intent</item>
///   <item><c>StructuredOutputStreamWidget</c> (tasks 040/041) — reads <see cref="WidgetType"/> when rendering workspace destinations</item>
///   <item>Action migration tasks 032-035 — populate this contract for the 4 migrated actions</item>
/// </list>
/// The <c>DeliverOutputNodeExecutor</c>'s existing <c>DeliveryNodeConfig</c> record stays
/// unchanged — it deserializes its own known fields (<c>deliveryType</c>, <c>template</c>,
/// <c>outputFormat</c>, R2 <c>outputType</c>/<c>targetPage</c>/<c>prePopulateFields</c>) and
/// silently ignores the new routing fields. Confirmed additive-safe at
/// <c>DeliverOutputNodeExecutor.cs:271-286</c> (ParseConfigOrDefault uses
/// <c>JsonSerializerOptions { PropertyNameCaseInsensitive = true }</c> — no
/// <c>UnmappedMemberHandling.Disallow</c>, default <c>Skip</c> applies).
/// </para>
/// <para>
/// <b>Backward compatibility (FR-27 + project CLAUDE.md §Constraints)</b>: nodes WITHOUT
/// these fields default to <see cref="NodeDestination.Chat"/> (current pre-R6 behavior).
/// No migration of existing playbook node JSON is required; pre-existing nodes work
/// unchanged.
/// </para>
/// <para>
/// <b>Conditional-required rule for <see cref="WidgetType"/></b>: REQUIRED when
/// <see cref="Destination"/> = <see cref="NodeDestination.Workspace"/>; IGNORED for all
/// other destinations. The rule is enforced at parse time by
/// <see cref="Validate"/> (not at JSON deserialization, since
/// <c>System.Text.Json</c> conditional-required is non-trivial to express declaratively).
/// </para>
/// </remarks>
/// <example>
/// Example <c>sprk_configjson</c> blob for a node routing to a workspace Summary widget:
/// <code>
/// {
///   "deliveryType": "json",
///   "template": "{{summary.output}}",
///   "destination": "workspace",
///   "widgetType": "Summary"
/// }
/// </code>
/// Example blob for a chat-bound node (no new fields — defaults to chat):
/// <code>
/// {
///   "deliveryType": "markdown",
///   "template": "## Summary\n{{summary.output.tldr}}"
/// }
/// </code>
/// </example>
public sealed record NodeRoutingConfig
{
    /// <summary>
    /// Per-playbook routing destination. Optional in the source JSON; defaults to
    /// <see cref="NodeDestination.Chat"/> when absent (FR-27 backward compatibility).
    /// </summary>
    [JsonPropertyName("destination")]
    public NodeDestination Destination { get; init; } = NodeDestination.Chat;

    /// <summary>
    /// Workspace widget type — required when <see cref="Destination"/> =
    /// <see cref="NodeDestination.Workspace"/>; ignored otherwise. String to allow
    /// extension by downstream widget tasks (Summary, DocumentViewer, Table, Dashboard,
    /// etc.) without recompiling this contract.
    /// </summary>
    /// <remarks>
    /// Validation rule enforced by <see cref="Validate"/>. The string is matched against
    /// the <c>WorkspaceWidgetRegistry</c> at render time (tasks 040/041). Unknown widget
    /// types degrade gracefully (the widget registry falls back to a default).
    /// </remarks>
    [JsonPropertyName("widgetType")]
    public string? WidgetType { get; init; }

    /// <summary>
    /// JSON deserialization options matching the existing
    /// <c>DeliverOutputNodeExecutor.JsonOptions</c> surface (case-insensitive property
    /// matching). Ensures the same parse semantics across the playbook node-config
    /// readers in the BFF.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parses a node-config JSON blob (from <c>sprk_playbooknode.sprk_configjson</c>)
    /// and extracts the routing config. Returns a default <see cref="NodeRoutingConfig"/>
    /// (destination = <see cref="NodeDestination.Chat"/>) when the blob is null, empty,
    /// unparseable, or missing the routing properties (FR-27 backward compatibility).
    /// </summary>
    /// <param name="configJson">
    /// The raw <c>sprk_configjson</c> string. May be null, empty, or contain arbitrary
    /// node-specific properties — only <c>destination</c> + <c>widgetType</c> are read.
    /// </param>
    /// <returns>
    /// A <see cref="NodeRoutingConfig"/>. Never null. Default values when the blob lacks
    /// the routing fields. Unknown JSON properties in the blob (e.g.
    /// <c>deliveryType</c>, <c>template</c>) are silently ignored — they belong to other
    /// readers (e.g. <c>DeliveryNodeConfig</c> in <c>DeliverOutputNodeExecutor</c>).
    /// </returns>
    public static NodeRoutingConfig Parse(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return new NodeRoutingConfig();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<NodeRoutingConfig>(configJson, JsonOptions);
            return parsed ?? new NodeRoutingConfig();
        }
        catch (JsonException)
        {
            // Malformed JSON — treat as no routing config (default to chat).
            // The DeliverOutputNodeExecutor follows the same swallow-and-default
            // pattern (see ParseConfigOrDefault). Logging belongs to the caller
            // (CapabilityRouter / widget registry consumer).
            return new NodeRoutingConfig();
        }
    }

    /// <summary>
    /// Validates the conditional-required rule: <see cref="WidgetType"/> is REQUIRED
    /// when <see cref="Destination"/> = <see cref="NodeDestination.Workspace"/>;
    /// IGNORED otherwise.
    /// </summary>
    /// <returns>
    /// A <see cref="NodeValidationResult"/> — <see cref="NodeValidationResult.Success"/>
    /// when the rule is satisfied, otherwise <see cref="NodeValidationResult.Failure"/>
    /// with a single descriptive error message.
    /// </returns>
    /// <remarks>
    /// Callers (CapabilityRouter, widget renderer) invoke this BEFORE attempting to
    /// resolve the widget — invalid configs are surfaced as the playbook author's
    /// error, not a render-time crash. This is a separate validation pass from the
    /// existing <c>DeliveryNodeConfig</c> <c>IsValidDeliveryType</c> check in
    /// <c>DeliverOutputNodeExecutor</c> (the executor's check stays untouched per
    /// NFR-08).
    /// </remarks>
    public NodeValidationResult Validate()
    {
        if (Destination == NodeDestination.Workspace && string.IsNullOrWhiteSpace(WidgetType))
        {
            return NodeValidationResult.Failure(
                "widgetType is required when destination = 'workspace' " +
                "(per-playbook node config; R6 Pillar 5 FR-27).");
        }

        return NodeValidationResult.Success();
    }
}

/// <summary>
/// JSON converter that maps <see cref="NodeDestination"/> to/from kebab-case wire
/// values (<c>chat</c>, <c>workspace</c>, <c>form-prefill</c>, <c>side-effect</c>) so the
/// C# enum names align with the JSON Schema (<c>node-routing-config.schema.json</c>) and
/// the existing playbook-canvas wire convention for enum-typed properties.
/// </summary>
/// <remarks>
/// .NET 8's built-in <see cref="JsonStringEnumConverter"/> writes PascalCase by default
/// and lacks kebab-case naming policy support (added in .NET 9). This bespoke converter
/// provides a small, allocation-light mapping. Read is case-insensitive on the wire
/// value to match the project-wide <c>PropertyNameCaseInsensitive = true</c> convention.
/// Unknown values throw <see cref="JsonException"/> — callers that need degrade-to-default
/// behavior should use <see cref="NodeRoutingConfig.Parse"/> (which swallows
/// <see cref="JsonException"/>).
/// </remarks>
public sealed class NodeDestinationJsonConverter : JsonConverter<NodeDestination>
{
    public override NodeDestination Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value?.ToLowerInvariant() switch
        {
            "chat" => NodeDestination.Chat,
            "workspace" => NodeDestination.Workspace,
            "form-prefill" => NodeDestination.FormPrefill,
            "side-effect" => NodeDestination.SideEffect,
            "both" => NodeDestination.Both,
            _ => throw new JsonException(
                $"Unknown NodeDestination value: '{value}'. Expected one of: " +
                "chat, workspace, form-prefill, side-effect, both.")
        };
    }

    public override void Write(Utf8JsonWriter writer, NodeDestination value, JsonSerializerOptions options)
    {
        var wire = value switch
        {
            NodeDestination.Chat => "chat",
            NodeDestination.Workspace => "workspace",
            NodeDestination.FormPrefill => "form-prefill",
            NodeDestination.SideEffect => "side-effect",
            NodeDestination.Both => "both",
            _ => throw new JsonException($"Unknown NodeDestination enum value: {value}")
        };
        writer.WriteStringValue(wire);
    }
}
