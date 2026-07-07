// FR-P3-08 (spaarke-ai-architecture-redesign-r1 task 047) — the work_product disposition
// leg's persistence seam. OutputRouter routes `work_product`-disposition outputs (ADR-040:
// disposition is the ONLY rendering contract) through this narrow persister, which
// generalizes the widgets-r1 topic-registry pattern (sprk_aitopicregistry +
// record-persisted envelope) from "one playbook node per topic" to platform-level,
// Binding-declared persistence — no per-capability persistence code.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Persistence seam for the <c>work_product</c> output disposition (ADR-040 / FR-P3-08):
/// <see cref="OutputRouter"/> stores the ledger entry FIRST, then hands the STORED
/// <see cref="SessionOutput"/> here to be written onto the session's host Dataverse
/// record — the envelope OUTLIVES the session (a matter summary belongs on the matter,
/// not just in chat history).
/// </summary>
/// <remarks>
/// <para>
/// <b>Interface rationale (ADR-010 tension, documented — CLAUDE.md §11)</b>:
/// (1) Existing overlap: the shipped record-persisted-outputs slot is the widgets-r1
/// topic-registry pattern (<c>sprk_aitopicregistry</c> + envelope written to
/// <c>sprk_targetfield</c> by a per-playbook <c>UpdateRecord</c> node); this type adds no
/// second slot — it generalizes the SAME registry + envelope contract to every Binding.
/// (2) Extension test: the write must run user-OBO through <see cref="IDataverseUserClient"/>;
/// injecting that client directly into <see cref="OutputRouter"/> would put registry
/// resolution + OData mechanics inside the router and make the leg unmockable at the
/// module boundary (ADR-038) — same seam shape as <see cref="IEmailDispositionSender"/>
/// (task 043, the disposition-leg precedent). (3) Cost of doing nothing: the ADR-040
/// work_product disposition stays a loud <c>NotSupportedException</c> stub and FR-P3-08
/// acceptance ("a work_product capability persists its envelope to the host record")
/// cannot ship.
/// </para>
/// <para>
/// <b>Declaration contract (ADR-039 — Binding is the single routing surface)</b>: WHETHER
/// an output persists is declared on the Binding row alone (<c>sprk_disposition =
/// work_product</c>). WHERE it persists is the capability's <c>sprk_aitopicregistry</c>
/// row — the registry row whose <c>sprk_topicname</c> equals the Binding's capability code
/// (<see cref="Binding.ConsumerCode"/> when meaningful, else
/// <see cref="Binding.ConsumerType"/>) supplies <c>sprk_hostentity</c> +
/// <c>sprk_targetfield</c>, exactly the role it already plays for the shipped insights
/// topics (matter-health → sprk_matter.sprk_performancesummary). The registry carries
/// target-mapping DATA, not a routing decision — no appsettings key, no code list, no new
/// manifest table (spec out-of-scope rule: reuse sprk_aitopicregistry + existing columns).
/// </para>
/// <para>
/// <b>User-OBO (spec MUST)</b>: every call this type makes — entity-set metadata reads,
/// the registry read, and the host-record PATCH — goes through
/// <see cref="IDataverseUserClient"/> under the calling user's exchanged token
/// (fail-closed; no app-only fallback). The user's own security context decides what can
/// be persisted where: a host record the user cannot update surfaces the user's own 404/403.
/// </para>
/// <para>
/// <b>Idempotency (per <c>{bindingId}@t{n}</c>)</b>: the write is a single-field PATCH of
/// the registry-declared target field with <c>If-Match: *</c> (update-only — see
/// <see cref="IDataverseUserClient.PatchAsync"/>; a missing/invisible host record can
/// never be upsert-created). Re-routing the same stored entry overwrites the field with a
/// byte-identical envelope (the envelope embeds the ledger key), and a NEWER turn's
/// envelope replaces an older one — the field always holds the LATEST work product, the
/// same last-write-wins semantics the widgets-r1 node ships. No rows are ever created, so
/// repeated routing cannot duplicate.
/// </para>
/// <para>
/// <b>Confirmation gating (FR-P2-02)</b>: this type performs NO gating. Side-effect
/// gating happens at INVOCATION time — loop tool calls gate by declared
/// <c>side_effect_class</c>/Binding <c>sprk_risk</c> through the ONE pending store, and a
/// gated invocation only reaches the OutputRouter AFTER the gate resolves. By the time an
/// output is being routed, the invocation that produced it was already approved (or was
/// never gate-classed); re-gating a routing leg would create a second gate surface.
/// </para>
/// <para>
/// <b>NFR-07</b>: logs carry identifiers + payload SIZE only (ledger key, topic, entity,
/// record id, target field) — never envelope content.
/// </para>
/// </remarks>
public interface IWorkProductRecordPersister
{
    /// <summary>
    /// Persists one stored work_product ledger entry to the session's host Dataverse
    /// record. Throws on every failure mode (no registry row, host-entity mismatch,
    /// invalid host record id, Dataverse failure) — the caller treats persistence as part
    /// of routing (loud, never a silent skip; the ledger entry remains addressable).
    /// </summary>
    /// <param name="entry">The STORED ledger entry (ADR-040: storage precedes persistence).</param>
    /// <param name="binding">The Binding that declared <c>work_product</c> — supplies the capability code the registry row is keyed on.</param>
    /// <param name="hostContext">The session's host record context (entity type + record id) — the persistence target.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Receipt naming where the envelope landed (identifiers only).</returns>
    Task<WorkProductPersistenceReceipt> PersistAsync(
        SessionOutput entry,
        Binding binding,
        ChatHostContext hostContext,
        CancellationToken cancellationToken = default);
}

/// <summary>Where a work_product envelope was persisted (identifiers only — NFR-07-safe to log).</summary>
public sealed record WorkProductPersistenceReceipt
{
    /// <summary>Host entity logical name the envelope was written to (from the registry row).</summary>
    public required string EntityLogicalName { get; init; }

    /// <summary>Host record id (from the session host context).</summary>
    public required Guid RecordId { get; init; }

    /// <summary>Registry-declared target column that now holds the envelope.</summary>
    public required string TargetField { get; init; }

    /// <summary>Ledger key of the persisted entry (<c>{bindingId}@t{n}</c>).</summary>
    public required string LedgerKey { get; init; }
}

/// <summary>
/// Default <see cref="IWorkProductRecordPersister"/>: resolves the capability's
/// <c>sprk_aitopicregistry</c> target mapping and PATCHes the envelope (derived verbatim
/// from the stored <see cref="SessionOutput"/> — see <see cref="WorkProductEnvelope"/>)
/// onto the host record under user-OBO. See the interface remarks for the full contract.
/// </summary>
public sealed partial class TopicRegistryWorkProductPersister : IWorkProductRecordPersister
{
    /// <summary>Logical name of the shipped record-persisted-outputs registry (widgets-r1 FR-04).</summary>
    internal const string RegistryLogicalName = "sprk_aitopicregistry";

    /// <summary>Envelope schema version — pinned by <c>infra/dataverse/outputschemas/work-product-envelope-v1.schema.json</c>.</summary>
    internal const string EnvelopeSchemaVersion = "1.0";

    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex LogicalNameRegex();

    private static readonly JsonSerializerOptions EnvelopeSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IDataverseUserClient _dataverse;
    private readonly ILogger<TopicRegistryWorkProductPersister> _logger;

    public TopicRegistryWorkProductPersister(
        IDataverseUserClient dataverse,
        ILogger<TopicRegistryWorkProductPersister> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<WorkProductPersistenceReceipt> PersistAsync(
        SessionOutput entry,
        Binding binding,
        ChatHostContext hostContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(hostContext);

        var topic = ResolveTopicName(binding);

        if (!Guid.TryParse(hostContext.EntityId, out var recordId) || recordId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Work-product persistence for output '{entry.Key}' (topic '{topic}'): the session host context " +
                $"carries an invalid record id ('{hostContext.EntityId}'). The host record IS the persistence " +
                "target — fix the surface that created the session's HostContext.");
        }

        // ── 1. Resolve the registry target mapping (topic → host entity + target field).
        var mapping = await ResolveTargetMappingAsync(topic, entry.Key, hostContext, cancellationToken)
            .ConfigureAwait(false);

        // ── 2. Derive the envelope VERBATIM from the stored ledger entry (ADR-040: the
        // ledger SessionOutput is the source the persisted envelope derives from).
        var envelopeJson = JsonSerializer.Serialize(
            WorkProductEnvelope.FromStoredEntry(entry),
            EnvelopeSerializerOptions);

        // ── 3. PATCH the single registry-declared column under the USER's token.
        // Entity-set resolution under the same token (write-handler pattern): a host table
        // invisible to the user 404s here, BEFORE any write is attempted.
        var hostEntitySet = await GetEntitySetNameAsync(mapping.HostEntity, entry.Key, cancellationToken)
            .ConfigureAwait(false);

        string patchBody;
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString(mapping.TargetField, envelopeJson);
                writer.WriteEndObject();
            }
            patchBody = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        // If-Match: * (update-only) — see IDataverseUserClient.PatchAsync. Single-field
        // overwrite: idempotent per entry, last-write-wins across turns (class remarks).
        var response = await _dataverse.PatchAsync(
            $"{hostEntitySet}({recordId:D})",
            patchBody,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            // The user's OWN access error surfaces (user-OBO — never escalates); the ledger
            // entry stays addressable (storage already happened in OutputRouter).
            throw new InvalidOperationException(
                $"Work-product persistence for output '{entry.Key}' failed: PATCH " +
                $"{mapping.HostEntity}({recordId:D}).{mapping.TargetField} returned {response.StatusCode} " +
                $"({response.ErrorCode}): {response.ErrorMessage}. The entry WAS stored to the session ledger " +
                "(ADR-040); only the record persistence failed.");
        }

        // NFR-07: identifiers + size only.
        _logger.LogInformation(
            "Work-product output persisted: key={Key} topic={Topic} entity={Entity} recordId={RecordId} " +
            "targetField={TargetField} envelopeBytes={EnvelopeBytes}",
            entry.Key, topic, mapping.HostEntity, recordId, mapping.TargetField, envelopeJson.Length);

        return new WorkProductPersistenceReceipt
        {
            EntityLogicalName = mapping.HostEntity,
            RecordId = recordId,
            TargetField = mapping.TargetField,
            LedgerKey = entry.Key,
        };
    }

    /// <summary>
    /// The registry join key: the Binding's capability code —
    /// <see cref="Binding.ConsumerCode"/> when it is a meaningful discriminator, else
    /// <see cref="Binding.ConsumerType"/> (resolution treats null/empty code as
    /// <c>"default"</c>, which is not a topic). Mirrors how the shipped insights rows key
    /// the registry by canonical capability name.
    /// </summary>
    internal static string ResolveTopicName(Binding binding) =>
        !string.IsNullOrWhiteSpace(binding.ConsumerCode)
        && !string.Equals(binding.ConsumerCode, "default", StringComparison.OrdinalIgnoreCase)
            ? binding.ConsumerCode!
            : binding.ConsumerType;

    /// <summary>
    /// Reads the enabled, active <c>sprk_aitopicregistry</c> row for <paramref name="topic"/>
    /// and validates its declared host entity against the session host context. Loud on
    /// every miss — a Binding that declares work_product WITHOUT a resolvable target
    /// mapping is a catalog authoring error, never a silent skip.
    /// </summary>
    private async Task<(string HostEntity, string TargetField)> ResolveTargetMappingAsync(
        string topic,
        string ledgerKey,
        ChatHostContext hostContext,
        CancellationToken cancellationToken)
    {
        var registrySet = await GetEntitySetNameAsync(RegistryLogicalName, ledgerKey, cancellationToken)
            .ConfigureAwait(false);

        // OData string literals escape single quotes by doubling them.
        var escapedTopic = topic.Replace("'", "''", StringComparison.Ordinal);
        var response = await _dataverse.GetAsync(
            $"{registrySet}?$select=sprk_topicname,sprk_mode,sprk_hostentity,sprk_targetfield" +
            $"&$filter=sprk_topicname eq '{escapedTopic}' and sprk_enabled eq true and statecode eq 0",
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Body is null)
        {
            throw new InvalidOperationException(
                $"Work-product persistence for output '{ledgerKey}': the topic-registry read for topic " +
                $"'{topic}' failed ({response.StatusCode} {response.ErrorCode}): {response.ErrorMessage}.");
        }

        var rows = response.Body.Value.TryGetProperty("value", out var value)
                   && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToList()
            : new List<JsonElement>();
        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                $"Work-product persistence for output '{ledgerKey}': no enabled sprk_aitopicregistry row " +
                $"declares topic '{topic}'. The Binding declares disposition work_product, so a registry row " +
                "(sprk_topicname = the Binding's capability code, sprk_hostentity + sprk_targetfield = the " +
                "persistence target) MUST exist — author it in the topic-registry model-driven app (widgets-r1 " +
                "FR-09 SME form); no code change is required.");
        }

        // The session's host context selects the row when a topic targets multiple host
        // entities (one registry row per topic+mode; distinct modes may target different
        // entities). Both sides normalize through the same vocabulary the host context uses.
        foreach (var row in rows)
        {
            var hostEntity = GetString(row, "sprk_hostentity");
            var targetField = GetString(row, "sprk_targetfield");
            if (hostEntity is null || targetField is null)
            {
                continue;
            }

            var normalizedRegistryEntity = EntityTypeNormalizer.Normalize(hostEntity);
            if (!string.Equals(normalizedRegistryEntity, hostContext.EntityType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Defense-in-depth: the registry is maker-editable NVARCHAR data. Reject values
            // that are not plain Dataverse logical names before they reach an OData path or
            // a PATCH body (same posture as the dataverse.* write handlers).
            if (!LogicalNameRegex().IsMatch(hostEntity) || !LogicalNameRegex().IsMatch(targetField))
            {
                throw new InvalidOperationException(
                    $"Work-product persistence for output '{ledgerKey}': registry row for topic '{topic}' " +
                    $"carries a malformed sprk_hostentity/sprk_targetfield ('{hostEntity}'/'{targetField}') — " +
                    "logical names must match ^[a-z][a-z0-9_]*$. Fix the registry row.");
            }

            return (hostEntity, targetField);
        }

        throw new InvalidOperationException(
            $"Work-product persistence for output '{ledgerKey}': topic '{topic}' is registered, but no " +
            $"registry row targets the session's host entity '{hostContext.EntityType}' " +
            $"(registered host entities: {string.Join(", ", rows.Select(r => GetString(r, "sprk_hostentity") ?? "?"))}). " +
            "Either the capability was invoked outside its declared host context, or the registry row's " +
            "sprk_hostentity needs correcting.");
    }

    /// <summary>
    /// Entity-set resolution under the user's token (read-handler pattern) — a table the
    /// user cannot see fails here with the user's own error, before any write.
    /// </summary>
    private async Task<string> GetEntitySetNameAsync(
        string logicalName,
        string ledgerKey,
        CancellationToken cancellationToken)
    {
        var response = await _dataverse.GetAsync(
            $"EntityDefinitions(LogicalName='{logicalName}')?$select=EntitySetName",
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Body is null)
        {
            throw new InvalidOperationException(
                $"Work-product persistence for output '{ledgerKey}': entity-set resolution for " +
                $"'{logicalName}' failed ({response.StatusCode} {response.ErrorCode}): {response.ErrorMessage}.");
        }

        return GetString(response.Body.Value, "EntitySetName")
            ?? throw new InvalidOperationException(
                $"Work-product persistence for output '{ledgerKey}': table '{logicalName}' has no entity-set name.");
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

/// <summary>
/// The persisted work-product envelope, derived VERBATIM from a stored ledger
/// <see cref="SessionOutput"/> (ADR-040: the ledger is the source; the record copy is a
/// projection). Serialized as a JSON string into the registry-declared longtext column —
/// the generalized successor of the widgets-r1 FR-14 envelope (<c>schemaVersion</c> +
/// <c>generatedAt</c> kept; topic-specific members replaced by the ledger identity +
/// schema-validated payload). Contract pinned at
/// <c>infra/dataverse/outputschemas/work-product-envelope-v1.schema.json</c> (NFR-06).
/// </summary>
public sealed record WorkProductEnvelope
{
    /// <summary>Envelope schema version (literal string, widgets-r1 convention — never an int).</summary>
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    /// <summary>Addressable ledger key (<c>{bindingId}@t{n}</c>) — ties the record copy back to its session ledger source.</summary>
    [JsonPropertyName("ledgerKey")]
    public required string LedgerKey { get; init; }

    /// <summary>Binding (<c>sprk_playbookconsumer</c>) id that produced the output.</summary>
    [JsonPropertyName("bindingId")]
    public required string BindingId { get; init; }

    /// <summary>Stable use-case vocabulary id (canonical §3).</summary>
    [JsonPropertyName("ucId")]
    public required string UcId { get; init; }

    /// <summary>Session turn ordinal the output was produced on.</summary>
    [JsonPropertyName("turn")]
    public required int Turn { get; init; }

    /// <summary>Ledger disposition vocabulary value (<c>work_product</c> on this leg).</summary>
    [JsonPropertyName("disposition")]
    public required string Disposition { get; init; }

    /// <summary>UTC timestamp the output was written to the ledger (envelope generation time).</summary>
    [JsonPropertyName("generatedAt")]
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Citations / document ids / ledger keys the output was grounded on (identifiers only). Omitted when none.</summary>
    [JsonPropertyName("sourceRefs")]
    public IReadOnlyList<string>? SourceRefs { get; init; }

    /// <summary>The schema-validated capability output — the stored ledger payload, verbatim.</summary>
    [JsonPropertyName("payload")]
    public required JsonElement Payload { get; init; }

    /// <summary>Derives the envelope from a stored ledger entry — field-for-field, no reshaping.</summary>
    public static WorkProductEnvelope FromStoredEntry(SessionOutput entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new WorkProductEnvelope
        {
            SchemaVersion = TopicRegistryWorkProductPersister.EnvelopeSchemaVersion,
            LedgerKey = entry.Key,
            BindingId = entry.BindingId,
            UcId = entry.UcId,
            Turn = entry.Turn,
            Disposition = entry.Disposition,
            GeneratedAt = entry.CreatedAt,
            SourceRefs = entry.SourceRefs is { Count: > 0 } ? entry.SourceRefs : null,
            Payload = entry.Payload,
        };
    }
}
