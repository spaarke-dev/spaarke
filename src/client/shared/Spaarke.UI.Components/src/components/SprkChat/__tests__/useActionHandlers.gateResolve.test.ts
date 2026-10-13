/**
 * Gate-resolve outcome contract (G-P3 UAT round-2 R2-A, 2026-07-07).
 *
 * Round-2 evidence: three confirmed dataverse.create_record gates failed
 * server-side (502 gate.dispatch-failed with the handler's instructive detail
 * in the ProblemDetails body) and the client rendered NOTHING the user could
 * read — the generic errorCode-only message went to a transient toast.
 *
 * These tests pin the client half of the fix: `resolveGate` (via
 * dispatchConfirmedAction) must surface the server's `detail` so SprkChat can
 * render an honest transcript message.
 */

import { dispatchConfirmedAction, rejectPendingAction } from '../hooks/useActionHandlers';
import type { IPendingAction } from '../types';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const pendingAction: IPendingAction = {
  actionId: 'confirmation-abc123',
  actionName: 'SYS-Dataverse Create Record',
  sessionId: 'session-1',
  summary: 'Create a record',
  parameters: {},
} as unknown as IPendingAction;

/**
 * The injected fetch in its PRODUCTION shape (`@spaarke/auth`'s authenticatedFetch): a 2xx is returned;
 * a non-2xx is THROWN as ApiError carrying the ProblemDetails body (only when the body has `title` or
 * `status`, as the real fetch parses it). Failure bodies below are therefore full ProblemDetails.
 */
function fetchReturning(status: number, body: unknown): (url: string, init?: RequestInit) => Promise<Response> {
  return throwingAuthenticatedFetch(
    () =>
      ({
        ok: status >= 200 && status < 300,
        status,
        json: async () => body,
      }) as unknown as Response
  );
}

describe('dispatchConfirmedAction — gate-resolve outcome contract (R2-A)', () => {
  it('surfaces the ProblemDetails detail on a 502 gate.dispatch-failed', async () => {
    const detail = "Column 'sprk_assignedto': lookup objects require a 'recordId' GUID on the native transport.";
    const outcome = await dispatchConfirmedAction(
      pendingAction,
      'https://bff.example',
      fetchReturning(502, { title: 'Bad Gateway', status: 502, errorCode: 'gate.dispatch-failed', detail })
    );

    expect(outcome.success).toBe(false);
    expect(outcome.errorCode).toBe('gate.dispatch-failed');
    // The handler's instructive error is the user's correction signal — it must
    // reach the rendered message, not be flattened to a bare errorCode.
    expect(outcome.message).toContain('recordId');
    expect(outcome.message).toContain(pendingAction.actionName);
  });

  it('falls back to the stable errorCode when the body carries no detail', async () => {
    const outcome = await dispatchConfirmedAction(
      pendingAction,
      'https://bff.example',
      fetchReturning(502, { title: 'Bad Gateway', status: 502, errorCode: 'gate.dispatch-failed' })
    );

    expect(outcome.success).toBe(false);
    expect(outcome.message).toContain('gate.dispatch-failed');
  });

  it('returns the confirmed summary on 200', async () => {
    const outcome = await dispatchConfirmedAction(
      pendingAction,
      'https://bff.example',
      fetchReturning(200, { status: 'confirmed', summary: 'Created record 651194cd in sprk_event.' })
    );

    expect(outcome.success).toBe(true);
    expect(outcome.message).toContain('Created record 651194cd');
  });

  it('extracts the additive record-link fields on 200 (G-P3 round-4 R4-3)', async () => {
    const recordUrl =
      'https://spaarkedev1.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=651194cd-3670-f111-ab0e-70a8a590c51c';
    const outcome = await dispatchConfirmedAction(
      pendingAction,
      'https://bff.example',
      fetchReturning(200, {
        status: 'confirmed',
        summary: "Record created in 'sprk_matter'.",
        recordUrl,
        recordEntityLogicalName: 'sprk_matter',
        recordId: '651194cd-3670-f111-ab0e-70a8a590c51c',
      })
    );

    expect(outcome.success).toBe(true);
    expect(outcome.recordUrl).toBe(recordUrl);
    expect(outcome.recordEntityLogicalName).toBe('sprk_matter');
    expect(outcome.recordId).toBe('651194cd-3670-f111-ab0e-70a8a590c51c');
  });

  it('leaves record-link fields undefined when the server sends none (pre-R4-3 responses)', async () => {
    const outcome = await dispatchConfirmedAction(
      pendingAction,
      'https://bff.example',
      fetchReturning(200, { status: 'confirmed', summary: 'ok' })
    );

    expect(outcome.success).toBe(true);
    expect(outcome.recordUrl).toBeUndefined();
    expect(outcome.recordEntityLogicalName).toBeUndefined();
    expect(outcome.recordId).toBeUndefined();
  });

  it('maps 409 to gate.not-pending with retry copy', async () => {
    const outcome = await dispatchConfirmedAction(
      pendingAction,
      'https://bff.example',
      fetchReturning(409, { title: 'Conflict', status: 409, errorCode: 'gate.not-pending' })
    );

    expect(outcome.success).toBe(false);
    expect(outcome.errorCode).toBe('gate.not-pending');
    expect(outcome.message).toContain('already resolved or has expired');
  });

  it('carries 422 gate.no-binding-target so SprkChat renders "recorded and approved, but cannot execute from chat"', async () => {
    const outcome = await dispatchConfirmedAction(
      pendingAction,
      'https://bff.example',
      fetchReturning(422, { title: 'Unprocessable Entity', status: 422, errorCode: 'gate.no-binding-target' })
    );

    expect(outcome.success).toBe(false);
    expect(outcome.errorCode).toBe('gate.no-binding-target');
  });

  it('reject: surfaces the ProblemDetails detail too', async () => {
    const outcome = await rejectPendingAction(
      pendingAction,
      'https://bff.example',
      fetchReturning(400, { title: 'Bad Request', status: 400, errorCode: 'gate.invalid', detail: 'Gate is malformed.' })
    );

    expect(outcome).toEqual({
      success: false,
      errorCode: 'gate.invalid',
      message: 'SYS-Dataverse Create Record failed: Gate is malformed.',
    });
  });

  it('falls back to gate.resolve-failed when the failure body is not ProblemDetails', async () => {
    const outcome = await dispatchConfirmedAction(pendingAction, 'https://bff.example', fetchReturning(500, 'not json'));

    expect(outcome.errorCode).toBe('gate.resolve-failed');
    expect(outcome.message).toBe('SYS-Dataverse Create Record could not be dispatched (gate.resolve-failed).');
  });

  it('keeps the returned-response path for a host fetch that returns non-2xx', async () => {
    const returning = jest.fn().mockResolvedValue({
      ok: false,
      status: 422,
      json: async () => ({ errorCode: 'gate.no-binding-target' }),
    } as unknown as Response);

    const outcome = await dispatchConfirmedAction(pendingAction, 'https://bff.example', returning);
    expect(outcome.errorCode).toBe('gate.no-binding-target');
  });
});
