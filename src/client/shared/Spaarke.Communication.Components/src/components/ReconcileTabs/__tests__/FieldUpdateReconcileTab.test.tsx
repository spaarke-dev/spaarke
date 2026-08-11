/**
 * FieldUpdateReconcileTab.test.tsx — the Pillar E Fields reconcile tab (task 055,
 * FR-E4 + NFR-10). Drives the closed acceptance set through the real component with
 * a mocked `authenticatedFetch` seam:
 *   • NFR-10 gate — no confirmed record ⇒ "confirm first", nothing fetched.
 *   • current→matched shown, matched EDITABLE, citation clickable, confidence shown.
 *   • Accept POSTs `/apply` with the EDITED value in `{ overrideValue }`.
 *   • Reject POSTs `/dismiss`; Hold makes NO write (leave Proposed).
 *   • Re-scope — changing the confirmed record re-fetches and swaps the list.
 *   • Feed is filtered to THIS communication's `pending-proposal` items.
 *   • Citation click → `onCitationClick` (task 054 anchor).
 *   • FormModal dual-use mount renders the proposal (ADR-050); dark-mode (ADR-021).
 */
import * as React from 'react';
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: [string, RequestInit?]) => mockAuthenticatedFetch(...args),
}));

import { FieldUpdateReconcileTab, FieldUpdateReconcileModal } from '../FieldUpdateReconcileTab';

const COMM_ID = 'comm-1';
const REGARDING_A = { entityType: 'sprk_matter', recordId: 'aaaaaaaa-0000-0000-0000-000000000001' };
const REGARDING_B = { entityType: 'sprk_matter', recordId: 'bbbbbbbb-0000-0000-0000-000000000002' };

function proposalItem(overrides: Record<string, unknown> = {}) {
  return {
    communicationId: COMM_ID,
    kind: 'pending-proposal',
    reviewLogId: 'rl-1',
    targetEntity: 'sprk_matter',
    targetField: 'sprk_closingdate',
    fieldType: 'DateTime',
    oldValue: '2026-01-01',
    newValue: '2026-08-15',
    proposalConfidence: 0.9,
    reason: 'The email states the closing date changed to August 15, 2026.',
    citationSource: 'body',
    citationLocator: 'body: sentence 1',
    citationQuotedText: 'the closing has been moved to August 15, 2026',
    ...overrides,
  };
}

/** A queue-feed GET response. */
function feed(items: unknown[]) {
  return { ok: true, status: 200, json: async () => ({ items, count: items.length }) };
}
/** An apply/dismiss POST response. */
function ok() {
  return { ok: true, status: 200, json: async () => ({ auditLogId: 'audit-1' }) };
}

/** Default: any queue-feed GET returns one proposal for COMM_ID; any POST succeeds. */
function wireDefault(items: unknown[] = [proposalItem()]) {
  mockAuthenticatedFetch.mockImplementation((url: string, init?: RequestInit) => {
    if (!init || (init.method ?? 'GET') === 'GET') return Promise.resolve(feed(items));
    return Promise.resolve(ok());
  });
}

const renderTab = (props: Partial<React.ComponentProps<typeof FieldUpdateReconcileTab>> = {}, theme = webLightTheme) =>
  render(
    <FluentProvider theme={theme}>
      <FieldUpdateReconcileTab communicationId={COMM_ID} regarding={REGARDING_A} {...props} />
    </FluentProvider>
  );

describe('FieldUpdateReconcileTab', () => {
  beforeEach(() => mockAuthenticatedFetch.mockReset());
  afterEach(() => jest.restoreAllMocks());

  // NFR-10 gate — no confirmed record ⇒ prompt to confirm first; nothing is fetched.
  it('gates on NFR-10 when no association is confirmed and does not fetch', async () => {
    renderTab({ regarding: null });
    expect(await screen.findByTestId('field-reconcile-gated')).toBeInTheDocument();
    expect(mockAuthenticatedFetch).not.toHaveBeenCalled();
  });

  // AC1 — a confirmed record's proposal shows current→matched, an editable value, a citation, and confidence.
  it('shows current→matched with an editable value, citation, and confidence', async () => {
    wireDefault();
    renderTab();
    const card = await screen.findByTestId('field-reconcile-card');
    expect(within(card).getByTestId('field-reconcile-old')).toHaveTextContent('2026-01-01');
    expect(within(card).getByTestId('field-reconcile-edit')).toHaveValue('2026-08-15'); // editable, seeded with matched
    expect(within(card).getByTestId('field-reconcile-confidence')).toHaveTextContent('90%');
    expect(within(card).getByTestId('field-reconcile-citation')).toBeInTheDocument();
  });

  // AC2 — Accept POSTs the EDITED value to /apply as { overrideValue }.
  it('Accept applies the edited value via POST /apply { overrideValue }', async () => {
    wireDefault();
    const onResolved = jest.fn();
    renderTab({ onProposalResolved: onResolved });

    const input = await screen.findByTestId('field-reconcile-edit');
    fireEvent.change(input, { target: { value: '2026-09-20' } });
    fireEvent.click(screen.getByTestId('field-reconcile-accept'));

    await waitFor(() =>
      expect(mockAuthenticatedFetch).toHaveBeenCalledWith(
        '/communications/proposals/rl-1/apply',
        expect.objectContaining({ method: 'POST' })
      )
    );
    const applyCall = mockAuthenticatedFetch.mock.calls.find(c => String(c[0]).endsWith('/apply'));
    expect(JSON.parse(applyCall![1].body)).toEqual({ overrideValue: '2026-09-20' });
    await waitFor(() => expect(onResolved).toHaveBeenCalledWith('rl-1', 'applied'));
    expect(screen.queryByTestId('field-reconcile-card')).not.toBeInTheDocument(); // row leaves Proposed
  });

  // AC2 — Reject terminally dismisses via POST /dismiss.
  it('Reject dismisses via POST /dismiss', async () => {
    wireDefault();
    const onResolved = jest.fn();
    renderTab({ onProposalResolved: onResolved });

    fireEvent.click(await screen.findByTestId('field-reconcile-reject'));

    await waitFor(() =>
      expect(mockAuthenticatedFetch).toHaveBeenCalledWith(
        '/communications/proposals/rl-1/dismiss',
        expect.objectContaining({ method: 'POST' })
      )
    );
    await waitFor(() => expect(onResolved).toHaveBeenCalledWith('rl-1', 'rejected'));
  });

  // AC2 — Hold leaves Proposed: NO apply/dismiss write (only the initial queue-feed GET fired).
  it('Hold makes no write (leave Proposed)', async () => {
    wireDefault();
    const onResolved = jest.fn();
    renderTab({ onProposalResolved: onResolved });

    fireEvent.click(await screen.findByTestId('field-reconcile-hold'));

    await waitFor(() => expect(onResolved).toHaveBeenCalledWith('rl-1', 'held'));
    // Only the queue-feed GET ran — no POST to apply or dismiss.
    const posts = mockAuthenticatedFetch.mock.calls.filter(c => c[1] && c[1].method === 'POST');
    expect(posts).toHaveLength(0);
  });

  // AC4 (NFR-10 re-scope) — changing the confirmed record re-fetches and swaps the list.
  it('re-scopes (re-fetches) when the confirmed record is overridden', async () => {
    mockAuthenticatedFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (!init || (init.method ?? 'GET') === 'GET') {
        const items = String(url).includes(REGARDING_B.recordId)
          ? [proposalItem({ reviewLogId: 'rl-b', targetField: 'sprk_mattername', newValue: 'Project B' })]
          : [proposalItem({ reviewLogId: 'rl-a', targetField: 'sprk_closingdate' })];
        return Promise.resolve(feed(items));
      }
      return Promise.resolve(ok());
    });

    const { rerender } = renderTab({ regarding: REGARDING_A });
    await screen.findByText('sprk_closingdate');

    rerender(
      <FluentProvider theme={webLightTheme}>
        <FieldUpdateReconcileTab communicationId={COMM_ID} regarding={REGARDING_B} />
      </FluentProvider>
    );

    // Re-fetched for record B and swapped the list — A's proposal is gone.
    expect(await screen.findByText('sprk_mattername')).toBeInTheDocument();
    expect(screen.queryByText('sprk_closingdate')).not.toBeInTheDocument();
    expect(mockAuthenticatedFetch.mock.calls.some(c => String(c[0]).includes(REGARDING_B.recordId))).toBe(true);
  });

  // Feed is filtered to THIS communication's pending-proposal items (not association-exceptions, not other comms).
  it('filters the feed to this communication pending-proposal items', async () => {
    wireDefault([
      proposalItem(), // this comm, pending-proposal ✓
      proposalItem({ reviewLogId: 'rl-2', kind: 'association-exception' }), // wrong kind ✗
      proposalItem({ reviewLogId: 'rl-3', communicationId: 'other-comm' }), // other comm ✗
    ]);
    renderTab();
    await screen.findByTestId('field-reconcile-list');
    expect(screen.getAllByTestId('field-reconcile-card')).toHaveLength(1);
  });

  // Task 054 — clicking a proposal's citation lifts it to the host (browse-shell activeCitation).
  it('fires onCitationClick with the proposal citation', async () => {
    wireDefault();
    const onCitationClick = jest.fn();
    renderTab({ onCitationClick });

    fireEvent.click(await screen.findByTestId('field-reconcile-citation'));

    expect(onCitationClick).toHaveBeenCalledWith(
      expect.objectContaining({ quotedText: 'the closing has been moved to August 15, 2026', source: 'body' })
    );
  });

  // Empty — a confirmed record with no proposals shows the empty state (not the gate).
  it('shows the empty state when the record has no proposals', async () => {
    wireDefault([]);
    renderTab();
    expect(await screen.findByTestId('field-reconcile-empty')).toBeInTheDocument();
  });

  // AC1/AC5 — the FormModal dual-use mount renders the proposal (ADR-050) and dark-mode renders cleanly (ADR-021).
  it('renders the proposal inside the FormModal mount under the dark theme without errors', async () => {
    wireDefault();
    const errSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    render(
      <FluentProvider theme={webDarkTheme}>
        <FieldUpdateReconcileModal open onClose={jest.fn()} communicationId={COMM_ID} regarding={REGARDING_A} />
      </FluentProvider>
    );
    expect(await screen.findByTestId('field-reconcile-card')).toBeInTheDocument();
    expect(errSpy).not.toHaveBeenCalled();
  });
});
