/**
 * Task 121 (spaarkeai-word-add-in-r1): a pane email save sends the RFC Message-ID as `email.internetMessageId` and the
 * Exchange item id separately, as `email.exchangeItemId`.
 *
 * Before task 121 the pane sent the item id as `internetMessageId`, so every pane-saved `.eml` stored the item id in
 * `sprk_emailmessageid`, had no Message-ID header, and never linked to the communication that captured the same email
 * (task 120's live finding). The "Related to" suggestions were looked up by the item id too, which no captured
 * communication carries.
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE, the Profile section, the picker (a stub) and the suggestions
 * service are doubled.
 */
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import { fetchRelatedCandidates } from '../../services/communicationSuggestionsService';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../RelatedToPicker', () => {
  const ReactModule = jest.requireActual('react');
  return {
    RelatedToPicker: (props: { onChange: (e: unknown) => void }) =>
      ReactModule.createElement(
        'button',
        {
          type: 'button',
          onClick: () =>
            props.onChange({
              id: 'AAAAAAAA-0000-0000-0000-00000000000A',
              entityType: 'Matter',
              logicalName: 'sprk_matter',
              name: 'Acme v. Beta',
            }),
        },
        'Pick matter'
      ),
  };
});
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));
jest.mock('../../services/communicationSuggestionsService', () => ({
  ...jest.requireActual('../../services/communicationSuggestionsService'),
  fetchRelatedCandidates: jest.fn(),
}));
const mockFetchRelatedCandidates = fetchRelatedCandidates as jest.MockedFunction<typeof fetchRelatedCandidates>;

const mockFetch = jest.fn();
global.fetch = mockFetch;

Object.defineProperty(global.crypto, 'subtle', {
  configurable: true,
  value: { digest: jest.fn(async () => new Uint8Array(32).buffer) },
});

const RFC_ID = '<CAF0a1b2c3@mail.example.com>';
const ITEM_ID = 'AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def=';

function OutlookPane(props: { internetMessageId?: string; itemId?: string; canSuggestRelatedRecords?: boolean }) {
  return (
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="outlook"
        {...(props.itemId !== undefined ? { itemId: props.itemId } : {})}
        {...(props.internetMessageId !== undefined ? { internetMessageId: props.internetMessageId } : {})}
        itemName="RE: Discovery schedule"
        senderEmail="opposing@counsel.example"
        attachments={[]}
        canSuggestRelatedRecords={props.canSuggestRelatedRecords ?? false}
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
}

const saveCalls = () =>
  mockFetch.mock.calls.filter(
    ([url, init]) => String(url).includes('/api/office/save') && (init as RequestInit | undefined)?.method === 'POST'
  );

async function saveAndReadEmail(): Promise<Record<string, unknown>> {
  fireEvent.click(screen.getByRole('button', { name: 'Pick matter' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(saveCalls().length).toBeGreaterThanOrEqual(1));
  return JSON.parse(String((saveCalls()[0]![1] as RequestInit).body)).email;
}

beforeEach(() => {
  jest.clearAllMocks();
  mockFetch.mockReset();
  mockFetch.mockResolvedValue({ ok: true, status: 200, json: async () => ({}), text: async () => '{}' });
  mockFetchRelatedCandidates.mockResolvedValue([]);
});

describe('SaveFlow — the email keys a pane save sends (task 121)', () => {
  it('sends the RFC Message-ID as internetMessageId and the Exchange item id as exchangeItemId', async () => {
    render(<OutlookPane itemId={ITEM_ID} internetMessageId={RFC_ID} />);

    const email = await saveAndReadEmail();

    expect(email.internetMessageId).toBe(RFC_ID);
    expect(email.exchangeItemId).toBe(ITEM_ID);
  });

  it('a compose item (no Message-ID yet) sends the item id alone — never as internetMessageId', async () => {
    render(<OutlookPane itemId={ITEM_ID} />);

    const email = await saveAndReadEmail();

    expect('internetMessageId' in email).toBe(false);
    expect(email.exchangeItemId).toBe(ITEM_ID);
  });

  it('looks the "Related to" suggestions up by the RFC Message-ID, not the item id', async () => {
    render(<OutlookPane itemId={ITEM_ID} internetMessageId={RFC_ID} canSuggestRelatedRecords />);

    await waitFor(() => expect(mockFetchRelatedCandidates).toHaveBeenCalledTimes(1));
    expect(mockFetchRelatedCandidates).toHaveBeenCalledWith(RFC_ID);
  });

  it('asks for no suggestions without a Message-ID (the item id matches no captured communication)', async () => {
    render(<OutlookPane itemId={ITEM_ID} canSuggestRelatedRecords />);

    await screen.findByRole('button', { name: 'Pick matter' });
    expect(mockFetchRelatedCandidates).not.toHaveBeenCalled();
  });
});
