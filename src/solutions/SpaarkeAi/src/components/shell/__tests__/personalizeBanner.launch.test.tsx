/**
 * personalizeBanner.launch.test.tsx — LegalWorkspace's "Personalize your workspace" banner opens the Workspace layout
 * wizard in-app when the Console's host is mounted, and otherwise keeps its original navigateTo dialog byte-for-byte
 * (including its bespoke 80% x 80% size) — spaarke-ontology-platform-r1 task 113; D-26.
 *
 * Classification (ADR-038 §7): MAINTAIN — a launch site of the migrated Workspace layout wizard.
 */
import "@testing-library/jest-dom";
import * as React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { registerInAppWizardHost } from "@spaarke/ui-components";

jest.mock("../../../../../LegalWorkspace/src/config/runtimeConfig", () => ({ getBffBaseUrl: () => "https://bff.example" }));

import { PersonalizeBanner } from "../../../../../LegalWorkspace/src/components/WorkspaceLoadingStates";

// Fluent's MessageBar observes its size; jsdom has no ResizeObserver.
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
(window as unknown as { ResizeObserver: unknown }).ResizeObserver = ResizeObserverStub;

let navigateTo: jest.Mock;
let unregister: (() => void) | null = null;

beforeEach(() => {
  sessionStorage.clear();
  navigateTo = jest.fn().mockResolvedValue(undefined);
  (window as unknown as { Xrm?: unknown }).Xrm = { Navigation: { navigateTo } };
});

afterEach(() => {
  unregister?.();
  unregister = null;
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

function mountBanner() {
  render(
    <FluentProvider theme={webLightTheme}>
      <PersonalizeBanner />
    </FluentProvider>
  );
}

describe("PersonalizeBanner — layout wizard launch", () => {
  it("opens in-app with the Console host registered; no navigateTo", async () => {
    const opener = jest.fn().mockResolvedValue(undefined);
    unregister = registerInAppWizardHost(opener, ["sprk_workspacelayoutwizard"]);
    mountBanner();

    fireEvent.click(screen.getByText("Open the Layout Wizard"));

    await waitFor(() => expect(opener).toHaveBeenCalledTimes(1));
    expect(opener).toHaveBeenCalledWith({
      webresourceName: "sprk_workspacelayoutwizard",
      data: `mode=create&bffBaseUrl=${encodeURIComponent("https://bff.example")}`,
    });
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it("with no host mounted, keeps the original 80% x 80% navigateTo dialog", async () => {
    mountBanner();

    fireEvent.click(screen.getByText("Open the Layout Wizard"));

    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    expect(navigateTo.mock.calls[0][0]).toEqual({
      pageType: "webresource",
      webresourceName: "sprk_workspacelayoutwizard",
      data: `mode=create&bffBaseUrl=${encodeURIComponent("https://bff.example")}`,
    });
    expect(navigateTo.mock.calls[0][1]).toEqual({
      target: 2,
      width: { value: 80, unit: "%" },
      height: { value: 80, unit: "%" },
      title: "Create New Workspace",
    });
  });
});
