/**
 * layoutSaveError — the wizard's message for a failed layout save (POST/PUT /api/workspace/layouts).
 *
 * The App injects `@spaarke/auth`'s authenticatedFetch, which THROWS ApiError(status) for a non-OK
 * response. The 412 concurrent-edit sentence was produced only by a `response.status === 412` check on a
 * returned response, so in production a lost If-Match save showed the raw "HTTP 412". handleFinish's
 * catch now delegates here. Errors are the REAL ApiError class (source-mapped @spaarke/auth).
 */
import { ApiError } from '@spaarke/auth';
import { LAYOUT_EDITED_ELSEWHERE_MESSAGE, layoutSaveError } from '../App';

describe('layoutSaveError', () => {
  it('a thrown 412 is the "edited in another tab… reopen and retry" sentence', () => {
    const err = layoutSaveError(new ApiError('HTTP 412', 412, null));
    expect(err.message).toBe(LAYOUT_EDITED_ELSEWHERE_MESSAGE);
    expect(err.message).toMatch(/edited in another tab or session/);
    expect(err.message).toMatch(/reopen Manage Workspaces to refresh, then retry/);
  });

  it('keeps the existing status cases', () => {
    expect(layoutSaveError(new ApiError('HTTP 409', 409, null)).message).toMatch(/Maximum 10 workspaces/);
    expect(layoutSaveError(new ApiError('Name is required', 400, { title: 'Bad Request', status: 400, detail: 'Name is required' })).message).toBe(
      'Name is required'
    );
    expect(layoutSaveError(new ApiError('HTTP 403', 403, null)).message).toBe('This layout cannot be modified.');
    expect(layoutSaveError(new ApiError('HTTP 404', 404, null)).message).toBe(
      'The layout was not found. It may have been deleted.'
    );
  });

  it('the 412 Error thrown for a RETURNED 412 response keeps its sentence', () => {
    expect(layoutSaveError(new Error(LAYOUT_EDITED_ELSEWHERE_MESSAGE)).message).toBe(LAYOUT_EDITED_ELSEWHERE_MESSAGE);
  });
});
