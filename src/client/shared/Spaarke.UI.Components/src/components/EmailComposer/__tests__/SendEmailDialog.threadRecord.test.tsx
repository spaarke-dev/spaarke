/**
 * SendEmailDialog.threadRecord.test.tsx (task 020, FR-07/FR-19)
 *
 * The R3 additive extension of `<SendEmailDialog/>`: an email opened from a
 * conversation must (a) carry the active `threadId` into the send payload so
 * the backend pins it (FR-19) and (b) auto-associate to the regarding record
 * via the EXISTING association mechanism (ADR-024 — one more
 * `ICommunicationAssociation`, NOT a second path). Plus an optional record
 * link renders in the composer.
 *
 * The regression guard is `SendEmailDialog.characterize.test.tsx` (task 010):
 * these props are OPTIONAL and, when omitted, the payload must match that
 * characterized baseline exactly. That "no-regression" case is asserted here
 * too (no `threadId`, no extra association).
 *
 * Injected fake `authenticatedFetch` per ADR-028/ADR-038; PlainText body to
 * avoid the Lexical rich-text editor (same rationale as the characterization
 * test).
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { SendEmailDialog, type ISendEmailDialogProps } from '../wrappers/SendEmailDialog';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';

const BFF = 'https://bff.example.com';
// Bare Dataverse GUIDs — the BFF binds threadId to Guid? and entityId is a record id.
const THREAD_ID = 'a1a1a1a1-1111-1111-1111-111111111111';
const MATTER_ID = 'b2b2b2b2-2222-2222-2222-222222222222';

function renderDialog(overrides: Partial<ISendEmailDialogProps> = {}, theme: typeof webLightTheme = webLightTheme) {
  const authenticatedFetch = jest.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ communicationId: 'comm-123' }),
  } as Response);
  const onClose = jest.fn();

  const utils = render(
    <FluentProvider theme={theme}>
      <SendEmailDialog
        open
        onClose={onClose}
        authenticatedFetch={authenticatedFetch as unknown as AuthenticatedFetchFn}
        bffBaseUrl={BFF}
        initialTo={['alice@example.com']}
        initialSubject="Documents for review"
        initialBody="Please see attached."
        initialBodyFormat="PlainText"
        {...overrides}
      />
    </FluentProvider>
  );

  return { ...utils, authenticatedFetch, onClose };
}

async function clickSendAndReadBody(authenticatedFetch: jest.Mock): Promise<Record<string, unknown>> {
  fireEvent.click(screen.getByRole('button', { name: /send/i }));
  await waitFor(() => expect(authenticatedFetch).toHaveBeenCalledTimes(1));
  const [, init] = authenticatedFetch.mock.calls[0] as [string, RequestInit];
  return JSON.parse(init.body as string) as Record<string, unknown>;
}

describe('SendEmailDialog — thread pin + auto-association (FR-07/FR-19)', () => {
  it('carries threadId into the send payload AND auto-associates the regarding record (existing mechanism)', async () => {
    const { authenticatedFetch } = renderDialog({
      threadId: THREAD_ID,
      regarding: { entityType: 'sprk_matter', id: MATTER_ID, name: 'Acme v. Beta' },
    });

    // The regarding record renders as an association chip (the EXISTING AssociationChips mechanism).
    expect(screen.getByText(/Acme v\. Beta/)).toBeInTheDocument();

    const body = await clickSendAndReadBody(authenticatedFetch);
    // FR-19: the active thread is pinned via the existing send path (no new branch).
    expect(body.threadId).toBe(THREAD_ID);
    // ADR-024: auto-associated via the same associations payload sendCommunication already sends.
    expect(body.associations).toEqual([
      expect.objectContaining({ entityType: 'sprk_matter', entityId: MATTER_ID, entityName: 'Acme v. Beta' }),
    ]);
    // Still the canonical Email send path.
    expect(body).toMatchObject({ communicationType: 'Email' });
  });

  it('dedups regarding against an explicit association case-insensitively on the GUID (single mechanism)', async () => {
    const { authenticatedFetch } = renderDialog({
      regarding: { entityType: 'sprk_matter', id: MATTER_ID.toLowerCase(), name: 'Acme v. Beta' },
      // Same record, DIFFERENT case — must still dedup to one association.
      associations: [{ entityType: 'sprk_matter', entityId: MATTER_ID.toUpperCase(), entityName: 'Acme (explicit)' }],
    });

    const body = await clickSendAndReadBody(authenticatedFetch);
    expect(body.associations).toHaveLength(1);
  });

  it('NO-REGRESSION: with no threadId/regarding the payload matches the characterized baseline (no threadId, no association)', async () => {
    const { authenticatedFetch } = renderDialog();

    const body = await clickSendAndReadBody(authenticatedFetch);
    expect(body.threadId).toBeUndefined();
    expect(body.associations).toEqual([]);
    // Baseline payload shape unchanged (same assertions as the characterization test).
    expect(body).toMatchObject({
      to: ['alice@example.com'],
      subject: 'Documents for review',
      body: 'Please see attached.',
      bodyFormat: 'PlainText',
      communicationType: 'Email',
      sendMode: 'SharedMailbox',
    });
  });

  it('renders the optional record link in the composer when recordLink is provided', () => {
    renderDialog({ recordLink: { url: 'https://crm.example.com/matter/9', label: 'Open matter record' } });
    const link = screen.getByRole('link', { name: 'Open matter record' });
    expect(link).toBeInTheDocument();
    expect(link).toHaveAttribute('href', 'https://crm.example.com/matter/9');
  });

  it('degrades an unsafe-scheme recordLink url to a non-clickable label (no javascript: href)', () => {
    renderDialog({ recordLink: { url: 'javascript:alert(1)', label: 'Open matter record' } });
    expect(screen.getByText('Open matter record')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Open matter record' })).toBeNull();
  });

  it('renders the extension under a dark-theme FluentProvider without hand-authored inline colors (ADR-021)', () => {
    const { container } = renderDialog(
      {
        threadId: 'thread-1',
        regarding: { entityType: 'sprk_matter', id: 'matter-9', name: 'Acme v. Beta' },
        recordLink: { url: 'https://crm.example.com/matter/9', label: 'Open matter record' },
      },
      webDarkTheme
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    const styledEls = container.querySelectorAll<HTMLElement>('[style]');
    for (const el of Array.from(styledEls)) {
      expect(el.style.color).toBe('');
      expect(el.style.backgroundColor).toBe('');
    }
  });
});
