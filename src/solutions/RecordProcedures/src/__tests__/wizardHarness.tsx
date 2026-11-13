/**
 * The harness for the wizard tests (ebilling-r1 task 084b): the real panel, the real clients, and a fake BFF that answers the two list reads and
 * every route of task 089 the wizards call. Not a test file.
 */

import { render, screen, within, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ProceduresPanel } from '../components/ProceduresPanel';
import { createRecordProceduresApi, type RecordProceduresView, type RecordRulesInForceView } from '../services/recordProceduresApi';
import { createOverridesApi } from '../services/overridesApi';
import { createDryRunApi } from '../services/dryRunApi';
import type { FirmChoices } from '../services/firmChoices';
import { FENWICK, MATTER_ID, OKAFOR, PROJECT_ID, TODAY, WA_ID, matterProcedures, matterRules } from './fixtures';
import { BASE, problem, serverFake, type Handler, type WriteCall, type WriteHandler } from './testKit';

const KINDS = {
  matter: { core: 'matters', table: 'sprk_matter', id: MATTER_ID },
  project: { core: 'projects', table: 'sprk_project', id: PROJECT_ID },
  workassignment: { core: 'workassignments', table: 'sprk_workassignment', id: WA_ID },
} as const;

export interface HarnessOptions {
  kind?: keyof typeof KINDS;
  procedures?: Handler | RecordProceduresView;
  rules?: Handler | RecordRulesInForceView;
  writes?: WriteHandler;
  /** Task 084d-4: the dry run's answers. Absent: every dry run fails (as an unanswered call), so the Impact step shows the refusal. */
  dryRun?: WriteHandler;
  /** Review round 1: the clients' timeout in ms (the real one is 20 s), so a test of a read that never answers need not wait for it. */
  timeoutMs?: number;
}

export function renderHarness(o: HarnessOptions = {}) {
  const k = KINDS[o.kind ?? 'matter'];
  const asHandler = <T,>(v: Handler | T | undefined, fallback: T): Handler => (typeof v === 'function' ? (v as Handler) : () => v ?? fallback);
  const server = serverFake({
    procedures: asHandler(o.procedures, matterProcedures),
    rules: asHandler(o.rules, matterRules),
    writes: o.writes,
    dryRun: o.dryRun,
  });
  const firmChoices: FirmChoices = { invoicedBy: jest.fn(() => Promise.resolve({ firms: [{ id: OKAFOR.id, name: OKAFOR.name! }], complete: true })) };
  const view = render(
    <FluentProvider theme={webLightTheme}>
      <ProceduresPanel
        core={k.core}
        table={k.table}
        recordId={k.id}
        api={createRecordProceduresApi(server.fetchFn, BASE)}
        overrides={createOverridesApi(server.fetchFn, BASE, { timeoutMs: o.timeoutMs })}
        dryRun={createDryRunApi(server.fetchFn, BASE, { timeoutMs: o.timeoutMs })}
        firmChoices={firmChoices}
        today={TODAY}
      />
    </FluentProvider>
  );
  return { ...view, ...server, recordId: k.id, core: k.core };
}

// ── Driving the wizard ───────────────────────────────────────────────────────

export const ready = () => screen.findByTestId('recordprocedures-ready');

export async function openProcedure(id: string, title: string): Promise<HTMLElement> {
  await ready();
  fireEvent.click(screen.getByTestId(`procedure-row-${id}`));
  return screen.findByRole('dialog', { name: title, hidden: true });
}

export async function openRule(id: string, title: string): Promise<HTMLElement> {
  await ready();
  fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));
  fireEvent.click(screen.getByTestId(`rule-row-${id}`));
  return screen.findByRole('dialog', { name: title, hidden: true });
}

export const button = (d: HTMLElement, name: string | RegExp) => within(d).getByRole('button', { name, hidden: true });
export const radio = (d: HTMLElement, name: string | RegExp) => within(d).getByRole('radio', { name, hidden: true });
export const next = (d: HTMLElement) => button(d, 'Next');
export const chooseRadio = (d: HTMLElement, name: string | RegExp) => fireEvent.click(radio(d, name));

/** Type into the "Reason" field of the open choice. */
export const typeReason = (d: HTMLElement, text: string) => fireEvent.change(within(d).getByLabelText(/^Reason/), { target: { value: text } });

/** On "On this record" with the choice made: go to Impact, then Confirm (waits for a removal preview when the step needs one). */
export async function toConfirm(d: HTMLElement) {
  fireEvent.click(next(d));
  await within(d).findByTestId('impact-step');
  await waitFor(() => expect(next(d)).toBeEnabled());
  fireEvent.click(next(d));
  await within(d).findByTestId('confirm-step');
}

/** The first step to the choices. */
export const toChoices = (d: HTMLElement) => {
  fireEvent.click(next(d));
  return within(d).findByTestId('mode-step');
};

export function respond(map: Partial<Record<WriteCall['route'], (c: WriteCall) => unknown>>): WriteHandler {
  return c => {
    const h = map[c.route];
    if (!h) return new Error(`unexpected ${c.route}`);
    return h(c);
  };
}

// ── Server bodies ────────────────────────────────────────────────────────────

export const createdBody = (over: Record<string, unknown> = {}) => ({
  id: 'new-row',
  kind: 'Procedure',
  mode: 'FollowLatest',
  procedureId: null,
  pinnedVersionId: null,
  policyId: null,
  firmId: null,
  reason: null,
  ...over,
});

export const previewBody = (over: Record<string, unknown> = {}) => ({
  overrideId: 'row',
  kind: 'Procedure',
  closesRule: false,
  warning: null,
  affectedText: null,
  visibleOpenInvoices: null,
  ...over,
});

export const removedBody = (over: Record<string, unknown> = {}) => ({
  overrideId: 'row',
  kind: 'Procedure',
  ruleSwitchedOff: false,
  closeComplete: true,
  affectedText: null,
  visibleOpenInvoices: null,
  message: null,
  ...over,
});

/** Task 081's read of a Procedure: its versions. */
export const procedureBody = (versions: Array<{ id: string; n: number; status: string; from: string | null; to?: string | null }>) => ({
  id: 'p',
  name: 'p',
  versions: versions.map(v => ({ id: v.id, versionNumber: v.n, status: v.status, effectiveFrom: v.from, effectiveTo: v.to ?? null, previousVersionId: null })),
});

/** An `ApiError` carrying a ProblemDetails with a title, as `authenticatedFetch` throws a refusal. */
export const refusal = (status: number, title: string, errorCode: string | null = null, extra: Record<string, unknown> = {}) => problem(status, errorCode, { title, ...extra });

export { FENWICK, OKAFOR };
