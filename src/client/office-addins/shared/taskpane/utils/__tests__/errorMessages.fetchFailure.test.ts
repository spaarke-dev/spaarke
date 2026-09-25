/**
 * Task 053 (spaarkeai-word-add-in-r1): `describeFetchFailure` — turns a failed fetch `Response` into a
 * user-facing message for the two SaveFlow.tsx sites that used to discard it wholesale
 * (`createRelatedRecord`'s `if (!res.ok) return null` and `relatedSearch`'s `if (!res.ok) return []`).
 * Pure-function tests against a minimal fake `Response` (only `.json()`/`.status` are read), independent
 * of the end-to-end `SaveFlow.quickCreateErrorSurfacing.test.tsx` / `RelatedToPicker.errorSurfacing.test.tsx`
 * render suites.
 *
 * Also pins the new `owner_unresolved` catalog entry `mapProblemDetailsToMessage` falls into — the code
 * task 031's load-bearing Project/Matter owner refusal actually sends (confirmed a top-level `errorCode`
 * via `OfficeQuickCreateContractTests`/`OfficeQuickCreateProjectContractTests`, NOT nested under
 * `extensions.code` — that nesting is specific to the GLOBAL exception handler task 050 fixed, a
 * different code path from QuickCreate's own `catch (SdapProblemException)` block).
 *
 * NEW FILE — ADR-038 (new tests in new files); `errorMessages.collision.test.ts` is the sibling
 * precedent for a topic-scoped `errorMessages.*.test.ts` file in this package.
 */
import { describeFetchFailure, mapProblemDetailsToMessage, type ProblemDetails } from '../errorMessages';

/** Minimal fake `Response` — only the members `describeFetchFailure` reads. */
function fakeResponse(status: number, body: unknown, bodyIsJson = true): Response {
  return {
    status,
    json: () => (bodyIsJson ? Promise.resolve(body) : Promise.reject(new SyntaxError('Unexpected token'))),
  } as unknown as Response;
}

describe('describeFetchFailure', () => {
  it('surfaces the SERVER-supplied detail for a 403 owner_unresolved ProblemDetails body (task 031)', async () => {
    const problem: ProblemDetails = {
      type: 'https://spaarke.com/errors/office/owner_unresolved',
      title: 'Project Not Created',
      status: 403,
      detail:
        'Your account could not be matched to a Dataverse user, so the new project could not be assigned ' +
        'to you and was not created. Ask an administrator to check that your user is provisioned in this ' +
        'environment.',
      errorCode: 'owner_unresolved',
      correlationId: 'corr-1',
    };

    const result = await describeFetchFailure(fakeResponse(403, problem));

    // The exact server detail — never a client-invented replacement string.
    expect(result.message).toBe(problem.detail);
    expect(result.recoverable).toBe(false); // an immediate retry cannot fix an unprovisioned account
  });

  it('never invents a Retry for owner_unresolved even via the generic ProblemDetails mapper', () => {
    // Same assertion at the mapProblemDetailsToMessage layer describeFetchFailure delegates to, so the
    // `owner_unresolved` catalog entry itself is pinned independent of any Response plumbing.
    const problem: ProblemDetails = {
      type: 'https://spaarke.com/errors/office/owner_unresolved',
      title: 'Matter Not Created',
      status: 403,
      detail: 'Your account could not be matched to a Dataverse user.',
      errorCode: 'owner_unresolved',
    };

    const result = mapProblemDetailsToMessage(problem);

    expect(result.recoverable).toBe(false);
    expect(result.action).toMatch(/administrator/i);
    expect(result.message).toBe(problem.detail);
  });

  it('falls back to a sensible, status-aware message for a non-ProblemDetails 502 (no body to read)', async () => {
    const result = await describeFetchFailure(fakeResponse(502, null, /* bodyIsJson */ false));

    expect(result.message).not.toBe('');
    expect(result.message.toLowerCase()).not.toContain('undefined');
    expect(result.recoverable).toBe(true); // 5xx — plausibly transient
  });

  it('never throws even when the body is not JSON at all', async () => {
    await expect(describeFetchFailure(fakeResponse(504, null, false))).resolves.toBeDefined();
  });

  it('treats a non-ProblemDetails 4xx as non-recoverable (unlike a 5xx)', async () => {
    const result = await describeFetchFailure(fakeResponse(400, { message: 'plain text, not RFC 7807' }));

    expect(result.recoverable).toBe(false);
  });

  it('prefers the catalog OFFICE_009 message over the generic fallback when the code IS recognized', async () => {
    const problem: ProblemDetails = {
      type: 'https://spaarke.com/errors/office/access-denied',
      title: 'Access Denied',
      status: 403,
      detail: 'You do not have permission to perform this action. Please contact your administrator.',
      errorCode: 'OFFICE_009',
    };

    const result = await describeFetchFailure(fakeResponse(403, problem));

    expect(result.title).toBe('Access Denied');
    expect(result.recoverable).toBe(false);
  });
});
