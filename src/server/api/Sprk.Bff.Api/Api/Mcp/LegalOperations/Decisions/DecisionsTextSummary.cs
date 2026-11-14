using System.Globalization;
using System.Text;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r1 task 022 (spec FR-03, FR-04, FR-14; design §9.5): what the model reads from spaarke_show_decisions.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: the Console renders the lane in the browser; HandoffUrlBuilder (Api/Agent, the April surface) and
//     InquiryRecordFacts.AppLink build Dataverse links from the environment URL, but neither builds a Console or Invoice Review page
//     link, and nothing reduces privileged Work Items.
//   Extension: the link shape is the one those builders and openInvoiceReview.ts use (main.aspx?pagetype=webresource), from the same
//     setting (Dataverse:EnvironmentUrl); the April builder is not reused because the MCP tools inject only the decision capability and
//     Api/Mcp helpers (ADR-013 constraint of this task) and that builder falls back to a hard-coded host.
//   Cost of doing nothing: with no widget (Chat, FR-14) the model could not name counts or link to the items, and without the
//     reduction privileged detail would reach the model (FR-04).
// =====================================================================================================================

/// <summary>
/// The model-visible side of <c>spaarke_show_decisions</c>: the FR-04 privilege reduction, the Console and Invoice Review links, and the
/// short text summary (<c>content</c>) that answers on its own when no widget renders (FR-14).
/// </summary>
/// <remarks>
/// <para><b>FR-04.</b> A Work Item whose <see cref="WorklistItemSummary.PrivilegeFlagged"/> is true reaches model-visible output only as
/// <see cref="PrivilegedLine"/>: no title, subject, reason, matter name or number, evidence or party name, and no row. A core record is
/// named only when a non-privileged row of the page sits in it. Full detail is for the app-only paging tool.</para>
/// <para><b>Links.</b> Built only from <c>Dataverse:EnvironmentUrl</c> (an absolute https URL with no user info) and fixed web-resource
/// names; never from record text. The Console (<c>sprk_spaarkeai</c>) has no launch parameter for one Work Item today, so a non-billing
/// item links to the Console, where the Decide lane is; a billing item links to its Invoice Review page with the <c>data</c> rules of
/// <c>openInvoiceReview.ts</c> (<c>invoiceId</c>, plus <c>lineId</c> only when present; bare lower-case guids; the value URL-encoded once).
/// No usable URL means no links, never a guessed host.</para>
/// </remarks>
internal static class DecisionsTextSummary
{
    /// <summary>The Spaarke Console's web resource (engineering identifier; product name "Spaarke Console").</summary>
    public const string ConsoleWebResource = "sprk_spaarkeai";

    /// <summary>The eBilling Invoice Review page (<c>INVOICE_REVIEW_WEBRESOURCE_NAME</c> in <c>openInvoiceReview.ts</c>).</summary>
    public const string InvoiceReviewWebResource = "sprk_invoicereviewpage";

    /// <summary>How many non-privileged items the text lists.</summary>
    public const int TopItems = 5;

    /// <summary>The one line a privileged Work Item is reduced to (FR-04, ADR-015), with the count of such items.</summary>
    public static string PrivilegedLine(int count, int pageItemCount, bool morePages) =>
        morePages
            ? $"{count.ToString(CultureInfo.InvariantCulture)} privileged items among the first {pageItemCount.ToString(CultureInfo.InvariantCulture)}"
              + " — more of the lane may be privileged; open to review"
            : $"{count.ToString(CultureInfo.InvariantCulture)} privileged items — open to review";

    /// <summary><c>structuredContent.privileged.scope</c> when the lane ends on page 1: the count covers the whole lane.</summary>
    public const string PrivilegedScopeLane = "lane";

    /// <summary><c>structuredContent.privileged.scope</c> when the lane continues after page 1: the count covers page 1 only (privilege
    /// is derived per page, task 018), so the lane may hold more.</summary>
    public const string PrivilegedScopeFirstPage = "firstPage";

    /// <summary>The scope of the privileged count for a lane that does (<paramref name="morePages"/>) or does not continue after page 1.</summary>
    public static string PrivilegedScope(bool morePages) => morePages ? PrivilegedScopeFirstPage : PrivilegedScopeLane;

    /// <summary>
    /// The base the links are built on: <paramref name="environmentUrl"/> (<c>Dataverse:EnvironmentUrl</c>) without a trailing slash, when it
    /// is an absolute https URL with no user info; otherwise null. This is <c>consoleBaseUrl</c> in the tool's structured content.
    /// </summary>
    public static string? ConsoleBaseUrl(string? environmentUrl)
    {
        if (!Uri.TryCreate(environmentUrl?.Trim(), UriKind.Absolute, out var env)
            || env.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(env.UserInfo)
            || !string.IsNullOrEmpty(env.Query) || !string.IsNullOrEmpty(env.Fragment))
        {
            return null;
        }

        return env.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    /// <summary>The Spaarke Console, where the Decide lane is. Null without a base.</summary>
    public static string? ConsoleLink(string? baseUrl) =>
        baseUrl is null ? null : $"{baseUrl}/main.aspx?pagetype=webresource&webresourceName={ConsoleWebResource}";

    /// <summary>The Invoice Review page on <paramref name="review"/>'s invoice (and line). Null without a base.</summary>
    public static string? InvoiceReviewLink(string? baseUrl, WorklistInvoiceReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (baseUrl is null)
        {
            return null;
        }

        var data = $"invoiceId={review.InvoiceId:D}" + (review.LineId is { } line ? $"&lineId={line:D}" : string.Empty);
        return $"{baseUrl}/main.aspx?pagetype=webresource&webresourceName={InvoiceReviewWebResource}&data={Uri.EscapeDataString(data)}";
    }

    /// <summary>
    /// The page with privileged Work Items taken out (FR-04): the rows the model may see, the core records those rows sit in, and how many
    /// rows were taken out.
    /// </summary>
    public static (IReadOnlyList<WorklistItemSummary> Visible, IReadOnlyDictionary<Guid, WorklistCoreSummary> Cores, int Privileged) Reduce(
        IReadOnlyList<WorklistItemSummary> items, IReadOnlyDictionary<Guid, WorklistCoreSummary> cores)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(cores);

        var visible = items.Where(i => !i.PrivilegeFlagged).ToList();
        var named = visible.Where(i => i.CoreRecordId is not null).Select(i => i.CoreRecordId!.Value).ToHashSet();
        var visibleCores = cores.Where(c => named.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value);
        return (visible, visibleCores, items.Count - visible.Count);
    }

    /// <summary>What the text is built from: already reduced (<see cref="Reduce"/>), so nothing privileged can reach it.</summary>
    /// <param name="Cards">The whole lane's category cards (<see cref="DecideLaneCategories.Partition"/>).</param>
    /// <param name="Category">The lens in force (<see cref="DecideLaneCategories.Resolve"/>).</param>
    /// <param name="RequestedCategory">The category the call asked for, when it differs from <paramref name="Category"/>.</param>
    /// <param name="Visible">The non-privileged rows of page 1, in Console order.</param>
    /// <param name="Cores">The core records of <paramref name="Visible"/>.</param>
    /// <param name="PageItemCount">How many items page 1 holds, privileged ones included.</param>
    /// <param name="PrivilegedOnPage">How many of them were reduced to <see cref="PrivilegedLine"/>.</param>
    /// <param name="MorePages">True when the lane continues after page 1.</param>
    /// <param name="BaseUrl"><see cref="ConsoleBaseUrl"/>; null for no links.</param>
    public sealed record Input(
        IReadOnlyList<DecideLaneCategory> Cards,
        string Category,
        string? RequestedCategory,
        IReadOnlyList<WorklistItemSummary> Visible,
        IReadOnlyDictionary<Guid, WorklistCoreSummary> Cores,
        int PageItemCount,
        int PrivilegedOnPage,
        bool MorePages,
        string? BaseUrl);

    /// <summary>The <c>content</c> text: counts per category, the top items with links, the privileged line, and where to go next.</summary>
    public static string Build(Input input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var total = input.Cards.First(c => c.Key == DecideLaneCategories.All).Count;
        var console = ConsoleLink(input.BaseUrl);
        var text = new StringBuilder();

        text.Append("Spaarke Decide lane: ").Append(Count(total, "item needs", "items need")).AppendLine(" your decision.");
        if (total == 0)
        {
            text.AppendLine("Nothing is waiting for a decision.");
            return text.ToString().TrimEnd();
        }

        text.Append("By category: ")
            .AppendJoin("; ", input.Cards.Where(c => c.Key != DecideLaneCategories.All).Select(c => $"{c.Label} {c.Count}"))
            .AppendLine(".");

        if (input.RequestedCategory is { } requested)
        {
            text.Append("No items are in ").Append(DecideLaneCategories.LabelOf(requested) ?? requested).AppendLine("; showing all categories.");
        }
        else if (input.Category != DecideLaneCategories.All)
        {
            var card = input.Cards.First(c => c.Key == input.Category);
            text.Append("Showing ").Append(card.Label).Append(" (").Append(card.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(").");
        }

        var top = input.Visible.Where(i => DecideLaneCategories.IsIn(DecideLaneMember.From(i), input.Category)).Take(TopItems).ToList();
        if (top.Count > 0)
        {
            text.AppendLine(input.MorePages ? $"Top items (from the first {input.PageItemCount} in Console order):" : "Top items:");
            for (var n = 0; n < top.Count; n++)
            {
                text.Append(n + 1).Append(". ").AppendLine(ItemLine(top[n], input.Cores, input.BaseUrl, console));
            }
        }
        else if (input.Category != DecideLaneCategories.All)
        {
            text.Append("None of the first ").Append(input.PageItemCount.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" items in Console order that can be listed here is in this category.");
        }

        // Page-scoped: when the lane continues, the line says so and appears even for 0 (a later page may hold privileged items).
        if (input.PrivilegedOnPage > 0 || input.MorePages)
        {
            text.AppendLine(PrivilegedLine(input.PrivilegedOnPage, input.PageItemCount, input.MorePages));
        }

        if (console is not null)
        {
            text.Append("Open the Spaarke Console to see and decide every item: ").AppendLine(console);
        }

        text.Append("Decisions are made in Spaarke (or in the decisions widget), not from this text.");
        return text.ToString();
    }

    private static string ItemLine(
        WorklistItemSummary item, IReadOnlyDictionary<Guid, WorklistCoreSummary> cores, string? baseUrl, string? console)
    {
        // The widget's headline (the subject name, else the title), then the reason (the rule's short name).
        var headline = OneLine(item.SubjectName) ?? OneLine(item.Title);
        var reason = OneLine(item.RuleShortName);
        var line = new StringBuilder(headline ?? reason ?? "Work Item");
        if (headline is not null && reason is not null)
        {
            line.Append(": ").Append(reason);
        }

        if (item.CoreRecordId is { } coreId && cores.TryGetValue(coreId, out var core) && OneLine(core.Name ?? core.Number) is { } matter)
        {
            line.Append(" (").Append(matter).Append(')');
        }

        if (item.RaisedOn is { } raised)
        {
            line.Append(", raised ").Append(raised.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (item.InvoiceReview is { } review)
        {
            // D-208: "Finding" is the user word for a billing Work Item in a billing link-out; EB-D-194: decided only in Invoice Review.
            var link = InvoiceReviewLink(baseUrl, review);
            return link is null
                ? line.Append(". Finding: decide it in Invoice Review.").ToString()
                : line.Append(". Finding: decide it in Invoice Review: ").Append(link).ToString();
        }

        return console is null ? line.Append('.').ToString() : line.Append(". Open in Spaarke: ").Append(console).ToString();
    }

    private static string Count(int count, string singular, string plural) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural)}";

    /// <summary>Record text on one line: control characters (line breaks included) become spaces, runs of spaces collapse. Null when blank.</summary>
    private static string? OneLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var ch = char.IsControl(c) ? ' ' : c;
            if (ch == ' ' && builder.Length > 0 && builder[^1] == ' ')
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString().Trim();
    }
}
