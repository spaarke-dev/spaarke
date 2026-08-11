/**
 * ReconciliationBrowseShell.citation.test.tsx — citation navigation (task 054,
 * NFR-11). Drives `activeCitation` through the shell → `EmailBodyView` and asserts
 * the closed acceptance set: a body-sourced quote jumps to + highlights the exact
 * passage (cited-passage callout — the `.eml`/HTML body can't be injected in place,
 * so the exact normalized passage is shown highlighted); an attachment-sourced
 * quote highlights inline in the folded attachment text; a forged/absent quote
 * surfaces "source not locatable" and does NOT highlight; and the highlight renders
 * under the dark theme with no console errors. The anchor is resolved by the Path-A
 * quoted-text `resolveQuotedCitation` — NOT the legal-number composeCitationResolver.
 */
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: [string, RequestInit?]) => mockAuthenticatedFetch(...args),
}));

import { ReconciliationBrowseShell } from '../ReconciliationBrowseShell';
import type { ReconciliationBrowseRecord } from '../ReconciliationBrowseShell.types';
import type { EmailCitation } from '../../../logic/citations';

const RECORD: ReconciliationBrowseRecord = {
  id: 'comm-1',
  subject: 'Quarterly filing update',
  from: 'jane.doe@example.com',
  to: 'counsel@spaarke.com',
  body: '<p>Please review the attached quarterly filing draft before the deadline.</p>',
  attachments: [
    {
      attachmentId: 'att-1',
      name: 'Q3-filing.pdf',
      text: 'SECTION 1. The registrant hereby files its quarterly report for the period ended September 30.',
      documentId: 'doc-1',
    },
  ],
};

function renderWithCitation(citation: EmailCitation, theme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <ReconciliationBrowseShell open onClose={jest.fn()} queue={[RECORD]} initialIndex={0} activeCitation={citation} />
    </FluentProvider>
  );
}

describe('ReconciliationBrowseShell — citation navigation', () => {
  beforeEach(() => {
    mockAuthenticatedFetch.mockReset();
    mockAuthenticatedFetch.mockRejectedValue(new Error('no eml archive in test'));
  });
  afterEach(() => jest.restoreAllMocks());

  it('jumps to + highlights a BODY-sourced citation in the cited-passage callout', async () => {
    renderWithCitation({ source: 'body', locator: 'body: sentence 1', quotedText: 'quarterly filing draft' });

    const banner = await screen.findByTestId('email-body-citation-banner');
    expect(banner).toBeInTheDocument();
    const mark = await screen.findByTestId('citation-highlight-mark');
    expect(mark).toHaveTextContent('quarterly filing draft');
    // Not-locatable affordance is absent when the quote resolves.
    expect(screen.queryByTestId('email-body-citation-not-locatable')).not.toBeInTheDocument();
  });

  it('jumps to + highlights an ATTACHMENT-sourced citation inline in the folded text', async () => {
    renderWithCitation({
      source: 'Q3-filing.pdf',
      locator: 'Q3-filing.pdf p.1',
      quotedText: 'registrant hereby files',
    });

    const mark = await screen.findByTestId('citation-highlight-mark');
    expect(mark).toHaveTextContent('registrant hereby files');
    // The highlight lives inside the attachment fold, not a body callout.
    expect(screen.queryByTestId('email-body-citation-banner')).not.toBeInTheDocument();
    expect(screen.getByTestId('email-body-attachment-fold')).toContainElement(mark);
  });

  it('surfaces "source not locatable" for a forged/absent quote and does NOT highlight', async () => {
    renderWithCitation({ source: 'body', quotedText: 'this sentence was never in the email' });

    expect(await screen.findByTestId('email-body-citation-not-locatable')).toHaveTextContent('Source not locatable');
    expect(screen.queryByTestId('citation-highlight-mark')).not.toBeInTheDocument();
  });

  it('renders the highlight under the dark Fluent theme (ADR-021) with no console errors', async () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderWithCitation({ source: 'body', quotedText: 'quarterly filing draft' }, webDarkTheme);

    expect(await screen.findByTestId('citation-highlight-mark')).toHaveTextContent('quarterly filing draft');
    await waitFor(() => expect(errorSpy).not.toHaveBeenCalled());
    errorSpy.mockRestore();
  });
});
