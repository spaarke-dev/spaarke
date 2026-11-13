/**
 * Task 084d-5b: the Assign Procedure wizard (notes/084d-dry-run-design.md "084d-5 design" sections 5 and 6, EB-D-227..230, EB-D-232): the entry
 * point decided by the panel from both answers, the Choose step on task 084d-5a's assignable list (offered, OwnRow, Gap, too many, 503, the
 * K3 note), Follow and Pin each planning one `assign`, the Impact request, the one POST with the exact body, a refusal kept in the wizard, and
 * the read's timeout and abort.
 */

import '@testing-library/jest-dom';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { ASSIGN_NOT_LISTED_NOTE, LIST_TIMED_OUT_TEXT, OWN_ROW_TEXT, SEARCH_DEBOUNCE_MS, tooManyText } from '../components/wizards/AssignProcedureWizard';
import { MAX_REASON_CHARS, planAssign } from '../components/wizards/plan';
import type { AssignableProcedureItem, RecordAssignableProceduresView, WritePermissions } from '../services/recordProceduresApi';
import { FENWICK, MATTER, MATTER_ID, OKAFOR, TODAY, WRITE_ALL, matterProcedures, matterRules } from './fixtures';
import { button, chooseRadio, createdBody, next, procedureBody, radio, ready, refusal, renderHarness, respond, typeReason } from './wizardHarness';
import type { WriteCall } from './testKit';

const NEVER = () => new Promise<never>(() => undefined);
const GAP_WARNING = 'No version of this Procedure would apply on this date. If you assign it, invoices checked from then on are held until a version applies.';

const item = (over: Partial<AssignableProcedureItem> & Pick<AssignableProcedureItem, 'id' | 'name'>): AssignableProcedureItem => ({
  type: 'Guideline',
  appliesTo: 'AssignedRecordsOnly',
  firm: null,
  statusIfAssigned: 'InForce',
  versionInForce: { id: `${over.id}-v2`, number: 2, status: 'Active', from: '2026-01-01', to: null },
  gap: null,
  ...over,
});

const LITIGATION = item({ id: 'p-lit', name: 'Litigation billing instructions', firm: FENWICK });
const EXPERT = item({
  id: 'p-expert',
  name: 'Expert witness policy',
  type: 'Policy',
  statusIfAssigned: 'Gap',
  versionInForce: null,
  gap: { kind: 'AfterLastVersion', text: 'Its last version ended on 30 Jun 2026.', warning: GAP_WARNING },
});
const OWN = { id: 'p-own', name: 'Discovery protocol', reason: 'OwnRow' };

function assignableView(over: Partial<RecordAssignableProceduresView> = {}): RecordAssignableProceduresView {
  return {
    record: MATTER,
    date: TODAY,
    firm: FENWICK,
    firmSource: 'Record',
    q: null,
    bound: 50,
    tooMany: false,
    procedures: [EXPERT, LITIGATION],
    notOffered: [OWN],
    label: 'If assigned now',
    ...over,
  };
}

const TOO_MANY = assignableView({ tooMany: true, procedures: null, notOffered: [] });

/** The two lists, each answering these permissions. */
const lists = (procedures: WritePermissions, rules: WritePermissions) => ({
  procedures: { ...matterProcedures, permissions: procedures },
  rules: { ...matterRules, permissions: rules },
});

const NO_CREATE: WritePermissions = { assignOrExclude: false, remove: true, change: true };

async function openAssign(): Promise<HTMLElement> {
  await ready();
  fireEvent.click(screen.getByTestId('assign-procedure'));
  return screen.findByRole('dialog', { name: 'Assign Procedure', hidden: true });
}

const choose = (d: HTMLElement) => within(d).findByTestId('assign-list');
const routes = (writeCalls: WriteCall[]) => writeCalls.map(c => c.route).filter(r => r !== 'procedure');

const versions = procedureBody([
  { id: 'lit-v1', n: 1, status: 'Superseded', from: '2025-01-01', to: '2026-01-01' },
  { id: 'lit-v2', n: 2, status: 'Active', from: '2026-01-01' },
  { id: 'lit-v3', n: 3, status: 'Draft', from: '2027-01-01' },
]);

describe('the entry point: the panel decides from both answers', () => {
  it.each([
    ['both answers allow a create', WRITE_ALL, WRITE_ALL, true],
    ['the Procedures answer does not', NO_CREATE, WRITE_ALL, false],
    ['the rules answer does not', WRITE_ALL, NO_CREATE, false],
    ['neither does', NO_CREATE, NO_CREATE, false],
  ])('%s: "Assign Procedure" is %s', async (_, procedures, rules, present) => {
    renderHarness(lists(procedures, rules));
    await ready();
    if (present) {
      expect(screen.getByTestId('assign-procedure')).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Assign Procedure' })).toBeInTheDocument();
    } else {
      // Absent, not disabled.
      expect(screen.queryByTestId('assign-procedure')).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Assign Procedure' })).not.toBeInTheDocument();
    }
  });
});

describe('Choose', () => {
  it('reads the assignable list with the lists\' query and lists each offered item with its name, type, appliesTo, firm, status if assigned and version; OwnRow is disabled with its reason', async () => {
    const { assignableCalls } = renderHarness({ assignable: () => assignableView() });
    const d = await openAssign();
    const list = await choose(d);

    expect(assignableCalls).toHaveLength(1);
    const url = new URL(assignableCalls[0].url);
    expect(assignableCalls[0].method).toBe('GET');
    expect(url.pathname).toBe(`/api/finance/matters/${MATTER_ID}/procedures/assignable`);
    // The lists were read with the record's billing firm (the server default), so no firmId; the lists' date; no name search.
    expect(url.searchParams.get('firmId')).toBeNull();
    expect(url.searchParams.get('date')).toBe(TODAY);
    expect(url.searchParams.has('q')).toBe(false);

    expect(within(list).getByTestId('assign-label')).toHaveTextContent('If assigned now');
    const lit = within(list).getByTestId('assign-item-p-lit');
    expect(lit).toHaveTextContent('Litigation billing instructions');
    expect(lit).toHaveTextContent('Guideline · Assigned records only · Fenwick & Calder LLP invoices only');
    expect(lit).toHaveTextContent('In force');
    expect(lit).toHaveTextContent('v2 · from 1 Jan 2026');
    expect(radio(d, /^Litigation billing instructions/)).toBeEnabled();
    const expert = within(list).getByTestId('assign-item-p-expert');
    expect(expert).toHaveTextContent('Policy · Assigned records only · Every firm');
    expect(expert).toHaveTextContent('No version');
    expect(expert).toHaveTextContent('No version on this date');

    const own = within(list).getByTestId('assign-not-offered-p-own');
    expect(own).toHaveTextContent('Discovery protocol');
    expect(own).toHaveTextContent(OWN_ROW_TEXT);
    expect(radio(d, /^Discovery protocol/)).toBeDisabled();
    fireEvent.click(radio(d, /^Discovery protocol/));
    expect(radio(d, /^Discovery protocol/)).not.toBeChecked();

    // Next waits for a choice.
    expect(next(d)).toBeDisabled();
    chooseRadio(d, /^Litigation billing instructions/);
    expect(next(d)).toBeEnabled();
  });

  it('a firm the user picked is sent as the list\'s firmId (the lists\' own query)', async () => {
    const { assignableCalls, calls } = renderHarness({
      procedures: q => ({ ...matterProcedures, firmSource: q.firmId ? 'Query' : 'Record', firm: q.firmId ? OKAFOR : FENWICK }),
      assignable: () => assignableView(),
    });
    await ready();
    fireEvent.click(screen.getByTestId('firm-picker'));
    fireEvent.click(await screen.findByRole('option', { name: OKAFOR.name! }));
    await waitFor(() => expect(calls.filter(c => c.query.firmId === OKAFOR.id)).toHaveLength(2));
    const d = await openAssign();
    await choose(d);
    expect(new URL(assignableCalls[0].url).searchParams.get('firmId')).toBe(OKAFOR.id);
  });

  it('a Gap Procedure shows its gap text and the warning that invoices are held (EB-D-230), at Choose and on Impact', async () => {
    renderHarness({ assignable: () => assignableView() });
    const d = await openAssign();
    await choose(d);
    const warning = within(d).getByTestId('assign-gap-p-expert');
    expect(warning).toHaveTextContent('Its last version ended on 30 Jun 2026.');
    expect(warning).toHaveTextContent(GAP_WARNING);
    expect(within(d).queryByTestId('assign-gap-p-lit')).not.toBeInTheDocument();

    chooseRadio(d, /^Expert witness policy/);
    fireEvent.click(next(d));
    await within(d).findByTestId('mode-step');
    fireEvent.click(next(d));
    const impact = await within(d).findByTestId('impact-step');
    expect(within(impact).getByTestId('assign-impact-gap')).toHaveTextContent(GAP_WARNING);
  });

  it('always shows the note on what is never listed (draft-only, another firm)', async () => {
    renderHarness({ assignable: () => assignableView() });
    const d = await openAssign();
    await choose(d);
    expect(within(d).getByTestId('assign-not-listed-note')).toHaveTextContent(ASSIGN_NOT_LISTED_NOTE);
    expect(ASSIGN_NOT_LISTED_NOTE).toBe("Procedures with only draft versions, or for another firm, aren't listed. Publish a version, or switch the panel's firm.");
  });

  it('too many: a search box and no list; typing re-asks with q only after the pause, once for the last text', async () => {
    const { assignableCalls } = renderHarness({
      assignable: c => (new URL(c.url).searchParams.get('q') ? assignableView({ q: 'Litig', procedures: [LITIGATION], notOffered: [] }) : TOO_MANY),
    });
    const d = await openAssign();
    const tooMany = await within(d).findByTestId('assign-too-many');
    expect(tooMany).toHaveTextContent('More than 50 Procedures match. Type part of a name to narrow the list.');
    expect(tooManyText(50)).toBe('More than 50 Procedures match. Type part of a name to narrow the list.');
    expect(within(d).queryByTestId('assign-list')).not.toBeInTheDocument();
    expect(within(d).queryAllByRole('radio', { hidden: true })).toHaveLength(0);
    expect(next(d)).toBeDisabled();

    const search = within(d).getByTestId('assign-search');
    fireEvent.change(search, { target: { value: 'Lit' } });
    fireEvent.change(search, { target: { value: '  Litig ' } });
    // Not yet: the search waits for the pause.
    expect(assignableCalls).toHaveLength(1);
    await waitFor(() => expect(assignableCalls).toHaveLength(2), { timeout: SEARCH_DEBOUNCE_MS * 5 });
    const url = new URL(assignableCalls[1].url);
    expect(url.searchParams.get('q')).toBe('Litig');
    expect(url.searchParams.get('date')).toBe(TODAY);

    const list = await choose(d);
    expect(within(list).getByTestId('assign-item-p-lit')).toBeInTheDocument();
    // The box stays while the user narrows the list.
    expect(within(d).getByTestId('assign-search')).toBeInTheDocument();
    expect(assignableCalls).toHaveLength(2);
  });

  it('a 503 shows the panel\'s "unavailable" words and no list', async () => {
    renderHarness({ assignable: () => refusal(503, 'Procedures are unavailable', 'procedures.unavailable') });
    const d = await openAssign();
    const failed = await within(d).findByTestId('assign-failed');
    expect(failed).toHaveTextContent('Procedures cannot be shown right now');
    expect(failed).toHaveTextContent('Nothing is shown rather than a partial list. Retry in a moment.');
    expect(within(d).queryByTestId('assign-list')).not.toBeInTheDocument();
    expect(within(d).queryAllByRole('radio', { hidden: true })).toHaveLength(0);
    expect(next(d)).toBeDisabled();
  });
});

describe('On this record: Follow and Pin each plan exactly one assign', () => {
  it('Follow latest is one assign with { procedureId, mode } only', () => {
    expect(planAssign('p-lit', 'follow', {}).steps).toEqual([{ kind: 'assign', procedureId: 'p-lit', mode: 'FollowLatest' }]);
    expect(planAssign(null, 'follow', {}).steps).toEqual([]);
  });

  it('Pin needs a pinnable version and a reason of at most 2000 characters; then it is one assign', () => {
    const pinnable = ['lit-v1', 'lit-v2'];
    expect(planAssign('p-lit', 'pin', { pinnable }).steps).toEqual([]);
    expect(planAssign('p-lit', 'pin', { versionId: 'lit-v2', reason: '  ', pinnable }).steps).toEqual([]);
    expect(planAssign('p-lit', 'pin', { reason: 'Grandfathered', pinnable }).steps).toEqual([]);
    expect(planAssign('p-lit', 'pin', { versionId: 'lit-v3', reason: 'Grandfathered', pinnable }).steps).toEqual([]);
    expect(planAssign('p-lit', 'pin', { versionId: 'lit-v2', reason: 'x'.repeat(MAX_REASON_CHARS + 1), pinnable }).steps).toEqual([]);
    expect(planAssign('p-lit', 'pin', { versionId: 'lit-v2', reason: ' Grandfathered ', pinnable }).steps).toEqual([
      { kind: 'assign', procedureId: 'p-lit', mode: 'Pinned', pinnedVersionId: 'lit-v2', reason: 'Grandfathered' },
    ]);
  });
});

describe('Impact and Finish', () => {
  it('Follow: the Impact request is Assign, and Finish posts once with exactly { procedureId, mode }; both lists are read again', async () => {
    const { dryRunCalls, writeCalls, calls } = renderHarness({ assignable: () => assignableView(), writes: respond({ assign: () => createdBody({ procedureId: 'p-lit' }) }) });
    const d = await openAssign();
    await choose(d);
    chooseRadio(d, /^Litigation billing instructions/);
    fireEvent.click(next(d));
    await within(d).findByTestId('mode-step');
    expect(radio(d, /^Follow latest/)).toBeChecked();
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');

    await waitFor(() => expect(dryRunCalls).toHaveLength(1));
    expect(dryRunCalls[0].pathname).toBe(`/api/finance/matters/${MATTER_ID}/procedures/dry-run`);
    expect(dryRunCalls[0].body).toEqual({ kind: 'Assign', assign: { procedureId: 'p-lit', mode: 'FollowLatest' } });

    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    const confirm = await within(d).findByTestId('confirm-step');
    expect(confirm).toHaveTextContent('Not on this record → Follows the latest version · v2');
    const reads = calls.length;
    fireEvent.click(button(d, 'Assign Procedure'));
    await within(d).findByTestId('success-body');

    expect(routes(writeCalls)).toEqual(['assign']);
    expect(writeCalls[0].method).toBe('POST');
    expect(writeCalls[0].pathname).toBe(`/api/finance/matters/${MATTER_ID}/procedure-assignments`);
    expect(writeCalls[0].body).toEqual({ procedureId: 'p-lit', mode: 'FollowLatest' });
    // The panel's silent re-read of both lists.
    await waitFor(() => expect(calls.length).toBe(reads + 2));
    expect(calls.slice(reads).map(c => c.leaf).sort()).toEqual(['procedures', 'rules-in-force']);
  });

  it('Pin: Next waits for a version and a reason; Impact asks Assign Pinned, and Finish posts once with exactly { procedureId, mode, pinnedVersionId, reason }', async () => {
    const { dryRunCalls, writeCalls } = renderHarness({
      assignable: () => assignableView(),
      writes: respond({ procedure: () => versions, assign: () => createdBody({ mode: 'Pinned' }) }),
    });
    const d = await openAssign();
    await choose(d);
    chooseRadio(d, /^Litigation billing instructions/);
    fireEvent.click(next(d));
    await within(d).findByTestId('mode-step');
    chooseRadio(d, /^Pin a version/);
    expect(next(d)).toBeDisabled();

    // Only the versions that have been in force are offered (Draft is not).
    fireEvent.click(await within(d).findByRole('combobox', { hidden: true }));
    expect(await screen.findByRole('option', { name: /^v2/, hidden: true })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /^v1/, hidden: true })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /^v3/, hidden: true })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('option', { name: /^v1/, hidden: true }));
    expect(next(d)).toBeDisabled();
    typeReason(d, ' Grandfathered rates ');
    expect(next(d)).toBeEnabled();

    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    await waitFor(() => expect(dryRunCalls).toHaveLength(1));
    expect(dryRunCalls[0].body).toEqual({ kind: 'Assign', assign: { procedureId: 'p-lit', mode: 'Pinned', pinnedVersionId: 'lit-v1', reason: 'Grandfathered rates' } });

    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    fireEvent.click(button(d, 'Assign Procedure'));
    await within(d).findByTestId('success-body');
    expect(routes(writeCalls)).toEqual(['assign']);
    expect(writeCalls.filter(c => c.route === 'assign')[0].body).toEqual({ procedureId: 'p-lit', mode: 'Pinned', pinnedVersionId: 'lit-v1', reason: 'Grandfathered rates' });
  });

  it('a 409 assignment.procedure_inactive is shown with its title and detail, and the wizard stays open on Confirm with nothing re-read', async () => {
    const { writeCalls, calls } = renderHarness({
      assignable: () => assignableView(),
      writes: respond({
        assign: () => refusal(409, 'Conflict', 'assignment.procedure_inactive', { detail: 'A deactivated Procedure cannot be assigned, pinned or excluded.' }),
      }),
    });
    const d = await openAssign();
    await choose(d);
    chooseRadio(d, /^Litigation billing instructions/);
    fireEvent.click(next(d));
    await within(d).findByTestId('mode-step');
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    const reads = calls.length;

    fireEvent.click(button(d, 'Assign Procedure'));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).toHaveTextContent('Conflict');
    expect(alert).toHaveTextContent('A deactivated Procedure cannot be assigned, pinned or excluded.');
    expect(d).toBeInTheDocument();
    expect(within(d).getByTestId('confirm-step')).toBeInTheDocument();
    expect(button(d, 'Assign Procedure')).toBeEnabled();
    expect(within(d).queryByTestId('success-body')).not.toBeInTheDocument();
    expect(routes(writeCalls)).toEqual(['assign']);
    expect(calls.length).toBe(reads);
  });
});

describe('the list read that does not answer', () => {
  it('is given up on at the timeout: "took too long", no list, and Retry asks again', async () => {
    let n = 0;
    const { assignableCalls } = renderHarness({ timeoutMs: 80, assignable: () => (++n === 1 ? NEVER() : assignableView()) });
    const d = await openAssign();
    expect(within(d).getByTestId('assign-loading')).toBeInTheDocument();
    const failed = await within(d).findByTestId('assign-failed');
    expect(failed).toHaveTextContent(LIST_TIMED_OUT_TEXT);
    expect(within(d).queryByTestId('assign-list')).not.toBeInTheDocument();
    expect(next(d)).toBeDisabled();

    fireEvent.click(within(d).getByTestId('assign-retry'));
    expect(await choose(d)).toBeInTheDocument();
    expect(assignableCalls).toHaveLength(2);
  });

  it('closing the wizard abandons the read (its abort signal fires); a newer search abandons the older one; a late answer changes nothing', async () => {
    const pending: Array<(v: unknown) => void> = [];
    const logged = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    try {
      const { mock } = renderHarness({
        assignable: c => (new URL(c.url).searchParams.get('q') ? new Promise(resolve => pending.push(resolve)) : TOO_MANY),
      });
      const d = await openAssign();
      await within(d).findByTestId('assign-too-many');
      const signalOf = (i: number) => (mock.mock.calls.filter(c => String(c[0]).includes('/assignable'))[i][1] as RequestInit).signal as AbortSignal;

      fireEvent.change(within(d).getByTestId('assign-search'), { target: { value: 'Alpha' } });
      await waitFor(() => expect(pending).toHaveLength(1), { timeout: SEARCH_DEBOUNCE_MS * 5 });
      fireEvent.change(within(d).getByTestId('assign-search'), { target: { value: 'Bravo' } });
      await waitFor(() => expect(pending).toHaveLength(2), { timeout: SEARCH_DEBOUNCE_MS * 5 });
      expect(signalOf(1).aborted).toBe(true);
      expect(signalOf(2).aborted).toBe(false);

      // The abandoned search answers late: it is not shown.
      await act(async () => pending[0](assignableView({ q: 'Alpha', procedures: [LITIGATION], notOffered: [] })));
      expect(within(d).queryByTestId('assign-list')).not.toBeInTheDocument();

      fireEvent.click(button(d, 'Cancel'));
      await waitFor(() => expect(screen.queryByRole('dialog', { hidden: true })).not.toBeInTheDocument());
      expect(signalOf(2).aborted).toBe(true);
      await act(async () => pending[1](assignableView({ q: 'Bravo' })));
      expect(logged).not.toHaveBeenCalled();
    } finally {
      logged.mockRestore();
    }
  });
});
