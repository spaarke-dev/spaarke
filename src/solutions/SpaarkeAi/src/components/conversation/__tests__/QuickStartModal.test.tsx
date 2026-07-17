/**
 * QuickStartModal.test.tsx — task 041 (FR-F2) unit tests.
 *
 * Verifies:
 *   1. Renders the existing `GetStartedCardsWidget` card library when open;
 *      renders nothing when closed.
 *   2. Clicking "Create Matter" launches via the shipped 012 hand-off
 *      envelope (`launchSurface({ consumerType: 'create-matter', ... })`) —
 *      the ONE Get-Started card with a `SURFACE_LAUNCH_REGISTRY` entry.
 *   3. Clicking a non-registry card (e.g. "Create Project" / "Assign Work")
 *      reuses the existing `@spaarke/ui-components` wizard launcher — NOT
 *      `launchSurface` (which would be a silent no-op for an unmapped
 *      consumertype).
 *   4. Any card click closes the modal.
 *   5. Renders in light AND dark theme without crashing (ADR-021 semantic-
 *      token parity).
 *
 * @see QuickStartModal.tsx — component under test
 * @see AssistantToolMenu.tsx — the launcher ("Quick Start" menu entry)
 */

import "@testing-library/jest-dom";
import * as React from "react";
import { render, screen } from "@testing-library/react";
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
}): { onClose: jest.Mock } {
  const theme = props?.theme ?? webLightTheme;
  const onClose = (props?.onClose as jest.Mock) ?? jest.fn();
  render(
    <FluentProvider theme={theme}>
      <QuickStartModal open={props?.open ?? true} onClose={onClose} />
    </FluentProvider>,
  );
  return { onClose };
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

  it('launches "Create Project" via the existing wizard launcher, NOT launchSurface', async () => {
    const onClose = jest.fn();
    renderModal({ onClose });
    const user = userEvent.setup();

    await user.click(screen.getByText("Create Project"));

    expect(mockLaunchCreateProjectWizard).toHaveBeenCalledTimes(1);
    expect(mockLaunchCreateProjectWizard).toHaveBeenCalledWith({
      bffBaseUrl: "https://test-bff.example.com",
    });
    expect(mockLaunchSurface).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('launches "Assign Work" via launchAssignWorkWizard and closes the modal', async () => {
    const onClose = jest.fn();
    renderModal({ onClose });
    const user = userEvent.setup();

    await user.click(screen.getByText("Assign Work"));

    expect(mockLaunchAssignWorkWizard).toHaveBeenCalledWith({
      bffBaseUrl: "https://test-bff.example.com",
    });
    expect(mockLaunchSurface).not.toHaveBeenCalled();
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
