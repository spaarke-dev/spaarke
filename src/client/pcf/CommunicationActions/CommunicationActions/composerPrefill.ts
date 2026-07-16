/**
 * Pure derivation of the `<SendEmailPage/>` pre-fill fields from a communication
 * record, keyed by compose mode. Extracted from the App so the reply/forward
 * contract (Reply → sender + "Re:", Forward → "Fwd:", Draft → carry recipients)
 * is testable without Xrm/auth. Task 044.
 */

export type ComposerMode = 'compose' | 'view' | 'reply' | 'forward' | 'draft';

export interface RecordPrefill {
  from: string;
  to: string;
  subject: string;
  body: string;
}

export interface ComposerFields {
  initialTo?: string[];
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
  if (mode === 'forward') {
    return {
      initialSubject: subject ? `Fwd: ${subject}` : undefined,
      initialBody: record?.body,
    };
  }
  // draft / compose / view — carry the record's current content.
  const to = splitRecipients(record?.to);
  return {
    initialTo: to.length > 0 ? to : undefined,
    initialSubject: subject || undefined,
    initialBody: record?.body,
  };
}
