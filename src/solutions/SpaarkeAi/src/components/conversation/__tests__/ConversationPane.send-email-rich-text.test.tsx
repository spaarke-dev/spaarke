/**
 * ConversationPane - the Send Email dialog opens in rich text (task 197, owner dev recheck 2026-11-18).
 *
 * The Quick Start "Send Email" card (blank) and the post-Draft "Send as email" chip (seeded from the drafted correspondence,
 * which is plain text) both open the shared composer. It must open in rich text (HTML): the pane passes
 * `initialBodyFormat="HTML"` and converts the plain-text draft body with the one shared `plainTextToHtml` (line breaks kept,
 * `<` and `&` escaped once). SendEmailDialog is a prop-capturing stub (the composer's own rich-text rendering of such a seed
 * is pinned in Spaarke.UI.Components `EmailComposer/__tests__/SendEmailDialog.richTextSeed.test.tsx`); `useConsumerChips` is
 * wrapped so the test can hand the pane a drafted correspondence and press the "Send as email" chip exactly as the chip
 * strip does.
 */

import * as React from "react";
import { act, render } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { PaneEventBus, PaneEventBusProvider } from "@spaarke/ai-widgets";

const sendEmailDialogCalls: Array<Record<string, unknown>> = [];
function newEmailDialogProps(): Record<string, unknown> | undefined {
  const raw = sendEmailDialogCalls.filter(p => p.defaultSendMode === undefined);
  return raw[raw.length - 1];
}

jest.mock("@spaarke/ui-components", () => {
  const actual = jest.requireActual("@spaarke/ui-components");
  return {
    ...actual,
    SprkChat: () => <div data-testid="sprkchat-stub" />,
    SendEmailDialog: (props: Record<string, unknown>) => {
      sendEmailDialogCalls.push(props);
      return <div data-testid="send-email-dialog-stub" />;
    },
    createXrmEmailComposeHandlers: () => ({}),
  };
});

// The deps the pane hands useConsumerChips: the pane's own draft and local-chip callbacks.
let chipDeps: Record<string, unknown> | null = null;
jest.mock("../useConsumerChips", () => {
  const actual = jest.requireActual("../useConsumerChips");
  return {
    ...actual,
    useConsumerChips: (deps: Record<string, unknown>) => {
      chipDeps = deps;
      return actual.useConsumerChips(deps);
    },
  };
});

const authenticatedFetchMock = jest.fn();

jest.mock("@spaarke/ai-widgets", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual("@spaarke/ai-widgets") as any;
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn(async () => "token"),
      bffBaseUrl: "https://test-bff.example.com",
      tenantId: "test-tenant",
      chatSessionId: "00000000-0000-0000-0000-000000000001",
      setChatSessionId: jest.fn(),
      clearChatSession: jest.fn(),
      playbookId: undefined,
      setPlaybookId: jest.fn(),
      clearPlaybookId: jest.fn(),
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

/** Press the post-Draft "Send as email" chip after the pane was handed a drafted correspondence. */
function sendAsEmailFrom(draft: Record<string, unknown> | null): void {
  act(() => {
    if (draft) (chipDeps!.onCorrespondenceDraft as (r: Record<string, unknown>) => void)(draft);
    (chipDeps!.onLocalChipAction as (id: string) => void)("local:send-as-email");
  });
}

describe("ConversationPane - the Send Email dialog opens in rich text (task 197)", () => {
  beforeEach(() => {
    sendEmailDialogCalls.length = 0;
    chipDeps = null;
  });

  it("the blank dialog (Quick Start Send Email) is asked for rich text, not plain text", () => {
    renderPane();

    expect(newEmailDialogProps()).toBeDefined();
    expect(newEmailDialogProps()?.initialBodyFormat).toBe("HTML");
    expect(newEmailDialogProps()?.initialBody).toBeUndefined();
  });

  it("the Send as email seed is the draft's plain text converted to HTML: line breaks kept, < and & escaped once", () => {
    renderPane();

    sendAsEmailFrom({
      subject: "Re: invoice",
      body: "Hello,\n\nThe fee is < $5,000 & final.\nKind regards",
      recipients_suggestion: ["alex@acme.example"],
    });

    const props = newEmailDialogProps();
    expect(props?.open).toBe(true);
    expect(props?.initialBodyFormat).toBe("HTML");
    expect(props?.initialSubject).toBe("Re: invoice");
    expect(props?.initialBody).toBe(
      "<p>Hello,</p><p>The fee is &lt; $5,000 &amp; final.<br>Kind regards</p>"
    );
  });

  it("must-not-fire: a draft with no body seeds no body; no draft at all opens a blank composer", () => {
    renderPane();
    sendAsEmailFrom({ subject: "Subject only" });
    expect(newEmailDialogProps()?.initialBody).toBeUndefined();
    expect(newEmailDialogProps()?.initialBodyFormat).toBe("HTML");

    sendEmailDialogCalls.length = 0;
    renderPane();
    sendAsEmailFrom(null);
    expect(newEmailDialogProps()?.initialBody).toBeUndefined();
  });
});
