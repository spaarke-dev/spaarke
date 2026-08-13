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
import {
  emailComposerReducer,
  initialState,
  mapStateToSendRequest,
  buildBodyWithAttachmentLinks,
  deriveReplyState,
  deriveForwardState,
} from '../EmailComposer.reducer';
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

  it('reply seeds To from the original sender, quotes the thread, prefixes Re:', () => {
    const state = initialState(baseProps({ mode: 'reply', sourceRecord }));
    expect(state.to.map(r => r.email)).toEqual(['sender@example.com']);
    expect(state.cc).toEqual([]);
    expect(state.subject).toBe('Re: Quarterly review');
    // Reply now quotes the previous thread (owner UAT R5 item 1) and stores it on quotedThread.
    expect(state.body).toContain('wrote:');
    expect(state.quotedThread).toBeTruthy();
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

  // D-5 fix (spaarkeai-assistant-enhancements-r3 task 025, hand-off from task 024
  // Step 9.5): the PROPS-ONLY path (no `sourceRecord` — `useEmailComposeActions`)
  // must seed `state.quotedThread` from `props.initialQuotedThread` so the
  // in-dialog AI sparkle's re-append (`runAiDraft`) does not drop an already-
  // seeded quoted thread on the first re-draft.
  it('D-5: reply/forward WITHOUT sourceRecord seeds quotedThread from initialQuotedThread', () => {
    const quoted = '<p>On Jul 1, sender@example.com wrote:</p><hr/><p>Original body</p>';
    const state = initialState(
      baseProps({
        mode: 'reply',
        sourceRecord: undefined,
        initialBody: `<p>AI draft</p><p></p>${quoted}`,
        initialQuotedThread: quoted,
      })
    );
    expect(state.quotedThread).toBe(quoted);
    expect(state.body).toContain(quoted);
  });

  it('D-5: quotedThread stays undefined when initialQuotedThread is omitted (no regression for existing callers)', () => {
    const state = initialState(baseProps({ mode: 'compose', initialBody: '<p>Hi</p>' }));
    expect(state.quotedThread).toBeUndefined();
  });

  it('D-5: when sourceRecord IS present, its derived quotedThread wins over initialQuotedThread (unchanged sourceRecord-path behavior)', () => {
    const state = initialState(
      baseProps({ mode: 'reply', sourceRecord, initialQuotedThread: '<p>should be ignored</p>' })
    );
    expect(state.quotedThread).not.toBe('<p>should be ignored</p>');
    expect(state.quotedThread).toContain('Original body');
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

  it('TOGGLE_ATTACHMENT_LINK flips linkSelected (task 104)', () => {
    const withAtt: EmailComposerState = { ...start(), attachments: [att('a1', { linkUrl: 'https://x/doc' })] };
    const on = emailComposerReducer(withAtt, { type: 'TOGGLE_ATTACHMENT_LINK', id: 'a1' });
    expect(on.attachments[0].linkSelected).toBe(true);
    expect(on.isDirty).toBe(true);
    const off = emailComposerReducer(on, { type: 'TOGGLE_ATTACHMENT_LINK', id: 'a1' });
    expect(off.attachments[0].linkSelected).toBe(false);
  });

  it('RESOLVE_ATTACHMENT_DOCUMENT patches documentId + driveItemId + linkUrl (item 9b)', () => {
    const withAtt: EmailComposerState = { ...start(), attachments: [att('l1', { source: 'local' })] };
    const resolved = emailComposerReducer(withAtt, {
      type: 'RESOLVE_ATTACHMENT_DOCUMENT',
      id: 'l1',
      documentId: 'doc-guid',
      driveItemId: 'drive-item',
      linkUrl: 'https://spe/doc',
    });
    expect(resolved.attachments[0].documentId).toBe('doc-guid');
    expect(resolved.attachments[0].driveItemId).toBe('drive-item');
    // linkUrl lights up the per-row Link toggle for an uploaded local file.
    expect(resolved.attachments[0].linkUrl).toBe('https://spe/doc');
  });

  it('RESOLVE_ATTACHMENT_DOCUMENT without linkUrl preserves any existing linkUrl (no clobber)', () => {
    const withAtt: EmailComposerState = {
      ...start(),
      attachments: [att('l1', { source: 'local', linkUrl: 'https://spe/existing' })],
    };
    const resolved = emailComposerReducer(withAtt, {
      type: 'RESOLVE_ATTACHMENT_DOCUMENT',
      id: 'l1',
      documentId: 'doc-guid',
    });
    expect(resolved.attachments[0].linkUrl).toBe('https://spe/existing');
  });

  it('ADD_ASSOCIATION appends (dedup) and REMOVE_ASSOCIATION drops by entityType+entityId (item 10)', () => {
    const a1 = { entityType: 'sprk_matter', entityId: 'm-1', entityName: 'Smith v. Jones' };
    const added = emailComposerReducer(start(), { type: 'ADD_ASSOCIATION', association: a1 });
    expect(added.associations).toHaveLength(1);
    expect(added.isDirty).toBe(true);

    // Re-adding the same record is a no-op (dedup on entityType+entityId).
    const dup = emailComposerReducer(added, { type: 'ADD_ASSOCIATION', association: { ...a1, entityId: 'M-1' } });
    expect(dup.associations).toHaveLength(1);

    const removed = emailComposerReducer(added, {
      type: 'REMOVE_ASSOCIATION',
      entityType: 'sprk_matter',
      entityId: 'M-1', // case-insensitive match
    });
    expect(removed.associations).toEqual([]);
    expect(removed.isDirty).toBe(true);
  });

  describe('SET_PRIMARY_ASSOCIATION (item 8 — single-primary "Related to")', () => {
    const withAssociations = (associations: EmailComposerState['associations']): EmailComposerState => ({
      ...start(),
      associations,
    });

    it('promotes the picked record to index 0, REPLACING the old primary, and marks dirty', () => {
      const oldPrimary = { entityType: 'sprk_matter', entityId: 'm-1', entityName: 'Old Primary' };
      const picked = { entityType: 'sprk_matter', entityId: 'm-2', entityName: 'New Primary' };
      const next = emailComposerReducer(withAssociations([oldPrimary]), {
        type: 'SET_PRIMARY_ASSOCIATION',
        association: picked,
      });
      // Picked is index 0 (primary); the old index-0 primary is dropped (single-primary model).
      expect(next.associations).toEqual([picked]);
      expect(next.associations[0].entityId).toBe('m-2');
      expect(next.isDirty).toBe(true);
    });

    it('preserves SECONDARY associations (index 1+) while replacing only the primary', () => {
      const oldPrimary = { entityType: 'sprk_matter', entityId: 'm-1', entityName: 'Old Primary' };
      const secondaryA = { entityType: 'sprk_document', entityId: 'd-1', entityName: 'Doc A' };
      const secondaryB = { entityType: 'contact', entityId: 'c-1', entityName: 'Contact B' };
      const picked = { entityType: 'sprk_matter', entityId: 'm-9', entityName: 'New Primary' };
      const next = emailComposerReducer(withAssociations([oldPrimary, secondaryA, secondaryB]), {
        type: 'SET_PRIMARY_ASSOCIATION',
        association: picked,
      });
      expect(next.associations).toEqual([picked, secondaryA, secondaryB]);
    });

    it('DEDUPS the picked record out of the preserved secondaries (case-insensitive, braces stripped)', () => {
      const oldPrimary = { entityType: 'sprk_matter', entityId: 'm-1', entityName: 'Old Primary' };
      // Same record as `picked` but as a secondary, with different case + braces — must be deduped.
      const dupSecondary = { entityType: 'SPRK_MATTER', entityId: '{M-9}', entityName: 'Dup' };
      const keepSecondary = { entityType: 'sprk_document', entityId: 'd-1', entityName: 'Doc A' };
      const picked = { entityType: 'sprk_matter', entityId: 'm-9', entityName: 'New Primary' };
      const next = emailComposerReducer(withAssociations([oldPrimary, dupSecondary, keepSecondary]), {
        type: 'SET_PRIMARY_ASSOCIATION',
        association: picked,
      });
      expect(next.associations).toEqual([picked, keepSecondary]);
    });

    it('works from the empty state (no existing primary to replace)', () => {
      const picked = { entityType: 'sprk_matter', entityId: 'm-1', entityName: 'First Primary' };
      const next = emailComposerReducer(withAssociations([]), {
        type: 'SET_PRIMARY_ASSOCIATION',
        association: picked,
      });
      expect(next.associations).toEqual([picked]);
      expect(next.isDirty).toBe(true);
    });
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

// ---------------------------------------------------------------------------
// Task 104 — reply/forward attachment inclusion (D1/D2)
// ---------------------------------------------------------------------------

describe('task 104 — attachment inclusion on reply/forward', () => {
  const stateWith = (
    attachments: IAttachmentItem[],
    overrides: Partial<EmailComposerState> = {}
  ): EmailComposerState => ({
    ...initialState(baseProps({ mode: 'compose' })),
    attachments,
    ...overrides,
  });

  describe('initialState with initialAttachments — per-mode default', () => {
    const carried: IAttachmentItem[] = [
      att('src-1', { source: 'related', documentId: 'doc-1', linkUrl: 'https://x/doc-1' }),
    ];

    it('forward defaults carried attachments to attach-file ON', () => {
      const state = initialState(baseProps({ mode: 'forward', initialAttachments: carried }));
      expect(state.attachments).toHaveLength(1);
      expect(state.attachments[0].selected).toBe(true);
      expect(state.attachments[0].linkSelected).toBe(false);
    });

    it('reply offers carried attachments but defaults attach-file OFF', () => {
      const state = initialState(baseProps({ mode: 'reply', initialAttachments: carried }));
      expect(state.attachments).toHaveLength(1);
      expect(state.attachments[0].selected).toBe(false);
    });

    it('honors an explicit selected override from the host', () => {
      const state = initialState(baseProps({ mode: 'reply', initialAttachments: [{ ...carried[0], selected: true }] }));
      expect(state.attachments[0].selected).toBe(true);
    });
  });

  describe('deriveReplyState / deriveForwardState (sourceRecord path)', () => {
    const source: ISourceCommunicationRecord = {
      communicationId: 'c1',
      from: 'a@x.com',
      to: ['b@x.com'],
      subject: 'Hi',
      body: '<p>b</p>',
      bodyFormat: 'HTML',
      attachments: [att('s1', { source: 'related', documentId: 'doc-1' })],
    };

    it('forward carries source attachments pre-selected', () => {
      const patch = deriveForwardState(source);
      expect(patch.attachments?.[0].selected).toBe(true);
    });

    it('reply carries source attachments offered-but-off', () => {
      const patch = deriveReplyState(source);
      expect(patch.attachments).toHaveLength(1);
      expect(patch.attachments?.[0].selected).toBe(false);
    });
  });

  describe('mapStateToSendRequest — selection → request payload', () => {
    it('threads only kept, resolved attachments into attachmentDocumentIds (forward include)', () => {
      const req = mapStateToSendRequest(
        stateWith([
          att('a1', { documentId: 'doc-1', selected: true }),
          att('a2', { documentId: 'doc-2', selected: true }),
        ])
      );
      expect(req.attachmentDocumentIds).toEqual(['doc-1', 'doc-2']);
    });

    it('excludes deselected attachments (reply default exclude)', () => {
      const req = mapStateToSendRequest(
        stateWith([
          att('a1', { documentId: 'doc-1', selected: false }),
          att('a2', { documentId: 'doc-2', selected: true }),
        ])
      );
      expect(req.attachmentDocumentIds).toEqual(['doc-2']);
    });

    it('excludes attachments without a resolved documentId (e.g. local picks)', () => {
      const req = mapStateToSendRequest(stateWith([att('a1', { selected: true })]));
      expect(req.attachmentDocumentIds).toEqual([]);
    });

    it('appends chosen document links to the HTML body (link path)', () => {
      const req = mapStateToSendRequest(
        stateWith(
          [
            att('a1', {
              fileName: 'contract.pdf',
              documentId: 'doc-1',
              selected: false,
              linkSelected: true,
              linkUrl: 'https://x/doc-1',
            }),
          ],
          {
            body: '<p>See below.</p>',
            bodyFormat: 'HTML',
          }
        )
      );
      expect(req.body).toContain('<p>See below.</p>');
      expect(req.body).toContain('href="https://x/doc-1"');
      expect(req.body).toContain('contract.pdf');
      // link-only, not attach-file:
      expect(req.attachmentDocumentIds).toEqual([]);
    });
  });

  describe('buildBodyWithAttachmentLinks', () => {
    it('returns the body unchanged when nothing is link-selected', () => {
      expect(buildBodyWithAttachmentLinks('<p>x</p>', 'HTML', [att('a1', { linkUrl: 'https://x' })])).toBe('<p>x</p>');
    });

    it('skips link-selected items missing a linkUrl (best-effort)', () => {
      const out = buildBodyWithAttachmentLinks('<p>x</p>', 'HTML', [att('a1', { linkSelected: true })]);
      expect(out).toBe('<p>x</p>');
    });

    it('formats a plain-text link block', () => {
      const out = buildBodyWithAttachmentLinks('Body', 'PlainText', [
        att('a1', { fileName: 'a.pdf', linkSelected: true, linkUrl: 'https://x/a' }),
      ]);
      expect(out).toContain('Linked documents:');
      expect(out).toContain('- a.pdf: https://x/a');
    });

    it('escapes HTML in the file name and URL', () => {
      const out = buildBodyWithAttachmentLinks('', 'HTML', [
        att('a1', { fileName: 'a&b.pdf', linkSelected: true, linkUrl: 'https://x/a?q=1&r=2' }),
      ]);
      expect(out).toContain('a&amp;b.pdf');
      expect(out).toContain('q=1&amp;r=2');
    });
  });
});
