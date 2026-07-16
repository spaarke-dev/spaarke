/**
 * EmailComposer.reducer.test.ts (task 023, W2)
 *
 * Unit coverage for the pure `<EmailComposer />` state machine:
 *   - `initialState(props)` per mode (compose / view / reply / forward / draft)
 *   - `emailComposerReducer` action transitions (SET_FIELD, attachment
 *     add/remove/toggle, SET_MODE transition matrix, send/draft flags, RESET).
 *
 * Everything under test is pure (no I/O, no platform APIs — ADR-012), so these
 * are ADR-038 domain-logic behavior contracts (MAINTAIN-class), not scaffolding.
 */
import { emailComposerReducer, initialState } from '../EmailComposer.reducer';
import type {
  EmailComposerState,
  IAttachmentItem,
  IEmailComposerProps,
  ISourceCommunicationRecord,
} from '../EmailComposer.types';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const noopFetch = jest.fn();

function baseProps(overrides: Partial<IEmailComposerProps>): IEmailComposerProps {
  return {
    mode: 'compose',
    mount: 'page',
    authenticatedFetch: noopFetch as unknown as IEmailComposerProps['authenticatedFetch'],
    ...overrides,
  };
}

const sourceRecord: ISourceCommunicationRecord = {
  communicationId: 'comm-1',
  from: 'sender@example.com',
  to: ['orig-to@example.com'],
  cc: ['orig-cc@example.com'],
  subject: 'Quarterly review',
  body: '<p>Original body</p>',
  bodyFormat: 'HTML',
  sentAt: '2026-07-01T10:00:00Z',
  attachments: [{ id: 'att-1', source: 'related', fileName: 'contract.pdf', sizeBytes: 2048, documentId: 'doc-1' }],
  associations: [{ entityType: 'sprk_matter', entityId: 'matter-1', entityName: 'Smith v. Jones' }],
};

function att(id: string, overrides: Partial<IAttachmentItem> = {}): IAttachmentItem {
  return { id, source: 'related', fileName: `${id}.pdf`, sizeBytes: 1024, ...overrides };
}

// ---------------------------------------------------------------------------
// initialState
// ---------------------------------------------------------------------------

describe('initialState — per-mode seeding', () => {
  it('compose seeds from initial* props and starts clean/editable', () => {
    const state = initialState(
      baseProps({
        mode: 'compose',
        initialTo: ['a@example.com'],
        initialCc: ['c@example.com'],
        initialSubject: 'Hello',
        initialBody: '<p>Hi</p>',
      })
    );

    expect(state.mode).toBe('compose');
    expect(state.to.map(r => r.email)).toEqual(['a@example.com']);
    expect(state.cc.map(r => r.email)).toEqual(['c@example.com']);
    expect(state.subject).toBe('Hello');
    expect(state.body).toBe('<p>Hi</p>');
    expect(state.readOnly).toBe(false);
    expect(state.isDirty).toBe(false);
    expect(state.isSending).toBe(false);
    expect(state.validation.ok).toBe(true);
  });

  it('compose defaults archiveToSpe to true and bodyFormat to HTML', () => {
    const state = initialState(baseProps({ mode: 'compose' }));
    expect(state.archiveToSpe).toBe(true);
    expect(state.bodyFormat).toBe('HTML');
    expect(state.sendMode).toBe('sharedMailbox');
  });

  it('compose maps wizardContext uploaded files to pre-selected attachments', () => {
    const state = initialState(
      baseProps({
        mode: 'compose',
        wizardContext: {
          uploadedFiles: [
            {
              documentId: 'doc-9',
              driveItemId: 'drive-9',
              fileName: 'brief.docx',
              mimeType: 'application/msword',
              sizeBytes: 4096,
            },
          ],
        },
      })
    );
    expect(state.attachments).toHaveLength(1);
    expect(state.attachments[0]).toMatchObject({ source: 'wizard', documentId: 'doc-9', selected: true });
  });

  it('view derives read-only state from sourceRecord', () => {
    const state = initialState(baseProps({ mode: 'view', sourceRecord }));
    expect(state.readOnly).toBe(true);
    expect(state.subject).toBe('Quarterly review');
    expect(state.to.map(r => r.email)).toEqual(['orig-to@example.com']);
    expect(state.attachments).toHaveLength(1);
  });

  it('reply seeds To from the original sender, blanks body, prefixes Re:', () => {
    const state = initialState(baseProps({ mode: 'reply', sourceRecord }));
    expect(state.to.map(r => r.email)).toEqual(['sender@example.com']);
    expect(state.cc).toEqual([]);
    expect(state.subject).toBe('Re: Quarterly review');
    expect(state.body).toBe('');
    expect(state.readOnly).toBe(false);
    expect(state.associations).toHaveLength(1);
  });

  it('forward blanks To, prefixes Fwd:, carries attachments pre-selected', () => {
    const state = initialState(baseProps({ mode: 'forward', sourceRecord }));
    expect(state.to).toEqual([]);
    expect(state.subject).toBe('Fwd: Quarterly review');
    expect(state.body).toContain('Forwarded message');
    expect(state.attachments).toHaveLength(1);
    expect(state.attachments[0].selected).toBe(true);
  });

  it('draft rehydrates To/Cc/subject/body editable from the draft record', () => {
    const state = initialState(baseProps({ mode: 'draft', sourceRecord }));
    expect(state.to.map(r => r.email)).toEqual(['orig-to@example.com']);
    expect(state.cc.map(r => r.email)).toEqual(['orig-cc@example.com']);
    expect(state.subject).toBe('Quarterly review');
    expect(state.readOnly).toBe(false);
    expect(state.communicationId).toBe('comm-1');
  });

  it('non-compose mode without a sourceRecord falls back to the base (compose-like) state', () => {
    const state = initialState(baseProps({ mode: 'reply', sourceRecord: undefined }));
    expect(state.subject).toBe('');
    expect(state.to).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// emailComposerReducer
// ---------------------------------------------------------------------------

describe('emailComposerReducer', () => {
  const start = (): EmailComposerState => initialState(baseProps({ mode: 'compose' }));

  it('SET_FIELD updates the field and marks dirty', () => {
    const next = emailComposerReducer(start(), { type: 'SET_FIELD', field: 'subject', value: 'New subject' });
    expect(next.subject).toBe('New subject');
    expect(next.isDirty).toBe(true);
  });

  it('SET_BODY_FORMAT switches the format and marks dirty', () => {
    const next = emailComposerReducer(start(), { type: 'SET_BODY_FORMAT', value: 'PlainText' });
    expect(next.bodyFormat).toBe('PlainText');
    expect(next.isDirty).toBe(true);
  });

  it('SET_RECIPIENTS replaces the addressed field', () => {
    const next = emailComposerReducer(start(), {
      type: 'SET_RECIPIENTS',
      field: 'to',
      value: [{ email: 'x@example.com', resolved: false }],
    });
    expect(next.to.map(r => r.email)).toEqual(['x@example.com']);
    expect(next.isDirty).toBe(true);
  });

  it('ADD_ATTACHMENT appends and REMOVE_ATTACHMENT drops by id', () => {
    const added = emailComposerReducer(start(), { type: 'ADD_ATTACHMENT', item: att('a1') });
    expect(added.attachments.map(a => a.id)).toEqual(['a1']);

    const removed = emailComposerReducer(added, { type: 'REMOVE_ATTACHMENT', id: 'a1' });
    expect(removed.attachments).toEqual([]);
    expect(removed.isDirty).toBe(true);
  });

  it('TOGGLE_ATTACHMENT_SELECTED flips selected between undefined/true and false', () => {
    const withAtt: EmailComposerState = { ...start(), attachments: [att('a1', { selected: true })] };
    const off = emailComposerReducer(withAtt, { type: 'TOGGLE_ATTACHMENT_SELECTED', id: 'a1' });
    expect(off.attachments[0].selected).toBe(false);
    const on = emailComposerReducer(off, { type: 'TOGGLE_ATTACHMENT_SELECTED', id: 'a1' });
    expect(on.attachments[0].selected).toBe(true);
  });

  describe('SET_MODE transition matrix', () => {
    const viewState = (): EmailComposerState => initialState(baseProps({ mode: 'view', sourceRecord }));

    it('view → reply clears readOnly and applies the reply patch', () => {
      const next = emailComposerReducer(viewState(), {
        type: 'SET_MODE',
        mode: 'reply',
        patch: { subject: 'Re: Quarterly review', to: [{ email: 'sender@example.com' }] },
      });
      expect(next.mode).toBe('reply');
      expect(next.readOnly).toBe(false);
      expect(next.subject).toBe('Re: Quarterly review');
    });

    it('view → forward clears readOnly', () => {
      const next = emailComposerReducer(viewState(), { type: 'SET_MODE', mode: 'forward' });
      expect(next.mode).toBe('forward');
      expect(next.readOnly).toBe(false);
    });

    it('view → compose clears readOnly', () => {
      const next = emailComposerReducer(viewState(), { type: 'SET_MODE', mode: 'compose' });
      expect(next.mode).toBe('compose');
      expect(next.readOnly).toBe(false);
    });

    it('compose → draft keeps editable and sets draft mode', () => {
      const next = emailComposerReducer(start(), { type: 'SET_MODE', mode: 'draft' });
      expect(next.mode).toBe('draft');
      expect(next.readOnly).toBe(false);
    });

    it('any → view sets readOnly true', () => {
      const next = emailComposerReducer(start(), { type: 'SET_MODE', mode: 'view' });
      expect(next.mode).toBe('view');
      expect(next.readOnly).toBe(true);
    });
  });

  it('BEGIN_SEND / END_SEND toggle isSending', () => {
    const sending = emailComposerReducer(start(), { type: 'BEGIN_SEND' });
    expect(sending.isSending).toBe(true);
    const done = emailComposerReducer(sending, { type: 'END_SEND' });
    expect(done.isSending).toBe(false);
  });

  it('BEGIN_SAVE_DRAFT sets flag; END_SAVE_DRAFT clears flag and dirty', () => {
    const dirty: EmailComposerState = { ...start(), isDirty: true };
    const saving = emailComposerReducer(dirty, { type: 'BEGIN_SAVE_DRAFT' });
    expect(saving.isSavingDraft).toBe(true);
    const saved = emailComposerReducer(saving, { type: 'END_SAVE_DRAFT' });
    expect(saved.isSavingDraft).toBe(false);
    expect(saved.isDirty).toBe(false);
  });

  it('SET_VALIDATION_ERRORS stores the result without touching dirty', () => {
    const result = { ok: false, errors: [{ field: 'to' as const, code: 'TO_REQUIRED' as const, message: 'x' }] };
    const next = emailComposerReducer(start(), { type: 'SET_VALIDATION_ERRORS', result });
    expect(next.validation).toBe(result);
    expect(next.isDirty).toBe(false);
  });

  it('RESET replaces the entire state with the provided snapshot', () => {
    const replacement = initialState(baseProps({ mode: 'reply', sourceRecord }));
    const next = emailComposerReducer(start(), { type: 'RESET', state: replacement });
    expect(next).toBe(replacement);
    expect(next.mode).toBe('reply');
  });

  it('unknown action returns the same state reference (no-op default)', () => {
    const s = start();
    // @ts-expect-error — exercising the reducer default branch with an unknown action
    const next = emailComposerReducer(s, { type: 'NOPE' });
    expect(next).toBe(s);
  });
});
