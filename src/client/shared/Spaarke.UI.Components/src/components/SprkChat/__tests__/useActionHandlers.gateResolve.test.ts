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

import { dispatchConfirmedAction } from '../hooks/useActionHandlers';
import type { IPendingAction } from '../types';

const pendingAction: IPendingAction = {
  actionId: 'confirmation-abc123',
  actionName: 'SYS-Dataverse Create Record',
  sessionId: 'session-1',
  summary: 'Create a record',
  parameters: {},
} as unknown as IPendingAction;

function fetchReturning(status: number, body: unknown): (url: string, init?: RequestInit) => Promise<Response> {
  return jest.fn().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as unknown as Response);
}

describe('dispatchConfirmedAction — gate-resolve outcome contract (R2-A)', () => {
  it('surfaces the ProblemDetails detail on a 502 gate.dispatch-failed', async () => {
    const detail = "Column 'sprk_assignedto': lookup objects require a 'recordId' GUID on the native transport.";
    const outcome = await dispatchConfirmedAction(
      pendingAction,
      'https://bff.example',
      fetchReturning(502, { errorCode: 'gate.dispatch-failed', detail })
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
      fetchReturning(502, { errorCode: 'gate.dispatch-failed' })
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

  it('maps 409 to gate.not-pending with retry copy', async () => {
    const outcome = await dispatchConfirmedAction(
      pendingAction,
      'https://bff.example',
      fetchReturning(409, { errorCode: 'gate.not-pending' })
    );

    expect(outcome.success).toBe(false);
    expect(outcome.errorCode).toBe('gate.not-pending');
    expect(outcome.message).toContain('already resolved or has expired');
  });
});
