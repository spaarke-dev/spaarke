/**
 * QuickStartModal.test.tsx — task 041 (FR-F2) unit tests.
 *
 * Verifies:
 *   1. Renders the existing `GetStartedCardsWidget` card library when open;
 *      renders nothing when closed.
 *   2. Clicking "Create Matter" or "Create Project" launches via the shipped
 *      012 hand-off envelope (`launchSurface({ consumerType, ... })`) — both are
 *      `SURFACE_LAUNCH_REGISTRY` entries, and both read the session file context
 *      threaded in for UAT R4-12.
 *   3. Clicking a non-registry card (e.g. "Assign Work" / "Send Email") reuses
 *      the existing `@spaarke/ui-components` wizard launcher — NOT `launchSurface`
 *      (which would be a silent no-op for an unmapped consumertype).
 *   3b. UAT R4-12: when a `getFileContext` is supplied, the create-matter /
 *      create-project launch carries the session's `fileIds` / `source.sessionId`
 *      / `provenance.sourceFiles` into the envelope.
 *   4. Any card click closes the modal.
 *   5. Renders in light AND dark theme without crashing (ADR-021 semantic-
 *      token parity).
 *
 * @see QuickStartModal.tsx — component under test
 * @see AssistantToolMenu.tsx — the launcher ("Quick Start" menu entry)
 */

import "@testing-library/jest-dom";
import * as React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
} from "@fluentui/react-components";

// ---------------------------------------------------------------------------
// Mocks — the 012 hand-off envelope + the existing wizard launchers.
// ---------------------------------------------------------------------------

const mockLaunchSurface = jest.fn();
const mockLaunchCreateProjectWizard = jest.fn();
const mockLaunchAssignWorkWizard = jest.fn();
const mockLaunchSummarizeFilesWizard = jest.fn();
const mockLaunchFindSimilarWizard = jest.fn();
const mockLaunchPlaybookIntent = jest.fn();

jest.mock("@spaarke/ui-components", () => {
  const actual = jest.requireActual("@spaarke/ui-components");
  return {
    ...actual,
    launchSurface: (...args: unknown[]) => mockLaunchSurface(...args),
    launchCreateProjectWizard: (...args: unknown[]) => mockLaunchCreateProjectWizard(...args),
    launchAssignWorkWizard: (...args: unknown[]) => mockLaunchAssignWorkWizard(...args),
    launchSummarizeFilesWizard: (...args: unknown[]) => mockLaunchSummarizeFilesWizard(...args),
    launchFindSimilarWizard: (...args: unknown[]) => mockLaunchFindSimilarWizard(...args),
    launchPlaybookIntent: (...args: unknown[]) => mockLaunchPlaybookIntent(...args),
  };
});

jest.mock("../../../config/runtimeConfig", () => ({
  getBffBaseUrl: () => "https://test-bff.example.com",
}));

// Import AFTER the mocks.
import { QuickStartModal } from "../QuickStartModal";

// ---------------------------------------------------------------------------
// Helper
// ---------------------------------------------------------------------------

function renderModal(props?: {
  open?: boolean;
  onClose?: () => void;
  theme?: typeof webLightTheme;
  getFileContext?: () => { sessionId: string | null; fileIds: string[]; fileNames: string[] } | null;
  initialTab?: "create" | "analysis";
  onCreateAnalysis?: (workTypeValue: number, workTypeLabel: string) => void;
}): { onClose: jest.Mock; onCreateAnalysis: jest.Mock } {
  const theme = props?.theme ?? webLightTheme;
  const onClose = (props?.onClose as jest.Mock) ?? jest.fn();
  const onCreateAnalysis = (props?.onCreateAnalysis as jest.Mock) ?? jest.fn();
  render(
    <FluentProvider theme={theme}>
      <QuickStartModal
        open={props?.open ?? true}
        onClose={onClose}
        getFileContext={props?.getFileContext}
        initialTab={props?.initialTab}
        onCreateAnalysis={onCreateAnalysis}
      />
    </FluentProvider>,
  );
  return { onClose, onCreateAnalysis };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("QuickStartModal", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  // Rendering
  // -------------------------------------------------------------------------

  it("renders the GetStartedCardsWidget card library when open", () => {
    renderModal();

    expect(screen.getByTestId("quick-start-modal")).toBeInTheDocument();
    expect(screen.getByTestId("getstartedcards-widget")).toBeInTheDocument();
    expect(screen.getByText("Create Matter")).toBeInTheDocument();
    expect(screen.getByText("Create Project")).toBeInTheDocument();
    expect(screen.getByText("Assign Work")).toBeInTheDocument();
  });

  it("does not render its content when closed", () => {
    renderModal({ open: false });

    expect(screen.queryByTestId("quick-start-modal")).not.toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Card → launch wiring
  // -------------------------------------------------------------------------

  it('launches "Create Matter" via the shipped 012 hand-off envelope (launchSurface)', async () => {
    const onClose = jest.fn();
    renderModal({ onClose });
    const user = userEvent.setup();

    await user.click(screen.getByText("Create Matter"));

    expect(mockLaunchSurface).toHaveBeenCalledTimes(1);
    expect(mockLaunchSurface).toHaveBeenCalledWith({
      consumerType: "create-matter",
      bffBaseUrl: "https://test-bff.example.com",
    });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('launches "Create Project" via the 012 hand-off envelope (launchSurface, R4-12)', async () => {
    const onClose = jest.fn();
    renderModal({ onClose });
    const user = userEvent.setup();

    await user.click(screen.getByText("Create Project"));

    // R4-12: create-project is a SURFACE_LAUNCH_REGISTRY entry and its wizard reads the
    // handoff envelope, so it now routes through launchSurface (carrying session files when
    // present) rather than the file-less Path-B launchCreateProjectWizard.
    expect(mockLaunchSurface).toHaveBeenCalledTimes(1);
    expect(mockLaunchSurface).toHaveBeenCalledWith({
      consumerType: "create-project",
      bffBaseUrl: "https://test-bff.example.com",
    });
    expect(mockLaunchCreateProjectWizard).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("threads the session file context into create-matter / create-project (R4-12)", async () => {
    const onClose = jest.fn();
    const getFileContext = () => ({
      sessionId: "sess-123",
      fileIds: ["file-a", "file-b"],
      fileNames: ["a.pdf", "b.docx"],
    });
    renderModal({ onClose, getFileContext });
    const user = userEvent.setup();

    await user.click(screen.getByText("Create Matter"));

    expect(mockLaunchSurface).toHaveBeenCalledWith({
      consumerType: "create-matter",
      bffBaseUrl: "https://test-bff.example.com",
      fileIds: ["file-a", "file-b"],
      source: { sessionId: "sess-123" },
      provenance: { sourceFiles: ["a.pdf", "b.docx"] },
    });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('launches "Assign Work" via the hand-off envelope (launchSurface, create-work-assignment) and closes the modal', async () => {
    // R5-8: "Assign Work" is a SURFACE_LAUNCH_REGISTRY entry (create-work-assignment) and its
    // wizard reads the handoff envelope, so it routes through launchSurface (carrying session files
    // when present) rather than the file-less Path-B launchAssignWorkWizard.
    const onClose = jest.fn();
    renderModal({ onClose });
    const user = userEvent.setup();

    await user.click(screen.getByText("Assign Work"));

    expect(mockLaunchSurface).toHaveBeenCalledWith({
      consumerType: "create-work-assignment",
      bffBaseUrl: "https://test-bff.example.com",
    });
    expect(mockLaunchAssignWorkWizard).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('launches "Send Email" via launchPlaybookIntent with the email-compose intent', async () => {
    const onClose = jest.fn();
    renderModal({ onClose });
    const user = userEvent.setup();

    await user.click(screen.getByText("Send Email"));

    expect(mockLaunchPlaybookIntent).toHaveBeenCalledWith({
      bffBaseUrl: "https://test-bff.example.com",
      intent: "email-compose",
    });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  // -------------------------------------------------------------------------
  // Task 064 (E1b): onRecordCreated after a committed record-creation wizard
  // -------------------------------------------------------------------------

  it("E1b: fires onRecordCreated with the committed record's id + entity (create-matter)", async () => {
    mockLaunchSurface.mockResolvedValue({
      handoffId: "h-1",
      launched: true,
      result: { committed: true, recordId: "mtr-committed" },
    });
    const onRecordCreated = jest.fn();
    render(
      <FluentProvider theme={webLightTheme}>
        <QuickStartModal open onClose={jest.fn()} onRecordCreated={onRecordCreated} />
      </FluentProvider>,
    );
    const user = userEvent.setup();

    await user.click(screen.getByText("Create Matter"));

    await waitFor(() =>
      expect(onRecordCreated).toHaveBeenCalledWith({ id: "mtr-committed", entityType: "sprk_matter" }),
    );
  });

  it("E1b: does NOT fire onRecordCreated when the wizard was cancelled (not committed)", async () => {
    mockLaunchSurface.mockResolvedValue({
      handoffId: "h-2",
      launched: true,
      result: { committed: false, cancelled: true },
    });
    const onRecordCreated = jest.fn();
    render(
      <FluentProvider theme={webLightTheme}>
        <QuickStartModal open onClose={jest.fn()} onRecordCreated={onRecordCreated} />
      </FluentProvider>,
    );
    const user = userEvent.setup();

    await user.click(screen.getByText("Create Matter"));

    // Give the awaited outcome a tick to settle, then assert nothing fired.
    await waitFor(() => expect(mockLaunchSurface).toHaveBeenCalled());
    expect(onRecordCreated).not.toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------
  // Tabbed surface (ai-advanced-capabilities-analysis-hub-r1)
  // -------------------------------------------------------------------------

  it("defaults to the Create tab (GetStartedCardsWidget) and shows both tabs", () => {
    renderModal();

    expect(screen.getByTestId("quick-start-tablist")).toBeInTheDocument();
    expect(screen.getByTestId("quick-start-tab-create")).toBeInTheDocument();
    expect(screen.getByTestId("quick-start-tab-analysis")).toBeInTheDocument();
    // Create tab active by default.
    expect(screen.getByTestId("getstartedcards-widget")).toBeInTheDocument();
    expect(screen.queryByTestId("analysis-cards-widget")).not.toBeInTheDocument();
  });

  it("opens directly on the Analysis tab when initialTab='analysis'", () => {
    renderModal({ initialTab: "analysis" });

    expect(screen.getByTestId("analysis-cards-widget")).toBeInTheDocument();
    expect(screen.queryByTestId("getstartedcards-widget")).not.toBeInTheDocument();
  });

  it("switching to the Analysis tab reveals the analysis cards", async () => {
    renderModal();
    const user = userEvent.setup();

    await user.click(screen.getByTestId("quick-start-tab-analysis"));

    expect(screen.getByTestId("analysis-cards-widget")).toBeInTheDocument();
    expect(screen.getByTestId("analysis-card-nda-analysis")).toBeInTheDocument();
  });

  it("NDA Analysis card fires onCreateAnalysis (Agreement Review work type) and closes", async () => {
    const onClose = jest.fn();
    const onCreateAnalysis = jest.fn();
    renderModal({ initialTab: "analysis", onClose, onCreateAnalysis });
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: /NDA Analysis/i }));

    expect(onCreateAnalysis).toHaveBeenCalledTimes(1);
    expect(onCreateAnalysis).toHaveBeenCalledWith(100000000, "NDA Analysis");
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  // -------------------------------------------------------------------------
  // Dark-mode parity (ADR-021)
  // -------------------------------------------------------------------------

  it("renders in dark theme without crashing (ADR-021 token usage)", () => {
    renderModal({ theme: webDarkTheme });

    expect(screen.getByTestId("quick-start-modal")).toBeInTheDocument();
    expect(screen.getByTestId("getstartedcards-widget")).toBeInTheDocument();
  });

  it("renders in light theme as the baseline (ADR-021 parity check)", () => {
    renderModal({ theme: webLightTheme });

    expect(screen.getByTestId("quick-start-modal")).toBeInTheDocument();
  });
});
