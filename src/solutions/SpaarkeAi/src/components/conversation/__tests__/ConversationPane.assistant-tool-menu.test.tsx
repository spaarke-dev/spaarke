/**
 * ConversationPane — Assistant tool drop-down mount (task 040 / FR-F1).
 *
 * Verifies the `AssistantToolMenu` mounts in the Assistant pane's PaneHeader
 * rightSlot ALONGSIDE the existing "New session" button and `HistoryMenu`
 * ("History ▾") — purely additive, per FR-F1's "does not disturb the
 * three-pane layout" acceptance criterion. The chat body (`SprkChat` stub +
 * its wrapper) still renders unaffected, proving the new trigger did not
 * displace or break the pane's existing content region.
 *
 * Mock pattern mirrors ConversationPane.new-session.test.tsx (SprkChat
 * prop-capture stub + useAiSession module mock + ThreePaneShell context mock).
 */

import "@testing-library/jest-dom";
import * as React from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { PaneEventBus, PaneEventBusProvider } from "@spaarke/ai-widgets";
import type { ISprkChatProps } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Mock SprkChat — prop capture stub (chat body presence is the layout proxy).
// ---------------------------------------------------------------------------

jest.mock("@spaarke/ui-components", () => {
  const actual = jest.requireActual("@spaarke/ui-components");
  return {
    ...actual,
    SprkChat: (_props: ISprkChatProps) => <div data-testid="sprkchat-stub" />,
  };
});

// ---------------------------------------------------------------------------
// Mock @spaarke/ai-widgets useAiSession — minimal authenticated session.
// ---------------------------------------------------------------------------

const TEST_SESSION_ID = "00000000-0000-0000-0000-000000000002";

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

function renderPane(): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={new PaneEventBus()}>
        <ConversationPane />
      </PaneEventBusProvider>
    </FluentProvider>,
  );
}

describe("ConversationPane — Assistant tool drop-down mount (task 040)", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("renders the Assistant tool drop-down trigger in the header beside New session + History", () => {
    renderPane();

    expect(screen.getByTestId("assistant-tool-menu-trigger")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "New session" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /history/i })).toBeInTheDocument();
  });

  it("does not disturb the chat content region — SprkChat still mounts", () => {
    renderPane();

    // Proxy for "three-pane layout unaffected": the pane's chat body renders
    // exactly as before the new header trigger was added.
    expect(screen.getByTestId("sprkchat-stub")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "AI Chat" })).toBeInTheDocument();
  });

  it("opens the Assistant tool drop-down and shows both entries without affecting the chat region", async () => {
    renderPane();
    const user = userEvent.setup();

    await user.click(screen.getByTestId("assistant-tool-menu-trigger"));

    expect(await screen.findByTestId("assistant-tool-quick-start")).toBeInTheDocument();
    expect(screen.getByTestId("assistant-tool-my-assistant")).toBeInTheDocument();
    // Chat body is untouched while the menu is open.
    expect(screen.getByTestId("sprkchat-stub")).toBeInTheDocument();
  });
});
