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

export function deriveComposerFields(mode: ComposerMode, record: RecordPrefill | null): ComposerFields {
  const subject = record?.subject ?? '';
  if (mode === 'reply') {
    return {
      initialTo: record?.from ? [record.from] : undefined,
      initialSubject: subject ? `Re: ${subject}` : undefined,
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
    };
  }
  if (mode === 'forward') {
    return {
      initialSubject: subject ? `Fwd: ${subject}` : undefined,
      initialBody: record?.body,
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
