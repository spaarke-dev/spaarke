/**
 * `thrownFetchError` is this package's copy of `@spaarke/auth`'s error guards (see its header for why
 * it is a copy). This suite pins the two to the same answers, against the REAL error classes.
 */
import { isApiError, problemOf, isAuthFailure } from '../thrownFetchError';
import * as auth from '../../../../Spaarke.Auth/src/errorGuards';
import { ApiError, AuthError } from '../../../../Spaarke.Auth/src/errors';

/** The same error from a second bundled copy of @spaarke/auth: same shape, different class. */
function foreignCopy(source: Error): Error {
  const copy = new Error(source.message);
  Object.assign(copy, source);
  copy.name = source.name;
  return copy;
}

const pd = { title: 'Refused', status: 422, detail: 'No.', reasonCode: 'sdap.x' };
const samples: Array<[string, unknown]> = [
  ['ApiError 404 with problem', new ApiError('Not Found', 404, { title: 'Not Found', status: 404 })],
  ['ApiError 422 with extensions', new ApiError('No.', 422, pd)],
  ['ApiError 401', new ApiError('Unauthorized', 401)],
  ['ApiError 502 without problem', new ApiError('HTTP 502', 502)],
  ['ApiError from another bundle copy', foreignCopy(new ApiError('No.', 422, pd))],
  ['AuthError auth_exhausted', new AuthError('Authentication failed after all retry attempts', 'auth_exhausted')],
  ['AuthError from another bundle copy', foreignCopy(new AuthError('expired', 'auth_exhausted'))],
  ['SdapHttpError-like', Object.assign(new Error('x'), { name: 'SdapHttpError', status: 404 })],
  ['TypeError (network)', new TypeError('Failed to fetch')],
  ['returned non-OK response object', { ok: false, status: 404 }],
  ['null', null],
  ['undefined', undefined],
];

describe('thrownFetchError matches @spaarke/auth', () => {
  it.each(samples)('%s', (_label, err) => {
    expect(isApiError(err)).toBe(auth.isApiError(err));
    for (const status of [401, 404, 422, 502]) {
      expect(isApiError(err, status)).toBe(auth.isApiError(err, status));
    }
    expect(isApiError(err, 401, 404)).toBe(auth.isApiError(err, 401, 404));
    expect(problemOf(err)).toEqual(auth.problemOf(err));
    expect(isAuthFailure(err)).toBe(auth.isAuthFailure(err));
  });

  it('answers the cases the fixes rely on', () => {
    expect(isApiError(new ApiError('Not Found', 404), 404)).toBe(true);
    expect(problemOf(new ApiError('No.', 422, pd))?.reasonCode).toBe('sdap.x');
    expect(isAuthFailure(new AuthError('expired', 'auth_exhausted'))).toBe(true);
    expect(isApiError({ ok: false, status: 404 })).toBe(false);
  });
});
