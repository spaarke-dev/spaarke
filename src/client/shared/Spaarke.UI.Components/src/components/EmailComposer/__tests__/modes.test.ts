/**
 * modes.test.ts (task 023, W2)
 *
 * Pure mode-derivation helpers (design §5.2/§5.5):
 *   - `dedupSubjectPrefix` — idempotent Re:/Fwd: prefixing
 *   - `deriveReplyState` / `deriveForwardState` / `deriveDraftState`
 *   - `mapStateToDraftUpdate` — draft-persistence payload shape
 *
 * ADR-038 domain-logic behavior contracts (pure functions; MAINTAIN-class).
 *
 * NOTE: `dedupSubjectPrefix` dedups only the exact prefix it is given
 * (`'Re:'` | `'Fwd:'`). It does NOT normalize Outlook's `FW:` variant onto
 * `Fwd:` — an `FW:`-prefixed subject would still be prepended with `Fwd:`.
 * That is the current implementation; the tests assert the real behavior.
 */
import {
  dedupSubjectPrefix,
  deriveReplyState,
  deriveForwardState,
  deriveDraftState,
  mapStateToDraftUpdate,
  initialState,
} from '../EmailComposer.reducer';
import type { EmailComposerState, IEmailComposerProps, ISourceCommunicationRecord } from '../EmailComposer.types';

const source: ISourceCommunicationRecord = {
  communicationId: 'comm-1',
  from: 'sender@example.com',
  to: ['orig-to@example.com'],
  cc: ['orig-cc@example.com'],
  subject: 'Status update',
  body: '<p>Body content</p>',
  bodyFormat: 'HTML',
  sentAt: '2026-07-01T10:00:00Z',
  attachments: [
    { id: 'att-1', source: 'related', fileName: 'a.pdf', sizeBytes: 1024, documentId: 'doc-1' },
    { id: 'att-2', source: 'spe', fileName: 'b.pdf', sizeBytes: 2048, documentId: 'doc-2' },
  ],
  associations: [{ entityType: 'sprk_matter', entityId: 'matter-1' }],
};

describe('dedupSubjectPrefix', () => {
  it('prepends Re: when absent', () => {
    expect(dedupSubjectPrefix('Hello', 'Re:')).toBe('Re: Hello');
  });

  it('does not double-prefix when Re: already present', () => {
    expect(dedupSubjectPrefix('Re: Hello', 'Re:')).toBe('Re: Hello');
  });

  it('is case-insensitive when detecting the existing prefix', () => {
    expect(dedupSubjectPrefix('re: Hello', 'Re:')).toBe('re: Hello');
    expect(dedupSubjectPrefix('RE: Hello', 'Re:')).toBe('RE: Hello');
  });

  it('prepends Fwd: when absent and dedups when present', () => {
    expect(dedupSubjectPrefix('Hello', 'Fwd:')).toBe('Fwd: Hello');
    expect(dedupSubjectPrefix('Fwd: Hello', 'Fwd:')).toBe('Fwd: Hello');
  });

  it('trims surrounding whitespace on the source subject', () => {
    expect(dedupSubjectPrefix('   Hello   ', 'Re:')).toBe('Re: Hello');
  });

  it('does NOT treat Outlook FW: as an existing Fwd: prefix (documented gap)', () => {
    expect(dedupSubjectPrefix('FW: Hello', 'Fwd:')).toBe('Fwd: FW: Hello');
  });
});

describe('deriveReplyState', () => {
  const patch = deriveReplyState(source);

  it('addresses the reply To the original sender only', () => {
    expect(patch.to?.map(r => r.email)).toEqual(['sender@example.com']);
    expect(patch.cc).toEqual([]);
  });

  it('applies the Re: subject and empties the body', () => {
    expect(patch.subject).toBe('Re: Status update');
    expect(patch.body).toBe('');
  });

  it('carries associations forward and is editable', () => {
    expect(patch.associations).toEqual(source.associations);
    expect(patch.readOnly).toBe(false);
    expect(patch.attachments).toEqual([]);
  });
});

describe('deriveForwardState', () => {
  const patch = deriveForwardState(source);

  it('blanks recipients and applies the Fwd: subject', () => {
    expect(patch.to).toEqual([]);
    expect(patch.subject).toBe('Fwd: Status update');
  });

  it('quotes the forwarded message into the body', () => {
    expect(patch.body).toContain('Forwarded message');
    expect(patch.body).toContain('Body content');
  });

  it('carries every source attachment pre-selected for inclusion', () => {
    expect(patch.attachments).toHaveLength(2);
    expect(patch.attachments?.every(a => a.selected === true)).toBe(true);
  });
});

describe('deriveDraftState', () => {
  const patch = deriveDraftState(source);

  it('rehydrates To/Cc/subject/body editable from the draft record', () => {
    expect(patch.to?.map(r => r.email)).toEqual(['orig-to@example.com']);
    expect(patch.cc?.map(r => r.email)).toEqual(['orig-cc@example.com']);
    expect(patch.subject).toBe('Status update');
    expect(patch.body).toBe('<p>Body content</p>');
    expect(patch.readOnly).toBe(false);
  });
});

describe('mapStateToDraftUpdate', () => {
  function draftState(): EmailComposerState {
    const props: IEmailComposerProps = {
      mode: 'draft',
      mount: 'page',
      authenticatedFetch: jest.fn() as unknown as IEmailComposerProps['authenticatedFetch'],
      sourceRecord: source,
    };
    return initialState(props);
  }

  it('maps engine state onto the sprk_communication draft payload shape', () => {
    const update = mapStateToDraftUpdate(draftState());
    expect(update).toEqual({
      sprk_to: 'orig-to@example.com',
      sprk_cc: 'orig-cc@example.com',
      sprk_subject: 'Status update',
      sprk_body: '<p>Body content</p>',
      sprk_bodyformat: 'HTML',
      sprk_communicationtype: 'Email',
    });
  });

  it('joins multiple recipients with a semicolon', () => {
    const state = draftState();
    const multi: EmailComposerState = {
      ...state,
      to: [
        { email: 'a@example.com', resolved: false },
        { email: 'b@example.com', resolved: false },
      ],
    };
    expect(mapStateToDraftUpdate(multi).sprk_to).toBe('a@example.com;b@example.com');
  });
});
