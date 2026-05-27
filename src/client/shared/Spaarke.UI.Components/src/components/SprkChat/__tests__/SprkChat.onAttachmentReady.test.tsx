/**
 * SprkChat onAttachmentReady callback — unit tests (R4 task 042 / W-4)
 *
 * Verifies the new `onAttachmentReady` prop fires exactly once per file that
 * transitions to `ready` state and is NOT called for chips in `extracting` or
 * `error` state. Hosts (e.g. ConversationPane in SpaarkeAi) use this signal
 * to dispatch `widget_load` on the workspace PaneEventBus channel and mount
 * a DocumentViewerWidget as a workspace tab.
 *
 * Test isolation: we mock the attachment hook (matches the strategy from
 * `SprkChat.attachments.test.tsx`) and rerender with different chip
 * states to drive the host effect deterministically.
 */

import * as React from 'react';
import { waitFor, act } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type {
  AttachmentChip,
  ChatAttachment,
  IUseChatFileAttachmentResult,
} from '../hooks/useChatFileAttachment';

// ---------------------------------------------------------------------------
// Mock useChatFileAttachment so the test drives chip state directly.
// ---------------------------------------------------------------------------

let mockHookResult: IUseChatFileAttachmentResult;
const mockClearAll = jest.fn();
const mockAddFiles = jest.fn().mockResolvedValue(undefined);
const mockRemoveFile = jest.fn();

jest.mock('../hooks/useChatFileAttachment', () => {
  const actual = jest.requireActual('../hooks/useChatFileAttachment');
  return {
    ...actual,
    useChatFileAttachment: () => mockHookResult,
  };
});

import { SprkChat } from '../SprkChat';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const mockFetch = jest.fn();
(global as any).fetch = mockFetch;

const mockAuthenticatedFetch = (url: string, init?: RequestInit) =>
  mockFetch(url, {
    ...init,
    headers: {
      ...(init?.headers ?? {}),
      Authorization: 'Bearer test-access-token',
    },
  });
const mockGetAccessToken = () => Promise.resolve('test-access-token');

const defaultProps = {
  playbookId: 'test-playbook-id',
  apiBaseUrl: 'https://api.example.com',
  authenticatedFetch: mockAuthenticatedFetch,
  getAccessToken: mockGetAccessToken,
};

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: jest.fn().mockResolvedValue(JSON.stringify(body)),
    json: jest.fn().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

function makeChip(
  id: string,
  filename: string,
  mimeType: string,
  status: AttachmentChip['status'],
  textContent: string | undefined = undefined,
): AttachmentChip {
  return {
    id,
    filename,
    sizeBytes: textContent?.length ?? 0,
    mimeType,
    status,
    textContent,
  };
}

function setHookResult(chips: AttachmentChip[]): void {
  mockHookResult = {
    files: chips,
    attachments: chips
      .filter((c) => c.status === 'ready' && typeof c.textContent === 'string')
      .map(
        (c): ChatAttachment => ({
          filename: c.filename,
          contentType: c.mimeType,
          textContent: c.textContent ?? '',
        }),
      ),
    errors: [],
    addFiles: mockAddFiles,
    removeFile: mockRemoveFile,
    clearAll: mockClearAll,
  };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('SprkChat — onAttachmentReady (R4 task 042 / W-4)', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setHookResult([]);
    mockFetch.mockImplementation(() => Promise.resolve(jsonResponse({})));
  });

  it('does NOT fire onAttachmentReady when no chips are present', async () => {
    const onAttachmentReady = jest.fn();

    await act(async () => {
      renderWithProviders(
        <SprkChat {...defaultProps} onAttachmentReady={onAttachmentReady} />,
      );
    });

    // Give effects a tick to run.
    await waitFor(() => {
      expect(onAttachmentReady).not.toHaveBeenCalled();
    });
  });

  it('does NOT fire onAttachmentReady for chips still extracting', async () => {
    const onAttachmentReady = jest.fn();
    setHookResult([makeChip('1', 'pending.pdf', 'application/pdf', 'extracting')]);

    await act(async () => {
      renderWithProviders(
        <SprkChat {...defaultProps} onAttachmentReady={onAttachmentReady} />,
      );
    });

    await waitFor(() => {
      expect(onAttachmentReady).not.toHaveBeenCalled();
    });
  });

  it('does NOT fire onAttachmentReady for chips in error state', async () => {
    const onAttachmentReady = jest.fn();
    setHookResult([
      {
        ...makeChip('1', 'broken.pdf', 'application/pdf', 'error'),
        error: 'extraction-failed',
      },
    ]);

    await act(async () => {
      renderWithProviders(
        <SprkChat {...defaultProps} onAttachmentReady={onAttachmentReady} />,
      );
    });

    await waitFor(() => {
      expect(onAttachmentReady).not.toHaveBeenCalled();
    });
  });

  it('fires onAttachmentReady once per ready chip with the typed payload', async () => {
    const onAttachmentReady = jest.fn();

    setHookResult([
      makeChip('chip-1', 'Contract.pdf', 'application/pdf', 'ready', 'PDF text content'),
      makeChip('chip-2', 'memo.txt', 'text/plain', 'ready', 'Plain text'),
    ]);

    await act(async () => {
      renderWithProviders(
        <SprkChat {...defaultProps} onAttachmentReady={onAttachmentReady} />,
      );
    });

    await waitFor(() => {
      expect(onAttachmentReady).toHaveBeenCalledTimes(2);
    });

    expect(onAttachmentReady).toHaveBeenCalledWith({
      filename: 'Contract.pdf',
      contentType: 'application/pdf',
      textContent: 'PDF text content',
    });
    expect(onAttachmentReady).toHaveBeenCalledWith({
      filename: 'memo.txt',
      contentType: 'text/plain',
      textContent: 'Plain text',
    });
  });

  it('does NOT re-fire onAttachmentReady on re-render when chip set is unchanged', async () => {
    const onAttachmentReady = jest.fn();
    setHookResult([
      makeChip('chip-1', 'Contract.pdf', 'application/pdf', 'ready', 'text-content'),
    ]);

    const { rerender } = renderWithProviders(
      <SprkChat {...defaultProps} onAttachmentReady={onAttachmentReady} />,
    );

    await waitFor(() => {
      expect(onAttachmentReady).toHaveBeenCalledTimes(1);
    });

    // Re-render with no chip change — the host callback must NOT fire again.
    await act(async () => {
      rerender(
        <SprkChat {...defaultProps} onAttachmentReady={onAttachmentReady} />,
      );
    });

    // Allow effects to flush; assert the counter has NOT advanced.
    await new Promise((r) => setTimeout(r, 0));
    expect(onAttachmentReady).toHaveBeenCalledTimes(1);
  });

  it('does not throw when the host callback itself throws', async () => {
    const onAttachmentReady = jest.fn(() => {
      throw new Error('host-callback-broken');
    });
    setHookResult([
      makeChip('chip-1', 'a.pdf', 'application/pdf', 'ready', 'content'),
    ]);

    // The test passes if rendering completes without an unhandled rejection.
    await act(async () => {
      renderWithProviders(
        <SprkChat {...defaultProps} onAttachmentReady={onAttachmentReady} />,
      );
    });

    await waitFor(() => {
      expect(onAttachmentReady).toHaveBeenCalledTimes(1);
    });
  });
});
