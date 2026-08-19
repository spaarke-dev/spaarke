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

// E2b/E2c (065) — the OOB Xrm bridge (metadata + lookup + navigateTo). `getXrmForPicker` is
// overridden per-test; `undefined` = non-MDA/dev host (the fieldType-hint / text-fallback path).
const mockPickerXrm: { current: unknown } = { current: undefined };
jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return { ...actual, getXrmForPicker: () => mockPickerXrm.current };
});

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
  beforeEach(() => {
    mockAuthenticatedFetch.mockReset();
    mockPickerXrm.current = undefined;
  });
  afterEach(() => jest.restoreAllMocks());

  // E2b (065) — an option-set proposal renders a dropdown seeded from live metadata; Accept sends the value.
  it('E2b: option-set field renders a metadata-seeded dropdown; Accept sends the chosen value', async () => {
    wireDefault([proposalItem({ fieldType: 'Picklist', targetField: 'sprk_stage', newValue: '1' })]);
    const getEntityMetadata = jest.fn().mockResolvedValue({
      Attributes: [
        {
          LogicalName: 'sprk_stage',
          AttributeType: 'Picklist',
          OptionSet: {
            Options: [
              { Value: 1, Label: { UserLocalizedLabel: { Label: 'Open' } } },
              { Value: 2, Label: { UserLocalizedLabel: { Label: 'Closed' } } },
            ],
          },
        },
      ],
    });
    mockPickerXrm.current = { Utility: { getEntityMetadata } };
    renderTab();

    const select = await screen.findByTestId('field-reconcile-edit-optionset');
    expect(getEntityMetadata).toHaveBeenCalledWith('sprk_matter', ['sprk_stage']);
    await waitFor(() => expect(within(select).getByText('Closed')).toBeInTheDocument());
    fireEvent.change(select, { target: { value: '2' } });
    fireEvent.click(screen.getByTestId('field-reconcile-accept'));

    await waitFor(() =>
      expect(mockAuthenticatedFetch.mock.calls.find(c => String(c[0]).endsWith('/apply'))).toBeTruthy()
    );
    const applyCall = mockAuthenticatedFetch.mock.calls.find(c => String(c[0]).endsWith('/apply'));
    expect(JSON.parse(applyCall![1].body)).toEqual({ overrideValue: '2' });
  });

  // E2b — a lookup field opens the OOB advanced-lookup with the attribute's Targets; a pick sets the id + name.
  it('E2b: lookup field opens the OOB lookup (targets from metadata); pick sets normalized id + name', async () => {
    wireDefault([proposalItem({ fieldType: 'Lookup', targetField: 'sprk_owner', newValue: '' })]);
    const getEntityMetadata = jest.fn().mockResolvedValue({
      Attributes: [{ LogicalName: 'sprk_owner', AttributeType: 'Lookup', Targets: ['systemuser', 'team'] }],
    });
    const lookupObjects = jest
      .fn()
      .mockResolvedValue([{ id: '{BBBBBBBB-2222-3333-4444-555555555555}', name: 'Priya Owner' }]);
    mockPickerXrm.current = { Utility: { getEntityMetadata, lookupObjects } };
    renderTab();

    const lookupBtn = await screen.findByTestId('field-reconcile-edit-lookup-btn');
    fireEvent.click(lookupBtn);
    await waitFor(() => expect(screen.getByTestId('field-reconcile-edit-lookup')).toHaveValue('Priya Owner'));
    expect(lookupObjects).toHaveBeenCalledWith(
      expect.objectContaining({ entityTypes: ['systemuser', 'team'], allowMultiSelect: false })
    );
    fireEvent.click(screen.getByTestId('field-reconcile-accept'));
    await waitFor(() =>
      expect(mockAuthenticatedFetch.mock.calls.find(c => String(c[0]).endsWith('/apply'))).toBeTruthy()
    );
    const applyCall = mockAuthenticatedFetch.mock.calls.find(c => String(c[0]).endsWith('/apply'));
    expect(JSON.parse(applyCall![1].body)).toEqual({ overrideValue: 'bbbbbbbb-2222-3333-4444-555555555555' });
  });

  // E2b — non-MDA host (no bridge): an option-set-hint proposal degrades to the editable text input.
  it('E2b: non-MDA host degrades option-set to the editable text input', async () => {
    wireDefault([proposalItem({ fieldType: 'Picklist', targetField: 'sprk_stage', newValue: 'draft' })]);
    mockPickerXrm.current = undefined; // no bridge
    renderTab();
    const card = await screen.findByTestId('field-reconcile-card');
    const input = within(card).getByTestId('field-reconcile-edit');
    expect(input).toHaveValue('draft');
    fireEvent.change(input, { target: { value: 'final' } });
    expect(input).toHaveValue('final');
    expect(within(card).queryByTestId('field-reconcile-edit-optionset')).not.toBeInTheDocument();
  });

  // E2c — "+ Update other fields" opens the confirmed record's OOB form; guarded no-op non-MDA.
  it('E2c: "Update other fields" opens the confirmed record form via navigateTo', async () => {
    wireDefault();
    const navigateTo = jest.fn().mockResolvedValue(undefined);
    mockPickerXrm.current = { Navigation: { navigateTo } };
    renderTab();

    const btn = await screen.findByTestId('field-reconcile-update-other');
    fireEvent.click(btn);
    await waitFor(() =>
      expect(navigateTo).toHaveBeenCalledWith(
        expect.objectContaining({
          pageType: 'entityrecord',
          entityName: 'sprk_matter',
          entityId: REGARDING_A.recordId,
        }),
        expect.any(Object)
      )
    );
  });

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
    // B2.2: the row STAYS visible in an Accepted state carrying a per-line Undo (the Accept button is gone).
    expect(await screen.findByTestId('field-reconcile-accepted')).toBeInTheDocument();
    expect(screen.getByTestId('field-reconcile-undo')).toBeInTheDocument();
    expect(screen.queryByTestId('field-reconcile-accept')).not.toBeInTheDocument();
  });

  // B2.2 — Undo reverses a just-accepted field via POST /undo, then the row shows a terminal "Undone" state.
  it('Undo reverses an accepted field via POST /undo', async () => {
    wireDefault();
    renderTab({});

    fireEvent.click(await screen.findByTestId('field-reconcile-accept'));
    fireEvent.click(await screen.findByTestId('field-reconcile-undo'));

    await waitFor(() =>
      expect(mockAuthenticatedFetch).toHaveBeenCalledWith(
        '/communications/proposals/rl-1/undo',
        expect.objectContaining({ method: 'POST' })
      )
    );
    expect(await screen.findByTestId('field-reconcile-undone')).toBeInTheDocument();
    expect(screen.queryByTestId('field-reconcile-undo')).not.toBeInTheDocument();
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
