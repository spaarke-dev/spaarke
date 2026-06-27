/**
 * ConversationPane — chat-routing-redesign-r1 task 117b
 * Playbook-options SSE event consumption + inline-link-button UX.
 *
 * Covers the acceptance criteria from
 * `tasks/117b-fe-link-buttons-and-library-link.poml`:
 *
 *   (1) Host wires `onPlaybookOptions` on SprkChat (callback prop is plumbed).
 *   (2) When SprkChat fires `onPlaybookOptions`, the host emits an
 *       Assistant chat message via `injectLocalMessage` with
 *       `metadata.responseType === 'playbook_options'` and the canonical
 *       payload shape.
 *   (3) `onSelectPlaybook` posts to `/api/ai/playbook-dispatch/execute` with
 *       `{ playbookId, sessionAttachmentIds, originalMessage, sessionId }`
 *       — `originalMessage` echoes the last text passed to
 *       `onBeforeSendMessage`.
 *   (4) `onOpenLibraryModal` calls Xrm.Navigation.navigateTo with the
 *       `sprk_playbooklibrary` web resource and an optional
 *       `sessionAttachmentIds=` filter.
 *   (5) ADR-015 logging discipline: payload contents are NOT in the
 *       console.log argument list (only counts + booleans).
 *
 * Test strategy mirrors `ConversationPane.r5.test.tsx`:
 *   - SprkChat is replaced with a prop-capturing stub.
 *   - `useAiSession` is mocked to inject `authenticatedFetch` + the
 *     `bffBaseUrl` the dispatcher URL is built against.
 *   - We drive `onPlaybookOptions` / `onBeforeSendMessage` / `onSelectPlaybook` /
 *     `onOpenLibraryModal` from outside, then assert on the host's outputs:
 *     `injectLocalMessage`, fetch calls, Xrm.Navigation.navigateTo calls.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { ISprkChatProps } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Mock @spaarke/ui-components — SprkChat is the heavy child.
// ---------------------------------------------------------------------------

const sprkChatPropsRef: { current: ISprkChatProps | null } = { current: null };

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      sprkChatPropsRef.current = props;
      return <div data-testid="sprkchat-stub" />;
    },
  };
});

// ---------------------------------------------------------------------------
// Mock @spaarke/ai-widgets — supply minimal session stub.
// authenticatedFetch is a jest.fn so we can assert on dispatcher calls.
// ---------------------------------------------------------------------------

const authenticatedFetchMock = jest.fn<Promise<Response>, [string, RequestInit?]>();

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn(),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-abc-123',
      setChatSessionId: jest.fn(),
      playbookId: undefined,
      setPlaybookId: jest.fn(),
      entityContext: null,
      contextMapping: null,
      isLoadingContextMapping: false,
      streaming: { onPaneEvent: null },
      streamingState: { isStreaming: false, tokenCount: 0 },
      turnCount: 0,
      isLoading: false,
    }),
  };
});

// Import AFTER the mocks.
import { ConversationPane } from '../ConversationPane';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderPane(): void {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ConversationPane />
      </PaneEventBusProvider>
    </FluentProvider>,
  );
}

interface PlaybookOptionsPayload {
  candidates: Array<{
    playbookId: string;
    playbookCode: string;
    displayName: string;
    confidence: number;
    reason: string;
  }>;
  libraryModalCta: boolean;
  sessionAttachmentIds: string[];
  rerankInvoked: boolean;
  rerankReason?: string | null;
}

function samplePayload(): PlaybookOptionsPayload {
  return {
    candidates: [
      {
        playbookId: '00000000-0000-0000-0000-000000000001',
        playbookCode: 'PB-001',
        displayName: 'Summarize Contract',
        confidence: 0.92,
        reason: 'top-confidence',
      },
      {
        playbookId: '00000000-0000-0000-0000-000000000002',
        playbookCode: 'PB-002',
        displayName: 'Risk Review',
        confidence: 0.86,
        reason: 'top-confidence',
      },
      {
        playbookId: '00000000-0000-0000-0000-000000000003',
        playbookCode: 'PB-003',
        displayName: 'Extract Obligations',
        confidence: 0.81,
        reason: 'top-confidence',
      },
    ],
    libraryModalCta: true,
    sessionAttachmentIds: ['file-id-1', 'file-id-2'],
    rerankInvoked: false,
    rerankReason: null,
  };
}

beforeEach(() => {
  sprkChatPropsRef.current = null;
  authenticatedFetchMock.mockReset();
  authenticatedFetchMock.mockResolvedValue(
    new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } }),
  );
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('ConversationPane → SprkChat playbook-options wiring (FR-49 / 50 / 51)', () => {
  it('wires onPlaybookOptions, onSelectPlaybook, and onOpenLibraryModal callbacks on SprkChat', () => {
    renderPane();
    expect(sprkChatPropsRef.current).not.toBeNull();
    // ISprkChatProps now exposes these three optional props — the host MUST set all three.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const props = sprkChatPropsRef.current as any;
    expect(typeof props.onPlaybookOptions).toBe('function');
    expect(typeof props.onSelectPlaybook).toBe('function');
    expect(typeof props.onOpenLibraryModal).toBe('function');
  });

  it('on onPlaybookOptions, sets injectLocalMessage to a playbook_options structured message', async () => {
    renderPane();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const props = sprkChatPropsRef.current as any;

    // Fire the SSE-payload callback.
    React.act(() => {
      props.onPlaybookOptions(samplePayload());
    });

    // The host should now be re-rendering with a non-null injectLocalMessage.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const refreshed = sprkChatPropsRef.current as any;
    const msg = refreshed.injectLocalMessage;
    expect(msg).not.toBeNull();
    expect(msg.role).toBe('Assistant');
    expect(msg.metadata.responseType).toBe('playbook_options');
    expect(msg.metadata.data.candidates).toHaveLength(3);
    expect(msg.metadata.data.libraryModalCta).toBe(true);
    expect(msg.metadata.data.sessionAttachmentIds).toEqual(['file-id-1', 'file-id-2']);
    // The fallback `content` is the prompt question (FR-49 UX intent).
    expect(msg.content).toContain('Which playbook');
  });

  it('on onPlaybookOptions with 0 candidates, surfaces a no-match fallback content but still keeps libraryModalCta true', () => {
    renderPane();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const props = sprkChatPropsRef.current as any;
    React.act(() => {
      props.onPlaybookOptions({
        candidates: [],
        libraryModalCta: true,
        sessionAttachmentIds: [],
        rerankInvoked: false,
        rerankReason: null,
      });
    });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const refreshed = sprkChatPropsRef.current as any;
    const msg = refreshed.injectLocalMessage;
    expect(msg.content).toContain("couldn't find a confident match");
    expect(msg.metadata.data.candidates).toHaveLength(0);
    expect(msg.metadata.data.libraryModalCta).toBe(true);
  });

  it('onSelectPlaybook POSTs to /api/ai/playbook-dispatch/execute with the locked body shape', async () => {
    renderPane();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const props = sprkChatPropsRef.current as any;

    // Capture the user's most-recent message text via onBeforeSendMessage.
    React.act(() => {
      props.onBeforeSendMessage('summarize this contract');
    });

    React.act(() => {
      props.onSelectPlaybook(
        '00000000-0000-0000-0000-000000000001',
        ['file-id-1', 'file-id-2'],
      );
    });

    // The dispatcher call is fired in a void async — wait a microtask.
    await Promise.resolve();
    await Promise.resolve();

    expect(authenticatedFetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = authenticatedFetchMock.mock.calls[0];
    expect(url).toBe('https://test-bff.example.com/api/ai/playbook-dispatch/execute');
    expect(init?.method).toBe('POST');
    expect(init?.headers).toEqual({ 'Content-Type': 'application/json' });
    const body = JSON.parse(init?.body as string);
    expect(body).toEqual({
      playbookId: '00000000-0000-0000-0000-000000000001',
      sessionAttachmentIds: ['file-id-1', 'file-id-2'],
      originalMessage: 'summarize this contract',
      sessionId: 'session-abc-123',
    });
  });

  it('onSelectPlaybook surfaces a friendly fallback message when the dispatcher returns 404 (orchestrator not wired yet)', async () => {
    authenticatedFetchMock.mockResolvedValueOnce(
      new Response('not found', { status: 404 }),
    );
    renderPane();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const props = sprkChatPropsRef.current as any;

    React.act(() => {
      props.onSelectPlaybook(
        '00000000-0000-0000-0000-000000000001',
        ['file-id-1'],
      );
    });

    // Wait for the promise + setPendingInjection re-render.
    await Promise.resolve();
    await Promise.resolve();
    await new Promise(resolve => setTimeout(resolve, 0));

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const refreshed = sprkChatPropsRef.current as any;
    expect(refreshed.injectLocalMessage).not.toBeNull();
    expect(refreshed.injectLocalMessage.content).toContain('dispatcher endpoint');
  });
});

describe('ConversationPane → onOpenLibraryModal (FR-51)', () => {
  // Save + restore Xrm so tests don't leak across the file.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let originalXrm: any;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const navigateToSpy = jest.fn<Promise<void>, [any, any?]>(() => Promise.resolve());

  beforeEach(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    originalXrm = (window as any).Xrm;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).Xrm = {
      Navigation: { navigateTo: navigateToSpy },
    };
    navigateToSpy.mockClear();
  });

  afterEach(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).Xrm = originalXrm;
  });

  it('opens sprk_playbooklibrary via Xrm.Navigation.navigateTo with sessionAttachmentIds filter', () => {
    renderPane();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const props = sprkChatPropsRef.current as any;

    React.act(() => {
      props.onOpenLibraryModal(['file-id-1', 'file-id-2']);
    });

    expect(navigateToSpy).toHaveBeenCalledTimes(1);
    const [navTarget, navOptions] = navigateToSpy.mock.calls[0];
    expect(navTarget).toEqual({
      pageType: 'webresource',
      webresourceName: 'sprk_playbooklibrary',
      data: 'sessionAttachmentIds=file-id-1%2Cfile-id-2',
    });
    expect(navOptions).toEqual({
      target: 2,
      width: { value: 85, unit: '%' },
      height: { value: 85, unit: '%' },
      title: 'Playbook Library',
    });
  });

  it('opens unfiltered when sessionAttachmentIds is empty', () => {
    renderPane();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const props = sprkChatPropsRef.current as any;

    React.act(() => {
      props.onOpenLibraryModal([]);
    });

    expect(navigateToSpy).toHaveBeenCalledTimes(1);
    const [navTarget] = navigateToSpy.mock.calls[0];
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((navTarget as any).data).toBe('');
  });

  it('is a no-op when Xrm.Navigation is unavailable (running outside Dataverse host)', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).Xrm = undefined;
    renderPane();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const props = sprkChatPropsRef.current as any;

    expect(() =>
      React.act(() => {
        props.onOpenLibraryModal(['file-id-1']);
      }),
    ).not.toThrow();
    expect(navigateToSpy).not.toHaveBeenCalled();
  });
});

describe('ADR-015 logging discipline', () => {
  it('onPlaybookOptions logs only counts + booleans — never candidate payload contents', () => {
    const logSpy = jest.spyOn(console, 'log').mockImplementation(() => {});
    renderPane();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const props = sprkChatPropsRef.current as any;

    React.act(() => {
      props.onPlaybookOptions(samplePayload());
    });

    // Find the playbook_options log line.
    const playbookCalls = logSpy.mock.calls.filter(call =>
      typeof call[0] === 'string' && call[0].includes('playbook_options received'),
    );
    expect(playbookCalls.length).toBeGreaterThan(0);
    // Each arg in the call MUST be a primitive count / boolean — never an array,
    // object, or string that includes a displayName / playbookId.
    for (const call of playbookCalls) {
      for (const arg of call) {
        if (typeof arg === 'string') {
          expect(arg).not.toContain('Summarize Contract');
          expect(arg).not.toContain('00000000-0000-0000-0000-000000000001');
        }
        // Reject object / array args — those would leak the payload.
        expect(Array.isArray(arg)).toBe(false);
        if (typeof arg === 'object' && arg !== null) {
          fail('Logged an object — ADR-015 violation (payload leak)');
        }
      }
    }
    logSpy.mockRestore();
  });
});
