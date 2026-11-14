/**
 * Task 134 slice B2 (ebilling 095f, EB-D-225): the sentence for 400 `ebilling.submission.correction_note_required`.
 */
import {
  SUBMISSION_ERROR_MESSAGES,
  describeSubmissionRefusal,
  isRetryableSubmissionRefusal,
  PERMANENT_SUBMISSION_CODES,
} from '../invoiceSubmissionMessages';

const CODE = 'ebilling.submission.correction_note_required';

describe('correction_note_required (095f)', () => {
  it('must-fire: has its own sentence, exactly', () => {
    expect(SUBMISSION_ERROR_MESSAGES[CODE]).toBeDefined();
    expect(describeSubmissionRefusal({ kind: 'refused', status: 400, code: CODE })).toBe(
      'Enter a correction note saying why the invoice is corrected. Nothing was submitted.'
    );
  });

  it('must-fire: it is not the generic sentence, nor the "details" sentence of a code-less 400', () => {
    const sentence = describeSubmissionRefusal({ kind: 'refused', status: 400, code: CODE });
    expect(sentence).not.toBe("The invoice couldn't be submitted. Nothing was submitted.");
    expect(sentence).not.toMatch(/didn't accept the submission's details/);
  });

  it('must-not-fire: it is a plain refusal (no retry, no upload-in-Spaarke hint), and a code-less 400 is unchanged', () => {
    expect(isRetryableSubmissionRefusal(400, CODE)).toBe(false);
    expect(PERMANENT_SUBMISSION_CODES.has(CODE)).toBe(false);
    expect(describeSubmissionRefusal({ kind: 'refused', status: 400, code: undefined })).toBe(
      "Spaarke didn't accept the submission's details. Nothing was submitted."
    );
  });
});
