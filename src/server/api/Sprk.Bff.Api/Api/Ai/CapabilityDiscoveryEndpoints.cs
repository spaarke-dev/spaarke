using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Capability-discovery READ endpoint (spec FR-A1-12, R1 — the gate-038 deferral
/// landing per <c>spaarke-ai-architecture-redesign-r2</c> task 041).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists</b>: soft-slash launchers had no authoritative capability
/// list, so a client could only render launchable capabilities by asking the
/// model to name them — risking invented/hallucinated capability names. This
/// endpoint projects the SAME closed catalog the agent-turn loop already uses
/// for text-path tool projection (<see cref="IConsumerRoutingService.ListTextProjectableBindingsAsync"/>,
/// FR-P2-01) into a client-safe shape, so a launcher menu can render
/// deterministically from catalog data instead of model output.
/// </para>
/// <para>
/// <b>ADR-039 closed catalog</b>: this endpoint enumerates catalog-declared
/// Bindings only (rows with a non-empty maker-authored <c>sprk_tooldescription</c>
/// — the same explicit catalog opt-in the loop's tool projection uses). It never
/// invents, infers, or supplements the list — there is exactly one query path
/// (<see cref="IConsumerRoutingService"/>), reused rather than duplicated
/// (CLAUDE.md §11 extension-over-new).
/// </para>
/// <para>
/// <b>ADR-013 facade boundary</b>: consumes <see cref="IConsumerRoutingService"/>
/// from <c>Services/Ai/PublicContracts/</c> — no AI-internal orchestration,
/// gate, or directive type is referenced. The response DTO is a purpose-built
/// projection (stable id + display label + surfaces + launch-args shape) that
/// deliberately does NOT leak catalog internals not needed by a launcher
/// (match-conditions JSON, workflow class, chip transitions, risk/capture-mode,
/// event memberships, model tier) — those stay internal to the routing/dispatch
/// seams that already consume the full <see cref="Binding"/> contract.
/// </para>
/// <para>
/// <b>Determinism</b>: ordering is inherited verbatim from
/// <see cref="IConsumerRoutingService.ListTextProjectableBindingsAsync"/>
/// (consumer type ordinal, then binding id — NFR-04 prompt-cache-stable
/// ordering); this endpoint does not re-sort. The projection carries no
/// per-request timestamps or generated correlation ids — <see cref="Binding.BindingId"/>
/// is a stable catalog identifier, not a per-request value.
/// </para>
/// <para>
/// <b>"Capabilities the caller may launch"</b> (auth constraint): filtered by
/// the Binding's declared <c>sprk_surfaces</c> placement — a Binding with no
/// declared surfaces is offered on ALL surfaces (per the column dictionary);
/// a Binding that DOES declare surfaces must include the requested one. The
/// default requested surface is <c>assistant</c> (the SpaarkeAi soft-slash
/// launcher's placement). No maker-facing graph/workflow authoring or runtime
/// risk logic is introduced — risk/side-effect gating stays entirely with the
/// existing Confirmation Gate (task 032); this endpoint is read-only metadata.
/// </para>
/// </remarks>
public static class CapabilityDiscoveryEndpoints
{
    private const string DefaultSurface = "assistant";

    public static IEndpointRouteBuilder MapCapabilityDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        // -----------------------------------------------------------------------
        // GET /api/ai/capabilities
        // Deterministic launchable-capability projection for soft-slash launchers
        // (FR-A1-12). Read-only; sourced from the closed catalog (ADR-039).
        // -----------------------------------------------------------------------
        var group = app.MapGroup("/api/ai/capabilities")
            .RequireAuthorization()
            .WithTags("AI Capability Discovery");

        group.MapGet("/", GetCapabilities)
            .WithName("GetCapabilities")
            .WithSummary("List launchable capabilities for deterministic soft-slash launchers")
            .WithDescription(
                "Returns the launchable-capability list (stable binding id, display label, " +
                "placement surfaces, launch-args shape) projected deterministically from the " +
                "closed catalog (ADR-039). Filtered to capabilities offered on the requested " +
                "surface (default 'assistant'). Used by SpaarkeAi soft-slash launchers to " +
                "render capabilities WITHOUT the model guessing what exists.")
            .Produces<CapabilityDiscoveryResponse>()
            .ProducesProblem(401)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// GET /api/ai/capabilities?surface={surface} — projects the closed catalog's
    /// text-projectable Bindings (FR-P2-01 query, reused) into the launcher-facing
    /// shape, filtered to the requested placement surface.
    /// </summary>
    private static async Task<IResult> GetCapabilities(
        IConsumerRoutingService routingService,
        ILoggerFactory loggerFactory,
        string? surface,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("CapabilityDiscoveryEndpoints");
        var requestedSurface = string.IsNullOrWhiteSpace(surface)
            ? DefaultSurface
            : surface.Trim().ToLowerInvariant();

        try
        {
            var bindings = await routingService
                .ListTextProjectableBindingsAsync(environment: null, cancellationToken)
                .ConfigureAwait(false);

            // Ordering is inherited verbatim from the catalog read (NFR-04) — do NOT re-sort.
            var items = bindings
                .Where(b => IsOfferedOnSurface(b.Surfaces, requestedSurface))
                .Select(ToCapabilityDto)
                .ToArray();

            logger.LogDebug(
                "[CAPABILITY-DISCOVERY] GET /api/ai/capabilities returning {Count} entries (surface={Surface})",
                items.Length, requestedSurface);

            return Results.Ok(new CapabilityDiscoveryResponse(items));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[CAPABILITY-DISCOVERY] Failed to enumerate launchable capabilities");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to enumerate launchable capabilities.");
        }
    }

    /// <summary>
    /// A Binding with no declared surfaces is offered on ALL surfaces (per the
    /// <c>sprk_surfaces</c> column dictionary); otherwise the requested surface
    /// must be one of the declared tokens (case-insensitive).
    /// </summary>
    private static bool IsOfferedOnSurface(IReadOnlyList<string> declaredSurfaces, string requestedSurface) =>
        declaredSurfaces.Count == 0 ||
        declaredSurfaces.Any(s => string.Equals(s, requestedSurface, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Projects a full <see cref="Binding"/> contract down to the launcher-facing
    /// shape. Deliberately omits catalog-internal fields not needed to render or
    /// launch a soft-slash entry (match conditions, workflow class, chip
    /// transitions, risk/capture-mode, event memberships, model tier, priority).
    /// </summary>
    private static CapabilityDto ToCapabilityDto(Binding b) => new(
        BindingId: b.BindingId,
        ConsumerType: b.ConsumerType,
        ConsumerCode: string.IsNullOrWhiteSpace(b.ConsumerCode) ? "default" : b.ConsumerCode,
        DisplayLabel: b.ToolDescription ?? string.Empty,
        Surfaces: b.Surfaces.ToArray(),
        LaunchArgsSchemaJson: b.InputSchemaJson);
}

#region Response DTOs

/// <summary>Response for GET /api/ai/capabilities.</summary>
/// <param name="Capabilities">
/// The launchable-capability list, in the SAME order returned by
/// <see cref="IConsumerRoutingService.ListTextProjectableBindingsAsync"/>
/// (consumer type ordinal, then binding id — stable across requests).
/// </param>
public sealed record CapabilityDiscoveryResponse(CapabilityDto[] Capabilities);

/// <summary>
/// One launchable capability's launch metadata — a client-safe projection of a
/// catalog <see cref="Binding"/> row. Carries no per-request timestamps or
/// generated correlation ids; <see cref="BindingId"/> is the stable catalog
/// identity used by the Click entry path (<c>POST .../dispatch</c>, ADR-039).
/// </summary>
/// <param name="BindingId">The <c>sprk_playbookconsumer</c> row id — pass verbatim to the Click-path dispatch endpoint.</param>
/// <param name="ConsumerType">Stable consumer-type code (<c>sprk_consumertype</c>), e.g. <c>chat-summarize</c>.</param>
/// <param name="ConsumerCode">Sub-discriminator (<c>sprk_consumercode</c>); <c>"default"</c> when not set.</param>
/// <param name="DisplayLabel">Maker-authored intent/display text (<c>sprk_tooldescription</c>) — the same text the agent loop sees as the capability's tool description.</param>
/// <param name="Surfaces">Declared placement surfaces (<c>sprk_surfaces</c>); empty means "offered on all surfaces."</param>
/// <param name="LaunchArgsSchemaJson">Raw launch-args shape (<c>sprk_analysisaction.sprk_inputschema</c>), or null when the capability declares none. Catalog DATA only — no runtime side-effect/risk logic is derived here (ADR-039).</param>
public sealed record CapabilityDto(
    Guid BindingId,
    string ConsumerType,
    string ConsumerCode,
    string DisplayLabel,
    string[] Surfaces,
    string? LaunchArgsSchemaJson);

#endregion
