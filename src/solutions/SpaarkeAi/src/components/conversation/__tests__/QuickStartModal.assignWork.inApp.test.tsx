/**
 * QuickStartModal.assignWork.inApp.test.tsx — #1420 (spaarke-ontology-platform-r1 task 113): Quick Start's
 * "Assign Work" card reports the created record.
 *
 * Before: the Create Work Assignment wizard had no `onComplete` seam, so even a successful create never wrote a
 * committed hand-off result; `launchSurface` read "cancelled" and Quick Start's `onRecordCreated` never fired for
 * Assign Work (it did for Create Matter / Create Project).
 *
 * This runs the REAL chain: QuickStartModal -> real `launchSurface` -> real `InAppWizardHost` -> real
 * `WorkAssignmentWizardDialog`. Only `WizardShell` (a driver that finishes the wizard on demand or cancels it) and
 * the work-assignment service (the create) are replaced.
 *
 * Classification (ADR-038 §7): MAINTAIN — the end-to-end honest-ack path for Assign Work.
 */
import "@testing-library/jest-dom";
import * as React from "react";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { InAppWizardHost } from "@spaarke/ui-components";

// The shell driver: exposes "finish" (runs the dialog's real onFinish, renders the real success actions) and
// "cancel" (the shell's close).
jest.mock("@spaarke/ui-components/components/Wizard/WizardShell", () => {
  const ReactActual = jest.requireActual("react");
  return {
    WizardShell: ReactActual.forwardRef((props: any, _ref: unknown) => {
      const [actions, setActions] = ReactActual.useState(null);
      return ReactActual.createElement(
        "div",
        { "data-testid": "wa-shell" },
        ReactActual.createElement(
          "button",
          {
            onClick: async () => {
              const config = await props.onFinish();
              setActions(config.actions);
            },
          },
          "drive-finish"
        ),
        ReactActual.createElement("button", { onClick: props.onClose }, "drive-cancel"),
        actions
      );
    }),
  };
});

const mockCreateWorkAssignment = jest.fn();
jest.mock("@spaarke/ui-components/components/CreateWorkAssignmentWizard/workAssignmentService", () => ({
  searchUsersAsLookup: jest.fn(),
  WorkAssignmentService: jest.fn().mockImplementation(() => ({
    createWorkAssignment: (...a: unknown[]) => mockCreateWorkAssignment(...a),
  })),
}));

jest.mock("../../../config/runtimeConfig", () => ({ getBffBaseUrl: () => "https://test-bff.example.com" }));

import { QuickStartModal } from "../QuickStartModal";

let navigateTo: jest.Mock;

beforeEach(() => {
  navigateTo = jest.fn().mockResolvedValue(undefined);
  (window as unknown as { Xrm?: unknown }).Xrm = { WebApi: {}, Navigation: { navigateTo } };
  sessionStorage.clear();
  mockCreateWorkAssignment.mockReset();
});

afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

function mount(onRecordCreated: jest.Mock) {
  render(
    <FluentProvider theme={webLightTheme}>
      <InAppWizardHost authenticatedFetch={jest.fn() as never} bffBaseUrl="https://test-bff.example.com" />
      <QuickStartModal open onClose={jest.fn()} onRecordCreated={onRecordCreated} />
    </FluentProvider>
  );
}

describe("Quick Start — Assign Work (#1420)", () => {
  it("finishing the in-app Create Work Assignment wizard reports the new record to onRecordCreated", async () => {
    mockCreateWorkAssignment.mockResolvedValue({ status: "ok", workAssignmentId: "wa-77", warnings: [] });
    const onRecordCreated = jest.fn();
    mount(onRecordCreated);
    const user = userEvent.setup();

    await user.click(screen.getByText("Assign Work"));
    await user.click(await screen.findByText("drive-finish"));
    await user.click(await screen.findByText("Close")); // the success screen's Close

    await waitFor(() =>
      expect(onRecordCreated).toHaveBeenCalledWith({ id: "wa-77", entityType: "sprk_workassignment" })
    );
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it("cancelling the wizard reports nothing", async () => {
    const onRecordCreated = jest.fn();
    mount(onRecordCreated);
    const user = userEvent.setup();

    await user.click(screen.getByText("Assign Work"));
    await user.click(await screen.findByText("drive-cancel"));

    await waitFor(() => expect(screen.queryByTestId("wa-shell")).toBeNull());
    await act(async () => {
      await Promise.resolve();
    });
    expect(onRecordCreated).not.toHaveBeenCalled();
    expect(navigateTo).not.toHaveBeenCalled();
  });
});
