/**
 * Task 046 (b), spaarkeai-word-add-in-r1: what the Outlook pane tells the server about an email's name.
 *
 * The server stores a SYSTEM-DERIVED .eml name with a short unique suffix, so two same-subject, same-date emails
 * never share a file. It must never change a name the user TYPED in the pane's Document Name box. The only way the
 * server can tell the two apart is `email.isNameSystemDerived`, which is what these tests pin.
 */
import { renderHook, act } from '@testing-library/react';
import { createHash } from 'crypto';
import { useSaveFlow, type SaveFlowContext } from '../useSaveFlow';
import type { EntitySearchResult } from '../useEntitySearch';

jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

const mockFetch = jest.fn();
global.fetch = mockFetch;

Object.defineProperty(global.crypto, 'subtle', {
  configurable: true,
  value: {
    digest: jest.fn(async (_algorithm: string, data: ArrayLike<number>) => {
      const digest = createHash('sha256')
        .update(Buffer.from(Array.from(data)))
        .digest();
      const out = new Uint8Array(digest.length);
      out.set(digest);
      return out.buffer;
    }),
  },
});

const MATTER: EntitySearchResult = {
  id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Acme v. Beta',
};

function outlookContext(overrides: Partial<SaveFlowContext> = {}): SaveFlowContext {
  return {
    hostType: 'outlook',
    itemId: 'AAMkAGI2TG93AAA=',
    itemName: 'Re: Filing',
    attachments: [],
    senderEmail: 'counsel@contoso.com',
    ...overrides,
  };
}

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) {
      const body = {
        jobId: 'job-1',
        statusUrl: '/api/office/jobs/job-1',
        streamUrl: '/api/office/jobs/job-1/stream',
        status: 'Queued',
        duplicate: false,
        correlationId: 'corr-1',
      };
      return { ok: true, status: 202, text: async () => JSON.stringify(body), json: async () => body };
    }
    // Job polling after an accepted save: keep it "running" so nothing completes mid-test.
    const status = { jobId: 'job-1', status: 'Running', completedPhases: [] };
    return { ok: true, status: 200, text: async () => JSON.stringify(status), json: async () => status };
  });
});

const getAccessToken = jest.fn().mockResolvedValue('token');

// eslint-disable-next-line @typescript-eslint/no-explicit-any -- the raw JSON wire body, asserted field by field
async function sentEmail(context: SaveFlowContext): Promise<Record<string, any>> {
  const { result } = renderHook(() =>
    useSaveFlow({ getAccessToken, pollingIntervalMs: 60_000, apiBaseUrl: 'https://bff' })
  );
  act(() => result.current.setSelectedEntity(MATTER));
  await act(async () => {
    await result.current.startSave(context);
  });

  const saves = mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save'));
  expect(saves).toHaveLength(1);
  return JSON.parse(String((saves[0] as [string, RequestInit])[1].body)).email;
}

describe('useSaveFlow — the .eml name signal (task 046)', () => {
  it('an email saved under its own subject is marked system-derived', async () => {
    const email = await sentEmail(outlookContext());

    expect(email.subject).toBe('Re: Filing');
    expect(email.isNameSystemDerived).toBe(true);
    expect(email).not.toHaveProperty('documentName');
  });

  it('a Document Name the user typed travels as documentName, unchanged and marked as typed; the subject stays the email’s own (#2416)', async () => {
    const email = await sentEmail(outlookContext({ documentName: 'Smith engagement letter' }));

    expect(email.documentName).toBe('Smith engagement letter');
    expect(email.subject).toBe('Re: Filing');
    expect(email.isNameSystemDerived).toBe(false);
  });

  it('an email with no subject still sends a subject (the server requires one)', async () => {
    const email = await sentEmail(outlookContext({ itemName: '', documentName: 'Typed' }));

    expect(email.subject).toBe('Untitled Email');
    expect(email.documentName).toBe('Typed');
  });

  it('a whitespace-only Document Name is no name: nothing is sent and the name is system-derived (#2416)', async () => {
    const email = await sentEmail(outlookContext({ documentName: '   ' }));

    expect(email).not.toHaveProperty('documentName');
    expect(email.isNameSystemDerived).toBe(true);
  });

  it('a typed name is sent trimmed (#2416)', async () => {
    const email = await sentEmail(outlookContext({ documentName: '  Smith letter  ' }));

    expect(email.documentName).toBe('Smith letter');
    expect(email.isNameSystemDerived).toBe(false);
  });

  it('"Keep both" after a typed-name collision keeps the typed name and asks for the unique stored suffix (#2416)', async () => {
    const email = await sentEmail(outlookContext({ documentName: 'Smith letter', allowRename: true }));

    expect(email.documentName).toBe('Smith letter');
    expect(email.isNameSystemDerived).toBe(true);
  });
});
