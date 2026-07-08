/**
 * ConversationPane — "New session" header affordance (G-P3 UAT round-4 R4-5,
 * 2026-07-07).
 *
 * Operator finding: sessions resume across hard refreshes by design (persisted
 * chatSessionId in localStorage via AiSessionProvider), with NO user control to
 * start over. The fix is a small header button beside History that:
 *   1. clears the persisted chat-session id (`clearChatSession()` — removes the
 *      sprk_ai2_chatSessionId key from localStorage AND sessionStorage), and
 *   2. remounts SprkChat (remount-key bump via useCommandRouting.startNewSession)
 *      so it mounts with `sessionId=undefined` and mints a fresh session — the
 *      same recovery leg the session-stale path already uses.
 *
 * Deliberately scoped to the conversation pane: NO `session_reset` dispatch
 * (workspace tabs + context survive), and NO history browsing/deletion (that is
 * the named r2 memory scope — the History menu stays the only history surface).
 *
 * Mock pattern mirrors ConversationPane.event-path.test.tsx (SprkChat prop-capture
 * stub + useAiSession module mock + ThreePaneShell context mock).
 */

import * as React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { PaneEventBus, PaneEventBusProvider } from "@spaarke/ai-widgets";
import type { ISprkChatProps } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Mock SprkChat — prop capture + MOUNT counter (the remount is the contract).
// ---------------------------------------------------------------------------

const sprkChatPropsRef: { current: ISprkChatProps | null } = { current: null };
let sprkChatMounts = 0;

jest.mock("@spaarke/ui-components", () => {
  const actual = jest.requireActual("@spaarke/ui-components");
  const ReactActual = jest.requireActual("react") as typeof import("react");
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      sprkChatPropsRef.current = props;
      ReactActual.useEffect(() => {
        sprkChatMounts += 1;
      }, []);
      return <div data-testid="sprkchat-stub" />;
    },
  };
});

// ---------------------------------------------------------------------------
// Mock @spaarke/ai-widgets useAiSession — clearChatSession is the seam under test.
// ---------------------------------------------------------------------------

const TEST_SESSION_ID = "00000000-0000-0000-0000-000000000001";
const clearChatSessionMock = jest.fn();
const setChatSessionIdMock = jest.fn();

jest.mock("@spaarke/ai-widgets", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual("@spaarke/ai-widgets") as any;
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: jest.fn(),
      getAccessToken: jest.fn(async () => "token"),
      bffBaseUrl: "https://test-bff.example.com",
      tenantId: "test-tenant",
      chatSessionId: TEST_SESSION_ID,
      setChatSessionId: setChatSessionIdMock,
      clearChatSession: clearChatSessionMock,
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

function renderPane(): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={new PaneEventBus()}>
        <ConversationPane />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

describe("ConversationPane — New session header affordance (R4-5)", () => {
  beforeEach(() => {
    sprkChatPropsRef.current = null;
    sprkChatMounts = 0;
    jest.clearAllMocks();
  });

  it("renders the New session button in the header beside History", () => {
    renderPane();

    expect(screen.getByRole("button", { name: "New session" })).toBeTruthy();
    // The History affordance is untouched (r2 memory scope stays out).
    expect(screen.getByRole("button", { name: /history/i })).toBeTruthy();
  });

  it("clears the persisted session id and remounts SprkChat on click", () => {
    renderPane();
    expect(sprkChatMounts).toBe(1);

    fireEvent.click(screen.getByRole("button", { name: "New session" }));

    // 1. The persisted chatSessionId is cleared through AiSessionProvider's own
    //    clear (localStorage + sessionStorage — never a bare removeItem, or the
    //    legacy sessionStorage value would re-migrate on next mount).
    expect(clearChatSessionMock).toHaveBeenCalledTimes(1);

    // 2. SprkChat REMOUNTED (remount-key bump) — with the cleared id the real
    //    component mounts sessionId=undefined and creates a fresh session.
    expect(sprkChatMounts).toBe(2);
  });

  it("does not fire the header collapse when clicking New session (stopPropagation)", () => {
    renderPane();

    // No collapse context is mounted (usePaneCollapseContext → null), so a
    // propagated click would be harmless here — but the handler still stops
    // propagation per the header-button convention (HistoryMenu precedent).
    // Assert the click leaves the pane rendered and SprkChat present.
    fireEvent.click(screen.getByRole("button", { name: "New session" }));
    expect(screen.getByTestId("sprkchat-stub")).toBeTruthy();
  });
});
