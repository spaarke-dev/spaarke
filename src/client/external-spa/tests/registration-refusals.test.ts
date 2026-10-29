/**
 * The registration POST's refusals are mapped to fixed reasons (a server message is never shown). A 403 for an address
 * that has no current invitation is its own reason, so the person is told to ask for a new invitation rather than to
 * wait for an administrator.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../src/types';

const bffApiCall = vi.fn();
vi.mock('../src/auth/bff-client', () => ({ bffApiCall: (...args: unknown[]) => bffApiCall(...args) }));

import { submitRegistration } from '../src/auth/registration-client';

const input = { firstName: 'Pat', lastName: 'Lee' };

function refusal(status: number, reasonCode: string): ApiError {
  return new ApiError(status, JSON.stringify({ status, reasonCode, detail: 'a server message' }));
}

describe('submitRegistration refusals', () => {
  afterEach(() => bffApiCall.mockReset());

  it.each([
    [403, 'sdap.access.deny.contact_not_invited', 'contact_not_invited'],
    [409, 'sdap.access.existing_record_found', 'existing_record_found'],
    [403, 'sdap.access.licensed_user_link_required', 'licensed_user_link_required'],
    [403, 'sdap.access.registration_contact_conflict', 'registration_contact_conflict'],
    [403, 'sdap.access.deny.some_other_code', 'blocked'],
  ])('%i %s is %s', async (status, reasonCode, expected) => {
    bffApiCall.mockRejectedValueOnce(refusal(status, reasonCode));
    expect(await submitRegistration(input)).toEqual({ kind: 'refused', reason: expected });
  });

  it('a registered caller is ok', async () => {
    bffApiCall.mockResolvedValueOnce({});
    expect(await submitRegistration(input)).toEqual({ kind: 'ok' });
  });
});
