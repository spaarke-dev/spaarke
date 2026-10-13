import { AuthError, ApiError } from '../src/errors';
import { isApiError, problemOf, isAuthFailure } from '../src/errorGuards';
import * as pkg from '../src/index';

/** The same error from a second bundled copy of @spaarke/auth: same shape, different class. */
function foreignCopy<T extends Error>(source: T): Error {
  const copy = new Error(source.message);
  Object.assign(copy, source);
  copy.name = source.name;
  return copy;
}

describe('isApiError', () => {
  const notFound = new ApiError('Not Found', 404, { title: 'Not Found', status: 404 });

  it('is true for any ApiError when no status is given', () => {
    expect(isApiError(notFound)).toBe(true);
    expect(isApiError(new ApiError('HTTP 500', 500))).toBe(true);
  });

  it('matches only the listed statuses when given', () => {
    expect(isApiError(notFound, 404)).toBe(true);
    expect(isApiError(notFound, 401, 403)).toBe(false);
    expect(isApiError(notFound, 403, 404)).toBe(true);
  });

  it('recognises an ApiError from another bundled copy of the package', () => {
    const foreign = foreignCopy(notFound);
    expect(foreign instanceof ApiError).toBe(false);
    expect(isApiError(foreign, 404)).toBe(true);
  });

  it('is false for AuthError, other status-carrying errors and non-errors', () => {
    expect(isApiError(new AuthError('expired', 'auth_exhausted'))).toBe(false);
    const sdap = Object.assign(new Error('x'), { name: 'SdapHttpError', status: 404 });
    expect(isApiError(sdap)).toBe(false);
    expect(isApiError(new TypeError('Failed to fetch'))).toBe(false);
    expect(isApiError({ ok: false, status: 404 })).toBe(false);
    expect(isApiError(null)).toBe(false);
    expect(isApiError(undefined)).toBe(false);
    expect(isApiError('ApiError')).toBe(false);
  });
});

describe('problemOf', () => {
  it('returns the parsed problem details of an ApiError, extensions included', () => {
    const pd = { title: 'Refused', status: 422, detail: 'No.', reasonCode: 'sdap.x' };
    expect(problemOf(new ApiError('No.', 422, pd))).toEqual(pd);
    expect(problemOf(foreignCopy(new ApiError('No.', 422, pd)))).toEqual(pd);
  });

  it('returns null when the ApiError carried no problem details', () => {
    expect(problemOf(new ApiError('HTTP 502', 502))).toBeNull();
  });

  it('returns null for anything that is not an ApiError', () => {
    expect(problemOf(new AuthError('expired', 'auth_exhausted'))).toBeNull();
    expect(problemOf(Object.assign(new Error('x'), { problemDetails: { title: 't' } }))).toBeNull();
    expect(problemOf(undefined)).toBeNull();
  });
});

describe('isAuthFailure', () => {
  it('is true for AuthError (the exhausted-401 path) and an ApiError 401', () => {
    expect(isAuthFailure(new AuthError('Authentication failed after all retry attempts', 'auth_exhausted'))).toBe(true);
    expect(isAuthFailure(foreignCopy(new AuthError('expired', 'auth_exhausted')))).toBe(true);
    expect(isAuthFailure(new ApiError('Unauthorized', 401))).toBe(true);
  });

  it('is false for other statuses and other errors', () => {
    expect(isAuthFailure(new ApiError('Forbidden', 403))).toBe(false);
    expect(isAuthFailure(new TypeError('Failed to fetch'))).toBe(false);
    expect(isAuthFailure(null)).toBe(false);
  });
});

describe('package index', () => {
  it('exports the guards', () => {
    expect(pkg.isApiError).toBe(isApiError);
    expect(pkg.problemOf).toBe(problemOf);
    expect(pkg.isAuthFailure).toBe(isAuthFailure);
  });
});
