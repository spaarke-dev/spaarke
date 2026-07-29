/**
 * useComposeToolbarActivation.test.tsx — FORCING TEST for AI-toolbar activation
 * (task 048, closes e2e-gap 2.2; project E2E DoD row 3: a receiver/trigger needs
 * proof it is MOUNTED + REACHABLE).
 *
 * This is a REAL-RENDER test through the REAL registration seam — it does NOT
 * mock the toolbar or pass an `actions` override. It mounts the activation hook
 * (with a stubbed capability fetch) ALONGSIDE a real `<ComposeAiToolbar>` reading
 * the live module registry, and proves:
 *   1. matching compose capabilities register their real bindingId → the matching
 *      buttons become ENABLED and dispatch with that exact bindingId;
 *   2. a capability with no matching DEFAULT action (the whole-document
 *      `compose-summarize` binding) is IGNORED — never appended, never enables a
 *      button that wasn't in the response;
 *   3. the 0-capabilities case (pre-047 deploy) and a fetch failure both leave the
 *      buttons DISABLED with no error UI and no throw.
 *
 * `@spaarke/auth`'s `useAuth()` is mocked (it throws outside a real MSAL
 * bootstrap); the capability fetch itself is driven by the hook's `fetchOverride`.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { Editor } from '@tiptap/react';
import { ComposeAiToolbar, __resetComposeAiToolbarActionsForTests } from './ComposeAiToolbar';
import { useComposeToolbarActivation } from './useComposeToolbarActivation';
import type { DispatchPaneEvent } from '@spaarke/ai-widgets/events';
import type { DispatchConsumer } from '@spaarke/ui-components';

jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    // Never actually used by these tests — the hook is driven via `fetchOverride`.
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// ---------------------------------------------------------------------------
// Helpers (mirror ComposeAiToolbar.test.tsx)
// ---------------------------------------------------------------------------

/** Minimal TipTap-`Editor`-shaped mock with a non-collapsed selection. */
function createMockEditor(params: { from: number; to: number; text: string }): Editor {
  const listeners = new Map<string, Set<() => void>>();
  const mock = {
    state: {
      selection: { from: params.from, to: params.to },
      doc: { textBetween: () => params.text },
    },
    on: (event: string, handler: () => void) => {
      const set = listeners.get(event) ?? new Set<() => void>();
      set.add(handler);
      listeners.set(event, set);
      return mock;
    },
    off: (event: string, handler: () => void) => {
      listeners.get(event)?.delete(handler);
      return mock;
    },
  };
  return mock as unknown as Editor;
}

function noopDispatch(): DispatchPaneEvent {
  return jest.fn() as unknown as DispatchPaneEvent;
}

/** Builds a fake `fetch` returning the given capability list (ok:200). */
function stubCapabilitiesFetch(capabilities: Array<{ bindingId: string; consumerType: string }>): typeof fetch {
  const response = {
    ok: true,
    status: 200,
    json: async () => ({ capabilities }),
  } as unknown as Response;
  return jest.fn().mockResolvedValue(response) as unknown as typeof fetch;
}

const BFF = 'https://bff.example.test';

/**
 * The real activation wiring under test: the hook (which registers into the live
 * module registry) mounted ALONGSIDE the real toolbar (which reads that registry
 * and re-renders via `subscribeComposeAiToolbarActions`). This is exactly the
 * host shape used in `composeEditor.registration.ts`.
 */
function ActivationHost(props: {
  editor: Editor;
  fetchOverride: typeof fetch;
  dispatchConsumerOverride?: DispatchConsumer;
}): React.JSX.Element {
  useComposeToolbarActivation({ bffBaseUrl: BFF, surface: 'compose', fetchOverride: props.fetchOverride });
  return (
    <FluentProvider theme={webLightTheme}>
      <ComposeAiToolbar
        editor={props.editor}
        documentRef={{ speDriveItemId: 'spe-123', sprkDocumentId: 'doc-456' }}
        sessionId="session-789"
        bffBaseUrl={BFF}
        dispatch={noopDispatch()}
        dispatchConsumerOverride={props.dispatchConsumerOverride}
      />
    </FluentProvider>
  );
}

afterEach(() => {
  __resetComposeAiToolbarActionsForTests();
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// 1. Matching capabilities activate buttons with the deployed bindingId;
//    non-matching capability is ignored.
// ---------------------------------------------------------------------------

describe('useComposeToolbarActivation — activation (E2E DoD row 3)', () => {
  it('registers matching compose capabilities → the SURFACED Draft-alternative button ENABLES with the deployed bindingId; retired + non-matching capabilities do not surface', async () => {
    const user = userEvent.setup();
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const dispatchConsumerOverride = jest.fn().mockResolvedValue({ streamId: 's1', status: 'complete' });

    const fetchOverride = stubCapabilitiesFetch([
      { bindingId: 'guid-draft', consumerType: 'compose-draft-alternative' },
      // RETIRED from the selection surface (round-8 #6, `surfaces: []`): the activation
      // hook still REGISTERS these bindingIds onto the registry, but they no longer
      // render on the BubbleMenu — proof that surface retirement survives live registration.
      { bindingId: 'guid-explain', consumerType: 'compose-explain-clause' },
      { bindingId: 'guid-defined-terms', consumerType: 'compose-defined-terms' },
      // NON-matching: the whole-document summarize binding is NOT a toolbar action.
      { bindingId: 'guid-wholedoc-summarize', consumerType: 'compose-summarize' },
    ]);

    render(
      <ActivationHost
        editor={editor}
        fetchOverride={fetchOverride}
        dispatchConsumerOverride={dispatchConsumerOverride}
      />
    );

    // The async fetch + registration lands AFTER first paint — the toolbar
    // re-renders via the module subscription and flips the SURFACED button to enabled.
    const draft = await screen.findByTestId('compose-ai-toolbar-compose-draft-alternative');
    await waitFor(() => expect(draft).toBeEnabled());

    // Retired tools do NOT render on the selection surface even though their capability
    // was returned + registered (Contextual AI Tool Library: `surfaces: []`).
    expect(screen.queryByTestId('compose-ai-toolbar-compose-explain-clause')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-ai-toolbar-compose-compare-to-playbook')).not.toBeInTheDocument();

    // The registered bindingId flows through verbatim on click (exact-value proof).
    await user.click(draft);
    expect(dispatchConsumerOverride).toHaveBeenCalledWith(
      'guid-draft',
      expect.objectContaining({ slots: expect.objectContaining({ selectionText: 'Hello world' }) })
    );

    // Overflow: with Explain/Compare/Defined-terms all retired from the selection
    // surface, the default overflow is empty — the registered defined-terms and the
    // non-matching whole-doc summarize both surface nothing here.
    await user.click(screen.getByTestId('compose-ai-toolbar-more'));
    expect(await screen.findByTestId('compose-ai-toolbar-more-empty')).toBeInTheDocument();
    expect(screen.queryByTestId('compose-ai-toolbar-overflow-compose-defined-terms')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-ai-toolbar-overflow-compose-summarize')).not.toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // 2. 0 capabilities (pre-047 deploy) → buttons stay disabled, no error.
  // -------------------------------------------------------------------------

  it('0 capabilities (pre-047) leaves all buttons DISABLED with no error UI', async () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const fetchOverride = stubCapabilitiesFetch([]);

    render(<ActivationHost editor={editor} fetchOverride={fetchOverride} />);

    const draft = await screen.findByTestId('compose-ai-toolbar-compose-draft-alternative');
    // Give the effect a chance to run and (not) register anything.
    await waitFor(() => expect(fetchOverride).toHaveBeenCalledTimes(1));
    expect(draft).toBeDisabled();
    // No error banner/alert rendered anywhere.
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // 3. Fetch failure → buttons stay disabled, no throw, no error UI.
  // -------------------------------------------------------------------------

  it('a fetch failure leaves buttons DISABLED — no throw, no error UI', async () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const failing = jest.fn().mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => ({}),
    } as unknown as Response) as unknown as typeof fetch;

    render(<ActivationHost editor={editor} fetchOverride={failing} />);

    const draft = await screen.findByTestId('compose-ai-toolbar-compose-draft-alternative');
    await waitFor(() => expect(failing).toHaveBeenCalledTimes(1));
    expect(draft).toBeDisabled();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
