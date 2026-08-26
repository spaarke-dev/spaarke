using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Compose;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// The Compose drafting workspace's route GROUP: <c>/api/compose/*</c>. This file owns the group
/// itself — its prefix, its <c>RequireAuthorization()</c> (ADR-008) and its tags — plus the wire
/// helpers every Compose handler shares. The handlers live in eight sibling
/// <c>Compose*Endpoints</c> files, one per cluster of routes that changes for its own reason
/// (mount · document · save · template · checkout · annotations · sync · active-document); each
/// states that reason in its own file header.
///
/// <para>The AI dispatch endpoint <c>/action/{consumerType}</c> was retired — AI actions flow
/// through the Assistant pane via R7 LinearConsumers (see cleanup PR).</para>
///
/// <para><b>Reason to change (this file)</b>: the group's own contract — its prefix, its
/// authorization posture, which clusters are mounted on it, and the shared RFC-7807 /
/// projection wire shapes below.</para>
/// </summary>
public static class ComposeEndpoints
{
    /// <summary>
    /// Maps all Compose endpoints under <c>/api/compose</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapComposeEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/compose")
            .RequireAuthorization()
            .WithTags("Compose");

        // The route surface is grouped by RESPONSIBILITY; each Map* below owns one cluster and
        // states its reason-to-change in its own file header. Every one of them maps onto THIS
        // group, so the group's prefix, RequireAuthorization() (ADR-008) and tags apply uniformly
        // to every route inside it, wherever the mapping statement physically lives.
        group.MapComposeMountEndpoints();
        group.MapComposeDocumentEndpoints();
        group.MapComposeSaveEndpoints();
        group.MapComposeTemplateEndpoints();
        group.MapComposeCheckoutEndpoints();
        group.MapComposeAnnotationEndpoints();
        // Takes `routes` as well: its webhook receiver is deliberately mapped OUTSIDE the
        // authenticated group (Graph's handshake + delivery are unauthenticated by contract).
        group.MapComposeSyncEndpoints(routes);
        group.MapComposeActiveDocumentEndpoints();

        // (8) POST /api/compose/edit-batch/validate RETIRED (task 064, owner decision 2026-08-25): it never
        // had a client caller — a repo-wide grep for `edit-batch` returns zero .ts/.tsx hits, because AI
        // edit placement happens client-side in `usePendingRedline`, which enforces the same anchor-first
        // contract in TypeScript. Its apply half (ComposeEditBatch/ComposeEditTransaction) applied edits by
        // character offset, and its span producer died with the text-search validator in task 052, so the
        // surface could not have applied anything again either. ComposeEditAnchorPass survives (the
        // ADR-043/041 assessment §7 C-7 designates it the home for closed-set validation) but now has no
        // production caller; see notes/064-orphan-retirement-decisions.md.

        // (9)/(9b) push-annotations + push-preview RETIRED (task 036, §6.5 Path B): the text-anchored
        // push-to-Word WRITE surface (the last text-search byte-author, DocxAnnotationWriter) was retired
        // entirely — R4 persists editor edits as native Word tracked-changes via ComposeShadowPatchEngine
        // on the Save path, making the standalone shuttle redundant. The READ-direction pull-annotations (10)
        // + reanchor (13) endpoints below are unaffected.

        return routes;
    }

    // The ValidateEditBatch handler + its EditBatchValidateRequest body were RETIRED by task 064 with the
    // route above. Nothing here is replaced: the anchor contract it enforced is enforced client-side by
    // `usePendingRedline`, which is where placement actually happens, and server-side by
    // ComposeEditAnchorPass, which the seam tests still drive directly.

    // ─────────────────────────────────────────────────────────────────────────
    // Shared wire helpers. Every Compose*Endpoints file imports them with
    // `using static Sprk.Bff.Api.Api.ComposeEndpoints;`, so handler bodies call them unqualified
    // exactly as they did when all of them lived in this one file.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a <see cref="ComposeDocxProjection"/> (the service-level shape both <c>LoadAsync</c> and
    /// <c>ProjectDocument</c> return) onto its wire DTO. FR-01 (task 010, spaarkeai-compose-fidelity-r4.5):
    /// extracted from the Load response construction so the Upload endpoint reuses the IDENTICAL
    /// mapping — one wire shape for every entry path (F-2 one reader), not a forked projection type.
    /// </summary>
    internal static ComposeProjectionResponse MapProjectionResponse(ComposeDocxProjection projection) =>
        new(
            Status: projection.Status switch
            {
                ComposeProjectionStatus.Success => "success",
                ComposeProjectionStatus.Partial => "partial",
                _ => "failed",
            },
            CanEdit: projection.CanEdit,
            Html: projection.Html,
            Warnings: projection.Warnings
                .Select(w => new ComposeProjectionWarningResponse(w.Code, w.Count))
                .ToList(),
            SchemaVersion: projection.SchemaVersion);

    /// <summary>Task 013 (012-review F7): maps service-layer projection warnings onto the wire DTO
    /// (code + count only — the Detail never crosses the wire). Null-propagating.</summary>
    internal static IReadOnlyList<ComposeProjectionWarningResponse>? MapWarningResponses(
        IReadOnlyList<ComposeProjectionWarning>? warnings)
        => warnings?.Select(w => new ComposeProjectionWarningResponse(w.Code, w.Count)).ToList();

    internal static IResult BadRequest(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
}

// ─────────────────────────────────────────────────────────────────────────────
// Request / response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Wire shape of the server DOCX→editor projection (design §3.3). <c>status</c> is
/// <c>"success" | "partial" | "failed"</c>; the client mounts <c>html</c> only when <c>canEdit</c>, else it
/// renders a read-only / "Open in Word" state. <c>warnings</c> carry codes + counts only (no content).</summary>
public sealed record ComposeProjectionResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("canEdit")] bool CanEdit,
    [property: JsonPropertyName("html")] string Html,
    [property: JsonPropertyName("warnings")] IReadOnlyList<ComposeProjectionWarningResponse> Warnings,
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion);

/// <summary>Wire shape of a single projection fidelity warning (Tier-1 safe — code + count only).</summary>
public sealed record ComposeProjectionWarningResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("count")] int Count);
