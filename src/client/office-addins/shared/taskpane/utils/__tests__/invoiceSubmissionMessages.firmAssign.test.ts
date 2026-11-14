/**
 * Task 134 slice C: the firm-assign sentences (ebilling 095g, "Contract for word-add-in-r1 (as built)"). One table, keyed
 * on `errorCode ?? reasonCode` only: `detail` is never an input, because one code can carry different `detail` texts.
 */
import {
  describeFirmAssignRefusal,
  FIRM_ASSIGN_ERROR_MESSAGES,
  FIRM_ASSIGN_GENERIC_MESSAGE,
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
    ['ebilling.firm_assign.slots_full', 'Two firms are already assigned. Replace one of them.'],
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
      { status: 0, code: undefined },
      { status: 500, code: 'constructor' },
      { status: 500, code: '__proto__' },
    ]) {
      expect(describeFirmAssignRefusal(refusal)).toBe(FIRM_ASSIGN_GENERIC_MESSAGE);
    }
    expect(FIRM_ASSIGN_GENERIC_MESSAGE).toBe("Spaarke couldn't add the firm right now. Nothing was changed.");
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
