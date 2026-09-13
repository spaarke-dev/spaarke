/**
 * spaarkeai-word-add-in-r1 task 038, end to end through the pane: Matter quick-create's
 * `POST /api/office/quickcreate/matter` body carries `matterTypeId` as a bare lowercase GUID
 * (ADR-044), Project/Invoice quick-create carries no `matterTypeId`, and the pane never sends a
 * matter number. Unlike `SaveFlow.versionMode.test.tsx`, `RelatedToPicker` is NOT mocked here — it is
 * the component under test for the create-form wiring; `DocumentProfileSection` and `SseClient` are
 * mocked (unrelated to this path, matches the sibling suite's pattern).
 */
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import { clearMatterTypesCache } from '../../services/matterTypeLookupService';

const MATTER_TYPES_CACHE_KEY = 'spaarke.officeAddin.matterTypes.v1';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

// Real-timer userEvent typing + a Dropdown popup interaction runs close to the file's default 10s
// testTimeout; both tests here get a longer budget to avoid flakiness under load.
const TEST_TIMEOUT_MS = 20000;

// jsdom has no ResizeObserver; Fluent's Dropdown/MessageBar need one to render.
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
Object.defineProperty(window, 'ResizeObserver', { configurable: true, writable: true, value: ResizeObserverStub });

const MATTER_TYPES_RESPONSE = {
  results: [
    { id: '11AED095-30DA-F011-8406-7CED8D1DC988', name: 'Litigation', code: 'LITG' },
    { id: '46c35aa2-30da-f011-8406-7ced8d1dc988', name: 'Patent', code: 'PAT' },
  ],
};

type FakeResponse = { ok: boolean; status: number; json: () => Promise<unknown> };
const json = (ok: boolean, status: number, body: unknown): FakeResponse => ({ ok, status, json: async () => body });

const mockFetch = jest.fn();

beforeEach(() => {
  jest.clearAllMocks();
  mockFetch.mockReset();
  // Full isolation for the matter-types cache (owner decision 2026-09-13) between tests — each test
  // that cares seeds exactly the cache state it needs.
  clearMatterTypesCache();
  mockFetch.mockImplementation(async (url: string) => {
    const u = String(url);
    if (u.includes('/api/office/search/matter-types')) {
      return json(true, 200, MATTER_TYPES_RESPONSE);
    }
    if (u.includes('/api/office/quickcreate/matter')) {
      return json(true, 201, { id: 'new-matter-1', logicalName: 'sprk_matter', name: 'Acme Litigation' });
    }
    if (u.includes('/api/office/quickcreate/project')) {
      return json(true, 201, { id: 'new-project-1', logicalName: 'sprk_project', name: 'Acme Project' });
    }
    // Related-candidates / other GETs the pane may issue — never exercised for hostType="word".
    return json(true, 200, {});
  });
  global.fetch = mockFetch as unknown as typeof fetch;
});

function renderPane() {
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

async function quickCreateCall(entityType: 'matter' | 'project') {
  await waitFor(() =>
    expect(mockFetch.mock.calls.some(([u]) => String(u).includes(`/api/office/quickcreate/${entityType}`))).toBe(true)
  );
  const [, init] = mockFetch.mock.calls.find(([u]) => String(u).includes(`/api/office/quickcreate/${entityType}`))!;
  return JSON.parse(String((init as RequestInit).body)) as Record<string, unknown>;
}

describe('SaveFlow — Matter quick-create sends matterTypeId (task 038)', () => {
  it(
    'Matter: the POST body carries matterTypeId as a bare lowercase GUID, and no matter number',
    async () => {
      renderPane();

      // Matter is the default chip; the picker loads its matter-type list on mount.
      await userEvent.click(await screen.findByRole('button', { name: 'New' }));
      await userEvent.type(screen.getByLabelText('New Matter name'), 'Acme Litigation');

      await waitFor(() => expect(screen.getByRole('combobox', { name: 'Matter Type' })).toBeEnabled());
      await userEvent.click(screen.getByRole('combobox', { name: 'Matter Type' }));
      await userEvent.click(await screen.findByRole('option', { name: 'Litigation' }));
      await userEvent.click(screen.getByRole('button', { name: 'Create' }));

      const body = await quickCreateCall('matter');
      expect(body).toEqual({ name: 'Acme Litigation', matterTypeId: '11aed095-30da-f011-8406-7ced8d1dc988' });
      expect(Object.keys(body).some(k => /number/i.test(k))).toBe(false);
    },
    TEST_TIMEOUT_MS
  );

  it(
    'Project: the POST body carries no matterTypeId, and behaviour is unchanged',
    async () => {
      renderPane();

      await userEvent.click(screen.getByRole('radio', { name: 'Project' }));
      await userEvent.click(screen.getByRole('button', { name: 'New' }));
      await userEvent.type(screen.getByLabelText('New Project name'), 'Acme Project');
      await userEvent.click(screen.getByRole('button', { name: 'Create' }));

      const body = await quickCreateCall('project');
      expect(body).toEqual({ name: 'Acme Project' });
    },
    TEST_TIMEOUT_MS
  );

  it(
    'Matter: a "not found" quick-create warning clears the matter-types cache (owner decision 2026-09-13)',
    async () => {
      // Seed the cache directly (deterministic regardless of other tests) and prove the picker is
      // served from it — the search/matter-types GET must never fire.
      window.localStorage.setItem(
        MATTER_TYPES_CACHE_KEY,
        JSON.stringify({
          fetchedAt: Date.now(),
          items: [{ id: '11aed095-30da-f011-8406-7ced8d1dc988', name: 'Litigation', code: 'LITG' }],
        })
      );
      mockFetch.mockImplementation(async (url: string) => {
        const u = String(url);
        if (u.includes('/api/office/search/matter-types')) {
          throw new Error('must be served from the cache, not fetched');
        }
        if (u.includes('/api/office/quickcreate/matter')) {
          return json(true, 201, {
            id: 'new-matter-2',
            logicalName: 'sprk_matter',
            name: 'Acme Litigation',
            warnings: ['The selected matter type was not found; the matter was created without it.'],
          });
        }
        return json(true, 200, {});
      });

      renderPane();

      await userEvent.click(await screen.findByRole('button', { name: 'New' }));
      await userEvent.type(screen.getByLabelText('New Matter name'), 'Acme Litigation');
      await waitFor(() => expect(screen.getByRole('combobox', { name: 'Matter Type' })).toBeEnabled());
      await userEvent.click(screen.getByRole('combobox', { name: 'Matter Type' }));
      await userEvent.click(await screen.findByRole('option', { name: 'Litigation' }));
      await userEvent.click(screen.getByRole('button', { name: 'Create' }));

      await quickCreateCall('matter');
      await waitFor(() => expect(window.localStorage.getItem(MATTER_TYPES_CACHE_KEY)).toBeNull());
    },
    TEST_TIMEOUT_MS
  );
});
