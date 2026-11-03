/**
 * Display helpers for the Files section (ebilling-r1 task 151e). Pure, so every rule is asserted directly.
 */

/** "1.2 MB", "340 KB", "812 B"; an em dash when the size is not recorded. */
export function formatSize(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined || !Number.isFinite(bytes) || bytes < 0) return '—';
  if (bytes < 1024) return `${Math.round(bytes)} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(bytes < 10 * 1024 ? 1 : 0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** The added-on date in the user's locale (date and time); an em dash when absent or not a date. */
export function formatAddedOn(iso: string | null | undefined, locale?: string, timeZone?: string): string {
  if (!iso) return '—';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '—';
  return date.toLocaleString(locale, { dateStyle: 'medium', timeStyle: 'short', timeZone });
}

/**
 * The media types a browser tab renders from a blob (the BFF serves the same allow-list inline: PDF, images, plain text). Anything else is
 * a download, so Open is offered only for these; Download is offered for every file.
 */
export function isViewable(contentType: string | null | undefined): boolean {
  const type = (contentType ?? '').split(';')[0].trim().toLowerCase();
  return type === 'application/pdf' || type === 'text/plain' || type === 'image/png' || type === 'image/jpeg' || type === 'image/gif' || type === 'image/webp';
}

/** "Invoice PDF" or "Supporting". */
export function purposeLabel(purpose: string): string {
  return purpose === 'InvoicePdf' ? 'Invoice PDF' : 'Supporting';
}
