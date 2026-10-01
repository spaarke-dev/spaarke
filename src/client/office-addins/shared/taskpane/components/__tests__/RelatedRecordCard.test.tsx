/**
 * Unit tests for RelatedRecordCard (spaarkeai-word-add-in-r1 task 026, FR-09).
 *
 * Covers:
 * - Absent state: no `documentIdentity`, `'checking'`, and every non-`'resolved'` outcome ('new' /
 *   'conflict' / 'indeterminate' / 'denied' / 'error') all render NOTHING (AC7 — the card is absent).
 * - Unassociated state: a resolved identity with no related record shows an explicit
 *   "not filed to a record" message — never a blank region (AC5).
 * - Associated state: type badge, descriptive name and number render for a Matter (whose primary name IS
 *   its number, per the server-side field-mapping fix task 026 makes) and for a WorkAssignment (whose
 *   friendly label — "Work Assignment" — differs from its logical name).
 * - A blank number (a pane-created Matter with no number yet) renders "No number yet", not an error, not a
 *   blank field (AC1 / the numbering hand-off, notes/030-numbering-handoff.md).
 * - A null descriptive name renders "Unnamed record" rather than an empty heading.
 * - An unrecognized entity type falls back to its raw logical name as the type label (defensive — the
 *   server only ever sends one of the four direct slots today).
 * - The `onOpenRecord` click seam (task 027 / FR-10): the Open button appears and fires the callback with
 *   the card's view model ONLY when the prop is supplied; absent it, the card is plain, non-interactive
 *   text — never a button with nothing to do.
 */

import React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RelatedRecordCard } from '../RelatedRecordCard';
import type { DocumentIdentityState, ResolvedRelatedRecord } from '../../services/documentIdentityService';

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

function resolved(relatedRecord: ResolvedRelatedRecord | null): DocumentIdentityState {
  return {
    kind: 'resolved',
    documentId: '11111111-1111-1111-1111-111111111111',
    documentName: 'Examiner report draft',
    fileName: 'Examiner report draft.docx',
    relatedRecord,
  };
}

// FluentProvider always renders its own wrapper <div> — "the card is absent" means RelatedRecordCard
// itself renders null INSIDE that wrapper, not that the wrapper is absent too.
function expectNothingRendered(container: HTMLElement): void {
  expect(container.firstChild).toBeEmptyDOMElement();
}

describe('RelatedRecordCard — absent state (AC7)', () => {
  it('renders nothing when documentIdentity is undefined (host has no identity capability)', () => {
    const { container } = renderWithProvider(<RelatedRecordCard />);
    expectNothingRendered(container);
  });

  it('renders nothing while identity is still checking', () => {
    const { container } = renderWithProvider(<RelatedRecordCard documentIdentity="checking" />);
    expectNothingRendered(container);
  });

  it.each<[string, DocumentIdentityState]>([
    ['new', { kind: 'new', reason: 'not_spaarke_document' }],
    ['conflict', { kind: 'conflict' }],
    ['indeterminate', { kind: 'indeterminate', reason: 'unavailable' }],
    ['denied', { kind: 'denied' }],
    ['error', { kind: 'error', message: 'boom' }],
  ])('renders nothing for a %s outcome (not an identified document)', (_label, identity) => {
    const { container } = renderWithProvider(<RelatedRecordCard documentIdentity={identity} />);
    expectNothingRendered(container);
  });
});

describe('RelatedRecordCard — unassociated state (AC5)', () => {
  it('shows an explicit not-filed message, never a blank region', () => {
    renderWithProvider(<RelatedRecordCard documentIdentity={resolved(null)} />);
    expect(screen.getByText(/not filed to a record yet/i)).toBeInTheDocument();
  });
});

describe('RelatedRecordCard — associated state', () => {
  it('shows type, descriptive name and number for a Matter', () => {
    renderWithProvider(
      <RelatedRecordCard
        documentIdentity={resolved({
          entityType: 'sprk_matter',
          id: '22222222-2222-2222-2222-222222222222',
          name: 'PAT-191111',
          displayName: 'Acme v Globex',
          number: 'PAT-191111',
        })}
      />
    );
    expect(screen.getByText('Matter')).toBeInTheDocument();
    expect(screen.getByText('Acme v Globex')).toBeInTheDocument();
    expect(screen.getByText('PAT-191111')).toBeInTheDocument();
  });

  it('shows the "Work Assignment" friendly label (differs from its logical name)', () => {
    renderWithProvider(
      <RelatedRecordCard
        documentIdentity={resolved({
          entityType: 'sprk_workassignment',
          id: '33333333-3333-3333-3333-333333333333',
          name: 'Discovery response',
          displayName: 'Discovery response',
          number: 'WA-000117',
        })}
      />
    );
    expect(screen.getByText('Work Assignment')).toBeInTheDocument();
    expect(screen.getByText('Discovery response')).toBeInTheDocument();
    expect(screen.getByText('WA-000117')).toBeInTheDocument();
  });

  it('renders a blank number as "No number yet", not an error (a pane-created Matter has none yet)', () => {
    renderWithProvider(
      <RelatedRecordCard
        documentIdentity={resolved({
          entityType: 'sprk_matter',
          id: '44444444-4444-4444-4444-444444444444',
          name: null,
          displayName: 'Beta Corp Litigation',
          number: null,
        })}
      />
    );
    expect(screen.getByText('Beta Corp Litigation')).toBeInTheDocument();
    expect(screen.getByText(/no number yet/i)).toBeInTheDocument();
  });

  it('renders "Unnamed record" when the descriptive name is null', () => {
    renderWithProvider(
      <RelatedRecordCard
        documentIdentity={resolved({
          entityType: 'sprk_invoice',
          id: '55555555-5555-5555-5555-555555555555',
          name: null,
          displayName: null,
          number: 'INV-000482',
        })}
      />
    );
    expect(screen.getByText(/unnamed record/i)).toBeInTheDocument();
    expect(screen.getByText('INV-000482')).toBeInTheDocument();
  });

  it('falls back to the raw logical name for an unrecognized entity type', () => {
    renderWithProvider(
      <RelatedRecordCard
        documentIdentity={resolved({
          entityType: 'sprk_somethingnew',
          id: '66666666-6666-6666-6666-666666666666',
          name: 'X',
          displayName: 'X',
          number: null,
        })}
      />
    );
    expect(screen.getByText('sprk_somethingnew')).toBeInTheDocument();
  });
});

describe('RelatedRecordCard — task 027 click seam', () => {
  const identity = resolved({
    entityType: 'sprk_matter',
    id: '77777777-7777-7777-7777-777777777777',
    name: 'PAT-100000',
    displayName: 'Gamma Merger',
    number: 'PAT-100000',
  });

  it('renders no button when onOpenRecord is not supplied', () => {
    renderWithProvider(<RelatedRecordCard documentIdentity={identity} />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('fires onOpenRecord with the card view model when Open is clicked', async () => {
    const user = userEvent.setup();
    const onOpenRecord = jest.fn();
    renderWithProvider(<RelatedRecordCard documentIdentity={identity} onOpenRecord={onOpenRecord} />);

    await user.click(screen.getByRole('button', { name: /open/i }));

    expect(onOpenRecord).toHaveBeenCalledTimes(1);
    expect(onOpenRecord).toHaveBeenCalledWith(
      expect.objectContaining({
        entityType: 'sprk_matter',
        id: '77777777-7777-7777-7777-777777777777',
        type: 'Matter',
        displayName: 'Gamma Merger',
        number: 'PAT-100000',
      })
    );
  });
});
