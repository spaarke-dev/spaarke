/**
 * Task 025 (spaarkeai-word-add-in-r1): `describeCollisionFailure` — the mapper for a refused CREATE
 * save's filename collision (OFFICE_020). Pure-function tests, independent of the end-to-end
 * `SaveFlowCollision.test.tsx` render suite: these pin the exact data shape the pane's two-option
 * choice depends on (`offerCollisionChoice`, `collisionFileName`, `collisionExistingDocumentId`).
 *
 * NEW FILE — no existing `errorMessages.test.ts` in this package to conflict with or extend.
 */
import { describeCollisionFailure, type ProblemDetails } from '../errorMessages';

const baseProblem: ProblemDetails = {
  type: 'https://spaarke.com/errors/office/conflict',
  title: 'File Already Exists',
  status: 409,
  detail: 'A file named "Brief.docx" already exists here. Nothing was uploaded or changed.',
  errorCode: 'OFFICE_020',
  fileName: 'Brief.docx',
  existingDocumentId: '11aed095-30da-f011-8406-7ced8d1dc988',
};

describe('describeCollisionFailure', () => {
  it('offers the collision choice and carries the file name + resolved existing document id', () => {
    const result = describeCollisionFailure(baseProblem);

    expect(result.offerCollisionChoice).toBe(true);
    expect(result.collisionFileName).toBe('Brief.docx');
    expect(result.collisionExistingDocumentId).toBe('11aed095-30da-f011-8406-7ced8d1dc988');
  });

  it('is stated as a neutral choice, never error-red — mirrors the shipped OBO wizard copy convention', () => {
    const result = describeCollisionFailure(baseProblem);

    expect(result.type).toBe('warning');
    expect(result.title).toBe('Name Already Exists');
    expect(result.recoverable).toBe(false); // not a bare "Retry" — the two-option choice IS the retry
  });

  it('prefers the server-supplied detail over the catalog default message', () => {
    const result = describeCollisionFailure(baseProblem);

    expect(result.message).toBe(baseProblem.detail);
  });

  it('omits collisionExistingDocumentId when the server could not resolve an owning document', () => {
    const { existingDocumentId: _drop, ...withoutExistingDocumentId } = baseProblem;

    const result = describeCollisionFailure(withoutExistingDocumentId);

    expect(result.offerCollisionChoice).toBe(true);
    expect(result.collisionExistingDocumentId).toBeUndefined();
    expect(result.collisionFileName).toBe('Brief.docx');
  });

  // ── Task 055 (#1005): the refusal must NAME the document it would write into ────────────────────

  it('carries the existing document NAME so the pane can say which document it would write into', () => {
    // Task 055: before this, the pane offered "Save as new version" against an opaque GUID, so a user had
    // no way to see the target was an unrelated document. The server now supplies its display name
    // (sprk_documentname — NOT the filename; task 020 split them), gated on the caller's read access.
    const result = describeCollisionFailure({
      ...baseProblem,
      existingDocumentName: 'Examiner Report — Canadian Application',
    } as ProblemDetails);

    expect(result.collisionExistingDocumentName).toBe('Examiner Report — Canadian Application');
    expect(result.collisionExistingDocumentId).toBe(baseProblem.existingDocumentId);
  });

  it('omits the name when the server withheld it — the caller cannot read that document', () => {
    // The authorization gate (task 055 §1.3): a caller without Read on the colliding document is told only
    // that the NAME is taken, never what the document is or where it is filed. The id is withheld in the
    // same breath, so the pane falls back to the already-shipped "Keep both only" state.
    const { existingDocumentId: _id, ...withheld } = baseProblem;

    const result = describeCollisionFailure(withheld as ProblemDetails);

    expect(result.offerCollisionChoice).toBe(true);
    expect(result.collisionFileName).toBe('Brief.docx');
    expect(result.collisionExistingDocumentName).toBeUndefined();
    expect(result.collisionExistingDocumentId).toBeUndefined();
  });

  it('leaves any OTHER error code unaffected — safe to call unconditionally on every create-path failure', () => {
    const genericFailure: ProblemDetails = {
      type: 'https://spaarke.com/errors/office/service-error',
      title: 'SPE Upload Failed',
      status: 502,
      detail: 'Failed to upload file to storage',
      errorCode: 'OFFICE_012',
    };

    const result = describeCollisionFailure(genericFailure);

    expect(result.offerCollisionChoice).toBeUndefined();
    expect(result.collisionFileName).toBeUndefined();
    expect(result.collisionExistingDocumentId).toBeUndefined();
    // Falls through to the ordinary catalog mapping, unchanged.
    expect(result.title).toBe('Upload Failed');
  });

  it('falls back to the default error shape for an unrecognized code, still without collision fields', () => {
    const unknown: ProblemDetails = {
      type: 'https://spaarke.com/errors/office/error',
      title: 'Something else',
      status: 500,
      detail: 'An unexpected thing happened',
    };

    const result = describeCollisionFailure(unknown);

    expect(result.offerCollisionChoice).toBeUndefined();
    expect(result.title).toBe('Something else');
  });
});
