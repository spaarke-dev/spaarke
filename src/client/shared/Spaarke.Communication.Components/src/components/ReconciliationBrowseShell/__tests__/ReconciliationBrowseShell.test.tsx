/**
 * ReconciliationBrowseShell.test.tsx
 *
 * UI tests for `<ReconciliationBrowseShell />` (email-communication-intelligence-r2
 * task 053, spec UI model §99 + NFR-11). Covers the task's closed acceptance set:
 *  (a) N-of-M browse — the shell steps prev/next through the queue via the
 *      canonical `SprkModal` + `nav` "N of M" counter (NOT a hand-rolled modal,
 *      NOT RecordNavigationModalShell), never returning to a grid to advance; the
 *      reader + tabs slot re-bind to each record.
 *  (b) attachment contents render folded into the reader as readable normalized
 *      TEXT (not file chips) — one continuous surface over body + attachment text.
 *  (c) "open original" opens an overlay preview (`PreviewModal`) hosting the
 *      reused `AttachmentList`.
 *  (d) the right pane exposes a slot hosting the three reconcile tabs.
 *  (e) NEGATIVE — an unextractable attachment does NOT crash: its content is
 *      omitted from the normalized text (a note shows instead) and open-original
 *      remains available.
 *  (f) dark mode (ADR-021) — renders under `webDarkTheme` with no console errors.
 *
 * `@spaarke/auth` is jest-mocked (this package never calls `initAuth()`); the
 * embedded `EmailBodyView` imports its free-function `authenticatedFetch`. Every
 * record here omits `emlDocumentId`, so the reader takes the `sprk_body`
 * degradation path (no fetch) and folds attachment text beneath it.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: [string, RequestInit?]) => mockAuthenticatedFetch(...args),
}));

import { ReconciliationBrowseShell } from '../ReconciliationBrowseShell';
import type { ReconciliationBrowseRecord } from '../ReconciliationBrowseShell.types';

function makeQueue(): ReconciliationBrowseRecord[] {
  return [
    {
      id: 'comm-1',
      subject: 'Quarterly filing update',
      from: 'jane.doe@example.com',
      to: 'counsel@spaarke.com',
      body: '<p>Please review the attached quarterly filing draft.</p>',
      attachments: [
        {
          attachmentId: 'att-1',
          name: 'Q3-filing.pdf',
          text: 'SECTION 1. The registrant hereby files its quarterly report for the period ended September 30.',
          documentId: 'doc-1',
        },
      ],
    },
    {
      id: 'comm-2',
      subject: 'Engagement letter countersigned',
      from: 'partner@firm.com',
      to: 'client@acme.com',
      body: '<p>Signed engagement letter enclosed.</p>',
    },
    {
      id: 'comm-3',
      subject: 'Wire instructions',
      from: 'ap@vendor.com',
      to: 'finance@spaarke.com',
      body: '<p>Remittance details below.</p>',
    },
  ];
}

function renderShell(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

describe('ReconciliationBrowseShell', () => {
  beforeEach(() => {
    mockAuthenticatedFetch.mockReset();
    // No record under test carries an `emlDocumentId`, so this should never be
    // called; if it were, reject → the reader's fallback branch (non-fatal).
    mockAuthenticatedFetch.mockRejectedValue(new Error('no eml archive in test'));
  });
  afterEach(() => jest.restoreAllMocks());

  it('steps N-of-M through the queue via SprkModal nav, never returning to a grid; reader + tabs re-bind', async () => {
    const onIndexChange = jest.fn();
    const renderTabs = (record: ReconciliationBrowseRecord) => (
      <div data-testid="tabs-marker">tabs for {record.id}</div>
    );

    renderShell(
      <ReconciliationBrowseShell
        open
        onClose={jest.fn()}
        queue={makeQueue()}
        initialIndex={0}
        onIndexChange={onIndexChange}
        renderTabs={renderTabs}
      />
    );

    // Canonical SprkModal "N of M" counter (single source, header nav group).
    expect(await screen.findByText('1 of 3')).toBeInTheDocument();
    // Reader bound to record 0; tabs slot bound to record 0.
    expect(await screen.findByTestId('reconciliation-browse-subject')).toHaveTextContent('Quarterly filing update');
    expect(screen.getByTestId('tabs-marker')).toHaveTextContent('tabs for comm-1');

    // Prev is disabled at the first record (cannot go before N=1).
    expect(screen.getByRole('button', { name: 'Previous record' })).toBeDisabled();

    // Advance — no grid involved; the shell steps internally.
    fireEvent.click(screen.getByRole('button', { name: 'Next record' }));

    expect(await screen.findByText('2 of 3')).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByTestId('reconciliation-browse-subject')).toHaveTextContent('Engagement letter countersigned')
    );
    expect(screen.getByTestId('tabs-marker')).toHaveTextContent('tabs for comm-2');
    expect(onIndexChange).toHaveBeenCalledWith(1, expect.objectContaining({ id: 'comm-2' }));

    // Step to the last record — Next becomes disabled (no return-to-grid needed).
    fireEvent.click(screen.getByRole('button', { name: 'Next record' }));
    expect(await screen.findByText('3 of 3')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next record' })).toBeDisabled();
  });

  it('folds attachment contents into the reader as readable normalized text (not chips)', async () => {
    renderShell(<ReconciliationBrowseShell open onClose={jest.fn()} queue={makeQueue()} initialIndex={0} />);

    // The email body renders (degradation path — no eml archive).
    expect(await screen.findByText(/Please review the attached quarterly filing draft/)).toBeInTheDocument();
    // The attachment's extracted CONTENT reads as text in the same reader surface.
    const foldText = await screen.findByTestId('email-body-attachment-text');
    expect(foldText).toHaveTextContent('SECTION 1. The registrant hereby files its quarterly report');
    // Its name labels the fold (a heading, not an interactive file chip/list row).
    expect(screen.getByText('Q3-filing.pdf')).toBeInTheDocument();
  });

  it('opens the raw attachment in an overlay preview (PreviewModal + AttachmentList) on "Open original"', async () => {
    const onOpenOriginalActivate = jest.fn();
    renderShell(
      <ReconciliationBrowseShell
        open
        onClose={jest.fn()}
        queue={makeQueue()}
        initialIndex={0}
        onOpenOriginalActivate={onOpenOriginalActivate}
      />
    );

    const openOriginal = await screen.findByTestId('email-body-attachment-open-original');
    fireEvent.click(openOriginal);

    // Overlay preview opens: a second dialog titled with the attachment name,
    // hosting the reused AttachmentList row (list semantics).
    const overlayRow = await screen.findByRole('listitem');
    expect(overlayRow).toBeInTheDocument();
    // Activating the overlay row invokes the host's real "open the file" seam.
    fireEvent.click(within(overlayRow).getByRole('button', { name: 'Q3-filing.pdf' }));
    expect(onOpenOriginalActivate).toHaveBeenCalledWith(
      expect.objectContaining({ attachmentId: 'att-1' }),
      expect.objectContaining({ id: 'comm-1' })
    );
  });

  it('exposes a right-pane slot that hosts caller-supplied reconcile tabs', async () => {
    renderShell(
      <ReconciliationBrowseShell
        open
        onClose={jest.fn()}
        queue={makeQueue()}
        initialIndex={0}
        renderTabs={(record, index) => (
          <div data-testid="tabs-marker">
            tab:{record.id}:{index}
          </div>
        )}
      />
    );

    const tabsPane = await screen.findByTestId('reconciliation-browse-tabs');
    expect(within(tabsPane).getByTestId('tabs-marker')).toHaveTextContent('tab:comm-1:0');
  });

  it('NEGATIVE — an unextractable attachment does not crash: shows a note, omits the text, keeps open-original', async () => {
    const queue: ReconciliationBrowseRecord[] = [
      {
        id: 'comm-x',
        subject: 'Scanned exhibit',
        from: 'a@b.com',
        to: 'c@d.com',
        body: '<p>See scanned exhibit.</p>',
        attachments: [
          {
            attachmentId: 'att-img',
            name: 'exhibit.png',
            extractable: false, // cannot extract to text (e.g. an image)
            documentId: 'doc-img',
          },
        ],
      },
    ];

    renderShell(<ReconciliationBrowseShell open onClose={jest.fn()} queue={queue} initialIndex={0} />);

    // No crash; the "not available as text" note stands in for the missing text.
    expect(await screen.findByTestId('email-body-attachment-unavailable')).toHaveTextContent(
      'Content not available as text'
    );
    // The text block is omitted (nothing to anchor into for this attachment).
    expect(screen.queryByTestId('email-body-attachment-text')).not.toBeInTheDocument();
    // Open-original remains available for the unextractable attachment.
    expect(screen.getByTestId('email-body-attachment-open-original')).toBeInTheDocument();
  });

  it('renders under the dark Fluent theme (ADR-021) with no console errors', async () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderShell(
      <ReconciliationBrowseShell open onClose={jest.fn()} queue={makeQueue()} initialIndex={0} />,
      webDarkTheme
    );

    expect(await screen.findByTestId('reconciliation-browse-subject')).toHaveTextContent('Quarterly filing update');
    await waitFor(() => expect(errorSpy).not.toHaveBeenCalled());
    errorSpy.mockRestore();
  });
});
