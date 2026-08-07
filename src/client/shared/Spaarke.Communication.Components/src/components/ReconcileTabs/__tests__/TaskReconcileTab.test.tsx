/**
 * TaskReconcileTab.test.tsx — the Pillar E Tasks reconcile tab (task 056, FR-E5 +
 * ADR-015 + NFR-10). Drives the closed acceptance set with a mocked `authenticatedFetch`:
 *   • NFR-10 gate — no confirmed record ⇒ "confirm first", nothing fetched.
 *   • editable task form (name/description/base/due/final/completed/status/assigned).
 *   • create-and-complete — status=Completed + completed date → POST 034 apply.
 *   • edited-name proposal → 056b ad-hoc create + 055b dismiss (no dropped edits).
 *   • "+ New task" ad-hoc → 056b create.
 *   • no auto-finalize (nothing POSTs without Accept); Reject → dismiss; Hold → no write.
 *   • re-scope on override; FormModal dual-use mount; dark-mode (ADR-021).
 */
import * as React from 'react';
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: [string, RequestInit?]) => mockAuthenticatedFetch(...args),
}));

import { TaskReconcileTab, TaskReconcileModal } from '../TaskReconcileTab';

const COMM_ID = 'comm-1';
const REGARDING_A = { entityType: 'sprk_matter', recordId: 'aaaaaaaa-0000-0000-0000-000000000001' };
const REGARDING_B = { entityType: 'sprk_matter', recordId: 'bbbbbbbb-0000-0000-0000-000000000002' };

function createTaskItem(overrides: Record<string, unknown> = {}) {
  return {
    communicationId: COMM_ID,
    kind: 'create-task',
    reviewLogId: 'rl-1',
    taskName: 'File the discovery responses',
    taskDescription: 'Prepare + file',
    taskDueDate: '2026-09-01',
    proposalConfidence: 0.85,
    citationSource: 'body',
    citationLocator: 'body: sentence 1',
    citationQuotedText: 'file the discovery responses by September 1',
    ...overrides,
  };
}

function feed(items: unknown[]) {
  return { ok: true, status: 200, json: async () => ({ items, count: items.length }) };
}
function ok() {
  return { ok: true, status: 200, json: async () => ({ createdTaskId: 'task-1', auditLogId: 'audit-1' }) };
}
function wireDefault(items: unknown[] = [createTaskItem()]) {
  mockAuthenticatedFetch.mockImplementation((url: string, init?: RequestInit) => {
    if (!init || (init.method ?? 'GET') === 'GET') return Promise.resolve(feed(items));
    return Promise.resolve(ok());
  });
}
/** Find the POST call whose url ends with `suffix`. */
function postCall(suffix: string) {
  return mockAuthenticatedFetch.mock.calls.find(c => c[1] && c[1].method === 'POST' && String(c[0]).endsWith(suffix));
}

const renderTab = (props: Partial<React.ComponentProps<typeof TaskReconcileTab>> = {}, theme = webLightTheme) =>
  render(
    <FluentProvider theme={theme}>
      <TaskReconcileTab communicationId={COMM_ID} regarding={REGARDING_A} {...props} />
    </FluentProvider>
  );

describe('TaskReconcileTab', () => {
  beforeEach(() => mockAuthenticatedFetch.mockReset());
  afterEach(() => jest.restoreAllMocks());

  it('gates on NFR-10 when no association is confirmed and does not fetch', async () => {
    renderTab({ regarding: null });
    expect(await screen.findByTestId('task-reconcile-gated')).toBeInTheDocument();
    expect(mockAuthenticatedFetch).not.toHaveBeenCalled();
  });

  // AC1 — an editable task form renders all FR-E5 fields.
  it('renders an editable task form with all FR-E5 fields', async () => {
    wireDefault();
    renderTab();
    const card = await screen.findByTestId('task-reconcile-card');
    expect(within(card).getByTestId('task-reconcile-name')).toHaveValue('File the discovery responses');
    for (const id of ['description', 'basedate', 'duedate', 'finalduedate', 'completeddate', 'status', 'assignedto']) {
      expect(within(card).getByTestId(`task-reconcile-${id}`)).toBeInTheDocument();
    }
  });

  // AC2 — create-and-complete: status=Completed + completed date → POST 034 apply carrying them.
  it('create-and-complete applies status=Completed + completed date via POST 034 apply', async () => {
    wireDefault();
    const onResolved = jest.fn();
    renderTab({ onTaskResolved: onResolved });
    const card = await screen.findByTestId('task-reconcile-card');

    fireEvent.change(within(card).getByTestId('task-reconcile-status'), { target: { value: '2' } });
    fireEvent.change(within(card).getByTestId('task-reconcile-completeddate'), { target: { value: '2026-08-07' } });
    fireEvent.click(within(card).getByTestId('task-reconcile-accept'));

    await waitFor(() => expect(postCall('/create-task/apply')).toBeTruthy());
    const body = JSON.parse(postCall('/create-task/apply')![1].body);
    expect(body.status).toBe(2);
    expect(body.completedDate).toBe('2026-08-07');
    await waitFor(() => expect(onResolved).toHaveBeenCalledWith('rl-1', 'applied'));
  });

  // Routing — an UNCHANGED proposal goes to 034 apply (not ad-hoc).
  it('an unchanged proposal Accept routes to POST 034 apply', async () => {
    wireDefault();
    renderTab();
    fireEvent.click(await screen.findByTestId('task-reconcile-accept'));
    await waitFor(() => expect(postCall('/create-task/apply')).toBeTruthy());
    expect(postCall(`/${COMM_ID}/create-task`)).toBeFalsy(); // did NOT use the ad-hoc path
  });

  // Routing — editing the NAME re-routes to the 056b ad-hoc create + a 055b dismiss (no dropped edits).
  it('an edited-name proposal Accept routes to ad-hoc create + dismiss', async () => {
    wireDefault();
    renderTab();
    const card = await screen.findByTestId('task-reconcile-card');
    fireEvent.change(within(card).getByTestId('task-reconcile-name'), { target: { value: 'Custom follow-up task' } });
    fireEvent.click(within(card).getByTestId('task-reconcile-accept'));

    await waitFor(() => expect(postCall(`/${COMM_ID}/create-task`)).toBeTruthy());
    const adhoc = JSON.parse(postCall(`/${COMM_ID}/create-task`)![1].body);
    expect(adhoc.subject).toBe('Custom follow-up task');
    expect(adhoc.regardingEntity).toBe('sprk_matter');
    expect(adhoc.regardingRecordId).toBe(REGARDING_A.recordId);
    await waitFor(() => expect(postCall('/rl-1/dismiss')).toBeTruthy()); // original proposal closed
  });

  // AC3 — "+ New task" ad-hoc creates via 056b (no reviewLogId).
  it('"+ New task" creates an ad-hoc task via POST 056b', async () => {
    wireDefault([]); // no proposals → only the ad-hoc form after clicking New task
    const onResolved = jest.fn();
    renderTab({ onTaskResolved: onResolved });
    await screen.findByTestId('task-reconcile-list');

    fireEvent.click(screen.getByTestId('task-reconcile-new'));
    const adhocCard = await screen.findByTestId('task-reconcile-adhoc-card');
    fireEvent.change(within(adhocCard).getByTestId('task-reconcile-name'), { target: { value: 'Call the client' } });
    fireEvent.click(within(adhocCard).getByTestId('task-reconcile-adhoc-accept'));

    await waitFor(() => expect(postCall(`/${COMM_ID}/create-task`)).toBeTruthy());
    expect(JSON.parse(postCall(`/${COMM_ID}/create-task`)![1].body).subject).toBe('Call the client');
    await waitFor(() => expect(onResolved).toHaveBeenCalledWith(null, 'applied'));
  });

  // ADR-015 — nothing finalizes without Accept (rendering a due-dated proposal fires no POST).
  it('does not auto-finalize: rendering a proposal fires no create POST', async () => {
    wireDefault();
    renderTab();
    await screen.findByTestId('task-reconcile-card');
    const posts = mockAuthenticatedFetch.mock.calls.filter(c => c[1] && c[1].method === 'POST');
    expect(posts).toHaveLength(0);
  });

  // Reject → dismiss.
  it('Reject dismisses via POST 055b', async () => {
    wireDefault();
    const onResolved = jest.fn();
    renderTab({ onTaskResolved: onResolved });
    fireEvent.click(await screen.findByTestId('task-reconcile-reject'));
    await waitFor(() => expect(postCall('/rl-1/dismiss')).toBeTruthy());
    await waitFor(() => expect(onResolved).toHaveBeenCalledWith('rl-1', 'rejected'));
  });

  // Hold → leave Proposed: no write (only the initial queue-feed GET).
  it('Hold makes no write (leave Proposed)', async () => {
    wireDefault();
    const onResolved = jest.fn();
    renderTab({ onTaskResolved: onResolved });
    fireEvent.click(await screen.findByTestId('task-reconcile-hold'));
    await waitFor(() => expect(onResolved).toHaveBeenCalledWith('rl-1', 'held'));
    expect(mockAuthenticatedFetch.mock.calls.filter(c => c[1] && c[1].method === 'POST')).toHaveLength(0);
  });

  // AC5 (NFR-10 re-scope) — changing the confirmed record re-fetches and swaps the list.
  it('re-scopes (re-fetches) when the confirmed record is overridden', async () => {
    mockAuthenticatedFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (!init || (init.method ?? 'GET') === 'GET') {
        const items = String(url).includes(REGARDING_B.recordId)
          ? [createTaskItem({ reviewLogId: 'rl-b', taskName: 'Task B' })]
          : [createTaskItem({ reviewLogId: 'rl-a', taskName: 'Task A' })];
        return Promise.resolve(feed(items));
      }
      return Promise.resolve(ok());
    });
    const { rerender } = renderTab({ regarding: REGARDING_A });
    await screen.findByDisplayValue('Task A');

    rerender(
      <FluentProvider theme={webLightTheme}>
        <TaskReconcileTab communicationId={COMM_ID} regarding={REGARDING_B} />
      </FluentProvider>
    );
    expect(await screen.findByDisplayValue('Task B')).toBeInTheDocument();
    expect(screen.queryByDisplayValue('Task A')).not.toBeInTheDocument();
  });

  // Feed filtered to this communication's create-task items.
  it('filters the feed to this communication create-task items', async () => {
    wireDefault([
      createTaskItem(),
      createTaskItem({ reviewLogId: 'rl-2', kind: 'pending-proposal' }),
      createTaskItem({ reviewLogId: 'rl-3', communicationId: 'other-comm' }),
    ]);
    renderTab();
    await screen.findByTestId('task-reconcile-list');
    expect(screen.getAllByTestId('task-reconcile-card')).toHaveLength(1);
  });

  it('fires onCitationClick with the proposal citation', async () => {
    wireDefault();
    const onCitationClick = jest.fn();
    renderTab({ onCitationClick });
    fireEvent.click(await screen.findByTestId('task-reconcile-citation'));
    expect(onCitationClick).toHaveBeenCalledWith(
      expect.objectContaining({ quotedText: 'file the discovery responses by September 1' })
    );
  });

  it('renders the proposal inside the FormModal mount under the dark theme without errors', async () => {
    wireDefault();
    const errSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    render(
      <FluentProvider theme={webDarkTheme}>
        <TaskReconcileModal open onClose={jest.fn()} communicationId={COMM_ID} regarding={REGARDING_A} />
      </FluentProvider>
    );
    expect(await screen.findByTestId('task-reconcile-card')).toBeInTheDocument();
    expect(errSpy).not.toHaveBeenCalled();
  });
});
