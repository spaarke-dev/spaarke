/**
 * composeDraftStore.test.ts — FR-03 (spaarkeai-compose-r7 task 040)
 *
 * Pure unit tests for the CLIENT-ONLY local draft store: save/get/clear semantics, the
 * single-slot overwrite model, logical-id match-gating on read/clear, and best-effort
 * resilience to unavailable/corrupt storage. No React, no network — runs standalone.
 */
import {
  COMPOSE_DRAFT_CONTENT_KEY,
  saveComposeDraft,
  getComposeDraft,
  clearComposeDraft,
  type ComposeDraftEntry,
} from './composeDraftStore';

describe('composeDraftStore', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it('saves and reads back a draft for the matching logical id', () => {
    saveComposeDraft('lid-1', '<p>hello</p>', 'Contract.docx');
    const draft = getComposeDraft('lid-1');
    expect(draft).not.toBeNull();
    expect(draft?.logicalId).toBe('lid-1');
    expect(draft?.html).toBe('<p>hello</p>');
    expect(draft?.fileName).toBe('Contract.docx');
    expect(typeof draft?.savedAt).toBe('string');
    expect(draft?.savedAt.length).toBeGreaterThan(0);
  });

  it('returns null when the persisted slot belongs to a different logical id', () => {
    saveComposeDraft('lid-1', '<p>a</p>');
    expect(getComposeDraft('lid-2')).toBeNull();
  });

  it('returns null when no draft has been saved', () => {
    expect(getComposeDraft('lid-x')).toBeNull();
  });

  it('is single-slot — the newest save overwrites the previous draft', () => {
    saveComposeDraft('lid-1', '<p>first</p>');
    saveComposeDraft('lid-2', '<p>second</p>');
    // The slot now belongs to lid-2; lid-1 is gone (never grows unbounded).
    expect(getComposeDraft('lid-1')).toBeNull();
    expect(getComposeDraft('lid-2')?.html).toBe('<p>second</p>');
  });

  it('clear(logicalId) removes the draft only when the slot matches that id', () => {
    saveComposeDraft('lid-1', '<p>a</p>');
    // A clear scoped to a DIFFERENT id must leave the slot intact.
    clearComposeDraft('lid-2');
    expect(getComposeDraft('lid-1')?.html).toBe('<p>a</p>');
    // A clear scoped to the OWNING id removes it.
    clearComposeDraft('lid-1');
    expect(getComposeDraft('lid-1')).toBeNull();
  });

  it('clear() with no id removes the slot unconditionally', () => {
    saveComposeDraft('lid-1', '<p>a</p>');
    clearComposeDraft();
    expect(getComposeDraft('lid-1')).toBeNull();
  });

  it('never persists an empty logical id', () => {
    saveComposeDraft('', '<p>a</p>');
    expect(window.localStorage.getItem(COMPOSE_DRAFT_CONTENT_KEY)).toBeNull();
  });

  it('preserves an omitted fileName as undefined (not the string "undefined")', () => {
    saveComposeDraft('lid-1', '<p>a</p>');
    const draft = getComposeDraft('lid-1') as ComposeDraftEntry;
    expect(draft.fileName).toBeUndefined();
  });

  it('returns null for a corrupt (unparseable) slot rather than throwing', () => {
    window.localStorage.setItem(COMPOSE_DRAFT_CONTENT_KEY, '{ not valid json');
    expect(() => getComposeDraft('lid-1')).not.toThrow();
    expect(getComposeDraft('lid-1')).toBeNull();
  });

  it('returns null for a slot missing the html field', () => {
    window.localStorage.setItem(COMPOSE_DRAFT_CONTENT_KEY, JSON.stringify({ logicalId: 'lid-1' }));
    expect(getComposeDraft('lid-1')).toBeNull();
  });
});
