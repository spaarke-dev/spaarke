/**
 * ConversationPane — Send Email dialog gets the compose template picker + AI-draft handlers
 * (task 186, owner D-219 Q3, UAT F-R4-2 / F-R4-3).
 *
 * `EmailComposer` hides the template picker unless the host passes `onListEmailTemplates` AND
 * `onRenderEmailTemplate`, and hides the AI-draft sparkle unless it passes `onDraftWithAi`
 * (EmailComposer.types.ts). The pane's "Send Email" Quick Start card / "Send as email" chip dialog
 * rendered `<SendEmailDialog>` without them. This pins that the pane forwards the handlers built by
 * `createXrmEmailComposeHandlers({ authenticatedFetch, bffBaseUrl })` to the dialog.
 *
 * Mock pattern mirrors ConversationPane.new-session.test.tsx (SprkChat stub + useAiSession mock +
 * ThreePaneShell context mock); SendEmailDialog is a prop-capturing stub (the composer's own
 * hidden-without / shown-with contract is covered in Spaarke.UI.Components EmailComposer __tests__/templatePicker.test.tsx and aiSparkle.test.tsx).
 */

import * as React from "react";
import { render } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { PaneEventBus, PaneEventBusProvider } from "@spaarke/ai-widgets";

// ---------------------------------------------------------------------------
// Mock @spaarke/ui-components — capture SendEmailDialog props; fake the compose-handler factory.
// ---------------------------------------------------------------------------

// Every SendEmailDialog the pane renders. There are TWO: the raw "New" dialog (Quick Start card /
// "Send as email" chip — the unit under test) and the record-scoped Reply/Forward composer owned by
// `useEmailPerItemCards` -> `useEmailComposeActions` (already wired; it sets `defaultSendMode`).
const sendEmailDialogCalls: Array<Record<string, unknown>> = [];
function newEmailDialogProps(): Record<string, unknown> | undefined {
  const raw = sendEmailDialogCalls.filter(p => p.defaultSendMode === undefined);
  return raw[raw.length - 1];
}
const onListEmailTemplatesSentinel = jest.fn();
const onRenderEmailTemplateSentinel = jest.fn();
const onDraftWithAiSentinel = jest.fn();
const onUploadLocalAttachmentSentinel = jest.fn();
const createXrmEmailComposeHandlersMock = jest.fn(() => ({
  onListEmailTemplates: onListEmailTemplatesSentinel,
  onRenderEmailTemplate: onRenderEmailTemplateSentinel,
  onDraftWithAi: onDraftWithAiSentinel,
  onUploadLocalAttachment: onUploadLocalAttachmentSentinel,
}));

jest.mock("@spaarke/ui-components", () => {
  const actual = jest.requireActual("@spaarke/ui-components");
  return {
    ...actual,
    SprkChat: () => <div data-testid="sprkchat-stub" />,
    SendEmailDialog: (props: Record<string, unknown>) => {
      sendEmailDialogCalls.push(props);
      return <div data-testid="send-email-dialog-stub" />;
    },
    createXrmEmailComposeHandlers: (...args: unknown[]) =>
      (createXrmEmailComposeHandlersMock as unknown as (...a: unknown[]) => unknown)(...args),
  };
});

// ---------------------------------------------------------------------------
// Mock @spaarke/ai-widgets useAiSession.
// ---------------------------------------------------------------------------

const authenticatedFetchMock = jest.fn();
const BFF_BASE_URL = "https://test-bff.example.com";

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

describe("ConversationPane — Send Email dialog compose handlers (task 186, D-219 Q3)", () => {
  beforeEach(() => {
    sendEmailDialogCalls.length = 0;
    createXrmEmailComposeHandlersMock.mockClear();
  });

  it("passes the template-list, template-render and AI-draft handlers to SendEmailDialog", () => {
    renderPane();

    expect(newEmailDialogProps()).toBeDefined();
    expect(newEmailDialogProps()?.onListEmailTemplates).toBe(onListEmailTemplatesSentinel);
    expect(newEmailDialogProps()?.onRenderEmailTemplate).toBe(onRenderEmailTemplateSentinel);
    expect(newEmailDialogProps()?.onDraftWithAi).toBe(onDraftWithAiSentinel);
    expect(newEmailDialogProps()?.onUploadLocalAttachment).toBe(onUploadLocalAttachmentSentinel);
  });

  it("builds the handlers from the pane's authenticatedFetch and BFF base URL", () => {
    renderPane();

    expect(createXrmEmailComposeHandlersMock).toHaveBeenCalledWith({
      authenticatedFetch: authenticatedFetchMock,
      bffBaseUrl: BFF_BASE_URL,
    });
    // The dialog sends through the same fetch/BFF pair (attachments + send path unchanged).
    expect(newEmailDialogProps()?.authenticatedFetch).toBe(authenticatedFetchMock);
    expect(newEmailDialogProps()?.bffBaseUrl).toBe(BFF_BASE_URL);
  });
});
