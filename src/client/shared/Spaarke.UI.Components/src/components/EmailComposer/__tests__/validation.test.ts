/**
 * validation.test.ts (task 023, W2)
 *
 * Behavior contract for the canonical `validateState` (design §5.8). Exercises
 * every client-raisable canonical `ValidationErrorCode` and the `forSend`
 * gating rule.
 *
 * NOTE on taxonomy coverage: `validateState` raises 7 of the 10 canonical
 * codes. The remaining three — `ATTACHMENT_BLOCKED_TYPE`, `FROM_REQUIRED`,
 * `FROM_NOT_APPROVED` — are BFF-authoritative (see EmailComposer.types.ts
 * ValidationErrorCode doc comment): the client has no reliable local signal to
 * raise them, so they are reserved for mapping `SendCommunicationError.code`
 * onto the same taxonomy. Those three are exercised as canonical codes in
 * `src/services/__tests__/communicationApi.test.ts` (fromResponse mapping),
 * not here — asserting them from `validateState` would be testing fiction.
 */
import {
  validateState,
  initialState,
  ATTACHMENT_MAX_COUNT,
  ATTACHMENT_MAX_TOTAL_BYTES,
} from '../EmailComposer.reducer';
import type {
  EmailComposerState,
  IAttachmentItem,
  IEmailComposerProps,
  ValidationErrorCode,
} from '../EmailComposer.types';

const noopFetch = jest.fn();

function composeState(overrides: Partial<EmailComposerState> = {}): EmailComposerState {
  const props: IEmailComposerProps = {
    mode: 'compose',
    mount: 'page',
    authenticatedFetch: noopFetch as unknown as IEmailComposerProps['authenticatedFetch'],
  };
  return { ...initialState(props), ...overrides };
}

function recipient(email: string) {
  return { email, resolved: false };
}

function att(id: string, sizeBytes: number): IAttachmentItem {
  return { id, source: 'related', fileName: `${id}.bin`, sizeBytes };
}

function codes(state: EmailComposerState, forSend = true): ValidationErrorCode[] {
  return validateState(state, { forSend }).errors.map(e => e.code);
}

describe('validateState — required-field codes (forSend=true)', () => {
  it('raises TO_REQUIRED when no recipients', () => {
    const state = composeState({ to: [], subject: 'S', body: 'B' });
    expect(codes(state)).toContain('TO_REQUIRED');
  });

  it('raises SUBJECT_REQUIRED when subject is blank/whitespace', () => {
    const state = composeState({ to: [recipient('a@example.com')], subject: '   ', body: 'B' });
    expect(codes(state)).toContain('SUBJECT_REQUIRED');
  });

  it('raises BODY_REQUIRED when body is blank', () => {
    const state = composeState({ to: [recipient('a@example.com')], subject: 'S', body: '' });
    expect(codes(state)).toContain('BODY_REQUIRED');
  });

  it('a fully valid compose passes with no errors', () => {
    const state = composeState({ to: [recipient('a@example.com')], subject: 'S', body: 'B' });
    const result = validateState(state, { forSend: true });
    expect(result.ok).toBe(true);
    expect(result.errors).toEqual([]);
  });
});

describe('validateState — recipient codes', () => {
  it('raises TO_INVALID_EMAIL for a malformed address', () => {
    const state = composeState({ to: [recipient('not-an-email')], subject: 'S', body: 'B' });
    expect(codes(state)).toContain('TO_INVALID_EMAIL');
  });

  it('validates cc/bcc addresses too (TO_INVALID_EMAIL covers all fields)', () => {
    const state = composeState({
      to: [recipient('a@example.com')],
      cc: [recipient('bad-cc')],
      subject: 'S',
      body: 'B',
    });
    expect(codes(state)).toContain('TO_INVALID_EMAIL');
  });

  it('raises TO_TOO_MANY when recipients exceed maxRecipients', () => {
    const state = composeState({
      to: [recipient('a@example.com'), recipient('b@example.com'), recipient('c@example.com')],
      subject: 'S',
      body: 'B',
    });
    const result = validateState(state, { forSend: true, maxRecipients: 2 });
    expect(result.errors.map(e => e.code)).toContain('TO_TOO_MANY');
  });
});

describe('validateState — structural attachment caps (apply regardless of forSend)', () => {
  it('raises ATTACHMENTS_TOO_MANY beyond the 150 cap', () => {
    const many = Array.from({ length: ATTACHMENT_MAX_COUNT + 1 }, (_, i) => att(`a${i}`, 16));
    const state = composeState({ to: [recipient('a@example.com')], subject: 'S', body: 'B', attachments: many });
    expect(codes(state)).toContain('ATTACHMENTS_TOO_MANY');
  });

  it('raises ATTACHMENT_TOO_LARGE beyond the 35 MB total cap', () => {
    const half = Math.ceil(ATTACHMENT_MAX_TOTAL_BYTES / 2) + 1;
    const state = composeState({
      to: [recipient('a@example.com')],
      subject: 'S',
      body: 'B',
      attachments: [att('big-1', half), att('big-2', half)],
    });
    expect(codes(state)).toContain('ATTACHMENT_TOO_LARGE');
  });

  it('excludes forward-deselected attachments from the cap totals', () => {
    const half = Math.ceil(ATTACHMENT_MAX_TOTAL_BYTES / 2) + 1;
    const state = composeState({
      to: [recipient('a@example.com')],
      subject: 'S',
      body: 'B',
      attachments: [att('kept', half), { ...att('dropped', half), selected: false }],
    });
    expect(codes(state)).not.toContain('ATTACHMENT_TOO_LARGE');
  });
});

describe('validateState — forSend gating', () => {
  it('forSend=false skips SUBJECT/BODY/TO required checks (incomplete draft is OK)', () => {
    const state = composeState({ to: [], subject: '', body: '' });
    const result = validateState(state, { forSend: false });
    expect(result.ok).toBe(true);
    expect(result.errors).toEqual([]);
  });

  it('forSend=false STILL enforces structural attachment caps', () => {
    const half = Math.ceil(ATTACHMENT_MAX_TOTAL_BYTES / 2) + 1;
    const state = composeState({ attachments: [att('big-1', half), att('big-2', half)] });
    expect(codes(state, false)).toContain('ATTACHMENT_TOO_LARGE');
  });

  it('allowEmptyBody suppresses BODY_REQUIRED even for a send', () => {
    const state = composeState({ to: [recipient('a@example.com')], subject: 'S', body: '' });
    const result = validateState(state, { forSend: true, allowEmptyBody: true });
    expect(result.errors.map(e => e.code)).not.toContain('BODY_REQUIRED');
  });

  it('view mode skips recipient/required validation entirely', () => {
    const state = composeState({ mode: 'view', to: [], subject: '', body: '' });
    const result = validateState(state, { forSend: true });
    expect(result.ok).toBe(true);
  });
});
