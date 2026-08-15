/**
 * EmailWorkspace.mapping.test.ts
 *
 * Unit tests for the card-row review-dot wiring in `mapRowToEmailCardItem`
 * (owner UAT 2026-07-30 R2 item 5). The left-list status dot went missing
 * because the "does this row carry association data?" gate ignored the
 * DENORMALIZED `sprk_regardingrecordname`/`sprk_regardingrecordnumber` columns —
 * the ones a maker most commonly puts on an inbox saved view. These tests pin the
 * fix: a filed email whose view surfaces only the Related-to name now derives a
 * `reviewTone`, while a row with no association columns still derives none (so a
 * view that selects no association data never renders a misleading all-red list).
 */
import {
  mapRowToEmailCardItem,
  deriveEmailWorkspaceVisibleState,
  EMAIL_VISIBLE_SNIPPET_CAP_CHARS,
  type EmailWorkspaceRecordState,
} from '../EmailWorkspace.mapping';

const EMAIL_TYPE = 100000000;

const BASE_ROW = {
  sprk_communicationid: 'c1',
  sprk_from: 'jane.doe@example.com',
  sprk_subject: 'Quarterly filing update',
  sprk_communicationtype: EMAIL_TYPE,
} as const;

describe('mapRowToEmailCardItem — review dot wiring (owner UAT R2 item 5)', () => {
  it('derives a reviewTone from the DENORMALIZED regarding name alone (the regression fix)', () => {
    const item = mapRowToEmailCardItem({
      ...BASE_ROW,
      // No status / provenance / typed lookup columns — only the denorm name+number,
      // exactly what an inbox view's "Related to" column surfaces.
      sprk_regardingrecordname: 'Acme v Beta',
      sprk_regardingrecordnumber: 'MAT-1',
    });
    expect(item.reviewTone).toBeDefined();
  });

  it('derives a reviewTone from an association status column', () => {
    const item = mapRowToEmailCardItem({ ...BASE_ROW, sprk_associationstatus: 100000000 });
    expect(item.reviewTone).toBeDefined();
  });

  it('derives NO reviewTone (no dot) when the row carries no association data at all', () => {
    const item = mapRowToEmailCardItem({ ...BASE_ROW });
    expect(item.reviewTone).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// FR-C1 (task 042b) — deriveEmailWorkspaceVisibleState (Path 1 persisted carrier)
// ---------------------------------------------------------------------------

function buildRecordState(overrides: Partial<EmailWorkspaceRecordState> = {}): EmailWorkspaceRecordState {
  return {
    subject: 'Quarterly filing update',
    from: 'jane.doe@example.com',
    to: null,
    cc: null,
    bcc: null,
    sentAt: '2026-08-01T09:00:00Z',
    receivedDate: '2026-08-02T10:30:00Z',
    sprk_body: '<p>Please review the attached <b>filing</b> before Friday.</p>',
    regardingRecordName: null,
    regardingRecordNumber: null,
    regardingRecordType: null,
    associationStatus: null,
    associationProvenanceJson: null,
    filedAssociations: [],
    monitor: false,
    highPriority: false,
    accessPermission: null,
    ...overrides,
  };
}

describe('deriveEmailWorkspaceVisibleState — compact persisted-carrier shape (FR-C1)', () => {
  it('derives subject/from/date + a plain-text snippet + the eml handle + the communicationId from the resolved record', () => {
    const state = deriveEmailWorkspaceVisibleState(buildRecordState(), 'eml-doc-1', 'c1');
    expect(state).not.toBeNull();
    expect(state!.subject).toBe('Quarterly filing update');
    expect(state!.from).toBe('jane.doe@example.com');
    // date prefers receivedDate over sentAt
    expect(state!.date).toBe('2026-08-02T10:30:00Z');
    // eml handle carried through (fetch handle for on-demand eml-render, FR-C4)
    expect(state!.emlDocumentId).toBe('eml-doc-1');
    // communicationId — the active-item conduit id-handle anchor (task 012, FR-05)
    expect(state!.communicationId).toBe('c1');
    // snippet is plain text (tags stripped), non-empty
    expect(state!.snippet).toContain('Please review the attached');
    expect(state!.snippet).not.toContain('<');
    // no thread column exists on sprk_communication today → omitted
    expect(state!.threadId).toBeUndefined();
  });

  it('falls back to sentAt when receivedDate is absent', () => {
    const state = deriveEmailWorkspaceVisibleState(buildRecordState({ receivedDate: null }), null, 'c1');
    expect(state!.date).toBe('2026-08-01T09:00:00Z');
    expect(state!.emlDocumentId).toBeNull();
  });

  it('caps the snippet at EMAIL_VISIBLE_SNIPPET_CAP_CHARS', () => {
    const long = 'x'.repeat(1000);
    const state = deriveEmailWorkspaceVisibleState(buildRecordState({ sprk_body: `<p>${long}</p>` }), null, 'c1');
    expect(state!.snippet!.length).toBeLessThanOrEqual(EMAIL_VISIBLE_SNIPPET_CAP_CHARS + 1); // +1 tolerates an ellipsis suffix
  });

  it('omits snippet when the body is empty (privacy default)', () => {
    const state = deriveEmailWorkspaceVisibleState(buildRecordState({ sprk_body: '' }), null, 'c1');
    expect(state!.snippet).toBeUndefined();
  });

  it('returns null when nothing is selected / the read is loading or failed (recordState null)', () => {
    expect(deriveEmailWorkspaceVisibleState(null, 'eml-doc-1', 'c1')).toBeNull();
  });

  it('returns null when the identity minimum (subject/from/date) is incomplete — never persists a carrier the agent-visible derivation would reject', () => {
    expect(deriveEmailWorkspaceVisibleState(buildRecordState({ subject: null }), null, 'c1')).toBeNull();
    expect(deriveEmailWorkspaceVisibleState(buildRecordState({ from: null }), null, 'c1')).toBeNull();
    expect(
      deriveEmailWorkspaceVisibleState(buildRecordState({ sentAt: null, receivedDate: null }), null, 'c1')
    ).toBeNull();
  });

  // spaarkeai-assistant-enhancements-r3 task 012 (FR-05) — negative case: no id → no handle.
  // A conduit id-handle cannot be constructed without an id; mirrors the identity-minimum gate above.
  it('returns null when communicationId is absent/empty — cannot construct an id handle without an id', () => {
    expect(deriveEmailWorkspaceVisibleState(buildRecordState(), 'eml-doc-1', null)).toBeNull();
    expect(deriveEmailWorkspaceVisibleState(buildRecordState(), 'eml-doc-1', undefined)).toBeNull();
    expect(deriveEmailWorkspaceVisibleState(buildRecordState(), 'eml-doc-1', '')).toBeNull();
  });
});
