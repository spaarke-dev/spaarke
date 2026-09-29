/**
 * composeDraftStore.test.ts — FR-03 (spaarkeai-compose-r7 task 040)
 *
 * Pure unit tests for the CLIENT-ONLY local draft store: save/get/clear semantics, the
 * PER-DOCUMENT slot model (r8 task 016, FR-S09 item 8), logical-id match-gating on
 * read/clear, legacy-slot migration, bounded retention, and best-effort resilience to
 * unavailable/corrupt storage. No React, no network — runs standalone.
 */
import {
  COMPOSE_DRAFT_CONTENT_KEY,
  composeDraftKey,
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

  // FR-S09 item 8 (r8 task 016) — THIS TEST WAS INVERTED, deliberately.
  //
  // It previously asserted `expect(getComposeDraft('lid-1')).toBeNull()` after a second
  // document drafted: the single-slot overwrite was written down as intended behaviour.
  // It is the bug. Two Compose documents open, and the second one's autosave tick silently
  // destroyed the first one's unsaved work — then recovery reported "no draft", because the
  // slot's logicalId no longer matched. The assertion is now the opposite, and it is the
  // regression guard: if a future change reintroduces a shared slot, this fails.
  it('keeps a draft PER DOCUMENT — a second document does not destroy the first', () => {
    saveComposeDraft('lid-1', '<p>first</p>');
    saveComposeDraft('lid-2', '<p>second</p>');
    expect(getComposeDraft('lid-1')?.html).toBe('<p>first</p>');
    expect(getComposeDraft('lid-2')?.html).toBe('<p>second</p>');
    // Distinct keys, not one slot re-labelled.
    expect(window.localStorage.getItem(composeDraftKey('lid-1'))).not.toBeNull();
    expect(window.localStorage.getItem(composeDraftKey('lid-2'))).not.toBeNull();
  });

  it('recovers a draft written into the LEGACY single slot by an earlier build', () => {
    // Exactly what the pre-016 store wrote: the global key, no per-document key.
    window.localStorage.setItem(
      COMPOSE_DRAFT_CONTENT_KEY,
      JSON.stringify({
        logicalId: 'lid-old',
        html: '<p>legacy</p>',
        fileName: 'Old.docx',
        savedAt: '2026-08-01T00:00:00.000Z',
      })
    );
    expect(getComposeDraft('lid-old')?.html).toBe('<p>legacy</p>');
    // Still match-gated: another document must not read it.
    expect(getComposeDraft('lid-other')).toBeNull();
  });

  it('retires the legacy slot once the document it belongs to writes its own', () => {
    window.localStorage.setItem(
      COMPOSE_DRAFT_CONTENT_KEY,
      JSON.stringify({ logicalId: 'lid-1', html: '<p>legacy</p>', savedAt: '2026-08-01T00:00:00.000Z' })
    );
    saveComposeDraft('lid-1', '<p>fresh</p>');
    expect(window.localStorage.getItem(COMPOSE_DRAFT_CONTENT_KEY)).toBeNull();
    expect(getComposeDraft('lid-1')?.html).toBe('<p>fresh</p>');
  });

  it('leaves a legacy slot belonging to a DIFFERENT document alone', () => {
    window.localStorage.setItem(
      COMPOSE_DRAFT_CONTENT_KEY,
      JSON.stringify({ logicalId: 'lid-other', html: '<p>theirs</p>', savedAt: '2026-08-01T00:00:00.000Z' })
    );
    saveComposeDraft('lid-1', '<p>mine</p>');
    expect(getComposeDraft('lid-other')?.html).toBe('<p>theirs</p>');
  });

  // The single-slot model's ONE real merit was bounded growth. Per-document keys keep that
  // property explicitly rather than by collision.
  it('retains a bounded number of drafts, evicting the oldest', () => {
    for (let i = 0; i < 14; i += 1) {
      window.localStorage.setItem(
        composeDraftKey(`lid-${i}`),
        JSON.stringify({
          logicalId: `lid-${i}`,
          html: `<p>${i}</p>`,
          // Ascending timestamps: lid-0 is the oldest.
          savedAt: new Date(Date.UTC(2026, 7, 1, 0, i)).toISOString(),
        })
      );
    }
    // One more write triggers the prune.
    saveComposeDraft('lid-newest', '<p>newest</p>');

    const remaining = Object.keys(window.localStorage).filter(k => k.startsWith(`${COMPOSE_DRAFT_CONTENT_KEY}:`));
    expect(remaining.length).toBe(10);
    // The just-written draft always survives; the oldest do not.
    expect(getComposeDraft('lid-newest')?.html).toBe('<p>newest</p>');
    expect(getComposeDraft('lid-0')).toBeNull();
    expect(getComposeDraft('lid-13')?.html).toBe('<p>13</p>');
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

  it('clear() with no id removes every draft slot', () => {
    saveComposeDraft('lid-1', '<p>a</p>');
    saveComposeDraft('lid-2', '<p>b</p>');
    clearComposeDraft();
    expect(getComposeDraft('lid-1')).toBeNull();
    expect(getComposeDraft('lid-2')).toBeNull();
  });

  it('never persists an empty logical id', () => {
    saveComposeDraft('', '<p>a</p>');
    expect(window.localStorage.getItem(COMPOSE_DRAFT_CONTENT_KEY)).toBeNull();
    expect(window.localStorage.getItem(composeDraftKey(''))).toBeNull();
    expect(window.localStorage.length).toBe(0);
  });

  it('preserves an omitted fileName as undefined (not the string "undefined")', () => {
    saveComposeDraft('lid-1', '<p>a</p>');
    const draft = getComposeDraft('lid-1') as ComposeDraftEntry;
    expect(draft.fileName).toBeUndefined();
  });

  it('returns null for a corrupt (unparseable) slot rather than throwing', () => {
    window.localStorage.setItem(composeDraftKey('lid-1'), '{ not valid json');
    expect(() => getComposeDraft('lid-1')).not.toThrow();
    expect(getComposeDraft('lid-1')).toBeNull();
  });

  it('returns null for a slot missing the html field', () => {
    window.localStorage.setItem(composeDraftKey('lid-1'), JSON.stringify({ logicalId: 'lid-1' }));
    expect(getComposeDraft('lid-1')).toBeNull();
  });
});
