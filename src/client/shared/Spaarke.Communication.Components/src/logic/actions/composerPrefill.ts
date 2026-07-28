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

export function deriveComposerFields(mode: ComposerMode, record: RecordPrefill | null): ComposerFields {
  const subject = record?.subject ?? '';
  // Reply / Reply All / Forward all load the original message quoted below the compose area
  // (owner UAT 2026-07-24 — "like a normal email thread"). Reply/Reply All leave two blank
  // lines above for the reply; Forward starts at the quoted block.
  const quoted = buildQuotedThread(record);
  if (mode === 'reply') {
    return {
      initialTo: record?.from ? [record.from] : undefined,
      initialSubject: subject ? `Re: ${subject}` : undefined,
      initialBody: quoted ? `<p></p><p></p>${quoted}` : undefined,
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
      initialBody: quoted ? `<p></p><p></p>${quoted}` : undefined,
    };
  }
  if (mode === 'forward') {
    return {
      initialSubject: subject ? `Fwd: ${subject}` : undefined,
      initialBody: quoted || record?.body,
    };
  }
  if (mode === 'compose') {
    // A brand-new blank email ("+ New") — no pre-fill from the record.
    return {};
  }
  // draft / view — carry the record's current content.
  const to = splitRecipients(record?.to);
  return {
    initialTo: to.length > 0 ? to : undefined,
    initialSubject: subject || undefined,
    initialBody: record?.body,
  };
}
