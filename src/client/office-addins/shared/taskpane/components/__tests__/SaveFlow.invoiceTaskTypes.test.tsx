/**
 * Task 133: Save's record types.
 *  - Slice A: Work Assignment exists in the add-in ONLY for the Invoice view's core-record picker.
 *  - Slice C (owner go 2026-11-20): Invoice left Save and "+ New" in both hosts. Save's picker, its "+ New" form and
 *    the "File to a record" picker offer Matter and Project only; an invoice remembered from an earlier save is not
 *    restored as the pre-selection. Invoices are still FOUND (the search types) and still shown as a document's
 *    record (useRelatedRecord) — reading one is unchanged.
 *
 * Harness mirrors `SaveFlow.filingAccess.test.tsx` (real SaveFlow + real RelatedToPicker).
 */
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import { fetchRelatedCandidates } from '../../services/communicationSuggestionsService';
import { ALL_ENTITY_TYPES } from '../../hooks/useEntitySearch';
import { SAVE_TARGET_TYPES, isSaveTargetType } from '../../hooks/useSaveFlow';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));
jest.mock('../../services/communicationSuggestionsService', () => ({
  fetchRelatedCandidates: jest.fn(),
}));

const TEST_TIMEOUT_MS = 60000;
const WAIT = { timeout: 10000 };

beforeEach(() => {
  global.fetch = jest.fn(async () => ({ ok: true, status: 200, json: async () => ({}) })) as unknown as typeof fetch;
  sessionStorage.clear();
  (fetchRelatedCandidates as jest.Mock).mockResolvedValue([]);
});

function pillNames(): string[] {
  const group = screen.getByRole('radiogroup', { name: 'Related to record type' });
  return within(group)
    .getAllByRole('radio')
    .map(r => r.textContent ?? '');
}

function renderSave(hostType: 'outlook' | 'word' = 'outlook') {
  render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType={hostType}
        itemId="AAMkAGI2TG93AAA="
        internetMessageId="<abc@contoso.com>"
        itemName="Invoice 4711"
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
}

describe('Save offers Matter and Project only (task 133 slice C)', () => {
  it(
    'must-fire: Outlook Save offers Matter and Project — no Invoice and no Work Assignment, in the picker or "+ New"',
    async () => {
      renderSave('outlook');
      await screen.findByLabelText('Search Matter records', {}, WAIT);
      expect(pillNames()).toEqual(['Matter', 'Project']);
      expect(screen.queryByRole('radio', { name: 'Invoice' })).toBeNull();

      await userEvent.click(screen.getByRole('button', { name: 'New' }));
      expect(await screen.findByText('Create New Record', {}, WAIT)).toBeInTheDocument();
      expect(pillNames()).toEqual(['Matter', 'Project']);
      expect(screen.queryByText(/Work ?Assignment/)).toBeNull();
      expect(screen.queryByLabelText('New Invoice name')).toBeNull();
    },
    TEST_TIMEOUT_MS
  );

  it(
    'must-fire: Word Save is the same shared picker — Matter and Project only',
    async () => {
      renderSave('word');
      await screen.findByLabelText('Search Matter records', {}, WAIT);
      expect(pillNames()).toEqual(['Matter', 'Project']);
    },
    TEST_TIMEOUT_MS
  );

  it(
    'must-not-fire: an invoice remembered from an earlier save is not restored as the selection; a matter still is',
    async () => {
      sessionStorage.setItem(
        'spaarke-last-association',
        JSON.stringify({ id: 'inv-1', entityType: 'Invoice', logicalName: 'sprk_invoice', name: 'INV-0042' })
      );
      const { unmount } = render(
        <FluentProvider theme={webLightTheme}>
          <SaveFlow
            hostType="outlook"
            itemId="AAMkAGI2TG93AAA="
            internetMessageId="<abc@contoso.com>"
            itemName="Invoice 4711"
            getAccessToken={jest.fn().mockResolvedValue('token')}
            apiBaseUrl="https://bff"
          />
        </FluentProvider>
      );
      await screen.findByLabelText('Search Matter records', {}, WAIT);
      expect(screen.queryByText('INV-0042')).toBeNull();
      unmount();

      // Control: the same restore path DOES bring back a matter, so the check above can fail.
      sessionStorage.setItem(
        'spaarke-last-association',
        JSON.stringify({ id: 'mat-1', entityType: 'Matter', logicalName: 'sprk_matter', name: 'Smith v Jones' })
      );
      renderSave('outlook');
      expect(await screen.findByText('Smith v Jones', {}, WAIT)).toBeInTheDocument();
    },
    TEST_TIMEOUT_MS
  );

  it('SAVE_TARGET_TYPES is exactly Matter and Project; reading types keep Invoice', () => {
    expect([...SAVE_TARGET_TYPES]).toEqual(['Matter', 'Project']);
    expect(isSaveTargetType('Invoice')).toBe(false);
    expect(isSaveTargetType('WorkAssignment')).toBe(false);
    expect(isSaveTargetType('Matter')).toBe(true);
    // Find and the Related-to card still read invoices.
    expect(ALL_ENTITY_TYPES).toContain('Invoice');
  });

  it('ALL_ENTITY_TYPES (the generic picker default) does not include WorkAssignment', () => {
    expect(ALL_ENTITY_TYPES).not.toContain('WorkAssignment');
  });
});
