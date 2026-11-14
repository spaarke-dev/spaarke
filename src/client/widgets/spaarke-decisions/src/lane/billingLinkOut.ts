/**
 * The link out for a billing Work Item (spaarkeai-mcp-bff-api-r1 task 031, FR-12; EB-D-194, D-207, D-208).
 *
 * A billing Work Item (one whose row carries `invoiceReview`) is decided only in the Invoice Review page, never in the widget and never
 * in Copilot. The Console opens that page with `openInvoiceReview`, which calls `Xrm.Navigation.navigateTo` and so cannot run in a
 * Copilot frame. The widget instead builds the page's URL and asks the host to open it (`openLink`).
 *
 * The page's `data` value is the shared rule, reused rather than copied: `buildInvoiceReviewLaunch(...).pageInput.data` is
 * `invoiceId={bare lower-case guid}` plus `&lineId={guid}` only when the item has a line. It is URL-encoded once more inside the link,
 * as the BFF's own text link does (`DecisionsTextSummary.InvoiceReviewLink`), because the platform decodes `data` once.
 *
 * The base is the server's `consoleBaseUrl` (the structured content of `spaarke_show_decisions`); the widget never guesses a host. No
 * usable base, or no invoice id, means no link: the row then offers nothing to do rather than a wrong address.
 */
import { buildInvoiceReviewLaunch } from '@spaarke/ui-components/components/Worklist/openInvoiceReview';
import { cleanGuid } from '@spaarke/ui-components/utils/guid';
import type { InvoiceReviewTarget } from '@spaarke/ui-components/components/Worklist/types';

/**
 * `consoleBaseUrl` as the server sends it, checked again here because the widget hands the link to the host: an absolute https URL with
 * no user info, query or fragment, without a trailing slash. Anything else is no base.
 */
export function normalizeConsoleBaseUrl(raw: unknown): string | null {
  if (typeof raw !== 'string' || raw.trim() === '') return null;
  try {
    const url = new URL(raw.trim());
    if (url.protocol !== 'https:' || url.username || url.password || url.search || url.hash) return null;
    return `${url.origin}${url.pathname}`.replace(/\/+$/, '');
  } catch {
    return null;
  }
}

/**
 * `consoleBaseUrl` from the `structuredContent` the host delivered for `spaarke_show_decisions` (the call that opened the widget):
 * the normalized base, or `null` when the result is not there yet, names none, or names an unusable one.
 */
export function consoleBaseUrlOf(toolResult: unknown): string | null {
  if (typeof toolResult !== 'object' || toolResult === null) return null;
  return normalizeConsoleBaseUrl((toolResult as { consoleBaseUrl?: unknown }).consoleBaseUrl);
}

/** The Invoice Review link for a billing item, or `null` when there is no invoice id or no usable base. */
export function buildBillingLinkOut(
  target: InvoiceReviewTarget | null | undefined,
  consoleBaseUrl: string | null | undefined
): string | null {
  if (!target || !cleanGuid(target.invoiceId)) return null;
  const base = normalizeConsoleBaseUrl(consoleBaseUrl);
  if (!base) return null;
  const { pageInput } = buildInvoiceReviewLaunch({ invoiceId: target.invoiceId, lineId: target.lineId ?? null });
  return `${base}/main.aspx?pagetype=webresource&webresourceName=${encodeURIComponent(pageInput.webresourceName)}&data=${encodeURIComponent(pageInput.data)}`;
}
