using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Playbooks;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 024 (spec FR-04, D-209, NFR-02, NFR-05; decision D-024-extract).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over the two playbook list routes' own rules (PlaybookEndpoints.ListPlaybooksForCallerAsync), on the existing
// /mcp/legal-operations host: no package, no DI registration (found by [McpArea]), no background work, no AI-internal type (it never
// names IPlaybookService; ADR-013).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: GET /api/ai/playbooks and GET /api/ai/playbooks/public (PlaybookEndpoints); the legacy agent's de-duplicated list
//     (AgentEndpoints, deleted by task 001; git show 37f0864afe).
//   Extension: it calls the routes' shared method twice; its own logic is only the union, the de-duplication by id and the slim rows.
//   Cost of doing nothing: Copilot users cannot discover which JPS playbooks they have (FR-04 fails).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_list_playbooks</c> (FR-04): the JPS playbooks the signed-in user has — their own and the public ones — as ONE list,
/// de-duplicated by id, in slim rows. Read-only; visible to the model and the app; inputs are an optional <c>nameFilter</c> and
/// <c>page</c>.
/// </summary>
/// <remarks>
/// <para><b>Reads.</b> As the caller, through <see cref="PlaybookEndpoints.ListPlaybooksForCallerAsync"/>: the owned list (the rules of
/// <c>GET /api/ai/playbooks</c>), then the public list (<c>GET /api/ai/playbooks/public</c>), each at the routes' defaults (20 per page,
/// newest first) for the same page and name filter. The ids are the union of the two routes' pages for the same caller and query; a
/// playbook in both appears once, as owned (the legacy agent's order).</para>
/// <para><b>Caller (ADR-028 A10).</b> The request context is <c>IHttpContextAccessor.HttpContext</c>, passed through unchanged. No caller is
/// the method's <c>UserContextRequired</c>. A caller whose <c>systemuser</c> cannot be resolved is refused with the same
/// <c>UserContextRequired</c> before the public list is read: A10 serves only callers that resolve to a <c>systemuser</c> (the owned
/// route answers that caller an empty page instead). A failure of either list is an <c>isError</c> result, never a partial list.</para>
/// <para><b>Model-visible output (ADR-015, NFR-02).</b> Rows carry id, name, description (at most 200 characters), owner kind and format
/// only: never a definition, a prompt or a scope. Rows are kept only while the result stays inside <see cref="RowBudgetBytes"/>, measured
/// with the escaping the result is written with, so the result is under 64 KiB at the list maximum whatever the text; a cut list says so
/// (<c>truncated</c>) and asks for a name filter.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class ListPlaybooksTool : McpToolDefinition
{
    public const string Name = "spaarke_list_playbooks";

    public const string NameFilterArgument = "nameFilter";

    public const string PageArgument = "page";

    /// <summary>The longest name filter accepted (a name fragment, not a query).</summary>
    public const int MaxNameFilterLength = 100;

    /// <summary>The highest page accepted (keeps the service's skip, page × 20, far from overflow).</summary>
    public const int MaxPage = 1000;

    /// <summary>The longest description in a row (characters, the ellipsis included).</summary>
    public const int MaxDescriptionLength = 200;

    /// <summary>The longest name in a row (characters, the ellipsis included).</summary>
    public const int MaxNameLength = 200;

    /// <summary>Owner kind of a playbook the caller owns.</summary>
    public const string Owned = "owned";

    /// <summary>Owner kind of a public playbook the caller does not own.</summary>
    public const string Public = "public";

    /// <summary>The format every row has (D-209: say which kind of playbook).</summary>
    public const string JpsFormat = "JPS";

    /// <summary>
    /// The bytes the rows may take in the result (their <c>structuredContent</c> rows plus their lines of text), as the default
    /// (escape-everything) encoder writes them. The envelope and the summary are far below the 8 KiB left to 64 KiB.
    /// </summary>
    public const int RowBudgetBytes = 56 * 1024;

    /// <summary>A refused argument's <c>reasonCode</c> (new with task 024; the routes answer a bad query value with a bare 400).</summary>
    public const string InvalidArgumentReasonCode = "playbook.invalid-argument";

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for"; D-209 qualifies "playbook").</summary>
    public const string Description =
        "Use for: listing the JPS playbooks (AI analysis recipes) the signed-in user has in Spaarke: their own and the public ones, each "
        + "once, with its id, name, a short description, whether it is the user's own ('owned') or 'public', and its format (JPS). Use it "
        + "when the user asks which AI analysis recipes exist or needs a playbook's id. The optional nameFilter narrows by a part of the "
        + "name; page continues the list (up to 20 of the user's own and 20 public ones per page). "
        + "Not for: legal or contract-review playbooks as documents (clause positions, negotiation guides): search documents for those; "
        + "a playbook's definition, steps or prompts; creating, changing or sharing a playbook, which is done in Spaarke; analysis results.";

    private static readonly JsonSerializerOptions Json = DecisionToolCall.Json;

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [NameFilterArgument] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = MaxNameFilterLength,
                ["description"] = "Optional. Only playbooks whose name contains this text.",
            },
            [PageArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["maximum"] = MaxPage,
                ["description"] = "Optional. The page to list, from 1. Default: 1.",
            },
        },
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            ListAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = Name,
                Title = "List my JPS playbooks",
                Description = Description,
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });

        // The input schema is stated, not inferred; the handler reads the raw arguments against the same set.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    private static Task<CallToolResult> ListAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        TimeProvider time,
        ILogger<ListPlaybooksTool> logger,
        CancellationToken cancellationToken)
    {
        var http = accessor.HttpContext;
        return DecisionToolCall.LogAroundAsync(
            logger, time, Name, http,
            () => RunAsync(request.Params?.Arguments, http, cancellationToken),
            RowCount);
    }

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments, HttpContext? http, CancellationToken cancellationToken)
    {
        if (!TryReadArguments(arguments, http?.TraceIdentifier, out var nameFilter, out var page, out var argumentError))
        {
            return argumentError!;
        }

        // The routes' rules, as the caller. No caller is the method's UserContextRequired, before anything is read.
        var owned = await PlaybookEndpoints.ListPlaybooksForCallerAsync(
            http, PlaybookListScope.Owned, page, nameFilter: nameFilter, cancellationToken: cancellationToken);
        if (owned.Outcome == PlaybookListOutcome.OwnerUnresolved)
        {
            // ADR-028 A10: /mcp serves only callers that resolve to a systemuser. The route lists an empty page; the tool refuses.
            return Error(PlaybookListResult.UserContextRequired, http);
        }

        if (owned.Outcome != PlaybookListOutcome.Listed)
        {
            return Error(owned, http);
        }

        var shared = await PlaybookEndpoints.ListPlaybooksForCallerAsync(
            http, PlaybookListScope.Public, page, nameFilter: nameFilter, cancellationToken: cancellationToken);
        if (shared.Outcome != PlaybookListOutcome.Listed)
        {
            return Error(shared, http);
        }

        // The union, de-duplicated by id: owned first, then the public rows not already listed (the legacy agent's order). A public row the
        // caller owns is dropped from the public rows: the two lists page independently, so it can fall on page 1 of the public list and on
        // page 2 of the owned one, and it would otherwise appear as "public" on one page and "owned" on another.
        var callerOwnerId = owned.OwnerSystemUserId;
        var seen = new HashSet<Guid>();
        var rows = owned.Items.Where(p => seen.Add(p.Id)).Select(p => Row(p.Id, p.Name, p.Description, Owned))
            .Concat(shared.Items
                .Where(p => callerOwnerId is null || p.OwnerId != callerOwnerId.Value)
                .Where(p => seen.Add(p.Id))
                .Select(p => Row(p.Id, p.Name, p.Description, Public)))
            .ToList();

        var kept = new List<PlaybookRow>(rows.Count);
        var lines = new StringBuilder();
        var used = 0;
        foreach (var row in rows)
        {
            var line = $"- {TextSafe(row.Name)} ({row.OwnerKind}), id {row.Id:D}\n";
            var cost = JsonSerializer.SerializeToUtf8Bytes(row, Json).Length + JsonSerializer.SerializeToUtf8Bytes(line, Json).Length;
            if (used + cost > RowBudgetBytes)
            {
                break;
            }

            used += cost;
            kept.Add(row);
            lines.Append(line);
        }

        var truncated = kept.Count < rows.Count;
        var hasMore = owned.Page!.HasNextPage || shared.Page!.HasNextPage;
        var ownedCount = kept.Count(r => r.OwnerKind == Owned);
        var text = new StringBuilder()
            .Append($"{kept.Count} JPS playbook(s) on page {page}: {ownedCount} owned, {kept.Count - ownedCount} public.")
            .Append(truncated ? " The list was cut to fit; narrow it with nameFilter." : string.Empty)
            .Append(hasMore ? $" More on page {page + 1}." : string.Empty)
            .Append('\n')
            .Append(lines)
            .ToString();

        return DecisionToolCall.Success(text, new ListPlaybooksContent(kept, page, hasMore, truncated));
    }

    private static PlaybookRow Row(Guid id, string? name, string? description, string ownerKind) =>
        new(id, Clip(name, MaxNameLength) ?? string.Empty, Clip(description, MaxDescriptionLength), ownerKind, JpsFormat);

    private static string? Clip(string? value, int max) =>
        value is null || value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");

    /// <summary>
    /// A name for a line of the text content: every control character (CR, LF, tab, NEL, ...) and Unicode line or paragraph separator
    /// becomes a space, so a user-authored name cannot start a line of its own and pass for another row (K2). The structured rows keep
    /// the name as it is.
    /// </summary>
    private static string TextSafe(string value) =>
        value.Any(IsLineBreaking)
            ? string.Create(value.Length, value, static (span, v) =>
            {
                for (var i = 0; i < v.Length; i++)
                {
                    span[i] = IsLineBreaking(v[i]) ? ' ' : v[i];
                }
            })
            : value;

    private static bool IsLineBreaking(char c) =>
        char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator;

    private static CallToolResult Error(PlaybookListResult result, HttpContext? http)
    {
        var message = result.ReasonCode == DataverseUserClientErrorCodes.UserContextRequired
            ? "Spaarke could not confirm your Spaarke user account. If you are signed in with a licensed Spaarke account, try again shortly."
            : "The JPS playbook list could not be read. Try again shortly.";
        return McpToolResults.FromError(new McpToolError(result.ReasonCode!, message, result.Status), http?.TraceIdentifier);
    }

    /// <summary>The two optional arguments, or an <see cref="InvalidArgumentReasonCode"/> error naming the fix (never an argument value).</summary>
    private static bool TryReadArguments(
        IDictionary<string, JsonElement>? arguments, string? correlationId, out string? nameFilter, out int page, out CallToolResult? error)
    {
        nameFilter = null;
        page = 1;
        error = null;
        if (arguments is null)
        {
            return true;
        }

        if (arguments.Keys.Any(k => k is not (NameFilterArgument or PageArgument)))
        {
            error = InvalidArgument($"{Name} takes only the optional arguments '{NameFilterArgument}' and '{PageArgument}'. Send the call again without the others.", correlationId);
            return false;
        }

        if (arguments.TryGetValue(NameFilterArgument, out var filter) && filter.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (filter.ValueKind != JsonValueKind.String || filter.GetString()!.Length > MaxNameFilterLength)
            {
                error = InvalidArgument($"'{NameFilterArgument}' must be text of at most {MaxNameFilterLength} characters. Leave it out to list every playbook.", correlationId);
                return false;
            }

            nameFilter = string.IsNullOrWhiteSpace(filter.GetString()) ? null : filter.GetString();
        }

        if (arguments.TryGetValue(PageArgument, out var pageValue) && pageValue.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (pageValue.ValueKind != JsonValueKind.Number || !pageValue.TryGetInt32(out page) || page < 1 || page > MaxPage)
            {
                page = 1;
                error = InvalidArgument($"'{PageArgument}' must be a whole number from 1 to {MaxPage}. Leave it out for the first page.", correlationId);
                return false;
            }
        }

        return true;
    }

    private static CallToolResult InvalidArgument(string message, string? correlationId) =>
        McpToolResults.FromError(new McpToolError(InvalidArgumentReasonCode, message, StatusCodes.Status400BadRequest), correlationId);

    private static int RowCount(CallToolResult result) =>
        result.IsError != true
        && result.StructuredContent is { ValueKind: JsonValueKind.Object } body
        && body.TryGetProperty("items", out var items)
        && items.ValueKind == JsonValueKind.Array
            ? items.GetArrayLength()
            : 0;
}

/// <summary>One slim row (ADR-015): no definition, prompt or scope ever.</summary>
internal sealed record PlaybookRow(Guid Id, string Name, string? Description, string OwnerKind, string Format);

/// <summary>
/// <c>structuredContent</c> of <c>spaarke_list_playbooks</c>: the rows, the page they are, whether either list continues on the next page,
/// and whether rows were left out to keep the result inside the size bound.
/// </summary>
internal sealed record ListPlaybooksContent(IReadOnlyList<PlaybookRow> Items, int Page, bool HasMore, bool Truncated);
