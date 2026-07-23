/**
 * ConversationPane — Rules-of-Hooks regression (React #300 hooks-count mismatch).
 *
 * Repro for the cold `composeMode=editor` first-load crash ("SpaarkeAi encountered
 * an error", clears on refresh): the Assistant pane's `if (!isAuthenticated) return
 * <AuthLoadingState/>` guard is a CONDITIONAL EARLY RETURN whose condition is
 * timing-dependent — `useAiSession().isAuthenticated` is false on the first render
 * (the async auth probe has not resolved) and true on the next. The bridge commit
 * (00f23dc37) added a `React.useMemo` (`transcriptFooter`) BELOW that guard, so the
 * second (authenticated) render executed one more hook than the first — React throws
 * minified error #300 "Rendered more hooks than during the previous render".
 *
 * This test renders the pane while unauthenticated (hits the early return), then
 * re-renders it authenticated (full tree), asserting the hook count is stable across
 * the two renders (no throw, no React hook-order console error). Guards against any
 * future hook being reintroduced below the auth guard.
 *
 * Mock pattern mirrors ConversationPane.new-session.test.tsx (SprkChat prop-capture
 * stub + useAiSession module mock + ThreePaneShell context mock), with the single
 * change that `isAuthenticated` is read from a mutable holder the test flips.
 */

import * as React from "react";
import { render } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { PaneEventBus, PaneEventBusProvider } from "@spaarke/ai-widgets";
import type { ISprkChatProps } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Mutable auth holder — flipped by the test between renders.
// ---------------------------------------------------------------------------

const authState = { isAuthenticated: false };

// ---------------------------------------------------------------------------
// Mock SprkChat — inert stub (only renders once auth resolves).
// ---------------------------------------------------------------------------

jest.mock("@spaarke/ui-components", () => {
  const actual = jest.requireActual("@spaarke/ui-components");
  return {
    ...actual,
    SprkChat: (_props: ISprkChatProps) => <div data-testid="sprkchat-stub" />,
  };
});

// ---------------------------------------------------------------------------
// Mock @spaarke/ai-widgets useAiSession — isAuthenticated read from authState.
// ---------------------------------------------------------------------------

const TEST_SESSION_ID = "00000000-0000-0000-0000-000000000001";

jest.mock("@spaarke/ai-widgets", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual("@spaarke/ai-widgets") as any;
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: authState.isAuthenticated,
      authenticatedFetch: jest.fn(async () => ({ ok: false, status: 404, json: async () => ({}) })),
      getAccessToken: jest.fn(async () => "token"),
      bffBaseUrl: "https://test-bff.example.com",
      tenantId: "test-tenant",
      chatSessionId: TEST_SESSION_ID,
      setChatSessionId: jest.fn(),
      clearChatSession: jest.fn(),
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

jest.mock("../../shell/ThreePaneShell", () => ({
  useShellStage: () => ({
    stage: "active-chat" as const,
    toLoading: jest.fn(),
    toActiveChat: jest.fn(),
    toReview: jest.fn(),
    reset: jest.fn(),
  }),
  useRestoreContext: () => null,
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
}));

// Import AFTER the mocks.
import { ConversationPane } from "../ConversationPane";

function renderPane() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={new PaneEventBus()}>
        <ConversationPane />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

describe("ConversationPane — Rules of Hooks across the auth guard (React #300 regression)", () => {
  beforeEach(() => {
    authState.isAuthenticated = false;
    jest.clearAllMocks();
  });

  it("keeps a stable hook count when isAuthenticated flips false → true", () => {
    // React logs a hook-order error to console.error before throwing #300; capture it
    // so a regression fails loudly on the message even if a future change swallows the throw.
    const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);

    // First render: unauthenticated → the pane hits the `if (!isAuthenticated) return` guard,
    // so only the hooks ABOVE the guard run (the smaller hook count).
    const { rerender } = renderPane();

    // Auth probe resolves → re-render authenticated → the full tree renders (every hook runs).
    // With a hook below the guard this second render trips React #300 during commit.
    authState.isAuthenticated = true;

    expect(() =>
      rerender(
        <FluentProvider theme={webLightTheme}>
          <PaneEventBusProvider bus={new PaneEventBus()}>
            <ConversationPane />
          </PaneEventBusProvider>
        </FluentProvider>
      )
    ).not.toThrow();

    const hookOrderError = consoleError.mock.calls.some((args) =>
      args.some(
        (a) =>
          typeof a === "string" &&
          (a.includes("Rendered more hooks") ||
            a.includes("Rendered fewer hooks") ||
            a.includes("change in the order of Hooks") ||
            a.includes("Minified React error #300"))
      )
    );
    expect(hookOrderError).toBe(false);

    consoleError.mockRestore();
  });
});
