using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;

// =====================================================================================================================
// SPIKE BRANCH ONLY: spike/specialist-agents-phase0 (specialist-legal-agents-r1 task 011; spec FR-02, FR-03, FR-05, NFR-05). NEVER MERGED,
// never on master's catalogue (project CLAUDE.md section 2). Built by copying ShowDecisionsTool's shape.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10): an [McpArea] tool on the existing /mcp/legal-operations route, so it inherits
// RequireAuthorization + RequireMcpConnectorToken (ADR-028 A10), the mcp rate limit and the kill switch (ADR-008, ADR-032). No package,
// no route, no OAuth registration; one facade registration (ISpecialistProfileSpikeAi) in AnalysisServicesModule.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: notes/reuse-scan.md "Caller -> per-user profile": ICallerSystemUserResolver (behind ISpecialistProfileSpikeAi); the
//     userFragment data-guard style (StatedProfileRenderer). No MCP tool returns a per-user profile.
//   Extension: wraps the resolver; the only new part is the static test-profile lookup (the profile tables are Phase 1).
//   Cost of doing nothing: G1 variants (a)/(b), G1-PAT and G3 cannot run, so D-A7 has no evidence.
// =====================================================================================================================

/// <summary>
/// <c>spike_get_profile</c> (FR-02): the signed-in caller's own TEST profile for one function (NDA review or patent claim drafting),
/// returned as a data-guarded block with the profile's returned instructions (the FR-05 canary) and its <c>profileVersion</c>.
/// Read-only and model-visible.
/// </summary>
/// <remarks>
/// <para><b>Fail closed (FR-02).</b> No signed-in caller, or a caller <see cref="ISpecialistProfileSpikeAi.ResolveCallerAsync"/> cannot
/// resolve, is <c>CALLER_UNRESOLVED</c>; a resolved caller with no profile keyed to their <c>systemuserid</c> is <c>NO_PROFILE</c>.
/// Neither returns any profile; there is no default or fallback profile.</para>
/// <para><b>Logging (FR-03, NFR-05).</b> One line of request header NAMES and one telemetry line per call; no header value, token,
/// claim, systemuserid or profile text is logged.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class SpikeGetProfileTool : McpToolDefinition
{
    public const string Name = "spike_get_profile";

    public const string FunctionArgument = "function";

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    public const string Description =
        "Use for: getting the signed-in attorney's own Spaarke profile before you review an NDA (function 'nda') or draft patent claims "
        + "(function 'patent'). It returns the attorney's positions or drafting standards for that function, instructions for your reply, "
        + "and the profileVersion. Call it first and apply what it returns. If it returns NO_PROFILE or CALLER_UNRESOLVED, stop and say so: "
        + "never use a default profile, another person's profile, or positions you infer. "
        + "Not for: changing the profile (only the attorney changes it); other people's profiles; reviewing or drafting the document itself.";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [FunctionArgument] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray([.. SpikeTestProfiles.Functions.Select(f => (JsonNode)JsonValue.Create(f)!)]),
                ["description"] = "Required. 'nda' for NDA review positions; 'patent' for patent claim drafting standards.",
            },
        },
        ["required"] = new JsonArray(FunctionArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            GetAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = Name,
                Title = "Get my profile (Phase 0 spike)",
                Description = Description,
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    private static async Task<CallToolResult> GetAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        ISpecialistProfileSpikeAi spike,
        TimeProvider time,
        ILogger<SpikeGetProfileTool> logger,
        CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var http = accessor.HttpContext;
        SpikeToolCall.LogHeaderNames(logger, Name, http);
        var outcome = SpikeToolCall.Exception;
        string? version = null;
        try
        {
            var (result, profile) = await RunAsync(request.Params?.Arguments, http, spike, SpikeTestProfiles.Default, cancellationToken);
            version = profile?.ProfileVersion;
            outcome = SpikeToolCall.OutcomeOf(result);
            return result;
        }
        finally
        {
            SpikeToolCall.LogCompleted(logger, Name, outcome, version, time.GetElapsedTime(started), http?.TraceIdentifier);
        }
    }

    /// <summary>The tool's work, with the profile set passed in (production: the embedded set).</summary>
    internal static async Task<(CallToolResult Result, SpikeTestProfile? Profile)> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        ISpecialistProfileSpikeAi spike,
        SpikeTestProfileSet profiles,
        CancellationToken cancellationToken)
    {
        var correlation = http?.TraceIdentifier;
        if (!SpikeToolCall.TryReadRequiredString(arguments, Name, FunctionArgument, 16, correlation, out var function, out var argumentError))
        {
            return (argumentError!, null);
        }

        if (!SpikeTestProfiles.Functions.Contains(function, StringComparer.Ordinal))
        {
            return (SpikeToolCall.Error(SpikeToolCall.InvalidArgument,
                $"'{FunctionArgument}' must be one of: {string.Join(", ", SpikeTestProfiles.Functions)}.", 400, correlation), null);
        }

        var (callerError, profile) = await ResolveProfileAsync(http, spike, profiles, function, cancellationToken);
        if (callerError != null)
        {
            return (callerError, null);
        }

        var structured = new SpikeProfileContent(
            profile!.ProfileVersion,
            profile.ProfileId,
            profile.Function,
            "test",
            profile.ModelTier,
            profile.Served,
            profile.Instructions);
        return (SpikeToolCall.Success(profile.RenderBlock(), structured), profile);
    }

    /// <summary>
    /// The caller's own profile for <paramref name="function"/>, or the FR-02 error: <c>CALLER_UNRESOLVED</c> (no signed-in caller, or
    /// the resolver could not resolve one) or <c>NO_PROFILE</c>. Shared with <see cref="SpikeReviewNdaTool"/> so both tools fail closed
    /// the same way.
    /// </summary>
    internal static async Task<(CallToolResult? Error, SpikeTestProfile? Profile)> ResolveProfileAsync(
        HttpContext? http, ISpecialistProfileSpikeAi spike, SpikeTestProfileSet profiles, string function, CancellationToken cancellationToken)
    {
        var correlation = http?.TraceIdentifier;
        if (http == null || http.User?.Identity?.IsAuthenticated != true)
        {
            return (CallerUnresolved(correlation), null);
        }

        var caller = await spike.ResolveCallerAsync(http.User, cancellationToken);
        if (!caller.IsResolved || string.IsNullOrWhiteSpace(caller.SystemUserId))
        {
            return (CallerUnresolved(correlation), null);
        }

        var profile = profiles.Find(function, caller.SystemUserId);
        if (profile == null)
        {
            return (SpikeToolCall.Error(SpikeToolCall.NoProfile,
                $"No {function} test profile is set up for the signed-in user. Stop: do not use a default profile or anyone else's.", 404, correlation), null);
        }

        return (null, profile);
    }

    private static CallToolResult CallerUnresolved(string? correlation) =>
        SpikeToolCall.Error(SpikeToolCall.CallerUnresolved,
            "The signed-in user could not be matched to a Spaarke user. Stop: do not use a default profile or anyone else's.", 401, correlation);
}

/// <summary><c>structuredContent</c> of <c>spike_get_profile</c>.</summary>
internal sealed record SpikeProfileContent(
    string ProfileVersion,
    string ProfileId,
    string Function,
    string Status,
    string ModelTier,
    JsonObject Profile,
    IReadOnlyList<string> Instructions);
