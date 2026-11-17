/**
 * Task 134 slice C: the firm-assign sentences (ebilling 095g, "Contract for word-add-in-r1 (as built)"). One table, keyed
 * on `errorCode ?? reasonCode` only: `detail` is never an input, because one code can carry different `detail` texts.
 */
import {
  describeFirmAssignRefusal,
  FIRM_ASSIGN_ERROR_MESSAGES,
  FIRM_ASSIGN_GENERIC_MESSAGE,
  FIRM_ASSIGN_UNCONFIRMED_MESSAGE,
  FIRM_LIST_DENIED_MESSAGE,
  FIRM_LIST_GENERIC_MESSAGE,
  SUBMISSION_ERROR_MESSAGES,
} from '../invoiceSubmissionMessages';

describe('describeFirmAssignRefusal', () => {
  it.each([
    [
      'sdap.access.deny.record_unavailable',
      "This record or firm isn't available to you, or you can't change this record.",
    ],
    [
      'ebilling.firm_assign.record_restricted',
      'This record is Restricted to internal users, so no firm can be given access to it.',
    ],
    [
      'ebilling.firm_assign.record_secure',
      'This record is secure, so assigning a firm would give it no access. Use Manage Access to add named people.',
    ],
    [
      'ebilling.firm_assign.record_limited',
      'This record is limited, so assigning a firm would give it no access. Use Manage Access to add named people.',
    ],
    ['ebilling.firm_assign.firm_inactive', 'This firm is inactive. Choose another firm.'],
    ['ebilling.firm_assign.not_a_law_firm', 'This organization is not a law firm. Choose a law firm.'],
    ['ebilling.firm_assign.firm_no_access', "This firm can't be given access to this record."],
    ['ebilling.firm_assign.slots_full', 'Two firms are already assigned. Replace one of them in Spaarke.'],
    ['ebilling.firm_assign.firm_already_assigned', 'That firm is already assigned to this record.'],
    ['ebilling.firm_assign.record_changed', 'The firms on this record changed. Check them and try again.'],
    ['ebilling.submission.record_unavailable', "Spaarke couldn't do this right now. Try again."],
    ['ebilling.disabled', 'Invoice submission is turned off in Spaarke.'],
  ])('%s has its contract sentence', (code, sentence) => {
    expect(describeFirmAssignRefusal({ status: 409, code })).toBe(sentence);
  });

  it('maps revoke_confirmation_required (which the pane can never cause) to the record_changed sentence', () => {
    expect(describeFirmAssignRefusal({ status: 409, code: 'ebilling.firm_assign.revoke_confirmation_required' })).toBe(
      describeFirmAssignRefusal({ status: 409, code: 'ebilling.firm_assign.record_changed' })
    );
  });

  it('every sentence the table holds is a plain string (no function, so no input but the code)', () => {
    for (const entry of Object.values(FIRM_ASSIGN_ERROR_MESSAGES)) expect(typeof entry).toBe('string');
  });

  it('a 400, a 404 with no code, an unknown code and no code at all get the generic sentence', () => {
    for (const refusal of [
      { status: 400, code: undefined },
      { status: 404, code: undefined },
      { status: 409, code: 'ebilling.firm_assign.not_yet_invented' },
      { status: 400, code: 'constructor' },
      { status: 0, code: undefined },
    ]) {
      expect(describeFirmAssignRefusal(refusal)).toBe(FIRM_ASSIGN_GENERIC_MESSAGE);
    }
    expect(FIRM_ASSIGN_GENERIC_MESSAGE).toBe("Spaarke couldn't add the firm right now. Nothing was changed.");
  });

  // The server may have written the firm before it failed: a 5xx the pane has no sentence for never says "Nothing was
  // changed". A repeat of the same add changes nothing, so the sentence says it is safe.
  it.each([
    [500, undefined],
    [502, undefined],
    [500, 'constructor'],
    [500, '__proto__'],
    [503, 'ebilling.firm_assign.not_yet_invented'],
    [504, undefined],
  ])('a %s with code %s (none this build knows) says it could not confirm, never "Nothing was changed"', (status, code) => {
    const text = describeFirmAssignRefusal({ status, code });
    expect(text).toBe("Spaarke couldn't confirm the firm was added. Try again; adding the same firm again is safe.");
    expect(text).toBe(FIRM_ASSIGN_UNCONFIRMED_MESSAGE);
    expect(text).not.toMatch(/Nothing was changed/);
  });

  it('must-not-fire: a known code keeps its sentence on a 5xx; a 4xx with no code stays generic', () => {
    expect(describeFirmAssignRefusal({ status: 503, code: 'ebilling.submission.record_unavailable' })).toBe(
      "Spaarke couldn't do this right now. Try again."
    );
    expect(describeFirmAssignRefusal({ status: 503, code: 'ebilling.disabled' })).toBe(
      'Invoice submission is turned off in Spaarke.'
    );
    expect(describeFirmAssignRefusal({ status: 400, code: undefined })).toBe(FIRM_ASSIGN_GENERIC_MESSAGE);
    expect(describeFirmAssignRefusal({ status: 499, code: undefined })).toBe(FIRM_ASSIGN_GENERIC_MESSAGE);
  });

  it('a 5xx on the LIST stays the list sentence (a search changed nothing, so there is nothing to confirm)', () => {
    expect(describeFirmAssignRefusal({ status: 500, code: undefined }, 'list')).toBe(FIRM_LIST_GENERIC_MESSAGE);
  });

  it("the list's uniform 404 says the user can't list law firms; the add keeps the record sentence for the same code", () => {
    const refusal = { status: 404, code: 'sdap.access.deny.record_unavailable' };
    expect(describeFirmAssignRefusal(refusal, 'list')).toBe("You can't list law firms in Spaarke.");
    expect(describeFirmAssignRefusal(refusal, 'list')).toBe(FIRM_LIST_DENIED_MESSAGE);
    expect(describeFirmAssignRefusal(refusal)).toBe(
      "This record or firm isn't available to you, or you can't change this record."
    );
    expect(describeFirmAssignRefusal(refusal, 'add')).toBe(describeFirmAssignRefusal(refusal));
  });

  it('the list gets its own generic sentence, not the one about adding', () => {
    expect(describeFirmAssignRefusal({ status: 404, code: undefined }, 'list')).toBe(FIRM_LIST_GENERIC_MESSAGE);
    expect(FIRM_LIST_GENERIC_MESSAGE).not.toMatch(/add the firm/);
  });

  it('must-not-fire: the status never changes the text of a code (a 503 from the route and from the filter agree)', () => {
    const code = 'ebilling.submission.record_unavailable';
    expect(describeFirmAssignRefusal({ status: 503, code })).toBe(describeFirmAssignRefusal({ status: 200, code }));
  });

  it('is its own table: the submission table says something else for the same 503 code', () => {
    const code = 'ebilling.submission.record_unavailable';
    expect(typeof SUBMISSION_ERROR_MESSAGES[code]).toBe('function');
    expect(describeFirmAssignRefusal({ status: 503, code })).not.toMatch(/Nothing was submitted/);
  });
});
