/**
 * Unit tests for `applyStampPrecedence` — spaarkeai-word-add-in-r1 task 051 (FR-02 client half).
 * A NEW test file (ADR-038) — `documentIdentityService.test.ts` is GATED and stays untouched; this
 * file is scoped narrowly to the owner's 2026-09-17 binding precedence.
 *
 * The precedence under test (task 014's note §9, as amended by the 2026-09-17 owner-decisions
 * block — supersedes task 014's POML step 5 "prefer the stamp"):
 *   1. A cloud URL that resolves (012 `resolved`) WINS.
 *   2. The stamp is used ONLY when 012 answers `new` (whose only three reasons are
 *      `not_cloud_document` / `not_resolvable` / `not_spaarke_document`).
 *   3. A resolved URL whose id DISAGREES with the stamp is `identity_conflict` — represented here by
 *      the EXISTING `kind: 'conflict'` outcome (see the function's own doc comment for why reusing
 *      it, rather than adding a new kind, is correct).
 *
 * A pure function — no Office.js, no network — so every case is a plain input/output assertion.
 */
import { applyStampPrecedence, type DocumentIdentityOutcome } from '../documentIdentityService';

const RESOLVED_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6';
const OTHER_ID = '11111111-1111-1111-1111-111111111111';

const RESOLVED: DocumentIdentityOutcome = {
  kind: 'resolved',
  documentId: RESOLVED_ID,
  documentName: 'Settlement Agreement',
  fileName: 'Settlement Agreement.docx',
  relatedRecord: { entityType: 'sprk_matter', id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', name: 'M-0042' },
};

const NEW_NOT_CLOUD: DocumentIdentityOutcome = { kind: 'new', reason: 'not_cloud_document' };
const NEW_NOT_RESOLVABLE: DocumentIdentityOutcome = { kind: 'new', reason: 'not_resolvable' };
const NEW_NOT_SPAARKE: DocumentIdentityOutcome = { kind: 'new', reason: 'not_spaarke_document' };
const CONFLICT: DocumentIdentityOutcome = { kind: 'conflict' };
const INDETERMINATE: DocumentIdentityOutcome = { kind: 'indeterminate', reason: 'unavailable' };
const DENIED: DocumentIdentityOutcome = { kind: 'denied' };
const ERROR: DocumentIdentityOutcome = { kind: 'error', message: 'network exploded' };

describe('applyStampPrecedence (owner precedence, task 051)', () => {
  describe('1) a resolved cloud URL WINS', () => {
    it('AC2: same id in URL and stamp — resolved, unchanged, SAME reference (the result is identical either way)', () => {
      const result = applyStampPrecedence(RESOLVED, RESOLVED_ID);

      expect(result).toBe(RESOLVED);
      expect(result.kind).toBe('resolved');
    });

    it('AC2 (braced/uppercase stamp): still recognized as the SAME id after canonicalization — resolved, unchanged', () => {
      const result = applyStampPrecedence(RESOLVED, '{3FA85F64-5717-4562-B3FC-2C963F66AFA6}');

      expect(result).toBe(RESOLVED);
    });

    it('no stamp at all (null) — resolved, unchanged, SAME reference (exactly as before this task)', () => {
      const result = applyStampPrecedence(RESOLVED, null);

      expect(result).toBe(RESOLVED);
    });

    it('AC3: resolved URL whose id DISAGREES with the stamp — identity_conflict (kind: "conflict"), no default save target', () => {
      const result = applyStampPrecedence(RESOLVED, OTHER_ID);

      expect(result).toEqual({ kind: 'conflict' });
    });
  });

  describe('2) the stamp is the fallback, but ONLY for the three named "new" reasons', () => {
    it('AC1: not_cloud_document + a valid stamp — resolves to the stamp id, with an honest empty display (no Graph round-trip: this is a pure function, no network exists to call)', () => {
      const result = applyStampPrecedence(NEW_NOT_CLOUD, RESOLVED_ID);

      expect(result).toEqual({
        kind: 'resolved',
        documentId: RESOLVED_ID,
        documentName: '',
        fileName: '',
        relatedRecord: null,
      });
    });

    it('not_resolvable + a valid stamp — also resolves via the stamp', () => {
      const result = applyStampPrecedence(NEW_NOT_RESOLVABLE, RESOLVED_ID);

      expect(result).toEqual({
        kind: 'resolved',
        documentId: RESOLVED_ID,
        documentName: '',
        fileName: '',
        relatedRecord: null,
      });
    });

    it('not_spaarke_document + a valid stamp — also resolves via the stamp', () => {
      const result = applyStampPrecedence(NEW_NOT_SPAARKE, RESOLVED_ID);

      expect(result).toEqual({
        kind: 'resolved',
        documentId: RESOLVED_ID,
        documentName: '',
        fileName: '',
        relatedRecord: null,
      });
    });

    it('canonicalizes a braced/uppercase stamp id before it lands in the outcome (ADR-044)', () => {
      const result = applyStampPrecedence(NEW_NOT_CLOUD, '{3FA85F64-5717-4562-B3FC-2C963F66AFA6}');

      expect(result).toMatchObject({ kind: 'resolved', documentId: RESOLVED_ID });
    });

    it('AC4: "new" with NO stamp (null) — unchanged, SAME reference (behaviour exactly as before this task)', () => {
      const result = applyStampPrecedence(NEW_NOT_CLOUD, null);

      expect(result).toBe(NEW_NOT_CLOUD);
    });
  });

  describe('3) every other outcome is untouched — the stamp is consulted for exactly the three "new" reasons, nothing else', () => {
    it.each([
      ["conflict (012's own different-drive collision)", CONFLICT],
      ['indeterminate', INDETERMINATE],
      ['denied', DENIED],
      ['error', ERROR],
    ])('%s + a valid stamp — unchanged, SAME reference (the stamp is never consulted here)', (_label, outcome) => {
      const result = applyStampPrecedence(outcome, RESOLVED_ID);

      expect(result).toBe(outcome);
    });
  });

  it("never throws for a malformed-looking stamp string — that is readDocumentStamp()'s job to have already filtered; this function only compares/canonicalizes", () => {
    expect(() => applyStampPrecedence(NEW_NOT_CLOUD, '')).not.toThrow();
    // An empty string is falsy, so it is treated the same as null — no stamp to fall back to.
    expect(applyStampPrecedence(NEW_NOT_CLOUD, '')).toBe(NEW_NOT_CLOUD);
  });
});
