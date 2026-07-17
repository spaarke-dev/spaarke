using System.Text.Json;
using System.Text.Json.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Smart pre-seed enrichment for <see cref="BindingDisposition.SurfaceLaunch"/> dispatches (spec FR-B2,
/// task 013 part 3 / D-013-01). A create capability drafts its closed-set fields as human LABELS
/// (ADR-039 — the LLM never emits the final closed-set value); this enricher turns those labels into
/// record ids via the deterministic <see cref="IConstrainedFieldResolver"/> (task 010) and merges the
/// result into the launch payload as a <c>resolvedLookups</c> object the launched wizard pre-selects
/// dropdowns from (client mapper <c>handoffSeedMapping.ts</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it runs</b>: <see cref="Chat.SessionDispatchOrchestrator"/> calls
/// <see cref="EnrichAsync"/> on the drafted <see cref="JsonElement"/> output BEFORE the ledger write
/// (<c>IOutputRouter.RouteAsync</c>) — the router stores an opaque payload it never parses (ADR-040
/// store-before-render), so the enriched <c>resolvedLookups</c> slot rides the existing SSE/ledger
/// surface with no second dispatch protocol (ADR-039).
/// </para>
/// <para>
/// <b>No model call anywhere</b>: the only work is metadata resolution via the deterministic resolver.
/// Any resolver failure or an unmapped consumer-type is a quiet no-op (ADR-032) — enrichment NEVER
/// fails the dispatch; the payload is returned unchanged and the wizard falls back to user-picked
/// dropdowns.
/// </para>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — no component resolves drafted
/// create-field labels into the launch envelope; the resolver (010) is a primitive with no consumer
/// until this lands. (2) <i>Extension</i> — this is not a new pipeline; it is a single pre-<c>RouteAsync</c>
/// transform on the shipped dispatch spine, injected optionally so the router/orchestrator contract is
/// unchanged. (3) <i>Cost-of-doing-nothing</i>: the launched Create-Matter wizard opens with the
/// practice-area / matter-type dropdowns blank even when the draft names them — the FR-B2 "defaulted
/// dropdowns" acceptance fails concretely.
/// </para>
/// </remarks>
public interface ISurfaceLaunchEnricher
{
    /// <summary>
    /// Resolve the drafted closed-set labels named by the R1 field-map for
    /// <paramref name="binding"/>'s consumer-type and merge a <c>resolvedLookups</c> object into
    /// <paramref name="output"/>. Returns <paramref name="output"/> unchanged when the binding is not a
    /// surface launch, the consumer-type has no field-map, the payload is not a JSON object, or nothing
    /// resolved (ADR-032 quiet no-op).
    /// </summary>
    Task<JsonElement> EnrichAsync(Binding binding, JsonElement output, CancellationToken cancellationToken = default);
}

/// <summary>
/// The shipped <see cref="ISurfaceLaunchEnricher"/>. Holds the R1 create-field map (keyed by
/// <c>sprk_consumertype</c>) and delegates every label→id resolution to the deterministic
/// <see cref="IConstrainedFieldResolver"/>. Sealed; no orchestrator-authored interface beyond the one
/// seam above (ADR-010 minimalism — the interface exists only so the orchestrator can take it as an
/// OPTIONAL dependency that hand-built test constructions omit).
/// </summary>
public sealed class SurfaceLaunchEnricher : ISurfaceLaunchEnricher
{
    private readonly IConstrainedFieldResolver _resolver;
    private readonly ILogger<SurfaceLaunchEnricher> _logger;

    /// <summary>
    /// One drafted closed-set field to resolve: the LLM draft key carrying the LABEL, the owning entity +
    /// attribute the resolver matches against, and the <c>resolvedLookups</c> key the client wizard mapper
    /// reads (its preferred alias — see <c>handoffSeedMapping.ts</c>).
    /// </summary>
    private sealed record LookupFieldMap(string DraftKey, string Entity, string Attribute, string LookupKey);

    /// <summary>
    /// R1 field-map. Only <c>create-matter</c> has resolvable closed-set fields in R1 (create-task /
    /// create-todo draft only free-text + an enum the surface owns). <c>practice_area_suggestion</c> /
    /// <c>matter_type_suggestion</c> are the CREATE-MATTER@v1 draft keys (notes/surface-launch-mechanism.md);
    /// <c>sprk_practicearea_ref</c> / <c>sprk_mattertype_ref</c> are the <c>sprk_matter</c> lookup attributes
    /// AND the client mapper's preferred <c>resolvedLookups</c> alias keys.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<LookupFieldMap>> FieldMaps =
        new Dictionary<string, IReadOnlyList<LookupFieldMap>>(StringComparer.OrdinalIgnoreCase)
        {
            ["create-matter"] = new LookupFieldMap[]
            {
                new("practice_area_suggestion", "sprk_matter", "sprk_practicearea_ref", "sprk_practicearea_ref"),
                new("matter_type_suggestion", "sprk_matter", "sprk_mattertype_ref", "sprk_mattertype_ref"),
            },
        };

    public SurfaceLaunchEnricher(IConstrainedFieldResolver resolver, ILogger<SurfaceLaunchEnricher> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<JsonElement> EnrichAsync(Binding binding, JsonElement output, CancellationToken cancellationToken = default)
    {
        // Only surface_launch dispositions carry a client-owned launch payload to enrich.
        if (binding.Disposition != BindingDisposition.SurfaceLaunch)
        {
            return output;
        }

        // Unmapped consumer-type (or a draft that is not a JSON object) — nothing to resolve. Quiet no-op.
        if (binding.ConsumerType is null || !FieldMaps.TryGetValue(binding.ConsumerType, out var maps)
            || output.ValueKind != JsonValueKind.Object)
        {
            return output;
        }

        var resolvedLookups = new JsonObject();
        foreach (var map in maps)
        {
            var label = ReadStringProperty(output, map.DraftKey);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;   // the draft did not propose this field — leave the picker to the user.
            }

            ConstrainedFieldResolution resolution;
            try
            {
                resolution = await _resolver
                    .ResolveAsync(map.Entity, map.Attribute, label, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // ADR-032 quiet no-op: enrichment must NEVER fail the dispatch. Identifiers only (NFR-07) —
                // never the proposed label (free text).
                _logger.LogWarning(ex,
                    "SurfaceLaunchEnricher: resolve failed for {Entity}.{Attribute} (binding {BindingId}); leaving the field unresolved.",
                    map.Entity, map.Attribute, binding.BindingId);
                continue;
            }

            resolvedLookups[map.LookupKey] = BuildLookupNode(resolution);
        }

        if (resolvedLookups.Count == 0)
        {
            return output;   // nothing resolved — return the payload untouched.
        }

        // Merge the resolvedLookups object into a mutable copy of the payload, then re-materialize a
        // DETACHED JsonElement (parity with the orchestrator's SerializeWorkflowResult clone discipline —
        // the returned element owns no caller JsonDocument lifetime).
        var root = JsonNode.Parse(output.GetRawText())!.AsObject();
        root["resolvedLookups"] = resolvedLookups;
        return JsonSerializer.SerializeToElement(root);
    }

    /// <summary>Read a string property from a JSON object, or null when absent / not a string.</summary>
    private static string? ReadStringProperty(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Build the client <c>ResolvedLookup</c> node: <c>confidence</c> (always), <c>recordId</c> (when the
    /// resolver resolved a top candidate — High/Low), and <c>candidates</c> (when supplied for picker
    /// rendering). Shape matches <c>surfaceHandoff/types.ts ResolvedLookup</c> exactly.
    /// </summary>
    private static JsonObject BuildLookupNode(ConstrainedFieldResolution resolution)
    {
        var node = new JsonObject
        {
            ["confidence"] = resolution.Confidence switch
            {
                ResolutionConfidence.High => "high",
                ResolutionConfidence.Low => "low",
                _ => "none",
            },
        };

        if (resolution.Resolved is not null)
        {
            node["recordId"] = resolution.Resolved;
        }

        if (resolution.Candidates.Count > 0)
        {
            var candidates = new JsonArray();
            foreach (var candidate in resolution.Candidates)
            {
                candidates.Add(new JsonObject
                {
                    ["recordId"] = candidate.Value,
                    ["label"] = candidate.Label,
                });
            }

            node["candidates"] = candidates;
        }

        return node;
    }
}
