/**
 * FieldUpdateReconcileTab.browse-mount.test.tsx — the Pillar E keystone seam
 * (tasks 053 + 054 + 055, NFR-11 + FR-E4 + AC5 "mounts in the browse-shell right
 * pane"). Composes the real `ReconciliationBrowseShell` (task 053) with
 * `FieldUpdateReconcileTab` in its `renderTabs` slot and a host that lifts the
 * tab's `onCitationClick` into the shell's `activeCitation`. Proves the browse-pane
 * dual-use mount end-to-end: a proposal renders in the right pane, and clicking its
 * citation drives the LEFT reader to jump to + highlight the exact cited passage
 * (task 054) — the whole point of the browse experience.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: [string, RequestInit?]) => mockAuthenticatedFetch(...args),
}));

import { ReconciliationBrowseShell } from '../../ReconciliationBrowseShell';
import type { ReconciliationBrowseRecord } from '../../ReconciliationBrowseShell/ReconciliationBrowseShell.types';
import type { EmailCitation } from '../../../logic/citations';
import { FieldUpdateReconcileTab } from '../FieldUpdateReconcileTab';

const COMM_ID = 'comm-1';
const REGARDING = { entityType: 'sprk_matter', recordId: 'aaaaaaaa-0000-0000-0000-000000000001' };
const QUOTED = 'the closing has been moved to August 15, 2026';

const RECORD: ReconciliationBrowseRecord = {
  id: COMM_ID,
  subject: 'Closing update',
  from: 'jane.doe@example.com',
  to: 'counsel@spaarke.com',
  // No emlDocumentId ⇒ the reader degrades to `body`; the body carries the quoted text so the
  // body-sourced citation resolves to the cited-passage callout.
  body: `<p>Hello counsel — please note ${QUOTED}. Regards.</p>`,
};

function proposalItem() {
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
    citationSource: 'body',
    citationLocator: 'body: sentence 1',
    citationQuotedText: QUOTED,
  };
}

/** Host that wires the tab's onCitationClick → the shell's activeCitation (the r5 code-page pattern). */
const BrowseMountHost: React.FC = () => {
  const [activeCitation, setActiveCitation] = React.useState<EmailCitation | undefined>();
  return (
    <ReconciliationBrowseShell
      open
      onClose={jest.fn()}
      queue={[RECORD]}
      initialIndex={0}
      activeCitation={activeCitation}
      renderTabs={record => (
        <FieldUpdateReconcileTab
          communicationId={record.id}
          regarding={REGARDING}
          onCitationClick={setActiveCitation}
        />
      )}
    />
  );
};

describe('FieldUpdateReconcileTab — browse-shell right-pane mount (seam 053+054+055)', () => {
  beforeEach(() => {
    mockAuthenticatedFetch.mockReset();
    mockAuthenticatedFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (!init || (init.method ?? 'GET') === 'GET') {
        // queue-feed GET (eml-render never fires — RECORD has no emlDocumentId).
        return Promise.resolve({ ok: true, status: 200, json: async () => ({ items: [proposalItem()], count: 1 }) });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => ({}) });
    });
  });
  afterEach(() => jest.restoreAllMocks());

  it('renders the proposal in the right pane and a citation click highlights it in the left reader', async () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <BrowseMountHost />
      </FluentProvider>
    );

    // The tab mounted in the browse-shell RIGHT pane.
    const tabsPane = await screen.findByTestId('reconciliation-browse-tabs');
    const card = await within(tabsPane).findByTestId('field-reconcile-card');
    expect(within(card).getByTestId('field-reconcile-edit')).toHaveValue('2026-08-15');

    // Click the citation → onCitationClick lifts it to activeCitation → the LEFT reader jumps + highlights.
    fireEvent.click(within(card).getByTestId('field-reconcile-citation'));

    const mark = await screen.findByTestId('citation-highlight-mark');
    expect(mark).toHaveTextContent(QUOTED);
    expect(screen.queryByTestId('email-body-citation-not-locatable')).not.toBeInTheDocument();
  });
});
