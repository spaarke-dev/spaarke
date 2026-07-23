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
import { render, screen, waitFor, within } from '@testing-library/react';
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
  it('registers matching compose capabilities → matching buttons ENABLE with the deployed bindingId; non-matching capability ignored', async () => {
    const user = userEvent.setup();
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const dispatchConsumerOverride = jest.fn().mockResolvedValue({ streamId: 's1', status: 'complete' });

    const fetchOverride = stubCapabilitiesFetch([
      { bindingId: 'guid-explain', consumerType: 'compose-explain-clause' },
      { bindingId: 'guid-compare', consumerType: 'compose-compare-to-playbook' },
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
    // re-renders via the module subscription and flips the button to enabled.
    const explain = await screen.findByTestId('compose-ai-toolbar-compose-explain-clause');
    await waitFor(() => expect(explain).toBeEnabled());
    expect(screen.getByTestId('compose-ai-toolbar-compose-compare-to-playbook')).toBeEnabled();

    // Draft was NOT in the response → its stub stays disabled.
    expect(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative')).toBeDisabled();

    // The registered bindingId flows through verbatim on click (exact-value proof).
    await user.click(explain);
    expect(dispatchConsumerOverride).toHaveBeenCalledWith(
      'guid-explain',
      expect.objectContaining({ slots: expect.objectContaining({ selectionText: 'Hello world' }) })
    );

    // Overflow: the matching whole-doc summarize was ignored (no such button was
    // ever appended); the matching `compose-defined-terms` DEFAULT overflow action
    // is now enabled. (FIX #5 removed `compose-summarize-word-changes` from the
    // selection toolbar registry, so it is no longer an overflow action here.)
    await user.click(screen.getByTestId('compose-ai-toolbar-more'));
    const definedTerms = await screen.findByTestId('compose-ai-toolbar-overflow-compose-defined-terms');
    expect(definedTerms).not.toHaveAttribute('aria-disabled', 'true');
    expect(screen.queryByTestId('compose-ai-toolbar-overflow-compose-summarize-word-changes')).not.toBeInTheDocument();
    // The non-matching capability was NOT turned into an action.
    expect(screen.queryByTestId('compose-ai-toolbar-overflow-compose-summarize')).not.toBeInTheDocument();
    expect(within(screen.getByRole('menu')).queryByText('compose-summarize')).not.toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // 2. 0 capabilities (pre-047 deploy) → buttons stay disabled, no error.
  // -------------------------------------------------------------------------

  it('0 capabilities (pre-047) leaves all buttons DISABLED with no error UI', async () => {
    const editor = createMockEditor({ from: 0, to: 11, text: 'Hello world' });
    const fetchOverride = stubCapabilitiesFetch([]);

    render(<ActivationHost editor={editor} fetchOverride={fetchOverride} />);

    const explain = await screen.findByTestId('compose-ai-toolbar-compose-explain-clause');
    // Give the effect a chance to run and (not) register anything.
    await waitFor(() => expect(fetchOverride).toHaveBeenCalledTimes(1));
    expect(explain).toBeDisabled();
    expect(screen.getByTestId('compose-ai-toolbar-compose-compare-to-playbook')).toBeDisabled();
    expect(screen.getByTestId('compose-ai-toolbar-compose-draft-alternative')).toBeDisabled();
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

    const explain = await screen.findByTestId('compose-ai-toolbar-compose-explain-clause');
    await waitFor(() => expect(failing).toHaveBeenCalledTimes(1));
    expect(explain).toBeDisabled();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
