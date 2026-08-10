/**
 * Pure derivation of the `<SendEmailPage/>` pre-fill fields from a communication
 * record, keyed by compose mode. Extracted from the App so the reply/forward
 * contract (Reply → sender + "Re:", Forward → "Fwd:", Draft → carry recipients)
 * is testable without Xrm/auth. Task 044.
 */

export type ComposerMode = 'compose' | 'view' | 'reply' | 'replyAll' | 'forward' | 'draft';

export interface RecordPrefill {
  from: string;
  to: string;
  cc: string;
  subject: string;
  body: string;
  /** Original sent/created timestamp (formatted) for the quoted-thread header. */
  sent?: string;
}

export interface ComposerFields {
  initialTo?: string[];
  initialCc?: string[];
  initialSubject?: string;
  initialBody?: string;
}

/** Optional overrides for {@link deriveComposerFields}. Additive — omitting them reproduces the historical prefill exactly. */
export interface DeriveComposerFieldsOptions {
  /**
   * FR-10 (BINDING thread-preservation invariant). An AI-drafted authored
   * message that seeds ONLY the authored-message region of the composer. When
   * supplied on a reply / replyAll / forward, the derived quoted previous thread
   * is STILL appended BELOW it, so the composed body is
   * `[AI draft] + [separator] + [quoted thread]` — reaching parity with the
   * in-dialog sparkle re-append (`EmailComposer.runAiDraft`). A `bodyOverride`
   * that replaces the WHOLE body (dropping the quoted thread) is a DEFECT this
   * path exists to prevent (`composeInitialBody` never drops a non-empty quote).
   * A blank/whitespace value is ignored (falls back to the historical lead).
   */
  bodyOverride?: string;
}

/**
 * Separator inserted between the AI-drafted authored region and the quoted
 * thread when a {@link DeriveComposerFieldsOptions.bodyOverride} is applied.
 * A single empty HTML paragraph — IDENTICAL in intent + structure to
 * `EmailComposer.runAiDraft`'s HTML re-append (`<p></p>`), so the seed path and
 * the in-dialog sparkle produce the same [draft]+[separator]+[thread] body.
 */
export const DRAFT_QUOTE_SEPARATOR_HTML = '<p></p>';

/**
 * Compose the composer's initial body from an optional AI-drafted authored
 * region and the derived quoted thread, holding the FR-10 invariant:
 *   - With a `bodyOverride`, the draft seeds ONLY the authored region ABOVE the
 *     quote; the (non-empty) quoted thread is STILL appended below it →
 *     `[draft] + [separator] + [quoted thread]`. Never a whole-body replace.
 *   - Without a `bodyOverride`, the historical `emptyLead` (blank room for the
 *     author to type above the quote) is used unchanged.
 */
function composeInitialBody(
  bodyOverride: string | undefined,
  quoted: string,
  emptyLead: string
): string | undefined {
  const draft = bodyOverride && bodyOverride.trim().length > 0 ? bodyOverride : undefined;
  if (draft) {
    // Draft path — authored region ABOVE, quoted thread STILL below (invariant: never dropped).
    return quoted ? `${draft}${DRAFT_QUOTE_SEPARATOR_HTML}${quoted}` : draft;
  }
  // No draft — preserve the exact historical body (empty lead room above the quote).
  return quoted ? `${emptyLead}${quoted}` : undefined;
}

/** Split a Dataverse recipient string (";"- or ","-separated) into trimmed, non-empty addresses. */
export function splitRecipients(raw: string | undefined | null): string[] {
  if (!raw) return [];
  return raw
    .split(/[;,]/)
    .map(t => t.trim())
    .filter(Boolean);
}

function escapeHtml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/**
 * Build the quoted original message for a reply/forward, like a normal email thread
 * (owner UAT 2026-07-24). A "From / Sent / Subject" header over the original body, quoted
 * in a `<blockquote>`. HTML (the composer defaults to rich text); `<hr>` is intentionally
 * avoided — the shared RichTextEditor registers no HorizontalRuleNode, so a header
 * paragraph + blockquote (QuoteNode) is used, both of which the editor round-trips.
 */
export function buildQuotedThread(record: RecordPrefill | null): string {
  if (!record) return '';
  const from = escapeHtml(record.from ?? '');
  const subject = escapeHtml(record.subject ?? '');
  const sent = escapeHtml(record.sent ?? '');
  const header =
    `<p><b>From:</b> ${from}` +
    (sent ? `<br><b>Sent:</b> ${sent}` : '') +
    (subject ? `<br><b>Subject:</b> ${subject}` : '') +
    `</p>`;
  return `${header}<blockquote>${record.body ?? ''}</blockquote>`;
}

export function deriveComposerFields(
  mode: ComposerMode,
  record: RecordPrefill | null,
  options?: DeriveComposerFieldsOptions
): ComposerFields {
  const subject = record?.subject ?? '';
  const bodyOverride = options?.bodyOverride;
  // Reply / Reply All / Forward all load the original message quoted below the compose area
  // (owner UAT 2026-07-24 — "like a normal email thread"). Reply/Reply All leave two blank
  // lines above for the reply; Forward starts at the quoted block. When an AI `bodyOverride`
  // is supplied (task 025 auto-draft cards / task 023 tools), the draft seeds the authored
  // region ABOVE the quote and the quoted thread is STILL appended below it (FR-10 invariant).
  const quoted = buildQuotedThread(record);
  if (mode === 'reply') {
    return {
      initialTo: record?.from ? [record.from] : undefined,
      initialSubject: subject ? `Re: ${subject}` : undefined,
      initialBody: composeInitialBody(bodyOverride, quoted, '<p></p><p></p>'),
    };
  }
  if (mode === 'replyAll') {
    // To = original sender; Cc = everyone else on the original (To + Cc), with the
    // sender removed and duplicates collapsed. (Does not attempt to strip the current
    // user — the mailbox identity isn't resolved here; a self entry is harmless.)
    const from = (record?.from ?? '').trim();
    const fromLower = from.toLowerCase();
    const others = [...splitRecipients(record?.to), ...splitRecipients(record?.cc)].filter(
      a => a.toLowerCase() !== fromLower
    );
    const cc = Array.from(new Set(others));
    return {
      initialTo: from ? [from] : undefined,
      initialCc: cc.length > 0 ? cc : undefined,
      initialSubject: subject ? `Re: ${subject}` : undefined,
      initialBody: composeInitialBody(bodyOverride, quoted, '<p></p><p></p>'),
    };
  }
  if (mode === 'forward') {
    // Forward starts AT the quoted block (no lead room). `quoted` is non-empty whenever a
    // record is present; the `record?.body` fallback preserves the historical edge case.
    const forwardQuoted = quoted || (record?.body ?? '');
    return {
      initialSubject: subject ? `Fwd: ${subject}` : undefined,
      initialBody: composeInitialBody(bodyOverride, forwardQuoted, ''),
    };
  }
  if (mode === 'compose') {
    // A brand-new blank email ("+ New") — no pre-fill from the record. An AI `bodyOverride`
    // (an AI-drafted brand-new email) seeds the body directly; there is no thread to preserve.
    return bodyOverride && bodyOverride.trim().length > 0 ? { initialBody: bodyOverride } : {};
  }
  // draft / view — carry the record's current content.
  const to = splitRecipients(record?.to);
  return {
    initialTo: to.length > 0 ? to : undefined,
    initialSubject: subject || undefined,
    initialBody: record?.body,
  };
}
