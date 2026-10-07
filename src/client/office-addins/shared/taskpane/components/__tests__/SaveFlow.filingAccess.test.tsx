/**
 * Task 084 (spaarkeai-word-add-in-r1, #1037 — "pickable equals savable"), the pane WIRING half.
 * `RelatedToPicker.filingAccess.test.tsx` pins how a `canFile === false` record renders; this suite pins
 * that the real `SaveFlow` gets that value to the picker at all:
 *   - `relatedSearch` asks for filing access (`access=file` on `GET /api/office/search/entities`);
 *   - its EXPLICIT field map carries `canFile` (a field the map does not list is silently dropped, so a
 *     record the save would refuse would render as selectable again);
 *   - Outlook's suggestion cards (`fetchRelatedCandidates`) reach the picker with their `canFile`, and a
 *     non-fileable one is never pre-selected.
 *
 * NEW FILE — ADR-038 (new tests in new files); harness mirrors `SaveFlow.quickCreateErrorSurfacing.test.tsx`
 * (real `SaveFlow` + real `RelatedToPicker`; `DocumentProfileSection` + `SseClient` mocked as unrelated).
 */
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import { fetchRelatedCandidates, type RelatedCandidate } from '../../services/communicationSuggestionsService';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));
jest.mock('../../services/communicationSuggestionsService', () => ({
  fetchRelatedCandidates: jest.fn(),
}));

const mockFetchRelatedCandidates = fetchRelatedCandidates as jest.MockedFunction<typeof fetchRelatedCandidates>;

// Same budget rationale as the sibling SaveFlow suites: real-timer typing under parallel jest load can
// approach the package's default 10s testTimeout. Every network call here is mocked.
const TEST_TIMEOUT_MS = 60000;
const WAIT = { timeout: 10000 };

/** The owner's copy, VERBATIM (task 084 constraint "exact copy"). */
const REASON = "You can view this record but can't file to it. Filing needs Append To permission on the record.";

type FakeResponse = { ok: boolean; status: number; json: () => Promise<unknown> };
const json = (ok: boolean, status: number, body: unknown): FakeResponse => ({ ok, status, json: async () => body });

const mockFetch = jest.fn();

beforeEach(() => {
  jest.clearAllMocks();
  mockFetch.mockReset();
  global.fetch = mockFetch as unknown as typeof fetch;
  // The pane restores the last selected record from sessionStorage; start every test with none.
  sessionStorage.clear();
  mockFetchRelatedCandidates.mockResolvedValue([]);
});

/** The search route's wire rows: one the caller cannot file to, one they can. */
const SEARCH_ROWS = [
  {
    id: 'bbbbbbbb-0000-0000-0000-000000000001',
    entityType: 'Matter',
    logicalName: 'sprk_matter',
    name: 'Secure Matter',
    displayInfo: 'SEC-001',
    canFile: false,
  },
  {
    id: 'bbbbbbbb-0000-0000-0000-000000000002',
    entityType: 'Matter',
    logicalName: 'sprk_matter',
    name: 'Open Matter',
    displayInfo: 'OPN-002',
    canFile: true,
  },
];

function searchUrls(): string[] {
  return mockFetch.mock.calls.map(c => String(c[0])).filter(u => u.includes('/api/office/search/entities'));
}

function renderWordPane() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="word"
        itemId="https://contoso.sharepoint.com/Brief.docx"
        itemName="Brief.docx"
        documentContentBase64="UEsDBBQAAAAIAAAAIQA="
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
}

function blockedCardFor(reasonEl: HTMLElement): HTMLElement {
  const card = reasonEl.closest('[aria-disabled="true"]');
  if (!(card instanceof HTMLElement)) throw new Error('reason text is not inside an aria-disabled card');
  return card;
}

describe('SaveFlow — the Save picker search asks for, and carries, filing access (task 084)', () => {
  it(
    'sends access=file on /api/office/search/entities',
    async () => {
      mockFetch.mockImplementation(async (url: string) =>
        String(url).includes('/api/office/search/entities')
          ? json(true, 200, { results: SEARCH_ROWS })
          : json(true, 200, {})
      );

      renderWordPane();
      await userEvent.type(await screen.findByLabelText('Search Matter records', {}, WAIT), 'Matter');
      await userEvent.click(screen.getByRole('button', { name: 'Search' }));

      await waitFor(() => expect(searchUrls()).toHaveLength(1), WAIT);
      const url = new URL(searchUrls()[0]!);
      expect(url.searchParams.get('access')).toBe('file');
      // The rest of the request is unchanged.
      expect(url.searchParams.get('q')).toBe('Matter');
      expect(url.searchParams.get('type')).toBe('Matter');
      expect(url.searchParams.get('top')).toBe('10');
    },
    TEST_TIMEOUT_MS
  );

  it(
    'a canFile:false row from the response reaches the picker DISABLED with the reason; the canFile:true row still selects',
    async () => {
      mockFetch.mockImplementation(async (url: string) =>
        String(url).includes('/api/office/search/entities')
          ? json(true, 200, { results: SEARCH_ROWS })
          : json(true, 200, {})
      );

      renderWordPane();
      await userEvent.type(await screen.findByLabelText('Search Matter records', {}, WAIT), 'Matter');
      await userEvent.click(screen.getByRole('button', { name: 'Search' }));

      const card = blockedCardFor(await screen.findByText(REASON, {}, WAIT));
      expect(card).toHaveAttribute('aria-disabled', 'true');
      expect(within(card).getByText('SEC-001 : Secure Matter')).toBeInTheDocument();
      expect(within(card).queryAllByRole('button')).toHaveLength(0);

      // Exactly one select control — the fileable row's — and it still works.
      const selects = screen.getAllByRole('button', { name: 'Select this record' });
      expect(selects).toHaveLength(1);
      await userEvent.click(selects[0]!);
      expect(await screen.findByRole('button', { name: 'Selected — click to clear' }, WAIT)).toBeInTheDocument();
      // The selected one is the fileable row, not the blocked one.
      expect(within(card).queryByRole('button', { name: 'Selected — click to clear' })).toBeNull();
    },
    TEST_TIMEOUT_MS
  );
});

describe('SaveFlow — Outlook suggestion cards carry filing access and never pre-select a non-fileable one (task 084)', () => {
  it(
    'a candidate the caller cannot file to renders disabled and is not selected',
    async () => {
      mockFetch.mockImplementation(async () => json(true, 200, {}));
      const blocked: RelatedCandidate = {
        id: 'cccccccc-0000-0000-0000-000000000001',
        entityType: 'Matter',
        logicalName: 'sprk_matter',
        name: 'Secure Matter',
        displayInfo: 'SEC-001',
        confidence: 0.99,
        canFile: false,
      };
      const fileable: RelatedCandidate = {
        id: 'cccccccc-0000-0000-0000-000000000002',
        entityType: 'Matter',
        logicalName: 'sprk_matter',
        name: 'Open Matter',
        confidence: 0.9,
      };
      mockFetchRelatedCandidates.mockResolvedValue([blocked, fileable]);

      render(
        <FluentProvider theme={webLightTheme}>
          <SaveFlow
            hostType="outlook"
            itemId="<abc@contoso.com>"
            itemName="Re: Secure matter"
            canSuggestRelatedRecords
            getAccessToken={jest.fn().mockResolvedValue('token')}
            apiBaseUrl="https://bff"
          />
        </FluentProvider>
      );

      const card = blockedCardFor(await screen.findByText(REASON, {}, WAIT));
      expect(mockFetchRelatedCandidates).toHaveBeenCalledWith('<abc@contoso.com>');
      expect(card).toHaveAttribute('aria-disabled', 'true');
      expect(within(card).queryAllByRole('button')).toHaveLength(0);
      // Nothing is pre-selected — not the blocked top match, not anything else.
      expect(screen.queryByRole('button', { name: 'Selected — click to clear' })).toBeNull();
      expect(screen.getAllByRole('button', { name: 'Select this record' })).toHaveLength(1);
    },
    TEST_TIMEOUT_MS
  );
});
