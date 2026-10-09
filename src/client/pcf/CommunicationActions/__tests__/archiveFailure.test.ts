/**
 * "Save to SharePoint" error line — built from what `@spaarke/auth`'s authenticatedFetch THROWS.
 *
 * The fetch never returns a non-2xx Response (it throws ApiError, or AuthError once its 401 retries are
 * spent), so the button's "Save to SharePoint failed (n)." frame only exists if the catch builds it.
 * Before the fix the catch showed `err.message` — a bare "HTTP 500" or the server detail with no frame.
 */
import { ApiError, AuthError } from '@spaarke/auth';
import { archiveFailureMessage } from '../CommunicationActions/archiveFailure';

describe('archiveFailureMessage', () => {
  it('frames a thrown 500 with no ProblemDetails as "Save to SharePoint failed (500)."', () => {
    expect(archiveFailureMessage(new ApiError('HTTP 500', 500))).toBe('Save to SharePoint failed (500).');
  });

  it("frames a thrown ApiError with the server's detail", () => {
    const detail = 'The communication has no .eml to archive.';
    const err = new ApiError(detail, 409, { title: 'Conflict', status: 409, detail });
    expect(archiveFailureMessage(err)).toBe(`Save to SharePoint failed (409): ${detail}`);
  });

  it('falls back to the ProblemDetails title', () => {
    const err = new ApiError('Archive unavailable', 503, { title: 'Archive unavailable', status: 503 });
    expect(archiveFailureMessage(err)).toBe('Save to SharePoint failed (503): Archive unavailable');
  });

  it('says the sign-in expired for a thrown AuthError', () => {
    const err = new AuthError('Authentication failed after all retry attempts', 'auth_exhausted');
    expect(archiveFailureMessage(err)).toBe(
      'Save to SharePoint failed: your sign-in has expired. Refresh the page to sign in again.'
    );
  });

  it('a network failure (fetch TypeError) says the server could not be reached, not "Failed to fetch"', () => {
    expect(archiveFailureMessage(new TypeError('Failed to fetch'))).toBe(
      'Save to SharePoint failed: the Spaarke server could not be reached. Check your connection.'
    );
  });

  it('any other non-HTTP error is the plain frame, never its raw message', () => {
    expect(archiveFailureMessage(new SyntaxError('Unexpected token < in JSON at position 0'))).toBe(
      'Save to SharePoint failed.'
    );
  });
});
